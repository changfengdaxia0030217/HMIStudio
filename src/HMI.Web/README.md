# HMIStudio Web

React + TypeScript + Vite 的本地运行监控前端。

- `npm install` 安装依赖
- `npm run dev` 启动本地开发服务器（127.0.0.1:5173）
- `npm run build` 类型检查并构建到 `../HMI.Server/wwwroot`

开发服务器默认访问 `http://127.0.0.1:5080` 的 ASP.NET Core API，可通过 `VITE_API_BASE` 覆盖。
