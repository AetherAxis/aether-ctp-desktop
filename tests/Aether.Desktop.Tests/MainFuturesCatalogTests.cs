using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aether.Desktop.MarketWatch;

namespace Aether.Desktop.Tests;

/// <summary>
/// 主力合约目录（客户端直爬东方财富）。这条链路决定「连真实前置时到底订阅什么」：
/// 代码规整错了 CTP 会静默拒收（订阅超时），过滤漏了月均合约会污染看板——每个分支都钉住。
/// 数据形状取自 2026-10-25 实抓（90 条一品种一条；dm/name/sc/uid 四字段即够解析）。
/// </summary>
public class MainFuturesCatalogTests
{
    private const string SampleJson = """
        {"total":5,"list":[
          {"dm":"PX612","name":"对二甲苯612","sc":115,"uid":"CZCE|PX612"},
          {"dm":"i2701","name":"铁矿石2701","sc":114,"uid":"DCE|i2701"},
          {"dm":"if2612","name":"沪深2612","sc":220,"uid":"CFFEX|if2612"},
          {"dm":"pp2703F","name":"聚丙烯月均2703","sc":114,"uid":"DCE|pp2703F"},
          {"dm":"cu2612","name":"沪铜2612","sc":113,"uid":"SHFE|cu2612"}
        ]}
        """;

    [Fact]
    public void 解析_规整代码_并过滤月均合约()
    {
        var entries = MainFuturesCatalog.Parse(SampleJson);

        Assert.Equal(4, entries.Count);
        // CZCE 3 位月份保持大写；DCE/SHFE 小写 4 位保持小写；CFFEX 小写输入要抬成大写（CTP 区分大小写）
        Assert.Equal(("PX612", "对二甲苯612", "CZCE"), (entries[0].Code, entries[0].Name, entries[0].Exchange));
        Assert.Equal(("i2701", "铁矿石2701", "DCE"), (entries[1].Code, entries[1].Name, entries[1].Exchange));
        Assert.Equal(("IF2612", "沪深2612", "CFFEX"), (entries[2].Code, entries[2].Name, entries[2].Exchange));
        Assert.Equal(("cu2612", "沪铜2612", "SHFE"), (entries[3].Code, entries[3].Name, entries[3].Exchange));
        // pp2703F 是 DCE 月均合约——非常规主力，不进订阅面（IF2612 这类中金所代码以数字结尾，不受影响）
        Assert.DoesNotContain(entries, e => e.Code.EndsWith('F'));
    }

    [Theory]
    [InlineData("PX612", "PX612")]      // CZCE 3 位月份 → 大写
    [InlineData("ta612", "TA612")]      // 小写输入的 CZCE 也要抬大写（证券主数据口径）
    [InlineData("IF2612", "IF2612")]
    [InlineData("if2612", "IF2612")]    // 中金所前缀 → 大写
    [InlineData("tl2612", "TL2612")]    // TL（三十债）——monorepo 原表漏的前缀
    [InlineData("cu2612", "cu2612")]
    [InlineData("CU2612", "cu2612")]    // 上期/大商/能源/广期 → 小写
    [InlineData("rb2610", "rb2610")]
    [InlineData("ax001", "AX001")]      // 规则纯词法：3 位数字=郑商所月份形态，仿真代码 ax001 也命中（仿真品种不经此路径，无实际影响）
    [InlineData("pp2703F", "")]         // 带尾缀字母的不是常规合约形态
    [InlineData("", "")]
    [InlineData("主力连续", "")]
    public void 代码规整(string input, string expected) =>
        Assert.Equal(expected, MainFuturesCatalog.NormalizeCode(input));

    [Fact]
    public void 无_list_属性_返回空目录() =>
        Assert.Empty(MainFuturesCatalog.Parse("""{"total":0}"""));

    [Fact]
    public void 空_list_返回空目录() =>
        Assert.Empty(MainFuturesCatalog.Parse("""{"total":0,"list":[]}"""));

    [Fact]
    public void 坏_JSON_直接抛_由调用方呈报()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => MainFuturesCatalog.Parse("not-json"));
    }

    [Fact]
    public void 未知_sc_用_uid_前半段兜底_无_uid_留空()
    {
        var json = """
            {"list":[
              {"dm":"ab2601","name":"新品种","sc":999,"uid":"XXE|ab2601"},
              {"dm":"cd2601","name":"无交易所信息","sc":0}
            ]}
            """;

        var entries = MainFuturesCatalog.Parse(json);

        Assert.Equal(2, entries.Count);
        Assert.Equal("XXE", entries[0].Exchange);
        Assert.Equal("", entries[1].Exchange);
    }

    [Fact]
    public async Task FetchAsync_走_URL_并解析()
    {
        var catalog = new MainFuturesCatalog(new HttpClient(new StubHandler(SampleJson)));

        var entries = await catalog.FetchAsync();

        Assert.Equal(4, entries.Count);
        Assert.Contains("PX612", entries.Select(e => e.Code));
    }

    [Fact]
    public async Task FetchAsync_非_2xx_抛_HttpRequestException()
    {
        var catalog = new MainFuturesCatalog(new HttpClient(new StubHandler("boom", HttpStatusCode.InternalServerError)));

        await Assert.ThrowsAsync<HttpRequestException>(() => catalog.FetchAsync());
    }

    [Fact]
    public void 真实前置判据_按地址精确匹配()
    {
        Assert.True(MarketWatchViewModel.IsRealFrontAddress(MainFuturesCatalog.SimNowMdFront));
        Assert.True(MarketWatchViewModel.IsRealFrontAddress(" tcp://182.254.243.31:40011 "));
        Assert.False(MarketWatchViewModel.IsRealFrontAddress(MarketWatchViewModel.LocalMdFront));
        Assert.False(MarketWatchViewModel.IsRealFrontAddress(MarketWatchViewModel.RemoteMdFront));
        Assert.False(MarketWatchViewModel.IsRealFrontAddress(null));
        Assert.False(MarketWatchViewModel.IsRealFrontAddress(""));
    }

    [Theory]
    [InlineData("ax001", true)]
    [InlineData("AX001", true)]          // 大小写不敏感——手输大写也是同一个仿真默认品种
    [InlineData("ax001,ax001", true)]
    [InlineData("ax001,rb2612", false)]  // 混入真实品种 = 用户明确的订阅意图，不替换
    [InlineData("rb2612", false)]
    public void 仿真默认看板判据(string raw, bool expected) =>
        Assert.Equal(expected, MarketWatchViewModel.IsSimOnlyBoard(
            raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()));

    private sealed class StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(MainFuturesCatalog.FuturesListUrl, request.RequestUri?.ToString());
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
