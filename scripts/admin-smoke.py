"""Integration checks against disposable SQLite databases; never touches the user's database.
Usage: python scripts/admin-smoke.py [published-directory]
"""
import base64
import hashlib
import html
import http.cookiejar
import json
import os
from pathlib import Path
import secrets
import sqlite3
import struct
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
from html.parser import HTMLParser

ROOT = Path(__file__).resolve().parents[1]
PUBLISH = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else ROOT / "artifacts/admin-publish"
TEMP = Path(tempfile.mkdtemp(prefix="explore-admin-test-"))
BASE = "http://127.0.0.1:5101"
PASSWORD = secrets.token_urlsafe(24)
KEY = secrets.token_hex(32)
salt = secrets.token_bytes(16)
password_hash = base64.b64encode(
    b"\x01" + struct.pack(">III", 2, 100000, len(salt)) + salt +
    hashlib.pbkdf2_hmac("sha512", PASSWORD.encode(), salt, 100000, 32)
).decode()
env = dict(os.environ, ASPNETCORE_ENVIRONMENT="Development", ASPNETCORE_URLS=BASE,
           Api__BaseUrl=BASE + "/", Database__Provider="Sqlite", Database__SeedDemo="true",
           Admin__Username="test-admin", Admin__PasswordHash=password_hash, Admin__ApiKey=KEY)
process = None
log = open(TEMP / "server.log", "a", encoding="utf-8")
jar = http.cookiejar.CookieJar()
browser = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
anonymous = urllib.request.build_opener()

def request(path, data=None, method=None, authenticated=True, headers=None, form=False):
    headers = dict(headers or {})
    if data is not None:
        payload = urllib.parse.urlencode(data).encode() if form else json.dumps(data).encode()
        headers["Content-Type"] = "application/x-www-form-urlencoded" if form else "application/json"
    else:
        payload = None
    req = urllib.request.Request(BASE + path, payload, headers, method=method)
    try:
        with (browser if authenticated else anonymous).open(req, timeout=20) as response:
            return response.status, response.read().decode(), response.headers
    except urllib.error.HTTPError as error:
        return error.code, error.read().decode(), error.headers

def check(condition, label):
    if not condition:
        raise AssertionError(label)
    print("PASS", label, flush=True)

def data(path, authenticated=True):
    status, body, _ = request(path, authenticated=authenticated)
    check(status == 200, "GET " + path)
    return json.loads(body)

class Tokens(HTMLParser):
    token = ""
    def handle_starttag(self, tag, attrs):
        values = dict(attrs)
        if tag == "input" and values.get("name") == "__RequestVerificationToken":
            self.token = values.get("value", "")

def token(path):
    status, body, _ = request(path)
    check(status == 200, "Form available: " + path)
    parser = Tokens()
    parser.feed(body)
    check(bool(parser.token), "Form has CSRF token")
    return parser.token

def start(db):
    global process
    env["ConnectionStrings__Explore"] = "Data Source=" + str(db)
    flags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
    process = subprocess.Popen(["dotnet", str(PUBLISH / "Explore.Web.dll")],
                               cwd=PUBLISH, env=env, stdout=log, stderr=log, creationflags=flags)
    for _ in range(80):
        if process.poll() is not None:
            raise RuntimeError("Server exited; see " + str(TEMP / "server.log"))
        try:
            if request("/health", authenticated=False)[0] == 200:
                return
        except (OSError, urllib.error.URLError):
            pass
        time.sleep(.25)
    raise RuntimeError("Server startup timeout")

def stop():
    global process
    if process and process.poll() is None:
        process.terminate()
        process.wait(timeout=15)
    process = None

try:
    start(TEMP / "new.db")
    check(len(data("/api/v1/places", False)) == 6, "Seed data stays published")
    check(request("/api/v1/admin/places", authenticated=False)[0] == 401, "Admin API rejects anonymous access")
    check("登入管理室" in html.unescape(request("/admin", authenticated=False)[1]), "Admin page redirects to login")
    check(request("/account/login", {"Username": "test-admin", "Password": PASSWORD}, form=True)[0] == 400,
          "Login rejects missing CSRF token")
    login_token = token("/account/login")
    bad = request("/account/login", {"Username": "test-admin", "Password": "wrong",
                                    "__RequestVerificationToken": login_token}, form=True)
    check("帳號或密碼不正確" in html.unescape(bad[1]), "Wrong password rejected")
    result = request("/account/login", {"Username": "test-admin", "Password": PASSWORD,
                                       "__RequestVerificationToken": token("/account/login")}, form=True)
    check(result[0] == 200 and "新增景點" in html.unescape(result[1]), "Login reaches management page")
    check(any(c.name == "Explore.Admin" for c in jar), "Authentication cookie issued")
    places = data("/api/v1/admin/places")
    record = dict(places[0], id=0, name="Smoke draft", isPublished=False, imageUrl="")
    check(request("/api/v1/admin/places", record)[0] == 400, "Cookie API write rejects missing CSRF token")
    form = {
        "Input.Name": "Smoke draft", "Input.Category": "文化走讀", "Input.Summary": "Draft summary",
        "Input.Description": "Draft description", "Input.Address": "Test address",
        "Input.Latitude": "25.03", "Input.Longitude": "121.56", "Input.VisitMinutes": "30",
        "Input.Theme": "oldtown", "Input.ImageUrl": "", "Input.IsPublished": "false",
        "__RequestVerificationToken": token("/admin/places/edit")
    }
    saved = request("/admin/places/edit", form, form=True)
    check(saved[0] == 200 and "景點已儲存" in html.unescape(saved[1]), "HTML form creates draft via shared API")
    record = next(p for p in data("/api/v1/admin/places") if p["name"] == "Smoke draft")
    place_id = record["id"]
    check(request(f"/api/v1/places/{place_id}", authenticated=False)[0] == 404, "Draft detail is private")
    check(not any(p["id"] == place_id for p in data("/api/v1/places", False)), "Draft absent from public list")
    check("Smoke draft" not in html.unescape(request("/", authenticated=False)[1]), "Draft absent from homepage")
    form["Input.ImageUrl"] = "javascript:alert(1)"
    form["__RequestVerificationToken"] = token(f"/admin/places/edit/{place_id}")
    invalid = request(f"/admin/places/edit/{place_id}", form, form=True)
    check("HTTPS" in html.unescape(invalid[1]), "Unsafe image URL rejected")
    check(data(f"/api/v1/admin/places/{place_id}")["imageUrl"] == "", "Invalid update does not change database")
    form["Input.ImageUrl"] = "https://example.com/photo.jpg"
    form["Input.IsPublished"] = "true"
    form["__RequestVerificationToken"] = token(f"/admin/places/edit/{place_id}")
    published = request(f"/admin/places/edit/{place_id}", form, form=True)
    check("景點已儲存" in html.unescape(published[1]), "HTML form publishes place")
    check(data(f"/api/v1/places/{place_id}", False)["imageUrl"] == form["Input.ImageUrl"], "Published photo URL returned by API")
    check("Smoke draft" in html.unescape(request("/", authenticated=False)[1]), "Published content appears on website")
    check(form["Input.ImageUrl"] in request(f"/places/{place_id}", authenticated=False)[1], "Detail renders photo")
    lead = next(p for p in data("/api/v1/admin/places") if p["id"] == 1)
    lead["isPublished"] = False
    csrf = token("/admin")
    check(request("/api/v1/admin/places/1", lead, method="PUT", headers={"X-CSRF-TOKEN": csrf})[0] == 200,
          "Authenticated cookie API can unpublish")
    check("時光街屋" not in html.unescape(request("/", authenticated=False)[1]), "Unpublished featured item removed")
    check(all(1 not in t["placeIds"] for t in data("/api/v1/trails", False)), "Routes cannot expose unpublished stops")
    stop()
    start(TEMP / "new.db")
    check(data(f"/api/v1/places/{place_id}", False)["name"] == "Smoke draft", "Published content survives restart")
    check(request("/api/v1/places/1", authenticated=False)[0] == 404, "Draft status survives restart")
    csrf = token("/admin")
    request("/account/logout", {"__RequestVerificationToken": csrf}, form=True)
    check(request("/api/v1/admin/places")[0] == 401, "Logout revokes browser access")
    stop()
    legacy = TEMP / "legacy.db"
    with sqlite3.connect(legacy) as conn:
        conn.execute("""CREATE TABLE Places (
            Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL,
            Category TEXT NOT NULL, Summary TEXT NOT NULL, Description TEXT NOT NULL,
            Address TEXT NOT NULL, Latitude REAL NOT NULL, Longitude REAL NOT NULL,
            VisitMinutes INTEGER NOT NULL, Theme TEXT NOT NULL)""")
        conn.execute("INSERT INTO Places VALUES (1,'Legacy place','culture','summary','description','address',25,121,30,'forest')")
    jar.clear()
    start(legacy)
    old = data("/api/v1/places", False)
    check(len(old) == 1 and old[0]["name"] == "Legacy place" and old[0]["isPublished"],
          "Legacy schema upgraded without changing existing content")
    check(len(list(TEMP.glob("legacy.db.before-content-management-*.db"))) == 1, "Pre-upgrade backup created")
    stop()
    start(legacy)
    check(len(data("/api/v1/places", False)) == 1, "Schema upgrade is idempotent")
    print("ALL ADMIN CHECKS PASSED", flush=True)
finally:
    stop()
    log.close()
    print("Disposable test files:", TEMP)
