param([int]$Port = 5099)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo "artifacts/publish"
$dll = Join-Path $project "Explore.Web.dll"
if (!(Test-Path $dll)) { throw "Run dotnet publish src/Explore.Web -c Release -o artifacts/publish first." }
$temp = Join-Path ([IO.Path]::GetTempPath()) ("explore-test-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory $temp | Out-Null
$base = "http://127.0.0.1:$Port"
$key = [guid]::NewGuid().ToString("N") + [guid]::NewGuid().ToString("N")
$old = @{}
$settings = @{
    ASPNETCORE_URLS=$base; Api__BaseUrl="$base/"; ASPNETCORE_ENVIRONMENT="Production"
    ConnectionStrings__Explore="Data Source=$temp/test.db"; Admin__ApiKey=$key
    Database__Provider="Sqlite"; Database__SeedDemo="true"; Cors__Origins__0="https://allowed.example"
}
foreach ($name in $settings.Keys) { $old[$name]=[Environment]::GetEnvironmentVariable($name); [Environment]::SetEnvironmentVariable($name,$settings[$name]) }
function Start-TestServer {
    $options = @{FilePath="dotnet"; ArgumentList=@($dll); WorkingDirectory=$project; PassThru=$true; RedirectStandardOutput="$temp/out.log"; RedirectStandardError="$temp/err.log"}
    if ($IsWindows) { $options.WindowStyle="Hidden" }
    $proc = Start-Process @options
    for ($i=0; $i -lt 60; $i++) {
        if ($proc.HasExited) { throw "Server exited: $(Get-Content $temp/err.log -Raw)" }
        try { $result=Invoke-RestMethod "$base/health"; if($result.status -eq "healthy"){ return $proc } } catch {}
        Start-Sleep -Milliseconds 250
    }
    Stop-Process -Id $proc.Id -ErrorAction SilentlyContinue
    throw "Server startup timed out."
}
function Assert($ok, $message) { if (!$ok) { throw $message }; Write-Host "PASS $message" }
try {
    $server = Start-TestServer
    $places = Invoke-RestMethod "$base/api/v1/places"
    Assert ($places.Count -eq 6) "Seed contains 6 places"
    $query = [uri]::EscapeDataString("時光")
    $found = Invoke-RestMethod "$base/api/v1/places?q=$query"
    Assert ($found.Count -eq 1 -and $found[0].id -eq 1) "Search filters places"
    $category = [uri]::EscapeDataString("自然風景")
    Assert ((Invoke-RestMethod "$base/api/v1/places?category=$category").Count -eq 3) "Category filters places"
    Assert ((Invoke-WebRequest "$base/api/v1/places/999" -SkipHttpErrorCheck).StatusCode -eq 404) "Missing place returns 404"
    Assert ((Invoke-RestMethod "$base/api/v1/trails").Count -eq 2) "Shared API returns routes"
    $html=[System.Net.WebUtility]::HtmlDecode((Invoke-WebRequest $base).Content)
    Assert ($html.Contains("地方誌") -and $html.Contains("時光街屋")) "Home renders API data"
    Assert ([System.Net.WebUtility]::HtmlDecode((Invoke-WebRequest "$base/places/1").Content).Contains("時光街屋")) "Detail page renders"
    Assert ([System.Net.WebUtility]::HtmlDecode((Invoke-WebRequest "$base/trails").Content).Contains("老城慢行")) "Routes page renders"
    Assert ((Invoke-WebRequest "$base/about").StatusCode -eq 200) "About page responds"
    foreach($match in [regex]::Matches($html,'(?:href|src)="([^"]+\.(?:css|js))"')) {
        $asset=$match.Groups[1].Value
        Assert ((Invoke-WebRequest "$base/$asset").StatusCode -eq 200) "Published asset responds: $asset"
    }
    Assert ((Invoke-WebRequest "$base/openapi/v1.json").StatusCode -eq 200) "OpenAPI is available"
    $body=$places[0] | ConvertTo-Json
    $unauthorized=Invoke-WebRequest "$base/api/v1/admin/places" -Method Post -ContentType "application/json" -Body $body -SkipHttpErrorCheck
    Assert ($unauthorized.StatusCode -eq 401) "Unauthenticated writes are rejected"
    $headers=@{"X-Admin-Key"=$key}
    $bad=Invoke-WebRequest "$base/api/v1/admin/places" -Method Post -Headers $headers -ContentType "application/json" -Body '{}' -SkipHttpErrorCheck
    Assert ($bad.StatusCode -eq 400) "Invalid payload is rejected"
    $created=Invoke-WebRequest "$base/api/v1/admin/places" -Method Post -Headers $headers -ContentType "application/json" -Body $body
    Assert ($created.StatusCode -eq 201) "Authorized POST persists data"
    $record=$created.Content | ConvertFrom-Json
    $record.name="Updated test place"
    $updated=Invoke-RestMethod "$base/api/v1/admin/places/$($record.id)" -Method Put -Headers $headers -ContentType "application/json" -Body ($record | ConvertTo-Json)
    Assert ($updated.name -eq "Updated test place") "Authorized PUT updates data"
    $cors=Invoke-WebRequest "$base/api/v1/places" -Headers @{Origin="https://allowed.example"}
    Assert ($cors.Headers["Access-Control-Allow-Origin"] -contains "https://allowed.example") "Configured CORS origin works"
    $denied=Invoke-WebRequest "$base/api/v1/places" -Headers @{Origin="https://unlisted.example"}
    Assert (!$denied.Headers.ContainsKey("Access-Control-Allow-Origin")) "Unlisted CORS origin is not allowed"
    Stop-Process -Id $server.Id
    $server.WaitForExit()
    $server=Start-TestServer
    $persisted=Invoke-RestMethod "$base/api/v1/places/$($record.id)"
    Assert ($persisted.name -eq "Updated test place") "Database survives process restart"
} finally {
    if ($server -and !$server.HasExited) { Stop-Process -Id $server.Id; $server.WaitForExit() }
    foreach ($name in $old.Keys) { [Environment]::SetEnvironmentVariable($name,$old[$name]) }
    # Retain only disposable test files outside the repository; never delete project data.
    Write-Host "Test logs: $temp"
}
