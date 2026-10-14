# HMIStudio 开发方案

> **文档说明**：本文是**本项目唯一的规划与需求文档**。原先并入的对标借鉴点（NEXT HMI 的 B1–B10 与鲲鹏恒控 KPHK 的 K1–K9）保留在第五、六节；新增的「**第七节 需求规格**」承接原《HMI组态框架总体技术方案》的有效需求。第二节各里程碑任务下的「**借鉴落点**」行与第五节末尾的 `### 5.5 借鉴点统一索引（取长补短）` 一并保留。

> - 创建日期：2026-09-29；最后更新：2026-10-12
> - 核对依据：**逐文件通读真实代码**（5 个项目）+ `优化记录.MD` 与本文件第七节
> - 本文件性质：**开发顺序建议**，非契约。凡与代码不符，以代码为准。
> - 已确认前提：**PLC 先做模拟器/桩驱动，真实硬件后置**；**目标架构为「HMI.Server + HMI.Web」**（见第六节 D1–D5）。
> - 口径：**本项目按新项目推进** —— 旧工程文件与 WPF 版 Studio/Runtime 一律不作兼容、不作保活（见第四节"已定实现形状"）。（`HMI.Studio` / `HMI.Runtime` 两个 WPF 项目及其代码**已于 2026-10-11 删除**）

---

## 一、现状核对（基于真实代码，非快照、非推测）

### 1.1 已真正落地（有实现、可运行）

| 能力 | 实现位置 | 说明 |
|---|---|---|
| 三层骨架 | `HMIStudio.sln` | **3 个项目**：Interface / Model / Services，均为 `net8.0`（WPF 的 Studio / Runtime 已删除） |
| 接口层契约 | `src/HMI.Core.Interface/*.cs` | Host/Variables/Actions/Controls/Expression/Plc/Camera/Project/Misc/Enums |
| 变量池 | `src/HMI.Core.Services/MemoryVariablePool.cs` | 细粒度变量锁、全量串行写队列（纯 FIFO 逐条下发，等价默认 `Strict`） |
| 订阅管理器 | `src/HMI.Core.Services/VariableSubscriptionManager.cs` | 画面引用计数 + 按 `CycleMs` 分组轮询 + `DebounceMs` 时间窗防抖 |
| 表达式引擎 | `src/HMI.Core.Services/ExpressionEngine.cs` | `${VarId}`、比较、`&&`/`||`、括号；异常视为 false |
| 动作引擎 | `src/HMI.Core.Services/ActionEngine.cs` | 已注册：`WriteVariable` / `SetBoolean` / `NavigateScreen` / `NavigateBack` / `Delay` |
| 画面导航 | `src/HMI.Core.Services/ScreenNavigationService.cs` | 画面栈压"来源"、返回弹栈 |
| 工程读写 | `src/HMI.Core.Services/ProjectStorage.cs` | JSON 加载/保存/基础校验（启动画面存在、变量 Id 去重） |
| 宿主生命周期 | `src/HMI.Core.Services/HostAndMisc.cs` | `HostLifetime` 已实现但当前**无任何宿主消费**（原 WPF Runtime 的 `App.xaml.cs` 内联装配已随项目删除）；M2 的 `HMI.Server` 将成为其使用者 |
| 消息总线 | `src/HMI.Core.Services/MessageBus.cs` | `InMemoryMessageBus` **已完整实现**（发布/订阅/退订）；当前**无消费方**，M2 的 WebSocket 值推送将首先消费 |

> 以下为**已删除的 WPF 版**（2026-10-11）在删除前的能力，仅作历史对照，**不计入上表**：
> - **Runtime 渲染**（`src/HMI.Runtime/RuntimeRenderer.cs`）：仅 Text / Button；可见性/可控性/背景/前景条件映射
> - **Runtime 宿主**（`src/HMI.Runtime/App.xaml.cs`、`MainWindow.xaml(.cs)`）：`--project <path>` 加载工程、启动画面激活、异常落盘
> - **设计器**（`src/HMI.Studio/MainWindow.xaml(.cs)`）：菜单+工具栏骨架、左侧资源树、变量表、画布（拖入 Text/Button）、右侧属性面板 5 组、多选批量、复制粘贴、Z 序、最近色四组、保存/打开/试运行

### 1.2 占位 / 空壳（接口在、实现在但无真实行为）

| 能力 | 证据 | 缺口 |
|---|---|---|
| PLC 通讯 | `HostAndMisc.cs` 的 `StubPlcDriver` | `ConnectAsync` 恒返回 false、状态恒 `Disconnected`；`IPlcDriverFactory` 未接入任何实现 |
| PLC 变量 | `MemoryVariablePool.Register` | 非内存变量一律标 `BadComm`，**从不发起任何采集** |
| 相机 | `HMI.Core.Interface/Camera.cs` | 仅契约，无设备枚举、无多路、无帧缓冲 |
| 日志 | `HostAndMisc.cs` `ConsoleLogger` | 仅控制台/落盘，`QueryAsync` 直接 `yield break`，无历史查询 |

### 1.3 完全未实现（仅接口声明或菜单禁用）

> 说明：以下所列为**已删除的 WPF 版**在删除前的状态，仅作历史对照。其 UI 类缺口将由 `HMI.Web` 承担。

- **业务模块**：报警、历史存储/趋势、配方、权限、多语言、数据记录、诊断面板 —— `Misc.cs` 仅接口声明，无任何实现类。
- **控件渲染**：Table / Trend / 相机 / 图元（直线/矩形/圆）/ 进度条 / 指示灯 / 下拉框 / 勾选框 —— 工具箱中除 Text/Button 外全部 `Enabled: false`，`RuntimeRenderer.BuildControl` 对非 Text/Button 返回 `null`。
- **编辑能力**：撤销、重做、查找、替换、对齐、分布、大小、分层、网格 —— `MainWindow.xaml` 中对应菜单 `IsEnabled="False"`，`EditFind_Click`/`EditReplace_Click` 仅弹"待画布/资源树切片实现"提示。
- **工程发布链路**：另存为、工程发布、编译工程、工程完整性校验、导入/移除 DLL、变量模拟器、表达式测试器、选项 —— 全部 `IsEnabled="False"`。
- **多画面**：设计器资源树里只有硬编码的 `ScreenA` 一个画面节点（`MainWindow.xaml` 的 `Tag="screen_edit"`）。
- **外挂 DLL 插件体系**：`IActionPluginManager` / `IActionPluginFactory` —— 仅**接口声明**（[Actions.cs](file:///e:/doc/vs%20project/HMIStudio/src/HMI.Core.Interface/Actions.cs#L42-L54)，无实现类）；`SHA256 白名单` / `IScriptEngine` / `IExtensionPlugin` —— 仅**方案声明**（代码中连接口都无声明，仅存在于已删除的原《HMI组态框架总体技术方案》中，其有效需求见第七节）；整体无实现。

### 1.4 文档与代码的偏差（必须先对齐，避免按文档排期踩空）

1. **（已消解）方案文档版本号自相矛盾**：原《HMI组态框架总体技术方案》文件名 V1.3 与正文"V1.4（合并版）"不一致；该文档**已于 2026-10-11 删除**，其有效需求并入第七节，此矛盾不再存在。
2. **§15 设计器目标形态基本未落地**：§15 的菜单栏/资源树/工具箱/属性面板/画布能力，代码里只有"骨架 + 少量实现"；§15.9 也已自我声明"非当前实现承诺"。
3. **§14 部分设计未落地**：
   - §14.2「写语义 Strict/Coalesce」：`WriteSemantics` 枚举已定义，但 `MemoryVariablePool` 的写队列**为纯 FIFO 逐条下发（等价默认 `Strict`）**，并未按 `Coalesce` 语义做入队合并。原「写后冷却同变量合并」机制已随 `CoolDownMs` 字段一并删除（详见第四节实现形状表）。
   - §14.3「坏标签熔断」（`MaxFailCount` / `FuseCooldownMs`）：代码中**完全没有**。
   - §14.1「批量读分帧」、§3.5「底层报文聚合」：**没有**；订阅管理器只按 `CycleMs` 分组，未按 PLC 站点/地址区间聚合。
   - §14.5「相机多路 / FrameBuffer」：**没有**（相机契约缺口详见 6.5）。

---

## 二、里程碑体系（唯一体系，M0 起）

> **口径说明**：本节为**唯一**里程碑体系，已合并原「M1–M7」与「阶段 0–3」两套表述。
> 因目标架构改为「HMI.Server + HMI.Web」（见第六节），**凡 UI 类任务一律标注「在 HMI.Web 实现」**；针对 WPF 设计器/Runtime 的专属任务已删除（**该两个项目已于 2026-10-11 删除**，见 6.3）。
>
> **整合说明**：每个里程碑的「**借鉴落点**」= 第五节外部对标（NEXT HMI B1–B10 / KPHK K1–K5 及次级项）中该里程碑应吸收的条目，格式为 `（借 Bx / Kx：一句话）`；统一索引见 `5.5`。

排序原则（三条）：
1. **先补齐"最小可用闭环"**：当前只能渲染 2 种控件、PLC 只有桩 → 先让"能真正跑起来"的链路变长。
2. **被依赖者先行**：报警依赖表格控件（§7.1 报警历史复用 `TableControl`）；表格/趋势依赖编辑器能配置其属性 → 控件与编辑器必须成对推进。
3. **风险前置**：接口已定（`IPlcDriver` 等），先做模拟实现固化调用链，真机驱动后置时替换成本最低。

> 口径说明：**M0 属"使能型例外"**——它是纯契约、无运行产出，排在首位是遵循原则2（被依赖者先行，其产出对 M2/M3/M6 全部生效），而非原则1。

### M0 Core 契约扩展（无 UI，纯契约先行）★最高优先

**目标**：把后续所有环节都要依赖的契约一次性补齐。这些产出对 `HMI.Server`/`HMI.Web` 全部生效，故零浪费。

任务（**开工顺序：1 → 2 → 3 → 4 → 5，相机契约殿后**——1–4 是 M2/M3/M6 的直接前置，相机属 M5）：
1. **`IPropertySource` 抽象**（放 `HMI.Core.Interface`，**多态**：接口 + 基础 4 实现 `$static`/`$var`/`$expr`/`$if`，**并入组件/Repeater 3 源** `$componentProp`/`$widgetProp`/`$repeatItem`，并补齐能力缺口的 `$formula`/`$recipe`+`$recipeList`/`$alarmCount`，STJ 判别字段 `type`）：以 `Source` **直接替换** `PropertyBindingInfo.Binding`/`IsBound`（不留旧字段兼容读）；`Mapping` 保留独立；**属性绑定预留"绑定方向"字段**（单向 `→` / 双向 `⇄`，对应 K8）——M0 只预留字段，双向写回在 M3 实现（共用 M0 的写结果 reason code 路径）（对应 B1 + 组件体系 + K8）。
2. **变量元数据通道**（快照、**替换**语义）+ **订阅优先级**（可见画面优先，常驻低频）（对应 B2）。快照字段须含 `type`/`min`/`max`/`writable`；`VariableMeta`（Core.Model）补 `Min`/`Max`（`double?`，缺省 null=不限）；`writable` **不新增持久字段**，由 `IsReadOnly`（唯一权威字段）派生于快照（现有模型已含 `Kind`/`Source`/`IsReadOnly`/`Alias`/`Comment`/`DebounceMs`/`WriteSemantics`，无需再补）。快照走**新增独立接口**（拟名 `IVariableMetadataProvider`），与值通道分离。**值通道补双时间戳**：`VariableValuePackage` 增 `DeviceTimestamp?`（设备 / 采样时间，可空）与 `ServerTimestamp`（落值时间，原 `Timestamp` 语义）——避免 M4 报警 / M8 历史建库后再返工（对应六层链路评审第 10 条）。
3. **写结果 reason code 枚举** + `requestId` 回执关联（对应 B3）。**改签名** `Task<WriteResult> WriteAsync(varId, value, requestId=null, ct=default)`；`WriteResult` 与 reason 枚举置于 `HMI.Core.Interface`；reason 最小集 `Ok`/`NotExist`/`ReadOnly`/`DriverError`/`Cancelled`；**M0 即启用 `IsReadOnly` 写拦截**（`reason=ReadOnly`）。
4. **工程格式整数门禁** `FormatVersion`（int，与字符串 `Version` 并存，初值 1）：门禁放 `LoadAsync`（`ValidateAsync` 只报告），仅"高于当前→拒绝"，**不做迁移链、不做迁移前备份**（新项目口径，旧 `.hmijob` 作废）（对应 B4）。
5. **相机契约补齐（殿后，属 M5 前置）**：`CameraFrame` 补 `Width`/`Height`/`PixelFormat`/`Stride`；`ICameraDevice` 补触发模式/曝光/增益/ROI/帧率；新增 `IVisionProcessor`（详见 6.5）。

**借鉴落点**：（借 B1：值源 `IPropertySource`——即第 1 项落地；**并补收组件/Repeater 源** `$componentProp`/`$widgetProp`/`$repeatItem` 与缺口源 `$formula`/`$recipe`/`$alarmCount`，见 5.2 B1）（借 B2：元数据快照——即第 2 项落地）（借 B3：写结果 reason code——即第 3 项落地；并在 B3 之上加 K3 的 `WriteSource` **写门禁**按调用来源分级）（借 B4：`FormatVersion` 整数门禁——即第 4 项落地）（借 K1：确立「分发双车道」**契约** Droppable/Lossless——本阶段只定契约，M2 落地）（借 K8：**绑定方向字段预留**——单向 `→` / 双向 `⇄`，即第 1 项落地）。

**验收**：五项契约定稿且旧绑定字段已替换（值源含组件/Repeater 源与绑定方向字段）；**值通道含双时间戳**；M2/M3/M6 可直接依赖。**不要求** WPF Studio/Runtime 继续可用（**两者已于 2026-10-11 删除**，见 6.3）。

**依赖**：无（可立即开工）。**风险**：低—中。

---

### M1 端到端采集地基（模拟驱动 + 变量系统补全）

**目标**：在没有真实 PLC 的前提下，让"PLC 变量"从"永远 BadComm"变成"能周期采集、能写、能重连"的完整链路。

任务：
1. 落地 `IPlcDriverFactory` / `IPlcDriverManager` 实现（当前 `StubPlcDriverManager.RegisterFactory` 是空方法）。
2. 新增 **MockPlcDriver**（即**离线模拟模式的落地载体**）：可配置（JSON 配置地址表 + **值生成策略：常量 / 自增 / 随机 / 按地址映射回放，按点可选、默认关闭**），支持 `ReadBatchAsync`/`WriteAsync`/断线模拟（可手动注入断线重连，验证 §8.3）。**数据主路径 = 人工置值**：未显式开启自动生成的点，其值只由人工 / 动作写入决定。**写语义**：`WriteAsync` 把值写进**驱动内置的模拟点表**（下次 `ReadBatchAsync` 即读回新值）并返回成功，**绝不外发真实硬件**；写前仍走 M0 的完整写校验链（只读点 `reason=ReadOnly`、越权拦截），失败即返回对应 reason。**人工置值优先**：某点被人工 / 动作写入后**暂停该点的自动值生成**，直到人工显式恢复（避免下一拍被自增 / 随机覆盖）。
3. `MemoryVariablePool.Register` 按 `VariableMeta.Kind` + `VariableSource.Channel` 分流：内存变量直接落值；PLC 变量交由驱动；质量位随驱动状态真实变化（`Good` / `BadComm`）。
4. 采集侧：把驱动 `ReadBatchAsync` 接到订阅管理器的轮询组（当前轮询只读本池内存值）。
5. 变量表补列（**UI 落点在 M3 / HMI.Web**）：展示协议通道、PLC 地址、只读、防抖、别名、备注等。**模型字段已齐，无需新增**（`Kind`/`Source`/`IsReadOnly`/`Alias`/`Comment`/`DebounceMs`/`WriteSemantics` 均已存在于 [VariableMeta.cs](file:///e:/doc/vs%20project/HMIStudio/src/HMI.Core.Model/VariableMeta.cs)；`Min`/`Max`/`Writable` 在 M0 补）。原 Studio 变量表仅 7 列：`VarId/分类/名称/类型/初值/周期ms/常驻`。
6. **写路径质量位归驱动裁决**（读代码发现的计划外缺陷）：`MemoryVariablePool.WriteAsync` 当前把质量位**硬编码为 `Good`**（[MemoryVariablePool.cs L105](file:///e:/doc/vs%20project/HMIStudio/src/HMI.Core.Services/MemoryVariablePool.cs#L105)），会使 PLC 点位的 `BadComm` 被一次写"**洗白成在线**"（假在线）。改为：质量位**只由驱动通讯状态裁决**，写操作**不改写**质量位；驱动写失败时置对应失败质量位（`CommFailure`）。
7. **订阅引用计数归零即释放**（读代码发现的计划外缺陷）：`VariableSubscriptionManager.RemoveRef` 当前**只减计数、从不从 `_varToCycle` 摘除变量、组永不停止**（[VariableSubscriptionManager.cs L76-85](file:///e:/doc/vs%20project/HMIStudio/src/HMI.Core.Services/VariableSubscriptionManager.cs#L76-L85)，注释自认"本切片保留组方便演示"），导致**变量一旦订阅过就永久采集**（画面切来切去持续累积、CPU 空转）。改为：引用计数归零 → 摘除该变量；组内变量数为 0 → 停组并释放循环；`IsPermanent` 变量除外。
8. **订阅回调移出变量锁**（读代码发现的计划外缺陷，**风险最高**）：`MemoryVariablePool` 的 `Broadcast` 在 `lock (slot.Sync)` **内**执行全部订阅者回调（[ApplyWrite L159-168](file:///e:/doc/vs%20project/HMIStudio/src/HMI.Core.Services/MemoryVariablePool.cs#L159-L168)、[SetQuality L114-123](file:///e:/doc/vs%20project/HMIStudio/src/HMI.Core.Services/MemoryVariablePool.cs#L114-L123) 同此），而写循环**只有一条线程** → 慢回调（同步 I/O / 重序列化）**阻塞全部写**；若回调内**同步等待另一变量的写入**（订阅回调是同步 `Action<T>`、不能 `await`，只能 `.Wait()` / `.GetAwaiter().GetResult()`），则写循环线程被占住、[队列读不到下一条](file:///e:/doc/vs%20project/HMIStudio/src/HMI.Core.Services/MemoryVariablePool.cs#L104-L106) → **死锁**（同线程 `lock` 可重入，故"回调里读同一变量"不爆，问题极隐蔽）。改为：**锁内只落值 + 取订阅者快照，锁外执行回调**（或回调经独立 Channel 异步派发）。

**借鉴落点**：（借 K4：驱动契约收窄为 6 方法 + 读失败**不抛**只置 `CommFailure` + `BlockPlanner` 块读——**在本阶段即按块读落地**，真机细化留 M7）（借 K2：实时库**无锁读** + 点 Id 作连续数组下标）（借 B2：可见画面优先的分级订阅）。

> 排期说明：§14.1「批量读分帧」**原排 M7**；按 K4 建议**前移至 M1 即按块读落地**，而真机分帧细化与验证**仍留 M7**——原排期不删除。

> **离线模拟模式口径（2026-10-12 补）**：采用**部署级驱动二选一**——工程 / 服务端配置里选「模拟驱动（`MockPlcDriver`）」或「真实驱动」；选模拟即进入**离线模拟**：整条链路照常跑（采集 / 写 / 重连 / 质量位），写入只落模拟表、**不发给真实 PLC**。数据**主路径 = 人工置值**；自动值生成（常量 / 自增 / 随机 / 回放）**按点可选、默认关闭**。**不引入运行期在线↔离线热切换**（避免驱动热替换 + 质量位重置 + 轮询组重启带来的"半在线"脏状态）。完整规则见 §7.25。

**验收**：配一个 PLC 变量（如 `plcCounter`，**默认人工置值**；另可开启「自增」策略做可选验证）→ 以**集成测试 / 最小控制台宿主**验证"采集值 ↔ 变量池 ↔ 写队列 ↔ 质量位"全链（M1 阶段浏览器前端未建、原 WPF Runtime 已删除，故**不以试运行为验收载体**）：变量能周期采集并刷新、写值生效、手动断线后质量位转 `BadComm` 且重连恢复；**写 PLC 点不改变其质量位**（不出现"假在线"）、**画面切离后变量停止采集**（引用计数归零即释放）、**订阅回调在锁外执行**（写循环不被慢回调阻塞、不死锁）。**"文本控件实时刷新 / 按钮写值生效"的可视化验收下移到 M2**（服务端 + 浏览器最小前端建好后）。

**依赖**：无（可立即开工）。元数据通道是 **M2/M3 浏览器端**的前置，**不阻塞 M1**。**风险**：低。

---

### M2 服务端宿主 + 协议层 + 最小前端

**目标**：打通「.NET 后端 ↔ 浏览器」端到端，先不做编辑器。

任务：新增 `HMI.Server`（ASP.NET Core）—— REST CRUD（工程/画面/变量）+ WebSocket 值推送（**成为 `InMemoryMessageBus` 的首个消费方**，补上「总线已装配但无消费方」的缺口）；最小前端（`HMI.Web`，**只读画面**）验证"采集值 → 服务端 → 浏览器渲染"全链。

**借鉴落点**：（借 K1：分发**双车道落地** Droppable/Lossless——界面订阅走 Droppable、报警/历史订阅走 Lossless）（借次级项「无头内核 + Supervisor」：`ReadyMarker/AutoRestart/MaxRestarts`、`IsStalled` 判活、`Kill(entireProcessTree:true)`、stdin EOF 自退——`HMI.Server` 宿主模型）（借次级项「自动化 API 统一多出口」：界面/REST/MCP/外部程序共用同一套方法，`AutomationResult{Ok,Message,Data}` 失败不抛，只碰磁盘工程、不碰运行态写值）。

**验收**：浏览器打开只读画面，Mock PLC 变量经 WebSocket 实时刷新；服务端重启后浏览器能自动重连并拿到最新元数据与值。

**依赖**：M0、M1。**风险**：中——协议层是全新契约。

---

### M3 浏览器编辑器与控件渲染

**目标**：把"能看"变成"能编"，并把工具箱里"点得出来但跑不起来"的控件补上。

任务（建议固定成对交付，一次一类）：
1. **编辑器**（在 HMI.Web 实现）：页面树、属性面板（**注册表驱动**，对应 B7）、实时预览、多画面/多标签、对齐/分布/大小/分层、撤销重做（含 B6 的节流合并）、查找替换、网格与标尺与缩放、Z 序、**控件组合 / 锁定 / 缩略导航图**（§15.6）。
2. **控件渲染**（在 HMI.Web 实现）：基础图元（直线/矩形/圆/椭圆/折线）、基础输入输出（数值显示/数值输入/开关/指示灯/下拉列表/勾选框/单选按钮）、高级控件（Table 带 `IDataSource`、Trend 实时曲线、图片容器、进度条、多色报警灯）、**鼠标 / 触摸事件归一与去重**（§5：Pressed / Released / Clicked 三业务事件）、**控件预设样式体系**（按钮/指示灯/棒图风格预设 + 自定义 SVG 皮肤；样式只管风格不含颜色、元素属性优先预设兜底——见 K7，与 B9 合并）、**可复用组件 + 槽位 + Repeater**（组件间传值用 M0 的 `$componentProp`/`$widgetProp`/`$repeatItem`）、**双向 `⇄` 绑定写回**（交互控件改值写回数据源，走 M0 预留的方向字段与写结果 reason code——见 K8）、**「离线模拟中」状态徽标**（运行态顶部常驻提示；离线标志**同时写入系统变量**，可被绑定到指示灯 / 报警——见 §7.25）。
3. **`.tsx` 热编译**：服务端内置 `esbuild`（单文件二进制、跨平台），支持外挂自定义控件热编译（对应 D1）；**编译失败保留上一版产物**（对应 B8，坏编辑不让操作员屏幕变空）。

**借鉴落点**：（借 B1 + B7：值源 + 字段注册表驱动 UI——属性面板按注册表生成，新增控件只需加注册表条目 + 渲染分支；组件/Repeater 值源见 M0）（借 B6：增量保存 / 撤销栈节流合并）（借 B8：编译失败保留上一版产物——`.tsx` 热编译失败时不回退到空）（借 B9 **+ K7 合并**：令牌体系 + 控件预设样式——状态色 vs 身份色、结构常量 vs 可编辑令牌分离；「样式只管风格不含颜色、样式是引用实时推导、元素属性优先预设兜底」与令牌体系共用同一套渲染解析顺序）（借 K8：单向 `→` / 双向 `⇄` 绑定——交互控件双向写回，走 M0 已预留的方向字段与写结果 reason code）（借 B10：威胁模型——「编辑器访问 = 代码执行」必须在部署说明中明说）（借次级项「设备参数表单数据驱动 `IConnectionSchemaProvider`」：加协议前端零改动）（借界面层机制：三段式绑定选择器、按钮动作类型化、点表四区地址向导、大点表窗口化 + 只订阅可见行、刷新当前文档、预览/运行双路径）。

**验收**：工具箱中已启用的控件，运行时外观与编辑器画布一致，且支持变量绑定刷新；新增控件仅需加注册表条目 + 渲染分支。

**依赖**：M2。**风险**：中——渲染性能见 6.7 待验证项；控件按族分文件，避免单文件膨胀。

---

### M4 报警系统（P0 业务模块）

**目标**：落地 §8.2，这是工控软件的最小业务闭环。

任务：`AlarmRule` 评估（变量 + 表达式 + 等级 Info/Warning/Fault/Critical）→ 状态机（未触发/激活/已确认/恢复）→ 顶部报警条 → 本地轻量库持久化（作表格数据源）→ 动作「确认当前选中/确认全部」→ 通讯故障/相机异常自动生成系统报警；**启动配置与退出收尾**（启动画面 + 启动时按序运行全局子程序 + **退出/关机前运行收尾子程序**——K9，避免"设备没复位就断电"）。

**必须遵守 B5**：**报警定义**（`alarms.json`）与**报警状态**（`alarm_state.json`）**分文件存放**，状态文件不混入工程定义，避免污染评审与版本管理。

**借鉴落点**：（借 B5：定义态 `alarms.json` / 状态态 `alarm_state.json` 分文件——即上文「必须遵守 B5」）（借 K5：报警状态机 `Normal→ActiveUnacked→ActiveAcked→ClearedUnacked→Normal` + `Key` 身份去重 + 回差/延时；历史 SQLite 单表 WAL + 攒批 + `ROW_NUMBER` SQL 内降采样 + `RetentionDays` 扫描 + VACUUM）（借 K9：启动配置 + 退出收尾子程序——即上文本期任务）。

**验收**：配一条报警规则 → 变量越限 → 顶部报警条亮起并可"确认当前选中/确认全部" → 报警记录落本地库、可在 Table 控件中查询；通讯断线时自动生成系统报警。

**依赖**：M3 的 Table 控件。**风险**：中——本地库**已定 SQLite**（2026-10-11，见第四节第 2 项）。

---

### M5 相机与视觉（海康 GigE + OpenCV）

**目标**：落地 §14.5 相机多路，并补齐"采集→处理→回写 PLC"的视觉闭环。

> 本里程碑原列于 P1/P2；因**硬件已定（海康 GigE）**且为核心差异化能力，建议单列并前移。

任务：海康 MVS SDK 采集（GigE，含软/硬触发、曝光、ROI、多路）→ OpenCvSharp 处理（ROI/算法/OK-NG/耗时，对应 `IVisionProcessor`）→ 判定结果回写 PLC（走写队列**保序**）→ 预览帧降采样（**原图不得推向浏览器**，见 6.8 冲突 3）；**离线模拟**：无相机 / 相机未接时由 **Mock 相机源**产出**假帧**（静态测试图打底 + 可选动态假流、`FrameSeq` 单调自增），走同一 `CameraFrame` 契约，使视觉链与图像控件在无硬件时可自测（见 §7.25 / §7.16）。

**借鉴落点**：（无直接对标：相机采集与视觉为本项目自身差异化能力，NEXT HMI 与 KPHK 两来源均未提供可借鉴点；本里程碑按 §14.5 与 6.5 契约自建，仅契约补齐来自 M0 第 5 项）。

**验收**：相机采集 → OpenCV 判定 OK/NG → 结果回写 PLC 变量；浏览器只看到降采样预览帧，**原图不进入 WebSocket**。

**依赖**：M0（相机契约）、M2（服务端）。**风险**：高——MVS SDK 集成 + Linux 网卡调优（见 6.4）。

---

### M6 工程发布与工程校验

**目标**：区分「设计源文件」与「运行包」，补 §15.2 调试菜单与 §15.10 编译校验。

任务：另存为、工程发布（打包单文件/目录工程包）、编译校验（表达式/脚本/变量引用/插件引用/停靠冲突/启动首页必须为普通画面）、工程完整性校验、诊断面板（变量实时监控表 / 通讯状态 / **日志历史查询**——补 `ConsoleLogger.QueryAsync` 的历史查询能力）、设计期工具（**变量模拟器** §15.2 / **表达式测试器** §15.2）。

**借鉴落点**：（借 B4：发布 / 格式门禁——发布链路与格式门禁同源）（借次级项「工程文件源抽象」：工程=目录分文件 JSON + `IProjectFileSource`(Directory/Zip) 抽象 + 加密包 AES-GCM **内存解密不落盘**）。

**验收**：工程可发布为运行包；编译校验能拦下"启动首页非普通画面/同方位多停靠"等非法配置。

**依赖**：M1（校验需真实变量来源）、M3（多画面与停靠规则才成立）。**风险**：低—中。

---

### M7 真实硬件驱动（ModbusTCP / S7 / 汇川 EIP）

**目标**：把 M1 的 Mock 驱动替换为真实实现（用户已确认硬件后置）。

任务：ModbusTCP 驱动（建议第一个，协议最简、无授权）→ S7 → 汇川 EIP(CIP)（含 `DatasizeUnconsistent` 硬熔断）→ 批量读分帧策略（§14.1）→ 写项间站闸 `WriteGapMs` → 坏标签熔断（§14.3 的 `MaxFailCount`/`FuseCooldownMs`，当前完全缺失）→ 写语义 `Coalesce` 合并（§14.2，本期暂不实现、留待 M7，见 7.18）。

**借鉴落点**：（§14.1 批量读分帧——真机分帧细化与验证；§14.3 坏标签熔断 `MaxFailCount`/`FuseCooldownMs`——真机落地；§14.2 写语义 `Coalesce` 合并——真机落地）（借 K4：真机驱动按**同一契约**替换 Mock——读失败不抛只置 `CommFailure`、`BlockPlanner` 块读、`BackoffPolicy` 指数退避 1→30s）。

> 排期说明：§14.1 原排 M7，K1/K4 建议 M1 即按块读落地（见 M1「借鉴落点」）；此处 M7 负责**真机分帧细化**，原排期不删除。

**依赖**：M0（写结果 reason code）、M1（驱动装配与 Mock 行为基线）。

**验收**：接入真实 PLC（ModbusTCP 先行）→ 批量读/写/断线重连与 Mock 行为一致；换协议仅替换驱动实现，上层零改动。

**触发条件**：具备真机或可联调环境时启动。**风险**：高——真实协议的超时/分帧/抖动问题只有真机能暴露，Mock 阶段无法覆盖。

---

### M8 P1/P2 模块（按需，可并行插入）

周期后台任务（§9.1）、历史存储与曲线（§9.2）、**报表定义（K6：历史聚合 Avg/Max/Min/Sum/Count + 报警事件过滤，运行期查库导 CSV，见 §7.11）**、工程备份恢复（§9.4）、配方（§10.1，遵守 B5 定义/状态分文件）、虚拟数字键盘（§10.2）、多语言（§10.3）、权限、外挂插件体系（脚本引擎）、编译失败保留上一版产物（B8）、主题令牌体系（B9）。

**借鉴落点**：（借 B8：编译失败**保留上一版产物**）（借 B9：主题令牌体系）（借 K6：报表定义——即上文本期任务）（借 B10：威胁模型）（借次级项「脚本引擎流水线」：黑名单安全分析→Roslyn→名+哈希缓存→协作式超时；结果五态不抛；`TickGate` 丢拍不堆积；全部脚本同一信号量串行）（借次级项「自动化 API 多出口」）（借次级项「MCP 服务器=Automation 薄包装」：独立 exe，stdio/http + token，刻意无运行态写值工具）（借次级项「权限模型」：角色等级 + 8 权限位 + `configMode` + PBKDF2 + `FixedTimeEquals` + 统一回话防账号枚举）。

**依赖**：视模块而定——历史存储/配方/权限依赖 M4 的本地库与 M3 的控件；主题令牌依赖 M3 的渲染层。**风险**：低（可并行插入）。

---

## 三、关键结论（附优缺点）

### 结论 1：编辑器与运行时走「浏览器一体化」，不维护双套渲染

> 原结论「设计器走渐进增强、不整体重构画布、M4 前拆 WPF 文件」**已随 6.3 作废**（WPF 设计器与 Runtime 均已删除）。

新结论：**编辑器与运行时共享同一份 `HMI.Web` 渲染代码**。这是"实时预览"可行的前提——若预览在浏览器、运行在 WPF，将必然维护两套渲染实现并随时间漂移。

- **优点**：① 预览与运行天然一致，无需防漂移夹具；② WPF 版现仅支持 Text/Button，重写损失极小；③ 控件、属性面板、绑定源可复用同一套注册表（B7）。
- **缺点**：① 编辑器需从零构建（WPF 版已投入的拖放/多选/复制粘贴/Z 序/最近色不能直接迁移）；② 浏览器端重图形性能需实测（见 6.7）；③ 需要新增服务端协议层。
- **结论**：不再拆分 WPF 大文件；**从零按"注册表驱动"构建 `HMI.Web` 编辑器**。

### 结论 2：PLC 按「模拟器先行」推进（已确认），但必须同时补齐接口装配

- **优点**：无硬件即可验证"连接→批量读→写→重连→质量位"全链；Mock 可做回归测试；`IPlcDriver` 接口已定，后置换实现成本低；也能反过来验证 M1"变量池按 Kind 分流"设计的正确性。
- **缺点**：无法暴露真实协议的分帧、超时、断线抖动、EIP 硬熔断等问题，§14.1/§14.3 的部分设计只能到真机验证；存在"模拟通过、真机翻车"的返工风险。
- **结论**：M1 先把 Mock 与接口装配做完（当前 `RegisterFactory` 是空方法，必须先补），M7 再替换真机。**同时建议**：Mock 驱动的地址模型直接按 ModbusTCP 风格设计（3x/4x/0x/1x），使 M7 首个真实驱动可以最小改动替换。

---

## 四、排期决策（4 项已全部拍板）

1. **M0 内五项契约扩展的先后顺序** —— **【已定】1 → 2 → 3 → 4 → 5**：契约 1–4 先行（M2/M3/M6 的直接前置），**相机契约（第5项）殿后**（属 M5）。
2. **M4 报警本地库选型** —— **【已定 2026-10-11】SQLite**：报警/历史统一本地库，按 K5 方案（单表 WAL + 攒批 + `ROW_NUMBER` SQL 内降采样 + `RetentionDays` 扫描 + VACUUM）。
3. **M5「相机与视觉」单列并前移**（原在 P1/P2）—— **【已定 2026-10-11】认可**：单列为 M5，位置按现排法 **M4 之后、M6 之前**（不再进一步前移到 M3 之前）。
4. **M6 工程发布/校验是否需提前** —— **【已定 2026-10-11】维持原位，不提前**：开工先做切片演示，演示载体直接用里程碑验收——**M2**（浏览器只读画面 + WebSocket 实时刷新）→ **M3**（浏览器编辑器能拖控件/绑变量）→ **M4**（越限→报警条→确认→落库，工控最小业务闭环）；M6（发布打包/编译校验）对"证明架构可行"无增量，提前做会因 M3 控件族未铺开而空规则集返工。演示形态为源码工程 + 命令行启动，暂无"发布包"形态。

**已定的 M0 实现形状**（2026-10-09 拍板，按**新项目口径**——旧工程文件与 WPF 版 Studio/Runtime 一律不作兼容、不作保活）：

| 项 | 决定 |
|---|---|
| `IPropertySource` 归属 | 放 `HMI.Core.Interface` |
| `IPropertySource` 形态 | **多态**：接口 + 基础 4 实现（`$static`/`$var`/`$expr`/`$if`）+ 组件/Repeater 3 源（`$componentProp`/`$widgetProp`/`$repeatItem`）+ 缺口源（`$formula`/`$recipe`+`$recipeList`/`$alarmCount`），STJ 判别字段 `type` |
| 绑定方向字段 | 属性绑定预留单向 `→` / 双向 `⇄`（M0 只留字段；双向写回 M3 实现，对应 K8） |
| 旧绑定字段 | **直接替换**：`Source` 取代 `PropertyBindingInfo.Binding`/`IsBound`，不留兼容读；`Mapping` 保留独立 |
| `VariableMeta` 量程 | 补 `Min`/`Max`（`double?`，null=不限） |
| `Writable` 权威 | `IsReadOnly` 为唯一持久权威；`Writable` 仅派生/快照字段，**不持久** |
| 元数据快照契约 | 新增独立接口（拟名 `IVariableMetadataProvider`），与值通道分离 |
| `FormatVersion` | int，与字符串 `Version` 并存；初值 1 |
| 格式门禁位置 | 门禁放 `LoadAsync`（`ValidateAsync` 只报告）；**不做迁移链/备份**，旧 `.hmijob` 作废 |
| 写结果 | 改签名 `Task<WriteResult> WriteAsync(varId, value, requestId=null, ct=default)`；`WriteResult` 与 reason 枚举置于 Interface |
| reason 集合 | `Ok`/`NotExist`/`ReadOnly`/`DriverError`/`Cancelled` |
| 只读写拦截 | M0 即启用（`reason=ReadOnly`） |
| 写节流 / 冷却 | **删除** `VariableMeta.CoolDownMs` 字段与变量池冷却机制；写队列为纯 `Strict` FIFO（逐条下发、保序）；`Coalesce` 暂不实现（留待 M7）；reason 不再含 `Cooldown` |

---

## 五、外部对标：可借鉴点清单（NEXT HMI + 鲲鹏恒控 KPHK）

参考项目：`https://github.com/agentthink/next-scada-hmi`（AGPL-3.0 开源，自托管 SCADA/HMI）。

### 5.0 核对口径与边界（先声明，避免误读）

- **来源**：该仓库 README，以及 `docs/dev/` 下的 `architecture/overview.md`、`architecture/data-formats.md`、`architecture/value-types.md`、`architecture/websocket.md`、`architecture/backend.md`、`architecture/frontend.md`、`reference/custom-widgets.md`、`reference/theming.md`、`reference/mcp.md`。以上均为**真实抓取的文档内容**，非推测。
- **补充抓取（2026-10-12 修正）**：首轮标记"未取得"的 `architecture/backend.md`、`reference/mcp.md` 已**重新抓取成功**。① `backend.md`：后端为 `main.py`（单工程应用，一进程一运行工程）+ `manager.py`（常驻门面，把 `/runtime/<slug>/*`、`/editor/<slug>/*` 反向代理到子进程，mDNS 对等发现仅属 manager）——**我方 T1 单机一体化不采纳多进程代理模型**，仅作对照留痕；② `mcp.md`：MCP 由 manager 承载 `/mcp/`（Streamable HTTP），工具按 `projects_*`/`pages_*`/`datasources_*`/`variables_*`/`alarms_*`/`components_*` 等分组，**content-only、无生命周期工具**——与 5.4「MCP=Automation 薄包装、刻意无运行态写值工具」一致，作为次级项佐证。
- **技术路线差异（关键）**：NEXT HMI 为「自托管服务 + 浏览器」路线（Python/FastAPI + React/TS + OPC-UA + `/ws`）；HMIStudio **原为**「WPF 设计器 + 本地 WPF Runtime」路线，**现目标架构已改为「HMI.Server + HMI.Web」，与 NEXT HMI 同构**（见第六节）。
  因此**技术栈层面已可借鉴**（React 生态、WebSocket 协议族、编辑器交互模式）；仅需按 .NET 后端与 T1 拓扑调整具体实现（如 `$http` 源、manager 多进程反向代理不采纳）。

### 5.1 NEXT HMI 借鉴点总表

| 编号 | 借鉴点 | NEXT HMI 做法（来源） | HMIStudio 现状（代码证据） | 落点 | 建议强度 |
|---|---|---|---|---|---|
| B1 | 属性值 = 「值源 Source」模型，**字段类型决定可选源** | value-types.md：`属性值 = 一个产出该字段所需类型的 Source`；源分"万能型"(`$static`/`$var`/`$if`/`$switch`…)与"定类型"(`$compare`/`$formula`/`$page`/`$viewport`…)；可用源**仅由字段类型决定**，无逐字段白名单 | 绑定只有布尔表达式：`ExpressionEngine`（`${VarId}`、比较、`&&`/`||`、括号，异常视为 false）+ 每属性 `Binding`/`IsBound` 布尔绑定；**源集只裁到 4 种**，缺组件/配方/报警计数/算术源 | M0 定抽象（**含组件/Repeater 源**）/ M3 用起来 | ★★★ |
| B2 | **元数据快照**与**值增量**分离 + 按可见性分级订阅 | websocket.md：握手先发 `var_metadata`（**权威替换**快照，含 `type/min/max/writable`），再分块发 `var_update`（500/块）；`set_context` 上报当前画面 → 后端解析变量引用并调整 OPC-UA 快速订阅 | `VariableValuePackage` 只有 `VarId/Value/Quality/Timestamp`，**无元数据通道**；`VariableSubscriptionManager` 仅按 `CycleMs` 分组轮询 + `DebounceMs` 防抖，**无"可见画面优先"概念** | M0 / M1 | ★★★ |
| B3 | 写结果 **reason code 稳定契约** + `requestId` 异步回执 | websocket.md：`write_response`/`write_error`，reason 为稳定契约（`permission_denied`/`read_only`/`invalid_value`/`value_out_of_range`/`opcua_unreachable`…）；`requestId` 关联 `onSuccess`/`onFailed`/`onSettled` | `ActionEngine` 未注册动作仅 `_ => WARN`；变量写无结果枚举、无回执关联 | M0 | ★★ |
| B4 | 工程格式**整数门禁** + **迁移链** + 迁移前备份 | data-formats.md：`formatVersion` 是"唯一的门"（低于则逐级迁移，高于则拒绝）；`minAppVersion` **仅展示**不比较（让旧版本能告诉用户"你需要哪个版本"）；迁移链 `_STEPS`（4→7→8→9），迁移前自动落 `.backups/pre-migration-*.zip` | `HmiProject.Version` 是**字符串** `"1.0.0"`（语义版本，非格式门禁）；`ProjectStorage.ValidateAsync` 仅两条规则（启动画面存在、变量 Id 去重） | M0 落地（**仅取整数门禁，不取迁移链/备份**） | ★★★ |
| B5 | **配置(定义)** 与 **运行时状态** 分文件存放 | data-formats.md：`alarms.json`(定义) vs `alarm_state.json`(激活/历史/确认态)；`recipes.json` vs `recipe_state.json`；且状态文件随工程传输（接收方本地 ack 被覆盖） | 报警/配方均未实现（`Misc.cs` 仅接口声明） | M4（必须此时定） | ★★★ |
| B6 | **增量保存** + 撤销栈"节流合并" | frontend.md：`loadedPageIds`/`dirtyPageIds`，保存时 `PUT index` + 仅对 dirty 页面 `PUT`；`projectActions.ts` 用**一个节流窗口**把连续击键折成**一步**撤销；组件创建/删除直写 API **不进**历史 | 原 Studio（已删除）为"整工程保存"；撤销/重做菜单 `IsEnabled="False"`（未实现） | M3 | ★★ |
| B7 | 编辑器 UI 由**字段注册表**驱动生成 | theming.md / custom-widgets.md：`THEME_TOKENS` 注册表（`cssVar`+`themePath`+编辑器元数据）→ 主题编辑器 UI 自动生成；widget `schema` 从源码抽取 → 属性面板自动生成 | 原 WPF 设计器：`MainWindow.xaml` ~79KB、`MainWindow.xaml.cs` ~99KB，属性面板逐字段**手写 XAML** | M3 | ★★★ |
| B8 | 编译失败**保留上一版产物** | custom-widgets.md：项目控件编译失败时保留上次的 `index.js`（"坏编辑不让操作员屏幕变空"）；但内置控件构建失败**不回退**（不允许发布与源码不符的旧产物） | 外挂插件体系未实现 | M8 | ★★ |
| B9 | 「状态色 vs 身份色」「结构常量 vs 可编辑令牌」分离 | theming.md：图表系列色 8 色**固定不可编辑**（系列色是**身份**不是状态，从强调色派生会让 8 条曲线读不出来）；`info` 固定蓝；`--hmi-topbar-height` 等**结构常量不进主题编辑器** | 原 Studio/Runtime（已删除）有"四组最近色"（`_recentColors["Background"/"Foreground"/"BorderColor"/"Screen"]`），控件样式硬编码；无令牌体系 | M3 / M8 | ★★ |
| B10 | 「**编辑器访问 = 代码执行**」威胁模型必须明说 | README Security + custom-widgets.md：编辑器能写服务器文件、能在每个操作员浏览器执行任意 JS；部署须在可信 OT 网络 + VPN 后，**绝不暴露公网** | 原设计器为本地 WPF 单机，故无此面；前端转 HMI.Web 后，D1 的 `.tsx` 热编译与 M8 的插件会引入同等风险 | M3 / M8 | ★★ |

### 5.2 四项最高价值借鉴点的取舍分析

#### B1 属性值「值源」模型（★★★，落点 M0 定抽象 / M3 用起来）

**为什么现在就要想**：M3 要补"数值显示 / 数值输入 / 下拉框 / 进度条"等控件。这些控件的绑定需求是**取值**，而当前 `ExpressionEngine` 只产出**布尔**（见 [Expression.cs](file:///e:/doc/vs%20project/HMIStudio/src/HMI.Core.Interface/Expression.cs) 契约与 `ExpressionEngine` 实现）。若直接开写，很容易长出第二套"数值绑定"补丁，最后变成布尔一套、数值一套、文本一套的三份逻辑。

**建议做法（裁剪版，不照搬 27 种源）**：
- 抽象一层 `IPropertySource`。**基础 4 种**：`$static`（字面量）、`$var`（变量取值，带可选 `index`）、`$expr`（现有表达式，产出 bool）、`$if`（条件选值）。
- **能力缺口补齐 3 类**（原方案遗漏，2026-10-12 补）：`$formula`（Float 算术，如 `({1}-32)/1.8`）、`$recipe`+`$recipeList`（配方绑定，含"实时值≠已加载集"）、`$alarmCount`（按级别计数）。
- **组件与重复器源（2026-10-12 补收，与 B1 同批定抽象）**：`$componentProp`（父级/动作传入值）、`$widgetProp`（组件导出属性，同级→我）、`$repeatItem`+`repeatIndex`（Repeater 复本项）——这三者是「可复用组件 + 槽位 + Repeater」的地基；**若 M3 才加会导致 B1 抽象返工，故并入 M0 一起定**。
- **属性声明里写明"该字段要什么类型"**（String/Int/Float/Bool/Color），属性面板据此**自动过滤**可选源并自动选默认编辑器 —— 这就是 B7 的落地方式。
- 现有 `Binding` **直接替换**为 `$expr` 源（不留旧字段兼容读，新项目口径）。
- **绑定方向字段预留**（对应 K8）：属性绑定记录单向 `→` / 双向 `⇄`，M0 只留字段、M3 才实现写回。

**优点**：① 一处抽象同时解决布尔/数值/文本绑定，避免三套逻辑；② 字段类型约束源，编辑器可预先拦住非法配置（比运行时才发现好）；③ 与 B7 天然合体，属性面板可自动生成。

**缺点**：① 需要改 `ConfigControlItem` 的绑定字段结构（`Source` 直接替换，见 M0）→ 触碰工程文件格式，**必须与 B4 的格式门禁一起做**，为将来格式演进留安全网（按新项目口径，旧工程不作兼容）；② 源的解析需在设计时与运行时**各实现一次**（NEXT HMI 用 JSON fixture 把 TS 与 Python 两版实现"钉"成一致），你只需一份，但仍要写单测防漂移；③ 抽象过早会拖慢进度 —— 若只想先跑通 2 个控件，可先做，但**不要**再做第二套并行绑定。

#### B2 元数据快照 + 可见性分级订阅（★★★，落点 M0 / M1）

**为什么重要**：浏览器端数值输入/进度条/趋势需要变量的量程与可写性，而 [VariableMeta.cs](file:///e:/doc/vs%20project/HMIStudio/src/HMI.Core.Model/VariableMeta.cs) 已有 `IsReadOnly`/`Alias`/`Comment`/`Source` 等字段，**唯独缺 `Min`/`Max`**（`Writable` 由 `IsReadOnly` 派生，不新增持久字段）。这些是**元数据**，不是值。当前 `VariableValuePackage` 无元数据通道 → 运行时无从知道"这个变量只读/量程多少"，数值输入控件就无法做越界校验。

**建议做法**：
- 新增独立的**元数据快照**下发路径（一次性、**替换**语义），与值增量分开。变量表改字段后，运行时重连必须拿到**新**元数据而不是沿用旧的（NEXT HMI 特意强调"重连不能保留上一代元数据"）。
- 订阅分级：现在 `VariableSubscriptionManager.EnsureGroupRunning` 只按 `CycleMs` 分组（见 [VariableSubscriptionManager.cs](file:///e:/doc/vs%20project/HMIStudio/src/HMI.Core.Services/VariableSubscriptionManager.cs#L87-L101)）。建议在"引用计数"之上加一层**优先级**：可见画面的变量 → 活跃轮询；后台常驻（`IsPermanent`）→ 低频轮询。

**优点**：① 数值输入/进度条/趋势的最小前置（量程、只读、单位）；② 分级订阅能在变量规模变大后显著降低 PLC 负载，且实现成本不高（现有分组机制可复用）；③ "元数据替换语义"能避免一类难查的 bug（改配置后运行端还按旧量程校验）。

**缺点**：① 元数据通道是新增契约，工作量 +；② 优先级分级需要"当前画面"信息，运行时侧要能上报画面切换（你已有 `ScreenNavigationService`，接入不难）。

#### B4 工程格式门禁（★★★，M0 落地）

**为什么必须提前决策**：B1 一旦改动绑定字段结构，工程文件就产生了**格式断点**。当前 `HmiProject.Version` 是字符串 `"1.0.0"`（[HmiProject.cs](file:///e:/doc/vs%20project/HMIStudio/src/HMI.Core.Model/HmiProject.cs#L19)），**不能**承担"格式门禁"职责 —— 语义版本是给人看的，格式版本是给程序判断的，二者混用会导致"版本号相同但结构不同"的隐性破坏。

**建议做法**：`HmiProject` 增加整数 `FormatVersion`（与字符串 `Version` 并存，分工明确，初值 1）；`LoadAsync` 判定"高于当前 → 拒绝"；`ValidateAsync` 只把它作为一条错误**报告**、不做拦截（与现有"只收集错误清单"的形态一致）。**按新项目口径不做迁移链、不做迁移前备份**，旧 `.hmijob` 作废。

**优点**：① 从此任何格式演进都有安全网（新版本程序对旧格式文件给出明确提示，而非抛看不懂的反序列化异常）；② `minAppVersion` 只展示不比较的设计很聪明 —— 旧版本程序能明确告诉用户"你需要 ≥X 版本"；③ 无迁移链负担，早期改动零成本。

**缺点**：① 只做"拒绝"不做"迁移"，意味格式升级后旧文件必须由用户重新组态（新项目阶段可接受）；② 早期工程少时容易觉得"过度设计" —— 但 B1 的改动会立刻让它变成刚需。

#### B7 字段注册表驱动编辑器 UI（★★★，落点 M3）

**为什么关键**：WPF 版 `MainWindow.xaml.cs` 已膨胀到 ~99KB，正是"UI 手写"的后果。NEXT HMI 的做法提供了**架构级**答案：不要让 UI 手写，让**注册表**生成 UI。由于编辑器要从零构建（结论 1），此项**应作为 `HMI.Web` 编辑器的初始设计**，而非事后重构。

**建议做法**：把"属性面板的每个字段"抽成数据：`字段名`、`显示名`、`类型`、`编辑器类型`、`校验规则`、`所属分组`。属性面板按注册表**生成**控件，而不是一列一列手写。这与 B1 的"字段类型决定可选源"是同一条链上的两件事。

**优点**：① 新增控件只需加注册表条目 + 渲染分支，编辑器侧零改动（工具箱列表一并受益）；② 从源头避免文件膨胀（WPF 版 99KB 的教训）；③ 为 M8 的外挂插件（自定义控件自带 schema → 自动出现在属性面板）铺路。

**缺点**：① 需要先设计元数据模型，前期投入可见；② 只有控件数量上来后才回本；③ 与 B1 耦合，建议 B1 → B7 顺序推进，不要并行。

### 5.3 借鉴点优先级结论

1. **M0 就该纳入**：B2（元数据快照 + 分级订阅）、B3（写结果枚举）—— 它们决定后续契约形状，后补成本高。
2. **M0 落地**：B4（格式门禁）—— 决策与落地都必须赶在 B1 改动绑定结构**之前**（M0 只做整数门禁，不做迁移链/备份）。
3. **M0 定抽象、M3 用起来**：B1 + B7（值源模型 + 注册表驱动 UI）—— 二者是一件事的两半。
4. **M4 必须遵守**：B5（定义态/运行态分文件）—— 报警确认状态若混写进工程定义文件，会污染评审与版本管理，事后拆分代价大。
5. **M3/M8 按需**：B6、B8、B9、B10。
6. **技术栈借鉴范围已放开**：因目标架构与服务端+浏览器同构（5.0），WebSocket 协议族、React 生态、编辑器交互模式均可参照；但 `$http` 源、manager 多进程反向代理等按 **T1 单机一体化**拓扑**不采纳**。
7. **KPHK 体系（见 5.4）**：K1（分发双车道）、K2（实时库无锁读）、K3（写门禁按来源）、K4（驱动契约收窄 + 块读前移）建议 **M0/M1 纳入**；K5（报警状态机 + 历史 SQLite）归 M4；**K6（报表定义）归 M8、K7（控件预设样式，与 B9 合并）落 M3、K8（单向/双向绑定）M0 预留字段、K9（启动配置 + 退出收尾）归 M4**；其余次级项按落点分散到 M2/M3/M6/M8。

### 5.4 对标二：鲲鹏恒控 KPHK（工业 HMI/SCADA 组态软件）

参考来源：`https://kphk.top/docs`（**程序集使用手册 21 章 + 界面使用手册 9 章**，均为在线正文实抓）。

- **技术路线（与我方同构）**：KPHK = 「WPF 壳 + WebView2（虚拟域名映射 `hmi.app.invalid`，不占端口）+ 无头 .NET 内核独立 exe `Hmi.RuntimeCore.Host`（stdio 通信，stdout 打一行 `RUNTIMECORE READY` + 每秒 `HEARTBEAT`，日志走 stderr）」；**编辑器与运行时共用同一套渲染器**。与我方「HMI.Server（无头内核）+ HMI.Web（浏览器渲染）」同构，故**机制层可直接借鉴**，仅需按我方 T1 单机拓扑与 React/TS 前端调整形态。
- **未取得 / 未确认（不纳入，缺证据不下结论）**：①「64 点一屏」具体数字（仅首页宣传语，手册正文未见）；② 脚本热重载（仅见 `ClearCache()`，无运行时热重载）；③ 多选批量改同一属性（界面手册仅确认多选后对齐/分布/图层）。
- **已确认（2026-10-12 修正）**：运行包"内存加载不落盘"机制——界面手册 07 章已明确：运行封包 `.hmipkg` 为 **AES 加密**（口令模式真加密、不暴露源码），运行时**解密后内存加载、不落盘**；与工程包 `.hmiproj`（可再编辑的 zip）用途区分。原「仅提名称」表述作废。
- **我方已领先于 KPHK 的三点**（不必抄，反向印证我方设计）：① **定义态/状态分文件**（我方 B5）——KPHK 程序集手册 06 章未见分区，`LoadedProject` 为统一可变容器；② **坏标签熔断**（我方 §14.3 `MaxFailCount`/`FuseCooldownMs`）——KPHK 仅有退避重连（`BackoffPolicy` 1→30s），无熔断；③ **格式版本门禁**（我方 B4）——KPHK 手册**未提及**版本门禁。

**借鉴点总表（K1–K9）**

| 编号 | 借鉴点 | KPHK 做法（来源章） | HMIStudio 现状（代码证据） | 落点 | 建议强度 |
|---|---|---|---|---|---|
| K1 | **分发双车道** Droppable / Lossless | 程序集 02/20：界面订阅走 `Droppable`（堵了就丢旧保新），报警/历史订阅走 `Lossless`（一条不丢） | `VariableSubscriptionManager` 仅按 `CycleMs` 分组轮询 + `DebounceMs` 防抖（见 [VariableSubscriptionManager.cs](file:///e:/doc/vs%20project/HMIStudio/src/HMI.Core.Services/VariableSubscriptionManager.cs#L87-L101)），**无"可丢/不可丢"通道概念** | M0 定契约 / M2 用 | ★★★ |
| K2 | **实时库无锁读**：点 Id 作连续数组下标 + seqlock 快照 | 程序集 04：读走 `seqlock` 乐观读、无锁；写分 `UpdateFromDriver`/`UpdateNonScalar`/`SetQuality`，返回 bool=被死区拦截；非标量写入后视为只读（禁原地改防撕裂） | `MemoryVariablePool` 现为细粒度变量锁 + 全量串行 FIFO 写队列，点值以字典存取（**非数组下标**） | M1 | ★★★ |
| K3 | **写门禁按"来源"分级**（RuntimeWriteGate） | 程序集 02/12/15：脚本**只能写内部变量**，写采集点抛异常；界面按钮可写；自动化 API / MCP **完全不碰运行态写值** | 我方 B3 已定 `IsReadOnly` 写拦截 + reason code（`Ok`/`NotExist`/`ReadOnly`/`DriverError`/`Cancelled`），但**无"调用来源"维度** | M0（在 B3 上加 `WriteSource`）/ M2 | ★★★ |
| K4 | **驱动契约收窄 + 失败不抛 + 块读规划** | 程序集 07：`IProtocolDriver` 仅 6 方法，调度/重连/拆包/写库归框架；读失败返 `Failure` **不抛异常**、只置质量 `CommFailure`；`BlockPlanner` 三约束切块（单次上限 / 地址连续 / `GapTolerance=8`）；`BackoffPolicy` 指数退避 1→30s | `StubPlcDriver.ConnectAsync` 恒 false；§14.1「批量读分帧」原排 M7 | **M1 前移**（Mock 也按块读）/ M7 | ★★★ |
| K5 | **报警状态机 + 历史 SQLite 定型** | 程序集 14：报警四限值 `HiHi/Hi/Lo/LoLo` + **回差（防抖）+ 延时（防毛刺）**，状态机 `Normal→ActiveUnacked→ActiveAcked→ClearedUnacked→Normal`，`Key` 为身份去重、`Ack`（人确认）≠`Clear`（恢复）；历史 SQLite 单表 `hist(tag_id,ts_utc,val,q)` WAL、攒批 1000 条/1 秒、`ROW_NUMBER` **SQL 内降采样**、`RetentionDays` 扫描 + VACUUM | M4 报警/历史均未实现；本地库**已定 SQLite**（2026-10-11，见第四节第 2 项） | M4 / M8 | ★★★ |
| K6 | **报表定义**（组态期定义、运行期出表） | 界面手册 07：工程树「其他配置→报表」为唯一入口；两种数据源——**历史聚合**（选点 + 聚合列 Avg/Max/Min/Sum/Count，每点一行）与**报警事件**（按最低级别/分组过滤 + 最多行数）；运行期查库出表、导 CSV | 报表模块**未实现**（`Misc.cs` 仅接口声明，无报表定义与数据源） | M8 | ★★ |
| K7 | **控件预设样式体系**（与 B9 合并） | 界面手册 04：按钮 9 风格 × 任意配色、指示灯 4 形状 × 6 风格 = 24 套、棒图 2 套 + 自定义 SVG 皮肤 + 模板配色 9 语义色。四条机制：**只管风格不含颜色** / **样式是引用（存 `StylePreset` 名）、外观实时推导** / **元素属性优先、预设兜底** / **"定义自身那一维"套用时写进元素属性** | 原 Studio 控件样式硬编码、四组最近色；**无预设样式 / 皮肤 / 语义色**体系（已随项目删除） | M3（与 B9 合并） | ★★★ |
| K8 | **单向 `→` / 双向 `⇄` 绑定** | 界面手册 04：交互控件（滑块/输入框/复选框）绑定时问单向还是双向——单向只把数据显示到控件、双向控件改值写回数据源；纯显示属性（文本/填充/填充比例）只能单向；绑定后属性行标 `⇄`/`→` | 我方绑定为**单向读**（§7.4「变量 → 控件外观（单向读）」），**无方向字段** | M0 **预留字段** / M3 用起来 | ★★★ |
| K9 | **启动配置 + 退出收尾** | 界面手册 07：启动配置（启动进哪个画面 + 启动时按序运行全局子程序 + **退出/关机前运行收尾子程序**）；原话警示"退出不跑收尾=设备没复位就断电" | 动作引擎**无子程序概念**；`HostLifetime` 已实现但当前**无宿主消费**（M2 起由 `HMI.Server` 消费） | M4 | ★★ |

**次级借鉴项（暂不单独立条，落点见括号）**
- **设备参数表单数据驱动**：实现 `IConnectionSchemaProvider` → 前端自动长参数表单，加协议前端零改动（M3，与 B7 同源强化）。
- **工程文件源抽象**：工程=目录分文件 JSON（一画面一文件/一模块一文件）+ `IProjectFileSource`(Directory/Zip) 抽象 + 加密包 AES-GCM **内存解密不落盘**（M6）。
- **脚本引擎流水线**：黑名单安全分析（禁 `System.IO/Net/Reflection/Threading/Interop`）→ Roslyn 编译 → 按"名+哈希"缓存 → 协作式超时 5s；结果**五态不抛**（`Ok/CompileError/Blocked/RuntimeError/Timeout`）；`TickGate` 丢拍不堆积；**全部脚本同一信号量串行**（M8，正好为我方 `IScriptEngine` 定契约）。
- **无头内核 + Supervisor**：`Hmi.Supervisor` 的 `ReadyMarker/AutoRestart/MaxRestarts`、`IsStalled` 判活、`Kill(entireProcessTree:true)`、stdin EOF 自退（M2，`HMI.Server` 宿主模型）。
- **自动化 API 统一多出口**：界面/REST/MCP/外部程序**共用同一套方法**，`AutomationResult{Ok,Message,Data}` 失败不抛，**只碰磁盘工程、不碰运行态写值**（M2/M8）。
- **MCP 服务器=Automation 薄包装**：独立 exe，stdio/http + token；**刻意无运行态写值工具**（M8）。
- **权限模型**：账号→角色（带等级）→8 权限位 + **等级约束"只能碰比自己低的"** + `configMode` 编辑态全放行/运行态真查 + PBKDF2-HMAC-SHA256 10 万次+盐+`FixedTimeEquals` + 统一回话防账号枚举（M8）。
- **界面层**（M3）：属性面板**全平铺**且"属性即数据皆可被 AI 操作"；**三段式绑定选择器**（范围→项→属性）全鼠标点选；按钮**动作类型化**（写点位/切画面/确认弹框/执行脚本/弹登录）；点表**引导式建点 + 寄存器四区地址向导**（[0区]DO/[1区]DI/[3区]AI/[4区]HR，类型↔区联动，只存一个 `address` 字段）；大点表**窗口化 + 只订阅可见行**；"刷新当前文档"只重挂当前标签；**预览/运行双路径**（进程内预览 vs 独立带登录门禁运行）；AI 助手"实时跟踪回放"+"完全自主"开关（默认关）。

**KPHK 的坑（当警示，来源程序集手册 21 章）**
1. **"同一能力多出口"漏改静默不生效**（KPHK 自认最常见坑）→ 印证我方 M0「契约统一、唯一入口」的必要性。
2. **RPC 命名顶层/嵌套不一致 → 静默返回空对象且 `ok:true`** → 协议层须严格 schema 校验。
3. **运行态忘关 `configMode` → 操作工能改一切** → 权限门禁须 fail-safe 默认关。
4. **插件 `AssemblyLoadContext` 不 `Unload` → 不能热重载** → 我方插件热重载不可想当然。
5. **脚本超时非强杀（僵尸任务仍在跑）** → 超时语义须写清。

### 5.5 借鉴点统一索引（取长补短）

> 本节把两个来源（NEXT HMI 的 B1–B10、KPHK 的 K1–K5）合并成一张表，作为"取长补短"的集中体现。`落点`列与第二节各里程碑的「借鉴落点」一一对应；`与另一来源的关系`列说明二者在同一问题上是否重叠、互补，或仅此一家独有。

| 来源 | 编号 | 借鉴点 | 落点 | 与另一来源的关系（重叠/互补/仅此一家） |
|---|---|---|---|---|
| NEXT HMI | B1 | 属性值 = 值源 Source 模型，**字段类型决定可选源** | M0 定抽象 / M3 用起来 | 仅此一家（值源抽象仅 NEXT 提供；KPHK 无对应） |
| NEXT HMI | B2 | 元数据快照与值增量分离 + 按可见性分级订阅 | M0 定契约 / M1 用 | 互补：与 K1 同为**分发/订阅层**——B2 管元数据与优先级，K1 管可丢/不可丢 |
| NEXT HMI | B3 | 写结果 reason code 稳定契约 + `requestId` 回执 | M0 | 互补：与 K3 同属**写路径**——B3 定结果契约，K3 加来源维度 |
| NEXT HMI | B4 | 工程格式整数门禁（仅取门禁，不取迁移链/备份） | M0 落地 / M6 发布 | 仅此一家（KPHK 未提及版本门禁，见 5.4） |
| NEXT HMI | B5 | 配置(定义)与运行时状态分文件存放 | M4 | 互补：与 K5 同属**报警/历史层**——B5 管定义态/状态态分文件，K5 管报警状态机与历史库 |
| NEXT HMI | B6 | 增量保存 + 撤销栈节流合并 | M3 | 仅此一家（KPHK 未见增量保存/撤销节流） |
| NEXT HMI | B7 | 编辑器 UI 由字段注册表驱动生成 | M3 | 互补：与 KPHK「设备参数表单数据驱动」同源强化，均属 UI 数据驱动 |
| NEXT HMI | B8 | 编译失败保留上一版产物 | M8 | 仅此一家（KPHK 未见） |
| NEXT HMI | B9 | 状态色 vs 身份色、结构常量 vs 可编辑令牌分离 | M3 / M8 | **已与 K7 合并**：B9 定令牌分离原则，K7（KPHK 预设样式）给组件外观落地形态 |
| NEXT HMI | B10 | 「编辑器访问 = 代码执行」威胁模型必须明说 | M3 / M8 | 互补：与 KPHK 坑 2/3（schema 校验、权限 fail-safe）同属**安全模型**，B10 明说威胁、KPHK 给反面案例 |
| KPHK | K1 | 分发双车道 Droppable / Lossless | M0 定契约 / M2 落地 | 互补：与 B2 同为分发/订阅层（B2 管元数据与优先级、K1 管可丢/不可丢） |
| KPHK | K2 | 实时库无锁读：点 Id 连续数组下标 + seqlock | M1 | 仅此一家（实时库无锁读仅 KPHK 提供；B2 在订阅层，不同层） |
| KPHK | K3 | 写门禁按"来源"分级（RuntimeWriteGate） | M0（在 B3 上加 `WriteSource`）/ M2 | 互补：与 B3 同为写路径（B3 定结果契约、K3 加来源维度） |
| KPHK | K4 | 驱动契约收窄为 6 方法 + 读失败不抛只置 `CommFailure` + `BlockPlanner` 块读 | M1 前移 / M7 真机 | 仅此一家（驱动契约与块读规划仅 KPHK 提供；与我方 §14.1/§14.3 互为印证） |
| KPHK | K5 | 报警状态机 + 历史 SQLite 定型（WAL/攒批/降采样/Retention） | M4 / M8 | 互补：与 B5 同为报警/历史层（B5 管分文件、K5 管状态机与历史库） |
| KPHK | K6 | 报表定义（历史聚合 + 报警事件，运行期出表/导 CSV） | M8 | 仅此一家（NEXT HMI 未见报表组态） |
| KPHK | K7 | 控件预设样式体系（风格×配色 + SVG 皮肤 + 模板配色） | M3（与 B9 合并） | **互补并已合并**：与 B9 同属外观/令牌层——B9 管令牌语义、K7 管组件预设落地 |
| KPHK | K8 | 单向 `→` / 双向 `⇄` 绑定 | M0 预留 / M3 用 | 互补：与 B1 值源抽象**同批定**——B1 管"值与类型"、K8 管"绑定方向"；写回走 B3/K3 写路径 |
| KPHK | K9 | 启动配置 + 退出收尾子程序 | M4 | 仅此一家（NEXT HMI 未见启动/退出子程序） |

---

## 六、目标架构分析：从「双 WPF 程序」到「服务端 + 浏览器」

> 本节为分析文档，**不含任何代码改动**。所有"现状"结论均来自对本仓库真实代码的逐文件核对；所有"外部事实"均标注来源。
> 里程碑落地路径见第二节 M0–M8。

### 6.0 已拍板的决策（用户确认）

| # | 决策项 | 结论 | 影响 |
|---|---|---|---|
| D1 | 自定义控件扩展方式 | **`.tsx` 热编译** | 需在服务端内置 JS 构建能力；接受"任意 JS 在操作员浏览器执行"的安全模型 |
| D2 | 前端技术选型 | **React + TypeScript**（见 6.7） | 引入前端构建工具链；`.tsx` 为原生形态 |
| D3 | 授权模式 | **保持闭源，不用加密狗** | 前端须压缩/混淆发布；不部署硬件加密狗 |
| D4 | 采集服务部署位置 | **T1 单机一体化**（见 6.6） | 采集/视觉/UI 服务/工程库同机；200ms 闭环天然本地 |
| D5 | 切入方式 | **接受「M0 Core 契约扩展先行」** | 契约先行、可增量；按新项目口径不背负旧格式兼容，WPF 版已删除 |

### 6.1 目标形态

现状（[HMIStudio.sln](file:///e:/doc/vs%20project/HMIStudio/HMIStudio.sln) 3 个项目）：

```
HMI.Core.Interface → HMI.Core.Model → HMI.Core.Services
// HMI.Studio / HMI.Runtime 两个 WPF 项目已于 2026-10-11 删除
```

目标形态：

```
HMI.Core.Interface → HMI.Core.Model → HMI.Core.Services
                                          ├→ HMI.Server    (ASP.NET Core: REST CRUD + WebSocket 值推送 + 采集/视觉宿主)
                                          └→ HMI.Web       (浏览器前端: 编辑器 + 运行时渲染, 共享同一份渲染代码)
```

### 6.2 七个诉求的落点核对（关键：只有 1 项是纯 UI）

> 「现状」列与 5.1 总表同源，此处仅列结论。

| 用户诉求 | 落点层级 | 性质 |
|---|---|---|
| 浏览器编辑器 + 实时预览 | 全新前端 + 服务端协议层 | ✅ 纯 UI |
| 页面/数据源/报警/翻译/用户管理 全在 `/editor/` | 前端 + 4 个新模块 | ⚠️ UI + 新增 |
| 27 种可组合属性源（`$var`/`$if`/`$switch`/`$componentProp`/`$widgetProp`…） | **Core.Interface 契约** | ❌ 要动 Core（B1 + 组件体系，**已并入 M0**） |
| viewport-aware 订阅优先级 | **Core.Services** | ❌ 要动 Core（B2） |
| JSON/CSV Git 原生工程 | **Core.Model + Core.Services** | ❌ 要动 Core（B4） |
| 历史库/报警确认/配方/权限 | **Core.Services 新模块** | ❌ 新增模块（B5） |
| `.tsx` 热编译自定义控件 | 插件体系 | ❌ 安全决策（已定 D1） |

**结论**：这不是"UI 重写"，准确表述是 **UI 重写 + Core 契约扩展 + 4 个新模块**（按新项目口径，旧工程格式不迁移）。

### 6.3 一个必须点破的连带后果：`RuntimeRenderer` 也作废

"实时预览"的前提是**浏览器能渲染控件**。若预览在浏览器、运行在 WPF，则必然维护**两套渲染实现**并随时间漂移。NEXT HMI 正是靠"编辑器与运行时共享同一份前端渲染代码"消除此问题。

| 项目 | 命运 | 依据 |
|---|---|---|
| `HMI.Core.Interface` | ✅ 复用（零第三方包） | csproj 无任何 PackageReference |
| `HMI.Core.Model` | ✅ 复用（零第三方包） | csproj 无任何 PackageReference |
| `HMI.Core.Services` | ✅ 复用（零第三方包） | csproj 仅引用 Interface/Model，纯 BCL 实现 |
| `HMI.Studio` | ❌ **已删除（2026-10-11）** | 原为 `net8.0-windows` + `UseWPF` + `UseWindowsForms` |
| `HMI.Runtime` | ❌ **已删除（2026-10-11）** | 原为 `net8.0-windows` + `UseWPF`；渲染全在 `RuntimeRenderer.cs`（删除前仅处理 `"Text"`/`"Button"`） |

### 6.4 跨平台能力核实（外部事实 + 来源）

| 组件 | 平台支持 | 结论 |
|---|---|---|
| 浏览器前端 | 任意 OS | ✅ |
| .NET 8 后端 | Windows / Linux / macOS | ✅ |
| 海康 MVS V5.1.0（Linux） | **Ubuntu 16.04–24.04（x86/x86_64/aarch64）、CentOS 7、RedHat 7、NVIDIA Jetson TX2** | ✅ **官方支持，采集侧可跨平台** |
| 工业相机 SDK 4.8.2.2 | 支持 GigE Vision | ✅ |
| OpenCvSharp4 | 授权 **Apache-2.0**；原生包含 `runtime.win`、`runtime.ubuntu.18.04-x64` | ✅ 闭源商用安全 |
| EmguCV | 授权 **GPLv3 或付费商业授权** | ❌ 闭源商用须付费，**排除** |

**结论：跨平台诉求可由现有技术栈完整满足，不需要换 Python。**

⚠️ 部署复杂度提示（非技术障碍）：Linux 下跑 GigE 需网卡调优，MVS 安装包自带 `set_socket_buffer_size.sh`、`set_rp_filter.sh` 脚本。

### 6.5 海康 GigE 对契约的硬要求

核对 [Camera.cs](file:///e:/doc/vs%20project/HMIStudio/src/HMI.Core.Interface/Camera.cs) 全文：

| 契约 | 现有 | GigE 实际需要 |
|---|---|---|
| `CameraFrame` | `Channel` / `Data` / `Timestamp` | **缺 `Width`/`Height`/`PixelFormat`/`Stride`** —— 缺则无法构造 `Mat`，OpenCV 根本无法处理 |
| `ICameraDevice` | `FrameReceived` / `StartAsync` / `StopAsync` | 缺曝光/增益/**触发模式（软/硬触发）**/ROI/帧率/包大小 |
| `IVisionProcessor` | **不存在** | ROI、算法、OK/NG 判定、耗时 |
| 方案 §12 声称的 `CameraDeviceConfig`/`CameraOpenResult`/`CameraCaptureResult`/`CameraStateArgs`/`CameraConfigItemMeta` | **grep 全库零匹配** | 全部需新增 |

**不可含糊的一条：采集 ≠ 处理，不可互替。** 采集必须走海康 MVS SDK（GigE Vision 协议，OpenCV `VideoCapture` 接不了工业 GigE）；处理走 OpenCvSharp。

### 6.6 采集服务部署位置：工控机 vs 服务器

| 维度 | 工控机（设备侧） | 服务器（机房/控制室） |
|---|---|---|
| 网络路径 / 延迟确定性 | 与 PLC/相机同网段直连，**延迟低且抖动小** | 跨网段/经交换机，**抖动大** |
| PLC 协议可达性 | S7/汇川等常只允许同网段或直连，**天然可达** | 需路由/网关/端口映射，常受限 |
| 相机带宽 | 多网口直连，相机可走独立网段，**不与管理流量争抢** | GigE 满帧率可达 100–1000 Mbps/路，多路将打满网段，需万兆/专线 |
| 可用性 | 随产线停机；分散 | **可集中冗余、集中运维** |
| 升级运维 | 分散升级，成本高 | **集中升级，成本低** |
| 环境 | 车间高温/粉尘/电磁干扰，需宽温/无风扇 | 机房环境，标准机架 |
| 成本 | 单台成本高（工业级） | 单台成本低，但需网络改造 |

**分析结论**：采集服务应部署在**工控机（设备侧）**，因为 PLC 协议与 GigE 相机都是"近距离、低延迟、高带宽"特性，且 200ms 闭环要求确定性。

三种拓扑：

| 方案 | 拓扑 | 适用 |
|---|---|---|
| T1 单机一体化 | 工控机同时跑 采集 + UI 服务 + 工程/历史库；浏览器经管理网访问 | 单机台、试点、演示 |
| T2 采集下沉 + 中心服务 | 工控机只跑采集/视觉/闭环；中心服务器跑 UI 服务 + 工程库 + 历史库 + 权限；两者经管理网互通 | 产线级 |
| T3 多采集节点 | 多台工控机（每台管一段设备）+ 中心服务 | 多产线/多车间 |

**已定 D4 = T1 单机一体化**（上文分析推荐 T2，但按当前规模决定取 T1）：采集、视觉、UI 服务、工程库/历史库同机部署；200ms 闭环天然本地，部署与运维最简。

**一条设计约束（即便走 T1 也必须遵守）**：代码结构上须保持"采集/视觉模块"与"UI 服务模块"**边界清晰**（依赖单向、经接口通信）。这样未来业务量上来后，仅改部署即可平滑演进到 T2（此时 200ms 闭环须完整落在采集节点内部，不得穿越网络），不需要改架构。

### 6.7 前端技术选型（回应 D2）

> 评判口径：**只按"对本项目更优"判定，不考虑现有团队经验。**

| 评判维度 | 权重 | React + TS | Vue 3 + TS | 依据 |
|---|---|---|---|---|
| `.tsx` 热编译（D1） | **高** | ✅ 原生一等形态，esbuild/swc 开箱支持 | ⚠️ 需 `@vitejs/plugin-vue-jsx`，属非惯用形态（Vue 惯用 `.vue` SFC） | D1 已定 `.tsx`，此项近乎直接决定 |
| 重图形（Canvas/WebGL）生态 | **高** | ✅ react-konva / PixiJS / react-flow / visx / deck.gl 等最全 | ⚠️ 同类库数量与维护度明显弱 | 对应"上千图元"风险 |
| 高频实时绑定默认表现 | 中 | ⚠️ 需外部 store + 选择器订阅，避免整树重渲染 | ✅ 代理响应式，细粒度默认更好 | React 可经刻意设计达成同等效果 |
| 生态规模 / 长期维护 / 可招聘 | 中 | ✅ 最大 | ⚠️ 次之 | 闭源长周期产品更看重 |
| 同域参考实现 | 低 | ✅ NEXT HMI 即 React | — | 有同类落地先例 |

**推荐：React + TypeScript。** 判定落在两项高权重维度：① D1 选的 `.tsx` 正是 React 的原生形态；② "上千图元"的图形风险依赖 Canvas/WebGL 生态，而该生态 React 明显更全。Vue 仅在"默认细粒度响应式"上占优，且该项 React 可通过外部 store + 选择器订阅达到同等效果。

**必须一并解决的工程约束（不可省）**：React 的高频更新路径必须刻意设计 —— 高频变量**不得**走组件重渲染，应采用"外部 store（选择器订阅）"或"Canvas/WebGL 直接绘制绕过 React re-render"。此项须在原型中一并验证，否则 React 的默认表现会成为瓶颈。

**待验证项（无实证，须实测，已确认安排）**：工业画面在上千图元下，**SVG 与 Canvas 的基准** + **上述高频响应式方案**，合并为一次原型实测。本节不下结论。

### 6.8 关键冲突与处置（冲突 1/3 待解，冲突 2 已决）

**冲突 1：`.tsx` 热编译 与 安全模型**（已定 D1 = 热编译）
- 你方案 §2 原设计是"插件 DLL + 加载前 SHA256 白名单校验"；热编译与之**互斥**，已按 D1 放弃白名单。
- 连带后果（须接受）：① 服务端需内置 JS 构建能力（建议 `esbuild` 单文件二进制，跨平台）；② 任意 JS 会在**所有操作员浏览器**执行 —— 这正是 NEXT HMI README 自己声明的威胁模型（B10）；③ 与 D3 闭源结合后，此能力应**默认关闭**，仅对受信工程/内部开发开启。

**冲突 2：200ms 闭环的部署位置**（用户已确认闭环不进浏览器 ✅）
- 采集/视觉/PLC 回写的闭环必须落在采集侧本地（T1 下同机，T2 下在采集节点内部），不得穿越网络。见 6.6。

**冲突 3：GigE 满帧率不能向浏览器推原图**
- 满帧率原图走 WebSocket 会打爆带宽。**必须在采集侧降采样/只推预览帧**，原图仅在服务端本地供视觉处理。此即 NEXT HMI viewport-aware 设计的真正动机（B2）。

### 6.9 代价与风险

**代价**
1. 重写前端编辑器（页面树/属性面板/实时预览）—— 最大工作量
2. 重写控件渲染（浏览器版）—— 损失小，现仅 Text/Button
3. 新增 `HMI.Server` 宿主 + 协议层（REST CRUD + WebSocket 值推送）
4. Core 契约扩展（见 M0 五项）
5. 工程格式改造：**单文件 `hmijob`（现已是 System.Text.Json 文本，非二进制）** → JSON/CSV 目录拆分（**新项目口径，无迁移负担**，与格式门禁同步做）
6. 新增 4 模块：历史库、报警确认、配方、权限
7. 相机采集与视觉（海康 MVS SDK + OpenCvSharp，见 M5）

**风险**
1. **渲染性能**：浏览器端 SVG/DOM 承载上千图元的表现未验证（6.7 待验证项）
2. **双实现漂移**：WPF→浏览器为全新实现，需单测/夹具防漂移
3. **热编译安全**（D1）：任意 JS 在操作员浏览器执行，须默认关闭并限受信工程
4. **闭源保护**（D3）：前端须压缩/混淆；`.NET` 端可评估混淆/AOT
5. **Linux + GigE** 网卡调优的实施复杂度

---

*本文档由现有代码逐文件核对后生成，未做任何代码改动。*
*第五、六节为外部参考与架构分析，其"现状"列均核对了本仓库真实代码，外部引文均来自真实抓取的 NEXT HMI 文档与海康/OpenCvSharp/EmguCV 官方来源。*

---

## 七、需求规格（承接原《HMI组态框架总体技术方案》V1.3 有效部分）

> 本节整理自原 `HMI组态框架总体技术方案_V1.3.md`（842 行 / 15 章），只归档其中**仍然有效**的需求：设计原则、整体架构与模块职责、变量系统、控件属性绑定、输入事件、动作脚本引擎、表格控件体系、P0/P1/P2 业务模块、全局架构约束，以及底层通讯 / 变量读写队列 / 防抖 / 相机多路等补充设计，和设计器界面中「要有什么菜单 / 控件 / 快捷键 / 停靠规则」的功能性需求。
> **取舍口径**：有效的功能需求原样保留（不改技术含义、不补内容）；随旧架构一并作废的内容全部丢弃 —— 含 **WPF 窗口 / 工具栏 / 资源树 / 输出面板等实现细节**、**插件 DLL + SHA256 哈希白名单**、**`CoolDownMs` 写后冷却机制（仅冷却，不含熔断）**，以及 **§11 接口清单 / §12 数据模型清单 / §14.7 接口原型附录**（与 M0 尚未定稿的新契约冲突）。**保留待实现**：坏标签熔断（§14.3 的 `MaxFailCount`/`FuseCooldownMs`）与写语义 `Coalesce` 合并（§14.2），二者均落 **M7**（口径见第四节与 7.18）。
> 原 `HMI组态框架总体技术方案_V1.3.md` 已删除，其有效需求以本节为准；正文中的「§X.Y」引用均指向**原方案章节号**，可按下表换算为本节编号。

### 溯源映射表

| 新编号 | 原章节 |
|---|---|
| 7.1 | 原 §1.2 |
| 7.2 | 原 §2（§2.1 / §2.2 / §2.3） |
| 7.3 | 原 §3（§3.1–§3.8） |
| 7.4 | 原 §4（§4.1–§4.5） |
| 7.5 | 原 §5（§5.1–§5.4） |
| 7.6 | 原 §6（§6.1–§6.3） |
| 7.7 | 原 §7（§7.1–§7.5） |
| 7.8 | 原 §8.1 |
| 7.9 | 原 §8.2 |
| 7.10 | 原 §8.3 |
| 7.11 | 原 §9（§9.1–§9.4） |
| 7.12 | 原 §10（§10.1–§10.3） |
| 7.13 | 原 §13（§13.1–§13.4） |
| 7.14 | 原 §14.1 |
| 7.15 | 原 §14.4 |
| 7.16 | 原 §14.5 |
| 7.17 | 原 §14.6 |
| 7.18 | 原 §14.2 |
| 7.19 | 原 §15.2 |
| 7.20 | 原 §15.4 |
| 7.21 | 原 §15.5 |
| 7.22 | 原 §15.6 |
| 7.23 | 原 §15.8 |
| 7.24 | 原 §15.10 |

> 说明：7.14–7.18 按已确认的保留清单顺序排列，故「原 §14.6」排在「原 §14.2」之前。

> 说明：原 §14.3 的坏标签熔断（`MaxFailCount`/`FuseCooldownMs`）**保留并在 M7 落地**，不单列 7.x 编号（任务正文见 M7）；原 §14.3 的冷却时间（`CoolDownMs`）部分随字段删除而丢弃。原 §14.2 的写语义 `Coalesce` 合并保留为「暂不实现、留待 M7」（见 7.18）。

> **接口与数据模型占位说明**：本文档原 §11 核心接口清单 / §12 核心数据模型清单 / §14.7 接口原型附录已随旧架构一并移除；接口与数据模型的权威定义以 **M0 Core 契约扩展**的产出为准，不在此重复。

> **其余已移除内容（便于溯源）**：原 §3.9（`CoolDownMs` 冷却 / 熔断 / 防抖新增）与 §14.3（冷却时间与熔断）中的 **冷却时间（`CoolDownMs`）机制**因字段已删除而丢弃（其中 `DebounceMs` 防抖有效，见 7.15）；**§14.3 的坏标签熔断保留，落 M7**；原 §15.1 主窗口布局结构、§15.3 左侧树形导航栏、§15.7 底部输出面板、§15.9 落地口径、§15.11 工具栏为纯 WPF 窗口结构，一并丢弃。正文中的「§15.9」等引用即指向以上已移除内容。

---

### 7.1 设计原则（原 §1.2）

- **积木化、模块化**：模块之间仅通过接口与消息总线通信，低耦合，可自由拆卸。
- **按需集成、精简能力**：只保留专用设备必需功能，砍掉通用组态的冗余复杂能力。
- **全异步**：所有 IO、通讯、相机、动作执行均采用 async/await，不暴露阻塞同步方法。
- **设计时与运行时分离**：组态编辑模型与运行时渲染模型完全分离，序列化只保存自定义模型。
- **资源可控**：所有后台任务支持取消令牌、超时机制，防止死循环与内存泄漏。

> 原「插件化：相机、PLC 驱动、自定义动作均为独立 DLL，加载前做 SHA256 哈希白名单校验」一条已被新架构决策推翻（改为 `.tsx` 热编译），故作废、不再归档。

---

### 7.2 整体架构总述（原 §2）

整体采用分层架构：**公共接口层 + 数据模型层**为所有模块的依赖底座；宿主作为容器，通过 `IHostLifetime` 统一管理模块启停顺序（接口与数据模型定义见本节末占位说明）。

**模块清单（原 §2.1）**

| 层级 | 模块 | 职责 |
|---|---|---|
| 公共底座 | 接口层 + 模型层 | 所有模块依赖，无业务实现 |
| 宿主层 | HMI.Server 宿主 + HMI.Web 前端 | 窗口宿主、模块加载、模式切换、全局异常捕获 |
| 核心服务 | 变量池 / 订阅管理器 | 变量读写、细粒度锁、分组轮询、引用计数；读写排队与防抖分发（见 §14） |
| 核心服务 | 表达式引擎 | 条件求值、属性绑定表达式解析 |
| 核心服务 | 消息总线 | 全局事件发布订阅 |
| 核心服务 | 动作引擎 | 动作链执行、超时、异步、失败策略 |
| 插件 | PLC 驱动 | 多协议 PLC 通讯（S7 / 汇川 EIP(CIP) / 汇川 ModbusTCP，见 §14.1），插件化 |
| 插件 | 相机 | 相机采集、图像回调推送，独立于前端（单路/多路，见 §14.5） |
| 插件 | 自定义动作 | 复杂业务逻辑扩展 |
| 前端 | 画布编辑器（设计时） | 拖拽控件、属性面板、动作脚本编辑 |
| 前端 | 画布渲染器（运行时） | 加载画面模型、绑定变量、响应消息 |
| 业务 | 报警管理 | 报警规则、状态机、确认、历史归档 |
| 业务 | 趋势曲线 + 历史存储 | 实时曲线、历史回放 |
| 业务 | 配方管理 | 配方保存/下载 |
| 业务 | 日志 / 诊断 / 权限 | 运行支撑 |

> 说明：原表「宿主层 = 主框架 WPF 程序」已改写为「HMI.Server 宿主 + HMI.Web 前端」；「插件 = PLC 驱动 DLL / 相机 DLL / 自定义动作 DLL」的 DLL 形态名已去掉（列为「插件」层级）；职责列除剥离已作废的实现形态词（`SHA256 校验`、`冷却`、`独立于 WPF`）外未作改动。

**模块通信规则（原 §2.2）**

- 模块之间禁止直接引用彼此项目，只依赖公共接口。
- 同步调用走接口方法；异步通知走消息总线发布订阅。
- 移除任意非 P0 模块后，P0 骨架仍可正常运行。

**设计时 / 运行时隔离要求（原 §2.3）**

- 设计器编辑模式下不执行任何 PLC 读写、相机采集、脚本运算。
- 运行时加载工程时，加载全部变量配置，初始化变量池；内存变量赋予初始值，PLC 变量、相机状态变量尝试读取一次。
- 画布所有控件 UI 刷新全部采用异步机制，防止主线程阻塞。

---

### 7.3 变量系统（原 §3）

**独立刷新周期（原 §3.1）**
每一个 PLC 变量可独立配置刷新周期（如 50ms、100ms、500ms、1000ms），变量元数据中保存 `CycleMs` 字段。

**画面按需激活刷新（原 §3.2）**
- 画面激活：扫描画面内所有控件绑定的变量，引用计数 +1，变量进入周期采集。
- 画面切离：引用计数 -1；计数归零后停止该变量 PLC 读取。
- 多画面共用同一变量时，引用计数机制保证不会因切走其中一个画面而误停采集。

**永久常驻变量（原 §3.3）**
变量元数据中 `IsPermanent = true` 时，不参与画面引用计数，无论哪个画面打开都按自身周期持续采集。用于急停、故障、总报警等核心安全信号。

**读写分离（原 §3.4）**
- **读**：受画面订阅、独立周期控制。
- **写**：按钮/脚本/动作发起的变量写入即时下发，不受订阅状态限制；所有写统一进入**全量串行写队列**，保序、保重试（详见 §14.2）。

**底层报文聚合（原 §3.5）**
用户层面每个变量独立配置周期；框架底层自动将同 PLC 站点、连续地址、周期相近的变量合并为批量读请求，避免单变量单发报文风暴。批量读策略按协议优化：CIP 一次往返、ModbusTCP 连续寄存器区间、S7 按 DB 连续区（详见 §14.1）。

**细粒度变量锁（原 §3.6）**
采用每个变量实例独立锁对象（细粒度锁），不使用全局变量池锁。不同变量并发读写互不阻塞；变量池的增删操作才使用全局锁。
- 多个脚本并发修改同一变量依靠细粒度锁互斥；同一变量同时读写并发，同样受细粒度锁保护。
- 内存变量：无重试机制，读写异常直接写日志。

**变量质量位（原 §3.7）**

| 质量位 | 含义 |
|---|---|
| Good | 正常通讯，数据有效 |
| BadComm | 通讯失败 |
| BadNotExist | 变量不存在 |
| Uncertain | 不确定 |

通讯断线时质量位置 `BadComm`；恢复后自动复位。

**变量分类与类型（原 §3.8）**
- 变量分三类：**内存变量、PLC 变量、相机状态变量**。
- 相机状态变量：相机插件推送图像数据失败仅写入调试日志，不做重试。
- 支持变量别名、变量注释（设计器属性面板展示）。
- 支持只读标记：只读的 PLC 变量不允许执行写操作、跳过写入重试逻辑。
- 支持数据类型：byte、int、dint、float、bool、string；类型不匹配的读取按预设规则处理。

> 原 §3.9「冷却 / 熔断 / 防抖（`CoolDownMs` / `DebounceMs` / `WriteSemantics`）」中，仅 §14.4「防抖 `DebounceMs`」有效（见 7.15）；`CoolDownMs` 冷却 / 熔断机制已删除，不作保留。

---

### 7.4 控件属性绑定体系（原 §4）

**设计原则（原 §4.1）**
采用**元数据驱动的通用绑定引擎**（不使用附加属性方案）：控件在模型层注册可绑定属性元数据，属性面板自动识别，无需为每个属性硬编码。

**双模式属性（原 §4.2）**
- 静态默认值：在属性面板直接设置颜色、文字、尺寸、字体等常量（即值源 `$static`）。
- 变量/表达式绑定：开启绑定后，变量或表达式计算结果覆盖静态值；绑定统一为**「值源」模型**（`$static`/`$var`/`$expr`/`$if`/`$formula`/`$recipe` 等，见 M0 B1），**字段类型决定可选源**。
- **绑定方向**：交互控件可设**单向 `→`**（只把数据显示到控件）或**双向 `⇄`**（控件改值写回数据源）；纯显示属性仅单向（见 K8）。
- 绑定异常、通讯断线、表达式报错时自动回退到预设默认值，画面不会出现空白或异常。

**可绑定属性范围（原 §4.3）**
文本、前景色、背景色、可见性、可用状态、闪烁、数值、角度等。位置和宽高尺寸默认仅静态设置，不开放绑定，以降低复杂度。

**表达式能力（原 §4.4）**
支持变量引用（`${VarId}`）、比较运算（> < >= <= == !=）、逻辑组合（&& ||）、括号优先级。属性绑定与动作节点条件判断共用同一表达式引擎。支持变量区间判断与变量类型转换。表达式执行异常，捕获并写调试日志，本次条件判定结果视为 false。

**两套独立体系（原 §4.5）**
- 属性绑定：变量 ↔ 控件外观（**方向可选**：单向 `→` 只读显示 / 双向 `⇄` 改值写回；纯显示属性仅单向，见 K8）。
- 事件动作：控件事件 → 执行脚本/逻辑。
- 两者完全独立，互不干扰。

---

### 7.5 输入事件：鼠标与触摸（原 §5）

**统一业务事件（原 §5.1）**
组态上层对外只暴露三个业务事件，不区分鼠标与触摸：
- **Pressed**：按下（鼠标左键按下 / 触摸屏触摸按下）
- **Released**：松开（鼠标左键松开 / 触摸屏触摸抬起）
- **Click**：完整点击（按下后在同一控件内松开）

**底层事件去重（原 §5.2）**
工业触摸屏驱动触摸时会同时触发 TouchDown 与 MouseDown，底层包装层自动去重：如果本次由触摸触发，屏蔽同步产生的鼠标事件，避免动作脚本重复执行。

**扩展能力（原 §5.3）**
动作上下文 ActionContext 中包含 InputSource（Mouse / Touch）字段，高级外挂插件脚本可读取以区分触发来源；普通组态界面不展示此选项。

**按钮事件与脚本（原 §5.4）**
按钮支持 Click、MouseDown、MouseUp 三个独立事件，每个事件可单独配置脚本与前置条件表达式；脚本在独立线程执行，支持超时终止，可读控件属性与变量池变量，可调用外挂插件自定义函数。

---

### 7.6 动作脚本引擎（原 §6）

**引擎核心规则（原 §6.1）**
- 支持 IF 条件分支，支持 && || 多条件组合表达式。
- 每个动作节点独立配置超时时间（非全局统一），超时自动终止当前节点并记录日志。
- 所有耗时动作自动后台 Task 执行，不阻塞 UI 线程。
- 动作节点支持前置条件表达式，表达式成立才执行该节点。
- 失败策略可选：Stop（整条动作链终止）或 Continue（跳过当前节点继续下一个）。
- 复杂业务逻辑通过外挂插件扩展。

> 原「DLL 加载前做 SHA256 哈希白名单校验」已随新架构（`.tsx` 热编译、放弃白名单）作废，不归档。

**动作链模型（原 §6.2）**
ActionChain 包含有序 ActionNode 列表；按钮事件、周期任务均通过 ActionChainId 引用动作链，多个按钮可复用同一动作链。

**内置动作清单（原 §6.3）**

| 分类 | 动作名称 | 说明 |
|---|---|---|
| 画面导航 | 跳转至指定画面 | 卸载当前画面，加载目标画面，自动处理变量订阅增减 |
| 画面导航 | 返回上一画面 | 读取画面栈回退，栈空不执行 |
| 报警操作 | 确认当前选中报警 | 将表格选中行报警标记为已确认 |
| 报警操作 | 确认全部激活报警 | 所有激活报警统一标记为已确认 |
| 变量读写 | 写入变量 | 即时下发写请求，入写队列保序下发（见 §14.2） |
| 变量读写 | 变量置1 / 置0 | 布尔变量快速写入 |
| 表格操作 | 表格新增行 / 编辑选中行 / 删除选中行 / 清空全部数据 | 异步执行 |
| 表格操作 | 表格导入 CSV / 导出 CSV | 后台异步，带超时 |
| 配方(P2) | 保存配方 | 将当前变量值存入配方 |
| 配方(P2) | 下载配方到变量 | 批量写入 PLC 变量 |
| 系统辅助 | 执行外挂插件动作 | 独立超时 |
| 系统辅助 | 延时等待 | 异步等待，不阻塞 UI |
| 系统辅助 | 播放提示音 | 报警提示 |

---

### 7.7 表格控件体系（原 §7）

**TableControl 标准组态控件（原 §7.1）**
表格作为独立 IConfigControl 控件，可拖拽到画布。报警历史、日志、数据记录、变量管理、用户管理、审计记录全部复用同一表格控件，仅通过绑定不同数据源区分。

**数据源解耦（原 §7.2）**
表格只负责渲染，不存储数据。报警、审计、日志、变量列表各自实现 IDataSource 接口，作为表格的数据源。表格订阅数据源 OnDataChanged 事件自动刷新。

**单元格动态绑定（原 §7.3）**
单元格文字、前景色、背景色支持变量/表达式绑定，例如故障行自动标红。

**TableActionButton 预制操作按钮（原 §7.4）**
表格配套的新增、编辑、删除、清空、导入、导出做成标准预制控件，组态时下拉选择功能 + 绑定目标表格 ID 即可，无需手写脚本。预制按钮内置权限校验，无权限自动置灰；预制功能不足时可追加自定义脚本/插件扩展。

**性能优化（原 §7.5）**
支持只读 / 可编辑两种模式；**前端虚拟滚动 + 分页**，支持大数据量，防止 UI 卡顿；导入导出在后台 Task 执行，带超时。

---

### 7.8 画面导航系统（原 §8.1）

采用单画面独占模式：同一时刻只加载一张主画面，旧画面卸载并触发变量订阅减计数。支持启动画面配置、画面栈返回、模态小弹窗。不支持多浮动窗口并行。

---

### 7.9 报警系统（原 §8.2）

- 报警规则：绑定变量 + 表达式条件 + 报警等级（Info/Warning/Fault/Critical）。
- 状态机：未触发 → 激活 → 已确认 → 恢复。
- 实时提示：顶部报警条，可选声音提示。
- 持久化：写入本地轻量数据库，作为表格控件的报警历史数据源。
- 独立积木模块，不需要报警功能可整体卸载；不做远程报警推送。
- 报警来源：通讯故障、IO 异常、相机异常均自动触发系统报警，携带时间戳与来源标记；恢复后自动清除对应报警。

---

### 7.10 通讯断线与重连管理（原 §8.3）

- **PLC 驱动**：断线后按全局统一间隔自动重连；连接状态写入系统变量，可绑定指示灯。
  - 支持多协议对象：S7 / 汇川 EIP(CIP) / 汇川 ModbusTCP，各自作为 `IPlcDriver` 的实现，重连由 `OnDisconnected` 驱动状态机（见 §14.1）。
  - 断线期间写队列项滞留，重连后自动续传（见 §14.2）。
  - 通讯异常时变量质量位标记 BadComm；恢复后自动复位。
  - 断线/重连事件写入系统日志。
- **无硬件场景**：未接入真实 PLC 时改用部署级「**离线模拟模式**」（见 §7.25），断线 / 重连行为可手动注入验证。
- **相机模块**：断流检测，自动重连；图像控件无信号时显示占位图。多路场景下断流隔离，单路断线不影响其它路（见 §14.5）。

---

### 7.11 P1 强烈建议模块（原 §9）

**周期后台任务模块（原 §9.1）**
在动作引擎内配置周期任务：执行周期 + 动作链 ID。后台异步循环执行，复用超时与取消令牌。任务数量有限制，防止大量高频任务耗尽 CPU。与 PLC 变量轮询相互独立，用于执行业务逻辑而非读取变量。

**趋势曲线控件 + 历史数据存储（原 §9.2）**
TrendCurve 为可拖拽组态控件，绑定变量绘制实时曲线，支持基础缩放。历史数据模块按采样周期将指定变量写入本地轻量数据库。仅本地回放，不做多 Y 轴、多游标、远程历史查询。

**工程诊断与运维面板（原 §9.3）**
内置固定诊断页面，不允许组态编辑。包含变量实时监控表（名称、值、质量位）、通讯状态面板、日志查看入口。用于现场调试排查。

**工程备份恢复模块（原 §9.4）**
一键打包工程为单文件工程包，支持导入恢复。工程记录版本标记与修改时间。不做增量备份与差异对比。

**报表定义模块（K6，原方案遗漏·2026-10-12 补）**
组态期定义报表（不是运行态出表）：两种数据源——**历史聚合**（选点 + 聚合列 Avg/Max/Min/Sum/Count，每点一行）与**报警事件**（按最低级别 / 分组过滤 + 最多行数）。运行期查本地库出表、导 CSV。不做多维分析与远程报表。

---

### 7.12 P2 按需可选模块（原 §10）

**配方管理（原 §10.1）**
配方为一组变量值集合。基于 TableControl 展示配方列表。内置动作：保存配方、下载配方至 PLC。仅基础增删，不做版本管理与远程同步。

**触摸屏虚拟数字键盘（原 §10.2）**
点击数值输入控件弹出内置数字小键盘，仅数字 + 小数点。不做全字母键盘。

**多语言模块（原 §10.3）**
仅控件静态文本支持语言字典翻译，变量动态内容不做多语言。无需求时直接不加载该模块。

---

### 7.13 全局架构约束（原 §13）

**通用约束（原 §13.1）**
- 定位：专用设备组态，非通用 HMI。所有模块遵循够用即止，砍掉通用组态冗余功能。
- P0 为基础骨架；P1、P2 全部可独立拆卸，移除后 P0 骨架可正常运行。
- 所有内置动作统一接入动作引擎，复用底层异步、超时、日志能力。
- 所有 IO 与后台任务全异步，CancellationToken 贯穿超时与停止。
- 设计时与运行时模型完全分离，序列化只保存自定义模型，不保存任何 UI 框架对象。
- 线程安全：变量池细粒度锁；消息总线回调在服务端按业务线程序列化（前端经 WebSocket 推送，无 UI 线程切换）。
- 资源释放：模块停止时逆序关闭，相机/PLC/任务全部释放，防止内存泄漏。

> 原「所有插件 DLL 加载前执行 SHA256 哈希白名单校验」已随新架构作废，不归档。

**系统约束与限制（原 §13.2）**
- 运行时不支持工程暂停功能，运行过程无法暂停 / 恢复。
- 删除画布控件仅解绑事件、脚本，不会自动删除变量池变量，需要用户手动清理冗余变量（删除控件时弹窗提示）。
- 设计器编辑模式下，不会执行任何 PLC 读写、相机采集、脚本运算。
- 变量配置解析失败：写日志，拒绝加载整个工程。

**异常处理与容错（原 §13.3）**
- 全部模块异常捕获，写入日志，禁止未捕获异常直接造成主程序崩溃。
- 脚本增加超时终止机制，防止死循环占用线程资源。
- 外挂插件隔离运行，插件内部异常不会导致运行时崩溃；支持动态卸载，卸载前等待内部任务完成或超时终止，卸载后释放资源。
- PLC 通讯断线不停止整个运行时，持续重试连接；恢复后自动清除报警。
- 条件表达式执行报错，判定结果返回 false，不中断画面与系统运行。
- PLC 变量读写失败：首次/运行周期失败写日志、生成系统报警，变量池继续运行；写入失败按全局统一重试次数/间隔重试，耗尽后写日志并放弃本次写入；通讯恢复自动清除报警并恢复周期读取。

**资源释放机制（原 §13.4）**
- 关闭工程时，统一停止全部后台任务：脚本线程、相机采集、PLC 通讯。
- 释放变量池资源、释放插件句柄、图像内存资源。
- 图像 Buffer、插件句柄、通讯连接全部做释放处理，规避内存泄漏。

---

### 7.14 PLC 多协议抽象（S7 / 汇川 EIP / 汇川 ModbusTCP）（原 §14.1）

**契约不变，协议作为 IPlcDriver 的不同实现**
正文 IPlcDriver / IPlcDriverFactory / IPlcDriverManager 即统一契约。S7、汇川 EtherNet/IP(CIP)、汇川 ModbusTCP 各自是对 IPlcDriver 的一个实现，上层（变量池 / 订阅管理器）只依赖接口，对具体协议无感；驱动插件仍按正文插件化加载。

**各协议批量读策略（降报文往返）**
- CIP：一次往返批量读多标签，按 CIP 单报文上限与 PLC 配置分帧。
- ModbusTCP：连续寄存器地址区间一次读取（ReadHoldingRegisters 起止，报文按需截断）。
- S7：按 DB 连续区批量读。

**IO 字节读（可选项）**
供 IO 画面拆位使用，作为驱动扩展能力，非必选；未连接或不支持时返回空。

**汇川补充说明**
EtherNet/IP(CIP) 适用于支持 CIP 的汇川型号；对仅支持 ModbusTCP 的型号，框架内置 ModbusTCP 驱动，由 Factory 按配置选型（"EIP"→InovanceEIP、"MODBUSTCP"→ModbusTCP），UI 下拉同步扩展。两个协议复用同一套队列 / 防抖 / 质量位管线，仅替换底层位次转换与批量读策略。
寄存器映射：3x 输入寄存器读 / 4x 保持寄存器写 / 0x、1x 线圈位按 Picture 变量地址映射。

**驱动选型取舍**
复用案例自研抽象，不入第三方商用库（HslCommunication 企业商用需授权，与专用框架定位冲突）。实现可参考既往案例已趟坑的逻辑，但不照搬命名与结构。

---

### 7.15 防抖（IO / 触发信号，变量元数据级：DebounceMs）（原 §14.4）

**两级防抖设计**
1. 上升沿判定（已有）：`cur && !_last`，只认 0→1 变化。
2. 时间窗滤抖（新增，DebounceMs）：某布尔变量 / IO 采样后，仅当信号在 DebounceMs 内保持稳定才视为有效动作。过滤机械触点 / 接线抖动毛刺、PLC 与上位机轮询时序造成的瞬时误触发。

**归属与触发位置**
- 放在变量缓存变化回调 → 订阅分发之间（对 IVariableSubscribeManager 的 change 事件做窗口判定），而非在 IOC 物理层，保证对触摸 / 按钮 / PLC 触发统一生效。
- 消费方：内置动作脚本、相机拍照触发、报警条件表达式均读「已防抖」的值；原始值保留在 DataCache 供诊断。

**实现要点**
- 每个需防抖变量维护最近一次采样时间戳 + 暂存值；DebounceMs 达到且值未再变化才对外分发。
- 与上升沿判定互不冲突：防抖管信号真实性，上升沿管是否触发，两者在管线中串联。
- **【约束 R3】** DebounceMs 需 ≥ 采样周期 CycleMs，否则永远无法判定稳定，组态校验时约束。

---

### 7.16 相机：单路 / 多路连接方式（ChannelCount 默认 5）（原 §14.5）

**连接模型**
- 枚举：DeviceEnumerator.EnumDevices 发现相机；支持 GigE（首选多路）与 USB3（单路推荐）。
- 每路独立实例：MvCameraDevice 封装 IDevice + IParameters + FrameBuffer，StartGrabbing(LatestImageOnly)。
- 异常隔离：单路异常 / 断线只影响该路，不及其它路，符合正文「采集插件**独立于前端渲染**、断流重连」。

**单路 vs 多路**

| 需求 | 推荐方式 |
|---|---|
| 单相机（如热延伸检测） | 单 MvCameraDevice，软触发或硬线触发，StartGrab 仅在画面激活时拉流 |
| 多相机并行（如铆接正反两面） | 每路一个 MvCameraDevice + 对应 FrameBuffer，互不阻塞；多路上限由 CameraManager 配置 ChannelCount（默认 5） |

**与 PLC 的触发联动**
- 软件触发：TriggerMode=On，触发源 software / hardware 双模式；生产模式拍一次取一次，靠帧序号（FrameSeq 单调自增）判定新帧。
- PLC 联动链路：PLC 布尔触发变量上升沿（走 §14.4 防抖）→ 激活相机抓一帧 → 视觉处理（外挂插件 / 内置流程）→ 判定 OK/NG → 结果回写 PLC 结果变量（走 §14.2 写队列，必须保序）→ 200ms 后自动复位。
- 多路场景下，触发变量（正面 / 反面）各自绑定对应通道，通道间互斥（PLC 侧保证不同时触发，软件侧再加互斥兜底）。

**离线模拟（无相机 / 相机未接）**
- 由 **Mock 相机源**（实现同一 `ICamera` / `CameraFrame` 契约）产出**假帧**：**静态测试图**（彩条 / 棋盘 / 时钟）打底，**可选动态假流**（按配置帧率推进、`FrameSeq` 单调自增）——用于验证触发模式、帧同步与视觉处理链，并避免图像控件无信号黑屏。
- 与真实相机**部署级二选一**（同 §7.25），不引入运行期热切换。

---

### 7.17 数据模型新增字段与接口映射（原 §14.6）

**VariableMeta 新增字段（并入 §12）**
- `DebounceMs`：时间窗防抖（毫秒，默认 0）；约束 ≥ `CycleMs`。

> 原 §14.6 中 `CoolDownMs`（写后冷却，字段与机制已删除）与 `WriteSemantics`（Strict / Coalesce 写语义合并，已定为暂不实现）两条字段随之移除，不归档。

**与正文接口 / 模型映射**

| 接口 / 模型 | 落点 |
|---|---|
| IVariablePool / IVariableSubscribeManager | 读分组订阅 + 写队列；防抖分发挂订阅层 |
| IPlcDriver / IPlcDriverFactory / IPlcDriverManager | 多协议抽象（S7 + 汇川 EIP/CIP + ModbusTCP） |
| PlcWriteResult / PlcVariableReadResult | 写队列重试 / 质量位返回 |
| ICameraDevice / ICameraFactory / ICameraManager | 每路实例 + FrameBuffer + 断流重连 |
| IExpressionEvaluator | 读「已防抖」的值 |
| 动作引擎 ActionNode | 触发 → 采图 → 回写联动作为内置 / 外挂动作 |

---

### 7.18 变量读写排队（读分组轮询 + 全量串行写队列）（原 §14.2）

**（一）读：按画面激活分组 + 批量**
- 采用「画面激活引用计数 → 周期采集；计数归零 → 停止；IsPermanent 常驻不停」。
- 高频集合 = 界面常驻（急停 / 故障 / 报警 / 常驻高频） ∪ 当前激活画面绑定的 read / readwrite 变量；非页面变量不轮询。
- 高频集合为空时退化全量一次（兜底，避免画布空转）。
- 连接成功首读全量填一次 DataCache，避免首次进画面数值逐个蹦出。
- 协议分流：批量读协议走 ReadBatch 分批复用一次往返；非批量协议逐个串行。
- 读轮询与写共用一把通讯互斥锁（SemaphoreSlim 1,1），杜绝读写同帧竞争。

**（二）写：全量串行队列（保序、保重试）**
- 所有写一律入队（FIFO），由队列统一调度「写 → 重试 → 缓存/UI 刷新」，无需区分连接与否；PLC 未连接时项滞留队列，重连后自动续传。
- 串行 drain：每项写前抢通讯锁；项间小幅间隔起站闸节流（默认参考 20ms，可配 WriteGapMs）。
- 重试：MaxWriteRetryCount（案例参考 10 次），失败重新入队，耗尽则放弃并清理 pending 标记，避免读线程永久跳过该标签。
- 写成功才更新 DataCache + 触发 UI。
- pending 窗口（写后约 500ms）内该标签跳过读，防「读到旧值」覆盖新写入。

> 原 §14.2「（三）写语义：Strict / Coalesce」一节已删除：写语义合并（Coalesce）定为暂不实现、留待 M7，不在此归档。

---

### 7.19 菜单栏完整定义（原 §15.2）

> **命令体系一致性**：菜单栏 / 工具栏 / 快捷键均为**同一命令层（Cmd）的不同入口**——工具栏按钮和快捷键只是高频命令的快速入口，功能权威定义集中在菜单栏，三者始终指向同一命令，避免功能重复或找不到。

**文件**
- 新建工程
- 打开工程
- 保存
- 另存为
- 工程发布（发布为运行态可加载工程包，区分设计源文件）
- 最近打开工程列表
- 退出

**编辑**
- 撤销（Undo）
- 重做（Redo）
- 剪切
- 复制
- 粘贴
- 删除
- 全选
- 查找（Ctrl+F）
- 替换（Ctrl+H）
- 取消选择

> 多选控件支持批量属性修改。
>
> **查找 / 替换作用范围**：
> 1. 画布控件名称、控件内静态文本
> 2. 变量名称、变量注释
> 3. 脚本内容、条件表达式
> 4. 报警文本、多语言文本、配方描述
>
> 支持选项：区分大小写、全字匹配；查找结果列表在底部输出面板展示，双击定位。

**格式**
- 对齐（子菜单）：左对齐 / 水平居中 / 右对齐 / 顶端对齐（上对齐）/ 垂直居中 / 底端对齐（下对齐）
- 分布（子菜单，多选控件生效）：水平等距分布 / 垂直等距分布
- 大小（子菜单，统一尺寸）：统一宽度 / 统一高度 / 统一宽高
- 分层（层级）（子菜单）：置于顶层 / 置于底层 / 上移一层 / 下移一层
- 网格（子菜单）：显示网格 / 启用网格吸附 / 网格设置（弹窗配置网格尺寸）

**调试**
- 通讯测试（单独测试 PLC、相机连接，不启动完整工程）
- 编译工程（语法校验：表达式、脚本、变量引用、画面停靠冲突校验，输出编译日志）
- 运行工程（加载编译后的工程包运行）

**工具**
- 导入外挂插件
- 移除外挂插件
- 变量模拟器：**设计期工具**——在设计器内模拟变量值、预览画面颜色 / 文字 / 控件可见性，**无需启动运行态**（与**运行态**的「离线模拟模式」是两回事，见 §7.25）
- 表达式测试器：独立弹窗测试条件表达式，输入变量查看判定结果
- 工程完整性校验（工程级交叉依赖 / 引用自洽检查，与「调试→编译工程」的语法校验定位区分）
- 选项（全局配置：网格默认大小、标尺开关、**全局工程基准分辨率**（整个工程唯一基准，普通画面以此为准，不可单画面修改）、运行设置（缩放模式、全屏开关）、日志保存路径、脚本全局超时默认值）

**视图**
- 工具箱（显示 / 隐藏）
- 属性面板（显示 / 隐藏）
- 项目资源树（左侧导航树，显示 / 隐藏）
- 底部输出面板（显示 / 隐藏）
- 标尺（画布顶部 / 左侧标尺开关）
- 全屏编辑
- 重置窗口布局（恢复默认面板布局）

**帮助**
- 关于
- 帮助文档

---

### 7.20 工具箱（控件库）（原 §15.4）

- 工具箱顶部增加**搜索框**；支持**控件收藏夹**，常用控件置顶。
- 控件分组：
  1. **基础图元**：直线、矩形、圆形、椭圆、多边形、折线
  2. **基础输入输出**：文本、数值显示、数值输入、按钮、开关、指示灯、多选框、下拉列表
  3. **高级画面控件**：图片容器、相机控件、表格控件、报警控件、实时曲线、历史曲线、进度条、日期时间控件、多色报警灯、曲线游标控件

---

### 7.21 属性面板（原 §15.5）

选中画布控件 / 树节点（画面 / 变量 / 报警 / 通讯），属性面板**动态加载分层折叠属性分组**，分组可展开 / 折叠，分组折叠状态自动记忆；面板顶部提供**属性搜索框**，输入关键词自动过滤并高亮匹配属性项，属性多时快速定位。

> **分组清单（工业组态软件标准分层做法）**：
> 1. **基础属性**（几乎所有对象都有）：名称（控件唯一标识，脚本、变量绑定引用）、注释、X / Y、宽度 / 高度、Z 序层级、是否锁定、是否可见（运行时可见性，支持变量绑定）
> 2. **样式属性**：背景色、前景文字颜色、字体（字体名 / 字号 / 粗体 / 斜体）、边框（颜色 / 粗细 / 线型）、透明度；**控件预设样式**（按钮/指示灯/棒图选风格，样式只管风格、颜色仍由各颜色属性决定——见 K7，与 B9 令牌体系合并）
> 3. **变量绑定属性**（所有支持变量绑定的控件）：值绑定、文本绑定、颜色绑定（按变量值自动切换颜色）、可见性绑定、启用 / 禁用绑定（权限 / 变量控制控件可操作性）。绑定点击项弹出**变量选择器**，从变量树选取
> 4. **事件脚本属性**（按钮、开关等可交互控件）：鼠标按下事件、鼠标松开事件、Click 点击事件；每个事件配置**前置条件表达式 + 绑定脚本**，并可单独设置**脚本超时时间**
> 5. **权限属性**：操作角色选择；无权限时控件可设为**禁用 / 隐藏**
> 6. **控件专属属性**（不同控件按需动态显示）：相机控件（ROI 区域、图像刷新模式、相机实例选择）、曲线控件（采样周期、绑定变量、坐标轴配置）、报警控件（过滤条件、显示行数）、表格控件（列定义、绑定记录组）、指示灯（多状态颜色映射表）等
>
> **画面专属分组**（仅在项目树中选中画面节点时显示，替换控件分组）：
> - 画面名称、注释
> - **画面停靠模式**（下拉选择）：普通画面 / 停靠顶部 / 停靠底部 / 停靠左侧 / 停靠右侧 / 弹窗画面
> - 画面宽度、画面高度（弹窗画面 / 停靠画面生效）
> - 弹窗选项（仅停靠模式＝弹窗画面显示）：模态窗口、窗口可拖动、显示标题栏
> - 背景色、运行缩放策略
> - 尺寸规则：停靠画面（顶 / 底 / 左 / 右）宽高受停靠方位约束——如停靠左侧宽度自定义、高度自动填充主窗体高度，停靠顶部高度自定义、宽度自动铺满主窗体；**普通画面尺寸以全局工程基准分辨率为准，不可单独修改**；**弹窗画面不受全局分辨率约束，宽高完全自定义**。
>
> **功能规则**：
> - 选中非画面类资源（变量 / 报警 / 通讯）时，自动切换为对应资源的属性分组，隐藏控件坐标、Z 序等画面相关分组。
> - **多选批量编辑**：多选同类型控件仅展示公共属性，公共属性修改后批量应用；不同类型控件多选只保留共有属性组。
> - **只读属性灰色标识**：如停靠画面的自动填充宽高、运行时状态类字段，灰色不可编辑。
> - **属性提示**：鼠标悬浮属性项，右下角弹出简短说明（如 Z 序：数值越大越在顶层）。
> - 分层折叠分组：选中对象仅加载其对应的属性组，无关分组自动隐藏。

---

### 7.22 画布编辑区能力（原 §15.6）

1. 画布多标签页，多画面同时打开，快速切换编辑
2. 控件锁定 / 解锁，锁定后不可拖拽修改，防止误操作
3. 控件【组合 / 取消组合】，多选控件打包为整体移动
4. 画布标尺（顶部、左侧标尺）
5. 画布右下角缩略导航图，大画面快速定位
6. 画布缩放：鼠标滚轮缩放，缩放比例下拉框 25%~400%
7. 拖拽变量树变量到画布，自动生成绑定控件，提升组态效率
8. 画布空白处右键菜单：【画面属性】、【全选】、【取消全选】、【网格开关】
9. 编辑停靠画面时，画布显示**停靠布局模拟边界**（如左侧停靠画面预览为长条纵向面板），直观呈现运行态实际停靠效果

---

### 7.23 快捷键清单（原 §15.8）

- Ctrl+N 新建工程
- Ctrl+O 打开工程
- Ctrl+S 保存工程
- Ctrl+Z 撤销
- Ctrl+Y 重做
- Ctrl+X 剪切
- Ctrl+C 复制
- Ctrl+V 粘贴
- Delete 删除选中控件
- Ctrl+A 全选画布控件
- Ctrl+F 查找
- Ctrl+H 替换
- Ctrl+G 组合选中控件
- Ctrl+Shift+G 取消组合

---

### 7.24 画面停靠模式与全局工程基准分辨率（原 §15.10）

设计器与运行态的分辨率 / 画面停靠统一规则，分散条目见 §15.2 / §15.3 / §15.5 / §15.6，本小节收纳其完整规则与运行态加载语义。

**一、全局工程基准分辨率**
- 整个工程唯一基准，在【工具→选项→全局配置】中设置。
- 所有**普通画面**默认以此为尺寸基准；普通画面不可单独修改画面尺寸，强制使用该基准。
- 运行时，普通画面、停靠面板、弹窗画面内的控件统一按缩放策略自适应（按设计器设定的缩放模式）。

**二、画面停靠类型（画面属性→停靠模式）**
- **普通画面**（默认）：铺满主画布，跟随全局分辨率缩放。
- **停靠顶部 / 停靠底部**：固定在运行态窗口顶 / 底，横向通栏，高度自定义、宽度自动铺满主窗体；常用于顶部菜单栏、标题栏、状态条、报警栏。
- **停靠左侧 / 停靠右侧**：固定在运行态窗口左 / 右侧，纵向，宽度自定义、高度自动填充主窗体高度；常用于导航树、功能菜单、快捷操作面板。
- **弹窗画面**：独立弹出子窗口，自定义宽高，模态 / 非模态可选，不占用主画布区域。
- 停靠画面**不参与中间主画面页面切换**，只要工程运行就一直显示。

**三、运行态画面加载顺序**
1. 加载 4 个方向停靠画面（左 / 上 / 右 / 底），固定渲染在窗体四边；
2. 在中间区域加载【启动首页】（需为普通画面）；
3. 切换画面时，仅替换中间区域普通画面，四边停靠画面保持常驻；
4. 弹窗画面由脚本 / 按钮动作触发，弹出独立子窗口；
5. **启动配置（K9）**：启动时按勾选顺序运行"启动时子程序"；**退出 / 关机前**运行"退出时子程序"（收尾代码——停泵 / 复位 / 存盘，避免"设备没复位就断电"）。启动不设登录闸（未登录 = 最低权限）。

**四、编译校验（编译工程时执行，见 §15.2 调试菜单）**
1. 同一停靠方位**只允许 1 个画面**（如只能有 1 个左侧停靠画面），重复配置输出警告；
2. 启动首页仅允许设置为【普通画面】，停靠画面、弹窗画面不能作为启动首页；
3. 停靠画面内的控件，禁止绑定替换自身所属停靠区域的画面切换逻辑（编译拦截）。

---

### 7.25 离线模拟模式（原方案遗漏 · 2026-10-12 补）

**一句话**：硬件 PLC 未接入时，运行程序照常跑——画面变量照常周期采集刷新、按钮 / 输入框照常操作、动作链照常执行；**只是所有读取来自模拟数据、所有写入落到模拟层，绝不发给真实 PLC**。

**与「变量模拟器」的区别（勿混）**

| 维度 | 离线模拟模式（本节） | 变量模拟器（§7.19 工具 / M6） |
|---|---|---|
| 定位 | **运行态**运行模式 | **设计期**调试工具 |
| 入口 | 部署配置选「模拟驱动」后启动运行 | 设计器内手动改变量值预览 |
| 范围 | 整条「采集 → 变量池 → 订阅 → 推送 → 显示」照常跑 | 只改值看画面反应，不跑采集链 |
| 写行为 | 可写（落模拟表），走完整写校验 | 无写入语义 |

**规则**
1. **进入方式 = 部署级驱动二选一**：工程 / 服务端配置里选「模拟驱动（`MockPlcDriver`）」或「真实驱动」；选模拟即离线模拟模式。**明确不引入运行期在线↔离线热切换**（避免驱动热替换 + 质量位重置 + 轮询组重启带来的"半在线"脏状态）。
2. **读**：值有两条来源——**① 人工置值（主路径、默认）**；**② 自动值生成（按点可选、默认关**：常量 / 自增 / 随机 / 按地址映射回放**）**。两者都落驱动内置模拟点表，经变量池 → 订阅轮询 → 推送 → 控件，全链与真机一致。
3. **写**：写入落**驱动内置的模拟点表**（下次读回即新值）并返回成功，**不落真实硬件**；写前仍走 M0 完整写校验（只读点 `reason=ReadOnly`、越权拦截）。
4. **人工置值优先**：某点被人工 / 动作写入后**暂停该点的自动值生成**，直到显式恢复。
5. **质量位与重连照常**：可手动注入断线 / 重连，质量位转 `BadComm` 并自动恢复（验证 §7.10）。
6. **模拟地址模型按 ModbusTCP 风格设计**（3x / 4x / 0x / 1x），使 M7 首个真实驱动可最小改动替换。

7. **其它采集源同样支持离线模拟**：**相机**由 Mock 相机源产出**假帧**——**静态测试图**（彩条 / 棋盘 / 时钟）打底 + **可选动态假流**（按配置帧率推进、`FrameSeq` 单调自增），走 M0 补齐的 `CameraFrame` 契约，可无硬件验证触发模式 / 帧同步 / 视觉处理链，并避免图像控件黑屏（见 §7.16）。
8. **「离线模拟中」状态提示**：运行态界面**顶部显示状态徽标**；**同时写入系统变量**（可像 §7.10 连接状态那样被绑定到指示灯 / 报警，供用户自定义）。
9. **术语消歧**：驱动这套自动产数据的能力统一叫「**值生成策略**」，不用"模拟器"一词；「**变量模拟器**」专指 §7.19 的**设计期**手动工具，两者互不依赖。

**落点**：驱动载体 **M1**（`MockPlcDriver`，见 M1 任务 2）；离线模式开关（配置入口）**M2**；相机离线模拟 **M5**（见 §7.16）；界面状态提示 **M3**（见 M3 任务 2）。

## M0 执行记录（2026-10-15）

本次在分支 `feature/m0-core-contracts` 开始落实 M0 契约，依据 NEXT HMI 的 B2/B3/B4 与 KPHK 的写入门禁方向取长补短，未复制参考项目代码。

已完成的首批改动：
- 三个现有 Core 项目目标框架从 `net8.0` 调整为用户已确认的 `.NET 10`。
- 新增 `HmiProject.FormatVersion` 整数格式版本与 `CurrentFormatVersion`；加载时拒绝不支持的格式，校验时报告格式版本问题。
- 变量元数据新增 `Min` / `Max`，并提供与实时值分离的 `IVariableMetadataProvider` / `VariableMetadataSnapshot`。
- 写入契约返回 `WriteResult`，包含稳定原因码和 `RequestId`；变量池执行只读拦截，并避免写入操作错误地把 PLC/相机变量质量位改成 Good。
- 值更新包将单一时间戳拆为可空的 `DeviceTimestamp` 与 UTC `ServerTimestamp`。
- 工程校验补充格式版本、量程上下界、刷新周期检查。

待继续完成：
- 统一属性值源 `IPropertySource`（静态值、变量、表达式、条件值及组件/Repeater 源）。
- 补充自动化测试并执行 Windows/.NET 10 构建验证；当前仅完成仓库级编辑，尚未声称构建通过。
- M1 修复订阅引用计数/空轮询组、隔离消费者异常，并实现 Mock PLC 驱动与通讯质量状态。
- 写入来源目前作为契约参数预留，细粒度来源门禁仍需后续实现，不能视为已完成权限控制。
