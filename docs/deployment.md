# 雲端部署

目前提供部署成品與設定，不代表已建立雲端資源。GitHub 的 joe 分支推送也不會自動產生公開網站網址。

## Docker 本機驗證

1. 安裝 Docker，於根目錄執行 docker compose up --build -d。
2. 開啟 http://localhost:8080，檢查 /health 與 /openapi/v1.json。
3. API 及網站共用容器的 8080 port，資料位於 /app/App_Data。
4. 容器使用非 root 的 app 使用者；掛載磁碟必須讓此使用者可寫入。
5. 正式入口由雲端 HTTPS / TLS proxy 提供，請強制 HTTPS；不直接把 HTTP 管理端點暴露到公網。

## Azure App Service（容器）

1. 將 Docker image 建置並推到你的 Azure Container Registry：
   az acr build --registry YOUR_REGISTRY --image local-journal:v1 .
2. 在 Azure Portal 建立 Linux Container Web App，選擇該 image，設定 registry 存取權限。
3. 容器 target port 設為 8080（傳統 custom container 模式設定 WEBSITES_PORT=8080）。
4. 啟用 HTTPS Only、WebSockets、Always On（依方案支援），初期維持單一 instance。Blazor Server 的互動需要 WebSocket 長連線。
5. 應用程式設定如下；機密值使用 Azure secret / Key Vault，不提交 Git：
   - Api__BaseUrl = http://localhost:8080/
   - Database__Provider = SqlServer
   - ConnectionStrings__Explore = Server=tcp:YOUR_SERVER.database.windows.net,1433;Initial Catalog=YOUR_DB;User ID=YOUR_USER;Password=YOUR_SECRET;Encrypt=True;TrustServerCertificate=False;
   - Admin__ApiKey = 至少 32 字元的隨機金鑰（不需要寫入 API 可不設定）
   - Database__SeedDemo = true（展示用；正式場域資料改為 false）
6. 建立 Azure SQL 空資料庫，設定網路存取權限及適當 DB 帳號。首次初始化需要建表權限。
7. 健康檢查路徑設 /health，確認 HTTPS 公開網址可取得景點與頁面。
8. 手機 App 設定 BaseAddress 為同一個 https://YOUR_APP.azurewebsites.net/。

其他支援長連線的 Docker 雲端亦可部署，監聽 port 固定為 8080，設定以上環境變數即可。

## SQLite 雲端展示

無 SQL Server 時可用 SQLite，但必須掛載持久磁碟至 /app/App_Data、只跑一個 replica。ConnectionStrings__Explore 設為 Data Source=/app/App_Data/explore.db。雲端無持久磁碟會導致重建容器後資料消失。不要將單一 SQLite 檔案用於多個 instance。

## 後續正式化

EnsureCreated 只用於新資料庫建表，不會升級既有 schema；修改資料表前先改用 migrations 並備份。管理 API 金鑰僅供可信管理端，會員與集章功能應另接正式的身份認證。跨來源網站需要精確的 Cors__Origins__0 設定。此版本只有公用導覽資料，沒有會員與登入 Cookie；加入登入後另需持久保存 Data Protection keys。

官方參考：
- https://learn.microsoft.com/en-us/azure/app-service/configure-custom-container
- https://learn.microsoft.com/en-us/aspnet/core/blazor/call-web-api?view=aspnetcore-10.0
