using System.Text;
using Godot;
using Dojo.Common;
using Dojo.Entities;

namespace Dojo.Stations;

/// <summary>
/// S09 · 数据驱动
///
/// 玩法：走过去砍假人。**但这一站真正的内容是"数值从哪来"。**
///
/// 三把武器（匕首 / 长剑 / 大剑）的**全部参数都写在 `.tres` 文件里**，
/// 代码里一个伤害数字都没有。左上角面板实时显示当前武器的每一项数值和理论 DPS。
///
/// 五个按键：
///   `1` `2` `3`  切换三把预设武器
///   `4`          随机生成一把武器（演示"配置驱动"能有多灵活）
///   `5`          **从磁盘重新加载当前 `.tres`** —— 这就是"热重载"
///
/// ★ 本站最想让你看到的一件事：
///   三把武器的**理论 DPS 几乎一样**（都在 33~38 之间），
///   但**手感完全是三个游戏**。
///   这说明"平衡"不等于"体验相同" —— 数值只能保证它们强度相当，
///   而"重不重、快不快、够不够远"是另一回事。
///   **算 DPS 是为了确认平衡，读 `.tres` 是为了调手感，两件事都要做。**
///
/// ★ 热重载怎么用（这是数据驱动最大的回报）：
///   用编辑器或记事本打开 `common/data/weapons/greatsword.tres`，
///   把 `Damage = 26` 改成 `Damage = 200`，保存，回到游戏里按 `5` ——
///   面板上的数字立刻变了，**不用重新编译，不用重启**。
///   这就是"迭代速度"：改一次数值从 30 秒变成 3 秒，你才会真的去调它。
/// </summary>
public partial class S09Data : StationBase
{
    public override string StationId => "s09_data";

    private static readonly Rect2 PlayArea = new(40, 130, 1200, 560);

    private const string DaggerPath = "res://common/data/weapons/dagger.tres";
    private const string LongswordPath = "res://common/data/weapons/longsword.tres";
    private const string GreatswordPath = "res://common/data/weapons/greatsword.tres";
    private const int PresetCount = 3;
    private const int HitsPerWeaponGoal = 5;

    private static readonly string[] PresetPaths = { DaggerPath, LongswordPath, GreatswordPath };
    private static readonly string[] PresetNames = { "匕首", "长剑", "大剑" };

    private Player _player = null!;
    private Node2D _swings = null!;
    private Node2D _numbers = null!;
    private PackedScene _swingScene = null!;
    private PackedScene _numberScene = null!;
    private CombatDummy[] _dummies = Array.Empty<CombatDummy>();
    private DamageNumberPool _pool = null!;
    private Label _panel = null!;
    private Line2D _aimLine = null!;

    private WeaponData _weapon = null!;

    /// <summary>
    /// ★ 三把预设的**缓存引用**。
    ///
    /// 这里踩过一个真实的坑：最初面板是在 `_Process` 里直接 `GD.Load` 去读这三把武器的，
    /// 也就是每秒 6 次资源加载。跑到 20 秒左右，Godot 的 C# 绑定直接抛
    /// `FATAL: Condition "gchandle.is_released()" is true` 崩掉了 ——
    /// 因为每次 Load 都会新建一个托管包装对象，高频调用把 GC 的终结器
    /// 和 Godot 的对象生命周期搅在了一起。
    ///
    /// **配置数据只在需要时加载一次，然后把引用留着。**
    /// 每帧去读（哪怕走缓存）都是错的 —— 它不只是慢，它会崩。
    /// </summary>
    private readonly WeaponData[] _presets = new WeaponData[PresetCount];

    private int _presetIndex = 1;          // 从"长剑"开始，它是基准
    private bool _usingRandom;
    private float _cooldown;

    /// <summary>每把"武器身份"命中过几次。用它来判定"你用两把不同的武器都打过"。</summary>
    private readonly Dictionary<string, int> _hitsByWeapon = new();
    private readonly bool[] _presetVisited = new bool[PresetCount];
    private bool _reloaded;
    private bool _randomHit;
    private bool _greatswordHit;
    private int _totalHits;
    private readonly bool[] _taskDone = new bool[5];

    protected override void StationReady()
    {
        _player = GetNode<Player>("Player");
        _swings = GetNode<Node2D>("Swings");
        _numbers = GetNode<Node2D>("Numbers");

        _swingScene = GD.Load<PackedScene>("res://src/stations/s09_data/weapon_swing.tscn")
                      ?? throw new InvalidOperationException("weapon_swing.tscn 加载失败");
        _numberScene = GD.Load<PackedScene>("res://common/combat/damage_number.tscn")
                       ?? throw new InvalidOperationException("damage_number.tscn 加载失败");

        _dummies = new[]
        {
            GetNode<CombatDummy>("Dummy1"),
            GetNode<CombatDummy>("Dummy2"),
            GetNode<CombatDummy>("Dummy3"),
        };

        _pool = new DamageNumberPool(_numbers, _numberScene);

        // 三把预设只在这里加载一次
        for (var i = 0; i < PresetCount; i++)
        {
            _presets[i] = GD.Load<WeaponData>(PresetPaths[i])
                          ?? throw new InvalidOperationException($"加载不到 {PresetPaths[i]}");
        }

        LevelKit.CreateBorderWalls(this, PlayArea);
        BuildLabels();
        BuildAimLine();
        EquipPreset(1);

        SetStatus("砍假人；按 1/2/3 换武器，按 4 随机生成，按 5 从磁盘热重载当前 .tres");
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
        if (@event.IsActionPressed("aux_1")) { EquipPreset(0); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_2")) { EquipPreset(1); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_3")) { EquipPreset(2); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_4")) { EquipRandom(); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_5")) { HotReload(); GetViewport().SetInputAsHandled(); return; }

        if (@event.IsActionPressed("attack"))
        {
            TrySwing();
            GetViewport().SetInputAsHandled();
            return;
        }

        base._UnhandledInput(@event);
    }

    // ---------- 武器切换 ----------

    private void EquipPreset(int index)
    {
        _presetIndex = Mathf.Clamp(index, 0, PresetCount - 1);
        _usingRandom = false;

        // 直接用缓存好的引用。Resource 的语义就是"同一个 .tres 各处拿到的是同一个对象"。
        _weapon = _presets[_presetIndex];

        _presetVisited[_presetIndex] = true;
        Flash($"装备：{_weapon.DisplayName}（伤害 {_weapon.Damage}，间隔 {_weapon.AttackInterval:0.00}s）");
    }

    /// <summary>
    /// 随机生成一把武器。
    /// **这就是"数据驱动"最直接的回报**：新增一把武器不需要写任何代码，
    /// 甚至不需要新建文件 —— 只要造一个对象、填上数据就行。
    /// </summary>
    private void EquipRandom()
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();

        _weapon = new WeaponData
        {
            DisplayName = $"随机武器 #{rng.RandiRange(100, 999)}",
            Description = "运行时生成的，磁盘上没有这个文件。",
            Damage = rng.RandiRange(4, 34),
            AttackInterval = rng.RandfRange(0.14f, 0.9f),
            Windup = rng.RandfRange(0.03f, 0.3f),
            Reach = rng.RandfRange(36f, 88f),
            ArcWidth = rng.RandfRange(28f, 90f),
            Knockback = rng.RandfRange(80f, 480f),
            CritChance = rng.RandfRange(0.05f, 0.4f),
            CritMultiplier = rng.RandfRange(1.5f, 3f),
            TintColor = Color.FromHsv(rng.Randf(), 0.55f, 1f, 0.32f),
        };

        _usingRandom = true;
        Flash($"随机生成：{_weapon.DisplayName}　伤害 {_weapon.Damage} / 间隔 {_weapon.AttackInterval:0.00}s / 距离 {_weapon.Reach:0}");
    }

    /// <summary>
    /// 从磁盘重新读，**忽略缓存**。
    /// 关键在于 `CacheMode.Replace`：默认加载走缓存，你改了文件也读不到新值。
    /// </summary>
    private void HotReload()
    {
        if (_usingRandom)
        {
            Flash("当前是运行时随机武器，磁盘上没有文件可重载 —— 先按 1/2/3 选一把预设");
            return;
        }

        var before = _weapon.Damage;
        var reloaded = WeaponData.ReloadFromDisk(PresetPaths[_presetIndex]);
        if (reloaded is null)
        {
            Flash($"重载失败：{PresetPaths[_presetIndex]}");
            return;
        }

        _weapon = reloaded;
        _presets[_presetIndex] = reloaded;   // 缓存也要换掉，否则面板还显示旧值
        _reloaded = true;

        Flash(before == reloaded.Damage
            ? $"已从磁盘重载「{reloaded.DisplayName}」—— 伤害还是 {reloaded.Damage}（文件没改？）"
            : $"已从磁盘重载「{reloaded.DisplayName}」—— 伤害 {before} → {reloaded.Damage}　不用重编译！", 3.0);
    }

    // ---------- 挥砍 ----------

    private void TrySwing()
    {
        if (_cooldown > 0f) return;
        _cooldown = _weapon.AttackInterval + _weapon.Windup;

        var direction = _player.AimDirection;

        var swing = _swingScene.Instantiate<WeaponSwing>();
        swing.Position = _player.Position + direction * (_weapon.Reach * 0.6f);
        swing.Rotation = direction.Angle();
        swing.Size = new Vector2(_weapon.Reach, _weapon.ArcWidth);
        swing.Tint = _weapon.TintColor;
        swing.Info = _weapon.ToDamageInfo(_player.GlobalPosition);
        swing.Hit += OnSwingHit;

        _swings.AddChild(swing);
    }

    private void OnSwingHit(DamageResult result, CombatDummy dummy)
    {
        dummy.PlayHitFeedback();
        _totalHits++;

        var key = _usingRandom ? "random" : PresetPaths[_presetIndex];
        _hitsByWeapon.TryGetValue(key, out var count);
        _hitsByWeapon[key] = count + 1;

        if (_usingRandom) _randomHit = true;
        if (!_usingRandom && _presetIndex == 2) _greatswordHit = true;

        var text = result.WasCrit ? $"{result.DisplayAmount}!" : result.DisplayAmount.ToString();
        var tint = _weapon.TintColor;
        var color = result.Killed ? new Color(1f, 0.5f, 0.38f)
            : result.WasCrit ? new Color(1f, 0.9f, 0.32f)
            : new Color(tint.R, tint.G, tint.B, 1f);
        _pool.Spawn(text, color, result.WasCrit || result.DisplayAmount >= 26, dummy.BarTopPosition);

        ScreenFx.Instance.Shake(Mathf.Clamp(result.DisplayAmount / 40f, 0.15f, 0.75f));
        ScreenFx.Instance.Hitstop(_weapon.Windup > 0.15f ? 0.07f : 0.035f);

        if (result.Killed) dummy.StartRespawn();
    }

    // ---------- 判定 ----------

    private void CheckGoals()
    {
        if (_greatswordHit) MarkOnce(0);
        if (_reloaded) MarkOnce(1);

        // 两把**不同的**武器各命中 5 次 —— 这条要求你真的去对比过
        var qualified = 0;
        foreach (var pair in _hitsByWeapon)
            if (pair.Value >= HitsPerWeaponGoal) qualified++;
        if (qualified >= 2) MarkOnce(2);

        if (_randomHit) MarkOnce(3);

        if (Array.TrueForAll(_presetVisited, visited => visited)) MarkOnce(4);

        if (!Array.TrueForAll(_taskDone, done => done)) return;

        Complete("三把预设都试过、用它打过人、还热重载过一次 —— 你现在知道「数据驱动」省的是什么了");
    }

    private void MarkOnce(int index)
    {
        if (_taskDone[index]) return;
        _taskDone[index] = true;
        MarkTaskDone(index);
    }

    // ---------- 界面 ----------

    private void BuildLabels()
    {
        _panel = LevelKit.MakeLabel(this, new Vector2(24, 58), "", 14,
            new Color(0.87f, 0.92f, 0.98f), HorizontalAlignment.Left, "WeaponPanel");
        _panel.Size = new Vector2(770, 420);
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
        var tint = _weapon.TintColor;
        _aimLine.DefaultColor = new Color(tint.R, tint.G, tint.B, 0.75f);
        // 指示线的长度就是这把武器的攻击距离 —— 换武器时一眼看出"够不够得着"
        _aimLine.Points = new[]
        {
            _player.Position + dir * 22f,
            _player.Position + dir * (_weapon.Reach * 0.75f),
        };
    }

    private void UpdatePanel()
    {
        var sb = new StringBuilder();

        sb.Append($"【当前武器】{_weapon.DisplayName}");
        if (_usingRandom) sb.Append("（运行时生成，磁盘上没有这个文件）");
        sb.Append('\n');
        sb.Append($"　{_weapon.Description}\n\n");

        sb.Append($"　伤害 {_weapon.Damage}　攻击间隔 {_weapon.AttackInterval:0.00}s　**理论 DPS {_weapon.Dps:0.0}**\n");
        sb.Append($"　前摇 {_weapon.Windup:0.00}s　距离 {_weapon.Reach:0}　范围 {_weapon.ArcWidth:0}　");
        sb.Append($"击退 {_weapon.Knockback:0}　暴击 {_weapon.CritChance * 100f:0}% × {_weapon.CritMultiplier:0.0}\n\n");

        sb.Append("【三把预设的 DPS 对比】");
        foreach (var w in _presets)
            sb.Append($"　{w.DisplayName} {w.Dps:0.0}");
        sb.Append("\n　→ DPS 几乎一样，但手感完全是三个游戏。**平衡 ≠ 体验相同。**\n\n");

        sb.Append("【按键】1 匕首　2 长剑　3 大剑　4 随机生成　5 从磁盘热重载当前 .tres\n");

        sb.Append("【命中次数】");
        for (var i = 0; i < PresetCount; i++)
        {
            _hitsByWeapon.TryGetValue(PresetPaths[i], out var n);
            sb.Append($"　{_presets[i].DisplayName} {n}");
        }
        _hitsByWeapon.TryGetValue("random", out var r);
        sb.Append($"　随机 {r}　（合计 {_totalHits}）");

        _panel.Text = sb.ToString();
    }
}
