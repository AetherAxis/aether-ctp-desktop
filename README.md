# Aether.Desktop

**纯行情终端**——⑩ `aether-workbench` 布局引擎的第一个消费者。**单 tab：行情**（2026-10-26 收敛：
交易/钱包页按用户决定撤除、后面按需求回归；此前经 五 tab → 三 tab → 单 tab 两轮收敛，决策记录见
DESIGN §3.8/§11）。依赖闭包 = ⑩（引擎）+ ⑦（ICtpSession 官方链路；③② 传递）——与 console 同款组件。

## 功能

- **免登录 MD 看板**：只连行情腿（MD），冷启动自动连上并订阅（默认本地前置 `tcp://127.0.0.1:17802` +
  `ax001`），合约/名称/最新/涨跌/涨跌幅/买卖一档/成交量/持仓量/变动次数/时间一屏看完。bot 跑起来时
  「价格在动」直接看得见；静默 10s 状态栏点名（前置断了，或 MD 被别的账号重建）。
- **前置清单可配置（2026-10-26）**：「管理 ⚙」打开对话框增删改前置（名称 + 地址），保存即持久化并
  即时刷新 chips——重启保持最新配置（用户档 `market.fronts` 优先；无档/坏档/清空回退内置
  本地/远程/SimNow，清单永不为空）。当前选中的前置高亮，点 chip 即填入地址。
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
dotnet run --project aether-desktop/src/Aether.Desktop
```

**看行情（直连形态，2026-10-26 起标准）**：bot 直连远程前置（`~/aether-bots/start-direct.sh`）时，
行情页点「远程」chip + 「订阅」即与 bot 同源（`tcp://117.72.186.128:17802`，选择会记住）。远程前置
推流正常（2026-10-26 活跃行情实测 30 秒 72 笔）；但服务器部署的是旧构建、无同值快照去重——行情
活跃时 tick 推送量很大，看板用「变动次数」表达真实波动不受影响。

**看行情（本地前置形态，改 ⑤ 时）**：`start-remote.sh` 起本地前置后默认 `tcp://127.0.0.1:17802`。

- 订阅档案（前置/经纪商/用户/品种）落 `~/.aether/aether-desktop/preferences.json`；MD 密码**绝不落盘**
- 布局/主题/窗口尺寸经 ⑩ 引擎持久化（同一文件）

## 组成

| 文件 | 职责 |
|---|---|
| `App.axaml(.cs)` | 组合根：⑩ AddWorkbench（单 tab 布局）+ ⑦ AddCtpInfrastructure + 贡献者注册 |
| `DesktopWorkspace.cs` | 工作台定义（单 tab）+ App 自己的 PanelKeys + 回归指引注释 |
| `AppViewContributor.cs` | IViewRegistryContributor——行情 panel 的视图/VM 工厂注册进 ⑩ 引擎 |
| `JsonPreferencesStore.cs` | IPreferencesStore 的 JSON 文件实现（GetAsync 同步完成——引擎启动期同步恢复依赖此） |
| `MarketWatch/MarketFronts.cs` | 前置清单模型 + 档案（用户档 `market.fronts` 优先/坏档空档回退内置三 chip/空清单=清键恢复默认）——钉单测 |
| `MarketWatch/MarketWatchViewModel.cs` | 行情页 VM：MD 订阅（免登录）/ 真实前置自动爬主力合约 / 看板行原位更新 / 每秒速率与静默检测 / 前置清单管理 |
| `MarketWatch/ManageFrontsDialog.axaml(.cs)` + `ManageFrontsViewModel.cs` | 「管理行情前置」模态对话框：编辑副本（增/删/改），保存落档+重载 chips，取消丢弃 |
| `MarketWatch/MainFuturesCatalog.cs` | 期货主力合约目录——客户端直爬东方财富 mk0830（Web 服务端 `SecuritySyncService` 的移植；代码规整 + 月均过滤 + sc→交易所），`NormalizeCode` 钉单测 |
| `MarketWatch/MarketMath.cs` | 涨跌口径纯计算（昨结→昨收→首笔）+ 显示格式——被单测钉住 |
| `MarketWatch/MarketWatchView.axaml` | 行情看板视图：订阅栏（可配置前置 chips + 管理 ⚙ + MD 密码 + 主力合约按钮）+ 口径条 + 十四列表格 |

## 来历

2026-10-20/21 建：从 Aether monorepo 桌面端迁移而来但**不无脑复制**——新组合根 + ⑩ 引擎
（当日在 monorepo Workbench 基础上切核独立成仓）。完整决策记录见工作区根 `MIGRATION.md`。

## 测试

```bash
dotnet test aether-desktop.slnx    # tests/Aether.Desktop.Tests：60 例纯逻辑面
```

覆盖偏好存储（同步契约/坏档重建）、行情口径（涨跌基准回退/无值哨兵不归零/变动次数只数真实变化/
空簿顶显示破折号）、主力合约目录（解析/代码规整/月均过滤/坏 JSON 抛出/真实前置判据/仿真默认看板
判据）。控件行为与真实订阅仍靠实跑验收。
