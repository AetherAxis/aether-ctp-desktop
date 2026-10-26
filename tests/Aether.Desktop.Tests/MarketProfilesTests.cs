using System;
using System.Linq;
using Aether.Desktop.MarketWatch;
using Ctp.Net;

namespace Aether.Desktop.Tests;

/// <summary>
/// 连接档案的纯逻辑面（对齐 monorepo 连接器的期货连接语义）。这些函数决定：
/// 首次启动播种哪些档案、旧偏好怎么迁移、连接后档案叫什么名、哪类前置自动爬主力——
/// 每个分支都钉住。
/// </summary>
public class MarketProfilesTests
{
    [Fact]
    public void 内置清单_三个且字段齐全()
    {
        var builtIns = MarketProfiles.BuiltIns();

        Assert.Equal(new[] { "本地", "远程", "SimNow" }, builtIns.Select(p => p.Name));
        Assert.All(builtIns, p =>
        {
            Assert.NotEqual(Guid.Empty, p.Id);
            Assert.NotEmpty(p.MdFrontAddress);
            Assert.NotEmpty(p.FrontAddress);
            Assert.Equal("9999", p.BrokerId);
            Assert.NotEmpty(p.AppId);
            Assert.NotEmpty(p.AuthCode);
            Assert.True(p.AutoLogin);
        });
        Assert.Equal(MarketWatchViewModel.LocalMdFront, builtIns[0].MdFrontAddress);
        Assert.Equal(MarketWatchViewModel.RemoteMdFront, builtIns[1].MdFrontAddress);
        Assert.Equal(MarketWatchViewModel.RealMdFront, builtIns[2].MdFrontAddress);
    }

    [Fact]
    public void 迁移_旧偏好命中内置_标该档()
    {
        var seeded = MarketProfiles.BuiltIns();

        var lastUsed = MarketProfiles.PickLastUsed(seeded, "tcp://117.72.186.128:17802", "9999", "watching");

        Assert.NotNull(lastUsed);
        Assert.Equal("远程", lastUsed.Name);
    }

    [Fact]
    public void 迁移_旧偏好地址不在内置_补一条上次使用档()
    {
        var seeded = MarketProfiles.BuiltIns();

        var lastUsed = MarketProfiles.PickLastUsed(seeded, "tcp://1.2.3.4:41205", "8888", "247163");

        Assert.NotNull(lastUsed);
        Assert.Equal("上次使用", lastUsed.Name);
        Assert.Equal("tcp://1.2.3.4:41205", lastUsed.MdFrontAddress);
        Assert.Equal("8888", lastUsed.BrokerId);
        Assert.Equal("247163", lastUsed.UserId);
    }

    [Fact]
    public void 迁移_无旧偏好_返回null用默认()
    {
        Assert.Null(MarketProfiles.PickLastUsed(MarketProfiles.BuiltIns(), null, null, null));
        Assert.Null(MarketProfiles.PickLastUsed(MarketProfiles.BuiltIns(), "  ", "9999", "watching"));
    }

    [Fact]
    public void 档案名_唯一用户号用用户号_重复加前置后缀()
    {
        var profiles = new[]
        {
            new CtpConnectionConfig { Id = Guid.NewGuid(), UserId = "botmm", MdFrontAddress = "tcp://127.0.0.1:17802" },
        };

        // 同用户号、不同前置 → 加 host 后缀
        var name = MarketProfiles.BuildProfileName(profiles, Guid.NewGuid(), "botmm", "tcp://117.72.186.128:17802");
        Assert.Equal("botmm·117.72.186.128", name);

        // 其它用户号不受影响
        Assert.Equal("botnoise", MarketProfiles.BuildProfileName(profiles, Guid.NewGuid(), "botnoise", "tcp://127.0.0.1:17802"));
        // 空用户号 → watching
        Assert.Equal("watching", MarketProfiles.BuildProfileName(profiles, Guid.NewGuid(), "", "tcp://x:1"));
    }

    [Fact]
    public void 前置标签_剥协议与端口_解析不出用原文()
    {
        Assert.Equal("1.2.3.4", MarketProfiles.FrontTag("tcp://1.2.3.4:41205"));
        Assert.Equal("1.2.3.4", MarketProfiles.FrontTag("1.2.3.4:41205"));
        Assert.Equal("plainhost", MarketProfiles.FrontTag("plainhost"));
        Assert.Equal("front", MarketProfiles.FrontTag(""));
        Assert.Equal("front", MarketProfiles.FrontTag(null));
    }
}
