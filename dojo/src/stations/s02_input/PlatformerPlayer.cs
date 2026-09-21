using Godot;

namespace Dojo.Stations;

/// <summary>
/// S02 用的平台跳跃角色。这是本站的主角 —— 它把「手感」拆成了几个可以单独开关的零件。
///
/// 三个辅助功能（可以用 1/2/3 键分别关掉，直观感受它们各自的贡献）：
///
///   ① 土狼时间（Coyote Time）
///      走出平台边缘之后，还允许你在 0.1 秒内起跳。
///      解决的问题：玩家"明明按了跳"，但因为在边缘多走了两像素而掉下去 —— 这不是玩家菜，是设计欠账。
///
///   ② 跳跃缓冲（Jump Buffer）
///      在落地前 0.12 秒内按下的跳跃会被记住，落地瞬间自动执行。
///      解决的问题：玩家在快要落地时连按跳跃想"落地即跳"，如果没有缓冲，这一下会被丢掉。
///
///   ③ 可变跳跃高度（Variable Jump Height）
///      松开跳跃键就立刻截断上升速度。轻点 = 小跳，按住 = 大跳。
///      解决的问题：只有一个固定跳跃高度时，近距离的精确落脚会非常难受。
///
/// 学习要点：
///   1. 手感 = 给玩家的失误留容错窗口。这三个功能全都在"多给一点点"，却决定了游戏是"跟手"还是"发飘"。
///   2. 计时器就是「一个 float 字段 + 每帧减 delta + 用之前判 > 0」。没有魔法。
///   3. 位移一律写在 _PhysicsProcess 里。见 s01 的说明。
///   4. 判断"刚刚按下"用 Input.IsActionJustPressed（轮询）；
///      而站台那边切换辅助开关用的是 _UnhandledInput（事件）。两种写法的区别是本站的第 5 条任务。
/// </summary>
public partial class PlatformerPlayer : CharacterBody2D
{
    // ---------- 移动参数：建议在编辑器里直接拖滑块感受数值对操作的影响 ----------

    [Export] public float MaxSpeed { get; set; } = 270f;
    [Export] public float GroundAcceleration { get; set; } = 2600f;
    [Export] public float GroundFriction { get; set; } = 3400f;
    [Export] public float AirAcceleration { get; set; } = 1700f;

    // ---------- 重力与跳跃 ----------

    [Export] public float Gravity { get; set; } = 1750f;
    [Export] public float MaxFallSpeed { get; set; } = 950f;
    [Export] public float JumpVelocity { get; set; } = -565f;

    /// <summary>松开跳跃键时，上升速度乘以这个系数。越小 = 松手后掉得越快 = 跳得越矮。</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float JumpCutMultiplier { get; set; } = 0.42f;

    /// <summary>土狼时间窗口（秒）。</summary>
    [Export] public float CoyoteTime { get; set; } = 0.10f;

    /// <summary>跳跃缓冲窗口（秒）。</summary>
    [Export] public float JumpBufferTime { get; set; } = 0.12f;

    // ---------- 冲刺 ----------

    [Export] public float DashSpeed { get; set; } = 760f;
    [Export] public float DashDuration { get; set; } = 0.16f;
    [Export] public float DashCooldown { get; set; } = 0.5f;

    // ---------- 三个辅助开关：由 S02Input 站用 1/2/3 键切换 ----------

    public bool CoyoteEnabled { get; set; } = true;
    public bool JumpBufferEnabled { get; set; } = true;
    public bool VariableJumpEnabled { get; set; } = true;

    // ---------- 给 HUD 读的实时状态 ----------

    public float CoyoteTimer { get; private set; }
    public float JumpBufferTimer { get; private set; }
    public float DashCooldownTimer { get; private set; }
    public float DashTimer { get; private set; }
    public bool IsDashing => DashTimer > 0f;
    public int DashCount { get; private set; }
    public int FallCount { get; private set; }
    public float Facing { get; private set; } = 1f;

    // ---------- 「你真的用上了这个机制」的检测 ----------
    // 站台靠这几个标志来给你的练习任务打勾。它们不是装饰：
    // 每一个都代表"如果关掉这个辅助，这次操作本来会失败"。
    // 把学习目标变成可检测的游戏事件，本身就是个值得学的技巧。

    /// <summary>跳跃缓冲救回了一次跳跃（按下时还跳不了，落地瞬间自动兑现）。</summary>
    public bool BufferSavedAJump { get; private set; }

    /// <summary>土狼时间救回了一次跳跃（已经离开地面才按下的）。</summary>
    public bool CoyoteSavedAJump { get; private set; }

    /// <summary>上一次跳跃的最大高度（像素），用来直观对比"轻点"和"按住"。</summary>
    public float LastJumpPeak { get; private set; }

    /// <summary>跳出过一次接近满高度的跳跃。</summary>
    public bool TallJumpDone { get; private set; }

    /// <summary>跳出过一次矮跳（提前松手截断过上升速度）。</summary>
    public bool ShortJumpDone { get; private set; }

    private bool _jumpRequestedWhileUnable;
    private float _jumpOriginY;
    private float _jumpPeak;
    private bool _trackingJump;

    private Polygon2D _body = null!;
    private Polygon2D _eye = null!;
    private readonly Color _normalColor = new(0.35f, 0.83f, 0.95f);
    private readonly Color _dashColor = new(0.98f, 0.86f, 0.35f);
    private Vector2 _dashDirection = Vector2.Right;

    public override void _Ready()
    {
        _body = GetNode<Polygon2D>("Body");
        _eye = GetNode<Polygon2D>("Eye");
        AddToGroup("player");
    }

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;

        TickTimers(dt);

        // Input.GetAxis 会把"左/右"两个动作合成一个 -1~1 的值，
        // 比自己做 if (left) x -= 1; if (right) x += 1; 更不容易写错（尤其是手柄）。
        var axis = Input.GetAxis("move_left", "move_right");
        if (axis != 0f) Facing = Mathf.Sign(axis);

        // 这里用「轮询」而不是事件：把三个动作的当前状态读出来，
        // 在物理帧里统一处理。好处是时序确定，不会因为一帧内收到多个事件而乱序。
        if (Input.IsActionJustPressed("jump")) RequestJump();
        if (Input.IsActionJustPressed("dash")) TryStartDash(axis);

        if (DashTimer > 0f)
        {
            // 冲刺期间：无视重力和输入，沿固定方向直线冲出去。
            // 「冲刺时锁死方向」是刻意的 —— 可转向的冲刺会让距离变得不可预期，关卡就没法设计了。
            Velocity = _dashDirection * DashSpeed;
        }
        else
        {
            ApplyHorizontal(dt, axis);
            ApplyGravity(dt);
            ConsumeBufferedJump();
            ApplyJumpCut();
        }

        MoveAndSlide();
        TrackJumpPeak();
        UpdateVisual();
    }

    /// <summary>重生到指定位置，并清空所有速度与计时器。</summary>
    public void RespawnAt(Vector2 position)
    {
        GlobalPosition = position;
        Velocity = Vector2.Zero;
        DashTimer = 0f;
        JumpBufferTimer = 0f;
        CoyoteTimer = 0f;
        FallCount++;
    }

    // ---------- 内部实现 ----------

    private void TickTimers(float dt)
    {
        // 土狼时间：站在地上时一直是满的；一旦离地就开始倒数。
        CoyoteTimer = IsOnFloor() ? CoyoteTime : Mathf.Max(0f, CoyoteTimer - dt);

        var previousBuffer = JumpBufferTimer;
        JumpBufferTimer = Mathf.Max(0f, JumpBufferTimer - dt);
        // 缓冲过期了，那个"没兑现的请求"就作废
        if (previousBuffer > 0f && JumpBufferTimer <= 0f) _jumpRequestedWhileUnable = false;

        DashCooldownTimer = Mathf.Max(0f, DashCooldownTimer - dt);
        if (DashTimer > 0f) DashTimer = Mathf.Max(0f, DashTimer - dt);
    }

    /// <summary>现在能不能起跳。辅助关掉时，"土狼时间"不存在，只有真正站在地上才能跳。</summary>
    private bool CanJump() => CoyoteEnabled ? CoyoteTimer > 0f : IsOnFloor();

    private void RequestJump()
    {
        if (!CanJump()) _jumpRequestedWhileUnable = true;

        if (!JumpBufferEnabled)
        {
            // 关掉缓冲 = 只认"这一帧真的能跳"。早按一点点都会被丢掉，这就是没有缓冲的手感。
            if (CanJump()) DoJump();
            return;
        }

        // 开启缓冲 = 先记下来，等能跳的时候自动兑现（见 ConsumeBufferedJump）。
        JumpBufferTimer = JumpBufferTime;
    }

    private void ConsumeBufferedJump()
    {
        if (JumpBufferTimer <= 0f || !CanJump()) return;

        DoJump();
        JumpBufferTimer = 0f;

        // 这一跳是"缓冲赚来的"：按下的那一刻其实跳不了
        if (_jumpRequestedWhileUnable)
        {
            BufferSavedAJump = true;
            _jumpRequestedWhileUnable = false;
        }
    }

    private void DoJump()
    {
        // 只改 Y 分量。水平速度保留，这样"跑动中起跳"才能把速度带过去。
        Velocity = new Vector2(Velocity.X, JumpVelocity);
        CoyoteTimer = 0f;

        // 跳过时人还悬在空中 —— 说明是土狼时间救的这一下
        if (!IsOnFloor()) CoyoteSavedAJump = true;

        _jumpOriginY = GlobalPosition.Y;
        _jumpPeak = 0f;
        _trackingJump = true;
    }

    /// <summary>记录每次跳跃的实际高度，用来区分"轻点"和"按住"。</summary>
    private void TrackJumpPeak()
    {
        if (!_trackingJump) return;

        _jumpPeak = Mathf.Max(_jumpPeak, _jumpOriginY - GlobalPosition.Y);

        // 落地了（要求已经真的离地过，否则起跳那一帧会被误判成落地）
        if (!IsOnFloor() || _jumpPeak <= 6f) return;

        _trackingJump = false;
        LastJumpPeak = _jumpPeak;

        if (_jumpPeak >= 78f) TallJumpDone = true;       // 接近满高度
        if (_jumpPeak is > 8f and < 52f) ShortJumpDone = true; // 提前松手截断过
    }

    private void ApplyJumpCut()
    {
        if (!VariableJumpEnabled) return;
        if (!Input.IsActionJustReleased("jump")) return;
        if (Velocity.Y >= 0f) return; // 已经在下降了，不用管

        Velocity = new Vector2(Velocity.X, Velocity.Y * JumpCutMultiplier);
    }

    private void TryStartDash(float axis)
    {
        if (DashTimer > 0f || DashCooldownTimer > 0f) return;

        var dir = axis != 0f ? Mathf.Sign(axis) : Facing;
        _dashDirection = new Vector2(dir, 0f);
        DashTimer = DashDuration;
        DashCooldownTimer = DashCooldown;
        DashCount++;
    }

    private void ApplyHorizontal(float dt, float axis)
    {
        var target = axis * MaxSpeed;

        float rate;
        if (axis == 0f) rate = GroundFriction;              // 松手：用摩擦力刹住
        else if (IsOnFloor()) rate = GroundAcceleration;    // 地面加速：很跟手
        else rate = AirAcceleration;                        // 空中加速：慢一点，保留惯性感

        Velocity = new Vector2(
            Mathf.MoveToward(Velocity.X, target, rate * dt),
            Velocity.Y);
    }

    private void ApplyGravity(float dt)
    {
        Velocity = new Vector2(Velocity.X, Mathf.Min(Velocity.Y + Gravity * dt, MaxFallSpeed));
    }

    private void UpdateVisual()
    {
        _body.Color = IsDashing ? _dashColor : _normalColor;
        // 冲刺时压扁一点，视觉上强调"冲出去了"
        _body.Scale = IsDashing ? new Vector2(1.22f, 0.8f) : Vector2.One;
        // 眼睛偏向朝向那一侧，让"面朝哪边"一眼可见（多边形本身是对称的，看不出来）
        _eye.Position = new Vector2(5f * Facing, -5f);
    }
}
