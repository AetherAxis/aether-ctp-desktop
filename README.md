# Aether.CtpDesktop

**纯行情终端**——⑩ `aether-workbench` 布局引擎的第一个消费者。**单 tab：行情**（2026-10-26 收敛：
交易/钱包页按用户决定撤除、后面按需求回归；此前经 五 tab → 三 tab → 单 tab 两轮收敛，决策记录见
DESIGN §3.8/§11）。依赖闭包 = ⑩（引擎）+ ⑦（ICtpSession 官方链路；③② 传递）——与 console 同款组件。

## 功能

- **免登录 MD 看板**：只连行情腿（MD），冷启动自动连上并订阅（默认本地前置 `tcp://127.0.0.1:17802` +
  `ax001`），合约/名称（ax001=Aether币价期货，真实品种爬取带出）/最新/涨跌/涨跌幅/买卖一档/成交量/持仓量/变动次数/时间一屏看完。bot 跑起来时
  「价格在动」直接看得见；静默 10s 状态栏点名（前置断了，或 MD 被别的账号重建）。
- **连接档案可配置（2026-10-26，对齐 monorepo 连接器的期货连接）**：chips = **完整连接档案**
  （③ 契约 `CtpConnectionConfig`：交易前置/行情前置/经纪商/用户号/密码/AppId/授权码/AutoLogin），
  存 **⑦ 凭据仓 ctp.db（0600）**。「管理 ⚙」对话框增删改全字段，保存即持久化并保持最新；
  **点 chip = 套用全部连接字段 + 直接订阅**；**连接成功即自动建档**（键 = 行情前置 + 用户号）；
  启动按 last-used 档案恢复（AutoLogin 旗标决定是否自动订阅）。仓空/全删光后重启 = 恢复内置
  （本地/远程/SimNow）；旧 `market.*` 偏好首次启动一次性迁移进仓。
- **真实前置（SimNow）模式**：「SimNow」chip 一键切 `tcp://182.254.243.31:40011`（公开地址，账号/密码
  不进仓）——连真实前置且品种还是仿真默认（ax001）时，订阅面自动替换为**客户端直爬的期货主力合约
  目录**（东方财富 mk0830 板块，一品种一条约 87 个；`MainFuturesCatalog`——移植自原 Web 服务端的爬虫，
  不经任何服务端）；「主力合约」按钮任何时候现爬现订。SimNow MD 实测免凭据（密码框留空即可，框留给
  期货公司柜台这类校验凭据的前置）。主力按月换约，爬取结果不落档、每次现爬。
- **交易（登录/账户/持仓/委托）与钱包（AQ 充提）已撤**（2026-10-26）——需要时从 git 历史回归：
  `Futures/`+`Connector/`+`Wallet/` 三目录与 ①④ 两个 ProjectReference、CI 里两段 checkout 一并加回
  （见 `DesktopWorkspace.cs` 注释）。实盘期间交易面本就由 ⑧ `CtpConsole --run-strategy` 承担。

## 用法

```bash
dotnet run --project aether-ctp-desktop/src/Aether.CtpDesktop
```

**看行情（直连形态，2026-10-26 起标准）**：bot 直连远程前置（`~/aether-bots/start-direct.sh`）时，
行情页点「远程」chip + 「订阅」即与 bot 同源（`tcp://117.72.186.128:17802`，选择会记住）。远程前置
推流正常（2026-10-26 活跃行情实测 30 秒 72 笔）；但服务器部署的是旧构建、无同值快照去重——行情
活跃时 tick 推送量很大，看板用「变动次数」表达真实波动不受影响。

**看行情（本地前置形态，改 ⑤ 时）**：`start-remote.sh` 起本地前置后默认 `tcp://127.0.0.1:17802`。

- 连接档案（前置/经纪商/用户/密码/AppId/授权码）存 `~/.aether/aether-desktop/ctp.db`（⑦ 凭据仓，0600）；
  订阅品种落 `preferences.json`（`market.symbols`）——**密码绝不进 preferences.json**
- 布局/主题/窗口尺寸经 ⑩ 引擎持久化（同一文件）

## 组成

| 文件 | 职责 |
|---|---|
| `App.axaml(.cs)` | 组合根：⑩ AddWorkbench（单 tab 布局）+ ⑦ AddCtpInfrastructure + 贡献者注册 |
| `DesktopWorkspace.cs` | 工作台定义（单 tab）+ App 自己的 PanelKeys + 回归指引注释 |
| `AppViewContributor.cs` | IViewRegistryContributor——行情 panel 的视图/VM 工厂注册进 ⑩ 引擎 |
| `JsonPreferencesStore.cs` | IPreferencesStore 的 JSON 文件实现（GetAsync 同步完成——引擎启动期同步恢复依赖此） |
| `MarketWatch/MarketProfiles.cs` | 连接档案纯逻辑面：内置清单播种 / 旧偏好一次性迁移 / 仿真前置端口判据（:17802 族 = 仿真，其余 = 真实柜台触发主力爬取）/ 档案名与前置标签——钉单测 |
| `MarketWatch/MarketWatchViewModel.cs` | 行情页 VM：MD 订阅（免登录）/ 真实前置自动爬主力合约 / 看板行原位更新 / 每秒速率与静默检测 / 连接档案（chip 套用+订阅、连接即建档、管理对话框） |
| `MarketWatch/ManageProfilesDialog.axaml(.cs)` + `ManageProfilesViewModel.cs` | 「管理连接档案」模态对话框：档案 chips 选条目 → 全字段表单（副本编辑），保存 diff 落 ⑦ 凭据仓，取消丢弃 |
| `MarketWatch/MainFuturesCatalog.cs` | 期货主力合约目录——客户端直爬东方财富 mk0830（Web 服务端 `SecuritySyncService` 的移植；代码规整 + 月均过滤 + sc→交易所），`NormalizeCode` 钉单测 |
| `MarketWatch/MarketMath.cs` | 涨跌口径纯计算（昨结→昨收→首笔）+ 显示格式——被单测钉住 |
| `MarketWatch/MarketWatchView.axaml` | 行情看板视图：订阅栏（连接档案 chips + 管理 ⚙ + 品种 + 主力合约/订阅/断开——连接字段全在档案里，页面零散配置框已撤）+ 口径条 + 十四列表格 |

## 来历

2026-10-20/21 建：从 Aether monorepo 桌面端迁移而来但**不无脑复制**——新组合根 + ⑩ 引擎
（当日在 monorepo Workbench 基础上切核独立成仓）。完整决策记录见工作区根 `MIGRATION.md`。

2026-10-28 改名：`aether-desktop` → `aether-ctp-desktop`（程序集/命名空间 `Aether.Desktop` → `Aether.CtpDesktop`）。
理由同 ⑧ `aether-apps` → `aether-ctp-hosts`：本仓只装 CTP 一条线的客户端 UI，旧名说不出是哪条线。
⚠ **用户数据目录 `~/.aether/aether-desktop/` 保持不变**——里面是 `ctp.db`（连接档案含密码，0600）与
`preferences.json`（布局/主题/订阅品种），改目录名只会让已存档案变孤儿。

## 测试

```bash
dotnet test aether-ctp-desktop.slnx    # tests/Aether.CtpDesktop.Tests：76 例纯逻辑面（2026-10-28 实测；纯逻辑面不涉 UI 布局）
```

覆盖偏好存储（同步契约/坏档重建）、行情口径（涨跌基准回退/无值哨兵不归零/变动次数只数真实变化/
空簿顶显示破折号）、主力合约目录（解析/代码规整/月均过滤/坏 JSON 抛出/真实前置判据/仿真默认看板
判据）。控件行为与真实订阅仍靠实跑验收。
