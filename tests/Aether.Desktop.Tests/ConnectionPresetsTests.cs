using Aether.Desktop.Connector;

namespace Aether.Desktop.Tests;

/// <summary>
/// 预设档案契约：<b>用户档案优先、坏档回退内置、清单永不为空</b>。
/// 预设是「点了就能连」的入口，档案坏掉绝不能把应用卡在启动路径上（构造 ConnectorViewModel 时同步读）。
/// </summary>
public class ConnectionPresetsTests
{
    [Fact]
    public void 无档案用内置()
    {
        Assert.Same(ConnectionPresets.BuiltIn, ConnectionPresets.Load(null));
        Assert.Same(ConnectionPresets.BuiltIn, ConnectionPresets.Load("   "));
    }

    [Fact]
    public void 坏JSON回退内置不抛()
    {
        Assert.Same(ConnectionPresets.BuiltIn, ConnectionPresets.Load("{ 坏"));
        Assert.Same(ConnectionPresets.BuiltIn, ConnectionPresets.Load("[]"));        // 空清单不能当结果（Presets[0] 会被取）
        Assert.Same(ConnectionPresets.BuiltIn, ConnectionPresets.Load("""[{"name":""},{"front":""}]"""));  // 全无效
    }

    [Fact]
    public void 有效档案覆盖内置_并trim与补全()
    {
        const string json = """
            [
              { "name": " 测试柜台 ", "front": " tcp://10.0.0.1:1 ", "mdFront": "", "instruments": [" ag2610 ", ""] },
              { "name": "第二", "front": "tcp://10.0.0.2:1", "mdFront": "tcp://10.0.0.2:2" }
            ]
            """;

        var presets = ConnectionPresets.Load(json);

        Assert.Equal(2, presets.Count);
        Assert.Equal("测试柜台", presets[0].Name);
        Assert.Equal("tcp://10.0.0.1:1", presets[0].Front);
        Assert.Equal("tcp://10.0.0.1:1", presets[0].MdFront);               // 空 mdFront 复用交易前置
        Assert.Equal(new[] { "ag2610" }, presets[0].Instruments);            // 空串被滤掉
        Assert.Empty(presets[1].Instruments);                                // 缺省为空而不是 null
    }

    [Fact]
    public void 单条坏数据只丢那一条()
    {
        const string json = """
            [ { "name": "好", "front": "tcp://1.1.1.1:1" }, { "name": "缺前置" } ]
            """;

        var presets = ConnectionPresets.Load(json);

        var only = Assert.Single(presets);
        Assert.Equal("好", only.Name);
    }

    [Fact]
    public void 序列化再读回等价()
    {
        var original = new[]
        {
            new ConnectionPreset("甲", "tcp://1.1.1.1:1", "tcp://1.1.1.1:2", new[] { "ag2610", "ax001" }),
            new ConnectionPreset("乙", "tcp://2.2.2.2:1", "tcp://2.2.2.2:2", Array.Empty<string>()),
        };

        var presets = ConnectionPresets.Load(ConnectionPresets.Serialize(original));

        Assert.Equal(2, presets.Count);
        Assert.Equal("甲", presets[0].Name);
        Assert.Equal(new[] { "ag2610", "ax001" }, presets[0].Instruments);
        Assert.Equal("tcp://2.2.2.2:2", presets[1].MdFront);
        Assert.Empty(presets[1].Instruments);
    }

    [Fact]
    public void 内置清单非空且第0条可作表单默认值()
    {
        Assert.NotEmpty(ConnectionPresets.BuiltIn);
        Assert.False(string.IsNullOrWhiteSpace(ConnectionPresets.BuiltIn[0].Front));
        Assert.False(string.IsNullOrWhiteSpace(ConnectionPresets.BuiltIn[0].MdFront));
    }
}
