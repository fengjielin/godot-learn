using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// S04 的守卫 —— 本站的主角。
///
/// 它的结构是"**角色持有世界模型，状态只做决策**"：
///   · Guard 负责**感知**（看得见吗 / 听到动静了吗）和**所有计时器**
///   · 各个 State 只负责"在这个状态下，我该干什么"
///
/// 为什么这样分：
///   如果让每个状态各自去算"我看得见玩家吗"，你会写五遍同样的射线检测，
///   而且五个状态对"看见"的定义迟早会不一致（一个算了视野锥、另一个忘了）。
///   **把"事实"集中在一处，"决策"分散到各状态** —— 这是写 AI 最省心的分工。
///
/// 另一个细节：本节点的 `ProcessPriority` 设成了 -10。
/// Godot 的 `_Process` 是按优先级从低到高调用的，所以守卫的感知会**先于**
/// 状态机的转移判断执行，保证"这一帧看到的"和"这一帧据此做的决定"是同一份数据。
/// 不设的话顺序不确定，AI 会偶尔慢一拍 —— 这种 bug 极难复现。
/// </summary>
public partial class Guard : CharacterBody2D
{
    /// <summary>攻击出手时发出。参数：是否命中。站台用它来播震屏、顿帧、飘字。</summary>
    [Signal] public delegate void StrikeLandedEventHandler(bool hit);

    /// <summary>状态切换时转发自状态机，方便站台只订阅一个对象。</summary>
    [Signal] public delegate void StateChangedEventHandler(string from, string to, string reason);

    public enum Mood
    {
        Patrol,
        Alert,
        Chase,
        Attack,
        Stun,
    }

    // ---------- 可调参数（在检查器里改，改完立刻能在游戏里感受到） ----------

    [Export] public float PatrolSpeed { get; set; } = 88f;
    [Export] public float AlertSpeed { get; set; } = 118f;
    [Export] public float ChaseSpeed { get; set; } = 168f;

    /// <summary>视野距离。</summary>
    [Export] public float SightRange { get; set; } = 280f;

    /// <summary>视野锥的半角（度）。180 就是"全向视野"。</summary>
    [Export(PropertyHint.Range, "10,180,1")] public float VisionHalfAngleDeg { get; set; } = 72f;

    /// <summary>听觉半径。石头落地的声音在这个范围内会被听到。</summary>
    [Export] public float HearingRange { get; set; } = 360f;

    [Export] public float AttackRange { get; set; } = 78f;

    /// <summary>攻击前摇。**有前摇玩家才有反应时间**，没有前摇的攻击等于耍赖。</summary>
    [Export] public float AttackWindup { get; set; } = 0.55f;

    /// <summary>攻击后摇（硬直）。</summary>
    [Export] public float AttackRecover { get; set; } = 0.75f;

    [Export] public float StunDuration { get; set; } = 2.0f;

    /// <summary>丢失目标多久之后放弃追击。</summary>
    [Export] public float LoseTargetSeconds { get; set; } = 1.2f;

    /// <summary>警觉状态持续多久。</summary>
    [Export] public float AlertSeconds { get; set; } = 1.8f;

    // ---------- 世界模型（感知结果 + 计时器） ----------

    public Node2D? Target { get; set; }
    public Vector2[] Waypoints { get; set; } = Array.Empty<Vector2>();
    public int WaypointIndex { get; set; }

    public Vector2 LastKnownTargetPosition { get; set; }
    public Vector2 NoisePosition { get; set; }
    public bool HasPendingNoise { get; set; }

    public double TimeSinceLastSeen { get; set; } = 999.0;
    public double AlertTimer { get; set; }
    public double StunTimer { get; set; }
    public double AttackTimer { get; set; }
    public double AttackCooldown { get; set; }

    public bool CanSeeTargetNow { get; private set; }

    /// <summary>
    /// 朝向（二维向量）。
    /// 刻意**不用一个 float 表示"朝左还是朝右"** —— 那样在上下移动时朝向就没法表达，
    /// 视野锥会一直横着，守卫往上下走的时候看起来像在"侧着眼睛看路"。
    /// 俯视角游戏里朝向天然是二维的，一开始就用 Vector2 能省掉后面所有的补丁。
    /// </summary>
    public Vector2 FacingDir { get; private set; } = Vector2.Right;

    /// <summary>进过多少次攻击状态（站台用它判定"你读到攻击那一步了"）。</summary>
    public int AttackCount { get; private set; }

    /// <summary>被砸晕过几次。</summary>
    public int StunCount { get; private set; }

    /// <summary>挥空了几次（攻击时玩家已经跑开）。</summary>
    public int MissCount { get; private set; }

    public StateMachine Machine { get; private set; } = null!;

    private RayCast2D _sight = null!;
    private Line2D _sightLine = null!;
    private Polygon2D _body = null!;
    private Polygon2D _visor = null!;
    private Polygon2D _telegraph = null!;
    private Label _marker = null!;

    // 巡逻/警戒/追击/攻击/眩晕 各自的配色
    private static readonly Color PatrolBody = new(0.34f, 0.72f, 0.45f);
    private static readonly Color AlertBody = new(0.90f, 0.80f, 0.34f);
    private static readonly Color ChaseBody = new(0.92f, 0.56f, 0.26f);
    private static readonly Color AttackBody = new(0.94f, 0.30f, 0.30f);
    private static readonly Color StunBody = new(0.72f, 0.46f, 0.92f);

    public override void _Ready()
    {
        _sight = GetNode<RayCast2D>("Sight");
        _sightLine = GetNode<Line2D>("SightLine");
        _body = GetNode<Polygon2D>("Body");
        _visor = GetNode<Polygon2D>("Visor");
        _telegraph = GetNode<Polygon2D>("Telegraph");
        _marker = GetNode<Label>("Marker");

        CollisionLayer = GameLayers.Enemy;
        CollisionMask = GameLayers.World;

        // 视线只被"世界"（墙、柱子）挡住。玩家自己不会挡自己的视线。
        _sight.CollisionMask = GameLayers.World;
        _sight.Enabled = false;

        _telegraph.Visible = false;
        _marker.Text = "";

        // 见类注释：让感知先于状态机的转移判断执行
        ProcessPriority = -10;

        BuildStateMachine();
    }

    /// <summary>
    /// ★ 状态转移表 —— 整个 AI 的行为规则全在这一段里。
    /// 本站左上角的面板会把它实时画出来（当前状态下每条出路、条件满足没），
    /// 所以你不用靠读代码来理解 AI。**这就是"把条件抽成表"最大的收益。**
    /// </summary>
    private void BuildStateMachine()
    {
        Machine = GetNode<StateMachine>("StateMachine");
        Machine.Actor = this;

        foreach (var child in Machine.GetChildren())
            if (child is State state)
                Machine.AddState(state);

        // ---- 从「巡逻」出发 ----
        Machine.AddTransition("Patrol", "Alert", "看到目标 或 听到动静", () => CanSeeTargetNow || HasPendingNoise);
        Machine.AddTransition("Patrol", "Stun", "被石头砸中", () => StunTimer > 0);

        // ---- 从「警觉」出发 ----
        Machine.AddTransition("Alert", "Chase", "确认看到目标", () => CanSeeTargetNow);
        Machine.AddTransition("Alert", "Stun", "被石头砸中", () => StunTimer > 0);
        Machine.AddTransition("Alert", "Patrol", "警戒时间到，什么也没有", () => AlertTimer <= 0);

        // ---- 从「追击」出发 ----
        Machine.AddTransition("Chase", "Attack", "进入攻击距离", () => CanSeeTargetNow && DistanceToTarget() <= AttackRange && AttackCooldown <= 0);
        Machine.AddTransition("Chase", "Stun", "被石头砸中", () => StunTimer > 0);
        Machine.AddTransition("Chase", "Alert", $"丢失目标超过 {LoseTargetSeconds:0.#} 秒", () => TimeSinceLastSeen > LoseTargetSeconds);

        // ---- 从「攻击」出发 ----
        Machine.AddTransition("Attack", "Stun", "★ 攻击被打断（前摇时被砸）", () => StunTimer > 0);
        Machine.AddTransition("Attack", "Chase", "攻击后摇结束", () => AttackTimer <= 0);

        // ---- 从「眩晕」出发 ----
        Machine.AddTransition("Stun", "Alert", "苏醒，进入警戒", () => StunTimer <= 0);

        Machine.Start("Patrol");

        // 把状态机的信号转出来，站台就只需要认识 Guard 一个对象
        Machine.StateChanged += (from, to, reason) => EmitSignal(SignalName.StateChanged, from, to, reason);
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;

        // 所有计时器都归角色管，状态只读不写 —— 避免"两个地方各减一次"
        AlertTimer = Mathf.Max(0f, (float)AlertTimer - dt);
        StunTimer = Mathf.Max(0f, (float)StunTimer - dt);
        AttackTimer = Mathf.Max(0f, (float)AttackTimer - dt);
        AttackCooldown = Mathf.Max(0f, (float)AttackCooldown - dt);

        // 感知
        CanSeeTargetNow = CanSeeTarget();
        if (CanSeeTargetNow && Target is not null)
        {
            LastKnownTargetPosition = Target.GlobalPosition;
            TimeSinceLastSeen = 0;
        }
        else
        {
            TimeSinceLastSeen += delta;
        }

        UpdateSightLine();
    }

    /// <summary>
    /// 看得见的时候画一条线到目标。
    /// 这条线是**给玩家看的**：没有它，"我到底有没有被发现"完全靠猜，
    /// 潜行玩法就成了盲盒。**任何"玩家无法从画面判断"的机制都是设计缺陷。**
    /// </summary>
    private void UpdateSightLine()
    {
        if (!CanSeeTargetNow || Target is null)
        {
            _sightLine.Visible = false;
            return;
        }

        _sightLine.Visible = true;
        _sightLine.Points = new[] { Vector2.Zero, Target.GlobalPosition - GlobalPosition };
        _sightLine.DefaultColor = Machine.CurrentName == "Chase" || Machine.CurrentName == "Attack"
            ? new Color(1f, 0.32f, 0.28f, 0.75f)
            : new Color(1f, 0.85f, 0.4f, 0.55f);
    }

    // ---------- 感知 ----------

    /// <summary>看得见目标吗？三个条件缺一不可：在射程内、在视野锥内、视线没被挡住。</summary>
    public bool CanSeeTarget()
    {
        if (Target is null) return false;

        var toTarget = Target.GlobalPosition - GlobalPosition;
        var distance = toTarget.Length();
        if (distance > SightRange) return false;

        // 视野锥：把"朝向"和"目标方向"的夹角和半角比一下
        var angleDeg = Mathf.RadToDeg(FacingDir.AngleTo(toTarget));
        if (Mathf.Abs(angleDeg) > VisionHalfAngleDeg) return false;

        // 视线：射线只被 World 层挡住，所以躲到柱子后面就看不见了
        _sight.TargetPosition = toTarget;
        _sight.ForceRaycastUpdate();
        return !_sight.IsColliding();
    }

    /// <summary>听到动静。石头落地、或者玩家跑动时都可以调它。</summary>
    public void Hear(Vector2 position)
    {
        if (GlobalPosition.DistanceTo(position) > HearingRange) return;

        NoisePosition = position;
        HasPendingNoise = true;
    }

    /// <summary>被石头砸中。</summary>
    public void Stun()
    {
        StunTimer = StunDuration;
        StunCount++;
        Velocity = Vector2.Zero;
    }

    public float DistanceToTarget()
        => Target is null ? 9999f : GlobalPosition.DistanceTo(Target.GlobalPosition);

    // ---------- 移动与朝向 ----------

    /// <summary>朝某个点走一步。返回"是否已经到达"。</summary>
    public bool MoveToward(Vector2 point, float speed, double delta)
    {
        var offset = point - GlobalPosition;

        if (offset.Length() < 8f)
        {
            Velocity = Velocity.MoveToward(Vector2.Zero, speed * 8f * (float)delta);
            MoveAndSlide();
            return true;
        }

        Velocity = offset.Normalized() * speed;
        FaceToward(point);
        MoveAndSlide();
        return false;
    }

    public void StopMoving()
    {
        Velocity = Vector2.Zero;
        MoveAndSlide();
    }

    public void FaceToward(Vector2 point)
    {
        var offset = point - GlobalPosition;
        if (offset.LengthSquared() <= 16f) return;

        FacingDir = offset.Normalized();

        // 朝向可见：面罩偏向哪边，视野锥就朝哪边。
        // 注意这里**没有**去翻转整个节点的 Scale ——
        // 给 PhysicsBody2D 设负缩放会让碰撞形状计算变得不可靠（Godot 也会警告）。
        // 想翻转外观，就单独翻外观节点，别碰物理体本身。
        _visor.Position = FacingDir * 12f;
    }

    // ---------- 表现 ----------

    public void SetMood(Mood mood)
    {
        _body.Color = mood switch
        {
            Mood.Patrol => PatrolBody,
            Mood.Alert => AlertBody,
            Mood.Chase => ChaseBody,
            Mood.Attack => AttackBody,
            _ => StunBody,
        };
        _visor.Color = _body.Color.Lightened(0.45f);
    }

    public string MarkerText
    {
        get => _marker.Text;
        set => _marker.Text = value;
    }

    /// <summary>攻击前摇的预警圈。progress 从 0 涨到 1。</summary>
    public void ShowTelegraph(float progress)
    {
        _telegraph.Visible = true;
        var scale = Mathf.Lerp(0.55f, 1.25f, Mathf.Clamp(progress, 0f, 1f));
        _telegraph.Scale = new Vector2(scale, scale);
        _telegraph.Color = new Color(1f, 0.35f, 0.3f, 0.16f + 0.3f * progress);
    }

    public void HideTelegraph() => _telegraph.Visible = false;

    /// <summary>攻击落地。玩家还在范围内就算命中。</summary>
    public bool DoStrike()
    {
        AttackCount++;

        var hit = CanSeeTargetNow && DistanceToTarget() <= AttackRange * 1.15f;
        if (!hit) MissCount++;

        return hit;
    }

    /// <summary>眩晕时转圈的表现。</summary>
    public void SpinVisual(double time)
    {
        Rotation = (float)Mathf.Sin(time * 7.0) * 0.28f;
    }

    public void ResetVisualRotation() => Rotation = 0f;
}
