# 管理室操作與本機資料

## 登入

執行 dotnet run --project src/Explore.Web 後，開啟 http://localhost:5080/admin。
本機 Development 環境首次啟動會建立唯一的隨機密碼：

- 帳號及密碼：src/Explore.Web/App_Data/admin-login.txt
- 密碼雜湊：src/Explore.Web/App_Data/admin-account.json
- 資料庫：src/Explore.Web/App_Data/explore.db
- Cookie 保護金鑰：src/Explore.Web/App_Data/keys/

App_Data 已列入 .gitignore 與 .dockerignore。不要將上述檔案推送 GitHub。
admin-login.txt 只是本機登入提示，不是驗證來源；刪除此提示不會更改密碼。
帳號採單一管理員，尚未提供自助重設密碼或多管理員管理頁。

## 編輯流程

1. 登入後選「新增景點」。
2. 填寫名稱、分類、簡介、詳細介紹、地址、座標與停留時間。
3. 可填入有授權的 HTTPS 照片網址。留白顯示插畫；尚未提供上傳檔案。
4. 預設為草稿，儲存後只在管理室顯示。
5. 勾選「發布到網站」並儲存，前台、搜尋及公開 API 才能取得資料。
6. 取消勾選再儲存，可將景點改回草稿。包含草稿站點的示範路線會暫時從公開 API 移除。
7. 用「查看前台」核對成果；完成後登出。

所有管理畫面都透過同一個 /api/v1/admin API 存取資料，沒有把服務金鑰嵌入網頁。
登入限制每來源 IP 每分鐘最多 10 個登入頁請求；遇到 429 請稍後再試。
登入效期 8 小時，不自動延長。更新密碼雜湊並重啟服務後，舊登入憑證會失效。

## 資料庫升級

首次啟動新版時，SQLite 會先檢查欄位：
- 舊版資料表缺少 ImageUrl 或 IsPublished 時，建立線上備份 explore.db.before-content-management-時間.db。
- 在交易中新增缺少欄位，既有景點保留原始資料且維持已發布狀態。
- 新增景點的預設發布狀態為草稿。
- 重複啟動不會重建資料庫或重複加入示範資料。

SQL Server 也有欄位存在檢查，但尚未在實際 SQL Server 環境驗證；正式上雲前需要另行驗證、備份並建立 migration 基線。

## 正式部署

Production 不會自動載入本機開發帳號。請透過部署環境的機密設定提供：
- Admin__Username
- Admin__PasswordHash：ASP.NET Core Identity PasswordHasher 產生的密碼雜湊。

若希望沿用本機管理員，可由管理者將 admin-account.json 中的 Username 與 PasswordHash
分別放入雲端機密設定（勿將整個明文提示檔案上傳）。正式環境 Cookie 限 HTTPS；
反向代理需正確轉送到同一服務，並保留 App_Data/keys。請勿把環境設成 Development 來上雲。

管理 API 也保留 X-Admin-Key 給可信伺服器工具；Cookie 寫入必須附 X-CSRF-TOKEN。
登入、登出及所有 HTML 管理表單使用框架防偽造請求驗證。草稿 API 回應不允許快取。

## 驗證

先執行 dotnet publish src/Explore.Web -c Release -o artifacts/publish，
再執行 python scripts/admin-smoke.py artifacts/publish。
測試會建立獨立臨時資料庫，不修改本機實際景點。
