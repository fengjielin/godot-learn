using Godot;

namespace MyGame.Entities.Player;

/// <summary>
/// 跑步状态 — 按住跑步键（Shift）时的高速移动。
///
/// 转换条件：
/// - 松开跑步键         → Walk（仍在移动时）
/// - 移动输入 = 0       → Idle
/// - 按下跳跃键         → Jump
/// - 按下攻击键         → Attack
/// </summary>
public partial class PlayerStateRun : PlayerState
{
    public override void PhysicsProcessState(double delta)
    {
        if (Player == null)
            return;

        // 应用重力
        ApplyGravity(delta);

        // 水平移动：方向 × 跑步速度
        var velocity = Player.Velocity;
        velocity.X = Player.MoveDirection * Player.RunSpeed;
        Player.Velocity = velocity;

        // 1. 松开跑步键 → 回到步行（仍在移动时）
        if (!Player.IsRunPressed && Player.MoveDirection != 0)
        {
            EmitSignal(SignalName.Transition, "Walk");
            return;
        }

        // 2. 停止移动 → 站立
        if (Player.MoveDirection == 0)
        {
            EmitSignal(SignalName.Transition, "Idle");
            return;
        }

        // 3. 按下跳跃键 → 跳跃
        if (Input.IsActionJustPressed("jump") && Player.IsOnFloor())
        {
            EmitSignal(SignalName.Transition, "Jump");
            return;
        }

        // 4. 按下攻击键 → 攻击
        if (Input.IsActionJustPressed("attack"))
        {
            EmitSignal(SignalName.Transition, "Attack");
            return;
        }

        Player.MoveAndSlide();
    }

    /// <summary>
    /// 应用重力 — 2D 中 Y 轴向下为正。
    /// </summary>
    private void ApplyGravity(double delta)
    {
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
    }
}
