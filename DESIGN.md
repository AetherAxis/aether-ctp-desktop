# Aether.Desktop 设计说明

> 本文件写「怎么设计 / 为什么这么设计 / 还能往哪升级」。工作区边界与迁移决策以根 `MIGRATION.md` 为准。

## 1. 定位与边界

⑪ 是 ⑩ `aether-workbench` 的消费者，也是 ⑦ `ctp-net-client`（`ICtpSession`）的原始宿主：登录、行情、账户/持仓/委托均在本进程完成，无服务端必需调用、无平台登录。

- **范围内**：行情（纯 MD 看板）、连接器、交易（行情+账户/持仓/委托）、策略、钱包 5 tab。
- **范围外**：AI/知识对象接线、打包分发；实盘仍由 ⑧ `CtpConsole --run-strategy` 承担。
- **仓库边界**：单项目，跨仓 `ProjectReference`，未包化。

## 2. 项目组成

| 目录/文件 | 职责 | 关键类型 | 出向依赖 |
|---|---|---|---|
| `Aether.Desktop.csproj` | `WinExe`/`net10.0`；macOS frameworks 复制 | — | ⑩、⑦（③②传递）、①、④ |
| `Program.cs`、`App.axaml(.cs)` | 入口；DI、`AddCtpInfrastructure`、`AddWorkbench` | `Program`、`App` | Avalonia、⑩、⑦、DI |
| `DesktopWorkspace.cs` | PanelKeys + 五 tab 定义 | `PanelKeys`、`DesktopWorkspace` | ⑩ |
| `AppViewContributor.cs` | 注册五 panel 视图/VM 工厂 | `IViewRegistryContributor` | ⑩ |
| `JsonPreferencesStore.cs` | JSON 偏好，读写同步 | `JsonPreferencesStore` | ⑩ |
| `MarketWatch/` | 行情看板：MD 免登录订阅、tick 原位更新、涨跌口径 | `MarketWatchViewModel`、`QuoteRow`、`MarketMath` | ⑦ |
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

### 3.6 行情页独立成 tab，且不要求交易登录（2026-10-25）
- **决策**：新增「行情」tab 作为首屏，只连 MD 腿并自动订阅；交易登录仍归连接器 tab。
- **理由**：用户的实际诉求是「两个 bot 跑起来时能肉眼看到价格在动」。原先把行情绑在交易页 ⇒ 必须先登录
  （密码不落盘，每次手输）、再手动订阅，看行情被交易流程挟持。MD 登录不校验密码（本前置与 SimNow 皆然），
  所以行情完全可以零凭据冷启动。默认前置写远程 `117.72.186.128:17802`（服务器已部署那套），本地/远程一键切。
- **涨跌基准**：昨结 → 昨收 → 本会话首笔，逐行标注（`QuoteRow.Basis`）。**不拿「相对上一笔」冒充涨跌**——
  前置每秒推送快照，价格不动时那是恒 0；也不拿 0 当基准算成 -100%。本前置当前不下发昨结/昨收
  （⑤ 的 tick 模板没这两个槽，见 `ctp-net-front/FEATURE_MATRIX.md`），所以仿真链路显示「首笔」——
  界面把口径写出来，比给一个没来源的百分比诚实。
- **共用 MD 连接**：不另开一条私有 MD 连接，仍用 ⑦ 单例 `ICtpSession`（重连/重订阅逻辑已在那层硬化）。
  代价：连接器里换账号登录会重建 MD，行情页订阅失效——**显式转「静默 N 秒」并留『重新订阅』按钮**，
  不做后台互抢（两页偷偷重建对方订阅，比看得见的失效更难查）。
- **代价**：行情页与交易页共享一条 MD 连接，不能同时用两个账号订阅；静默检测是 1s 定时器，非推送式。

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
| 1 | P0 | ~~无测试项目~~（已建，59 例；CI 仍只有 Build 步） | `tests/Aether.Desktop.Tests` | CI 补 Test 步 | 需 CI 环境有 macOS frameworks（行情测试不碰原生库） |
| 2 | P0 | 文档与代码不一致 | README 曾称三页；2026-10-25 已按五 tab 重写 | 保持文档随 tab 变更同步 | 无代码风险 |
| 3 | P1 | 持仓均价与浮亏错误 | `RefreshPositionsAsync` 用 `tick.LastPrice`（缺失为 0）作 `AvgPrice` 再算 `Profit` | 改用 `CtpPosition.PositionProfit`；契约补均价后改真均价 | 改契约需同步 ③⑦ mapper |
| 4 | P1 | 自动轮询未实现 | README 称 3s 轮询；无 `Timer`/`Task.Delay`，仅建连与手动刷新时查询 | `DispatcherTimer` 或 `PeriodicTimer` + UI 编组 | 与 tick 共用 UI 线程，防阻塞 |
| 5 | P1 | 密码明文落盘 | `SaveFormPrefsAsync` 原样写 `Password` 到 `preferences.json` | 接系统钥匙串，偏好只存引用 | 需跨平台抽象；旧明文需清理 |
| 6 | P2 | 策略页为桩 | `RunBacktest`/`StartLive` 只改状态文本，无 ⑥ 运行时引用 | 接 ⑥ `IBacktestRuntime`/`ILiveTradingRuntime` | 新增依赖，验证成本高 |
| 7 | P2 | 预设硬编码 | `DefaultPresets` 写死 `117.72.186.128` 与本地端口 | 外置配置，内置默认兜底 | 需定义格式与回退 |
| 8 | P2 | 跨仓源码耦合 | 四个 `ProjectReference` 指上级目录；CI checkout 六仓 | P4 推进 PackageReference + 固定版本 | 需建包发布与版本对齐 |

## 7. 本轮升级落地（2026-10-24）

- **测试项目**（第 1 项）：新建 `tests/Aether.Desktop.Tests`（29 例全绿）：偏好存储（同步完成契约、设 null 即删键、跨实例持久化、坏文件当空重建、密码键可彻底删掉）、连接预设档案（用户档优先/坏档回退内置/空清单兜底/单条坏数据只丢自己/序列化往返）、显示格式（方向/开平/委托状态字符→中文）。CI 加 Test 步骤。
- **持仓均价与浮亏**（第 3 项）：不再拿 `tick.LastPrice` 冒充开仓价、不再把 `Profit` 硬填 0。**契约补 `PositionCost`**（原生 `PositionCost` 一路补齐：② SDK `Position` → ③ `CtpPosition` → ⑦ mapper），界面列改为「持仓成本」按原值显示（均价 = 成本 ÷ (手数 × 合约乘数)，乘数不在契约里，故不折算）；浮盈改用柜台口径 `PositionProfit`。tick 回调只更新现价——**不再用「价差 × 手数」自己算**（那是价格单位不是金额，且与昨仓结算价基准不符）。
- **自动轮询**（第 4 项）：`DispatcherTimer` 3s 轮询账户+持仓（与 README 一致；原来类注释宣称轮询但代码里没有），连接时启动/断开停止，`_pollBusy` 防重入。委托表仍是手动刷新（空表查询 30s 超时为已知边界）。
- **密码明文落盘**（第 5 项）：**密码不再持久化**，并在构造时清掉历史版本写下的明文键；`AutoLogin` 因冷启动无密码而无法自动完成登录——改为明确提示而不是拿空密码去撞柜台。要恢复「记住密码/自动登录」须接系统钥匙串（macOS Keychain / Windows DPAPI / libsecret），偏好只存引用。
- **预设硬编码**（第 7 项）：新增 `ConnectionPresets`（偏好键 `connector.presets` 里的 JSON 优先，无档/坏档/全无效/空清单一律回退内置，清单永不为空），`ConnectorViewModel.Presets` 绑定之，界面改绑 `{Binding Presets}`。
- **README**（第 2 项）：按实际 tab（连接器/交易/策略/钱包，2026-10-25 起再加「行情」）重写里程碑与组成表，补「密码不落盘」「3s 轮询」「委托手动刷新」的真实行为。

**未做（有意）**：第 6 项策略页接 ⑥ `IBacktestRuntime`/`ILiveTradingRuntime`（新增依赖 + 验证成本高）；第 8 项切 PackageReference 需先发包。

## 8. 本轮升级落地（2026-10-25）：行情页

- **新增「行情」tab**（首屏）：`MarketWatch/`（VM + 视图 + `MarketMath`）。零凭据冷启动订阅 MD，
  看板行原位更新（不重建集合，滚动位置不抖），列：合约/最新/涨跌/涨跌幅/买一/买量/卖一/卖量/
  成交量/持仓量/变动次数/基准/更新时间；口径条每秒刷新 tick 速率与最近 tick，静默 10s 点名提示。
- **涨跌口径统一**：`MarketMath.PickBasis`（昨结→昨收→首笔）同时用于行情页与交易页——交易页原来的
  「相对上一笔」会让同一价位反复推送时来回跳 0，既不是今日涨跌也不是真实波动。
- **无值哨兵**：`LastPrice<=0`（映射层已把 DBL_MAX 归零）保持上一笔，不把价格打成 0；空簿顶显示「—」。
- **测试 29 → 59 例**：新增 `MarketMathTests`（基准回退/百分比/方向容差/格式化/品种解析）与
  `QuoteRowTests`（首笔锁定基准/后补昨结替换/重复推送不虚增变动次数/空簿顶/断开重置）。
- **实跑验收（L2）**：应用启动即连远程前置并出数（ax001），本地重启 bot 后观察到 tick 的时间与价格
  推进（09:48:43 → 09:48:45，99.03125 → 99.015625，volume 10755 → 10756）。**上游已知问题**：
  同一笔 tick 会被前端重复下发若干次（bot 与桌面端日志都能看到连续同值行），属 ⑤ 的 tick 帧/重投问题，
  本仓不做掩盖——看板用「变动次数」而非推送次数来表达真实波动。
