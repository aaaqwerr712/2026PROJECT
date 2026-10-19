# 地方誌 — 網站與共用 API 雛形

HTML / CSS 編輯式網站，以 Blazor Web App (.NET 10) 提供互動。網站與未來的 .NET MAUI App 都呼叫同一個 ASP.NET Core REST API，手機不直接存取資料庫。

## 快速啟動

安裝 .NET 10 SDK（建議最新安全更新），在專案根目錄執行：

```powershell
dotnet restore Explore.slnx
dotnet run --project src/Explore.Web
```

開啟 http://localhost:5080 。第一次啟動會用 EF Core 建立 SQLite 資料庫並加入 6 個虛構景點。不要把範例座標當作真實導航資料。

- 網站：景點搜尋、分類、詳細頁、兩條主題路線、關於計畫。
- API：公開端只讀已發布的景點與完整路線；管理端可查詢草稿、新增、修改及發布景點。
- 管理室：管理員登入、景點列表與編輯表單、照片網址、草稿／發布狀態。
- 共用：Explore.Shared 內含 DTO 與 ExploreApiClient，可供 MAUI 專案參考。
- 資料庫：預設 SQLite，設定可切換 SQL Server / Azure SQL。
- 部署：Dockerfile、持久磁碟 compose、GitHub Actions 建置與 smoke tests。

## 架構

```text
Blazor 網站 ── HTTP ─┐
                    ├─ /api/v1 → EF Core → SQLite / SQL Server
未來 MAUI App ─ HTTPS┘
```

網站與 API 先放在同一個服務，減少雲端部署項目。Blazor Server 透過 ExploreApiClient 呼叫 HTTP API，沒有直接讀取資料庫。App 的 API Base URL 使用公開 HTTPS 網域；容器內網站使用 localhost:8080。這些畫面是 HTML + Razor + CSS，不需另建 Node.js 前端。

## API

| 方法 | 路徑 | 用途 |
| --- | --- | --- |
| GET | /health | 資料庫連線健康檢查 |
| GET | /api/v1/places?q=關鍵字&category=分類 | 篩選景點（上限 100 筆） |
| GET | /api/v1/places/{id} | 景點詳情；不存在回傳 404 |
| GET | /api/v1/trails | 主題路線與景點 ID 順序 |
| GET | /api/v1/admin/places | 管理列表，包含草稿，需要管理員驗證 |
| GET | /api/v1/admin/places/{id} | 管理詳情，包含草稿，需要管理員驗證 |
| POST | /api/v1/admin/places | 新增景點，需要管理員驗證；預設草稿 |
| PUT | /api/v1/admin/places/{id} | 更新景點與發布狀態，需要管理員驗證 |
| GET | /openapi/v1.json | OpenAPI 規格，可匯入 Postman |

管理室位於 /admin。本機 Development 環境首次啟動會產生隨機管理員密碼，存於 src/Explore.Web/App_Data/admin-login.txt；驗證使用 admin-account.json 內的雜湊。這些檔案不會加入 Git。登入使用 HttpOnly Cookie，表單與 Cookie API 寫入均驗證 CSRF token。

正式環境不會自動建立帳號，需設定 Admin__Username 與 Admin__PasswordHash。伺服器對伺服器管理也可設定 Admin__ApiKey（至少 32 字元），並使用 X-Admin-Key；不可嵌入 App 或網頁。詳細設定見 docs/admin.md。

未來 App 註冊範例：

```csharp
builder.Services.AddSingleton(new ExploreApiClient(new HttpClient {
    BaseAddress = new Uri("https://YOUR-SERVICE/")
}));
```

請在 MAUI 專案加入 Explore.Shared 專案參考；不需 SQL 連線字串。原生 HttpClient 不受瀏覽器 CORS 限制。若使用獨立網頁前端，透過 Cors__Origins__0 設定精確來源網址。

## 測試與發布

```powershell
dotnet build Explore.slnx -c Release
dotnet publish src/Explore.Web -c Release -o artifacts/publish
pwsh ./scripts/smoke.ps1
python scripts/admin-smoke.py artifacts/publish
docker compose up --build -d
```

Docker 開啟 http://localhost:8080 。資料寫入 explore-data named volume；不要使用 docker compose down -v，除非要清空資料。雲端步驟見 [部署說明](docs/deployment.md)。

## 第一版邊界

這是可執行、可部署的雛形，並非完整會員系統。會員登入、個人收藏、QR 集章、定位、照片上傳與 MAUI App 尚未實作。路線目前為後端靜態示範定義，景點存入資料庫。初始資料庫使用 EnsureCreated；此次版本會自動補上 ImageUrl 與 IsPublished 欄位，升級 SQLite 前會建立備份並保留現有資料。後續正式資料結構演進仍需建立 EF migrations 基線。

視覺參考 https://www.twreporter.org/ 的編輯式層級與留白；未使用其商標、文章或照片。圖片以自製 CSS 插畫示意。正式場域確定後再換成授權照片與經查證內容。
