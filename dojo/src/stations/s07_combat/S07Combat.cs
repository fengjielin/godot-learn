using System.Text;
using Godot;
using Dojo.Common;
using Dojo.Entities;

namespace Dojo.Stations;

/// <summary>
/// S07 · 战斗与数值管线
///
/// 玩法很简单：走到假人旁边按 `J` 砍它。真正的内容在**左上角那块面板**上 ——
/// 它把最近一次命中的**每一环**都摊开给你看：
///
///   ① 命中判定 → ② 无敌帧 → ③ 暴击 → ④ 护盾吸收 → ⑤ 生命扣减 → ⑥ 击退 → ⑦ 进入无敌帧 → ⑧ 死亡
///
/// 按 `1`~`5` 可以随时把某一环关掉（护盾 / 暴击 / 无敌帧 / 击退 / 飘字池），
/// 关掉之后再打一次，对比面板上少了哪一行、少了多少伤害。
///
/// **为什么值得把管线画出来**：战斗最烦的 bug 不是"数值不对"，
/// 而是"**为什么这次是 24，那次是 12**"。只留下最终数字的话你只能靠猜；
/// 把每一环的输入输出都记下来，答案就在眼前。
/// 这和 S04 状态机那块面板是同一个思路：**能观测，才能调。**
/// </summary>
public partial class S07Combat : StationBase
{
    public override string StationId => "s07_combat";

    private static readonly Rect2 PlayArea = new(40, 130, 1200, 560);

    private const int BaseDamage = 12;
    private const float CritChance = 0.15f;
    private const float CritMultiplier = 2f;

    /// <summary>
    /// 击退力度。**这个数字要按"玩家跟不跟得上"来定，不是越大越爽。**
    /// 一开始设 260，结果每一击都把假人推出攻击范围，43 次挥砍只命中 3 次 ——
    /// 演示完全进行不下去。降到 200 之后玩家可以边打边跟，
    /// 击退依然是"能感觉到"的，但不会把目标永久推开。
    /// </summary>
    private const float Knockback = 200f;

    private const float SwingCooldown = 0.26f;
    private const int PoolGoal = 20;

    private static readonly Color ShieldColor = new(0.5f, 0.8f, 1f);
    private static readonly Color NormalColor = new(1f, 1f, 1f);
    private static readonly Color CritColor = new(1f, 0.9f, 0.32f);
    private static readonly Color ImmuneColor = new(0.66f, 0.7f, 0.78f);
    private static readonly Color KillColor = new(1f, 0.5f, 0.38f);

    private Player _player = null!;
    private Node2D _swings = null!;
    private Node2D _numbers = null!;
    private PackedScene _swingScene = null!;
    private PackedScene _numberScene = null!;
    private CombatDummy[] _dummies = Array.Empty<CombatDummy>();
    private DamageNumberPool _pool = null!;
    private Label _panel = null!;
    private Line2D _aimLine = null!;

    private float _cooldown;
    private int _swingsMade;
    private int _hits;
    private int _directSpawns;
    private bool _sawShield;
    private bool _sawCrit;
    private bool _sawIframe;
    private bool _sawKnockback;
    private readonly bool[] _taskDone = new bool[5];

    protected override void StationReady()
    {
        _player = GetNode<Player>("Player");
        _swings = GetNode<Node2D>("Swings");
        _numbers = GetNode<Node2D>("Numbers");

        _swingScene = GD.Load<PackedScene>("res://src/stations/s07_combat/melee_swing.tscn")
                      ?? throw new InvalidOperationException("melee_swing.tscn 加载失败");
        _numberScene = GD.Load<PackedScene>("res://common/combat/damage_number.tscn")
                       ?? throw new InvalidOperationException("damage_number.tscn 加载失败");

        _dummies = new[]
        {
            GetNode<CombatDummy>("Dummy1"),
            GetNode<CombatDummy>("Dummy2"),
            GetNode<CombatDummy>("Dummy3"),
        };

        _pool = new DamageNumberPool(_numbers, _numberScene);

        LevelKit.CreateBorderWalls(this, PlayArea);
        BuildLabels();
        BuildAimLine();

        SetStatus("走到假人旁边按 J 砍它，看左上角面板里那条管线是怎么一环一环走完的");
    }

    public override void _Process(double delta)
    {
        if (_cooldown > 0f) _cooldown = Mathf.Max(0f, _cooldown - (float)delta);
        UpdatePanel();
        UpdateAimLine();
        CheckGoals();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("aux_1")) { CombatRules.ShieldEnabled = !CombatRules.ShieldEnabled; FlashToggle("护盾", CombatRules.ShieldEnabled); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_2")) { CombatRules.CritEnabled = !CombatRules.CritEnabled; FlashToggle("暴击", CombatRules.CritEnabled); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_3")) { CombatRules.IframeEnabled = !CombatRules.IframeEnabled; FlashToggle("无敌帧", CombatRules.IframeEnabled); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_4")) { CombatRules.KnockbackEnabled = !CombatRules.KnockbackEnabled; FlashToggle("击退", CombatRules.KnockbackEnabled); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_5")) { CombatRules.DamageNumberPoolEnabled = !CombatRules.DamageNumberPoolEnabled; FlashToggle("飘字对象池", CombatRules.DamageNumberPoolEnabled); GetViewport().SetInputAsHandled(); return; }

        if (@event.IsActionPressed("attack"))
        {
            TrySwing();
            GetViewport().SetInputAsHandled();
            return;
        }

        base._UnhandledInput(@event);
    }

    // ---------- 挥砍 ----------

    private void TrySwing()
    {
        if (_cooldown > 0f) return; // 冷却：防止一帧连砍，也让"无敌帧"的演示更容易观察
        _cooldown = SwingCooldown;
        _swingsMade++;

        var direction = _player.AimDirection;

        var swing = _swingScene.Instantiate<MeleeSwing>();
        swing.Position = _player.Position + direction * 52f;
        swing.Rotation = direction.Angle();
        swing.Info = new DamageInfo
        {
            BaseDamage = BaseDamage,
            CritChance = CritChance,
            CritMultiplier = CritMultiplier,
            Knockback = Knockback,
            SourcePosition = _player.GlobalPosition,
        };
        swing.Hit += OnSwingHit;

        _swings.AddChild(swing);
    }

    /// <summary>一次挥砍命中了某个目标。**这里不计算伤害 —— 伤害由 Damageable 的管线算完返回。**</summary>
    private void OnSwingHit(DamageResult result, CombatDummy dummy)
    {
        _hits++;
        dummy.PlayHitFeedback();

        if (result.BlockedByIFrame)
        {
            _sawIframe = true;
            ShowNumber("免疫", ImmuneColor, big: false, dummy.BarTopPosition);
            ScreenFx.Instance.Shake(0.12f);
            return;
        }

        if (result.ShieldAbsorbed > 0) _sawShield = true;
        if (result.WasCrit) _sawCrit = true;
        if (result.Knockback != Vector2.Zero) _sawKnockback = true;

        var text = result.WasCrit ? $"{result.DisplayAmount}!" : result.DisplayAmount.ToString();
        var color = result.Killed ? KillColor
            : result.WasCrit ? CritColor
            : result.HpLost == 0 ? ShieldColor  // 只打掉护盾：用盾的颜色，一眼看出"没掉血"
            : NormalColor;

        ShowNumber(text, color, result.WasCrit, dummy.BarTopPosition);

        // 打击感三件套（S05 的公共设施）
        ScreenFx.Instance.Shake(result.WasCrit ? 0.6f : 0.35f);
        ScreenFx.Instance.Hitstop(0.045f);
        ScreenFx.Instance.Flash(result.WasCrit ? CritColor : new Color(1f, 0.9f, 0.8f), 0.18f, 0.1f);

        if (result.Killed) dummy.StartRespawn();
    }

    private void ShowNumber(string text, Color color, bool big, Vector2 position)
    {
        if (CombatRules.DamageNumberPoolEnabled)
        {
            _pool.Spawn(text, color, big, position);
            return;
        }

        // 对照实验：不走对象池，每次新建、用完自己销毁。
        // 按 5 切换，然后对比面板上的"创建 / 播放 / 省下"三个数字。
        var fresh = _numberScene.Instantiate<DamageNumber>();
        fresh.AutoFreeOnRelease = true;
        _numbers.AddChild(fresh);
        fresh.Play(text, color, big, position);
        _directSpawns++;
    }

    // ---------- 判定与界面 ----------

    private void CheckGoals()
    {
        if (_sawShield) MarkOnce(0);
        if (_sawCrit) MarkOnce(1);
        if (_sawIframe) MarkOnce(2);
        if (_sawKnockback) MarkOnce(3);
        if (_hits >= PoolGoal) MarkOnce(4);

        if (!Array.TrueForAll(_taskDone, done => done)) return;

        Complete("护盾、暴击、无敌帧、击退、飘字池 —— 五个环节你都亲眼见过它们生效了");
    }

    private void MarkOnce(int index)
    {
        if (_taskDone[index]) return;
        _taskDone[index] = true;
        MarkTaskDone(index);
    }

    private void FlashToggle(string name, bool on) => Flash($"{name}：{(on ? "开" : "关")}");

    private void BuildLabels()
    {
        _panel = LevelKit.MakeLabel(this, new Vector2(24, 58), "", 14,
            new Color(0.87f, 0.92f, 0.98f), HorizontalAlignment.Left, "PipelinePanel");
        _panel.Size = new Vector2(768, 420);
    }

    private void BuildAimLine()
    {
        _aimLine = new Line2D
        {
            Name = "AimLine",
            Width = 2.5f,
            DefaultColor = new Color(0.95f, 0.88f, 0.62f, 0.7f),
            Antialiased = true,
        };
        AddChild(_aimLine);
    }

    private void UpdateAimLine()
    {
        var dir = _player.AimDirection;
        _aimLine.Points = new[]
        {
            _player.Position + dir * 22f,
            _player.Position + dir * 74f,
        };
    }

    private void UpdatePanel()
    {
        var sb = new StringBuilder();

        // ---- 管线：取"最近一次"----
        // 用一个简单的启发式：谁的总命中数最大就更可能刚被打过。
        var newest = _dummies.OrderByDescending(d => d.Health.TotalHitsTaken).FirstOrDefault();
        var last = newest?.Health.LastResult;

        sb.Append("【伤害管线】最近一次命中：");
        if (last is null)
        {
            sb.Append("（还没打过 —— 走到假人旁边按 J）\n");
        }
        else
        {
            sb.Append('\n');
            foreach (var step in last.Steps)
                sb.Append($"   {(step.Applied ? "▶" : "·")} {step.Name}　{step.Detail}\n");
        }

        sb.Append('\n');
        sb.Append($"【开关】1 护盾[{OnOff(CombatRules.ShieldEnabled)}]　2 暴击[{OnOff(CombatRules.CritEnabled)}]　" +
                  $"3 无敌帧[{OnOff(CombatRules.IframeEnabled)}]　4 击退[{OnOff(CombatRules.KnockbackEnabled)}]　" +
                  $"5 飘字池[{OnOff(CombatRules.DamageNumberPoolEnabled)}]\n");

        sb.Append($"【飘字池】创建 {_pool.TotalCreated} 个实例，走池 {_pool.TotalPlayed} 次，" +
                  $"省下 {_pool.SavedInstantiations} 次 Instantiate（峰值同时 {_pool.PeakConcurrent} 条）");
        if (_directSpawns > 0) sb.Append($"　｜　关池后另建了 {_directSpawns} 个临时实例");
        sb.Append('\n');

        var blocked = 0;
        var crits = 0;
        foreach (var dummy in _dummies)
        {
            blocked += dummy.Health.TotalBlockedByIframe;
            crits += dummy.Health.TotalCrits;
        }

        sb.Append($"【统计】挥砍 {_swingsMade} 次　命中 {_hits} 次　暴击 {crits} 次　被无敌帧免疫 {blocked} 次");

        _panel.Text = sb.ToString();
    }

    private static string OnOff(bool value) => value ? "开" : "关";
}
