using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// S07 的训练假人 —— 一个"能挨打的东西"。
///
/// 它自己**完全不含伤害逻辑**：所有数值都交给子节点 `Damageable`。
/// 它只负责三件"表现层"的事：
///   ① 把 HP / 护盾画成两根条
///   ② 受击时闪一下
///   ③ 无敌帧期间持续闪烁（让玩家看得见"现在打不中"）
///
/// 第 ③ 条特别重要：**无敌帧如果看不见，玩家会以为是"我这一下没打中"，
/// 然后开始怀疑自己的操作。** 任何"免疫"都必须在画面上给反馈 ——
/// 这和 S04 里守卫的视线线是同一个道理。
/// </summary>
public partial class CombatDummy : CharacterBody2D
{
    public Damageable Health { get; private set; } = null!;

    private Polygon2D _body = null!;
    private Polygon2D _visor = null!;
    private Polygon2D _hpFill = null!;
    private Polygon2D _shieldFill = null!;
    private double _respawnTimer;
    private double _flashTimer;
    private Vector2 _home;

    private const float BarWidth = 68f;
    private static readonly Color IdleBody = new(0.42f, 0.34f, 0.42f);
    private static readonly Color FlashBody = new(1f, 0.72f, 0.62f);

    public bool IsRespawning => _respawnTimer > 0;

    public override void _Ready()
    {
        _body = GetNode<Polygon2D>("Body");
        _visor = GetNode<Polygon2D>("Visor");
        _hpFill = GetNode<Polygon2D>("Bars/HpFill");
        _shieldFill = GetNode<Polygon2D>("Bars/ShieldFill");

        CollisionLayer = GameLayers.Enemy;
        CollisionMask = GameLayers.World;

        Health = GetNode<Damageable>("Damageable");
        Health.KnockbackActor = this;
        _home = GlobalPosition;

        UpdateBars();
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;

        if (_flashTimer > 0f) _flashTimer = Mathf.Max(0f, _flashTimer - dt);

        // 无敌帧期间闪烁。频率刻意做快一点，和"受击闪白"区分开。
        if (CombatRules.IframeEnabled && Health.IsInvulnerable)
        {
            var blink = Mathf.Sin((float)Time.GetTicksMsec() * 0.035f) > 0f;
            _body.Color = blink ? FlashBody : IdleBody;
        }
        else
        {
            _body.Color = _flashTimer > 0f ? FlashBody : IdleBody;
        }

        if (_respawnTimer > 0)
        {
            _respawnTimer -= delta;
            if (_respawnTimer <= 0) Revive();
        }

        UpdateBars();
    }

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;

        // 击退给的是速度，这里负责让它自然停下。
        // 没有这句的话，被击退的假人会一直漂到墙边。
        Velocity = Velocity.MoveToward(Vector2.Zero, 1400f * dt);
        MoveAndSlide();

        // ★ 训练假人是"钉在地上"的：被击退之后会自己弹回原位。
        //
        // 这一条是踩坑之后加的：不加的话，打几下就把假人推到墙角，
        // 之后所有挥砍全部打空气 —— 我实测 35 次挥砍只命中 2 次，演示根本进行不下去。
        // 它同时也符合直觉：训练用的木桩本来就不该被推着满地跑。
        // **展示型场景里的每个物体，都要问一句"反复使用之后它会漂到哪里去"。**
        if (IsRespawning) return;
        if (Velocity.LengthSquared() > 4f) return;
        if (GlobalPosition.DistanceSquaredTo(_home) < 9f) return;

        GlobalPosition = GlobalPosition.Lerp(_home, 1f - Mathf.Exp(-3.5f * dt));
    }

    /// <summary>被打中之后的表现。由站台在结算完伤害后调用。</summary>
    public void PlayHitFeedback()
    {
        _flashTimer = 0.12f;

        var tween = CreateTween();
        tween.TweenProperty(this, "scale", new Vector2(1.22f, 0.82f), 0.04);
        tween.TweenProperty(this, "scale", Vector2.One, 0.22)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
    }

    public void StartRespawn()
    {
        _respawnTimer = 1.4;
        _body.Color = new Color(0.24f, 0.22f, 0.26f);
        _visor.Color = new Color(0.3f, 0.3f, 0.34f);
    }

    private void Revive()
    {
        Health.Reset();
        _visor.Color = new Color(1f, 0.86f, 0.72f);
        UpdateBars();
    }

    private void UpdateBars()
    {
        _hpFill.Scale = new Vector2(Mathf.Clamp((float)Health.Hp / Health.MaxHp, 0f, 1f), 1f);
        _shieldFill.Scale = new Vector2(
            Health.MaxShield <= 0 ? 0f : Mathf.Clamp((float)Health.Shield / Health.MaxShield, 0f, 1f), 1f);
    }

    /// <summary>给站台读两根条的世界坐标，用来决定飘字从哪儿冒出来。</summary>
    public Vector2 BarTopPosition => GlobalPosition + new Vector2(0f, -66f);

    public static float BarPixelWidth => BarWidth;
}
