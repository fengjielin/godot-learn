using Godot;
using Dojo.Common;

namespace Dojo.Common;

/// <summary>
/// 同名效果再次施加时的规则。**这是状态效果系统里唯一真正难的决定。**
///
/// 三种规则各有各的适用场景，选错了会做出很怪的手感：
///   · Refresh（刷新时长）—— 适合"状态类"效果：加速、护盾、无敌。
///     连踩两次不会让你跑得更快，只是把持续时间重置。
///   · Stack（叠层）—— 适合"伤害类"效果：中毒、流血、点燃。
///     叠层让"连续命中"有额外收益，是 DoT 玩法的核心。
///   · Ignore（已有则无效）—— 适合"唯一性"效果：变身、霸体。
///     第二次施加应该被明确拒绝，而不是悄悄吞掉。
/// </summary>
public enum StackRule
{
    Refresh,
    Stack,
    Ignore,
}

/// <summary>
/// 一种状态效果的定义。**全部是数据**，没有任何逻辑 ——
/// 逻辑在 StatusEffects 里，规则在 StackRule 里。
/// </summary>
public sealed record StatusEffectDef(
    string Id,
    string DisplayName,
    Color Color,
    float Duration,
    int MaxStacks,
    StackRule Rule,
    string Description,
    float Cooldown);

/// <summary>
/// 一次施加的结果，包含**每一环的过程** —— 和 S07 的伤害管线是同一个思路。
/// （这里直接复用了 S07 定义的 `DamageStep`，因为它就是"一步过程"这个通用概念。）
/// </summary>
public sealed class StatusApplyResult
{
    public string EffectId { get; set; } = "";
    public bool Applied { get; set; }
    public string Outcome { get; set; } = "";
    public List<DamageStep> Steps { get; } = new();

    public void Add(string name, string detail, bool applied = true)
        => Steps.Add(new DamageStep(name, detail, applied));
}

/// <summary>效果表。整个"药水台"里有什么，全在这里。</summary>
public static class StatusEffectLibrary
{
    public const string Haste = "haste";
    public const string Poison = "poison";
    public const string Shield = "shield";
    public const string Regen = "regen";

    public static readonly StatusEffectDef[] All =
    {
        new(Haste, "加速", new Color(0.45f, 0.85f, 1f),
            Duration: 6f, MaxStacks: 1, Rule: StackRule.Refresh,
            Description: "速度 +60%（刷新）", Cooldown: 1.0f),

        new(Poison, "中毒", new Color(0.62f, 0.9f, 0.4f),
            Duration: 5f, MaxStacks: 5, Rule: StackRule.Stack,
            // 冷却必须**短于**"玩家站在毒池里的时间"，否则叠层会显得迟钝 ——
            // 最初设 0.35s，比药水台的 0.22s 重试间隔还长，实测 5 秒只叠到 3 层。
            // 调到 0.20s 之后，站在毒池里 1 秒多就能叠满。
            Description: "2 点/0.5s ×层数（最多 5 层）", Cooldown: 0.20f),

        new(Shield, "无敌", new Color(1f, 0.88f, 0.4f),
            Duration: 4f, MaxStacks: 1, Rule: StackRule.Refresh,
            Description: "免疫伤害与中毒（刷新）", Cooldown: 1.5f),

        new(Regen, "回血", new Color(0.5f, 0.95f, 0.62f),
            Duration: 5f, MaxStacks: 1, Rule: StackRule.Refresh,
            Description: "4 点/秒（刷新）", Cooldown: 1.0f),
    };

    public static StatusEffectDef Get(string id)
    {
        foreach (var def in All)
            if (def.Id == id) return def;
        throw new ArgumentException($"没有名为 '{id}' 的状态效果");
    }
}
