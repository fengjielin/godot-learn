using Godot;

namespace MyGame.Entities.Player;

/// <summary>
/// 攻击状态 — 播放攻击动作，期间不能移动。
///
/// 转换条件：
/// - 计时结束 → Idle
///
/// 使用计时器模拟攻击持续时间（后续可改为动画信号驱动）。
/// </summary>
public partial class PlayerStateAttack : PlayerState
{
    /// <summary>攻击持续时间（秒）</summary>
    [Export]
    public float AttackDuration { get; set; } = 0.5f;

    /// <summary>攻击剩余时间</summary>
    private float _attackTimer;

    public override void Enter()
    {
        base.Enter();

        // 重置计时器
        _attackTimer = AttackDuration;

        // 攻击时停止水平移动
        if (Player != null)
        {
            var velocity = Player.Velocity;
            velocity.X = 0;
            Player.Velocity = velocity;
        }

        GD.Print("攻击！");
    }

    public override void PhysicsProcessState(double delta)
    {
        if (Player == null)
            return;

        // 倒计时
        _attackTimer -= (float)delta;

        // 应用重力（攻击时仍然受重力影响，2D: Y 轴向下为正）
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

        // 计时结束 → 回到 Idle
        if (_attackTimer <= 0)
        {
            EmitSignal(SignalName.Transition, "Idle");
        }
    }
}
