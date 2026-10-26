using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aether.Desktop.MarketWatch;
using Aether.Workbench;

namespace Aether.Desktop.Tests;

/// <summary>
/// 行情前置清单的档案语义。这些规则决定「管理」对话框保存后重启是否还记得——
/// 用户档被坏 JSON/空清单打破时静默回退内置，用户会以为是自己的配置丢了却查不到报错，
/// 每条回退分支都钉住。
/// </summary>
public class MarketFrontPresetsTests
{
    private sealed class FakeStore : IPreferencesStore
    {
        public Dictionary<string, string?> Data { get; } = new();

        public Task<string?> GetAsync(string key, CancellationToken ct = default)
            => Task.FromResult(Data.GetValueOrDefault(key));

        public Task SetAsync(string key, string? value, CancellationToken ct = default)
        {
            Data[key] = value;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void 无档_回内置三个()
    {
        var fronts = MarketFrontPresets.Load(new FakeStore());

        Assert.Equal(3, fronts.Count);
        Assert.Equal("本地", fronts[0].Name);
        Assert.Equal(MarketWatchViewModel.LocalMdFront, fronts[0].MdFront);
        Assert.Equal("远程", fronts[1].Name);
        Assert.Equal(MarketWatchViewModel.RemoteMdFront, fronts[1].MdFront);
        Assert.Equal("SimNow", fronts[2].Name);
        Assert.Equal(MarketWatchViewModel.RealMdFront, fronts[2].MdFront);
    }

    [Fact]
    public void 坏_JSON_回内置()
    {
        var store = new FakeStore { Data = { ["market.fronts"] = "not-json" } };
        Assert.Equal(3, MarketFrontPresets.Load(store).Count);
    }

    [Fact]
    public void 空数组_回内置()
    {
        var store = new FakeStore { Data = { ["market.fronts"] = "[]" } };
        Assert.Equal(3, MarketFrontPresets.Load(store).Count);
    }

    [Fact]
    public void 用户档优先_并过滤无效条目与首尾空白()
    {
        var store = new FakeStore
        {
            Data = { ["market.fronts"] =
                """[{"name":"公司柜台","mdFront":"tcp://1.2.3.4:17802"},{"name":"  ","mdFront":"tcp://x:1"},{"name":"空址","mdFront":""},{"name":" 期货二 ","mdFront":" tcp://5.6.7.8:17802 "}]""" },
        };

        var fronts = MarketFrontPresets.Load(store);

        Assert.Equal(2, fronts.Count);
        Assert.Equal(("公司柜台", "tcp://1.2.3.4:17802"), (fronts[0].Name, fronts[0].MdFront));
        Assert.Equal(("期货二", "tcp://5.6.7.8:17802"), (fronts[1].Name, fronts[1].MdFront));   // trim 生效
    }

    [Fact]
    public void 保存_读取_往返一致()
    {
        var store = new FakeStore();
        var saved = new List<MarketFront>
        {
            new("公司柜台", "tcp://1.2.3.4:17802"),
            new("SimNow", MarketWatchViewModel.RealMdFront),
        };

        MarketFrontPresets.Save(store, saved);
        var loaded = MarketFrontPresets.Load(store);

        Assert.Equal(2, loaded.Count);
        Assert.Equal("公司柜台", loaded[0].Name);
        Assert.Equal("tcp://1.2.3.4:17802", loaded[0].MdFront);
        Assert.Equal("SimNow", loaded[1].Name);
    }

    [Fact]
    public void 保存空清单_清键_下次加载回内置()
    {
        var store = new FakeStore();
        MarketFrontPresets.Save(store, new List<MarketFront> { new("a", "tcp://x:1") });
        Assert.NotNull(store.Data["market.fronts"]);

        MarketFrontPresets.Save(store, new List<MarketFront>());   // 「全删光」= 恢复默认

        Assert.Null(store.Data["market.fronts"]);                  // 键被清
        Assert.Equal(3, MarketFrontPresets.Load(store).Count);     // 回内置
    }
}
