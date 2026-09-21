using Godot;
using Dojo.Common;
using Dojo.Entities;

namespace Dojo.Stations;

/// <summary>
/// S03 · 物理与碰撞
///
/// 一块场地上有四个实验台，每个对应一个「碰撞」的核心概念。四个目标全部达成就通关。
///
///   A 射击台   —— **碰撞层与掩码**：子弹的掩码可以按 1/2/3/4 切换，行为随之完全改变
///   B 推箱子   —— CharacterBody2D 与 RigidBody2D 的交互（推开箱子必须手动施力）
///   C 视线台   —— RayCast2D：两点之间有没有被挡住
///   D 毒池     —— Area2D 的进入/离开信号，以及"持续在区域内"的判定
///
/// 这一站最想让你记住的一句话：
///   **层（layer）是"我是什么"，掩码（mask）是"我要检测什么"。**
///   搞混这两个，碰撞就永远不触发，而且报错信息里什么都不会说 —— 它只是"不发生"。
/// </summary>
public partial class S03Physics : StationBase
{
    public override string StationId => "s03_physics";

    private static readonly Rect2 PlayArea = new(40, 130, 1200, 570);
    private static readonly Vector2 SpawnPoint = new(110, 300);
    private static readonly Vector2 PushPadCenter = new(1140, 300);
    private static readonly Vector2 HealPadCenter = new(1180, 470);
    private static readonly Rect2 SightZone = new(40, 420, 580, 280);

    private const float HideGoalSeconds = 3.0f;
    private const int HurtGoalTicks = 5;
    private const int MaxHp = 100;
    private const int HazardDamage = 12;
    private const float HazardTickInterval = 0.45f;

    /// <summary>
    /// 子弹的四种碰撞掩码预设 —— 本站的核心教具。
    /// 注意 layer 是固定的（子弹永远在 Projectile 层），变的只有 mask。
    /// </summary>
    private static readonly (string Name, uint Mask, string Hint)[] Presets =
    {
        ("只检测敌人", GameLayers.Enemy,
            "子弹会直接穿过墙，命中墙后的靶子"),
        ("只检测墙壁", GameLayers.World,
            "子弹被墙挡下；正对靶子开枪也打不到它"),
        ("墙壁 + 敌人", GameLayers.Enemy | GameLayers.World,
            "先撞到谁就停在谁身上 —— 这是「正常」的子弹"),
        ("什么都不检测", 0u,
            "一路飞过去，墙和靶子都当它不存在"),
    };

    private Player _player = null!;
    private PackedScene _bulletScene = null!;
    private Turret _turret = null!;
    private Target _targetA = null!;
    private Target _targetB = null!;
    private RigidBody2D _box = null!;
    private Area2D _hazard = null!;
    private Polygon2D _hazardVisual = null!;
    private Label _presetLabel = null!;
    private Line2D _aimLine = null!;

    private int _preset;
    private readonly bool[] _presetUsed = new bool[4];
    private int _shotsFired;
    private int _hp = MaxHp;
    private int _hazardTicks;
    private int _deaths;
    private double _hazardTimer;
    private double _healAccumulator;
    private double _pulse;
    private float _hideSeconds;
    private bool _shootGoal;
    private bool _pushGoal;
    private bool _hideGoal;
    private bool _hurtGoal;

    protected override void StationReady()
    {
        _player = GetNode<Player>("Player");
        _bulletScene = GD.Load<PackedScene>("res://src/stations/s03_physics/bullet.tscn")
                       ?? throw new InvalidOperationException("bullet.tscn 加载失败");

        _turret = GetNode<Turret>("Turret");
        _turret.TargetNode = _player;

        _targetA = GetNode<Target>("TargetA");
        _targetB = GetNode<Target>("TargetB");

        _box = GetNode<RigidBody2D>("PushBox");

        _hazard = GetNode<Area2D>("Hazard");
        _hazardVisual = GetNode<Polygon2D>("Hazard/Visual");
        _hazard.BodyEntered += OnHazardEntered;
        _hazard.BodyExited += OnHazardExited;

        LevelKit.CreateBorderWalls(this, PlayArea);
        BuildLabels();
        BuildAimIndicator();
        ApplyPreset(0);

        SetStatus("四个实验台各完成一个目标即通关");
    }

    public override void _Process(double delta)
    {
        TickHazard(delta);
        TickHealPad(delta);
        TickSightZone(delta);
        CheckGoals();
        UpdateAimIndicator();

        // 毒池呼吸效果：让"这里危险"一眼可见
        _pulse += delta;
        _hazardVisual.Color = new Color(0.4f, 0.18f, 0.22f, 0.75f + 0.25f * Mathf.Sin((float)_pulse * 2.4f));

        UpdatePanel();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("aux_1")) { ApplyPreset(0); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_2")) { ApplyPreset(1); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_3")) { ApplyPreset(2); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_4")) { ApplyPreset(3); GetViewport().SetInputAsHandled(); return; }

        if (@event.IsActionPressed("attack"))
        {
            Fire();
            GetViewport().SetInputAsHandled();
            return;
        }

        base._UnhandledInput(@event);
    }

    // ---------- A 射击台 ----------

    private void ApplyPreset(int index)
    {
        _preset = Mathf.Clamp(index, 0, Presets.Length - 1);
        _presetUsed[_preset] = true;

        var p = Presets[_preset];
        Flash($"子弹掩码 → {p.Name}：{p.Hint}");

        // 四种组合都试过 = 你至少把"掩码"的极端情况都见过了
        if (Array.TrueForAll(_presetUsed, used => used))
        {
            MarkTaskDone(4);
        }
    }

    private void Fire()
    {
        var bullet = _bulletScene.Instantiate<Bullet>();
        // 瞄准方向由共享的 Player 提供（动过鼠标就用鼠标方向，否则用朝向）
        var direction = _player.AimDirection;

        // 这些都要在 AddChild 之前设好 —— AddChild 会立刻触发 _Ready。
        bullet.Direction = direction;
        bullet.Position = _player.Position + direction * 24f;
        bullet.CollisionMask = Presets[_preset].Mask;

        AddChild(bullet);
        _shotsFired++;
    }

    private void BuildAimIndicator()
    {
        _aimLine = new Line2D
        {
            Name = "AimLine",
            Width = 2.5f,
            DefaultColor = new Color(1f, 0.92f, 0.5f, 0.8f),
            Antialiased = true,
        };
        AddChild(_aimLine);
    }

    private void UpdateAimIndicator()
    {
        // 一小段短横线指示枪口方向 —— 没有这个，玩家第一次射击全靠猜
        var dir = _player.AimDirection;
        _aimLine.Points = new[]
        {
            _player.Position + dir * 20f,
            _player.Position + dir * 74f,
        };
    }

    // ---------- D 毒池 ----------

    private void TickHazard(double delta)
    {
        // 连续在区域内 = 用 GetOverlappingBodies 每帧问一次"现在谁在里面"。
        // 注意这和 BodyEntered/BodyExited（只在进出那一刻触发一次）是两种不同的用法，
        // 本站两种都用了，可以对照。
        if (_hazard.GetOverlappingBodies().Count == 0)
        {
            _hazardTimer = 0;
            return;
        }

        _hazardTimer += delta;
        if (_hazardTimer < HazardTickInterval) return;

        _hazardTimer -= HazardTickInterval;
        _hp = Mathf.Max(0, _hp - HazardDamage);
        _hazardTicks++;

        if (_hp <= 0) RespawnAfterDeath();
        else Flash($"毒池侵蚀 −{HazardDamage} HP（剩 {_hp}）", 0.9);
    }

    private void OnHazardEntered(Node2D body)
    {
        if (body != _player) return;
        Flash("进入毒池 —— Area2D 的 BodyEntered 只在「刚进来」那一瞬间触发一次");
    }

    private void OnHazardExited(Node2D body)
    {
        if (body != _player) return;
        Flash("离开毒池 —— BodyExited 同理，只有一次", 1.6);
    }

    private void RespawnAfterDeath()
    {
        _deaths++;
        _hp = MaxHp;
        _player.GlobalPosition = SpawnPoint;
        _player.Velocity = Vector2.Zero;
        Flash($"中毒倒地！已送回起点（第 {_deaths} 次）", 2.6);
    }

    private void TickHealPad(double delta)
    {
        if (_hp >= MaxHp) return;
        if (_player.GlobalPosition.DistanceTo(HealPadCenter) > 78f) return;

        _healAccumulator += delta;
        if (_healAccumulator < 0.1) return;

        _healAccumulator = 0;
        _hp = Mathf.Min(MaxHp, _hp + 5);
    }

    // ---------- C 视线台 ----------

    private void TickSightZone(double delta)
    {
        if (_hideGoal) return;
        if (!SightZone.HasPoint(_player.GlobalPosition)) return;
        if (_turret.HasLineOfSight) return;

        // 只有"在 C 区内"且"射线被挡住"时才累计
        _hideSeconds = Mathf.Min(HideGoalSeconds, _hideSeconds + (float)delta);
    }

    // ---------- 目标判定 ----------

    private void CheckGoals()
    {
        if (!_shootGoal && _targetA.WasHit && _targetB.WasHit)
        {
            _shootGoal = true;
            MarkTaskDone(0);
            Flash("两个靶子都打中了 —— 你已经在用掩码控制子弹该撞谁了", 3.0);
        }

        if (!_pushGoal && _box.Position.DistanceTo(PushPadCenter) < 62f)
        {
            _pushGoal = true;
            MarkTaskDone(1);
            Flash("箱子入位 —— CharacterBody2D 推 RigidBody2D 必须自己施力，物理引擎不会替你传", 3.5);
        }

        if (!_hideGoal && _hideSeconds >= HideGoalSeconds)
        {
            _hideGoal = true;
            MarkTaskDone(2);
            Flash("在掩体后躲满 3 秒 —— 射线被挡住了，所以炮塔就是看不见你", 3.0);
        }

        if (!_hurtGoal && _hazardTicks >= HurtGoalTicks)
        {
            _hurtGoal = true;
            MarkTaskDone(3);
            Flash("毒池里挨了 5 次 —— 这就是 Area2D 做持续伤害区的方式", 3.0);
        }

        if (_shootGoal && _pushGoal && _hideGoal && _hurtGoal)
        {
            Complete("四个实验台全部完成：层与掩码、推刚体、视线射线、伤害区域");
        }
    }

    // ---------- 界面 ----------

    private void BuildLabels()
    {
        // 左上角面板同时也是 A 区的说明，所以第一行自带区名（见 UpdatePanel）。
        _presetLabel = LevelKit.MakeLabel(this, new Vector2(24, 62), "", 15,
            new Color(0.85f, 0.91f, 0.98f), HorizontalAlignment.Left, "PresetPanel");
        _presetLabel.Size = new Vector2(600, 130);

        // 各区的功能标注。位置都是避让出来的：
        //   右上角被通用任务面板占到 y≈264 为止，所以 B 区、D 区的说明必须放在它下面；
        //   底部 y>672 被状态行占着，所以毒池说明放进毒池内部而不是压在状态行上。
        // 掩体贴在墙体右侧、靶子左侧的空档里：左不压墙（墙右缘 x=340），
        // 右不压靶子（靶子左缘 x=497），上不压左上角面板（面板到 y≈176 为止）。
        LevelKit.MakeLabel(this, new Vector2(430, 262), "掩体（World 层）", 13, new Color(0.6f, 0.66f, 0.78f));
        LevelKit.MakeLabel(this, new Vector2(720, 386), "B 推箱子（RigidBody2D）", 14,
            new Color(0.78f, 0.72f, 0.95f), HorizontalAlignment.Left, "LabelB");
        LevelKit.MakeLabel(this, new Vector2(56, 440), "C 视线台（RayCast2D）", 14,
            new Color(0.66f, 0.92f, 0.8f), HorizontalAlignment.Left, "LabelC");
        LevelKit.MakeLabel(this, new Vector2(700, 440), "D 毒池（Area2D 持续伤害）", 14,
            new Color(0.95f, 0.72f, 0.72f), HorizontalAlignment.Left, "LabelD");

        LevelKit.MakeLabel(this, new Vector2(1140, 372), "目标垫", 13, new Color(0.5f, 0.88f, 0.66f));
        LevelKit.MakeLabel(this, new Vector2(1180, 534), "治疗垫", 13, new Color(0.5f, 0.88f, 0.66f));
        LevelKit.MakeLabel(this, new Vector2(900, 496), "踩进来才会扣血", 13, new Color(0.98f, 0.78f, 0.78f));
    }

    private void UpdatePanel()
    {
        var p = Presets[_preset];
        var maskText = GameLayers.Describe(p.Mask);

        _presetLabel.Text =
            $"【A 射击台 · 层与掩码】子弹掩码预设 {_preset + 1}/{Presets.Length}：{p.Name}   （按 1 / 2 / 3 / 4 切换）\n" +
            $"    layer = {GameLayers.Describe(GameLayers.Projectile)}（{GameLayers.Projectile}）  ← 我是什么，固定不变\n" +
            $"    mask  = {maskText}（{p.Mask}）  ← 我检测什么，就是这里在变\n" +
            $"    {p.Hint}\n" +
            $"    J / 鼠标左键射击：朝鼠标方向（没动过鼠标时朝最后移动的方向）";

        var hits = (_targetA.WasHit ? 1 : 0) + (_targetB.WasHit ? 1 : 0);
        SetStatus(
            $"Ａ 打靶 {hits}/2（已射 {_shotsFired} 发）    " +
            $"Ｂ 推箱 {(_pushGoal ? "完成" : "未完成")}    " +
            $"Ｃ 隐蔽 {_hideSeconds:0.0}/{HideGoalSeconds:0.0}s    " +
            $"Ｄ 承伤 {_hazardTicks}/{HurtGoalTicks}    HP {_hp}    倒地 {_deaths} 次");
    }
}
