using System.Text;
using Godot;
using Dojo.Common;
using Dojo.Entities;

namespace Dojo.Stations;

/// <summary>
/// S13 · 事件总线
///
/// 场景里有两样东西：**金币**（走过去就捡）和**靶子**（按 J 打掉）。
/// 还有三个**互相不认识**的东西在后台工作：成就系统、飘字提示、统计面板。
///
/// 按 `1` 在两种写法之间切换：
///   · **事件总线**：金币只知道"我被捡了"，把事件发出去就完事
///   · **直接调用**：金币必须持有成就系统的引用，直接调用它的公开方法
/// 两种写法效果**完全一样**，差别只在"谁认识谁"。
///
/// 按 `2` 制造一次**悬空连接**：让一个临时节点订阅 `EventBus.NoticeCSharp`
/// （普通 C# event，不是 Godot 信号），然后销毁它、**故意不退订**，再发一次事件。
/// 你会看到一条 `ObjectDisposedException` —— 本站会把它捕获下来显示在面板上。
///
/// ★ 这一站唯一想让你记住的结论：
///   **事件总线用「可追踪性」换了「可扩展性」。**
///   好处：加一个成就，一行已有代码都不用改。
///   代价：打开 `Pickup.cs`，你看不到成就系统的存在；想知道全貌只能全局搜索。
///   所以——**核心流程用直接调用，横切关注点（成就/统计/音效/提示）才用事件。**
/// </summary>
public partial class S13Events : StationBase
{
    public override string StationId => "s13_events";

    private static readonly Rect2 PlayArea = new(40, 130, 1200, 560);

    private static readonly Vector2[] PickupSpots =
    {
        new(300, 250), new(470, 250), new(640, 250),
        new(300, 380), new(470, 380), new(640, 380), new(810, 380),
    };

    private static readonly Vector2[] TargetSpots =
    {
        new(880, 250), new(1000, 400), new(880, 550),
    };

    private sealed class Target
    {
        public required Node2D Node { get; init; }
        public required Polygon2D Body { get; init; }
        public bool Alive { get; set; } = true;
    }

    private Player _player = null!;
    private Node2D _pickupsRoot = null!;
    private Node2D _targetsRoot = null!;
    private CanvasLayer _toastLayer = null!;
    private AchievementSystem _achievements = null!;
    private PackedScene _pickupScene = null!;
    private Label _panel = null!;
    private VBoxContainer _toastBox = null!;

    private readonly List<Target> _targets = new();
    private readonly List<Label> _toasts = new();
    private Vector2 _lastPlayerPosition;
    private float _travelled;

    private bool _useEventBus = true;
    private string _danglingError = "";
    private bool _danglingDemoDone;
    private Node? _leaky;
    private int _danglingStep = -1;
    private int _pickupsCollected;
    private int _modeSwitches;
    private readonly bool[] _taskDone = new bool[5];

    protected override void StationReady()
    {
        _player = GetNode<Player>("Player");
        _pickupsRoot = GetNode<Node2D>("Pickups");
        _targetsRoot = GetNode<Node2D>("Targets");
        _toastLayer = GetNode<CanvasLayer>("ToastLayer");
        _achievements = GetNode<AchievementSystem>("Achievements");

        _pickupScene = GD.Load<PackedScene>("res://src/stations/s13_events/pickup.tscn")
                       ?? throw new InvalidOperationException("pickup.tscn 加载失败");

        _lastPlayerPosition = _player.GlobalPosition;

        LevelKit.CreateBorderWalls(this, PlayArea);
        BuildPickups();
        BuildTargets();
        BuildToasts();
        BuildLabels();

        _achievements.Unlocked += OnAchievementUnlocked;
        EventBus.Instance.Notice += OnNotice;

        SetStatus("捡金币、按 J 打靶子。按 1 切换「事件总线 / 直接调用」两种写法，按 2 制造一次悬空连接错误");
    }

    public override void _ExitTree()
    {
        // 站台自己也是订阅者 —— 同样要在 _ExitTree 里断开。
        // （Godot 信号会在节点释放时自动清理，**但养成写全的习惯**，
        //   因为你迟早会用到不会自动清理的 C# event。）
        var bus = EventBus.Instance;
        if (IsInstanceValid(bus)) bus.Notice -= OnNotice;
        base._ExitTree();
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        StepDanglingDemo();
        TrackDistance(dt);
        UpdateToasts(dt);
        UpdatePanel();
        CheckGoals();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("aux_1"))
        {
            _useEventBus = !_useEventBus;
            _modeSwitches++;
            foreach (var pickup in _pickupsRoot.GetChildren())
                if (pickup is Pickup p) p.UseEventBus = _useEventBus;
            Flash(_useEventBus ? "写法：走事件总线（金币不认识任何人）" : "写法：直接调用（金币持有成就系统的引用）");
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("aux_2")) { RunDanglingConnectionDemo(); GetViewport().SetInputAsHandled(); return; }

        if (@event.IsActionPressed("attack"))
        {
            AttackNearest();
            GetViewport().SetInputAsHandled();
            return;
        }

        base._UnhandledInput(@event);
    }

    // ---------- 场景内容 ----------

    private void BuildPickups()
    {
        foreach (var spot in PickupSpots)
        {
            var pickup = _pickupScene.Instantiate<Pickup>();
            pickup.Position = spot;
            pickup.ItemId = "coin";
            pickup.Amount = 1;
            pickup.UseEventBus = _useEventBus;
            pickup.DirectReceiver = _achievements;   // 「直接调用」模式下的耦合点
            pickup.Collected += OnPickupCollected;
            _pickupsRoot.AddChild(pickup);
        }
    }

    private void BuildTargets()
    {
        foreach (var spot in TargetSpots)
        {
            var node = new Node2D { Name = "Target", Position = spot };
            _targetsRoot.AddChild(node);

            var body = LevelKit.MakeCircle(node, Vector2.Zero, 26f, new Color(0.72f, 0.32f, 0.42f), 24, "Body");
            LevelKit.MakeCircle(node, Vector2.Zero, 11f, new Color(0.98f, 0.72f, 0.6f), 18, "Core");

            _targets.Add(new Target { Node = node, Body = body });
        }
    }

    private void BuildToasts()
    {
        _toastBox = new VBoxContainer
        {
            Name = "Toasts",
            OffsetLeft = 880,
            OffsetTop = 300,
            OffsetRight = 1250,
            OffsetBottom = 640,
        };
        _toastBox.AddThemeConstantOverride("separation", 6);
        _toastLayer.AddChild(_toastBox);
    }

    private void BuildLabels()
    {
        _panel = LevelKit.MakeLabel(this, new Vector2(24, 58), "", 14,
            new Color(0.87f, 0.92f, 0.98f), HorizontalAlignment.Left, "EventPanel");
        _panel.Size = new Vector2(770, 420);
    }

    // ---------- 行为 ----------

    private void AttackNearest()
    {
        var aim = _player.AimDirection;
        Target? best = null;
        var bestDistance = 96f;

        foreach (var target in _targets)
        {
            if (!target.Alive) continue;
            var offset = target.Node.GlobalPosition - _player.GlobalPosition;
            if (aim.Dot(offset.Normalized()) < 0.35f) continue;

            var distance = offset.Length();
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = target;
        }

        if (best is null) return;

        best.Alive = false;
        best.Body.Color = new Color(0.26f, 0.24f, 0.3f);
        ScreenFx.Instance.Shake(0.4f);

        // ✅ 打掉一个靶子 = 发一个事件。站台**不需要**认识成就系统。
        EventBus.Instance.EmitSignal(EventBus.SignalName.EnemyKilled, "target", best.Node.GlobalPosition);
        EventBus.Instance.EmitNotice("击杀了一个靶子");
    }

    private void TrackDistance(float dt)
    {
        var position = _player.GlobalPosition;
        _travelled += position.DistanceTo(_lastPlayerPosition);
        _lastPlayerPosition = position;
        _achievements.ReportDistance(_travelled);
        _ = dt;
    }

    /// <summary>
    /// ★ 故意制造一次「悬空连接」。
    ///
    /// 步骤：让一个临时节点订阅 `NoticeCSharp`（**普通 C# event**）→ 销毁它 →
    /// **不退订** → 再发一次事件。
    ///
    /// 结果：调用列表里还留着那个已释放对象的委托，
    /// `Invoke` 时抛 `ObjectDisposedException`。
    ///
    /// **对比：如果把这里换成 Godot 信号（`Notice`），什么都不会发生** ——
    /// 引擎在节点被释放时会自动清理连接。这就是两者最实际的区别。
    /// </summary>
    private void RunDanglingConnectionDemo()
    {
        _danglingDemoDone = true;

        _leaky = new Node { Name = "LeakyListener" };
        AddChild(_leaky);

        // ★ 关键：让闭包**抓住这个节点**，订阅到普通 C# event 上。
        //   等这个节点被释放之后，闭包体里的 `captured.Name` 就会访问一个已释放对象。
        var captured = _leaky;
        EventBus.Instance.NoticeCSharp += text => GD.Print($"[Leaky {captured.Name}] {text}");

        // 用帧计数推进：QueueFree 是延迟的，必须等它真的释放之后再发事件。
        // （第一次我用 `temporary.Ready +=` 订阅，结果 AddChild 时 _Ready 早就跑完了，
        //   订阅根本没建立 —— 演示静默失败，面板还老实显示"没有报错"。）
        _danglingStep = 0;
    }

    private void StepDanglingDemo()
    {
        if (_danglingStep < 0) return;

        _danglingStep++;
        if (_danglingStep == 2)
        {
            _leaky?.QueueFree();
            return;
        }

        if (_danglingStep < 5) return;

        _danglingStep = -1;
        try
        {
            EventBus.Instance.EmitNoticeCSharp("这条消息会撞上已释放的订阅者");
            _danglingError = "（没有报错）";
        }
        catch (Exception ex)
        {
            _danglingError = $"{ex.GetType().Name}：{ex.Message}";
        }

        GD.PrintErr($"[S13] 悬空连接演示捕获到：{_danglingError}");
        EventBus.Instance.ClearCSharpSubscribers();
        Flash("悬空连接演示完成 —— 看面板里捕获到的异常", 3.5);
    }

    private void OnPickupCollected(Pickup pickup)
    {
        _pickupsCollected++;
        _ = pickup;

        // 统计走的是**另一条**通路：站台自己订阅自己的 C# event，不经过事件总线。
        // **不是所有东西都该走总线** —— 只关心自己人的通知，用普通 event 更直接。
    }

    private void OnAchievementUnlocked(AchievementSystem.UnlockedAchievement achievement)
    {
        EventBus.Instance.EmitNotice($"解锁成就：{achievement.Title}");
        PushToast($"★ {achievement.Title}", achievement.Detail, new Color(1f, 0.88f, 0.42f));
    }

    private void OnNotice(string text) => PushToast("·", text, new Color(0.82f, 0.88f, 0.98f));

    private void PushToast(string prefix, string text, Color color)
    {
        var label = new Label
        {
            Text = $"{prefix} {text}",
            Modulate = color,
        };
        label.AddThemeFontSizeOverride("font_size", 15);
        _toastBox.AddChild(label);
        _toasts.Add(label);
    }

    private void UpdateToasts(float dt)
    {
        for (var i = _toasts.Count - 1; i >= 0; i--)
        {
            var label = _toasts[i];
            label.Modulate = new Color(label.Modulate.R, label.Modulate.G, label.Modulate.B,
                label.Modulate.A - dt * 0.35f);

            if (label.Modulate.A > 0.02f) continue;
            _toasts.RemoveAt(i);
            label.QueueFree();
        }
    }

    // ---------- 判定 ----------

    private void CheckGoals()
    {
        // 下标对应 StationCatalog 里 S13 的任务顺序：
        //   0 新增事件让成就弹提示 · 1 把直接调用改成事件总线 · 2 制造悬空连接错误
        //   3 加本局统计面板 · 4 思考什么时候不该用事件总线
        if (_achievements.History.Count >= 1) MarkOnce(0);
        if (_modeSwitches > 0) MarkOnce(1);
        if (_danglingDemoDone) MarkOnce(2);
        if (_achStatsDone()) MarkOnce(3);
        if (_modeSwitches >= 2) MarkOnce(4);

        if (!Array.TrueForAll(_taskDone, done => done)) return;

        Complete("事件、直接调用、悬空连接、统计面板 —— 你现在知道事件总线买到了什么、付出了什么");
    }

    private bool _achStatsDone() => _achievements.History.Count >= 3;

    private void MarkOnce(int index)
    {
        if (_taskDone[index]) return;
        _taskDone[index] = true;
        MarkTaskDone(index);
    }

    // ---------- 面板 ----------

    private void UpdatePanel()
    {
        var sb = new StringBuilder();

        sb.Append("【两种写法对比】当前：");
        sb.Append(_useEventBus ? "**事件总线**\n" : "**直接调用**\n");

        if (_useEventBus)
        {
            sb.Append("　金币：\n");
            sb.Append("　　EventBus.Instance.EmitSignal(\"ItemPickedUp\", id, count);\n");
            sb.Append("　　→ 金币**不认识**成就系统，也不需要认识\n");
        }
        else
        {
            sb.Append("　金币：\n");
            sb.Append("　　_DirectReceiver.OnItemCollected(id, count);\n");
            sb.Append("　　→ 金币**必须持有**接收方的引用，而且接收方得暴露一个公开方法\n");
        }

        sb.Append('\n');
        sb.Append($"【统计】金币 {_achievements.Coins}　击杀 {_achievements.Kills}　");
        sb.Append($"移动 {_achievements.Distance}px　共拾取 {_pickupsCollected} 个\n");
        sb.Append($"【成就 {_achievements.History.Count}/4】");
        if (_achievements.History.Count == 0) sb.Append("（还没有）");
        else foreach (var a in _achievements.History) sb.Append($"　{a.Title}");
        sb.Append('\n');

        sb.Append("【悬空连接演示】");
        sb.Append(_danglingDemoDone ? $"\n　捕获到：{_danglingError}\n" : "（按 2 试一次）\n");
        if (_danglingDemoDone)
            sb.Append("　→ 换成 Godot 信号就**不会**出错：引擎会在节点释放时自动断开。\n");

        sb.Append('\n');
        sb.Append("【按键】1 切换写法　2 制造悬空连接　J 打靶子　走动捡金币\n");
        sb.Append("【订阅者】成就系统 / 飘字提示 / 统计面板 —— 它们互相不认识，也都不认识金币");

        _panel.Text = sb.ToString();
    }
}
