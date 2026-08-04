using Godot;

namespace MyGame.Entities.Player;

/// <summary>
/// 跳跃状态 — 玩家在空中，受到重力影响。
///
/// 转换条件：
/// - 落地（IsOnFloor）→ Walk（有移动输入）或 Idle（无输入）
///
/// 进入时自动施加向上的初始速度（2D 中向上为负 Y）。
/// </summary>
public partial class PlayerStateJump : PlayerState
{
    public override void Enter()
    {
        base.Enter();

        // 进入状态时立即施加跳跃初速度（2D: 负 Y = 向上）
        if (Player != null)
        {
            var velocity = Player.Velocity;
            velocity.Y = Player.JumpVelocity;
            Player.Velocity = velocity;
        }

        GD.Print("跳跃！");
    }

    public override void PhysicsProcessState(double delta)
    {
        if (Player == null)
            return;

        // 应用重力（2D: 正 Y = 向下加速）
        var velocity = Player.Velocity;
        velocity.Y += Player.Gravity * (float)delta;
        Player.Velocity = velocity;

        // 空中可以左右移动
        velocity = Player.Velocity;
        velocity.X = Player.MoveDirection * Player.MoveSpeed;
        Player.Velocity = velocity;

        Player.MoveAndSlide();

        // 落地 → 根据是否有移动输入决定回到 Walk 还是 Idle
        if (Player.IsOnFloor())
        {
            var nextState = Player.MoveDirection != 0 ? "Walk" : "Idle";
            EmitSignal(SignalName.Transition, nextState);
        }
    }
}
