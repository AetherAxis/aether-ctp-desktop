using System;
using System.Globalization;

namespace Aether.Desktop.MarketWatch;

/// <summary>
/// 行情看板的纯计算面（涨跌基准、涨跌额/幅、显示格式）。抽出来是为了能被单元测试钉住——
/// 界面上的数字错一位没人看得出来，但「涨跌算的是上一笔还是昨结」是口径问题，必须有明确答案。
/// <para>
/// <b>基准选择</b>：柜台昨结价 &gt; 昨收价 &gt; 本会话收到的第一笔价。
/// 前两者来自 tick 的 <c>PreSettlement/PreClose</c>；我们自己的前置当前**不下发**这两个字段（恒 0），
/// 所以仿真链路会落到「本会话首笔」——界面上把基准逐行标出来（<see cref="QuoteRow.Basis"/>），
/// 不拿一个没有来源的百分比冒充「今日涨跌」。
/// </para>
/// </summary>
public static class MarketMath
{
    public const string BasisSettlement = "昨结";
    public const string BasisClose = "昨收";
    public const string BasisFirst = "首笔";

    /// <summary>选涨跌基准价与它的口径标签。三个来源都不可用时返回 (0, 首笔)——此时涨跌幅显示为 0 而不是「-100%」。</summary>
    public static (double Reference, string Basis) PickBasis(double preSettlement, double preClose, double sessionFirst)
    {
        if (preSettlement > 0) return (preSettlement, BasisSettlement);
        if (preClose > 0) return (preClose, BasisClose);
        return (sessionFirst > 0 ? sessionFirst : 0, BasisFirst);
    }

    /// <summary>涨跌额（价差）。基准缺失时返回 0——不能拿 0 当基准算出「现价」这么大的涨幅。</summary>
    public static double Change(double last, double reference)
        => reference > 0 && last > 0 ? last - reference : 0;

    /// <summary>涨跌幅（%）。基准缺失时为 0。</summary>
    public static double ChangePercent(double last, double reference)
        => reference > 0 && last > 0 ? (last - reference) / reference * 100 : 0;

    /// <summary>方向：1 涨 / -1 跌 / 0 平（容忍浮点噪声）。</summary>
    public static int Direction(double change, double epsilon = 1e-9)
        => change > epsilon ? 1 : change < -epsilon ? -1 : 0;

    /// <summary>价格显示：至少两位小数，最多四位（真实柜台有 0.005 这类最小变动价位，截成两位会看不出变化）。</summary>
    public static string PriceText(double price) => price.ToString("0.00##", CultureInfo.InvariantCulture);

    /// <summary>涨跌额显示，恒带符号（+0.12 / -0.12 / 0.00）。</summary>
    public static string ChangeText(double change)
        => change.ToString("+0.00##;-0.00##;0.00", CultureInfo.InvariantCulture);

    /// <summary>涨跌幅显示，恒带符号与百分号。</summary>
    public static string PercentText(double percent)
        => percent.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "%";

    /// <summary>行情时间显示：<c>HH:mm:ss.mmm</c>（毫秒三位补零；柜台毫秒是 int，不补零会显示成 09:30:5.7）。</summary>
    public static string TimeText(string updateTime, int millisec)
        => string.IsNullOrEmpty(updateTime) ? "" : $"{updateTime}.{millisec:000}";

    /// <summary>tick 速率：给定窗口内的样本数与窗口秒数 → tick/s（窗口为 0 时返回 0）。</summary>
    public static double TickRate(int samplesInWindow, double windowSeconds)
        => windowSeconds > 0 ? samplesInWindow / windowSeconds : 0;
}
