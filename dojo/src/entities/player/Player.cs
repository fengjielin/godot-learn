using Godot;

namespace Dojo.Entities;

/// <summary>
/// 玩家角色。枢纽与大多数练习站都复用这个场景。
///
/// 学习要点：
///   1. CharacterBody2D 是「自己写移动逻辑」的角色体：你算好 Velocity，
///      然后调用 MoveAndSlide()，它负责沿墙壁滑动、上下楼梯（2D 里就是贴墙滑行）。
///      RigidBody2D 是交给物理引擎推，适合箱子、布娃娃这类"不受控"的东西。
///   2. Input.GetVector() 会帮你把 4 个方向的按键合成一个 -1~1 的向量，
///      并且自带「斜向不会更快」的归一化和手柄死区处理。自己手写很容易漏掉这两点。
///   3. 加速度（Acceleration）和摩擦力（Friction）分开设置，是"操作手感"的关键：
///      起步快、刹车更快，角色才会显得跟手。
///   4. 加入 "player" 分组，别的节点就能用 GetTree().GetNodesInGroup("player") 找到它，
///      而不需要知道场景结构 —— 这是 Godot 里最轻量的解耦手段。
/// </summary>
public partial class Player : CharacterBody2D
{
    /// <summary>最大移动速度（像素/秒）。</summary>
    [Export] public float MaxSpeed { get; set; } = 235f;

    /// <summary>加速度（像素/秒²）。越大起步越"跟手"。</summary>
    [Export] public float Acceleration { get; set; } = 2200f;

    /// <summary>摩擦力（像素/秒²）。通常应该略大于加速度，让刹车更利落。</summary>
    [Export] public float Friction { get; set; } = 2800f;

    /// <summary>输入死区。手柄摇杆漂移时靠它忽略微小偏移。</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float InputDeadzone { get; set; } = 0.2f;

    /// <summary>
    /// 推动 RigidBody2D 时给对方的稳定速度（像素/秒）。0 = 不推（默认）。
    ///
    /// 为什么需要它：CharacterBody2D 是"自己算位移"的，它撞到 RigidBody2D 时
    /// **不会**自动把力传过去 —— 物理引擎只负责让它自己停下。想让箱子动，就得手动处理。
    /// </summary>
    [Export] public float PushSpeed { get; set; } = 0f;

    /// <summary>本帧的输入方向（已归一化，长度 0 或 1）。</summary>
    public Vector2 InputDirection { get; private set; }

    /// <summary>最后一次移动的方向，用于决定攻击/朝向。永远不为零向量。</summary>
    public Vector2 Facing { get; private set; } = Vector2.Down;

    private bool _mouseAiming;

    /// <summary>
    /// 瞄准方向。玩家动过鼠标之后就用鼠标方向，否则用最后移动的方向。
    ///
    /// 为什么要做这个兜底：`Facing` 的初值是"下"，如果一上来就朝鼠标射，
    /// 鼠标还没动过（停在 0,0）时子弹会全部射向左上角。**新手教程里最忌讳
    /// "我什么都没做，但它做了一件莫名其妙的事"。**
    /// </summary>
    public Vector2 AimDirection
    {
        get
        {
            if (!_mouseAiming) return Facing.Normalized();

            var toMouse = GetGlobalMousePosition() - GlobalPosition;
            return toMouse.LengthSquared() < 16f ? Facing.Normalized() : toMouse.Normalized();
        }
    }

    private Polygon2D _facingMark = null!;

    public override void _Ready()
    {
        _facingMark = GetNode<Polygon2D>("FacingMark");
        AddToGroup("player");
    }

    /// <summary>只用来侦测"玩家动过鼠标"。用 _Input 是因为鼠标移到 UI 上时
    /// 事件会被 Control 吃掉，_UnhandledInput 就收不到了。</summary>
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && motion.Relative.LengthSquared() > 1f)
            _mouseAiming = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        // 为什么用 _PhysicsProcess 而不是 _Process：
        //   物理引擎按固定步长（默认 60Hz）推进，只有在这里算出来的位移才与物理世界一致。
        //   在 _Process 里改 Velocity 会导致「帧率越高跑得越远」的经典 bug。
        InputDirection = Input.GetVector("move_left", "move_right", "move_up", "move_down", InputDeadzone);

        var targetVelocity = InputDirection * MaxSpeed;
        var rate = InputDirection == Vector2.Zero ? Friction : Acceleration;

        // MoveToward 比 Lerp 更好用：它保证「按固定速率逼近」，
        // 不会像 Lerp 那样在接近目标时越来越慢、永远差一点点。
        Velocity = Velocity.MoveToward(targetVelocity, rate * (float)delta);

        if (InputDirection != Vector2.Zero)
        {
            Facing = InputDirection.Normalized();
            _facingMark.Rotation = Facing.Angle();
        }

        MoveAndSlide();

        if (PushSpeed > 0f) PushRigidBodies();
    }

    /// <summary>
    /// 把"这一帧撞到的刚体"朝自己前进的方向推。
    ///
    /// MoveAndSlide 会把本帧的碰撞记录留在 GetSlideCollision(i) 里，
    /// 必须在它之后立刻读 —— 下一帧就被覆盖了。
    ///
    /// 这里用「设定速度」而不是「施加冲量」，是踩过坑之后的结论：
    ///   冲量方案（每帧 ApplyImpulse）看起来更"物理"，但实际效果很差 ——
    ///   玩家一撞到箱子就会被碰撞判定停住，得重新加速才追得上；
    ///   于是整个过程是"一撞一停"，箱子的平均速度只有理论值的三分之一，
    ///   而且会随帧率与阻尼变化。**关卡设计师需要的是可预测，不是物理正确。**
    ///   直接补足速度则完全可控：按住方向键，箱子就以固定速度稳定前进。
    /// </summary>
    private void PushRigidBodies()
    {
        for (var i = 0; i < GetSlideCollisionCount(); i++)
        {
            var collision = GetSlideCollision(i);
            if (collision.GetCollider() is not RigidBody2D body) continue;

            // 方向取「主轴」而不是"从玩家指向箱子"的真实方向：
            // 真实方向在从角落推时会带一个垂直分量，箱子会慢慢往侧面漂，
            // 推几次之后就偏离了目标垫 —— 关卡就变成不可设计的了。
            var delta = body.GlobalPosition - GlobalPosition;
            var direction = Mathf.Abs(delta.X) >= Mathf.Abs(delta.Y)
                ? new Vector2(Mathf.Sign(delta.X), 0f)
                : new Vector2(0f, Mathf.Sign(delta.Y));

            // 只补"沿推动方向还缺的那部分速度"，不整个覆盖 ——
            // 否则箱子自己的惯性会被抹掉（撞到墙也不反弹，看着很假）。
            var along = body.LinearVelocity.Dot(direction);
            if (along >= PushSpeed) continue;

            body.LinearVelocity += direction * (PushSpeed - along);
        }
    }
}
