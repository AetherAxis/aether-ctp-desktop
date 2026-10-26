# Aether.Desktop 设计说明

> 本文件写「怎么设计 / 为什么这么设计 / 还能往哪升级」。工作区边界与迁移决策以根 `MIGRATION.md` 为准。

## 1. 定位与边界

⑪ 是 ⑩ `aether-workbench` 的消费者，也是 ⑦ `ctp-net-client`（`ICtpSession`）的原始宿主：登录、行情、账户/持仓/委托均在本进程完成，无服务端必需调用、无平台登录。

- **范围内**：行情——单 tab 纯行情终端（MD 看板 + 主力合约直爬；2026-10-26 收敛，见 §11）。
- **范围外**：交易/钱包（**已撤**，按需求从 git 历史回归）；AI/知识对象接线（归 ⑮ aether-ai-app）、打包分发；实盘交易由 ⑧ `CtpConsole --run-strategy` 承担。
- **仓库边界**：单项目，跨仓 `ProjectReference`（⑩+⑦），未包化。

## 2. 项目组成

| 目录/文件 | 职责 | 关键类型 | 出向依赖 |
|---|---|---|---|
| `Aether.Desktop.csproj` | `WinExe`/`net10.0`；macOS frameworks 复制 | — | ⑩、⑦（③②传递） |
| `Program.cs`、`App.axaml(.cs)` | 入口；DI、`AddCtpInfrastructure`、`AddWorkbench` | `Program`、`App` | Avalonia、⑩、⑦、DI |
| `DesktopWorkspace.cs` | PanelKeys + 单 tab 定义 + 交易/钱包回归指引 | `PanelKeys`、`DesktopWorkspace` | ⑩ |
| `AppViewContributor.cs` | 注册行情 panel 视图/VM 工厂 | `IViewRegistryContributor` | ⑩ |
| `JsonPreferencesStore.cs` | JSON 偏好，读写同步 | `JsonPreferencesStore` | ⑩ |
| `MarketWatch/` | 行情看板：MD 免登录订阅（**MD 唯一持有者**）、tick 原位更新、涨跌口径、真实前置自动爬主力合约 | `MarketWatchViewModel`、`MainFuturesCatalog`、`QuoteRow`、`MarketMath` | ⑦ |
| `Directory.Packages.props`、`ci.yml` | 中央包版本；Release 构建 + Test（checkout 四仓：⑩⑦③②） | — | Avalonia、MVVM、DI、Actions |

## 3. 关键设计决策

### 3.1 桌面端是这些组件的原始宿主
- **决策**：直接引用 ⑩/⑦/①/④，不复制会话/映射。
- **理由**：⑧ console 复用同款 session，协议不分叉。
- **代价**：跨仓编译耦合，CI checkout 六仓，上游改动即断构建。

### 3.2 视图与工作台均为声明式注册
- **决策**：`AppViewContributor` 按 panel key 注册视图/VM 工厂；四 tab 由 `PanelDescriptor` + `LayoutTabbed` 声明。
- **理由**：⑩ 零业务依赖；布局可序列化，插件产出同一份定义。
- **代价**：注册键是字符串，漏注册只在运行时空 panel。

### 3.3 ~~登录控制器独立成 VM、寄居交易页顶栏~~（交易页已随 2026-10-26 收敛撤除，见 §11）
- **历史决策**：登录/重连只在 `ConnectorViewModel`（连断唯一所有者）；UI 从独立 tab 收编为交易页顶栏。
- **现状**：`Connector/` 目录已删——行情页成为 App 里唯一的 `ICtpSession` 消费者（更简单：无共享连接
  时序问题）。交易能力回归时按 `DesktopWorkspace.cs` 注释恢复。

### 3.4 偏好 `GetAsync` 同步完成
- **决策**：返回 `Task.FromResult`；`SetAsync` 同步落盘、吞异常。
- **理由**：引擎启动期在 UI 线程同步恢复主题/窗口尺寸，真异步会死锁。
- **代价**：主线程写盘；磁盘故障静默。

### 3.5 ~~M1 交易页 = bot 运行证据面~~（交易页已随 2026-10-26 收敛撤除，见 §11）
- **历史决策**：登录栏 + 账户 + 持仓 + 委托；委托查询保持手动。
- **现状**：bot 运行证据面归 ⑧ console 日志 + 服务器链路；桌面端纯看行情。回归时从 git 历史取。

### 3.6 行情页独立成 tab，且不要求交易登录（2026-10-25）
- **决策**：新增「行情」tab 作为首屏，只连 MD 腿并自动订阅；交易登录归交易页顶栏（§3.3 收敛后）。
- **默认本地前置（2026-10-26 修正口径）**：默认值仍是本地 `127.0.0.1:17802`——理由是「行情随标准
  开发拓扑走」（两个 bot → 本地前置 → 远程交易所）。**远程前置同样会推流**：2026-10-26 活跃行情下
  复测，远程 17802 在 30 秒活跃窗口推 72 笔 tick（bid/ask/vol 全动）；2026-10-25 记的「远程只回
  订阅快照 2 帧、不推流」是市场静默期的误判，已推翻。远程 chip 的适用面因此扩大——bot 直连远程
  前置、本机不起本地前置的形态下它就是主路径。
- **理由**：用户的实际诉求是「两个 bot 跑起来时能肉眼看到价格在动」。原先把行情绑在交易页 ⇒ 必须先登录
  （密码不落盘，每次手输）、再手动订阅，看行情被交易流程挟持。本前置 MD 登录不校验密码
  （⑤ 明示「凭据不校验——行情订阅无账户语义」），所以仿真链行情可以零凭据冷启动。本地/远程一键切。
- **涨跌基准**：昨结 → 昨收 → 本会话首笔，逐行标注（`QuoteRow.Basis`）。**不拿「相对上一笔」冒充涨跌**——
  前置每秒推送快照，价格不动时那是恒 0；也不拿 0 当基准算成 -100%。本前置当前不下发昨结/昨收
  （⑤ 的 tick 模板没这两个槽，见 `ctp-net-front/FEATURE_MATRIX.md`），所以仿真链路显示「首笔」——
  界面把口径写出来，比给一个没来源的百分比诚实。
- **共用 MD 连接**：不另开一条私有 MD 连接，仍用 ⑦ 单例 `ICtpSession`（重连/重订阅逻辑已在那层硬化）。
  代价：连接器里换账号登录会重建 MD，行情页订阅失效——**显式转「静默 N 秒」并留『重新订阅』按钮**，
  不做后台互抢（两页偷偷重建对方订阅，比看得见的失效更难查）。
- **凭据口径（2026-10-25 实证收口）**：本前置/⑤ 与 SimNow 的 MD 腿都**不校验**——SimNow 实测
  `watching`+空密码登录/订阅/87 合约 tick 流全通（17:07 harness）。行情页因此加「MD 密码」框
  （会话内存、绝不落盘）：SimNow 留空即可，框留给其它需要凭据的真实前置（期货公司柜台）。
- **代价**：行情页与交易页共享一条 MD 连接，不能同时用两个账号订阅；静默检测是 1s 定时器，非推送式。

### 3.7 主力合约订阅面——客户端直爬，不经服务端（2026-10-25）
- **决策**：真实前置（SimNow）上的订阅面 = `MainFuturesCatalog` 直爬东方财富 mk0830 期货主力板块
  （一品种一条），连真实前置且品种还是仿真默认（ax001）时自动替换；「主力合约」按钮手动可触发。
- **理由**：原链路是 monorepo Web 服务端爬 → 证券库 → 客户端增量同步（`SecuritySyncService` +
  `SecurityClientSyncService`），本地桌面端没有服务端——把爬虫移植进客户端直接爬，正是
  「本地能力与 Web 服务解耦」的迁移方向。ax001 在真实柜台不存在，不替换则永远静默。
- **取舍**：
  - **爬取结果不落档**（不进 `SymbolInput`/偏好）：主力按月换约，存档跨月即过期；每次现爬现订，
    换月自动跟进。用户手输过品种（≠ ax001 默认）则尊重输入——那是明确的订阅意图。
  - **月均合约过滤**（pp2703F 一族）：非常规主力，订阅无意义且污染看板（实测 90 条中 3 条）。
  - **代码规整幂等**（CZCE 3 位月份/CFFEX 前缀大写，其余小写）：东财当前直接给规整形态，此步
    防源头大小写漂移；移植自 Stockapp `ToCtpInstrumentCode`，补 TL（三十债，原表漏）。
  - **SimNow 账号/密码不进仓**：仓里只有公开前置地址；凭据按工作区纪律留根 CLAUDE.md / 会话现填。

### 3.8 布局收敛：五 tab → 三 tab（2026-10-25，用户「之前的页面是不是多余了」）
- **决策**：行情 / 交易 / 钱包。撤「连接器」tab（收编为交易页顶栏，§3.3）；撤「策略」tab（假按钮桩，
  接 ⑥ 运行时时以真功能回归）；交易页自带行情板移除（与行情页完全重复，看行情归行情页）。
- **理由**：三块冗余——① 登录是手段不是目的地，独立成页即死空间；② 交易页行情板与行情页重复，
  且依赖连接器的 MD 订阅活着（两处 MD 订阅在同一 ⑦ 单例连接上互抢重建——DESIGN 3.6 记过的隐患
  一半源头在连接器）；③ 策略页的「回测/实盘」按钮只改状态文本——空壳 UI 比没有更糟。
- **取舍**：
  - MD 订阅唯一持有者 = 行情页（`MarketWatchViewModel`）。交易页 tick 只剩一个消费点：持仓「现价」列
    （浮盈仍走 3s 轮询的柜台口径 `PositionProfit`，不用价差自算）。行情页订阅面覆盖不到的持仓合约
    现价列静止——可接受（要看就切行情页/加订阅）。
  - 连接预设档案（`ConnectionPreset.Instruments`）保留字段但不再驱动订阅——用户档兼容 + 测试不动。
  - 删除 `PresetIsCurrentConverter`（零引用死代码）与 `TickRow`（随行情板移除）。
- **代价**：旧五 tab 布局档由 ⑩ 的容错恢复（实测启动无异常）；策略能力回归要等 ⑥ 接线批次。

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
| 1 | P0 | ~~无测试项目~~（已建，84 例，CI 已加 Test 步） | `tests/Aether.Desktop.Tests` | — | — |
| 2 | P0 | 文档与代码不一致 | README 曾与实际 tab 不符；已随 2026-10-25 布局收敛重写 | 保持文档随 tab 变更同步 | 无代码风险 |
| 3 | P1 | ~~持仓均价与浮亏错误~~（已修：柜台 `PositionCost`/`PositionProfit` 口径，见 §7） | — | — | — |
| 4 | P1 | ~~自动轮询未实现~~（已修：`DispatcherTimer` 3s，见 §7） | — | — | — |
| 5 | P1 | ~~密码明文落盘~~（已修：不落盘 + 清历史明文键，见 §7） | — | 恢复「记住密码」须接系统钥匙串 | 需跨平台抽象 |
| 6 | P2 | ~~策略页为桩~~（2026-10-25 布局收敛**已撤**——接 ⑥ `IBacktestRuntime`/`ILiveTradingRuntime` 时以真功能回归） | — | — | 新增依赖，验证成本高 |
| 7 | P2 | ~~预设硬编码~~（连接预设随交易页撤除一并删除，2026-10-26） | — | — | — |
| 8 | P2 | 跨仓源码耦合 | 两个 `ProjectReference`（⑩⑦）指上级目录；CI checkout 四仓 | P4 推进 PackageReference + 固定版本 | 需建包发布与版本对齐 |

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

## 9. 本轮升级落地（2026-10-25）：主力合约直爬 + 真实前置订阅

- **`MainFuturesCatalog`**：客户端直爬东方财富 mk0830 期货主力板块——monorepo Web 服务端
  `SecuritySyncService.SyncFromExternalAsync/ParseFutureData` 的移植（同 URL、同「写死不进配置」纪律），
  链路从「服务端爬 → 证券库 → 客户端增量同步」改为**本地直爬**。解析过滤 DCE 月均合约（pp2703F 一族，
  实测 90 条中 3 条）、`NormalizeCode` 规整（CZCE 3 位月份/CFFEX 前缀大写——含原表漏的 TL、其余小写）。
- **订阅面切换**：`SubscribeAsync` 在「真实前置 + 仿真默认品种（ax001）」时先爬再订（用户手输过品种则尊重
  输入）；「主力合约」按钮任何时候现爬现订。爬取结果**不落档**（主力按月换约，存档跨月即过期）。
  新增「SimNow」chip（`tcp://182.254.243.31:40011`，公开地址；账号/密码按纪律不进仓）。
- **MD 密码框**（会话内存、不落盘）：本前置 MD 不校验留空即可；真实前置校验凭据——console 真前置会话
  一直用登录凭据连 MD，行情页对齐该口径。
- **看板加「名称」列**：爬取目录带中文名（对二甲苯612 等），手输/仿真品种无来源则空。
- **测试 59 → 84 例**：`MainFuturesCatalogTests`（解析/规整/月均过滤/坏 JSON 抛出/uid 兜底/FetchAsync 走
  URL/非 2xx 抛出/真实前置判据/仿真默认看板判据）。
- **实跑验收（L2）**：`/tmp/aether-realfront-smoke`（仓外 harness，引用本仓 + ⑦）——
  ① 直爬实跑 87 条（过滤月均后）；② 87 合约整表订阅经官方 SDK 到**本远程前置**（`117.72.186.128:17802`）
  登录/订阅零异常，交易所解析出的 3 个已知合约（i2701/m2701/au261，服务器交易所历史残留合约表）收到快照
  tick；③ ax001 对照实跑有活跃行情（vol=12296/oi=5120）；④ **SimNow 真前置验收（当日 17:07 前置从
  断连窗口恢复后重跑 harness）**：免凭据 MD 登录（user=watching/空密码）+ 87/87 合约全部有 tick——
  60s 窗口 6307 笔（约 105 tick/s），jd2612 3296 / sc2612 728 / sn2611 384290 等真实盘口带 bid/ask/vol/oi。
  下午 14 时的「不可达」是 SimNow 天级断连窗口（两端口 TCP 超时约 3 小时），非代码问题。

## 10. 本轮落地（2026-10-25）：布局收敛五 tab → 三 tab

- **三 tab**：行情 / 交易 / 钱包（`DesktopWorkspace`）。决策与理由见 §3.8；旧五 tab 布局档由 ⑩ 容错恢复。
- **交易页收编登录栏**：`FuturesView` 顶两行 = 预设 chips + 交易前置/用户号/密码 + 连接/断开 + 状态
  （主行）与经纪商/行情前置/AppId/授权码/自动连接（高级行），绑定源 `FuturesViewModel.Connector`
  （`ConnectorViewModel` 保持连断唯一所有者）。删除 `ConnectorView` 与 `PresetIsCurrentConverter`（零引用）。
- **连接器 MD 逻辑删除**：`Instruments` 字段、预设切换订阅、登录后订阅全部移除——MD 唯一持有者
  是行情页，交易页零 MD 代码（消除同连接互抢重建）。
- **交易页行情板移除**：`Quotes`/`TickRow` 删除，tick 只更新持仓「现价」（`_lastPrice` 缓存 +
  `PositionRow.CurrentPrice`）；持仓/委托表转全宽。
- **策略页撤除**：`Strategy/` 三文件删除（假按钮桩）；PanelKeys/贡献者/App DI 同步收敛。
- **测试**：84/84 通过（纯逻辑面不涉 UI 布局，无需新增——布局属 GUI 实跑验收面）。
- **实跑验收（L2）**：重构后应用启动无异常（旧五 tab 布局档恢复正常），行情 tab 冷启动自动连本地
  前置出数（ax001 19:35:35 tick）；三 tab 形态与登录栏布局留用户日常使用核实。

## 11. 本轮落地（2026-10-26）：收敛为单 tab 纯行情终端

- **用户决定**：「交易和钱包可以先去掉吧，后面再按需求补」——桌面端当前就是一台行情终端。
  删除 `Futures/`（含 `Connector/` 登录控制器——两者本为一体）与 `Wallet/` 三目录及其测试
  （DisplayFormat/ConnectionPresets，84→60 例，其余全绿）。
- **依赖闭包随之收窄**：① `Aether.Ledger` 与 ④ `Aether.Exchange.Rpc` 两个 ProjectReference 移除
  （钱包页是它们在仓内的唯一消费者）；CI checkout 六仓 → 四仓（⑩⑦③②）。回归交易/钱包时按
  `DesktopWorkspace.cs` 注释加回。
- **ICtpSession 单消费者**：行情页是 App 里唯一的 session 消费者——共享 MD 连接的时序问题
  （§3.6 记过的「互抢/静默」）在这个形态下不存在。
- **实测启动**：单 tab 布局冷启动直连远程前置出数正常（旧三 tab 布局档被 ⑩ 容错恢复）。

## 12. 本轮落地（2026-10-26）：前置清单可配置——管理对话框 + 持久化

- **用户诉求**：「连接选择需要弄成配置的，比如弹出对话框，然后配置后会保持最新配置。」
- **模型**（`MarketFronts.cs`）：chip 数据源 = `MarketFront {Name, MdFront, IsCurrent}`；
  档案键 `market.fronts`（JSON `[{name, mdFront}]`）。**用户档优先，内置兜底**（本地/远程/SimNow
  原值——同此前写死的三个 chip）；无档/坏 JSON/过滤后为空一律回内置（清单永不为空）；
  保存空清单 = 清键 = 恢复默认（对话框里明示）。与旧 ConnectionPresets 同一套回退语义。
- **对话框**（`ManageFrontsDialog`）：模态 Window 编辑**副本**（`FrontEntry` 增删改）；「保存」→ VM 落档 +
  重载 chips + 状态栏确认；取消/关窗丢弃。VM 不持窗口引用——关窗经 `CloseRequested` 事件由 code-behind 订阅。
- **chips 数据驱动**：写死的 远程/本地/SimNow 三按钮换成 `ItemsControl` 绑定 `Fronts`；当前地址匹配的
  chip 高亮（`IsCurrent` 由 VM 在 `OnMdFrontChanged` 重算——地址是身份，改名不影响）。
- **选中态持久化**：点 chip 只填地址（与手输同路），「订阅」时随 `market.mdFront` 落档——配置后保持最新。
- **测试**：+6 例（MarketFrontPresetsTests：无档/坏 JSON/空数组回内置、用户档过滤与 trim、保存读取
  往返、空清单清键恢复默认）；60→66 例全绿。对话框交互面（模态/编辑/保存链）属 GUI 实跑验收。

## 13. 本轮落地（2026-10-26）：连接管理对齐 monorepo 连接器——完整档案 + ⑦ 凭据仓

- **用户纠偏**：「管理不仅仅是前置服务器，要把整个配置都可配置——参考 Aether 连接器中的期货连接。」
  §12 的「前置清单」（仅名称+地址，存 preferences.json）当日重做。
- **参照**：monorepo Stockapp `CtpConnectorViewModel`——档案 = 完整 `CtpConnectionConfig`（③ 契约现成），
  存 `ICtpConfigStore`（⑦ SQLite 凭据仓，0600）；chip = 档案、点击 = 套用 + 连接；**连接即自动建档**
  （键 = 前置 + 用户号，失败尝试也留档）；last-used 档案驱动启动恢复 + AutoLogin。
- **移植取舍**：
  - 键 = **行情前置** + 用户号（本 App 无交易腿；交易前置/AppId/授权码随档案留档——交易页回归时即用）。
  - 密码存凭据仓（monorepo 同款；**preferences.json 仍绝不存密码**——DESIGN 的「密码不落盘」条款
    精确化为「不落明文偏好」）。top bar 的 MD 密码框仍在（套档案带出/手改，连接即建档存回）。
  - 冷启动自动订阅改由 last-used 档案的 AutoLogin 门控（内置档案默认开 = 旧行为不变）。
  - 仓空播种内置三档案（本地/远程/SimNow，**各分配 Guid**——测试逮住过三档同 Guid.Empty 互踩）；
    「管理」里全删光后重启恢复内置（与 §12 同语义）；旧 `market.*` 偏好一次性迁移（命中内置标
    last-used，地址不在内置补「上次使用」档）。
  - **自动爬主力的触发判据换端口族**：`MarketProfiles.IsSimFrontAddress`（任意 host 的 :17802 /
    sim / 127.0.0.1:17800 = 仿真族；其余 = 真实柜台）——镜像 monorepo 连接器的 IsSimFront 判据，
    用户自配的期货公司前置也自动走主力爬取（此前只认 SimNow 常量精确匹配）。
- **测试**：+10 例（IsSimFrontAddress 端口族 11 断言 / 内置清单字段与 Guid 唯一 / 迁移三分支 /
  档案名后缀 / 前置标签），66→76 例全绿；对话框与 chip 交互面属 GUI 实跑验收。

## 14. 本轮落地（2026-10-26）：顶栏去零散配置框——连接字段只活在档案里

- **用户定调**：「配置在管理中了，页面管理后面的配置就应该去掉。」顶栏撤掉 行情前置/经纪商/用户/MD 密码
  四组输入框——连接配置的唯一入口 = 档案（chips 套用 /「管理 ⚙」编辑）。顶栏只剩：档案 chips + 管理 ⚙ +
  品种 + 主力合约 + 订阅/断开 + 状态。
- VM 的 MdFront/Broker/User/MdPassword 保留为**内部连接状态**（档案套用进来、订阅与建档消费），
  只是不再绑输入框——临时改配置的路径从页面消失，统一走档案。

## 15. 本轮落地（2026-10-26）：ax001 名称兜底（名称列）

- 板上 ax001 行的「名称」列从空 → **Aether币价期货**：`SimInstrumentNames` 内置兜底（与 ④
  InstrumentCatalog 品种表同源同值——只此一条，不臆造其它名字）。权威源在服务端（④/RPC 已带
  `Name`）；⑤ CTP wire「查合约」名称槽未逆向（FEATURE_MATRIX §1-8 follow-up）前，桌面端用内置
  兜底，wire 补齐后改走服务端名。爬取目录（真实品种）名称不受影响。
