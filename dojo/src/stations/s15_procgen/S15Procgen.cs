using System.Text;
using Godot;
using Dojo.Common;
using Dojo.Entities;

namespace Dojo.Stations;

/// <summary>
/// S15 · 程序化生成
///
/// 按键：
///   `1` 切换算法（随机撒点 / 噪声洞穴 / BSP 房间）
///   `2` 换一个种子并重新生成
///   `3` **用同一个种子重新生成** —— 验证"同种子同地图"
///   `4` 连通性修复 开/关（把非最大连通区填成墙）
///   `5` 打乱全局随机，然后再按 3 生成 —— **看它到底可不可复现**
///
/// ★ 本站唯一想让你记住的一句话：
///   **同一套算法 + 同一个种子 = 同一个世界。可复现是关键。**
///
///   为什么可复现这么重要？因为程序化生成的 bug **只在某些种子下出现**。
///   玩家报"我这局出生在墙里了"，你没法复现就完全无从下手。
///   有了种子，他只要把种子告诉你，你就能看到**一模一样**的那张地图。
///   **种子是程序化生成的 bug 报告格式。**
///
/// ★ 那按 `5` 是干什么的？它会打乱 `GD.Randi()` 的全局随机状态。
///   如果你的生成代码用了全局随机（`GD.Randf()` 之类），地图就会跟着变 ——
///   这正是**最常见的"不可复现"来源**：游戏里任何一处随机调用都会污染它。
///   本工程用的是自己持有的 `RandomNumberGenerator`，所以你按 `5` 之后再按 `3`，
///   地图依然一模一样。
///
/// ★ 耗时统计（任务 ⑤）被拆成了两段，因为它们**是两个完全不同的问题**：
///   · **算法耗时** —— 生成 `bool[,]` 网格。想优化就换算法、减少遍历。
///   · **建节点耗时** —— 把网格变成屏幕上的东西。这里才是大头，
///     而且答案是**别一格格建节点**（用 TileMapLayer 或 MultiMesh 批量绘制）。
///   面板会同时显示两者，让你看清 100ms 到底花在哪。
/// </summary>
public partial class S15Procgen : StationBase
{
    public override string StationId => "s15_procgen";

    private static readonly Vector2 Origin = new(80, 152);
    private const float CellSize = DungeonGenerator.CellSize;

    private static readonly string[] AlgorithmNames = { "随机撒点", "噪声洞穴", "BSP 房间" };

    private Player _player = null!;
    private Node2D _tilesRoot = null!;
    private Node2D _overlayRoot = null!;
    private NavigationRegion2D _navRegion = null!;
    private Label _panel = null!;
    private StaticBody2D _wallBody = null!;

    private readonly DungeonGenerator _generator = new();
    private GenAlgorithm _algorithm = GenAlgorithm.BspRooms;
    private int _seed = 20260101;
    private bool _repairConnectivity = true;

    private double _gridMs;
    private double _nodeMs;
    private int _navPolys;
    private string _lastFingerprint = "";
    private int _mapsGenerated;
    private int _reproduceChecks;
    private int _reproduceMatches;
    private bool _sawDisconnected;
    private bool _sawRepairFix;
    private bool _globalRandomPolluted;
    private bool _nodeBuildWasTheBottleneck;
    private readonly HashSet<GenAlgorithm> _triedAlgorithms = new();
    private readonly bool[] _taskDone = new bool[5];

    protected override void StationReady()
    {
        _player = GetNode<Player>("Player");
        _tilesRoot = GetNode<Node2D>("Tiles");
        _overlayRoot = GetNode<Node2D>("Overlay");
        _navRegion = GetNode<NavigationRegion2D>("NavRegion");

        _wallBody = new StaticBody2D
        {
            Name = "WallBody",
            CollisionLayer = GameLayers.World,
            CollisionMask = 0,
        };
        AddChild(_wallBody);

        BuildLabels();
        Regenerate(newSeed: false);

        SetStatus("1 换算法　2 换种子生成　3 同种子重新生成（验证可复现）　4 连通性修复开关　5 打乱全局随机再按 3");
    }

    public override void _Process(double delta)
    {
        _ = delta;
        UpdatePanel();
        CheckGoals();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("aux_1"))
        {
            _algorithm = (GenAlgorithm)(((int)_algorithm + 1) % 3);
            _triedAlgorithms.Add(_algorithm);
            Regenerate(newSeed: true);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("aux_2")) { Regenerate(newSeed: true); GetViewport().SetInputAsHandled(); return; }

        if (@event.IsActionPressed("aux_3"))
        {
            var before = _lastFingerprint;
            Regenerate(newSeed: false);
            _reproduceChecks++;
            if (before == _lastFingerprint) _reproduceMatches++;
            Flash(before == _lastFingerprint
                ? $"可复现 ✓ 种子 {_seed} 两次生成的指纹都是 {_lastFingerprint}"
                : $"不可复现 ✗ 指纹从 {before} 变成了 {_lastFingerprint}", 3.5);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("aux_4"))
        {
            _repairConnectivity = !_repairConnectivity;
            Regenerate(newSeed: false);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("aux_5"))
        {
            // 污染全局随机状态。**如果生成代码用了 GD.Randf()，地图就会变。**
            for (var i = 0; i < 1000; i++) GD.Randi();
            _globalRandomPolluted = true;
            Flash("已污染全局随机状态 —— 现在按 3 看看地图还一样吗", 3.5);
            GetViewport().SetInputAsHandled();
            return;
        }

        base._UnhandledInput(@event);
    }

    // ---------- 生成 ----------

    private void Regenerate(bool newSeed)
    {
        if (newSeed) _seed = Mathf.Abs((int)(Time.GetTicksUsec() & 0x7FFFFFFF));

        // ---- 第一段：纯算法 ----
        var t0 = Time.GetTicksUsec();
        _generator.Generate(_seed, _algorithm);
        _gridMs = (Time.GetTicksUsec() - t0) / 1000.0;

        _triedAlgorithms.Add(_algorithm);
        var wasDisconnected = !_generator.IsFullyConnected;
        if (wasDisconnected) _sawDisconnected = true;

        var filled = 0;
        if (_repairConnectivity && wasDisconnected)
        {
            filled = _generator.KeepLargestRegionOnly();
            if (_generator.IsFullyConnected) _sawRepairFix = true;
        }

        _lastFingerprint = _generator.Fingerprint;
        _mapsGenerated++;

        // ---- 第二段：把网格变成屏幕上的东西 ----
        var t1 = Time.GetTicksUsec();
        RebuildVisuals();
        RebuildCollision();
        RebuildNavMesh();
        _nodeMs = (Time.GetTicksUsec() - t1) / 1000.0;

        if (_nodeMs > _gridMs) _nodeBuildWasTheBottleneck = true;

        // 把玩家放到第一块可走的格子上（否则可能生成在墙里）
        var start = _generator.CellToWorld(_generator.FindOpenCell(), Origin);
        _player.GlobalPosition = start;
        _player.Velocity = Vector2.Zero;

        _ = filled;
    }

    private void RebuildVisuals()
    {
        foreach (var child in _tilesRoot.GetChildren()) child.QueueFree();

        for (var x = 0; x < DungeonGenerator.Width; x++)
        for (var y = 0; y < DungeonGenerator.Height; y++)
        {
            var isWall = _generator.Grid[x, y];
            var position = Origin + new Vector2(x * CellSize + CellSize / 2f, y * CellSize + CellSize / 2f);

            var tile = new Polygon2D
            {
                Name = "T",
                Position = position,
                Color = isWall ? new Color(0.19f, 0.22f, 0.3f) : new Color(0.1f, 0.115f, 0.155f),
                Polygon = LevelKit.RectPoints(new Vector2(CellSize - 1.5f, CellSize - 1.5f)),
            };
            _tilesRoot.AddChild(tile);
        }

        foreach (var child in _overlayRoot.GetChildren()) child.QueueFree();
        var border = new Line2D
        {
            Width = 2f,
            DefaultColor = new Color(0.35f, 0.42f, 0.56f, 0.8f),
            Closed = true,
            Points = new[]
            {
                Origin, Origin + new Vector2(DungeonGenerator.Width * CellSize, 0),
                Origin + new Vector2(DungeonGenerator.Width * CellSize, DungeonGenerator.Height * CellSize),
                Origin + new Vector2(0, DungeonGenerator.Height * CellSize),
            },
        };
        _overlayRoot.AddChild(border);
    }

    private void RebuildCollision()
    {
        foreach (var child in _wallBody.GetChildren()) child.QueueFree();
        foreach (var wall in _generator.WallRects(Origin))
            LevelKit.AddWallShape(_wallBody, wall.GetCenter(), wall.Size, "W");
    }

    private void RebuildNavMesh()
    {
        var area = new Rect2(Origin, new Vector2(DungeonGenerator.Width * CellSize, DungeonGenerator.Height * CellSize));
        _navRegion.NavigationPolygon = NavMeshBuilder.BuildFromWalls(area, _generator.WallRects(Origin), cell: CellSize);
        _navPolys = _navRegion.NavigationPolygon.GetPolygonCount();
    }

    // ---------- 判定 ----------

    private void CheckGoals()
    {
        // 下标对应 StationCatalog 里 S15 的任务顺序：
        //   0 输入种子可复现 · 1 三种算法 · 2 flood fill 验证连通 · 3 把结果画出来 · 4 生成耗时统计
        if (_reproduceChecks > 0 && _reproduceMatches == _reproduceChecks) MarkOnce(0);
        if (_triedAlgorithms.Count >= 3) MarkOnce(1);
        if (_sawDisconnected && _sawRepairFix) MarkOnce(2);
        if (_mapsGenerated >= 5) MarkOnce(3);
        if (_nodeBuildWasTheBottleneck) MarkOnce(4);

        if (!Array.TrueForAll(_taskDone, done => done)) return;

        Complete("同种子同地图、三种算法、连通性验证、绘制、耗时分解 —— 可复现的地牢生成你做出来了");
    }

    private void MarkOnce(int index)
    {
        if (_taskDone[index]) return;
        _taskDone[index] = true;
        MarkTaskDone(index);
    }

    // ---------- 面板 ----------

    private void BuildLabels()
    {
        _panel = LevelKit.MakeLabel(this, new Vector2(24, 56), "", 13,
            new Color(0.87f, 0.92f, 0.98f), HorizontalAlignment.Left, "ProcgenPanel");
        _panel.Size = new Vector2(770, 470);
    }

    private void UpdatePanel()
    {
        var sb = new StringBuilder();

        sb.Append($"【种子】{_seed}　【算法】{AlgorithmNames[(int)_algorithm]}　");
        sb.Append($"【指纹】{_lastFingerprint}　（**同种子同算法 → 指纹必然相同**）\n\n");

        sb.Append($"【可复现验证】按过 {_reproduceChecks} 次「同种子重新生成」，");
        sb.Append(_reproduceChecks == 0 ? "还没试过\n" : $"指纹一致 {_reproduceMatches}/{_reproduceChecks}\n");
        if (_globalRandomPolluted)
            sb.Append("　★ 你污染过全局随机状态（按过 5）—— 结果依然可复现，因为本站没用全局随机\n");
        sb.Append('\n');

        sb.Append("【连通性 flood fill】");
        sb.Append($"可走 {_generator.OpenCells} 格　连通区 {_generator.RegionCount} 个　最大区 {_generator.LargestRegion} 格\n");
        sb.Append(_generator.IsFullyConnected
            ? "　✓ 整张地图连通 —— 玩家一定能走到任意可走的地方\n"
            : "　✗ **地图不连通**！有格子玩家永远到不了（试试把「连通性修复」打开）\n");
        sb.Append($"　连通性修复：{(_repairConnectivity ? "开（非最大连通区会被填成墙）" : "关")}\n\n");

        sb.Append("【耗时分解】← 任务 ⑤ 的答案在这里\n");
        sb.Append($"　① 算法（生成 bool 网格 + flood fill）　{_gridMs,7:0.00}ms\n");
        sb.Append($"　② 建节点（{DungeonGenerator.Width * DungeonGenerator.Height} 个 Polygon2D + 碰撞 + 导航）　{_nodeMs,7:0.00}ms\n");
        sb.Append($"　→ 瓶颈在 **{(_nodeMs > _gridMs ? "建节点" : "算法")}**。");
        sb.Append(_nodeMs > _gridMs
            ? "**优化方向不是算法，是别一格格建节点**（用 TileMapLayer 或 MultiMesh 批量绘制）。\n"
            : "算法本身还有优化空间。\n");
        sb.Append($"　导航网格：{_navPolys} 个多边形（**用生成出来的墙算的 —— 同一份数据，三种用途**）\n\n");

        sb.Append($"【累计】生成 {_mapsGenerated} 张地图　试过算法 {_triedAlgorithms.Count}/3\n");
        sb.Append("【按键】1 换算法　2 换种子　3 同种子重新生成　4 连通性修复　5 污染全局随机");

        _panel.Text = sb.ToString();
    }
}
