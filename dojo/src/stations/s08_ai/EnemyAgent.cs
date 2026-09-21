using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// S08 的敌人 —— 把前面几站学到的东西全串起来：
///   · 状态机（S04）：巡逻 / 追击 / 搜索
///   · 视野锥 + 射线（S03/S04）：看得见才追
///   · 导航网格（本站在教）：绕开墙走
///   · 群体分离（本站在教）：多个敌人不叠在一起
///
/// ★ 五个"AI 能力"开关（面板上 1~5 可切换），每一个都对应一条**独立的**能力：
///     ① 视野锥   关掉 = 变成全向视野（360° 都能看见你）
///     ② 听觉     关掉 = 冲刺不再吸引敌人
///     ③ 寻路     关掉 = 直线朝你走（会一头撞在墙上）
///     ④ 分离力   关掉 = 5 个敌人叠成一坨
///     ⑤ 搜索     关掉 = 丢失目标后立刻回巡逻，不再搜索
///
///   关掉它们再玩一遍，你会立刻明白**每一条各自贡献了多少"像活物"的感觉**。
///   这也是本站唯一想让你记住的学习方法：**把能力拆成可单独关闭的开关。**
///
/// ★ 为什么朝向用二维向量而不是"朝左还是朝右"：
///   视野锥要判断"目标在不在我前方"，这天然是二维的。
///   用 float 表示朝向的话，上下移动时朝向就没法表达，锥形会一直横着。
///   （这个教训在 S04 里已经踩过一次了。）
/// </summary>
public partial class EnemyAgent : CharacterBody2D
{
    [Signal] public delegate void StateChangedEventHandler(string from, string to, string reason);

    // ---------- 五个能力开关（站台的面板会显示并切换它们） ----------
    public static bool VisionConeEnabled { get; set; } = true;
    public static bool HearingEnabled { get; set; } = true;
    public static bool PathfindingEnabled { get; set; } = true;
    public static bool SeparationEnabled { get; set; } = true;
    public static bool SearchEnabled { get; set; } = true;

    [Export] public float PatrolSpeed { get; set; } = 120f;
    [Export] public float ChaseSpeed { get; set; } = 152f;
    [Export] public float SearchSpeed { get; set; } = 116f;

    [Export] public float SightRange { get; set; } = 300f;
    [Export(PropertyHint.Range, "10,180,1")] public float VisionHalfAngleDeg { get; set; } = 60f;
    [Export] public float HearingRange { get; set; } = 420f;

    [Export] public float LoseTargetSeconds { get; set; } = 1.6f;
    [Export] public float SearchSeconds { get; set; } = 3.2f;

    /// <summary>
    /// 分离力的作用半径。两个敌人靠得比这近就会互相推开。
    /// 它决定了"包围圈"的最终大小：5 个敌人围成一圈时，相邻间距 ≈ 1.18 × 半径。
    /// 想要 70 像素的间距，半径就得放到 60 以上。
    /// </summary>
    [Export] public float SeparationRadius { get; set; } = 76f;

    /// <summary>
    /// 分离力的强度。**太大敌人会互相弹开、走不出直线；太小还是会叠。**
    /// 最初设 0.85，实测 5 个敌人追到位之后最小间距只有 23 像素 ——
    /// 因为"朝玩家走"的力是单位长度，而分离力最多只有 0.85，
    /// 结果还是被玩家那个点吸成一坨。调到 1.8 之后才真的摊开成包围圈。
    /// </summary>
    [Export] public float SeparationWeight { get; set; } = 1.8f;

    public Node2D? Target { get; set; }
    public Vector2[] Waypoints { get; set; } = Array.Empty<Vector2>();
    public int WaypointIndex { get; set; }

    public Vector2 FacingDir { get; private set; } = Vector2.Right;
    public Vector2 LastKnownTargetPosition { get; set; }
    public Vector2 NoisePosition { get; set; }
    public bool HasPendingNoise { get; set; }
    public double TimeSinceLastSeen { get; set; } = 999.0;

    public bool CanSeeTargetNow { get; private set; }
    public StateMachine Machine { get; private set; } = null!;
    public NavigationAgent2D Nav { get; private set; } = null!;

    /// <summary>搜索状态的倒计时。由状态自己写，角色统一读。</summary>
    public float SearchTimer { get; set; }

    /// <summary>
    /// 当前导航路径的点数。面板显示它，用来证明"它真的在绕路" ——
    /// 直线上没人挡的时候是 2（起点+终点），绕墙时会涨到 5~8。
    ///
    /// 注意它是**缓存值**，每 0.25 秒才算一次：
    /// `GetCurrentNavigationPath()` 每次调用都会分配一个新数组，
    /// 5 个敌人 × 每帧一次 = 每秒 300 次分配。面板上的数字不值得这个代价。
    /// **能算得便宜的东西，别让它变成每帧的开销。**
    /// </summary>
    public int PathPointCount { get; private set; }

    private float _pathSampleTimer;

    private RayCast2D _sight = null!;
    private Polygon2D _body = null!;
    private Polygon2D _visor = null!;
    private Polygon2D _cone = null!;

    private static readonly Color PatrolColor = new(0.42f, 0.6f, 0.86f);
    private static readonly Color ChaseColor = new(0.92f, 0.36f, 0.32f);
    private static readonly Color SearchColor = new(0.92f, 0.76f, 0.34f);

    public override void _Ready()
    {
        _sight = GetNode<RayCast2D>("Sight");
        _body = GetNode<Polygon2D>("Body");
        _visor = GetNode<Polygon2D>("Visor");
        _cone = GetNode<Polygon2D>("VisionCone");

        CollisionLayer = GameLayers.Enemy;
        CollisionMask = GameLayers.World;

        _sight.CollisionMask = GameLayers.World;
        _sight.Enabled = false;

        Nav = GetNode<NavigationAgent2D>("Nav");
        Nav.PathDesiredDistance = 6f;
        // 停在离目标 30 像素的地方，而不是贴到 14 —— 贴太近的话 5 个敌人会挤在一个点上
        Nav.TargetDesiredDistance = 30f;

        AddToGroup("enemies");

        // 感知先于状态机决策执行（同 S04）
        ProcessPriority = -10;

        BuildStateMachine();
        BuildVisionCone();
    }

    private void BuildStateMachine()
    {
        Machine = GetNode<StateMachine>("StateMachine");
        Machine.Actor = this;

        foreach (var child in Machine.GetChildren())
            if (child is State state)
                Machine.AddState(state);

        Machine.AddTransition("Patrol", "Chase", "看得见目标", () => CanSeeTargetNow);
        Machine.AddTransition("Patrol", "Search", "听到动静", () => HearingEnabled && HasPendingNoise);

        Machine.AddTransition("Chase", "Search", $"丢失目标超过 {LoseTargetSeconds:0.#} 秒", () => TimeSinceLastSeen > LoseTargetSeconds);

        Machine.AddTransition("Search", "Chase", "重新看到目标", () => CanSeeTargetNow);
        Machine.AddTransition("Search", "Patrol", "搜索时间到", () => SearchTimer <= 0 || !SearchEnabled);

        Machine.Start("Patrol");
        Machine.StateChanged += (from, to, reason) => EmitSignal(SignalName.StateChanged, from, to, reason);
    }

    public override void _Process(double delta)
    {
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

        // 视觉表现：身体颜色跟着状态走，视野锥跟着朝向走
        _body.Color = Machine.CurrentName switch
        {
            "Chase" => ChaseColor,
            "Search" => SearchColor,
            _ => PatrolColor,
        };
        _visor.Color = _body.Color.Lightened(0.5f);
        _cone.Visible = VisionConeEnabled;
        _cone.Scale = new Vector2(FacingDir.X >= 0f ? 1f : -1f, FacingDir.Y >= 0f ? 1f : -1f);
        _visor.Position = FacingDir * 11f;

        _pathSampleTimer += (float)delta;
        if (_pathSampleTimer < 0.25f) return;
        _pathSampleTimer = 0f;
        PathPointCount = Nav.IsNavigationFinished() ? 0 : Nav.GetCurrentNavigationPath().Length;
    }

    // ---------- 感知 ----------

    /// <summary>看得见目标吗？三个条件：在射程内、在视野锥内、视线没被墙挡住。</summary>
    public bool CanSeeTarget()
    {
        if (Target is null) return false;

        var toTarget = Target.GlobalPosition - GlobalPosition;
        if (toTarget.Length() > SightRange) return false;

        if (VisionConeEnabled)
        {
            var angleDeg = Mathf.RadToDeg(FacingDir.AngleTo(toTarget));
            if (Mathf.Abs(angleDeg) > VisionHalfAngleDeg) return false;
        }

        _sight.TargetPosition = toTarget;
        _sight.ForceRaycastUpdate();
        return !_sight.IsColliding();
    }

    /// <summary>听到动静。**超出听觉半径就完全没反应** —— 这是"声音会衰减"的最简形态。</summary>
    public void Hear(Vector2 position)
    {
        if (!HearingEnabled) return;
        if (GlobalPosition.DistanceTo(position) > HearingRange) return;

        NoisePosition = position;
        HasPendingNoise = true;
    }

    // ---------- 移动 ----------

    /// <summary>
    /// 朝目标点移动。寻路开启时走导航网格（绕墙），关闭时走直线（撞墙）。
    /// 返回"是否已经到达"。
    /// </summary>
    public bool MoveTo(Vector2 destination, float speed, double delta)
    {
        var dt = (float)delta;
        Vector2 direction;

        if (PathfindingEnabled)
        {
            Nav.TargetPosition = destination;
            if (Nav.IsNavigationFinished())
            {
                StopMoving();
                return GlobalPosition.DistanceTo(destination) < 24f;
            }
            direction = (Nav.GetNextPathPosition() - GlobalPosition).Normalized();
        }
        else
        {
            var offset = destination - GlobalPosition;
            if (offset.Length() < 14f)
            {
                StopMoving();
                return true;
            }
            direction = offset.Normalized();
        }

        // 叠上群体分离力。**注意是"叠在寻路方向之上"，不是替换它** ——
        // 替换的话敌人就会互相推开而完全不追你了。
        direction += ComputeSeparation();

        if (direction.LengthSquared() < 0.0001f) direction = Vector2.Right;
        direction = direction.Normalized();

        Velocity = direction * speed;
        FacingDir = direction;
        MoveAndSlide();

        return false;
    }

    /// <summary>
    /// 群体分离（Boids 里那条 "separation" 规则的最简形式）：
    /// 对每个靠得太近的同伴，产生一个**远离它**的力，距离越近力越大。
    ///
    /// 为什么必须有它：5 个敌人各自朝玩家走，如果没有任何相互排斥，
    /// 它们的路径会完全重合 —— 最后看起来像**一个**敌人。
    /// 有了分离力，它们会自然摊开成一个包围圈。
    /// </summary>
    private Vector2 ComputeSeparation()
    {
        if (!SeparationEnabled) return Vector2.Zero;

        var push = Vector2.Zero;
        foreach (var node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node == this || node is not Node2D other) continue;

            var offset = GlobalPosition - other.GlobalPosition;
            var distance = offset.Length();
            if (distance < 0.01f || distance > SeparationRadius) continue;

            // 距离越近推力越大（线性衰减）
            push += offset.Normalized() * (1f - distance / SeparationRadius);
        }

        return push * SeparationWeight;
    }

    public void StopMoving()
    {
        Velocity = Velocity.MoveToward(Vector2.Zero, 900f * (float)GetPhysicsProcessDeltaTime());
        MoveAndSlide();
    }

    /// <summary>把视野锥画出来。扇形用一串多边形顶点逼近。</summary>
    private void BuildVisionCone()
    {
        var half = Mathf.DegToRad(VisionHalfAngleDeg);
        var points = new List<Vector2> { Vector2.Zero };
        const int segments = 14;

        for (var i = 0; i <= segments; i++)
        {
            var angle = Mathf.Lerp(-half, half, (float)i / segments);
            points.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * SightRange);
        }

        _cone.Polygon = points.ToArray();
        _cone.Color = new Color(0.6f, 0.75f, 1f, 0.055f);
    }
}
