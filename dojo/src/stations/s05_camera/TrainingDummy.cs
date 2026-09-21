using Godot;

namespace Dojo.Stations;

/// <summary>
/// S05 的训练假人 —— 打它一下就能感受到"顿帧 + 震屏 + 白闪"三件套。
///
/// 它故意不做任何伤害计算（那是 S07 的事）。
/// 这一站只关心一件事：**同样的动作，加不加这些反馈，"打中"的感觉差多少。**
/// </summary>
public partial class TrainingDummy : Node2D
{
    public int HitCount { get; private set; }

    private Polygon2D _body = null!;
    private Polygon2D _face = null!;
    private Polygon2D _mark = null!;

    private static readonly Color IdleBody = new(0.38f, 0.30f, 0.34f);
    private static readonly Color IdleFace = new(0.60f, 0.40f, 0.44f);
    private static readonly Color HitBody = new(0.95f, 0.55f, 0.42f);
    private static readonly Color HitFace = new(1f, 0.85f, 0.6f);

    public override void _Ready()
    {
        _body = GetNode<Polygon2D>("Body");
        _face = GetNode<Polygon2D>("Face");
        _mark = GetNode<Polygon2D>("Mark");
        ResetVisual();
    }

    public void Hit()
    {
        HitCount++;

        _body.Color = HitBody;
        _face.Color = HitFace;
        _mark.Color = Colors.White;

        // 被击反馈：整体后退一点再弹回来（"命中顿挫"的最简形态）
        var tween = CreateTween();
        tween.TweenProperty(this, "scale", new Vector2(1.22f, 0.82f), 0.04);
        tween.TweenProperty(this, "scale", Vector2.One, 0.24)
            .SetTrans(Tween.TransitionType.Elastic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenCallback(Callable.From(ResetVisual));
    }

    public void ResetVisual()
    {
        _body.Color = IdleBody;
        _face.Color = IdleFace;
        _mark.Color = new Color(0.16f, 0.12f, 0.14f);
    }
}
