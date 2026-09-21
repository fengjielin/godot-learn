using System.Text;
using Godot;
using Dojo.Common;
using Dojo.Entities;

namespace Dojo.Stations;

/// <summary>
/// S04 · 状态机
///
/// 一个守卫，五个状态：**巡逻 → 警觉 → 追击 → 攻击**，外加一个能打断一切的**眩晕**。
/// 你可以走进它的视野、躲到柱子后面甩掉它、或者用石头砸晕它 / 把它引开。
///
/// 这一站最特别的地方是左上角那块面板 —— 它不是装饰，而是**把状态机本身画出来给你看**：
///
///   · 当前在哪个状态、待了多久、上一次是哪个
///   · **从当前状态出发的每一条转移**，以及它的条件此刻满不满足
///   · 哪些状态你已经见过
///   · 最近几次转移是什么、因为什么触发的
///
/// 为什么值得做这块面板：
///   状态机最容易出的 bug 不是"写错"，而是"**你根本不知道它现在在干嘛**"。
///   玩家说"这个怪有时候会卡住"，你盯着代码看十分钟也看不出所以然。
///   但只要把"当前状态 + 每条转移的满足情况"摊在屏幕上，问题往往一眼就能定位。
///   **可观测性不是附加功能，它是复杂系统能不能被调好的前提。**
/// </summary>
public partial class S04Fsm : StationBase
{
    public override string StationId => "s04_fsm";

    private static readonly Rect2 PlayArea = new(40, 130, 1200, 560);

    /// <summary>巡逻路线（顺时针绕一圈）。场景里那四个绿色菱形就是它们。</summary>
    private static readonly Vector2[] PatrolRoute =
    {
        new(380, 300),
        new(900, 300),
        new(900, 600),
        new(380, 600),
    };

    private static readonly string[] AllStates = { "Patrol", "Alert", "Chase", "Attack", "Stun" };

    private Player _player = null!;
    private Guard _guard = null!;
    private Node2D _thrownRoot = null!;
    private PackedScene _stoneScene = null!;
    private Label _panel = null!;
    private Line2D _aimLine = null!;

    private int _stonesThrown;
    private int _stonesLanded;
    private int _timesSpotted;
    private bool _lostTargetOnce;
    private bool _stunnedWhileAttacking;

    private readonly List<string> _recentTransitions = new();
    private readonly bool[] _taskDone = new bool[5];

    protected override void StationReady()
    {
        _player = GetNode<Player>("Player");
        _guard = GetNode<Guard>("Guard");
        _thrownRoot = GetNode<Node2D>("Thrown");

        _stoneScene = GD.Load<PackedScene>("res://src/stations/s04_fsm/stone.tscn")
                      ?? throw new InvalidOperationException("stone.tscn 加载失败");

        _guard.Target = _player;
        _guard.Waypoints = PatrolRoute;
        // 从"往下走"那段开始，这样守卫会先经过玩家所在的区域，几秒内就能触发第一次发现
        _guard.WaypointIndex = 2;
        _guard.StateChanged += OnGuardStateChanged;
        _guard.StrikeLanded += OnStrikeLanded;

        LevelKit.CreateBorderWalls(this, PlayArea);
        BuildLabels();
        BuildAimLine();

        SetStatus("走进视野触发「警觉→追击→攻击」；躲到柱子后甩掉它；按 J 扔石头砸晕或引开它");
    }

    public override void _Process(double delta)
    {
        UpdatePanel();
        UpdateAimLine();
        CheckGoals();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("attack"))
        {
            ThrowStone();
            GetViewport().SetInputAsHandled();
            return;
        }

        base._UnhandledInput(@event);
    }

    // ---------- 玩家的两个动作 ----------

    private void ThrowStone()
    {
        var stone = _stoneScene.Instantiate<Stone>();
        var direction = _player.AimDirection;

        stone.Direction = direction;
        stone.Position = _player.Position + direction * 26f;
        stone.Landed += OnStoneLanded;
        stone.HitGuard += OnStoneHitGuard;

        _thrownRoot.AddChild(stone);
        _stonesThrown++;
    }

    private void OnStoneLanded(Vector2 position)
    {
        _stonesLanded++;

        var distance = _guard.GlobalPosition.DistanceTo(position);
        var heard = distance <= _guard.HearingRange;

        // 制造动静：在听觉半径内，守卫会转去查看
        _guard.Hear(position);

        Flash(heard
            ? $"石头落地（{distance:0}px）—— 守卫听到了，正赶过去查看"
            : $"石头落地（{distance:0}px）—— 超出听觉范围 {_guard.HearingRange:0}px，守卫没反应", 1.8);
    }

    private void OnStoneHitGuard()
        => Flash("砸中了！守卫进入【眩晕】状态 —— 注意转移表里它是怎么打断其它状态的", 2.2);

    private void OnStrikeLanded(bool hit)
    {
        if (hit)
        {
            ScreenFx.Instance.Shake(0.75f);
            ScreenFx.Instance.Hitstop(0.06f);
            ScreenFx.Instance.Flash(new Color(1f, 0.45f, 0.4f), 0.42f, 0.18f);
            Flash("被守卫打中了！它现在进入【后摇】—— 这就是你的反击窗口", 2.0);
        }
        else
        {
            ScreenFx.Instance.Shake(0.3f);
            ScreenFx.Instance.Flash(new Color(1f, 0.88f, 0.5f), 0.2f, 0.14f);
            Flash("守卫挥空了 —— 你躲过了它的前摇（那 0.55 秒就是留给你反应的）", 2.0);
        }
    }

    // ---------- 状态转移的观察 ----------

    private void OnGuardStateChanged(string from, string to, string reason)
    {
        _recentTransitions.Insert(0, $"{from}→{to}");

        while (_recentTransitions.Count > 4)
            _recentTransitions.RemoveAt(_recentTransitions.Count - 1);

        // 练习任务 ①：完整走完 巡逻→警觉→追击→攻击
        // （用 Machine.HasVisited("Attack") 判定，见 CheckGoals）

        // 练习任务 ②：靠掩体甩掉追击，触发 Chase → Alert
        if (from == "Chase" && to == "Alert") _lostTargetOnce = true;

        // 练习任务 ④：在攻击前摇里把守卫砸晕 —— 这是"优先级"最直观的演示
        if (from == "Attack" && to == "Stun") _stunnedWhileAttacking = true;

        if (to == "Chase" && (from == "Patrol" || from == "Alert")) _timesSpotted++;

        _ = reason; // 转移原因已经写在转移表里了，面板会显示当前状态的那一份
    }

    // ---------- 目标判定 ----------

    private void CheckGoals()
    {
        if (_guard.Machine.HasVisited("Attack")) MarkOnce(0);
        if (_guard.StunCount >= 1) MarkOnce(1);
        if (_lostTargetOnce) MarkOnce(2);
        if (Array.TrueForAll(AllStates, s => _guard.Machine.HasVisited(s))) MarkOnce(3);
        if (_stunnedWhileAttacking) MarkOnce(4);

        if (!Array.TrueForAll(_taskDone, done => done)) return;

        Complete("五个状态你全都见过，而且两次特殊的优先级切换（丢失目标 / 攻击被打断）也做到了");
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
        _panel = LevelKit.MakeLabel(this, new Vector2(24, 60), "", 14,
            new Color(0.86f, 0.92f, 0.98f), HorizontalAlignment.Left, "FsmPanel");
        _panel.Size = new Vector2(760, 340);

        // 世界标注的位置都是避让出来的：
        // 左上角被状态机面板占到 y≈320 为止，右上角被通用任务面板占着，
        // 底部 y>672 被状态行占着 —— 所以标注只能放在这几块之外的缝隙里。
        LevelKit.MakeLabel(this, new Vector2(900, 645), "绿色菱形 = 巡逻路点（守卫会绕着走）", 13,
            new Color(0.5f, 0.8f, 0.6f));
        LevelKit.MakeLabel(this, new Vector2(640, 558), "柱子可以挡视线 —— 躲到后面就看不见你了", 13,
            new Color(0.62f, 0.7f, 0.84f));
    }

    private void BuildAimLine()
    {
        _aimLine = new Line2D
        {
            Name = "AimLine",
            Width = 2.5f,
            DefaultColor = new Color(0.95f, 0.88f, 0.62f, 0.75f),
            Antialiased = true,
        };
        AddChild(_aimLine);
    }

    private void UpdateAimLine()
    {
        var dir = _player.AimDirection;
        _aimLine.Points = new[]
        {
            _player.Position + dir * 20f,
            _player.Position + dir * 66f,
        };
    }

    private void UpdatePanel()
    {
        var machine = _guard.Machine;
        var sb = new StringBuilder();

        sb.Append($"【状态机】当前 ");
        sb.Append(machine.CurrentName);
        sb.Append($"　持续 {machine.TimeInState:0.00}s　上一次 {machine.PreviousName}　累计切换 {machine.ChangeCount} 次\n\n");

        sb.Append($"从「{machine.CurrentName}」出发的转移（自上而下，第一条满足的立刻生效 —— 顺序就是优先级）：\n");

        var outgoing = machine.TransitionsFrom(machine.CurrentName);
        if (outgoing.Count == 0)
            sb.Append("   （没有出路 —— 这个状态会永远持续下去）\n");
        else
            foreach (var t in outgoing)
                sb.Append($"   {(t.IsSatisfied ? "▶ 满足　" : "· 不满足")}　{t.Label}　→　{t.To}\n");

        sb.Append('\n');
        sb.Append("已见过：");
        foreach (var name in AllStates)
            sb.Append($"{name}{(machine.HasVisited(name) ? "✓" : "✗")}  ");

        sb.Append("\n最近转移：");
        sb.Append(_recentTransitions.Count == 0 ? "（还没有）" : string.Join("　", _recentTransitions));

        sb.Append($"\n石头 扔{_stonesThrown}/落地{_stonesLanded}　被看见 {_timesSpotted} 次　被砸晕 {_guard.StunCount} 次");

        _panel.Text = sb.ToString();
    }
}
