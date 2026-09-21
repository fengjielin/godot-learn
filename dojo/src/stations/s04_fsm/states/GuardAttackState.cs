using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// 攻击：前摇 → 出手 → 后摇。
///
/// 三个阶段都必要，缺一个都会让战斗变得不可读：
///   · **前摇（Windup）**：给玩家反应时间。没有它，攻击就是"凭空挨了一下"。
///     本站刻意做了 0.55 秒，并且用一个不断扩张的红圈把它画出来 ——
///     **预警必须看得见，不然等于没有。**
///   · **出手**：伤害判定只在这一瞬间发生，而不是整个攻击过程都在判定。
///   · **后摇（Recover）**：硬直。这是玩家的"奖励窗口" ——
///     你躲过了攻击，就能趁着这段硬直反击。没有后摇，AI 会变成无懈可击的绞肉机。
///
/// 本站还把 `Exit()` 用来挂"攻击后的冷却"：
/// 不管这个状态是怎么结束的（正常打完、还是被石头打断），冷却都会生效。
/// **把"无论怎么离开都要做的事"放在 Exit 里，比在每个转移上都写一遍可靠得多。**
/// </summary>
public partial class GuardAttackState : State
{
    private bool _struck;

    public override void Enter()
    {
        var guard = Actor<Guard>();

        guard.AttackTimer = guard.AttackWindup + guard.AttackRecover;
        guard.SetMood(Guard.Mood.Attack);
        guard.MarkerText = "!";
        guard.StopMoving();

        _struck = false;

        // 进入攻击时的表现：预警圈亮起来（练习任务 ②说的"OnEnter 播放特效"）
        guard.ShowTelegraph(0f);
    }

    public override void Exit()
    {
        var guard = Actor<Guard>();

        guard.HideTelegraph();
        guard.MarkerText = "";
        guard.AttackCooldown = 0.45;
    }

    public override void Tick(double delta)
    {
        var guard = Actor<Guard>();

        if (_struck) return;

        var windupLeft = (float)(guard.AttackTimer - guard.AttackRecover);
        guard.ShowTelegraph(1f - Mathf.Max(0f, windupLeft / guard.AttackWindup));

        if (guard.AttackTimer > guard.AttackRecover) return;

        // 前摇结束 —— 出手，只此一次
        _struck = true;
        var hit = guard.DoStrike();
        guard.EmitSignal(Guard.SignalName.StrikeLanded, hit);
    }
}
