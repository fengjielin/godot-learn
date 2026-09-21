using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// S05 的撞击柱 —— 撞上去会震屏。
///
/// 做法：一个 StaticBody2D（挡住玩家）+ 一个略大一圈的 Area2D（提前感知玩家接近）。
/// 为什么要"略大一圈"：如果只用碰撞来触发，那么触发的那一刻玩家已经被挡住了，
/// 速度已经被物理求解归零，你读到的 `Velocity` 是 0 —— 震动强度就永远算不对。
/// **"先感觉到、再挡住"是这类撞击反馈的标准做法。**
/// </summary>
public partial class BumpPost : StaticBody2D
{
    [Signal] public delegate void BumpedEventHandler(float speed);

    /// <summary>低于这个速度撞上来不算"撞"，只是蹭了一下。</summary>
    private const float MinBumpSpeed = 120f;

    public int BumpCount { get; private set; }

    private Area2D _detector = null!;
    private Polygon2D _body = null!;
    private Polygon2D _edge = null!;
    private float _cooldown;

    private static readonly Color IdleColor = new(0.30f, 0.34f, 0.42f);
    private static readonly Color IdleEdge = new(0.52f, 0.58f, 0.70f);

    public override void _Ready()
    {
        _detector = GetNode<Area2D>("Detector");
        _body = GetNode<Polygon2D>("Body");
        _edge = GetNode<Polygon2D>("Edge");

        CollisionLayer = GameLayers.World;
        CollisionMask = 0;

        _body.Color = IdleColor;
        _edge.Color = IdleEdge;

        _detector.BodyEntered += OnBodyEntered;
    }

    public override void _Process(double delta)
    {
        if (_cooldown > 0f) _cooldown = Mathf.Max(0f, _cooldown - (float)delta);
    }

    public void ResetVisual()
    {
        _body.Color = IdleColor;
        _edge.Color = IdleEdge;
        Scale = Vector2.One;
    }

    private void OnBodyEntered(Node2D body)
    {
        if (_cooldown > 0f) return;
        if (body is not CharacterBody2D character) return;

        var speed = character.Velocity.Length();
        if (speed < MinBumpSpeed) return;

        _cooldown = 0.5f;
        BumpCount++;

        _body.Color = new Color(0.95f, 0.72f, 0.35f);
        _edge.Color = new Color(1f, 0.9f, 0.6f);

        var tween = CreateTween();
        tween.TweenProperty(this, "scale", new Vector2(1.14f, 0.9f), 0.05);
        tween.TweenProperty(this, "scale", Vector2.One, 0.22);
        tween.TweenCallback(Callable.From(ResetVisual));

        EmitSignal(SignalName.Bumped, speed);
    }
}
