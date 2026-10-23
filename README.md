# Aether.Desktop

轻量桌面 App——⑩ `aether-workbench` 布局引擎的第一个消费者。**四个 tab：连接器 / 交易 / 策略 / 钱包**
（见 `DesktopWorkspace.cs` 与 `App.axaml`）；
依赖闭包 = ⑩（引擎）+ ⑦（ICtpSession 官方链路；③② 传递）+ ①（链查询）+ ④（SimRpc 提币）——
与 console 同款组件，桌面端本就是这些组件的原始宿主。

## 里程碑

- **M1（当前）连接器 + 交易**：连接器 tab 管登录/预设/订阅（ICtpSession 单例，交易与钱包都在它之上）；
  交易 tab = 行情板 + 账户摘要 + 持仓/委托表 + 限价/市价下单。**「bot 在跑」的证据面**——持仓在动、
  MM 挂单躺在委托页、权益在变、tick 实时流；账户/持仓每 3s 自动轮询（委托表手动刷新——空表查询
  有 30s 超时的已知边界）。持仓的「浮动盈亏」取柜台口径 `PositionProfit`，不本地用价差估。
- M2 钱包页（已可用，随账本/交易所成型）：① Aether.Ledger 查链上余额 + ④ SimRpc `withdraw` 提币。
- M3 策略页（**桩**）：`RunBacktest`/`StartLive` 目前只改状态文本，尚未接 ⑥ `IBacktestRuntime`/`ILiveTradingRuntime`。

## 用法

```bash
dotnet run --project aether-desktop/src/Aether.Desktop
```

登录栏填前置地址（与连 SimNow 同形）：本地实验室 `tcp://127.0.0.1:17901/17902`、标准端口
`tcp://127.0.0.1:17801/17802`、服务器 `tcp://<服务器IP>:17801/17802`——账户/密码即交易所账户
（首登开户）。想看 bot 自己的账户就用 bot 的账户登录（如 `botmm/botmm`）。

- 登录档案（前置地址/经纪商/用户号/AppID/授权码）持久化到 `~/.aether/aether-desktop/preferences.json`；**密码不落盘**（冷启动需手输，自动登录因此需手输一次）——要恢复「记住密码」须接系统钥匙串
- 布局/主题/窗口尺寸经 ⑩ 引擎持久化（同一文件）
- **委托查询是手动刷新**（「刷新委托」按钮）——空表 SDK 不派发 17B 应答的已知边界（30s 超时 + 管线停摆），自动轮询会打断 tick 流；账户/持仓每 3s 自动轮询（空表正常回派）

## 组成

| 文件 | 职责 |
|---|---|
| `App.axaml(.cs)` | 组合根：⑩ AddWorkbench（四 tab 布局）+ ⑦ AddCtpInfrastructure + 贡献者注册 + WorkbenchServices.Attach |
| `DesktopWorkspace.cs` | 工作台定义（连接器/交易/策略/钱包四 tab）+ App 自己的 PanelKeys |
| `AppViewContributor.cs` | IViewRegistryContributor——四个 panel 的视图/VM 工厂注册进 ⑩ 引擎 |
| `JsonPreferencesStore.cs` | IPreferencesStore 的 JSON 文件实现（GetAsync 同步完成——引擎启动期同步恢复依赖此） |
| `Futures/FuturesViewModel.cs` | 期货页 VM：登录/订阅/行情板/账户/持仓/委托；3s 轮询；SDK 回调 UI 线程编组 |
| `Futures/FuturesView.axaml` | 期货页视图：登录栏 + 行情板 + 账户摘要 + 持仓/委托表 |
| `Strategy/`、`Wallet/` | 策略页（桩，待接 ⑥ 运行时）、钱包页（链查询 + 提币） |

## 来历

2026-10-20/21 建：从 Aether monorepo 桌面端迁移而来但**不无脑复制**——新组合根 + ⑩ 引擎
（当日在 monorepo Workbench 基础上切核独立成仓）+ 只摘三功能，WorkspaceDefinition 新写
（数据驱动布局的红利：新 App 的界面就是一份新定义）。完整决策记录见工作区根 `MIGRATION.md`。

## 测试

```bash
dotnet test aether-desktop.slnx    # tests/Aether.Desktop.Tests：29 例纯逻辑面
```

覆盖偏好存储（同步契约/坏档重建/密码键可彻底删除）、连接预设档案（用户档优先/坏档回退内置）、
显示格式（方向/开平/委托状态字符→中文，以 ③ 契约为准）。控件行为与真实登录/下单仍靠实跑验收。
