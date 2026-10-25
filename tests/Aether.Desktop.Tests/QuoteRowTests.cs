using Aether.Desktop.MarketWatch;
using Ctp.Net;

namespace Aether.Desktop.Tests;

/// <summary>
/// 行情行的吃 tick 行为（不碰控件，纯状态机）。重点钉三件事：
/// ① 基准一旦确定就不再被后来的「无值」tick 冲掉；② 柜台无值哨兵（0）不能把价格打成 0；
/// ③ 「变动次数」只数真实价格变化——这正是「行情在动」的证据面，重复推送不能虚增。
/// </summary>
public class QuoteRowTests
{
    private static CtpTick Tick(
        double last, double bid = 0, double ask = 0, int bidVolume = 0, int askVolume = 0,
        long volume = 0, double openInterest = 0, double preSettlement = 0, double preClose = 0,
        string time = "09:30:05", int millisec = 7) => new()
    {
        InstrumentId = "ax001",
        LastPrice = last,
        BidPrice = bid, BidVolume = bidVolume,
        AskPrice = ask, AskVolume = askVolume,
        Volume = volume, OpenInterest = openInterest,
        PreSettlement = preSettlement, PreClose = preClose,
        UpdateTime = time, UpdateMillisec = millisec,
    };

    [Fact]
    public void 首笔无柜台基准时用首笔价且涨跌为零()
    {
        var row = new QuoteRow { Instrument = "ax001" };
        row.Apply(Tick(99.5));

        Assert.Equal(MarketMath.BasisFirst, row.Basis);
        Assert.Equal(99.5, row.Reference, 9);
        Assert.Equal("99.50", row.LastText);
        Assert.Equal("0.00", row.ChangeText);
        Assert.Equal("0.00%", row.PercentText);
        Assert.Equal(0, row.Moves);
        Assert.True(row.HasQuote);
    }

    [Fact]
    public void 价格上行时涨跌与方向与变动次数()
    {
        var row = new QuoteRow { Instrument = "ax001" };
        row.Apply(Tick(100));
        row.Apply(Tick(101));
        row.Apply(Tick(101));   // 重复推送：价格没变，变动次数不增

        Assert.Equal("101.00", row.LastText);
        Assert.Equal("+1.00", row.ChangeText);
        Assert.Equal("+1.00%", row.PercentText);
        Assert.True(row.IsUp);
        Assert.False(row.IsDown);
        Assert.Equal(1, row.Moves);
    }

    [Fact]
    public void 价格下行()
    {
        var row = new QuoteRow { Instrument = "ax001" };
        row.Apply(Tick(100));
        row.Apply(Tick(98.5));

        Assert.Equal("-1.50", row.ChangeText);
        Assert.Equal("-1.50%", row.PercentText);
        Assert.True(row.IsDown);
        Assert.Equal(1, row.Moves);
    }

    [Fact]
    public void 有昨结就用昨结当基准()
    {
        var row = new QuoteRow { Instrument = "ax001" };
        row.Apply(Tick(99, preSettlement: 90));

        Assert.Equal(MarketMath.BasisSettlement, row.Basis);
        Assert.Equal("+9.00", row.ChangeText);
        Assert.Equal("+10.00%", row.PercentText);
    }

    [Fact]
    public void 后补的昨结会替换首笔基准()
    {
        var row = new QuoteRow { Instrument = "ax001" };
        row.Apply(Tick(99));                        // 首笔：柜台还没给昨结
        Assert.Equal(MarketMath.BasisFirst, row.Basis);

        row.Apply(Tick(99, preSettlement: 90));     // 昨结到了 → 改用昨结
        Assert.Equal(MarketMath.BasisSettlement, row.Basis);
        Assert.Equal("+9.00", row.ChangeText);
    }

    [Fact]
    public void 无值哨兵不能把价格归零()
    {
        var row = new QuoteRow { Instrument = "ax001" };
        row.Apply(Tick(99, volume: 10));
        row.Apply(Tick(0, volume: 10));   // 本笔 LastPrice=0（无值）

        Assert.Equal("99.00", row.LastText);
        Assert.Equal(0, row.Moves);       // 没有真实变动
    }

    [Fact]
    public void 空簿顶显示破折号而不是0()
    {
        var row = new QuoteRow { Instrument = "ax001" };
        row.Apply(Tick(99, bid: 0, ask: 0));

        Assert.Equal("—", row.BidText);
        Assert.Equal("—", row.AskText);
        Assert.Equal("—", row.BidVolumeText);
        Assert.Equal("—", row.AskVolumeText);
    }

    [Fact]
    public void 买卖一档量与时间毫秒()
    {
        var row = new QuoteRow { Instrument = "ax001" };
        row.Apply(Tick(99, bid: 98.5, ask: 99.5, bidVolume: 3, askVolume: 7, volume: 1234, openInterest: 88));

        Assert.Equal("98.50", row.BidText);
        Assert.Equal("99.50", row.AskText);
        Assert.Equal("3", row.BidVolumeText);
        Assert.Equal("7", row.AskVolumeText);
        Assert.Equal("1,234", row.VolumeText);
        Assert.Equal("88", row.OpenInterestText);
        Assert.Equal("09:30:05.007", row.TimeText);
    }

    [Fact]
    public void 断开重置后回到空白态()
    {
        var row = new QuoteRow { Instrument = "ax001" };
        row.Apply(Tick(99, bid: 98.5));
        row.Reset();

        Assert.False(row.HasQuote);
        Assert.Equal(0, row.Reference);
        Assert.Equal("—", row.LastText);
        Assert.Equal("—", row.BidText);
        Assert.Equal(0, row.Moves);
        Assert.Equal(MarketMath.BasisFirst, row.Basis);
    }
}
