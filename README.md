# Aether.Desktop

轻量桌面 App——⑩ `aether-workbench` 布局引擎的第一个消费者。**三个 tab：行情 / 交易 / 钱包**
（2026-10-25 布局收敛，原五 tab——见 DESIGN 3.8）；
依赖闭包 = ⑩（引擎）+ ⑦（ICtpSession 官方链路；③② 传递）+ ①（链查询）+ ④（SimRpc 提币）——
与 console 同款组件，桌面端本就是这些组件的原始宿主。

## 里程碑

- **M0 行情页（2026-10-25）观测面**：只连行情腿（MD）的看板——**不需要交易账号/密码**，冷启动自动
  连上并订阅（默认本地前置 `tcp://127.0.0.1:17802` + `ax001`），合约/名称/最新/涨跌/涨跌幅/买卖一档/
  成交量/持仓量/变动次数/时间一屏看完。两个 bot 跑起来时，「价格在动」直接看得见：变动次数在涨、
  买卖一档在跳、tick/s 在跑；行情静默 10s 会在状态栏点名（前置断了，或 MD 被别的账号重建）。
  **真实前置（SimNow）模式（2026-10-25）**：「SimNow」chip 一键切真前置——连真实前置且品种还是
  仿真默认（ax001）时，订阅面自动替换为**客户端直爬的期货主力合约目录**（东方财富 mk0830 板块，
  一品种一条，约 87 个；`MainFuturesCatalog`——移植自原 Web 服务端的爬虫，不再经服务端/证券库同步）；
  「主力合约」按钮任何时候可手动现爬现订（主力按月换约，爬取结果不落档）。
- **M1 交易页**：**登录栏（原「连接器」tab 收编为顶栏——预设 chips + 前置/用户号/密码 + 高级字段）**
  + 账户摘要 + 持仓 + 委托表。**「bot 在跑」的证据面**——持仓在动、MM 挂单躺在委托页、权益在变；
  账户/持仓每 3s 自动轮询（委托表手动刷新——空表查询有 30s 超时的已知边界）。持仓的「浮动盈亏」
  取柜台口径 `PositionProfit`，不本地用价差估。**下单入口未做**（界面与 VM 均无报单命令）。
  交易页的行情板已随布局收敛移除——看行情归行情页（MD 订阅唯一持有者），tick 只用来更新持仓现价。
- M2 钱包页（已可用，随账本/交易所成型）：① Aether.Ledger 查链上余额 + ④ SimRpc `withdraw` 提币。
- 策略页：**假按钮桩已撤（2026-10-25）**——接 ⑥ `IBacktestRuntime`/`ILiveTradingRuntime` 时以真功能回归，不做只改状态文本的空壳。

## 用法

```bash
dotnet run --project aether-desktop/src/Aether.Desktop
```

**看行情（仿真链）**：启动即进行情页——行情前置默认本地 `tcp://127.0.0.1:17802`（转连远程交易所；「远程」chip 切
`tcp://117.72.186.128:17802`），品种默认 `ax001`；改完点「订阅」。订阅档案（前置/经纪商/用户/品种）落
`preferences.json`，下次启动照旧自动连。**本前置的行情腿不校验密码**，仿真链看行情不需要任何账号。

**看行情（真实前置 SimNow）**：「SimNow」chip 切 `tcp://182.254.243.31:40011`（公开地址，账号/密码不进仓），
点「订阅」时若品种还是 ax001 默认，先爬主力合约目录再整表订阅；爬取失败则按输入品种订阅并提示。
主力目录每次现爬（87 个左右，DCE 月均合约已过滤）——换月自动跟进，不依赖任何服务端。
**SimNow 的 MD 腿实测不校验凭据**（2026-10-25：watching+空密码登录、87/87 合约 tick 流全通）——
用户/MD 密码随意；「MD 密码」框留给其它需要凭据的真实前置（期货公司柜台），会话内存不落盘。

**要交易/看账户**：交易页顶栏就是登录栏——预设 chips（本地实验室/标准端口/服务器）填好前置，
账户/密码即交易所账户（首登开户）。想看 bot 自己的账户就用 bot 的账户登录（如 `botmm/botmm`）。
高级字段（经纪商/行情前置/AppId/授权码）预设会带，手动换柜台时才碰。

- 登录档案（前置地址/经纪商/用户号/AppID/授权码）持久化到 `~/.aether/aether-desktop/preferences.json`；**密码不落盘**（冷启动需手输，自动登录因此需手输一次）——要恢复「记住密码」须接系统钥匙串
- **MD 订阅的唯一持有者是行情页**——交易页不再碰行情连接（原连接器 tab 的 MD 订阅逻辑已删，
  消除了两页在同一 ⑦ 单例连接上互抢重建的隐患）
- 布局/主题/窗口尺寸经 ⑩ 引擎持久化（同一文件）
- **委托查询是手动刷新**（「刷新」按钮）——空表 SDK 不派发 17B 应答的已知边界（30s 超时 + 管线停摆），自动轮询会打断 tick 流；账户/持仓每 3s 自动轮询（空表正常回派）

## 组成

| 文件 | 职责 |
|---|---|
| `App.axaml(.cs)` | 组合根：⑩ AddWorkbench（三 tab 布局）+ ⑦ AddCtpInfrastructure + 贡献者注册 + WorkbenchServices.Attach |
| `DesktopWorkspace.cs` | 工作台定义（行情/交易/钱包三 tab）+ App 自己的 PanelKeys |
| `AppViewContributor.cs` | IViewRegistryContributor——三个 panel 的视图/VM 工厂注册进 ⑩ 引擎 |
| `JsonPreferencesStore.cs` | IPreferencesStore 的 JSON 文件实现（GetAsync 同步完成——引擎启动期同步恢复依赖此） |
| `MarketWatch/MarketWatchViewModel.cs` | 行情页 VM：MD 订阅（免登录）/ 真实前置自动爬主力合约 / 看板行原位更新 / 每秒速率与静默检测；`QuoteRow.Apply` 吃 tick |
| `MarketWatch/MainFuturesCatalog.cs` | 期货主力合约目录——客户端直爬东方财富 mk0830（Web 服务端 `SecuritySyncService` 的移植；代码规整 + 月均过滤 + sc→交易所），`NormalizeCode` 钉单测 |
| `MarketWatch/MarketMath.cs` | 涨跌口径纯计算（昨结→昨收→首笔）+ 显示格式——被单测钉住 |
| `MarketWatch/MarketWatchView.axaml` | 行情看板视图：订阅栏（远程/本地/SimNow chips + MD 密码 + 主力合约按钮）+ 口径条 + 十四列表格 |
| `Connector/ConnectorViewModel.cs` | 登录控制器（寄居交易页顶栏）：LoginAsync/Logout + 预设 + 登录档案；**不碰 MD**（行情页唯一持有） |
| `Connector/ConnectionPresets.cs` | 连接预设（用户档优先/坏档回退内置） |
| `Futures/FuturesViewModel.cs` | 交易页 VM：挂 `Connector`（登录栏绑定源）+ 账户/持仓/委托轮询与查询；tick 只更新持仓现价 |
| `Futures/FuturesView.axaml` | 交易页视图：登录主行 + 高级字段行 + 账户摘要 + 持仓 + 委托（无行情板——看行情归行情页） |
| `Wallet/` | 钱包页（链查询 + 提币） |

## 来历

2026-10-20/21 建：从 Aether monorepo 桌面端迁移而来但**不无脑复制**——新组合根 + ⑩ 引擎
（当日在 monorepo Workbench 基础上切核独立成仓）+ 只摘三功能，WorkspaceDefinition 新写
（数据驱动布局的红利：新 App 的界面就是一份新定义）。完整决策记录见工作区根 `MIGRATION.md`。

## 测试

```bash
dotnet test aether-desktop.slnx    # tests/Aether.Desktop.Tests：84 例纯逻辑面
```

覆盖偏好存储（同步契约/坏档重建/密码键可彻底删除）、连接预设档案（用户档优先/坏档回退内置）、
显示格式（方向/开平/委托状态字符→中文，以 ③ 契约为准）、行情口径（涨跌基准回退/无值哨兵不归零/
变动次数只数真实变化/空簿顶显示破折号）、主力合约目录（解析/代码规整/月均过滤/坏 JSON 抛出/
真实前置判据/仿真默认看板判据）。控件行为与真实登录/下单仍靠实跑验收。
