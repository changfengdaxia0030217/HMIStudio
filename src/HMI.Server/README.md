# HMIStudio 本地运行时服务

## 启动

需要安装 .NET 10 SDK。在仓库根目录运行：

```powershell
dotnet run --project src/HMI.Server
```

默认只监听本机地址 `http://127.0.0.1:5080`。首次启动会在 `src/HMI.Server/data/demo.hmijob` 创建演示工程；如需加载其他工程，可通过 `HMISTUDIO_PROJECT` 环境变量指定完整路径。

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
- 仅用于首版模拟驱动；此服务不建立真实 PLC 连接。
- 当前尚未实现用户身份认证、角色权限和细粒度写入授权，不应直接暴露到不可信网络。
- 浏览器编辑器和运行画面将在后续里程碑接入。
