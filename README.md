# Aether.Desktop

轻量桌面 App——⑩ `aether-workbench` 布局引擎的第一个消费者。**期货 / 回测 / 钱包**三页；
依赖闭包 = ⑩（引擎）+ ⑦（ICtpSession 官方链路；③② 传递）——与 console 同款组件，桌面端本就是这些组件的原始宿主。

## 里程碑

- **M1（当前）期货页**：连前置（本地/服务器同形）→ 行情板 + 账户/持仓/委托查询。**「bot 在跑」的证据面**——持仓在动、MM 挂单躺在委托页、权益在变、tick 实时流。
- M2（规划）钱包页：① Aether.Ledger（AQ 充值/提币——对接 ⑧ `doc/funding-runbook.md` 的日常操作面）。
- M3（规划）回测页：⑥ `IBacktestRuntime`（LeanBacktestRunner）+ 策略选择/运行。

## 用法

```bash
dotnet run --project aether-desktop/src/Aether.Desktop
```

登录栏填前置地址（与连 SimNow 同形）：本地实验室 `tcp://127.0.0.1:17901/17902`、标准端口
`tcp://127.0.0.1:17801/17802`、服务器 `tcp://<服务器IP>:17801/17802`——账户/密码即交易所账户
（首登开户）。想看 bot 自己的账户就用 bot 的账户登录（如 `botmm/botmm`）。

- 登录档案持久化到 `~/.aether/aether-desktop/preferences.json`（密码明文——本地 sim 工具，同 console config.json 语义）
- 布局/主题/窗口尺寸经 ⑩ 引擎持久化（同一文件）
- **委托查询是手动刷新**（「刷新委托」按钮）——空表 SDK 不派发 17B 应答的已知边界（30s 超时 + 管线停摆），自动轮询会打断 tick 流；账户/持仓每 3s 自动轮询（空表正常回派）

## 组成

| 文件 | 职责 |
|---|---|
| `App.axaml(.cs)` | 组合根：⑩ AddWorkbench（三 tab 布局）+ ⑦ AddCtpInfrastructure + 贡献者注册 + WorkbenchServices.Attach |
| `DesktopWorkspace.cs` | 工作台定义（期货/回测/钱包三 tab）+ App 自己的 PanelKeys |
| `AppViewContributor.cs` | IViewRegistryContributor——三 panel 的视图/VM 工厂注册进 ⑩ 引擎 |
| `JsonPreferencesStore.cs` | IPreferencesStore 的 JSON 文件实现（GetAsync 同步完成——引擎启动期同步恢复依赖此） |
| `Futures/FuturesViewModel.cs` | 期货页 VM：登录/订阅/行情板/账户/持仓/委托；3s 轮询；SDK 回调 UI 线程编组 |
| `Futures/FuturesView.axaml` | 期货页视图：登录栏 + 行情板 + 账户摘要 + 持仓/委托表 |
| `Backtest/`、`Wallet/` | M3/M2 占位页 |

## 来历

2026-10-20/21 建：从 Aether monorepo 桌面端迁移而来但**不无脑复制**——新组合根 + ⑩ 引擎
（当日在 monorepo Workbench 基础上切核独立成仓）+ 只摘三功能，WorkspaceDefinition 新写
（数据驱动布局的红利：新 App 的界面就是一份新定义）。完整决策记录见工作区根 `MIGRATION.md`。
