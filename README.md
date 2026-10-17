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
- 工程备份：通过本机 API 下载 ZIP，包含工程 JSON、SQLite 报警历史快照与备份清单
- 备份校验预览与恢复：在编辑器中检查 ZIP 工程结构、画面/变量数量、SQLite 完整性及报警历史表必要字段；恢复会再次校验并暂存文件，仅在服务下次启动时应用，应用失败会尝试回滚
- 编辑器撤销/重做、复制/粘贴、多选、批量对齐、批量删除、保存后重新读取验证
- 运行画面共享控件渲染器；按钮支持变量写入或跳转画面，运行时支持上一/下一画面历史导航，数值输入可绑定可写变量，并按变量元数据校验最小/最大值；绑定变量质量异常时显示告警标识，禁用质量不良变量的数值写入，写入按钮也会遵守目标变量的可写与质量状态
- 报警历史 CSV 导出：报警中心可导出最近最多 1000 条事件，使用 UTF-8 BOM，便于 Excel 打开；CSV 字段经过转义
- Windows CI：前端 TypeScript 构建、.NET restore/build/test、运行时 API 与工程持久化 smoke test

## 云端开发与在线预览（GitHub Codespaces）

如果暂时不想在自己的电脑安装 .NET/Node.js，可以使用 Codespaces 在云端运行项目：

1. 打开仓库，进入 **Code → Codespaces → Create codespace on `feature/m0-core-contracts`**。
2. 等待云端开发容器初始化。首次初始化会安装前端依赖、构建前端并还原 .NET 依赖。
3. 容器启动后会自动运行 HMIStudio 服务。Codespaces 检测到 **5080** 端口后，可在 **Ports** 面板打开转发网址预览。
4. 修改代码后，前端重新构建：在终端运行 `cd src/HMI.Web && npm run build`；服务端代码修改后，停止并重新运行服务，或重启 Codespace。

云端服务通过端口转发访问，默认用于开发预览，不应放入真实生产数据，也不要连接真实 PLC。Codespaces 是否可用及运行时长取决于 GitHub 账户额度。首次启动会从仓库分支创建独立云端环境，不会自动合并到 `main`。

## 工程备份

运行服务时访问 `http://127.0.0.1:5080/api/backup`，浏览器会下载一个 ZIP 备份包。编辑器工具栏的“校验备份”可只读检查 ZIP；“恢复备份”会对上传包再次检查，并将文件暂存到 `data/restore-pending`。恢复不会在运行中覆盖打开的工程或 SQLite 文件，而是在下次服务启动时、加载工程与打开数据库之前应用。应用前会在 `data/restore-rollback` 保存回滚副本；启动阶段应用失败时会尝试恢复原文件。若备份不含报警历史，则保留当前报警数据库。恢复前仍建议下载当前备份。恢复任务暂存成功后，请正常关闭并重新启动 HMIStudio；在重启前当前运行实例不会改变。

## 发布 Windows x64 便携版

开发者在 Windows x64 环境安装 .NET 10 SDK 和 Node.js 20+ 后，在仓库根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Publish-HMIStudio-Windows.ps1
```

脚本会构建前端，并发布自包含的 Windows x64 后端，生成：

- `artifacts/HMIStudio-win-x64/`：便携版完整目录
- `artifacts/HMIStudio-win-x64.zip`：可分发压缩包

目标电脑无需安装 .NET Runtime、.NET SDK 或 Node.js。将 ZIP 解压到可写入的本地目录，然后运行 `Start-HMIStudio.ps1`。首次启动会在发布目录创建 `data` 文件夹，用于工程 JSON 与 SQLite 报警历史。升级前请备份该目录。

GitHub Actions 会构建发布包并启动自包含程序，检查健康 API、前端页面和变量 API。便携版仍仅供本机模拟使用，不要将服务暴露到不可信网络。

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

这是可操作的早期模拟版本，不是生产级 HMI。暂不支持真实 PLC 驱动、用户认证/角色权限与事件脚本。按钮目前支持变量写入和静态目标画面导航；复杂的条件动作、表达式/公式求值和组件化模板仍需继续开发。报警中心支持活动报警确认、历史查看与 CSV 导出；变量监视支持实时订阅与筛选。仍需进一步完善边界场景与端到端交互测试。不要将本地服务暴露到不可信网络。
