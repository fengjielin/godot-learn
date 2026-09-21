using Godot;

namespace Dojo.Common;

/// <summary>
/// 战斗规则的全局开关。
///
/// 为什么用静态类：S07 的五个开关（护盾 / 暴击 / 无敌帧 / 击退 / 飘字池）是
/// **整个实验的全局条件**，不是某个敌人的属性 —— 关掉暴击是要看"整个战斗变成什么样"。
/// 所以它们放在一处，所有受击者共享。
///
/// 代价要说清楚：静态可变状态让"谁改的"变得难追。真实项目里这种东西
/// 应该收在一个 `GameRules` 单例里并配一个调试面板（本站就是那个面板）。
/// **能接受静态状态的前提是：它只出现在调试/规则层，不要渗进玩法逻辑。**
/// </summary>
public static class CombatRules
{
    public static bool ShieldEnabled { get; set; } = true;
    public static bool CritEnabled { get; set; } = true;
    public static bool IframeEnabled { get; set; } = true;
    public static bool KnockbackEnabled { get; set; } = true;
    public static bool DamageNumberPoolEnabled { get; set; } = true;
}
