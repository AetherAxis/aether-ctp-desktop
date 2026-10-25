using Aether.Desktop.MarketWatch;

namespace Aether.Desktop.Tests;

/// <summary>
/// 行情看板的涨跌口径。这些数字直接决定用户看到的「涨/跌」——口径错了不会有异常，
/// 只会让人对着一个假的百分比做判断，所以每一条分支都钉住。
/// </summary>
public class MarketMathTests
{
    [Fact]
    public void 有昨结就用昨结()
    {
        var (reference, basis) = MarketMath.PickBasis(preSettlement: 100, preClose: 101, sessionFirst: 99);
        Assert.Equal(100, reference);
        Assert.Equal(MarketMath.BasisSettlement, basis);
    }

    [Fact]
    public void 没昨结退到昨收()
    {
        var (reference, basis) = MarketMath.PickBasis(preSettlement: 0, preClose: 101, sessionFirst: 99);
        Assert.Equal(101, reference);
        Assert.Equal(MarketMath.BasisClose, basis);
    }

    [Fact]
    public void 都没有退到本会话首笔()
    {
        var (reference, basis) = MarketMath.PickBasis(preSettlement: 0, preClose: 0, sessionFirst: 99.5);
        Assert.Equal(99.5, reference);
        Assert.Equal(MarketMath.BasisFirst, basis);
    }

    [Fact]
    public void 基准全缺时涨跌为0而不是负100()
    {
        var (reference, basis) = MarketMath.PickBasis(0, 0, 0);
        Assert.Equal(0, reference);
        Assert.Equal(MarketMath.BasisFirst, basis);
        Assert.Equal(0, MarketMath.Change(99, reference));
        Assert.Equal(0, MarketMath.ChangePercent(99, reference));
    }

    [Theory]
    [InlineData(100, 99, -1)]
    [InlineData(100, 101, 1)]
    [InlineData(100, 100, 0)]
    public void 涨跌额(double reference, double last, double expected)
        => Assert.Equal(expected, MarketMath.Change(last, reference));

    [Fact]
    public void 涨跌幅按基准算百分比()
    {
        Assert.Equal(1, MarketMath.ChangePercent(101, 100), 6);
        Assert.Equal(-2, MarketMath.ChangePercent(98, 100), 6);
    }

    [Fact]
    public void 方向含浮点容差()
    {
        Assert.Equal(0, MarketMath.Direction(1e-12));
        Assert.Equal(0, MarketMath.Direction(-1e-12));
        Assert.Equal(1, MarketMath.Direction(0.01));
        Assert.Equal(-1, MarketMath.Direction(-0.01));
    }

    [Theory]
    [InlineData(99.09375, "99.0938")]   // 真实柜台最小变动价位可能小于 0.01——截两位会看不出变化
    [InlineData(100, "100.00")]
    [InlineData(0.005, "0.005")]
    public void 价格至少两位最多四位(double price, string expected)
        => Assert.Equal(expected, MarketMath.PriceText(price));

    [Theory]
    [InlineData(0.5, "+0.50")]
    [InlineData(-0.5, "-0.50")]
    [InlineData(0, "0.00")]
    public void 涨跌额恒带符号(double change, string expected)
        => Assert.Equal(expected, MarketMath.ChangeText(change));

    [Theory]
    [InlineData(1.234, "+1.23%")]
    [InlineData(-1.234, "-1.23%")]
    [InlineData(0, "0.00%")]
    public void 涨跌幅恒带符号与百分号(double percent, string expected)
        => Assert.Equal(expected, MarketMath.PercentText(percent));

    [Fact]
    public void 毫秒补三位()
    {
        Assert.Equal("09:30:05.007", MarketMath.TimeText("09:30:05", 7));
        Assert.Equal("", MarketMath.TimeText("", 0));
    }

    [Fact]
    public void tick速率()
    {
        Assert.Equal(2, MarketMath.TickRate(10, 5));
        Assert.Equal(0, MarketMath.TickRate(10, 0));
    }

    [Fact]
    public void 品种清单容忍中英文逗号与空格且去重()
    {
        var symbols = MarketWatchViewModel.SplitSymbols(" ax001，IF2612, ax001 ; m2701 ");
        Assert.Equal(new[] { "ax001", "IF2612", "m2701" }, symbols);
    }
}
