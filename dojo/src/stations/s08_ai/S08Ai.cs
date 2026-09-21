using System.Text;
using Godot;
using Dojo.Common;
using Dojo.Entities;

namespace Dojo.Stations;

/// <summary>
/// S08 · 敌人 AI 与寻路
///
/// 五只敌人在一个迷宫里巡逻。你走进去，它们会：
///   · 用**视野锥**发现你（绕到背后就不会被发现）
///   · 用**听觉**听到你冲刺（安静地走就不会）
///   · 用**导航网格**绕开墙追你（不是一头撞在墙上）
///   · 用**分离力**摊开包围你（不是五个叠成一个）
///   · 丢失目标后去**最后知道的位置搜索**（不是立刻忘记）
///
/// 左上角面板上有五个开关，可以逐个关掉这些能力，**亲身对比每一条值多少**。
///
/// ★ 一个工程上的关键决定：**墙的矩形定义只有一份**（`WallRects`），
///   碰撞体、导航网格、可见图形全部由它生成。
///   如果墙的碰撞和导航网格来自两份不同的数据，你迟早会遇到
///   "敌人撞在看不见的墙上"或者"敌人穿墙走过来"这类没法调试的问题。
///   **同一份数据 → 多个表示，而不是多个数据源。**
/// </summary>
public partial class S08Ai : StationBase
{
    public override string StationId => "s08_ai";

    private static readonly Rect2 PlayArea = new(40, 130, 1200, 560);

    /// <summary>
    /// 迷宫的墙。**唯一数据源** —— 碰撞、导航网格、画面都由它生成。
    /// 摆成一个"之"字形，逼着敌人必须绕路才能走到玩家身边。
    /// </summary>
    private static readonly Rect2[] WallRects =
    {
        // 横在上方的那一面墙逼着敌人在两个巡逻点之间绕上去再绕下来 ——
        // 这就是"寻路"和"直线走"肉眼可见的区别。
        new(760, 150, 44, 240),
        new(380, 380, 44, 310),
        new(1050, 200, 44, 320),
    };

    private const float SprintSpeed = 340f;
    private const float WalkSpeed = 235f;
    private const float NoiseInterval = 0.45f;

    /// <summary>
    /// 五只敌人。**分散在巡逻路线的不同位置** —— 1 号就在"朝玩家那一侧走"的点上，
    /// 所以游戏开始一两秒内就会有人发现你，不用等一整圈。
    /// </summary>
    private static readonly Vector2[] EnemySpawns =
    {
        new(900, 600),
        new(620, 600),
        new(620, 320),
        new(900, 320),
        new(760, 250),
    };

    /// <summary>
    /// 巡逻路线。注意它必须**沿着导航网格走**才能顺畅 ——
    /// (620,320) → (900,320) 这一段中间隔着上方那面墙，
    /// 敌人会自己绕上去再下来。这正是"寻路"和"直线走"的区别。
    /// </summary>
    private static readonly Vector2[] PatrolRoute =
    {
        new(620, 600), new(620, 320), new(900, 320), new(900, 600),
    };

    private Player _player = null!;
    private Node2D _enemiesRoot = null!;
    private Node2D _wallsRoot = null!;
    private NavigationRegion2D _navRegion = null!;
    private PackedScene _enemyScene = null!;
    private EnemyAgent[] _enemies = Array.Empty<EnemyAgent>();
    private Label _panel = null!;

    private float _noiseTimer;
    private float _sprintSeconds;
    private float _behindBackTimer;
    private int _noiseEvents;
    private float _minSeparation = 999f;
    private bool _sawChaseNearby;
    private bool _sawSearch;
    private bool _noiseAttracted;
    private readonly bool[] _taskDone = new bool[5];

    protected override void StationReady()
    {
        _player = GetNode<Player>("Player");
        _enemiesRoot = GetNode<Node2D>("Enemies");
        _wallsRoot = GetNode<Node2D>("Walls");
        _navRegion = GetNode<NavigationRegion2D>("NavRegion");

        _enemyScene = GD.Load<PackedScene>("res://src/stations/s08_ai/enemy_agent.tscn")
                      ?? throw new InvalidOperationException("enemy_agent.tscn 加载失败");

        LevelKit.CreateBorderWalls(this, PlayArea);
        BuildWallsFromSingleSource();
        BuildNavMesh();
        SpawnEnemies();
        BuildLabels();

        SetStatus("走进迷宫：绕到背后它看不见你，冲刺会被听见，关掉左边五个开关之一再试一次");
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        UpdateSprint(dt);
        TrackBehindBack(dt);
        TrackSeparation();
        CheckGoals();
        UpdatePanel();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("aux_1")) { EnemyAgent.VisionConeEnabled = !EnemyAgent.VisionConeEnabled; Flash($"视野锥：{OnOff(EnemyAgent.VisionConeEnabled)}"); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_2")) { EnemyAgent.HearingEnabled = !EnemyAgent.HearingEnabled; Flash($"听觉：{OnOff(EnemyAgent.HearingEnabled)}"); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_3")) { EnemyAgent.PathfindingEnabled = !EnemyAgent.PathfindingEnabled; Flash($"寻路：{OnOff(EnemyAgent.PathfindingEnabled)}"); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_4")) { EnemyAgent.SeparationEnabled = !EnemyAgent.SeparationEnabled; Flash($"分离力：{OnOff(EnemyAgent.SeparationEnabled)}"); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_5")) { EnemyAgent.SearchEnabled = !EnemyAgent.SearchEnabled; Flash($"搜索行为：{OnOff(EnemyAgent.SearchEnabled)}"); GetViewport().SetInputAsHandled(); return; }

        base._UnhandledInput(@event);
    }

    // ---------- 关卡：一份数据，三种表示 ----------

    private void BuildWallsFromSingleSource()
    {
        var body = new StaticBody2D
        {
            Name = "WallBody",
            CollisionLayer = GameLayers.World,
            CollisionMask = 0,
        };
        _wallsRoot.AddChild(body);

        foreach (var wall in WallRects)
        {
            // ① 看得见
            var center = wall.GetCenter();
            LevelKit.MakeRect(body, center, wall.Size, new Color(0.27f, 0.31f, 0.39f), "WallVisual");
            LevelKit.MakeRect(body, new Vector2(center.X, wall.Position.Y + 4f),
                new Vector2(wall.Size.X, 8f), new Color(0.44f, 0.5f, 0.62f), "WallTop");

            // ② 撞得到
            LevelKit.AddWallShape(body, center, wall.Size, "WallShape");
        }
    }

    private void BuildNavMesh()
    {
        // ③ 走得到 —— 导航网格由**同一份** WallRects 生成
        _navRegion.NavigationPolygon = NavMeshBuilder.BuildFromWalls(PlayArea, WallRects, cell: 40f);

        var count = _navRegion.NavigationPolygon.GetPolygonCount();
        var verts = _navRegion.NavigationPolygon.GetVertices().Length;
        GD.Print($"[S08] 导航网格：{count} 个多边形，{verts} 个顶点");
    }

    private void SpawnEnemies()
    {
        var list = new List<EnemyAgent>();

        foreach (var spawn in EnemySpawns)
        {
            var enemy = _enemyScene.Instantiate<EnemyAgent>();
            enemy.Position = spawn;
            enemy.Target = _player;
            enemy.Waypoints = PatrolRoute;
            enemy.WaypointIndex = Array.IndexOf(EnemySpawns, spawn) % PatrolRoute.Length;

            _enemiesRoot.AddChild(enemy);
            enemy.StateChanged += OnEnemyStateChanged;
            list.Add(enemy);
        }

        _enemies = list.ToArray();
    }

    // ---------- 冲刺与噪音 ----------

    private void UpdateSprint(float dt)
    {
        // 冲刺：更快，但**会发出声音**。这是"速度换隐蔽"这个经典取舍的最小实现。
        var moving = _player.InputDirection != Vector2.Zero;
        var sprinting = moving && Input.IsActionPressed("dash");

        _player.MaxSpeed = sprinting ? SprintSpeed : WalkSpeed;

        if (!sprinting)
        {
            _noiseTimer = 0f;
            return;
        }

        _sprintSeconds += dt;
        _noiseTimer -= dt;
        if (_noiseTimer > 0f) return;

        _noiseTimer = NoiseInterval;
        EmitNoise(_player.GlobalPosition);
    }

    private void EmitNoise(Vector2 position)
    {
        _noiseEvents++;
        foreach (var enemy in _enemies)
            enemy.Hear(position);
    }

    // ---------- 统计 ----------

    /// <summary>
    /// 算一下"玩家是不是在某个敌人的背后" —— 在它的视野距离内，却不在它的视野锥内。
    /// 这是"潜行"这个玩法能不能成立的**唯一判据**，值得单独量出来。
    /// </summary>
    private void TrackBehindBack(float dt)
    {
        foreach (var enemy in _enemies)
        {
            var toPlayer = _player.GlobalPosition - enemy.GlobalPosition;
            if (toPlayer.Length() > enemy.SightRange) continue;

            var angleDeg = Mathf.RadToDeg(enemy.FacingDir.AngleTo(toPlayer));
            if (Mathf.Abs(angleDeg) <= enemy.VisionHalfAngleDeg) continue;

            _behindBackTimer += dt;
            return;
        }
    }

    private void TrackSeparation()
    {
        _minSeparation = 999f;
        for (var i = 0; i < _enemies.Length; i++)
        {
            for (var j = i + 1; j < _enemies.Length; j++)
            {
                var distance = _enemies[i].GlobalPosition.DistanceTo(_enemies[j].GlobalPosition);
                _minSeparation = Mathf.Min(_minSeparation, distance);
            }
        }
    }

    private void OnEnemyStateChanged(string from, string to, string reason)
    {
        if (to == "Search") _sawSearch = true;

        // Patrol → Search 只有一条路：听到了动静。
        // 所以这一条转移就是"听觉真的起效了"的直接证据 ——
        // 比"你按了冲刺键"强得多：**判定要盯着结果，不要盯着输入。**
        if (from == "Patrol" && to == "Search") _noiseAttracted = true;

        _ = reason;
    }

    private void CheckGoals()
    {
        // ① 视野锥：绕到背后而不被发现
        if (_behindBackTimer >= 1.0f) MarkOnce(0);

        // ② 听觉：有敌人因为听到噪音而改变行为
        if (_noiseAttracted) MarkOnce(1);

        // ③ 寻路：有敌人绕开墙追到了你身边
        var chasers = 0;
        foreach (var enemy in _enemies)
        {
            if (enemy.Machine.CurrentName != "Chase") continue;
            chasers++;
            if (enemy.GlobalPosition.DistanceTo(_player.GlobalPosition) < 110f) _sawChaseNearby = true;
        }

        if (_sawChaseNearby) MarkOnce(2);

        // ④ 分离力：四只以上同时追
        if (chasers >= 4) MarkOnce(3);

        // ⑤ 搜索行为
        if (_sawSearch) MarkOnce(4);

        if (!Array.TrueForAll(_taskDone, done => done)) return;

        Complete("视野锥、听觉、导航寻路、群体分离、丢失搜索 —— 五条 AI 能力你都亲眼见过它们生效了");
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
            new Color(0.87f, 0.92f, 0.98f), HorizontalAlignment.Left, "AiPanel");
        _panel.Size = new Vector2(770, 420);

        LevelKit.MakeLabel(this, new Vector2(120, 662), "你的起点", 13, new Color(0.7f, 0.78f, 0.92f));
    }

    private void UpdatePanel()
    {
        var sb = new StringBuilder();

        sb.Append($"【导航网格】{_navRegion.NavigationPolygon.GetPolygonCount()} 个多边形（40px 格子）　");
        sb.Append($"路径点数 = 敌人当前位置到目标的折线顶点数\n");
        sb.Append($"【噪音】冲刺 {(EnemyAgent.HearingEnabled ? "会被听见" : "无效")}　已发 {_noiseEvents} 次　");
        sb.Append($"（安静走路 = 不发声）\n\n");

        var chasers = 0;
        for (var i = 0; i < _enemies.Length; i++)
        {
            var enemy = _enemies[i];
            var state = enemy.Machine.CurrentName;
            if (state == "Chase") chasers++;

            var seen = enemy.CanSeeTargetNow ? "见" : "—";
            var distance = enemy.GlobalPosition.DistanceTo(_player.GlobalPosition);
            sb.Append($"#{i + 1} {state,-7} {seen}  路径 {enemy.PathPointCount,2} 点  距你 {distance,4:0}\n");
        }

        sb.Append('\n');
        sb.Append($"【开关】1 视野锥[{OnOff(EnemyAgent.VisionConeEnabled)}]　2 听觉[{OnOff(EnemyAgent.HearingEnabled)}]　");
        sb.Append($"3 寻路[{OnOff(EnemyAgent.PathfindingEnabled)}]　4 分离力[{OnOff(EnemyAgent.SeparationEnabled)}]　");
        sb.Append($"5 搜索[{OnOff(EnemyAgent.SearchEnabled)}]\n");
        sb.Append($"【统计】追击中 {chasers}/{_enemies.Length}　敌人最小间距 {_minSeparation,4:0}px　");
        sb.Append($"绕到背后 {_behindBackTimer:0.0}s　冲刺 {_sprintSeconds:0.0}s");

        _panel.Text = sb.ToString();
    }

    private static string OnOff(bool value) => value ? "开" : "关";
}
