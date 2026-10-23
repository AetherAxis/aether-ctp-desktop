using Aether.Desktop.Futures;
using Ctp.Net;

namespace Aether.Desktop.Tests;

/// <summary>
/// 界面文案口径（买/卖、开/平、委托状态字符 → 中文）。这些字符串同时出现在界面与日志里，
/// 漂移会让两边各说各话；CTP 的状态是**字符码**（不是枚举），映射错了就是把「已撤」显示成「未知」。
/// </summary>
public class DisplayFormatTests
{
    [Theory]
    [InlineData(CtpDirection.Buy, "买")]
    [InlineData(CtpDirection.Sell, "卖")]
    public void 方向(CtpDirection direction, string expected)
        => Assert.Equal(expected, FuturesViewModel.DirText(direction));

    [Theory]
    [InlineData(CtpOffsetFlag.Open, "开")]
    [InlineData(CtpOffsetFlag.Close, "平")]
    [InlineData(CtpOffsetFlag.CloseToday, "平今")]
    [InlineData(CtpOffsetFlag.CloseYesterday, "平昨")]
    public void 开平(CtpOffsetFlag offset, string expected)
        => Assert.Equal(expected, FuturesViewModel.OffsetText(offset));

    [Fact]
    public void 买卖与开平拼接()
    {
        Assert.Equal("买开", FuturesViewModel.DirOffsetText(CtpDirection.Buy, CtpOffsetFlag.Open));
        Assert.Equal("卖平今", FuturesViewModel.DirOffsetText(CtpDirection.Sell, CtpOffsetFlag.CloseToday));
    }

    [Theory]
    [InlineData('0', "全成")]
    [InlineData('1', "部成在队")]
    [InlineData('2', "部成作废")]
    [InlineData('3', "未成交在队")]
    [InlineData('4', "已作废")]
    [InlineData('5', "已撤销")]
    [InlineData('a', "未知")]                 // 'a' = 柜台未给状态——不能说成「在途」
    [InlineData('b', "条件单未触发")]
    [InlineData('c', "条件单已触发")]
    [InlineData('z', "状态z")]                // 契约不认识的码：保留原码好排查
    public void 委托状态字符映射(char raw, string expected)
        => Assert.Equal(expected, FuturesViewModel.OrderStatusText(raw));

    [Fact]
    public void 状态文案以契约为准_字符码与枚举同一结果()
    {
        foreach (var status in Enum.GetValues<CtpOrderStatus>())
            Assert.Equal(
                FuturesViewModel.OrderStatusText(status),
                FuturesViewModel.OrderStatusText((char)(byte)status));
    }
}
