using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// S03 的炮塔 —— 用一条射线判断"我看不看得见目标"。
///
/// 学习要点：
///   1. 射线检测（RayCast2D）回答的问题永远是同一个：
///      **从 A 到 B 这条直线，中间有没有挡住它的东西。**
///      视线、子弹轨迹、地面检测、鼠标拾取，全是这一个问题的变体。
///   2. 射线是"一根没有粗细的线"。所以它只会被**在碰撞层里**的物体挡住。
///      本站把射线的 mask 设成只包含 World 层 —— 于是玩家和箱子都不会挡视线，
///      只有墙和掩体会。想让敌人互相挡视线，就得把 Enemy 层也加进 mask。
///   3. `ForceRaycastUpdate()` 让射线**立刻**按当前状态算一次，
///      而不是等下一个物理帧。当你在一帧里改了射线方向又要马上用结果时，这是必须的。
///      配套做法是把 `enabled` 设为 false，避免引擎的自动更新和手动更新互相打架。
///   4. 射线本身没有长度限制 —— "射程"要自己另外判（见 SightRange）。
/// </summary>
public partial class Turret : Node2D
{
    /// <summary>射程。超出这个距离就当作看不见。</summary>
    [Export] public float SightRange { get; set; } = 430f;

    public bool HasLineOfSight { get; private set; }

    /// <summary>要观察的目标。由站台在 _Ready 之后赋值。</summary>
    public Node2D? TargetNode { get; set; }

    private static readonly Color SeenColor = new(0.95f, 0.34f, 0.36f);
    private static readonly Color BlockedColor = new(0.34f, 0.85f, 0.55f);

    private RayCast2D _ray = null!;
    private Line2D _beam = null!;
    private Polygon2D _dome = null!;

    public override void _Ready()
    {
        _ray = GetNode<RayCast2D>("Ray");
        _beam = GetNode<Line2D>("Beam");
        _dome = GetNode<Polygon2D>("Dome");

        // 视线只被"世界"挡住：玩家、箱子都不该遮挡视线。
        _ray.CollisionMask = GameLayers.World;
        // 关掉自动更新，改由 _PhysicsProcess 手动更新一次 —— 保证读到的就是这一帧的结果。
        _ray.Enabled = false;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (TargetNode is null) return;

        var toTarget = TargetNode.GlobalPosition - GlobalPosition;

        if (toTarget.Length() > SightRange)
        {
            HasLineOfSight = false;
            _beam.Visible = false;
            _dome.Color = BlockedColor;
            return;
        }

        // RayCast2D.TargetPosition 是**相对于射线自己**的坐标。
        _ray.TargetPosition = toTarget;
        _ray.ForceRaycastUpdate();

        HasLineOfSight = !_ray.IsColliding();

        var endPoint = HasLineOfSight
            ? toTarget
            : _ray.GetCollisionPoint() - GlobalPosition; // 被挡住：光束画到撞墙点为止

        _beam.Visible = true;
        _beam.Points = new[] { Vector2.Zero, endPoint };
        _beam.DefaultColor = HasLineOfSight ? SeenColor : BlockedColor;
        _dome.Color = HasLineOfSight ? SeenColor : BlockedColor;
    }
}
