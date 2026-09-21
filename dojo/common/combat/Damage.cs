using Godot;

namespace Dojo.Common;

/// <summary>
/// 一次攻击的"输入参数"。**只描述"打得多重"，不描述"打到了谁"。**
///
/// 为什么要把"伤害数值"和"扣血逻辑"分开：
///   武器、陷阱、毒、掉落伤害……来源千差万别，但"怎么扣"只有一套规则。
///   把它们绑在一起，就会出现"毒是按帧扣的所以不吃暴击""陷阱的击退方向算错了"
///   这类只在某个来源上出现的怪 bug。
///   **分开之后，规则的修改对所有伤害来源同时生效。**
/// </summary>
public sealed class DamageInfo
{
    public int BaseDamage { get; init; } = 10;

    /// <summary>暴击概率（0~1）。</summary>
    public float CritChance { get; init; } = 0.15f;

    /// <summary>暴击倍率。默认双倍。</summary>
    public float CritMultiplier { get; init; } = 2f;

    /// <summary>击退力度（像素/秒）。0 = 不击退。</summary>
    public float Knockback { get; init; } = 240f;

    /// <summary>伤害来源的位置。**击退方向由它和受击者的位置算出来**，不是随便定的。</summary>
    public Vector2 SourcePosition { get; init; }
}

/// <summary>伤害管线里的一环。`Applied == false` 表示这一环被跳过或被免疫。</summary>
public sealed record DamageStep(string Name, string Detail, bool Applied);

/// <summary>
/// 一次命中的**完整结果**，包括每一环的过程。
///
/// 为什么要把过程也留下来：
///   战斗最烦的 bug 不是"数值不对"，而是"**为什么这次是 24，那次是 12**"。
///   如果只留下最终数字，你只能靠猜。把每一环的输入输出都记下来，
///   面板一显示，答案就在眼前。**这和 S04 状态机的面板是同一个思路。**
/// </summary>
public sealed class DamageResult
{
    public int BaseDamage { get; set; }
    public bool BlockedByIFrame { get; set; }
    public bool WasCrit { get; set; }
    public int ShieldAbsorbed { get; set; }
    public int ShieldRemaining { get; set; }
    public int HpLost { get; set; }
    public int HpRemaining { get; set; }
    public Vector2 Knockback { get; set; }
    public bool Killed { get; set; }

    public List<DamageStep> Steps { get; } = new();

    /// <summary>飘字应该显示的数字（免疫时显示 0 并被特殊标色）。</summary>
    public int DisplayAmount => BlockedByIFrame ? 0 : ShieldAbsorbed + HpLost;

    public void Add(string name, string detail, bool applied = true)
        => Steps.Add(new DamageStep(name, detail, applied));
}
