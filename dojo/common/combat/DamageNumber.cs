using Godot;

namespace Dojo.Common;

/// <summary>
/// 一条伤害飘字。**它由对象池反复复用，所以有两套状态：`InUse` 和"外观"。**
///
/// 学习要点：
///   1. 池化对象**必须能干净地重置**。`Play()` 里要把所有会残留的东西全部写一遍：
///      位置、颜色、字号、缩放、透明度、计时器。漏掉任何一个，
///      下一次复用时它就会带着上一次的痕迹出现 —— 这种 bug 看起来像"随机发生"，
///      其实是你漏了一个字段。
///   2. 池化对象**不能自己 QueueFree**。它只能"把自己标记为空闲"，
///      由池来管理生命周期。**销毁权必须只有一方持有。**
/// </summary>
public partial class DamageNumber : Node2D
{
    /// <summary>池用它判断这条飘字能不能被复用。</summary>
    public bool InUse { get; private set; }

    /// <summary>
    /// 不在池里的时候（比如站台的"关掉飘字池"对照实验），允许它自己销毁。
    /// **池化对象默认不能自杀**（销毁权归池），这是唯一的例外，
    /// 而且这个例外必须由使用方显式打开 —— 免得两种模式混在一起时出现悬空引用。
    /// </summary>
    public bool AutoFreeOnRelease { get; set; }

    private Label _label = null!;
    private float _age;
    private float _life;
    private Vector2 _velocity;
    private float _baseScale = 1f;

    public override void _Ready()
    {
        _label = GetNode<Label>("Label");
        Release();
    }

    /// <summary>播放一次。**所有状态都在这里重置** —— 见类注释第 1 条。</summary>
    public void Play(string text, Color color, bool big, Vector2 position)
    {
        Position = position;
        Scale = Vector2.One * (big ? 1.35f : 1f);
        Modulate = Colors.White;
        Rotation = 0f;

        _label.Text = text;
        _label.AddThemeColorOverride("font_color", color);
        _label.AddThemeFontSizeOverride("font_size", big ? 30 : 21);

        // 随机横向初速，让连续命中时数字不会叠成一坨
        _velocity = new Vector2((float)GD.RandRange(-34.0, 34.0), big ? -128f : -104f);
        _baseScale = big ? 1.35f : 1f;

        _age = 0f;
        _life = 0.85f;
        InUse = true;
        Visible = true;
    }

    /// <summary>交还给池。默认不销毁自己。</summary>
    public void Release()
    {
        InUse = false;
        Visible = false;
        if (AutoFreeOnRelease) QueueFree();
    }

    public override void _Process(double delta)
    {
        if (!InUse) return;

        var dt = (float)delta;
        _age += dt;

        _velocity += new Vector2(0f, 330f) * dt; // 一点重力，让弧线看得清
        Position += _velocity * dt;

        // 前 45% 保持不透明，之后淡出 —— 一出现就淡出会看不清数字
        var fadeStart = _life * 0.45f;
        var alpha = _age <= fadeStart ? 1f : 1f - Mathf.Clamp((_age - fadeStart) / (_life - fadeStart), 0f, 1f);
        Modulate = new Color(1f, 1f, 1f, alpha);

        // 轻微回弹：大数字会"弹"一下
        var pop = _age < 0.12f ? Mathf.Lerp(1.25f, 1f, _age / 0.12f) : 1f;
        Scale = Vector2.One * _baseScale * pop;

        if (_age >= _life) Release();
    }
}
