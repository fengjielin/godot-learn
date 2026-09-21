using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// 一次挥砍的判定框。**几何形状来自 WeaponData** —— 匕首短而窄，大剑长而宽。
///
/// ★ 一个很容易踩的坑：`sub_resource` 默认是**共享**的。
///   如果直接改从场景里取到的 `RectangleShape2D.Size`，
///   那么"所有挥砍实例"的判定框会一起变 —— 你会看到上一刀的形状残留到下一刀。
///   正确做法是**每个实例新建一份资源**（下面就是这么做的）。
///   这和 S10 背包里"改一个道具会影响所有同种道具"是同一类问题。
/// </summary>
public partial class WeaponSwing : Area2D
{
    public event Action<DamageResult, CombatDummy>? Hit;

    [Export] public float LifeTime { get; set; } = 0.1f;

    public DamageInfo Info { get; set; } = new();
    public Vector2 Size { get; set; } = new(52f, 44f);
    public Color Tint { get; set; } = new(1f, 0.94f, 0.7f, 0.32f);

    private readonly HashSet<CombatDummy> _hitTargets = new();
    private float _age;

    public override void _Ready()
    {
        var visual = GetNode<Polygon2D>("Visual");
        var edge = GetNode<Polygon2D>("Edge");
        var shape = GetNode<CollisionShape2D>("Shape");

        CollisionLayer = GameLayers.PlayerAttack;
        CollisionMask = GameLayers.Enemy;

        // ① 判定框：新建一份形状资源，不要改共享的 sub_resource
        shape.Shape = new RectangleShape2D { Size = Size };

        // ② 外观：按同一份尺寸重建多边形，让"看到的"和"打到的"一致
        visual.Polygon = LevelKit.RectPoints(Size);
        visual.Color = Tint;
        edge.Polygon = new[]
        {
            new Vector2(-Size.X / 2f, -Size.Y / 2f - 3f),
            new Vector2(Size.X / 2f + 3f, -Size.Y / 2f - 3f),
            new Vector2(Size.X / 2f + 3f, -Size.Y / 2f + 7f),
            new Vector2(-Size.X / 2f, -Size.Y / 2f + 7f),
        };
        edge.Color = new Color(Tint.R, Tint.G, Tint.B, 0.9f);

        // ③ 挥砍动画：张开一点再淡出
        var tween = CreateTween();
        tween.TweenProperty(visual, "scale", new Vector2(1.15f, 1.2f), 0.04);
        tween.TweenProperty(visual, "modulate:a", 0f, LifeTime);
        tween.TweenProperty(edge, "modulate:a", 0f, LifeTime);
    }

    public override void _PhysicsProcess(double delta)
    {
        _age += (float)delta;

        foreach (var body in GetOverlappingBodies())
        {
            if (body is not CombatDummy dummy) continue;
            if (!_hitTargets.Add(dummy)) continue;

            Hit?.Invoke(dummy.Health.Apply(Info), dummy);
        }

        if (_age >= LifeTime) QueueFree();
    }
}
