# HMIStudio

面向 Windows x64 单机部署的 HMI/组态软件首版。技术栈为 C# + ASP.NET Core（.NET 10）+ React/TypeScript，当前只提供本地模拟 PLC，不连接真实设备。

## 当前可用能力

- 本机 ASP.NET Core 运行时、演示工程自动创建、JSON 工程持久化
- 变量元数据、写入校验/回执、质量位与 SSE 实时订阅
- 运行监控：设备状态、变量列表、目标速度写入、模拟信号注入与报警切换
- 变量监视：搜索变量、查看当前值/上下限/质量位/读写权限，接收 SSE 实时更新
- 报警中心：活动报警确认、报警事件历史查询与 SQLite 持久化，页面自动刷新
- 组态编辑器：控件库、拖拽移动、八方向缩放、控件与画面属性编辑、静态值/变量绑定
- 多画面管理：新建/删除画面、切换编辑画面、设置工程启动画面，画面配置保存到 JSON 工程
- 工程结构校验：在编辑器中检查已保存的 JSON 工程结构，并显示校验结果
- 编辑器撤销/重做、复制/粘贴、多选、批量对齐、批量删除、保存后重新读取验证
- 运行画面共享控件渲染器；按钮支持变量写入或跳转画面，运行时支持上一/下一画面历史导航，数值输入可绑定可写变量，并按变量元数据校验最小/最大值；绑定变量质量异常时显示告警标识，禁用质量不良变量的数值写入，写入按钮也会遵守目标变量的可写与质量状态
- Windows CI：前端 TypeScript 构建、.NET restore/build/test、运行时 API 与工程持久化 smoke test

## 云端开发与在线预览（GitHub Codespaces）

如果暂时不想在自己的电脑安装 .NET/Node.js，可以使用 Codespaces 在云端运行项目：

1. 打开仓库，进入 **Code → Codespaces → Create codespace on `feature/m0-core-contracts`**。
2. 等待云端开发容器初始化。首次初始化会安装前端依赖、构建前端并还原 .NET 依赖。
3. 容器启动后会自动运行 HMIStudio 服务。Codespaces 检测到 **5080** 端口后，可在 **Ports** 面板打开转发网址预览。
4. 修改代码后，前端重新构建：在终端运行 `cd src/HMI.Web && npm run build`；服务端代码修改后，停止并重新运行服务，或重启 Codespace。

云端服务通过端口转发访问，默认用于开发预览，不应放入真实生产数据，也不要连接真实 PLC。Codespaces 是否可用及运行时长取决于 GitHub 账户额度。首次启动会从仓库分支创建独立云端环境，不会自动合并到 `main`。

## 快速启动（Windows）

前置条件：安装 **.NET 10 SDK** 与 **Node.js 20+**，并允许 PowerShell 执行本地脚本。

在仓库根目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Start-HMIStudio.ps1
```

启动脚本会检查环境、安装缺失的前端依赖、构建 React 前端、启动本机服务，并在服务就绪后打开浏览器：

`http://127.0.0.1:5080`

首次启动会自动创建演示工程。服务默认只绑定本机 loopback，不连接真实 PLC。服务日志位于系统临时目录的 `hmistudio-server.log` 与 `hmistudio-server-error.log`。脚本启动时会打印停止服务的命令。

## 模拟运行流程

1. 打开 **组态编辑**，从左侧控件库添加按钮、文本、指示灯或数值输入。
2. 选择控件，在右侧属性检查器设置外观。文本/数值显示可切换静态值或绑定工程变量。
3. 对按钮设置 **写入变量** 与 **写入值**；布尔变量可使用 `toggle` 在 TRUE/FALSE 间切换。
4. 点击 **保存工程**，等待保存后重新读取验证成功。
5. 返回运行监控，再打开 **运行画面**。按钮写入和数值输入会提交到本地模拟变量，变量值由实时订阅更新。
6. 在运行监控页也可以直接启停模拟设备、修改目标速度、注入速度值及触发/清除报警。

## 开发模式

终端一启动后端：

```powershell
dotnet run --project src/HMI.Server
```

终端二启动前端：

```powershell
cd src/HMI.Web
npm install
npm run dev
```

打开 `http://127.0.0.1:5173`。开发前端默认访问 `http://127.0.0.1:5080` API。

## 主要目录

- `src/HMI.Core.Interface`：核心接口和 DTO
- `src/HMI.Core.Model`：工程、画面、变量元数据与属性值源模型
- `src/HMI.Core.Services`：变量池、订阅、工程存储、模拟 PLC
- `src/HMI.Server`：本地 ASP.NET Core 服务
- `src/HMI.Web`：React/TypeScript 运行监控、组态编辑器与运行画面
- `src/HMI.Core.Tests`：核心自动化测试

## 当前边界

这是可操作的早期模拟版本，不是生产级 HMI。暂不支持真实 PLC 驱动、用户认证/角色权限与事件脚本。按钮目前支持变量写入和静态目标画面导航；复杂的条件动作、表达式/公式求值和组件化模板仍需继续开发。报警中心和变量监视已提供基础操作能力，但仍需进一步完善边界场景与端到端交互测试。不要将本地服务暴露到不可信网络。
