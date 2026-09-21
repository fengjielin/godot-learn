using System.Text;
using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// S14 · 对象池与性能
///
/// 按 `1` 用**朴素方式**生成 1200 颗子弹（每颗都 Instantiate，寿命到了自己 QueueFree）；
/// 按 `2` 用**对象池**生成同样多；按 `3` 清空；按 `4` 把池预热到 1200。
///
/// 每次生成都会**自动测量**两件事：
///   · **生成耗时** —— 那一帧里 Instantiate 循环花了多少毫秒
///   · **生成后 1.5 秒的平均帧时间** —— 反映"一堆对象同时存在"时的持续开销
///
/// ★ 本站唯一想让你记住的事：**先测量，再优化。**
///   面板上那两行数字不是装饰 —— 它们是"优化"这个动作的唯一依据。
///   没有数字就改代码，99% 是在浪费时间：你可能在优化一个根本不是瓶颈的地方，
///   也可能把一个 0.2ms 的问题优化成 0.1ms 然后觉得自己很厉害。
///
/// ★ 第二个想让你记住的事：**池化省下的到底是什么。**
///   不是"对象本身"，而是**创建和销毁这两件事**。
///   所以如果子弹寿命很长、屏幕上同时只有十几颗，池化省下的几乎可以忽略；
///   只有当"每秒创建销毁几百次"时它才明显。**面板上那两行数字会告诉你什么时候值得。**
/// </summary>
public partial class S14Pooling : StationBase
{
    public override string StationId => "s14_pooling";

    private static readonly Rect2 PlayArea = new(40, 130, 1200, 560);

    private const int BurstCount = 1200;
    private const float SampleSeconds = 1.5f;
    private const float BulletLife = 2.4f;
    private const float BulletSpeed = 420f;

    /// <summary>一次测试的结果。</summary>
    private sealed record RunResult(string Mode, int Count, double SpawnMs, double AvgFrameMs, int NodeDelta);

    private Node2D _bulletsRoot = null!;
    private PackedScene _bulletScene = null!;
    private Label _panel = null!;

    private readonly List<PoolBullet> _pool = new();
    private readonly List<PoolBullet> _naive = new();
    private RunResult? _naiveResult;
    private RunResult? _pooledResult;

    private double _samplingLeft;
    private double _sampleTotal;
    private int _sampleFrames;
    private string _samplingMode = "";
    private double _samplingSpawnMs;
    private int _samplingCount;
    private int _samplingNodeDelta;

    private int _spawnedNaive;
    private int _spawnedPooled;
    private int _reuses;          // 池复用次数 = 省下的 Instantiate 次数
    private int _clears;
    private readonly bool[] _taskDone = new bool[5];

    protected override void StationReady()
    {
        _bulletsRoot = GetNode<Node2D>("Bullets");
        _bulletScene = GD.Load<PackedScene>("res://src/stations/s14_pooling/bullet.tscn")
                       ?? throw new InvalidOperationException("bullet.tscn 加载失败");

        LevelKit.CreateBorderWalls(this, PlayArea);
        BuildLabels();

        SetStatus("按 1 朴素生成 1200 颗　2 池化生成 1200 颗　3 清空　4 预热对象池　（F3 看引擎自带的性能面板）");
    }

    public override void _Process(double delta)
    {
        if (_samplingLeft > 0)
        {
            _samplingLeft -= delta;
            _sampleTotal += delta;
            _sampleFrames++;

            if (_samplingLeft <= 0) FinishSample();
        }

        UpdatePanel();
        CheckGoals();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("aux_1")) { SpawnNaive(); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_2")) { SpawnPooled(); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_3")) { ClearAll(); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_4")) { Prewarm(); GetViewport().SetInputAsHandled(); return; }

        base._UnhandledInput(@event);
    }

    // ---------- 两种生成方式 ----------

    /// <summary>朴素：每颗子弹都是新造出来的，寿命到了自己销毁。</summary>
    private void SpawnNaive()
    {
        ClearAll();
        var before = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);

        var t0 = Time.GetTicksUsec();
        for (var i = 0; i < BurstCount; i++)
        {
            var bullet = _bulletScene.Instantiate<PoolBullet>();
            bullet.AutoFree = true;
            _bulletsRoot.AddChild(bullet);
            LaunchOne(bullet, i);
            _naive.Add(bullet);
        }
        var spawnMs = (Time.GetTicksUsec() - t0) / 1000.0;

        _spawnedNaive += BurstCount;
        BeginSample("朴素 Instantiate", spawnMs, BurstCount,
            (int)(Performance.GetMonitor(Performance.Monitor.ObjectNodeCount) - before));
    }

    /// <summary>池化：对象预先造好，只重置状态再启用。</summary>
    private void SpawnPooled()
    {
        ClearAll();
        var before = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);

        var t0 = Time.GetTicksUsec();
        for (var i = 0; i < BurstCount; i++)
        {
            var bullet = RentFromPool();
            LaunchOne(bullet, i);
        }
        var spawnMs = (Time.GetTicksUsec() - t0) / 1000.0;

        _spawnedPooled += BurstCount;
        BeginSample("对象池复用", spawnMs, BurstCount,
            (int)(Performance.GetMonitor(Performance.Monitor.ObjectNodeCount) - before));
    }

    /// <summary>池按需增长：没有空闲的才真的新建一个。预热过就不用新建。</summary>
    private PoolBullet RentFromPool()
    {
        foreach (var bullet in _pool)
        {
            if (bullet.InUse) continue;
            _reuses++;
            return bullet;
        }

        var fresh = _bulletScene.Instantiate<PoolBullet>();
        fresh.AutoFree = false;
        _bulletsRoot.AddChild(fresh);
        _pool.Add(fresh);
        return fresh;
    }

    private void Prewarm()
    {
        var t0 = Time.GetTicksUsec();
        var made = 0;
        while (_pool.Count < BurstCount)
        {
            var bullet = _bulletScene.Instantiate<PoolBullet>();
            bullet.AutoFree = false;
            _bulletsRoot.AddChild(bullet);
            bullet.Release();
            _pool.Add(bullet);
            made++;
        }
        var ms = (Time.GetTicksUsec() - t0) / 1000.0;
        Flash($"对象池已预热到 {_pool.Count} 个实例（新建 {made} 个，耗时 {ms:0.0}ms）—— 现在按 2 生成不会再 new 任何东西", 3.0);
    }

    private void LaunchOne(PoolBullet bullet, int index)
    {
        var angle = index * 2.399963f; // 黄金角，让子弹均匀铺开而不是挤成一坨
        var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        bullet.Launch(new Vector2(640, 400), direction, BulletSpeed, BulletLife,
            Color.FromHsv((index % 360) / 360f, 0.6f, 1f));
    }

    private void ClearAll()
    {
        foreach (var bullet in _naive)
        {
            if (IsInstanceValid(bullet)) bullet.QueueFree();
        }
        _naive.Clear();

        foreach (var bullet in _pool) bullet.Release();

        _clears++;
    }

    // ---------- 测量 ----------

    private void BeginSample(string mode, double spawnMs, int count, int nodeDelta)
    {
        _samplingMode = mode;
        _samplingSpawnMs = spawnMs;
        _samplingCount = count;
        _samplingNodeDelta = nodeDelta;
        _samplingLeft = SampleSeconds;
        _sampleTotal = 0;
        _sampleFrames = 0;
    }

    private void FinishSample()
    {
        var avgMs = _sampleFrames == 0 ? 0 : _sampleTotal / _sampleFrames * 1000.0;
        var result = new RunResult(_samplingMode, _samplingCount, _samplingSpawnMs, avgMs, _samplingNodeDelta);

        if (_samplingMode.StartsWith("朴素")) _naiveResult = result;
        else _pooledResult = result;

        Flash($"{_samplingMode}：{_samplingCount} 颗 —— 生成 {_samplingSpawnMs:0.0}ms，之后平均帧时间 {avgMs:0.00}ms", 3.5);
    }

    // ---------- 判定 ----------

    private void CheckGoals()
    {
        // 下标对应 StationCatalog 里 S14 的任务顺序：
        //   0 实现对象池并循环复用 · 1 批量绘制再对比 · 2 F3 记录节点数与 FPS
        //   3 用分析器找最耗时函数 · 4 思考：池化什么时候反而更慢
        // 其中 1 和 3 需要你打开引擎自带的工具自己动手，站内判定对应的是
        // "你完成了一次**可比较的测量**" —— 没有测量就谈不上优化。
        if (_pooledResult is not null) MarkOnce(0);              // 对象池跑通
        if (_reuses > 0) MarkOnce(1);                            // 复用真的发生了
        if (_naiveResult is not null && _pooledResult is not null) MarkOnce(2);  // 两种都量过
        if (_clears > 0) MarkOnce(3);
        if (_spawnedPooled >= BurstCount * 3) MarkOnce(4);       // 反复跑过，有足够数据下结论

        if (!Array.TrueForAll(_taskDone, done => done)) return;

        Complete("两种方式都量过了 —— 现在你手上有数字，可以回答「池化到底省了什么」了");
    }

    private void MarkOnce(int index)
    {
        if (_taskDone[index]) return;
        _taskDone[index] = true;
        MarkTaskDone(index);
    }

    // ---------- 界面 ----------

    private void BuildLabels()
    {
        _panel = LevelKit.MakeLabel(this, new Vector2(24, 58), "", 14,
            new Color(0.87f, 0.92f, 0.98f), HorizontalAlignment.Left, "PoolPanel");
        _panel.Size = new Vector2(770, 420);
    }

    private void UpdatePanel()
    {
        var sb = new StringBuilder();

        sb.Append($"【引擎实时数据】节点数 {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):0}　");
        sb.Append($"FPS {Performance.GetMonitor(Performance.Monitor.TimeFps):0}　");
        sb.Append($"帧时间 {Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0:0.00}ms　");
        sb.Append($"绘制调用 {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0}\n\n");

        sb.Append("【对比】　生成耗时　　之后 1.5s 平均帧时间　　节点增量\n");
        sb.Append(FormatRun("朴素 Instantiate", _naiveResult));
        sb.Append(FormatRun("对象池复用　　", _pooledResult));

        if (_naiveResult is not null && _pooledResult is not null)
        {
            var spawnDelta = _naiveResult.SpawnMs - _pooledResult.SpawnMs;
            var frameDelta = _naiveResult.AvgFrameMs - _pooledResult.AvgFrameMs;
            sb.Append($"\n　→ 生成阶段省下 {spawnDelta:0.0}ms");
            sb.Append($"（朴素 {_naiveResult.SpawnMs:0.0}ms → 池化 {_pooledResult.SpawnMs:0.0}ms）\n");
            sb.Append($"　→ 之后每帧省下 {frameDelta:0.00}ms");
            sb.Append($"（朴素 {_naiveResult.AvgFrameMs:0.00}ms → 池化 {_pooledResult.AvgFrameMs:0.00}ms）\n");
            sb.Append("　→ **" + (frameDelta > 0.05 ? "差距明显，这个场景值得池化" : "差距很小，这个场景池化可有可无") + "**\n");
        }
        else
        {
            sb.Append("\n　（两种方式各跑一次，这里会给出差值）\n");
        }

        sb.Append($"\n【累计】朴素生成 {_spawnedNaive} 颗　池化生成 {_spawnedPooled} 颗　");
        sb.Append($"池复用 {_reuses} 次（= 省下 {_reuses} 次 Instantiate）　池大小 {_pool.Count}\n");
        sb.Append("【按键】1 朴素 1200 颗　2 池化 1200 颗　3 清空　4 预热对象池　F3 引擎性能面板");

        _panel.Text = sb.ToString();
    }

    private static string FormatRun(string label, RunResult? result)
    {
        if (result is null) return $"{label}　（还没测）\n";
        return $"{label}　{result.SpawnMs,7:0.0}ms　　{result.AvgFrameMs,10:0.00}ms　　{result.NodeDelta,8}\n";
    }
}
