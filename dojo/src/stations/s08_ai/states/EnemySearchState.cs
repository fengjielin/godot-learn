using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// 搜索：去"最后知道你在哪"的地方找一圈。
///
/// 这一个状态把两种来源合并了：
///   · **听到的动静**（`HasPendingNoise`）—— 去声音那里看看
///   · **最后看到的位置** —— 追丢之后回去找
/// 它们的**行为完全一样**（走过去 + 找一会儿），所以不需要两个状态。
/// **加状态之前先问一句：这个状态的行为和已有的某个状态真的不一样吗？**
/// 行为相同、只是触发原因不同的话，用一个状态 + 一个"去哪"的参数就够了。
///
/// 这也是为什么任务 ① 说的"新增搜索状态"值得做：
/// 有了它，AI 从"要么瞎逛、要么死追"变成了有记忆、会犹豫的东西。
/// </summary>
public partial class EnemySearchState : State
{
    private Vector2 _searchPoint;

    public override void Enter()
    {
        var enemy = Actor<EnemyAgent>();

        // 优先去听声音的地方，否则回最后看到目标的地方
        _searchPoint = enemy.HasPendingNoise ? enemy.NoisePosition : enemy.LastKnownTargetPosition;

        enemy.SearchTimer = enemy.SearchSeconds;
        enemy.HasPendingNoise = false;
    }

    public override void PhysicsTick(double delta)
    {
        var enemy = Actor<EnemyAgent>();
        enemy.MoveTo(_searchPoint, enemy.SearchSpeed, delta);
    }

    public override void Tick(double delta)
    {
        // 倒计时由角色统一管（和 S04 一样，避免两个地方各减一次）
        var enemy = Actor<EnemyAgent>();
        if (enemy.SearchTimer > 0f) enemy.SearchTimer -= (float)delta;
    }
}
