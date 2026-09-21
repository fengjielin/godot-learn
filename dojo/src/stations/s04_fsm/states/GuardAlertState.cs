using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// 警觉：听到了动静、或者刚跟丢目标，过去看看。
///
/// 这是"警觉 → 追击 → 攻击"这条链里最容易被省略、但最不该省的一环。
/// 没有它，AI 就变成"要么瞎逛、要么死追"两种极端：
///   · 玩家丢个石头，守卫立刻全速冲过来 —— 太聪明，不像人
///   · 玩家一闪身，守卫立刻忘记 —— 太蠢
/// 中间这个"过去看看，看不着就回去"的过渡，才让 AI 显得**像个活物**。
///
/// Enter / Exit 在这里有了真实用途：
///   Enter 设倒计时、抬头顶的问号；Exit 把问号清掉。
/// **进入一个状态要做的事，和离开时要收的尾，往往是不对称的** ——
/// 这就是为什么生命周期要有两个钩子，而不是一个。
/// </summary>
public partial class GuardAlertState : State
{
    private double _investigateTime;

    public override void Enter()
    {
        var guard = Actor<Guard>();

        guard.AlertTimer = guard.AlertSeconds;
        guard.SetMood(Guard.Mood.Alert);
        guard.MarkerText = "?";
        guard.StopMoving();

        // 听到动静就转向那个方向；否则转向最后看到目标的位置
        var point = guard.HasPendingNoise ? guard.NoisePosition : guard.LastKnownTargetPosition;
        guard.FaceToward(point);
        _investigateTime = 0;
    }

    public override void Exit()
    {
        Actor<Guard>().MarkerText = "";
    }

    public override void PhysicsTick(double delta)
    {
        var guard = Actor<Guard>();
        var point = guard.HasPendingNoise ? guard.NoisePosition : guard.LastKnownTargetPosition;

        _investigateTime += delta;

        // 走过去看看。靠近了就停下东张西望，别像无头苍蝇一样原地打转。
        if (guard.GlobalPosition.DistanceTo(point) > 30f)
            guard.MoveToward(point, guard.AlertSpeed, delta);
        else
            guard.StopMoving();
    }

    public override void Tick(double delta)
    {
        // 警戒时间由角色统一倒数（见 Guard._Process）。
        // 这里只负责"时间到了就忘掉那个噪音"这件收尾的事。
        var guard = Actor<Guard>();
        if (guard.AlertTimer > 0) return;

        guard.HasPendingNoise = false;
    }
}
