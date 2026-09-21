using Godot;

namespace Dojo.Common;

/// <summary>
/// 屏幕反馈（Autoload）：屏幕震动 + 顿帧 + 白闪。
///
/// 为什么做成全局单例而不是写在某个练习站里：
///   这三样东西是"打击感"的通用零件，S07 战斗站、S08 敌人受击都要用。
///   而且它们必须**跨场景安全** —— 顿帧会改全局的 Engine.TimeScale，
///   如果带着 0.05 的时间缩放切换场景，整个游戏会永远慢动作。
///   集中在一处才好统一兜底（见 CancelAll 和 StationBase._ExitTree 的调用）。
///
/// 学习要点：
///   1. **屏幕震动用"创伤值"而不是"震动时长"**。
///      受击时加一点 trauma（0~1），每帧衰减，实际位移 = trauma² × 最大幅度。
///      好处：连续受击会自然叠加（不会互相打断），而且平方让衰减"前段猛、后段干脆"，
///      线性衰减看起来像机器在匀速晃，很假。这个技巧出自 Squirrel Eiserloh 的
///      "Juicing Your Cameras" 演讲，几乎是行业标准做法。
///   2. **顿帧就是临时把 Engine.TimeScale 调低**（比如 0.05）持续几十毫秒。
///      它不改变任何游戏逻辑，只改变"时间流逝的速度"，
///      但能让一次命中显得"有分量"。代价是所有东西一起慢下来，
///      所以持续时间和缩放系数都要很小 —— 0.06 秒 / 0.05 倍是常用的量级。
///   3. 顿帧期间必须用**真实时间**来判断何时恢复。如果用 delta 计时，
///      而 delta 本身已经被缩放了，那么"0.06 秒"会变成"1.2 秒"。
///      Time.GetTicksUsec() 不受 TimeScale 影响，这里用的就是它。
/// </summary>
public partial class ScreenFx : Node
{
    public static ScreenFx Instance { get; private set; } = null!;

    // ---- 三个开关。S05 练习站让你随时把它们关掉，直接感受差值 ----
    public bool ShakeEnabled { get; set; } = true;
    public bool HitstopEnabled { get; set; } = true;
    public bool FlashEnabled { get; set; } = true;

    /// <summary>创伤衰减速度（单位/秒）。越大越"脆"。</summary>
    [Export] public float TraumaDecay { get; set; } = 2.4f;

    /// <summary>trauma = 1 时的最大位移（像素）。</summary>
    [Export] public float MaxShakeOffset { get; set; } = 32f;

    /// <summary>trauma = 1 时的最大旋转（弧度）。</summary>
    [Export] public float MaxShakeRotation { get; set; } = 0.045f;

    /// <summary>当前抖动偏移。由 CameraRig 读取并叠加到相机位置上。</summary>
    public Vector2 ShakeOffset { get; private set; }
    public float ShakeRotation { get; private set; }
    public float Trauma { get; private set; }

    private CanvasLayer _flashLayer = null!;
    private ColorRect _flashRect = null!;
    private Tween? _flashTween;

    private ulong _hitstopEndUsec;
    private bool _hitstopActive;

    public override void _EnterTree()
    {
        Instance = this;

        // 顿帧期间其它节点可能被时间缩放影响，但这个节点必须一直跑 ——
        // 否则就没人负责把 TimeScale 恢复回来了，游戏会永远卡在慢动作里。
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        _flashLayer = new CanvasLayer { Name = "FlashLayer", Layer = 120 };
        _flashRect = new ColorRect
        {
            Name = "Flash",
            Color = Colors.White,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _flashRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _flashRect.Modulate = new Color(1, 1, 1, 0);

        _flashLayer.AddChild(_flashRect);
        AddChild(_flashLayer);
    }

    public override void _ExitTree() => CancelAll();

    public override void _Process(double delta)
    {
        TickHitstop();
        TickShake(delta);
    }

    // ---------- 对外接口 ----------

    /// <summary>加一点"创伤"。0~1 之间叠加；实际抖动幅度是它的平方。</summary>
    public void Shake(float trauma)
    {
        if (!ShakeEnabled) return;
        Trauma = Mathf.Min(1f, Trauma + trauma);
    }

    /// <summary>顿帧：在 realSeconds 秒（真实时间）内把时间流速降到 timeScale 倍。</summary>
    public void Hitstop(float realSeconds, float timeScale = 0.05f)
    {
        if (!HitstopEnabled) return;

        Engine.TimeScale = timeScale;
        _hitstopEndUsec = Time.GetTicksUsec() + (ulong)(realSeconds * 1_000_000.0);
        _hitstopActive = true;
    }

    /// <summary>全屏闪一下。受击闪红、拾取闪白，是最廉价也最有效的反馈。</summary>
    public void Flash(Color color, float strength = 0.45f, float seconds = 0.18f)
    {
        if (!FlashEnabled) return;

        _flashTween?.Kill();
        _flashRect.Color = color;
        _flashRect.Modulate = new Color(1, 1, 1, strength);

        _flashTween = CreateTween();
        _flashTween.SetPauseMode(Tween.TweenPauseMode.Process);
        _flashTween.TweenProperty(_flashRect, "modulate:a", 0f, seconds);
    }

    /// <summary>
    /// 把时间缩放和抖动全部复位。
    /// **任何会改变场景的地方都应该调它** —— 带着 0.05 的 TimeScale 换场景，
    /// 新场景会以二十分之一的速度运行，而且看起来像"游戏卡死了"。
    /// </summary>
    public void CancelAll()
    {
        Engine.TimeScale = 1.0;
        _hitstopActive = false;
        Trauma = 0f;
        ShakeOffset = Vector2.Zero;
        ShakeRotation = 0f;
    }

    public bool IsHitstopActive => _hitstopActive;

    // ---------- 内部 ----------

    private void TickHitstop()
    {
        if (!_hitstopActive) return;
        if (Time.GetTicksUsec() < _hitstopEndUsec) return;

        Engine.TimeScale = 1.0;
        _hitstopActive = false;
    }

    private void TickShake(double delta)
    {
        if (Trauma <= 0f)
        {
            ShakeOffset = Vector2.Zero;
            ShakeRotation = 0f;
            return;
        }

        Trauma = Mathf.Max(0f, Trauma - TraumaDecay * (float)delta);

        // 平方：让大创伤很猛、小创伤几乎看不见，衰减曲线比线性自然得多
        var amount = Trauma * Trauma;

        ShakeOffset = new Vector2(
            (float)GD.RandRange(-1.0, 1.0),
            (float)GD.RandRange(-1.0, 1.0)) * amount * MaxShakeOffset;

        ShakeRotation = (float)GD.RandRange(-1.0, 1.0) * amount * MaxShakeRotation;
    }
}
