using Godot;

namespace Dojo.Common;

/// <summary>
/// 状态效果容器 —— 挂在角色身上，管理所有 Buff / Debuff。
///
/// ★ 三层结构（这是本站唯一想让你记住的架构）：
///     ① **定义**（StatusEffectDef）—— 纯数据：持续多久、最多几层、什么规则
///     ② **规则**（StackRule）—— 同名再次施加时怎么办
///     ③ **容器**（本类）—— 管计时、管叠层、管触发
///
///   把它们分开之后，"加一个新效果"就只是往表里加一行，
///   而"改叠加规则"只改一个枚举值。**这是 S09 数据驱动在系统层的延伸。**
///
/// ★ 冷却和持续时间的区别（很容易混）：
///   · **持续时间**是"效果在身上挂多久"
///   · **冷却**是"多久之内不能再次施加"
///   两者独立。没有冷却的话，站在药水台上就能无限刷新 —— 所以本站每个效果都有冷却。
///
/// ★ 每层独立计时（Stack 规则）：
///   中毒叠到 3 层时，三层各有自己的倒计时。第一个到期的消失、剩下两层继续。
///   如果做成"共享一个倒计时"，那么每叠一层就等于刷新全部 —— 那是 Refresh，不是 Stack。
///   **叠层和刷新最本质的区别就在这里。**
/// </summary>
public partial class StatusEffects : Node
{
    /// <summary>效果被成功施加（含刷新/叠层）。参数：效果 id。</summary>
    public event Action<string>? EffectApplied;

    /// <summary>某一层（或整个效果）消失。参数：效果 id。</summary>
    public event Action<string>? EffectExpired;

    /// <summary>需要定期造成伤害时调用（中毒）。参数：伤害值。</summary>
    public event Action<int>? DamageTick;

    /// <summary>需要定期治疗时调用。参数：治疗量。</summary>
    public event Action<int>? HealTick;

    private sealed class Layer
    {
        public float Remaining;
    }

    private sealed class Active
    {
        public StatusEffectDef Def = null!;
        public readonly List<Layer> Layers = new();
        public float CooldownRemaining;

        public int StackCount => Layers.Count;
        public float LongestRemaining
        {
            get
            {
                var best = 0f;
                foreach (var layer in Layers) best = Mathf.Max(best, layer.Remaining);
                return best;
            }
        }
    }

    private readonly Dictionary<string, Active> _active = new();
    private float _poisonTickTimer;
    private float _regenTickTimer;

    /// <summary>移动速度倍率 —— 由加速效果决定。角色每帧读它。</summary>
    public float MoveSpeedMultiplier { get; private set; } = 1f;

    /// <summary>是否处于无敌。它同时用于"免疫伤害"和"免疫中毒"。</summary>
    public bool IsInvulnerable => Has(StatusEffectLibrary.Shield);

    public sealed record EffectView(StatusEffectDef Def, int Stacks, float LongestRemaining, float CooldownRemaining);

    /// <summary>给 UI 用的一份快照。**不要直接把内部字典暴露给界面** —— 它会被改。</summary>
    public List<EffectView> Snapshot()
    {
        var list = new List<EffectView>();
        foreach (var pair in _active)
            list.Add(new EffectView(pair.Value.Def, pair.Value.StackCount, pair.Value.LongestRemaining, pair.Value.CooldownRemaining));
        return list;
    }

    /// <summary>查一个效果当前的状态。没有就返回 null。</summary>
    public EffectView? Find(string id)
    {
        if (!_active.TryGetValue(id, out var active) || active.StackCount == 0) return null;
        return new EffectView(active.Def, active.StackCount, active.LongestRemaining, active.CooldownRemaining);
    }

    public int StackCountOf(string id) => Find(id)?.Stacks ?? 0;

    public bool Has(string id) => _active.TryGetValue(id, out var a) && a.StackCount > 0;

    /// <summary>★ 施加一个效果，返回**每一步的过程**。</summary>
    public StatusApplyResult Apply(string id)
    {
        var def = StatusEffectLibrary.Get(id);
        var result = new StatusApplyResult { EffectId = id };

        // ---- ① 免疫检查 ----
        // 无敌期间免疫中毒。注意这条规则是**写在"毒"这一侧还是"无敌"这一侧**的？
        // 这里写在施加逻辑里（"有毒要进来时，先问一句我是不是无敌"）——
        // 因为它描述的是"谁能进来"，属于容器的职责。
        if (def.Id == StatusEffectLibrary.Poison && IsInvulnerable)
        {
            result.Add("① 免疫检查", "当前处于【无敌】—— 中毒被完全免疫", applied: false);
            result.Outcome = "被免疫挡下";
            return result;
        }
        result.Add("① 免疫检查", "未被免疫，继续", applied: false);

        // ---- ② 冷却检查 ----
        if (_active.TryGetValue(id, out var existing) && existing.CooldownRemaining > 0f)
        {
            result.Add("② 冷却检查", $"冷却中（还剩 {existing.CooldownRemaining:0.00}s）—— 本次施加被拒绝", applied: false);
            result.Outcome = "被冷却挡下";
            return result;
        }
        result.Add("② 冷却检查", "不在冷却中，继续", applied: false);

        if (existing is null)
        {
            existing = new Active { Def = def };
            _active[id] = existing;
        }

        // ---- ③ 叠加规则 ----
        switch (def.Rule)
        {
            case StackRule.Stack:
                if (existing.StackCount >= def.MaxStacks)
                {
                    // 层数满了怎么办？本站的规则是"刷新最早那一层"。
                    // **这也是一个必须明确做出的决定** —— 常见的另外两种是
                    // "直接拒绝"和"刷新全部层"。三者手感完全不同。
                    existing.Layers.RemoveAt(0);
                    existing.Layers.Add(new Layer { Remaining = def.Duration });
                    result.Add("③ 叠加规则", $"已达上限 {def.MaxStacks} 层 —— 移除最早的一层，补上新层");
                }
                else
                {
                    existing.Layers.Add(new Layer { Remaining = def.Duration });
                    result.Add("③ 叠加规则", $"最多 {def.MaxStacks} 层，原有 {existing.StackCount - 1} 层 → 叠到 {existing.StackCount} 层（新层独立计时）");
                }
                break;

            case StackRule.Refresh:
                foreach (var layer in existing.Layers) layer.Remaining = def.Duration;
                if (existing.StackCount == 0) existing.Layers.Add(new Layer { Remaining = def.Duration });
                result.Add("③ 叠加规则", $"规则是「刷新时长」—— 仍是 1 层，倒计时重置为 {def.Duration:0.0}s（不会更强）");
                break;

            default:
                result.Add("③ 叠加规则", "规则是「已有则无效」—— 本次施加被忽略", applied: false);
                result.Outcome = "已有该效果，忽略";
                return result;
        }

        existing.CooldownRemaining = def.Cooldown;
        UpdateDerived();

        result.Applied = true;
        result.Outcome = $"{def.DisplayName} {existing.StackCount} 层，最长剩余 {existing.LongestRemaining:0.00}s";
        EffectApplied?.Invoke(id);
        return result;
    }

    /// <summary>清空所有效果（重生时用）。</summary>
    public void Clear()
    {
        _active.Clear();
        _poisonTickTimer = 0f;
        _regenTickTimer = 0f;
        UpdateDerived();
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        var expired = new List<string>();

        foreach (var pair in _active)
        {
            var active = pair.Value;
            if (active.CooldownRemaining > 0f) active.CooldownRemaining = Mathf.Max(0f, active.CooldownRemaining - dt);

            // 倒着遍历：删除元素时不会打乱后面的下标
            for (var i = active.Layers.Count - 1; i >= 0; i--)
            {
                active.Layers[i].Remaining -= dt;
                if (active.Layers[i].Remaining > 0f) continue;

                active.Layers.RemoveAt(i);
                EffectExpired?.Invoke(pair.Key);
            }

            if (active.StackCount == 0) expired.Add(pair.Key);
        }

        foreach (var id in expired) _active.Remove(id);

        TickPeriodicDamage(dt);
        UpdateDerived();
    }

    /// <summary>
    /// 中毒：**层数越多，跳得越频繁**。这是"叠层有收益"的具体体现。
    ///
    /// 单层伤害从 3 降到 2，是实测调出来的：本站的玩法是"站在毒池里把层数叠满"，
    /// 3 点/层 × 5 层 = 每 0.5 秒 15 点，玩家根本活不到叠满就死了（实测跳伤 26 次就倒地）。
    /// **演示型数值要按"玩家需要活多久"来定，不是按"打起来爽不爽"。**
    /// </summary>
    private void TickPeriodicDamage(float dt)
    {
        if (_active.TryGetValue(StatusEffectLibrary.Poison, out var poison) && poison.StackCount > 0)
        {
            _poisonTickTimer += dt;
            if (_poisonTickTimer >= 0.5f)
            {
                _poisonTickTimer = 0f;
                DamageTick?.Invoke(2 * poison.StackCount);
            }
        }
        else
        {
            _poisonTickTimer = 0f;
        }

        if (Has(StatusEffectLibrary.Regen))
        {
            _regenTickTimer += dt;
            if (_regenTickTimer >= 1f)
            {
                _regenTickTimer = 0f;
                HealTick?.Invoke(4);
            }
        }
        else
        {
            _regenTickTimer = 0f;
        }
    }

    private void UpdateDerived()
        => MoveSpeedMultiplier = Has(StatusEffectLibrary.Haste) ? 1.6f : 1f;
}
