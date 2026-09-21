using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// S06 · UI 与 HUD
///
/// 这一站不是一个"走动的房间"，而是一整块 UI 实验屏。它由三部分组成：
///
///   ① **布局实验台**（左）—— 同一个盒子，按 1 在「容器布局 / 绝对定位」之间切换，
///      按 3 改变盒子宽度。你会发现：
///        · 容器布局：文字自动折行，内容永远待在盒子里
///        · 绝对定位：宽度写死，盒子一窄，右边就被**静默切掉**（不报任何错）
///      这是 UI 新手最常见的坑 —— 而且它不会崩，只是"看起来怪"。
///
///   ② **控件陈列柜**（右）—— 按 4 切换主题配色，所有控件一起变。
///      底下还有一排按钮做**焦点导航**实验：点一下把焦点放进去，
///      然后用方向键移动、回车确认。这套东西不需要写一行代码，
///      因为 Godot 的 Control 自带几何焦点邻居计算。
///
///   ③ **暂停菜单**（ESC）—— 由 StationBase 给**每个练习站**都装了一份。
///      里面有真正的音量滑条和全屏开关，改完立刻生效并写进存档。
///
/// 本站的判定刻意和"按键动作"绑定，而不是"你读懂了没"：
/// 切换过布局、开过暂停菜单、换过主题、改过音量、看过内置动作说明 —— 五条都做到即通关。
/// </summary>
public partial class S06Ui : StationBase
{
    public override string StationId => "s06_ui";

    /// <summary>可切换的窗口尺寸。分辨率变化的目的是让你看布局怎么自适应。</summary>
    private static readonly Vector2I[] Resolutions =
    {
        new(1280, 720),
        new(1600, 900),
        new(1024, 576),
    };

    /// <summary>
    /// 实验盒的三种宽度。
    /// 上限 360 是算出来的：UI 区右侧要给通用任务面板留出 ~456 像素，
    /// 所以两列各自只有约 377 宽。**这就是"给固定尺寸的 UI 元素预留空间"的真实约束** ——
    /// 不是随便挑一个看着顺眼的数字。
    /// </summary>
    private static readonly float[] BoxWidths = { 360f, 270f, 190f };

    private PanelContainer _testBox = null!;
    private Control _containerContent = null!;
    private Control _absoluteContent = null!;
    private Label _modeLabel = null!;
    private PanelContainer _infoPanel = null!;
    private Label _focusStatus = null!;
    private OptionButton _option = null!;
    private HSlider _slider = null!;
    private ProgressBar _bar = null!;

    private int _boxWidthIndex;
    private int _resolutionIndex;
    private bool _absoluteMode;
    private bool _pauseOpened;
    private bool _layoutTouched;
    private UiTheme.Palette _initialPalette;
    private int _volumeChangesAtStart;
    private int _focusChanges;

    private readonly bool[] _taskDone = new bool[5];

    protected override void StationReady()
    {
        _testBox = GetNode<PanelContainer>("Ui/Columns/Left/TestBox");
        _containerContent = GetNode<Control>("Ui/Columns/Left/TestBox/ContainerContent");
        _absoluteContent = GetNode<Control>("Ui/Columns/Left/TestBox/AbsoluteContent");
        _modeLabel = GetNode<Label>("Ui/Columns/Left/ModeLabel");
        _infoPanel = GetNode<PanelContainer>("Ui/InfoPanel");
        _focusStatus = GetNode<Label>("Ui/Columns/Right/Gallery/Margin/VBox/FocusStatus");
        _option = GetNode<OptionButton>("Ui/Columns/Right/Gallery/Margin/VBox/Option");
        _slider = GetNode<HSlider>("Ui/Columns/Right/Gallery/Margin/VBox/Slider");
        _bar = GetNode<ProgressBar>("Ui/Columns/Right/Gallery/Margin/VBox/Bar");

        // OptionButton 的选项只能用代码填 —— 它不是"写死的三个按钮"，
        // 而是"一个列表 + 当前选中项"，所以天生要由数据驱动。
        _option.AddItem("最低画质");
        _option.AddItem("中等画质");
        _option.AddItem("最高画质");
        _option.Selected = 1;

        // 滑条和进度条联动：让陈列柜里的控件不是死的
        _slider.ValueChanged += value => _bar.Value = value;

        WireFocusRow();

        _initialPalette = UiTheme.Instance.CurrentPalette;
        _volumeChangesAtStart = GameSettings.VolumeChangeCount;

        // 这两条判定必须靠信号，不能靠 _Process 轮询 ——
        // 暂停菜单一开，GetTree().Paused 就把本站的 _Process 冻住了，
        // "菜单开过吗 / 音量改过吗"根本轮询不到。**暂停期间还能被知道的事，只能用信号。**
        Pause.Opened += () => _pauseOpened = true;
        Pause.Closed += CheckGoals;

        ApplyLayoutMode();
        ApplyBoxWidth();
        UpdateFocusStatus(null);

        SetStatus("五个任务都是「动手做过就算」：切布局、开暂停菜单、换主题、改音量、看内置动作说明");
    }

    public override void _Process(double delta)
    {
        // 暂停菜单是不是被打开过
        if (Pause.IsOpen) _pauseOpened = true;

        CheckGoals();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("aux_1")) { _absoluteMode = !_absoluteMode; _layoutTouched = true; ApplyLayoutMode(); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_2")) { CycleResolution(); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_3")) { _boxWidthIndex = (_boxWidthIndex + 1) % BoxWidths.Length; _layoutTouched = true; ApplyBoxWidth(); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_4")) { UiTheme.Instance.TogglePalette(); Flash($"主题配色 → {PaletteName()}"); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_5")) { _infoPanel.Visible = !_infoPanel.Visible; GetViewport().SetInputAsHandled(); return; }

        base._UnhandledInput(@event);
    }

    // ---------- ① 布局实验台 ----------

    private void ApplyLayoutMode()
    {
        _containerContent.Visible = !_absoluteMode;
        _absoluteContent.Visible = _absoluteMode;
        RefreshModeLabel();
    }

    private void ApplyBoxWidth()
    {
        _testBox.CustomMinimumSize = new Vector2(BoxWidths[_boxWidthIndex], 250f);
        RefreshModeLabel();
    }

    private void RefreshModeLabel()
    {
        var width = BoxWidths[_boxWidthIndex];
        var mode = _absoluteMode ? "绝对定位" : "容器布局";

        _modeLabel.Text = _absoluteMode
            ? $"当前：{mode} · 盒子宽 {width:0}　→　文字宽度写死在 330，盒子窄了就被切掉，而且**不会报错**"
            : $"当前：{mode} · 盒子宽 {width:0}　→　文字自动折行，内容永远待在盒子里";
    }

    private void CycleResolution()
    {
        _resolutionIndex = (_resolutionIndex + 1) % Resolutions.Length;
        var size = Resolutions[_resolutionIndex];

        // 无头模式（自动化验证）没有真正的窗口，改了也没意义
        if (DisplayServer.GetName() != "headless")
            DisplayServer.WindowSetSize(size);

        Flash($"窗口分辨率 → {size.X}×{size.Y}　看看两边的布局还正不正常");
    }

    // ---------- ② 焦点导航 ----------

    private void WireFocusRow()
    {
        var row = GetNode<HBoxContainer>("Ui/Columns/Right/Gallery/Margin/VBox/FocusRow");
        foreach (var child in row.GetChildren())
        {
            if (child is not Button button) continue;

            button.FocusEntered += () => OnFocusChanged(button);
            button.Pressed += () => Flash($"「{button.Text}」被激活（鼠标点击或回车都算）");
        }
    }

    private void OnFocusChanged(Button button)
    {
        _focusChanges++;
        UpdateFocusStatus(button);
    }

    private void UpdateFocusStatus(Button? button)
    {
        _focusStatus.Text = button is null
            ? "焦点：无（点一下按钮，或用 TAB 把焦点移进来）"
            : $"焦点：{button.Text}　（已切换 {_focusChanges} 次 —— 方向键会自己找最近的邻居，不用写代码）";
    }

    // ---------- 目标判定 ----------

    private void CheckGoals()
    {
        if (_layoutTouched) MarkOnce(0);
        if (_pauseOpened) MarkOnce(1);
        if (UiTheme.Instance.CurrentPalette != _initialPalette) MarkOnce(2);
        if (GameSettings.VolumeChangeCount != _volumeChangesAtStart) MarkOnce(3);
        if (_infoPanel.Visible) MarkOnce(4);

        if (!Array.TrueForAll(_taskDone, done => done)) return;

        Complete("布局、暂停菜单、主题、设置存档、内置动作 —— 五样都亲手试过了");
    }

    private void MarkOnce(int index)
    {
        if (_taskDone[index]) return;
        _taskDone[index] = true;
        MarkTaskDone(index);
    }

    private string PaletteName()
        => UiTheme.Instance.CurrentPalette == UiTheme.Palette.Ocean ? "冷调" : "暖调";
}
