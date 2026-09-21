using Godot;
using Dojo.Common;
using Dojo.Game;

namespace Dojo.Hub;

/// <summary>
/// 枢纽 —— 练功房的大厅。16 座传送门通向 16 个练习站。
///
/// 学习要点：
///   1. 传送门不是手摆 16 个，而是「读目录 → 循环实例化」。这是数据驱动的最小形态：
///      加一个练习站只需要改 StationCatalog，枢纽不用动。
///   2. 订阅全局信号后，一定要在 _ExitTree 里退订。Hub 会在每次从练习站返回时重建，
///      如果只订阅不退订，EventBus 上就会挂着一堆已释放的 Hub，报 "Object has been freed"。
///      这个坑在练习站 s13_events 会让你亲手踩一次。
///   3. _Process 里做的「每 30 秒自动存档」演示了游戏里最常见的定时做法：
///      累加 delta，超过阈值就触发并清零。比开一个 Timer 节点更轻。
/// </summary>
public partial class Hub : Node2D
{
    /// <summary>传送门网格：左上角第一个门的位置。</summary>
    [Export] public Vector2 GridOrigin { get; set; } = new(190, 202);

    /// <summary>
    /// 传送门网格的格子间距。
    /// 竖直间距 130 是算出来的：门上方要放标题（-64~-36），下方要放提示（+34~+64），
    /// 所以相邻两行的「下行提示底部」到「上行标题顶部」必须留出余量，否则文字会叠在一起。
    /// </summary>
    [Export] public Vector2 CellSize { get; set; } = new(300, 130);

    /// <summary>每行几个门。</summary>
    [Export] public int Columns { get; set; } = 4;

    /// <summary>枢纽可活动区域，四面会被自动围上墙。</summary>
    [Export] public Rect2 PlayableArea { get; set; } = new(24, 118, 1232, 566);

    private const double AutoSaveInterval = 30.0;

    private Node2D _portalsRoot = null!;
    private Label _progressLabel = null!;
    private Label _statusLabel = null!;
    private PackedScene _portalScene = null!;
    private double _autoSaveTimer;

    public override void _Ready()
    {
        _portalsRoot = GetNode<Node2D>("Portals");
        _progressLabel = GetNode<Label>("HubHud/TopBar/ProgressLabel");
        _statusLabel = GetNode<Label>("HubHud/StatusLabel");

        _portalScene = GD.Load<PackedScene>("res://src/hub/portal.tscn")
            ?? throw new InvalidOperationException("portal.tscn 加载失败");

        LevelKit.CreateBorderWalls(this, PlayableArea);
        BuildPortals();
        RefreshHud();

        EventBus.Instance.StationCompleted += OnStationCompleted;

        // 音频资源还没生成时，这一句会静默跳过（AudioManager 内部检查了文件是否存在）
        AudioManager.Instance.PlayMusic("res://assets/audio/bgm_hub.ogg");
    }

    public override void _ExitTree()
    {
        // 退订！否则 EventBus 会一直持有已销毁的 Hub。
        EventBus.Instance.StationCompleted -= OnStationCompleted;
    }

    public override void _Process(double delta)
    {
        _autoSaveTimer += delta;
        if (_autoSaveTimer < AutoSaveInterval) return;

        _autoSaveTimer = 0;
        SaveSystem.Instance.Save();
    }

    private void BuildPortals()
    {
        var all = StationCatalog.All;
        for (var i = 0; i < all.Length; i++)
        {
            var info = all[i];
            var column = i % Columns;
            var row = i / Columns;

            var portal = _portalScene.Instantiate<Portal>();
            portal.Position = GridOrigin + new Vector2(column * CellSize.X, row * CellSize.Y);

            _portalsRoot.AddChild(portal);
            // Configure 必须在 AddChild 之后：它要用到 _Ready 里缓存好的子节点。
            portal.Configure(info);
        }
    }

    private void RefreshHud()
    {
        var data = SaveSystem.Instance.Data;
        _progressLabel.Text =
            $"练习站 {StationCatalog.ImplementedCount}/{StationCatalog.TotalCount} 已建成    " +
            $"已通关 {data.CompletedStations.Count}/{StationCatalog.TotalCount}";

        var last = data.LastStationId;
        var lastInfo = last.Length > 0 ? StationCatalog.Get(last) : null;
        _statusLabel.Text = lastInfo is not null
            ? $"上次在：{lastInfo.DisplayName}（再进去一次即可刷新手感）"
            : "从 S01 开始：走上任意传送门，按 E 进入";
    }

    private void OnStationCompleted(string stationId)
    {
        EventBus.Instance.EmitNotice($"练习站完成：{stationId}");
        RefreshHud();
    }
}
