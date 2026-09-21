using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// S02 · 输入与手感
///
/// 玩法：一条四段平台的水平跳跃路线，走到最右边的目标环即通关。
///      第三段到第四段之间有一个 190 像素的大缺口 —— 不用冲刺绝对过不去。
///
/// 这一站最特别的地方：三个"手感辅助"可以用 1/2/3 键随时关掉。
/// **关掉它们再走一遍，你才能真的明白每一行代码值多少。**
///
///   1 = 土狼时间（走出边缘后 0.1 秒内仍可起跳）
///   2 = 跳跃缓冲（落地前 0.12 秒内按下的跳跃会被记住）
///   3 = 可变跳跃高度（松开跳跃键立刻截断上升）
///
/// 五条练习任务的勾选，都绑定在"你真的做出了对应操作"上：
///   ① 缓冲救回过一次跳跃   ② 土狼时间救回过一次跳跃
///   ③ 既跳出过高跳也跳出过矮跳   ④ 用过三次冲刺   ⑤ 用过事件式输入（1/2/3 开关）
/// 换句话说：**这一站的进度条不是走个过场，它就是你的操作记录。**
/// </summary>
public partial class S02Input : StationBase
{
    public override string StationId => "s02_input";

    private static readonly Rect2 PlayArea = new(40, 130, 1200, 570);
    private static readonly Vector2 SpawnPoint = new(140, 606);
    private static readonly Vector2 GoalPoint = new(1135, 486);
    private const float GoalRadius = 54f;
    private const float FallY = 745f;

    private static readonly Vector2 BarOrigin = new(112, 146);
    private static readonly Vector2 BarSize = new(268, 11);
    private static readonly Vector2 BarSpacing = new(0, 28);

    private PlatformerPlayer _player = null!;
    private Label _assistLabel = null!;
    private Label _stateLabel = null!;
    private Polygon2D _goalRing = null!;
    private Polygon2D _coyoteBar = null!;
    private Polygon2D _bufferBar = null!;

    private int _assistToggleCount;
    private bool _goalReached;
    private double _pulse;

    protected override void StationReady()
    {
        _player = GetNode<PlatformerPlayer>("Player");

        // 平台跳跃站用 SidesOnly：只有左右两面墙，没有地板 —— 掉下去才是真的掉下去。
        LevelKit.CreateWalls(this, PlayArea, LevelKit.WallSides.SidesOnly);

        BuildGoalMarker();
        BuildGuides();
        BuildMeter();

        SetStatus("走到最右边的目标环。过不去的缺口，就是三个辅助在替你兜底的地方");
    }

    public override void _Process(double delta)
    {
        // 掉出画面 = 重生。这就是平台游戏里最朴素的"失败处理"。
        if (_player.GlobalPosition.Y > FallY)
        {
            _player.RespawnAt(SpawnPoint);
            Flash($"掉下去了（第 {_player.FallCount} 次）。提示：离开平台边缘后的 0.1 秒内还跳得起来", 2.4);
        }

        UpdateMeter();
        UpdateBars();
        UpdateGoalMarker(delta);
        RefreshTaskChecks();
        CheckGoal();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // 注意这里走的是「事件」路线，而玩家自己用的是 Input.IsActionJustPressed「轮询」路线。
        // 两种写法并存并不是笔误 —— 这正是第 5 条练习任务要你对比的东西：
        //   事件适合"点一下触发一件事"（切开关、开菜单），
        //   轮询适合"每个物理帧读一次状态再统一决策"（移动、跳跃、冲刺）。
        // 事件有个轮询没有的好处：你能拿到完整的 InputEvent，从而知道是键盘、鼠标还是手柄按的。
        if (@event.IsActionPressed("aux_1")) { ToggleAssist(1); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_2")) { ToggleAssist(2); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_3")) { ToggleAssist(3); GetViewport().SetInputAsHandled(); return; }

        base._UnhandledInput(@event);
    }

    // ---------- 搭场景 ----------

    private void BuildGoalMarker()
    {
        _goalRing = LevelKit.MakeCircle(this, GoalPoint, 36f, new Color(0.30f, 0.85f, 0.55f, 0.5f), 32, "GoalRing");
        LevelKit.MakeLabel(this, GoalPoint + new Vector2(0, 40), "目标", 15, new Color(0.6f, 0.95f, 0.72f));
    }

    private void BuildGuides()
    {
        LevelKit.MakeLabel(this, new Vector2(180, 556), "起点 · 助跑", 14, new Color(0.55f, 0.66f, 0.82f));
        LevelKit.MakeLabel(this, new Vector2(490, 516), "缺口 100px", 14, new Color(0.55f, 0.66f, 0.82f));
        LevelKit.MakeLabel(this, new Vector2(770, 476), "缺口 100px", 14, new Color(0.55f, 0.66f, 0.82f));
        LevelKit.MakeLabel(this, new Vector2(965, 430), "缺口 190px", 15, new Color(0.98f, 0.84f, 0.42f));
        LevelKit.MakeLabel(this, new Vector2(965, 452), "Shift 冲刺才过得去", 14, new Color(0.98f, 0.84f, 0.42f));
    }

    private void BuildMeter()
    {
        _assistLabel = LevelKit.MakeLabel(this, new Vector2(24, 64), "", 15,
            new Color(0.84f, 0.90f, 0.98f), HorizontalAlignment.Left, "AssistMeter");
        _assistLabel.Size = new Vector2(540, 90);

        // 进度条：顶点从原点开始画（不是居中），这样 Scale.x 就等于"还剩下多少比例"。
        // 用居中的多边形做进度条会很别扭 —— 它会从中间往两边缩。
        MakeBar(BarOrigin, new Color(0.16f, 0.20f, 0.26f), "CoyoteBarBg");
        _coyoteBar = MakeBar(BarOrigin, new Color(0.32f, 0.82f, 0.95f), "CoyoteBarFill");

        MakeBar(BarOrigin + BarSpacing, new Color(0.16f, 0.20f, 0.26f), "BufferBarBg");
        _bufferBar = MakeBar(BarOrigin + BarSpacing, new Color(0.98f, 0.86f, 0.35f), "BufferBarFill");

        _stateLabel = LevelKit.MakeLabel(this, new Vector2(24, 212), "", 15,
            new Color(0.72f, 0.80f, 0.92f), HorizontalAlignment.Left, "StateMeter");
        _stateLabel.Size = new Vector2(600, 90);
    }

    private Polygon2D MakeBar(Vector2 topLeft, Color color, string name)
    {
        var bar = new Polygon2D
        {
            Name = name,
            Color = color,
            Position = topLeft,
            Polygon = new[]
            {
                Vector2.Zero,
                new Vector2(BarSize.X, 0f),
                BarSize,
                new Vector2(0f, BarSize.Y),
            },
        };
        AddChild(bar);
        return bar;
    }

    // ---------- 每帧更新 ----------

    private void UpdateMeter()
    {
        var p = _player;

        _assistLabel.Text =
            $"土狼时间 Coyote Time        走路沿之后还允许起跳 {p.CoyoteTime:0.00}s     [{(p.CoyoteEnabled ? "开" : "关")}] 按 1\n" +
            $"跳跃缓冲 Jump Buffer        落地前 {p.JumpBufferTime:0.00}s 按的跳跃会记住   [{(p.JumpBufferEnabled ? "开" : "关")}] 按 2\n" +
            $"可变跳跃高度                松手就截断上升                    [{(p.VariableJumpEnabled ? "开" : "关")}] 按 3";

        _stateLabel.Text =
            $"土狼剩 {p.CoyoteTimer:0.000}s        缓冲剩 {p.JumpBufferTimer:0.000}s\n" +
            $"在地面 {(p.IsOnFloor() ? "是" : "否")}    冲刺 {p.DashCooldownTimer:0.00}s / 共 {p.DashCount} 次    " +
            $"上次跳跃高度 {p.LastJumpPeak:0}px ({FullJumpPeak:0}px = 按住, 约 {ShortJumpPeak:0}px = 轻点)    掉落 {p.FallCount} 次";
    }

    /// <summary>理论满跳高度，用来给玩家一个参照。v²/(2g)。</summary>
    private float FullJumpPeak => _player.JumpVelocity * _player.JumpVelocity / (2f * _player.Gravity);

    private float ShortJumpPeak => FullJumpPeak * _player.JumpCutMultiplier * _player.JumpCutMultiplier;

    private void UpdateBars()
    {
        var p = _player;

        _coyoteBar.Scale = new Vector2(p.CoyoteEnabled ? Mathf.Clamp(p.CoyoteTimer / p.CoyoteTime, 0f, 1f) : 0f, 1f);
        _bufferBar.Scale = new Vector2(p.JumpBufferEnabled ? Mathf.Clamp(p.JumpBufferTimer / p.JumpBufferTime, 0f, 1f) : 0f, 1f);
        _coyoteBar.Color = p.CoyoteEnabled ? new Color(0.32f, 0.82f, 0.95f) : new Color(0.30f, 0.33f, 0.38f);
        _bufferBar.Color = p.JumpBufferEnabled ? new Color(0.98f, 0.86f, 0.35f) : new Color(0.30f, 0.33f, 0.38f);
    }

    private void UpdateGoalMarker(double delta)
    {
        _pulse += delta;
        _goalRing.Scale = Vector2.One * (1f + 0.09f * Mathf.Sin((float)_pulse * 3f));
    }

    // ---------- 逻辑 ----------

    private void ToggleAssist(int index)
    {
        switch (index)
        {
            case 1:
                _player.CoyoteEnabled = !_player.CoyoteEnabled;
                Flash(_player.CoyoteEnabled ? "土狼时间：开" : "土狼时间：关 —— 现在走出边缘就再也跳不起来了");
                break;
            case 2:
                _player.JumpBufferEnabled = !_player.JumpBufferEnabled;
                Flash(_player.JumpBufferEnabled ? "跳跃缓冲：开" : "跳跃缓冲：关 —— 现在早按一点点都会被丢掉");
                break;
            case 3:
                _player.VariableJumpEnabled = !_player.VariableJumpEnabled;
                Flash(_player.VariableJumpEnabled ? "可变跳跃高度：开" : "可变跳跃高度：关 —— 现在只有一种跳跃高度了");
                break;
        }

        _assistToggleCount++;
        if (_assistToggleCount >= 3) MarkTaskDone(4);
    }

    private void CheckGoal()
    {
        if (_goalReached) return;
        if (_player.GlobalPosition.DistanceTo(GoalPoint) > GoalRadius) return;

        _goalReached = true;

        var allAssistsOff = !_player.CoyoteEnabled && !_player.JumpBufferEnabled && !_player.VariableJumpEnabled;
        Complete(allAssistsOff
            ? "而且是在三个辅助全关的情况下做到的 —— 你现在应该很清楚它们各自值多少了"
            : "再试一次：按 1/2/3 把三个辅助全关掉，看看还能不能过");
    }

    /// <summary>
    /// 把"玩家真的用上了某个机制"变成任务打勾。
    /// 放在 _Process 里轮询状态，因为这些都是随时可能达成的成就。
    /// </summary>
    private void RefreshTaskChecks()
    {
        if (_player.BufferSavedAJump) MarkTaskDone(0);
        if (_player.CoyoteSavedAJump) MarkTaskDone(1);
        if (_player.TallJumpDone && _player.ShortJumpDone) MarkTaskDone(2);
        if (_player.DashCount >= 3) MarkTaskDone(3);
    }
}
