using Godot;

namespace Dojo.Common;

/// <summary>
/// 「能挨打的东西」——生命值、护盾、无敌帧、击退，以及**整条伤害管线**。
///
/// 用法：作为一个子节点挂在角色身上，角色自己的 `_Process` 里不用管它（它自己跑）。
/// 攻击方拿到它，调 `Apply(info)`，读返回的 `DamageResult`。
///
/// ★ 管线顺序（本站的核心教学内容，顺序本身就是设计）：
///
///   ① 无敌帧检查 ── 在**最前面**。被免疫的一击不该消耗护盾、不该触发暴击、
///                   也不该产生击退 —— 它应该"完全没发生过"。
///                   如果放在后面，你会看到"无敌期间敌人还在掉护盾"这种诡异现象。
///   ② 暴击判定 ── 在护盾之前。因为暴击是"这一击有多重"，属于攻击属性；
///                 护盾是"承受了多少"，属于防御属性。**先算攻击，再算防御。**
///   ③ 护盾吸收 ── 先扣盾再扣血。注意"盾不够就溢出到血"，这是玩家最熟悉的规则。
///   ④ 生命扣减
///   ⑤ 击退      ── 用**攻击者→受击者**的方向，而不是"受击者当前朝向的反方向"。
///                   前者在玩家绕后攻击时依然正确，后者会把人往奇怪的方向推。
///   ⑥ 进入无敌帧 ── 在**最后**。它保护的是"接下来"的伤害，不是这一次。
///   ⑦ 死亡判定
///
/// 这条顺序不是唯一正确答案，但**它必须是一个明确的决定**，
/// 而不是"代码写到哪算哪"。绝大多数战斗 bug 都出在顺序上。
/// </summary>
public partial class Damageable : Node
{
    [Signal] public delegate void DamagedEventHandler();
    [Signal] public delegate void DiedEventHandler();

    [Export] public int MaxHp { get; set; } = 100;
    [Export] public int MaxShield { get; set; } = 30;

    /// <summary>受击后的无敌时间。0 = 不无敌。</summary>
    [Export] public float InvulnerableSeconds { get; set; } = 0.5f;

    /// <summary>
    /// 要往哪个物体上施加击退。由拥有者在 _Ready 里赋值。
    /// 不依赖 `GetParent()` 之类的结构位置 —— 场景层级一变就会静默取错节点。
    /// </summary>
    public CharacterBody2D? KnockbackActor { get; set; }

    public int Hp { get; private set; }
    public int Shield { get; private set; }
    public bool IsDead => Hp <= 0;

    public float IframeTimer { get; private set; }
    public bool IsInvulnerable => IframeTimer > 0f;

    /// <summary>最近一次命中的完整过程。站台的面板就是显示它。</summary>
    public DamageResult? LastResult { get; private set; }

    public int TotalHitsTaken { get; private set; }
    public int TotalDamageTaken { get; private set; }
    public int TotalBlockedByIframe { get; private set; }
    public int TotalCrits { get; private set; }

    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        _rng.Randomize();
        Reset();
    }

    public override void _Process(double delta)
    {
        if (IframeTimer > 0f) IframeTimer = Mathf.Max(0f, IframeTimer - (float)delta);
    }

    public void Reset()
    {
        Hp = MaxHp;
        Shield = MaxShield;
        IframeTimer = 0f;
        LastResult = null;
    }

    /// <summary>★ 整条伤害管线。顺序见类注释。</summary>
    public DamageResult Apply(DamageInfo info)
    {
        var result = new DamageResult { BaseDamage = info.BaseDamage };

        result.Add("① 命中判定", $"Hitbox ∩ Hurtbox —— 命中（基础伤害 {info.BaseDamage}）");

        // ---- ② 无敌帧：放在最前面，免疫就是"完全没发生过" ----
        if (CombatRules.IframeEnabled && IframeTimer > 0f)
        {
            result.BlockedByIFrame = true;
            result.HpRemaining = Hp;
            result.ShieldRemaining = Shield;
            result.Add("② 无敌帧", $"剩余 {IframeTimer:0.00}s —— 这一击被完全免疫", applied: false);
            result.Add("③④⑤⑥", "被免疫的一击不消耗护盾、不触发暴击、不产生击退", applied: false);

            TotalBlockedByIframe++;
            LastResult = result;
            return result;
        }

        result.Add("② 无敌帧", CombatRules.IframeEnabled ? "不在无敌中，继续结算" : "无敌帧已关闭", CombatRules.IframeEnabled);

        // ---- ③ 暴击：属于"攻击属性"，先算 ----
        var damage = info.BaseDamage;
        if (CombatRules.CritEnabled && _rng.Randf() < info.CritChance)
        {
            result.WasCrit = true;
            damage = Mathf.RoundToInt(damage * info.CritMultiplier);
            TotalCrits++;
            result.Add("③ 暴击判定", $"{info.CritChance * 100f:0.#}% 命中 → ×{info.CritMultiplier:0.#} → {damage}");
        }
        else
        {
            result.Add("③ 暴击判定", CombatRules.CritEnabled ? $"{info.CritChance * 100f:0.#}% 未命中 → {damage}" : "暴击已关闭", CombatRules.CritEnabled);
        }

        // ---- ④ 护盾：先扣盾，盾不够才溢出到血 ----
        if (CombatRules.ShieldEnabled && Shield > 0)
        {
            var absorbed = Mathf.Min(Shield, damage);
            Shield -= absorbed;
            damage -= absorbed;
            result.ShieldAbsorbed = absorbed;
            result.Add("④ 护盾吸收", $"护盾 {Shield + absorbed} → {Shield}（吸收 {absorbed}），剩余伤害 {damage}");
        }
        else
        {
            result.Add("④ 护盾吸收", CombatRules.ShieldEnabled ? "护盾已空，直接扣血" : "护盾已关闭", CombatRules.ShieldEnabled && Shield > 0);
        }

        // ---- ⑤ 生命 ----
        var hpLost = Mathf.Min(Hp, Mathf.Max(0, damage));
        Hp -= hpLost;
        result.HpLost = hpLost;
        result.HpRemaining = Hp;
        result.ShieldRemaining = Shield;
        result.Add("⑤ 生命扣减", $"HP {Hp + hpLost} → {Hp}（−{hpLost}）");

        // ---- ⑥ 击退：方向 = 攻击者 → 受击者 ----
        // 注意 Damageable 是 Node（不是 Node2D），它自己没有坐标 ——
        // 位置要从它服务的那个角色身上取。
        var selfPosition = KnockbackActor is not null ? KnockbackActor.GlobalPosition : Vector2.Zero;
        var direction = selfPosition - info.SourcePosition;
        direction = direction.LengthSquared() < 1f ? Vector2.Right : direction.Normalized();

        if (CombatRules.KnockbackEnabled && info.Knockback > 0f)
        {
            result.Knockback = direction * info.Knockback;
            if (KnockbackActor is not null) KnockbackActor.Velocity = result.Knockback;
            result.Add("⑥ 击退", $"力度 {info.Knockback:0}，方向 ({direction.X:0.00}, {direction.Y:0.00})");
        }
        else
        {
            result.Add("⑥ 击退", CombatRules.KnockbackEnabled ? "本次攻击没有击退" : "击退已关闭", CombatRules.KnockbackEnabled && info.Knockback > 0f);
        }

        // ---- ⑦ 进入无敌帧：保护的是"接下来"的伤害 ----
        if (CombatRules.IframeEnabled && InvulnerableSeconds > 0f)
        {
            IframeTimer = InvulnerableSeconds;
            result.Add("⑦ 进入无敌帧", $"接下来 {InvulnerableSeconds:0.00}s 内免疫一切伤害");
        }
        else
        {
            result.Add("⑦ 进入无敌帧", CombatRules.IframeEnabled ? "本角色没有无敌帧" : "无敌帧已关闭", false);
        }

        // ---- ⑧ 死亡 ----
        result.Killed = Hp <= 0;
        if (result.Killed) result.Add("⑧ 死亡判定", $"HP ≤ 0 —— 死亡");

        TotalHitsTaken++;
        TotalDamageTaken += result.ShieldAbsorbed + result.HpLost;
        LastResult = result;

        EmitSignal(SignalName.Damaged);
        if (result.Killed) EmitSignal(SignalName.Died);

        return result;
    }

    public void ResetStats()
    {
        TotalHitsTaken = 0;
        TotalDamageTaken = 0;
        TotalBlockedByIframe = 0;
        TotalCrits = 0;
    }
}
