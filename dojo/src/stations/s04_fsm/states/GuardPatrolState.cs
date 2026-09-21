using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// 巡逻：沿着一串路点来回走。
///
/// 注意这个状态**完全没有**判断"要不要转去追击" ——
/// 那只写在 Guard 的转移表里。状态只回答一个问题：**"在巡逻时我该干什么"。**
///
/// 这就是状态机最大的好处：加一个新行为（比如"巡逻时偶尔停下来东张西望"），
/// 你只需要改这一个文件，不用担心把别的地方弄坏。
/// </summary>
public partial class GuardPatrolState : State
{
    public override void Enter()
    {
        var guard = Actor<Guard>();
        guard.SetMood(Guard.Mood.Patrol);
        guard.MarkerText = "";
        guard.ResetVisualRotation();
    }

    public override void PhysicsTick(double delta)
    {
        var guard = Actor<Guard>();
        if (guard.Waypoints.Length == 0)
        {
            guard.StopMoving();
            return;
        }

        var target = guard.Waypoints[guard.WaypointIndex];
        if (guard.MoveToward(target, guard.PatrolSpeed, delta))
            guard.WaypointIndex = (guard.WaypointIndex + 1) % guard.Waypoints.Length;
    }
}
