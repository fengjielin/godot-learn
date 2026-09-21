using Godot;

namespace Dojo.Common;

/// <summary>
/// 武器数据 —— 一个**自定义 Resource**。
///
/// 为什么数值要从代码里搬出来：
///   写在代码里的数字，改一次就要重新编译、重新运行、重新走到那个场景才能看到效果。
///   于是没人愿意调，最后所有武器手感都一样。
///   搬进 `.tres` 之后：**在编辑器里改数字 → 按一次按钮 → 立刻看到手感变化**。
///   这不是"代码更整洁"，而是**让迭代速度变快** —— 而迭代速度决定了游戏好不好玩。
///
/// 学习要点：
///   1. `[GlobalClass]` 让这个类型在编辑器的「新建资源」列表里出现，
///      而且 `.tres` 里可以直接写 `script_class="WeaponData"`。
///   2. `[Export]` 的字段会出现在检查器里，可以拖滑块、可以直接编辑 `.tres` 文件。
///   3. Resource 是**共享**的：同一个 `.tres` 被多处引用时是同一个对象。
///      想改一个实例而不影响别人，要 `Duplicate()` —— 这个坑在 S10 背包里会正面遇到。
///
/// 什么该进 Resource，什么不该：
///   ✅ 该进：数值、曲线、颜色、引用（贴图/音效/子弹场景）
///   ❌ 不该进：运行时状态（当前耐久、冷却剩余时间）、指向场景节点的引用
///   判断标准一句话：**"这行数据在游戏运行期间会变吗？会变就不该进配置。"**
/// </summary>
[GlobalClass]
public partial class WeaponData : Resource
{
    [Export] public string DisplayName { get; set; } = "无名武器";

    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";

    /// <summary>单次命中伤害。</summary>
    [Export] public int Damage { get; set; } = 10;

    /// <summary>两次攻击之间的最小间隔（秒）。</summary>
    [Export] public float AttackInterval { get; set; } = 0.35f;

    /// <summary>出手前摇（秒）。给玩家反应时间，也给打击感留出"蓄力"的余地。</summary>
    [Export] public float Windup { get; set; } = 0.08f;

    /// <summary>攻击距离：判定框前端离角色中心多远。</summary>
    [Export] public float Reach { get; set; } = 52f;

    /// <summary>判定框的宽度（扇形张开的程度）。</summary>
    [Export] public float ArcWidth { get; set; } = 44f;

    [Export] public float Knockback { get; set; } = 200f;
    [Export] public float CritChance { get; set; } = 0.15f;
    [Export] public float CritMultiplier { get; set; } = 2f;

    /// <summary>判定框的颜色，用来看出"这一刀范围多大"。</summary>
    [Export] public Color TintColor { get; set; } = new(1f, 0.94f, 0.7f);

    /// <summary>
    /// 理论 DPS。**这一个数字就是"调参"这个词的全部意义**：
    /// 大剑单次伤害高，但攻速慢；匕首单次低，但快。
    /// 只看伤害数字会觉得大剑强得多，把 DPS 算出来才发现差别没那么大 ——
    /// 而它们的**手感**差别是巨大的。
    /// </summary>
    public float Dps => AttackInterval <= 0.001f ? 0f : Damage / AttackInterval;

    public DamageInfo ToDamageInfo(Vector2 source) => new()
    {
        BaseDamage = Damage,
        CritChance = CritChance,
        CritMultiplier = CritMultiplier,
        Knockback = Knockback,
        SourcePosition = source,
    };

    /// <summary>从磁盘重新读一份，忽略缓存。**"改完立刻看到效果"靠的就是它。**</summary>
    public static WeaponData? ReloadFromDisk(string resPath)
        => ResourceLoader.Load<WeaponData>(resPath, cacheMode: ResourceLoader.CacheMode.Replace);
}
