using Godot;

namespace Dojo.Common;

/// <summary>
/// 伤害飘字的对象池。
///
/// 为什么飘字一定要池化：
///   一次连击可能瞬间产生十几条飘字。如果每次都 `Instantiate` + `QueueFree`，
///   你会得到：频繁的内存分配（GC 压力）、节点树的反复增删（引擎开销）、
///   以及偶发的卡顿 —— 而且**恰恰在你打得最爽的时候卡**。
///
/// 池化省下的到底是多少？这里刻意把数字统计出来给你看：
///   `TotalCreated` = 真正 new 出来的节点数
///   `TotalPlayed`  = 累计播放次数
///   两者之差就是**被省掉的 Instantiate 次数**。S07 的面板会实时显示它。
///
/// **不要凭感觉说"池化更快"，把省下的次数和帧时间量出来。**
/// （更系统的测量在 S14 对象池站。）
/// </summary>
public sealed class DamageNumberPool
{
    private readonly PackedScene _scene;
    private readonly Node _parent;
    private readonly List<DamageNumber> _instances = new();
    private int _cursor;

    /// <summary>真正创建过的节点数。</summary>
    public int TotalCreated => _instances.Count;

    /// <summary>累计播放次数。</summary>
    public int TotalPlayed { get; private set; }

    /// <summary>因为复用而省下的 Instantiate 次数。</summary>
    public int SavedInstantiations => Mathf.Max(0, TotalPlayed - TotalCreated);

    /// <summary>池里同时存在过的最大节点数 —— 也就是"不池化的话要创建多少个"。</summary>
    public int PeakConcurrent { get; private set; }

    public DamageNumberPool(Node parent, PackedScene scene)
    {
        _parent = parent;
        _scene = scene;
    }

    public void Spawn(string text, Color color, bool big, Vector2 position)
    {
        var instance = Rent();
        instance.Play(text, color, big, position);

        TotalPlayed++;

        var alive = 0;
        foreach (var item in _instances)
            if (item.InUse) alive++;
        PeakConcurrent = Mathf.Max(PeakConcurrent, alive);
    }

    public void ResetStats()
    {
        TotalPlayed = 0;
        PeakConcurrent = 0;
    }

    private DamageNumber Rent()
    {
        // 从上次的位置往后找第一个空闲的（轮转，避免总从同一个开始扫描）
        for (var i = 0; i < _instances.Count; i++)
        {
            var index = (_cursor + i) % _instances.Count;
            if (_instances[index].InUse) continue;

            _cursor = (index + 1) % _instances.Count;
            return _instances[index];
        }

        // 全都占着 —— 才真的新建一个。池"按需增长"，不需要预热。
        var fresh = _scene.Instantiate<DamageNumber>();
        _parent.AddChild(fresh);
        _instances.Add(fresh);
        _cursor = 0;

        return fresh;
    }
}
