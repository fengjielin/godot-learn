using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// S01 · 节点与生命周期
///
/// 玩法：按住空格不断生成会撞墙反弹的方块，方块活 3 秒后自毁。
/// 目标：让屏幕上同时存在 10 个方块。
///
/// 这一站刻意不讲任何复杂系统，只讲最基础也最容易搞错的东西：
///   场景树是什么、节点在什么时候被创建/初始化/更新/销毁、
///   以及「信号」和「分组」这两个 Godot 里最重要的解耦工具。
///
/// 建议的练法：
///   1. 先在 Godot 编辑器里打开 src/stations/s01_lifecycle/s01_lifecycle.tscn，
///      看右边「场景」面板里的树结构，对照 Wanderer.cs 里的 _EnterTree/_Ready/_ExitTree。
///   2. 运行，按住空格，同时打开「输出」面板观察打印顺序。
///   3. 然后按 TAB 里的任务逐条改代码。每改一条就重跑一次，看现象变化。
/// </summary>
public partial class S01Lifecycle : StationBase
{
    public override string StationId => "s01_lifecycle";

    /// <summary>过关条件：同时存在的方块数。</summary>
    private const int AliveGoal = 10;

    /// <summary>按住空格时的生成间隔（秒）。</summary>
    private const double SpawnInterval = 0.11;

    private static readonly Vector2 SpawnPoint = new(360, 626);
    private static readonly Vector2 GatherPoint = new(760, 404);
    private static readonly Rect2 PlayArea = new(40, 128, 1200, 560);

    private PackedScene _wandererScene = null!;
    private Node2D _spawnedRoot = null!;
    private Label _idleHint = null!;

    private double _spawnCooldown;
    private double _statusRefreshTimer;
    private int _spawnCounter;
    private int _totalSpawned;
    private int _peakAlive;
    private int _lastSurvivedId;
    private float _lastSurvivedSeconds;

    protected override void StationReady()
    {
        _wandererScene = GD.Load<PackedScene>("res://src/stations/s01_lifecycle/wanderer.tscn")
                         ?? throw new InvalidOperationException("wanderer.tscn 加载失败");
        _spawnedRoot = GetNode<Node2D>("Spawned");

        LevelKit.CreateBorderWalls(this, PlayArea);
        BuildGuideVisuals();

        RefreshStatus();
    }

    public override void _Process(double delta)
    {
        // 用「轮询」而不是「事件」来处理连续触发：按住空格就持续生成。
        // 这两种输入方式的选择标准见练习站 s02_input。
        if (Input.IsActionPressed("spawn") && _spawnCooldown <= 0)
        {
            SpawnWanderer();
            _spawnCooldown = SpawnInterval;
        }

        _spawnCooldown -= delta;

        if (Input.IsActionJustPressed("command"))
            CommandAllToGatherPoint();

        _statusRefreshTimer += delta;
        if (_statusRefreshTimer >= 0.15)
        {
            _statusRefreshTimer = 0;
            RefreshStatus();
        }
    }

    private void BuildGuideVisuals()
    {
        // 生成台
        LevelKit.MakeRect(this, SpawnPoint + new Vector2(0, 52), new Vector2(160, 26), new Color(0.16f, 0.22f, 0.30f));
        LevelKit.MakeLabel(this, SpawnPoint + new Vector2(-150, 40), "生成台：按住 空格", 15,
            new Color(0.62f, 0.9f, 0.8f), HorizontalAlignment.Left);

        // 集合点（按 G 把所有方块赶过来）
        LevelKit.MakeCircle(this, GatherPoint, 62f, new Color(0.20f, 0.16f, 0.10f), 40);
        LevelKit.MakeCircle(this, GatherPoint, 52f, new Color(0.28f, 0.22f, 0.13f), 40);
        LevelKit.MakeLabel(this, GatherPoint + new Vector2(0, 72), "集合点：按 G 用 CallGroup 下达指令", 15,
            new Color(0.95f, 0.82f, 0.5f));

        _idleHint = LevelKit.MakeLabel(this, new Vector2(640, 176),
            "什么都没有？按住 空格 生成游走方块", 18, new Color(0.7f, 0.78f, 0.9f));
    }

    private void SpawnWanderer()
    {
        var wanderer = _wandererScene.Instantiate<Wanderer>();

        // Id 必须在 AddChild 之前赋值 —— 因为 AddChild 会立刻触发 _EnterTree，
        // 那里就已经在打印 Id 了。这是「节点生命周期顺序」最典型的一个坑。
        _spawnCounter++;
        wanderer.Id = _spawnCounter;
        wanderer.Position = SpawnPoint + new Vector2(
            (float)GD.RandRange(-46, 46),
            (float)GD.RandRange(-18, 18));

        _spawnedRoot.AddChild(wanderer);

        wanderer.Bounced += OnWandererBounced;
        wanderer.Expired += OnWandererExpired;

        _totalSpawned++;
    }

    private void OnWandererBounced(int bounceCount)
    {
        // 这里故意什么都不做，只是证明信号确实在发。
        // 想验证的话，把下面这行的注释去掉，观察输出面板。
        // GD.Print($"[S01] 收到 Bounced 信号：第 {bounceCount} 次弹跳");
    }

    private void OnWandererExpired(int id, float survivedSeconds)
    {
        _lastSurvivedId = id;
        _lastSurvivedSeconds = survivedSeconds;
    }

    private void CommandAllToGatherPoint()
    {
        // CallGroup 是「一对多」的指令：不需要持有任何方块的引用，
        // 只要它们加入了 "wanderers" 分组，就能一次性调用同名方法。
        // 这就是 Godot 里最轻量的解耦手段。
        GetTree().CallGroup("wanderers", "RetreatTo", GatherPoint);
        Flash("已用 GetTree().CallGroup 给所有 wanderers 下达 RetreatTo 指令");
        MarkTaskDone(3);
    }

    private void RefreshStatus()
    {
        var alive = GetTree().GetNodesInGroup("wanderers").Count;
        _peakAlive = Mathf.Max(_peakAlive, alive);

        _idleHint.Visible = _totalSpawned == 0;

        var survivedText = _lastSurvivedId > 0
            ? $"    最近销毁：#{_lastSurvivedId} 活了 {_lastSurvivedSeconds:0.0} 秒"
            : "";

        SetStatus($"在场 {alive}    峰值 {_peakAlive}/{AliveGoal}    累计生成 {_totalSpawned}{survivedText}");

        if (_peakAlive < AliveGoal) return;

        MarkTaskDone(0);
        Complete($"同时存在过 {_peakAlive} 个游走方块");
    }
}
