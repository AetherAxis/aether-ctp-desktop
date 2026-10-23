# Aether.Desktop 设计说明

> 本文件写「怎么设计 / 为什么这么设计 / 还能往哪升级」。工作区边界与迁移决策以根 `MIGRATION.md` 为准。

## 1. 定位与边界

⑪ 是 ⑩ `aether-workbench` 的消费者，也是 ⑦ `ctp-net-client`（`ICtpSession`）的原始宿主：登录、行情、账户/持仓/委托均在本进程完成，无服务端必需调用、无平台登录。

- **范围内**：连接器、交易（行情+账户/持仓/委托）、策略、钱包四 tab。
- **范围外**：AI/知识对象接线、打包分发；实盘仍由 ⑧ `CtpConsole --run-strategy` 承担。
- **仓库边界**：单项目，跨仓 `ProjectReference`，未包化。

## 2. 项目组成

| 目录/文件 | 职责 | 关键类型 | 出向依赖 |
|---|---|---|---|
| `Aether.Desktop.csproj` | `WinExe`/`net10.0`；macOS frameworks 复制 | — | ⑩、⑦（③②传递）、①、④ |
| `Program.cs`、`App.axaml(.cs)` | 入口；DI、`AddCtpInfrastructure`、`AddWorkbench` | `Program`、`App` | Avalonia、⑩、⑦、DI |
| `DesktopWorkspace.cs` | PanelKeys + 四 tab 定义 | `PanelKeys`、`DesktopWorkspace` | ⑩ |
| `AppViewContributor.cs` | 注册四 panel 视图/VM 工厂 | `IViewRegistryContributor` | ⑩ |
| `JsonPreferencesStore.cs` | JSON 偏好，读写同步 | `JsonPreferencesStore` | ⑩ |
| `Connector/` | 登录/登出、预设、订阅 | `ConnectorViewModel`、`ConnectionPreset` | ⑦ |
| `Futures/` | 行情板+账户/持仓/委托，回调编组到 UI | `FuturesViewModel`、`TickRow` 等 | ⑦ |
| `Strategy/` | 回测/实盘入口（M3 占位） | `StrategyViewModel`、`StrategyInfo` | MVVM |
| `Wallet/` | 链设置、钱包导入、充提、场内现金 | `WalletViewModel` | ① `LedgerClient`、④ `SimRpcClient` |
| `Directory.Packages.props`、`ci.yml` | 中央包版本；Release 构建，checkout 六仓 | — | Avalonia、MVVM、DI、Actions |

## 3. 关键设计决策

### 3.1 桌面端是这些组件的原始宿主
- **决策**：直接引用 ⑩/⑦/①/④，不复制会话/映射。
- **理由**：⑧ console 复用同款 session，协议不分叉。
- **代价**：跨仓编译耦合，CI checkout 六仓，上游改动即断构建。

### 3.2 视图与工作台均为声明式注册
- **决策**：`AppViewContributor` 按 panel key 注册视图/VM 工厂；四 tab 由 `PanelDescriptor` + `LayoutTabbed` 声明。
- **理由**：⑩ 零业务依赖；布局可序列化，插件产出同一份定义。
- **代价**：注册键是字符串，漏注册只在运行时空 panel。

### 3.3 连接器独立成页，其余页被动消费单例 session
- **决策**：登录/重连只在 `ConnectorViewModel`；期货页订阅 `ConnectionStateChanged` 刷新。
- **理由**：单例 session 只有一个连断所有者，避免互踢线。
- **代价**：跨 tab 时序只靠状态文本提示。

### 3.4 偏好 `GetAsync` 同步完成
- **决策**：返回 `Task.FromResult`；`SetAsync` 同步落盘、吞异常。
- **理由**：引擎启动期在 UI 线程同步恢复主题/窗口尺寸，真异步会死锁。
- **代价**：主线程写盘；磁盘故障静默。

### 3.5 M1 只做期货页：bot 运行证据面
- **决策**：聚合行情板、账户、持仓、委托；委托查询保持手动。
- **理由**：持仓变化、MM 挂单、权益变化与 tick 流即 bot 运行证据；委托空表会触发 SDK 30s 超时拖停管线。
- **代价**：委托非实时；自动刷新未落地（§6-4）。

## 4. 运行与验证

```bash
dotnet run --project aether-desktop/src/Aether.Desktop
```

偏好写 `~/.aether/aether-desktop/preferences.json`，连接档案为同目录 `ctp.db`。前置：`tcp://127.0.0.1:17801/17802` 或 `tcp://<IP>:17801/17802`。

CI 在 ubuntu-latest + .NET 10 只跑 `dotnet build aether-desktop.slnx -c Release`，**无 Test 步**。macOS 需复制三个 framework，否则首个 P/Invoke 即 `DllNotFoundException`。按 `MIGRATION.md` L1–L5，本仓只有 L1 证据；页面行为、回调编组、退出清理须实跑核实。

## 5. 升级记录（2026-10-24）

| 项 | 变更前 | 变更后 | 依据 / 验证 |
|---|---|---|---|
| `Avalonia` | 12.1.1 | 12.1.4 | `git diff Directory.Packages.props` |
| `Avalonia.Themes.Fluent` | 12.1.1 | 12.1.4 | 同上 |
| `Avalonia.Desktop` | 12.1.1 | 12.1.4 | 同上 |
| `Microsoft.Extensions.DependencyInjection` | 10.0.11 | 10.0.13 | 同上 |

仅改中央版本文件；未跑 restore/build，补丁级兼容性需本机与 CI 各跑一次 Release 构建确认。

## 6. 待升级项

| # | 优先级 | 项目 | 现状证据 | 建议 | 代价 / 风险 |
|---|---|---|---|---|---|
| 1 | P0 | 无测试项目 | 全仓无 `*.Tests.csproj`；CI 仅 Build 步 | 先覆盖纯逻辑：格式化、预设解析、`ParseEndpoint` | 控件测试需 headless 包 |
| 2 | P0 | 文档与代码不一致 | README 称三页、M1 仅期货页；实为四 tab | 按实际四 tab 重写 README | 无代码风险；P3 已要求 |
| 3 | P1 | 持仓均价与浮亏错误 | `RefreshPositionsAsync` 用 `tick.LastPrice`（缺失为 0）作 `AvgPrice` 再算 `Profit` | 改用 `CtpPosition.PositionProfit`；契约补均价后改真均价 | 改契约需同步 ③⑦ mapper |
| 4 | P1 | 自动轮询未实现 | README 称 3s 轮询；无 `Timer`/`Task.Delay`，仅建连与手动刷新时查询 | `DispatcherTimer` 或 `PeriodicTimer` + UI 编组 | 与 tick 共用 UI 线程，防阻塞 |
| 5 | P1 | 密码明文落盘 | `SaveFormPrefsAsync` 原样写 `Password` 到 `preferences.json` | 接系统钥匙串，偏好只存引用 | 需跨平台抽象；旧明文需清理 |
| 6 | P2 | 策略页为桩 | `RunBacktest`/`StartLive` 只改状态文本，无 ⑥ 运行时引用 | 接 ⑥ `IBacktestRuntime`/`ILiveTradingRuntime` | 新增依赖，验证成本高 |
| 7 | P2 | 预设硬编码 | `DefaultPresets` 写死 `117.72.186.128` 与本地端口 | 外置配置，内置默认兜底 | 需定义格式与回退 |
| 8 | P2 | 跨仓源码耦合 | 四个 `ProjectReference` 指上级目录；CI checkout 六仓 | P4 推进 PackageReference + 固定版本 | 需建包发布与版本对齐 |
