using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// S03 的子弹。它是本站最重要的教具 —— 因为它的**碰撞掩码可以在运行时改**。
///
/// 学习要点：
///   1. 掩码（collision_mask）决定"我会检测到谁"。
///      掩码里没有 World 层，子弹就**根本不会**和墙发生碰撞 ——
///      不是"撞到了再判断要不要处理"，而是连事件都不会来。
///      这就是层与掩码最大的价值：**把"该不该交互"提前到物理引擎里解决**，
///      而不是在每个对象的碰撞回调里写一堆 if。
///   2. Godot 已经按掩码过滤过了，所以回调里不需要再判断"对方是不是我要的类型"。
///      能走到 OnBodyEntered，就说明对方在掩码里。
///   3. Area2D 有两种检测事件，别搞混：
///        · BodyEntered  → 检测到 PhysicsBody2D（StaticBody2D / CharacterBody2D / RigidBody2D）
///        · AreaEntered  → 检测到另一个 Area2D（比如靶子的受击框）
///      想同时处理两者，就得两个信号都接。
/// </summary>
public partial class Bullet : Area2D
{
    [Signal] public delegate void HitTargetEventHandler();
    [Signal] public delegate void HitWallEventHandler();

    [Export] public float Speed { get; set; } = 660f;
    [Export] public float LifeTime { get; set; } = 2.6f;

    /// <summary>由发射者在 AddChild 之前设置。</summary>
    public Vector2 Direction { get; set; } = Vector2.Right;

    private float _age;
    private bool _consumed;
    private Polygon2D _body = null!;

    public override void _Ready()
    {
        _body = GetNode<Polygon2D>("Body");

        // 「我是什么」在场景里就定好了：子弹永远在 Projectile 层。
        // 「我检测什么」由发射者按预设设置，见 S03Physics.ApplyPreset。
        CollisionLayer = GameLayers.Projectile;

        BodyEntered += OnBodyEntered;
        AreaEntered += OnAreaEntered;
    }

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;
        _age += dt;

        Position += Direction * Speed * dt;
        Rotation = Direction.Angle();

        if (_age >= LifeTime) QueueFree();
    }

    private void OnBodyEntered(Node2D body)
    {
        // 能进到这里 = 对方的层在我的掩码里 = 对方是"我该撞的东西"。
        // 掩码为 0 时，这个回调永远不会触发 —— 子弹会直接穿过去。
        _body.Color = new Color(1f, 0.62f, 0.35f);
        EmitSignal(SignalName.HitWall);
        Consume();
    }

    private void OnAreaEntered(Area2D area)
    {
        if (area is not Target target) return;

        target.RegisterHit();
        EmitSignal(SignalName.HitTarget);
        Consume();
    }

    private void Consume()
    {
        // 一帧内可能同时撞到多个东西，保险起见只处理第一次。
        if (_consumed) return;
        _consumed = true;
        QueueFree();
    }
}
