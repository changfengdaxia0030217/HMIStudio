# HMIStudio 本地运行时服务

需要安装 .NET 10 SDK 和 Node.js 20+。

## 方式一：构建前端并由 ASP.NET Core 提供静态页面

在仓库根目录执行：

```powershell
cd src/HMI.Web
npm install
npm run build
cd ../..
dotnet run --project src/HMI.Server
```

浏览器打开 `http://127.0.0.1:5080`。首次启动会在 `src/HMI.Server/data/demo.hmijob` 创建演示工程。加载其他工程可通过 `HMISTUDIO_PROJECT` 环境变量指定完整路径。

## 方式二：前后端分别调试

终端一启动后端：

```powershell
dotnet run --project src/HMI.Server
```

终端二启动 Vite：

```powershell
cd src/HMI.Web
npm install
npm run dev
```

打开 `http://127.0.0.1:5173`。开发前端默认访问 `http://127.0.0.1:5080` API；可用 `VITE_API_BASE` 覆盖。

## 当前 API

- `GET /api/health`：健康检查与运行时长
- `GET /api/project`：工程摘要
- `GET /api/screens`：画面列表
- `GET /api/screens/{screenId}`：画面模型
- `GET /api/variables`：变量元数据、当前值和质量位
- `GET /api/variables/{varId}`：单个变量
- `POST /api/variables/{varId}/write`：提交变量写入，返回明确写入回执
- `GET /api/runtime/stream?varIds=line.speed,line.alarm`：Server-Sent Events 实时值流
- `POST /api/runtime/mock-values`：向模拟驱动注入外部值，用于调试与演示

写入请求示例：

```json
{ "value": 60, "requestId": "demo-write-001" }
```

模拟值请求示例：

```json
{ "protocol": "MOCK", "address": "line.speed", "value": 61.5 }
```

## 安全与边界

- 默认只绑定 loopback（本机），不向局域网公开。
- 首版仅使用模拟驱动，不建立真实 PLC 连接。
- 当前尚未实现用户身份认证、角色权限和细粒度写入授权，不应直接暴露到不可信网络。
- 当前前端为运行监控预览；完整组态编辑器、画布操作和属性绑定求值仍在后续里程碑实现。
