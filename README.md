# HMIStudio

面向 Windows x64 单机部署的 HMI/组态软件首版。当前技术路线为 C# + ASP.NET Core（.NET 10）+ React/TypeScript，首版只包含模拟 PLC 驱动，不连接真实设备。

## 当前进度

- 核心变量池、变量元数据、写入回执与质量位
- JSON 工程格式版本校验
- 变量订阅生命周期与按周期采集
- 模拟 PLC 读写、批量刷新与模拟信号注入
- 本地 ASP.NET Core 运行时 API 和 SSE 实时变量流
- React/TypeScript 运行监控预览
- Windows CI：前端类型检查/构建、.NET restore/build/test、运行时 API smoke test

## 快速启动

需要安装 .NET 10 SDK 和 Node.js 20+。在仓库根目录运行：

```powershell
cd src/HMI.Web
npm install
npm run build
cd ../..
dotnet run --project src/HMI.Server
```

浏览器打开：

`http://127.0.0.1:5080`

首次启动会自动创建本地演示工程。默认只绑定本机 loopback，不连接真实 PLC。

前端单独开发：

```powershell
cd src/HMI.Web
npm install
npm run dev
```

打开 `http://127.0.0.1:5173`，后端仍需在 5080 端口运行。

## 主要目录

- `src/HMI.Core.Interface`：核心接口和 DTO
- `src/HMI.Core.Model`：工程、画面、变量元数据与属性值源模型
- `src/HMI.Core.Services`：变量池、订阅、工程存储、模拟 PLC
- `src/HMI.Server`：本地 ASP.NET Core 服务
- `src/HMI.Web`：React/TypeScript 前端
- `src/HMI.Core.Tests`：核心自动化测试

## 当前边界

当前前端是运行监控预览，不是完整组态编辑器。拖拽画布、控件属性面板、属性值源求值、报警历史/SQLite、用户权限和真实 PLC 驱动仍在后续里程碑。由于尚未实现认证与授权，不要把服务暴露到不可信网络。
