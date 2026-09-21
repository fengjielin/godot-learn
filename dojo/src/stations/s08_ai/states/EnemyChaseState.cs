using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// 追击：走导航网格过去抓你。
///
/// 注意这里用的是**玩家的实时位置**，而不是"最后看到的位置"。
/// 能进入这个状态就说明目标可见（转移条件是 `CanSeeTargetNow`），
/// 所以用实时位置不算作弊。一旦看不见，会由转移表切到 Search，
/// 那时用的才是 `LastKnownTargetPosition`。
///
/// **AI 的"聪明程度"由它知道多少信息决定，不由算法决定。**
/// 想让敌人变笨，最有效的做法是**减少它知道的信息**（缩短视野、去掉听觉、
/// 丢失目标后立刻忘记），而不是去改寻路算法。
/// </summary>
public partial class EnemyChaseState : State
{
    public override void PhysicsTick(double delta)
    {
        var enemy = Actor<EnemyAgent>();
        if (enemy.Target is null) return;

        enemy.MoveTo(enemy.Target.GlobalPosition, enemy.ChaseSpeed, delta);
    }
}
