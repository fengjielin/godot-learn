using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// 一次近战挥砍的判定框。存在时间极短（0.12 秒），期间检测有没有打到人。
///
/// 学习要点一：**为什么攻击判定要单独做一个"存在 0.12 秒"的东西，而不是让玩家每帧检测？**
///   因为"挥砍"是一个**有始有终的动作**。做成独立对象之后：
///     · 前摇/判定/后摇天然分离，可以各自调时长
///     · 每把武器可以有自己的判定形状和伤害参数
///     · 同一个目标在一次挥砍里只会被结算一次（本类的 `_hitTargets`）
///   如果直接写在玩家身上，这三件事都会变成"靠标志位硬凑"。
///
/// 学习要点二：**Godot 的 `[Signal]` 只能传 Variant 能表达的类型**
///   （数字、字符串、向量、Godot 对象……）。想传 `DamageResult` 这种自定义 C# 对象，
///   要么拆成几个基本类型，要么——像这里一样——**直接用普通的 C# `event`**。
///   两者可以混用：对外（编辑器、跨语言）用信号，对内的强类型回调用 event。
/// </summary>
public partial class MeleeSwing : Area2D
{
    /// <summary>打到某个目标时触发。带自定义对象，所以用 C# event 而不是 Godot 信号。</summary>
    public event Action<DamageResult, CombatDummy>? Hit;

    [Export] public float LifeTime { get; set; } = 0.12f;

    public DamageInfo Info { get; set; } = new();

    private readonly HashSet<CombatDummy> _hitTargets = new();
    private Polygon2D _visual = null!;
    private float _age;

    public override void _Ready()
    {
        _visual = GetNode<Polygon2D>("Visual");
        CollisionLayer = GameLayers.PlayerAttack;
        CollisionMask = GameLayers.Enemy;

        // 挥砍动画：快速张开再消失
        var tween = CreateTween();
        tween.TweenProperty(_visual, "scale", new Vector2(1.18f, 1.18f), 0.04);
        tween.TweenProperty(_visual, "modulate:a", 0f, 0.12);
    }

    public override void _PhysicsProcess(double delta)
    {
        _age += (float)delta;

        foreach (var body in GetOverlappingBodies())
        {
            if (body is not CombatDummy dummy) continue;
            if (!_hitTargets.Add(dummy)) continue; // 一次挥砍对同一目标只结算一次

            var result = dummy.Health.Apply(Info);
            Hit?.Invoke(result, dummy);
        }

        if (_age >= LifeTime) QueueFree();
    }
}
