using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// 追击：朝目标冲过去。
///
/// 一个容易被忽略的细节：**目标是"玩家的当前位置"，还是"最后看到的位置"？**
/// 这里用的是当前位置（因为能走到这个状态就说明目标可见），
/// 而一旦看不见了，会由转移表切到 Alert，那时候用的才是 LastKnownTargetPosition。
///
/// 这个区分很重要：如果追击时一直追"最后看到的位置"，守卫会显得很迟钝；
/// 如果永远追"玩家的真实位置"（穿墙也知道你在哪），那就是作弊。
/// **AI 的"聪明程度"本质上由"它知道多少信息"决定，而不是由算法决定。**
/// </summary>
public partial class GuardChaseState : State
{
    public override void Enter()
    {
        var guard = Actor<Guard>();
        guard.SetMood(Guard.Mood.Chase);
        guard.MarkerText = "!";
        guard.ResetVisualRotation();
    }

    public override void Exit() => Actor<Guard>().MarkerText = "";

    public override void PhysicsTick(double delta)
    {
        var guard = Actor<Guard>();
        if (guard.Target is null) return;

        guard.MoveToward(guard.Target.GlobalPosition, guard.ChaseSpeed, delta);
    }
}
