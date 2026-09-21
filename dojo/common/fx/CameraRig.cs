using Godot;

namespace Dojo.Common;

/// <summary>
/// 跟随玩家的相机。挂在关卡场景里当一个普通节点即可。
///
/// 相机是"玩家永远不会注意、但少了就完全不能玩"的东西。
/// 它有三个独立的问题要解决，本站把它们拆成三个开关：
///
///   ① 跟随方式：直接贴住玩家，还是平滑插值追上去？
///      直接跟随的问题：玩家每一点微小的速度变化都会晃到画面，长时间看很累。
///      平滑跟随的问题：太慢会"拖后腿"，玩家冲刺时角色跑到屏幕边缘。
///      正确的做法是**指数平滑**（下面用的是 1 - exp(-k·dt)），它有两个好处：
///        · 与帧率无关 —— 30fps 和 144fps 下追上去的快慢完全一致
///        · k 越大越紧跟，是一个直觉上可调的参数
///      （用 Lerp(a, b, 0.1f) 这种写法是错的：帧率翻倍，跟随速度也翻倍。）
///
///   ② 前瞻（look ahead）：相机朝玩家关注的方向偏移一点，让你能提前看到前面的东西。
///      不前瞻的话，你永远只能看到角色前方半个屏幕。
///      本站用鼠标方向做前瞻，实际项目里常用"角色朝向"或"移动方向"。
///
///   ③ 边界（limits）：相机不该拍到关卡外面的黑边。
///      注意 limit 生效时**角色会偏离屏幕中心** —— 这会让相机"被卡住了"的感觉
///      变得很明显，很多游戏会额外做一个"边界内软化"来缓解。
///
/// 学习要点：抖动的位移由 ScreenFx 提供，相机只负责叠加。
/// **一个变量只有一个写入者**，否则两个系统抢着写 Offset，调起来会疯掉。
/// </summary>
public partial class CameraRig : Camera2D
{
    /// <summary>平滑跟随（关掉 = 直接贴住玩家，方便对比）。</summary>
    [Export] public bool SmoothFollow { get; set; } = true;

    /// <summary>跟随的"硬度"。越大越紧跟，8~12 是比较舒服的区间。</summary>
    [Export] public float FollowSharpness { get; set; } = 9f;

    [Export] public bool LookAheadEnabled { get; set; } = true;

    /// <summary>前瞻的最大偏移。太大反而会晕。</summary>
    [Export] public Vector2 LookAheadMax { get; set; } = new(48f, 36f);

    /// <summary>前瞻的跟随速度。应该比相机本体慢，否则会跟着鼠标乱抖。</summary>
    [Export] public float LookAheadSharpness { get; set; } = 4f;

    [Export] public float MinZoom { get; set; } = 0.6f;
    [Export] public float MaxZoom { get; set; } = 1.8f;
    [Export] public float ZoomStep { get; set; } = 0.12f;

    private Node2D? _target;
    private Vector2 _baseline;
    private Vector2 _lookAhead;
    private Rect2 _bounds;
    private bool _limitsEnabled = true;

    public Vector2 Baseline => _baseline;

    public override void _Ready()
    {
        _baseline = GlobalPosition;
        MakeCurrent();
    }

    /// <summary>设置相机活动范围（一般是关卡边界）。</summary>
    public void SetBounds(Rect2 bounds)
    {
        _bounds = bounds;
        ApplyLimits();
    }

    public void SetLimitsEnabled(bool enabled)
    {
        _limitsEnabled = enabled;
        ApplyLimits();
    }

    /// <summary>滚轮缩放。steps > 0 放大（看得更近），< 0 缩小。</summary>
    public void ZoomBy(float steps)
    {
        var next = Mathf.Clamp(Zoom.X + steps * ZoomStep, MinZoom, MaxZoom);
        Zoom = new Vector2(next, next);
    }

    public override void _Process(double delta)
    {
        _target ??= FindPlayer();
        if (_target is null) return;

        var dt = (float)delta;

        // ---- 前瞻 ----
        var desiredLookAhead = Vector2.Zero;
        if (LookAheadEnabled)
        {
            var toMouse = GetGlobalMousePosition() - _target.GlobalPosition;
            desiredLookAhead = toMouse.LimitLength(1f) * LookAheadMax;
        }
        _lookAhead = _lookAhead.Lerp(desiredLookAhead, SmoothFactor(LookAheadSharpness, dt));

        var desired = _target.GlobalPosition + _lookAhead;

        // ---- 跟随 ----
        _baseline = SmoothFollow
            ? _baseline.Lerp(desired, SmoothFactor(FollowSharpness, dt))
            : desired;

        // ---- 叠加抖动 ----
        // 抖动**不参与平滑**，否则会被"追上去"的过程吃掉，看起来软绵绵的没有冲击力。
        GlobalPosition = _baseline + ScreenFx.Instance.ShakeOffset;
        Rotation = ScreenFx.Instance.ShakeRotation;
    }

    /// <summary>
    /// 与帧率无关的平滑系数。
    /// 1 - e^(-k·dt) 的意义：每秒把"剩余差距"消掉 (1 - e^-k) 的比例。
    /// k=9 时约等于每秒消掉 99.99% —— 听起来很猛，但因为是按比例逼近，
    /// 实际手感是"紧跟但有一点回弹"，这正是想要的。
    /// </summary>
    private static float SmoothFactor(float sharpness, float dt)
        => 1f - Mathf.Exp(-sharpness * dt);

    private static Node2D? FindPlayer()
    {
        var nodes = Engine.GetMainLoop() is SceneTree tree
            ? tree.GetNodesInGroup("player")
            : null;

        if (nodes is { Count: > 0 } && nodes[0] is Node2D node) return node;
        return null;
    }

    private void ApplyLimits()
    {
        if (_limitsEnabled)
        {
            LimitLeft = (int)_bounds.Position.X;
            LimitTop = (int)_bounds.Position.Y;
            LimitRight = (int)_bounds.End.X;
            LimitBottom = (int)_bounds.End.Y;
        }
        else
        {
            // 一个很大的范围 = 相当于没有边界，方便对比
            const int far = 100000;
            LimitLeft = -far;
            LimitTop = -far;
            LimitRight = far;
            LimitBottom = far;
        }
    }
}
