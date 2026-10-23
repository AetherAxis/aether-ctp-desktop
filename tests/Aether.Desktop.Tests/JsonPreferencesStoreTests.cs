using Aether.Desktop;

namespace Aether.Desktop.Tests;

/// <summary>
/// 偏好存储是布局/主题/表单档案的唯一落盘点。<b>同步完成</b>是硬要求（引擎启动期在 UI 线程
/// <c>GetAwaiter().GetResult()</c>，真异步实现会死锁），且坏文件必须当空重建而不是让应用起不来。
/// </summary>
public class JsonPreferencesStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"prefs-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { File.Delete(_path); } catch { }
    }

    [Fact]
    public async Task 取未设键返null_设后可取回()
    {
        var store = new JsonPreferencesStore(_path);

        Assert.Null(await store.GetAsync("missing"));
        await store.SetAsync("k", "v");
        Assert.Equal("v", await store.GetAsync("k"));
    }

    [Fact]
    public async Task 设null即删除键()
    {
        var store = new JsonPreferencesStore(_path);
        await store.SetAsync("k", "v");
        await store.SetAsync("k", null);

        Assert.Null(await store.GetAsync("k"));
        var reopened = new JsonPreferencesStore(_path);      // 落盘里也不该再有这个键
        Assert.Null(await reopened.GetAsync("k"));
    }

    [Fact]
    public async Task 跨实例持久化()
    {
        await new JsonPreferencesStore(_path).SetAsync("connector.front", "tcp://1.1.1.1:1");

        var second = new JsonPreferencesStore(_path);

        Assert.Equal("tcp://1.1.1.1:1", await second.GetAsync("connector.front"));
    }

    [Fact]
    public async Task 坏文件当空重建不抛()
    {
        await File.WriteAllTextAsync(_path, "{ 不是 JSON");

        var store = new JsonPreferencesStore(_path);

        Assert.Null(await store.GetAsync("k"));
        await store.SetAsync("k", "v");   // 覆盖成合法文件
        Assert.Equal("v", await new JsonPreferencesStore(_path).GetAsync("k"));
    }

    [Fact]
    public async Task 密码键可以彻底删掉_落盘不再含明文()
    {
        await new JsonPreferencesStore(_path).SetAsync("connector.pass", "明文口令");

        var store = new JsonPreferencesStore(_path);
        Assert.Equal("明文口令", await store.GetAsync("connector.pass"));
        // ConnectorViewModel 构造时做的就是这一步：读到旧明文 → 置 null（见其 ctor 注释）
        await store.SetAsync("connector.pass", null);

        Assert.DoesNotContain("明文口令", await File.ReadAllTextAsync(_path));
    }
}
