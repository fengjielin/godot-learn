using Godot;

namespace MyGame.Entities.Player;

/// <summary>
/// 受伤状态 — 收到伤害时进入，有短暂无敌时间和击退效果。
///
/// 转换条件：
/// - 计时结束 → Idle
///
/// 后续可扩展：无敌闪烁效果、受伤动画等。
/// </summary>
public partial class PlayerStateHurt : PlayerState
{
    /// <summary>受伤硬直时间（秒）</summary>
    [Export]
    public float HurtDuration { get; set; } = 0.8f;

    /// <summary>击退力度</summary>
    [Export]
    public float KnockbackStrength { get; set; } = 200.0f;

    /// <summary>受伤剩余时间</summary>
    private float _hurtTimer;

    public override void Enter()
    {
        base.Enter();

        _hurtTimer = HurtDuration;

        // 击退效果 — 朝玩家移动方向的反方向弹出（2D: 负 Y = 向上弹）
        if (Player != null)
        {
            var velocity = Player.Velocity;
            var facingSign = Mathf.Sign(velocity.X);
            if (facingSign == 0) facingSign = -1; // 静止时默认向左击退

            velocity.X = -facingSign * KnockbackStrength;
            velocity.Y = KnockbackStrength * -0.5f; // 轻微上弹（负 = 向上）
            Player.Velocity = velocity;
        }

        GD.Print("受伤！");
    }

    public override void PhysicsProcessState(double delta)
    {
        if (Player == null)
            return;

        _hurtTimer -= (float)delta;

        // 应用重力（2D: Y 轴向下为正）
        var velocity = Player.Velocity;
        if (!Player.IsOnFloor())
        {
            velocity.Y += Player.Gravity * (float)delta;
        }
        else
        {
            velocity.Y = 0.1f;
        }
        Player.Velocity = velocity;

        Player.MoveAndSlide();

        // 恢复时间结束 → 回到 Idle
        if (_hurtTimer <= 0)
        {
            EmitSignal(SignalName.Transition, "Idle");
        }
    }
}
