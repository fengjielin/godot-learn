using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>巡逻：沿路点走，走的是导航网格算出来的路（所以会绕开墙）。</summary>
public partial class EnemyPatrolState : State
{
    public override void Enter()
    {
        var enemy = Actor<EnemyAgent>();
        enemy.SearchTimer = 0f;
    }

    public override void PhysicsTick(double delta)
    {
        var enemy = Actor<EnemyAgent>();
        if (enemy.Waypoints.Length == 0) return;

        var target = enemy.Waypoints[enemy.WaypointIndex];
        if (enemy.MoveTo(target, enemy.PatrolSpeed, delta))
            enemy.WaypointIndex = (enemy.WaypointIndex + 1) % enemy.Waypoints.Length;
    }
}
