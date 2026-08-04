using Godot;

namespace MyGame.Entities.Player;

/// <summary>
/// 站立状态 — 玩家静止不动，监听输入决定切换到哪个状态。
///
/// 转换条件：
/// - 移动输入 ≠ 0 → Walk
/// - 按下跳跃键  → Jump
/// - 按下攻击键  → Attack
/// </summary>
public partial class PlayerStateIdle : PlayerState
{
    public override void PhysicsProcessState(double delta)
    {
        if (Player == null)
            return;

        // 应用重力，确保在地面上时不会悬浮
        ApplyGravity(delta);

        // 水平方向逐渐减速到零（站立时不应滑动）
        var velocity = Player.Velocity;
        velocity.X = Mathf.MoveToward(velocity.X, 0, Player.MoveSpeed * (float)delta * 10f);
        Player.Velocity = velocity;

        // 1. 有移动输入 → 行走
        if (Player.MoveDirection != 0)
        {
            EmitSignal(SignalName.Transition, "Walk");
            return;
        }

        // 2. 按下跳跃键 → 跳跃
        if (Input.IsActionJustPressed("jump") && Player.IsOnFloor())
        {
            EmitSignal(SignalName.Transition, "Jump");
            return;
        }

        // 3. 按下攻击键 → 攻击
        if (Input.IsActionJustPressed("attack"))
        {
            EmitSignal(SignalName.Transition, "Attack");
            return;
        }

        Player.MoveAndSlide();
    }

    /// <summary>
    /// 应用重力 — 2D 中 Y 轴向下为正，所以用 += 加法。
    /// </summary>
    private void ApplyGravity(double delta)
    {
        var velocity = Player.Velocity;
        if (!Player.IsOnFloor())
        {
            // 2D: Y 正轴朝下，重力向下加速
            velocity.Y += Player.Gravity * (float)delta;
        }
        else
        {
            // 贴在地面上时，给一个微小的向下速度防止抖动
            velocity.Y = 0.1f;
        }
        Player.Velocity = velocity;
    }
}
