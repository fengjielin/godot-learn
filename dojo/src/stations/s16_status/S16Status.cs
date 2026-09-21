using System.Text;
using Godot;
using Dojo.Common;
using Dojo.Entities;

namespace Dojo.Stations;

/// <summary>
/// S16 · 状态效果
///
/// 地上四个药水台：加速 / 中毒 / 无敌 / 回血。**站上去就会持续施加**，
/// 于是你马上会遇到那个核心问题：**同名效果再次施加时该怎么办？**
///
/// 本站的答案是三条规则，各自对应一类效果：
///   · 加速、无敌、回血 → **刷新时长**（不会更强，只是重置倒计时）
///   · 中毒            → **叠层**（每层独立计时，最多 5 层，层数越多跳得越频繁）
///   · 每个效果还有 **冷却** —— 否则站在台上就能无限刷，前两条规则就没意义了
///
/// 面板会把**每一次施加**的判定过程摊开：免疫检查 → 冷却检查 → 叠加规则。
/// 这和 S07 的伤害管线是同一套思路：**把过程留下来，你才不用猜。**
///
/// 另外两个容易忽略的细节：
///   ① **无敌也要免疫中毒** —— 否则"无敌"就不成立了。这条规则写在容器的施加逻辑里。
///   ② 中毒层数满了之后怎么办？本站是"移除最早一层、补上新层"。
///      也可以选择"直接拒绝"或"刷新全部层"，**三者手感完全不同，必须明确决定。**
/// </summary>
public partial class S16Status : StationBase
{
    public override string StationId => "s16_status";

    private static readonly Rect2 PlayArea = new(40, 130, 1200, 560);

    private static readonly Vector2[] PadPositions =
    {
        new(320, 260),   // 加速
        new(580, 260),   // 中毒
        new(320, 570),   // 无敌
        new(580, 570),   // 回血
    };

    private const float PadRadius = 58f;
    private const float PadRetryInterval = 0.22f;
    private const int PoisonStackGoal = 5;

    private Player _player = null!;
    private Node2D _padsRoot = null!;
    private CanvasLayer _iconLayer = null!;
    private Damageable _health = null!;
    private StatusEffects _effects = null!;
    private Label _panel = null!;

    private readonly Dictionary<string, Line2D> _rings = new();
    private readonly Dictionary<string, Label> _ringLabels = new();
    private readonly Dictionary<string, Tween> _ringTweens = new();

    private StatusApplyResult? _lastResult;
    private float _padRetryTimer;
    private int _hp;
    private int _maxHp = 220;   // 见 StatusEffects.TickPeriodicDamage 的注释：
                                // 演示"叠满 5 层"需要玩家活得够久，所以血条给宽一点
    private int _poisonTicks;
    private int _heals;
    private readonly HashSet<string> _everApplied = new();
    private bool _sawRefresh;
    private bool _sawCooldown;
    private bool _sawImmune;
    private bool _reachedMaxPoison;
    private bool _ringsShown;
    private readonly bool[] _taskDone = new bool[5];

    protected override void StationReady()
    {
        _player = GetNode<Player>("Player");
        _padsRoot = GetNode<Node2D>("Pads");
        _iconLayer = GetNode<CanvasLayer>("IconLayer");
        _hp = _maxHp;

        // 运行时给玩家挂一个 Damageable 和 StatusEffects ——
        // **组件可以运行时挂载**，不必写进玩家场景。
        // 这样别的练习站不需要为"本站才用得到的东西"买单。
        _health = new Damageable { MaxHp = _maxHp, MaxShield = 0, InvulnerableSeconds = 0f };
        _player.AddChild(_health);

        _effects = new StatusEffects();
        _player.AddChild(_effects);
        _effects.DamageTick += OnPoisonTick;
        _effects.HealTick += OnHealTick;
        _effects.EffectApplied += OnEffectApplied;

        LevelKit.CreateBorderWalls(this, PlayArea);
        BuildPads();
        BuildIcons();
        BuildLabels();

        SetStatus("站到药水台上就会持续施加；注意每次施加被「免疫 / 冷却 / 叠加规则」哪一环拦下");
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _padRetryTimer -= dt;

        // 玩家移动速度受"加速"影响
        _player.MaxSpeed = 235f * _effects.MoveSpeedMultiplier;

        if (_padRetryTimer <= 0f) TryApplyFromPad();
        UpdateIcons(dt);
        UpdatePanel();
        CheckGoals();
    }

    // ---------- 药水台 ----------

    private void BuildPads()
    {
        for (var i = 0; i < StatusEffectLibrary.All.Length; i++)
        {
            var def = StatusEffectLibrary.All[i];

            var pad = new Area2D
            {
                Name = "Pad_" + def.Id,
                Position = PadPositions[i],
                CollisionLayer = GameLayers.Interactable,
                CollisionMask = GameLayers.Player,
                Monitorable = false,
            };
            pad.AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = PadRadius } });
            _padsRoot.AddChild(pad);

            LevelKit.MakeCircle(pad, Vector2.Zero, PadRadius - 6f, def.Color.Darkened(0.62f), 32, "Visual");
            LevelKit.MakeLabel(pad, new Vector2(0, -96), def.DisplayName, 16, def.Color);
            LevelKit.MakeLabel(pad, new Vector2(0, 74), def.Description, 12, new Color(0.66f, 0.72f, 0.85f));
        }
    }

    private void TryApplyFromPad()
    {
        for (var i = 0; i < StatusEffectLibrary.All.Length; i++)
        {
            var def = StatusEffectLibrary.All[i];
            if (_player.GlobalPosition.DistanceTo(PadPositions[i]) > PadRadius) continue;

            _padRetryTimer = PadRetryInterval;
            _lastResult = _effects.Apply(def.Id);
            RecordResult(_lastResult);
            return;
        }
    }

    private void RecordResult(StatusApplyResult result)
    {
        if (result.Applied) _everApplied.Add(result.EffectId);

        foreach (var step in result.Steps)
        {
            if (!step.Applied && step.Detail.Contains("冷却中")) _sawCooldown = true;
            if (!step.Applied && step.Detail.Contains("免疫")) _sawImmune = true;
            if (step.Applied && step.Detail.Contains("刷新时长")) _sawRefresh = true;
        }

        if (result.EffectId == StatusEffectLibrary.Poison &&
            _effects.StackCountOf(StatusEffectLibrary.Poison) >= PoisonStackGoal)
            _reachedMaxPoison = true;
    }

    // ---------- 生命 ----------

    private void OnPoisonTick(int damage)
    {
        _poisonTicks++;
        _hp = Mathf.Max(0, _hp - damage);
        ScreenFx.Instance.Shake(0.18f);
        ScreenFx.Instance.Flash(new Color(0.55f, 0.85f, 0.4f), 0.16f, 0.12f);

        if (_hp > 0) return;
        _hp = _maxHp;
        _effects.Clear();
        _player.GlobalPosition = new Vector2(140, 620);
        Flash("中毒身亡 —— 已送回起点并清空所有效果", 2.4);
    }

    private void OnHealTick(int amount)
    {
        _heals++;
        _hp = Mathf.Min(_maxHp, _hp + amount);
    }

    private void OnEffectApplied(string id)
    {
        // 每次成功施加都重启这个效果的倒计时环 —— **用 Tween 驱动一个"不是属性"的东西**
        var def = StatusEffectLibrary.Get(id);
        if (!_ringTweens.TryGetValue(id, out var tween) || tween is null) return;

        tween.Kill();
        tween = CreateTween();
        tween.TweenMethod(Callable.From<float>(t => SetRing(id, t)), 0f, 1f, def.Duration);
        _ringTweens[id] = tween;
    }

    // ---------- 图标与倒计时环 ----------

    private void BuildIcons()
    {
        var x = 24f;
        foreach (var def in StatusEffectLibrary.All)
        {
            var holder = new Node2D { Name = "Icon_" + def.Id, Position = new Vector2(x + 34f, 470f) };
            _iconLayer.AddChild(holder);

            LevelKit.MakeCircle(holder, Vector2.Zero, 30f, def.Color.Darkened(0.7f), 24, "Disc");

            // 环用 Line2D 画：它是一个**圆弧**，点数由 progress 决定。
            // 用 Tween 的 TweenMethod 去驱动它 —— 这正是 Tween 的用法之一：
            // **不只是补间属性，也可以补间"任意一个函数调用"。**
            var ring = new Line2D
            {
                Name = "Ring",
                Width = 4f,
                Closed = false,
                Antialiased = true,
                DefaultColor = def.Color,
                Points = BuildArc(1f, 34f),
            };
            holder.AddChild(ring);
            _rings[def.Id] = ring;

            var label = LevelKit.MakeLabel(holder, new Vector2(0, 48), def.DisplayName, 13, def.Color);
            label.Size = new Vector2(140, 24);
            _ringLabels[def.Id] = label;

            x += 150f;
        }
    }

    private static Vector2[] BuildArc(float progress, float radius, int segments = 28)
    {
        var count = Mathf.Max(2, Mathf.RoundToInt(segments * Mathf.Clamp(progress, 0.02f, 1f)));
        var points = new Vector2[count + 1];
        for (var i = 0; i <= count; i++)
        {
            var angle = Mathf.Tau * (i / (float)count) - Mathf.Pi / 2f;
            points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        return points;
    }

    private void SetRing(string id, float t)
    {
        if (!_rings.TryGetValue(id, out var ring)) return;
        ring.Points = BuildArc(1f - t, 34f);
    }

    private void UpdateIcons(float dt)
    {
        foreach (var def in StatusEffectLibrary.All)
        {
            var view = _effects.Find(def.Id);
            var has = view is not null;
            var holder = _rings[def.Id].GetParent<Node2D>();
            holder.Visible = has;

            if (!has)
            {
                if (_ringTweens.TryGetValue(def.Id, out var t) && t is not null) { t.Kill(); _ringTweens[def.Id] = null!; }
                continue;
            }

            var stacks = view!.Stacks;
            var remaining = view.LongestRemaining;
            _ringsShown = true;   // 只要有一个环被画出来，就说明 Tween 那条路走通了
            _ringLabels[def.Id].Text = stacks > 1
                ? $"{def.DisplayName} ×{stacks}  {remaining:0.0}s"
                : $"{def.DisplayName}  {remaining:0.0}s";
        }
    }

    // ---------- 判定与界面 ----------

    private void CheckGoals()
    {
        // 注意这里的下标必须和 StationCatalog 里 S16 的任务顺序**一一对应**：
        //   0 加冷却 · 1 实现叠层 · 2 定义叠加规则 · 3 Tween 倒计时环 · 4 加免疫
        // （第一次写的时候我按"实现顺序"标了下标，结果打勾打到了隔壁那一格上 ——
        //   任务列表和判定逻辑是两个地方，**改一处就要回头核一遍另一处**。）
        if (_sawCooldown) MarkOnce(0);
        if (_reachedMaxPoison) MarkOnce(1);
        if (_sawRefresh) MarkOnce(2);
        if (_ringsShown) MarkOnce(3);
        if (_sawImmune) MarkOnce(4);

        if (!Array.TrueForAll(_taskDone, done => done)) return;

        Complete("叠层、刷新、冷却、免疫、Tween 倒计时环 —— 你现在知道「同名效果再来一次」有多少种答案了");
    }

    private void MarkOnce(int index)
    {
        if (_taskDone[index]) return;
        _taskDone[index] = true;
        MarkTaskDone(index);
    }

    private void BuildLabels()
    {
        _panel = LevelKit.MakeLabel(this, new Vector2(24, 58), "", 14,
            new Color(0.87f, 0.92f, 0.98f), HorizontalAlignment.Left, "StatusPanel");
        _panel.Size = new Vector2(770, 420);
    }

    private void UpdatePanel()
    {
        var sb = new StringBuilder();

        sb.Append("【当前效果】");
        var snapshot = _effects.Snapshot();
        if (snapshot.Count == 0) sb.Append("（无）");
        else
            foreach (var view in snapshot)
                sb.Append($"　{view.Def.DisplayName} ×{view.Stacks} {view.LongestRemaining:0.0}s");
        sb.Append('\n');

        sb.Append($"【生命】{_hp}/{_maxHp}　移动速度 ×{_effects.MoveSpeedMultiplier:0.00}");
        if (_effects.IsInvulnerable) sb.Append("　★ 无敌中（免疫一切伤害，也免疫中毒）");
        sb.Append('\n');
        sb.Append($"【统计】中毒跳伤 {_poisonTicks} 次　回血 {_heals} 次\n\n");

        sb.Append("【最近一次施加】");
        if (_lastResult is null) sb.Append("（还没站上过药水台）\n");
        else
        {
            sb.Append($"{StatusEffectLibrary.Get(_lastResult.EffectId).DisplayName}\n");
            foreach (var step in _lastResult.Steps)
                sb.Append($"   {(step.Applied ? "▶" : "·")} {step.Name}　{step.Detail}\n");
            sb.Append($"   结果：{_lastResult.Outcome}\n");
        }

        sb.Append('\n');
        sb.Append("【规则速查】加速/无敌/回血 = 刷新时长　·　中毒 = 叠层（最多 5 层，每层独立计时）\n");
        sb.Append("　　　　　　每个效果都有冷却 —— 否则站在台上就能无限刷，前两条规则就没意义了");

        _panel.Text = sb.ToString();
    }
}
