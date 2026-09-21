using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// S04 的投石 —— 玩家的两个用途：**砸晕守卫** 和 **制造动静把它引开**。
///
/// 学习要点：一个物件对不同的碰撞对象有不同后果时，用 `BodyEntered` 里做类型判断就够了，
/// 不需要给每种对象单独做一套碰撞层。**能简单解决的就别上抽象。**
/// （对比 S03：那里子弹的掩码是"教学展品"，所以要能实时切换；
///   这里石头的行为是固定的，直接判断就行。）
///
/// 还有一个细节：石头**飞到头也算落地**（寿命耗尽时同样发 Landed 信号）。
/// 少了这一条，玩家会发现"石头飞到一半没了，守卫却什么都没听见"——很出戏。
/// **玩家的每一个动作都应该有可预期的反馈**，哪怕这个动作是"没打中"。
/// </summary>
public partial class Stone : Area2D
{
    /// <summary>落地（或飞到寿命尽头）时发出，参数是发出动静的位置。</summary>
    [Signal] public delegate void LandedEventHandler(Vector2 position);

    /// <summary>砸到守卫身上。</summary>
    [Signal] public delegate void HitGuardEventHandler();

    [Export] public float Speed { get; set; } = 540f;
    [Export] public float LifeTime { get; set; } = 2.0f;

    /// <summary>由发射者在 AddChild 之前设置。</summary>
    public Vector2 Direction { get; set; } = Vector2.Right;

    private float _age;
    private bool _consumed;

    public override void _Ready()
    {
        CollisionLayer = GameLayers.Projectile;
        CollisionMask = GameLayers.World | GameLayers.Enemy;
        BodyEntered += OnBodyEntered;
    }

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;
        _age += dt;

        Position += Direction * Speed * dt;
        Rotation += dt * 11f;

        if (_age >= LifeTime) Land(GlobalPosition);
    }

    private void OnBodyEntered(Node2D body)
    {
        if (_consumed) return;

        if (body is Guard guard)
        {
            _consumed = true;
            guard.Stun();
            EmitSignal(SignalName.HitGuard);
            QueueFree();
            return;
        }

        // 打到墙或柱子 —— 一样会发出动静
        Land(GlobalPosition);
    }

    private void Land(Vector2 position)
    {
        if (_consumed) return;
        _consumed = true;

        EmitSignal(SignalName.Landed, position);
        QueueFree();
    }
}
