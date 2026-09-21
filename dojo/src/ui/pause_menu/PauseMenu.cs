using Godot;
using Dojo.Common;
using Dojo.Game;

namespace Dojo.Ui;

/// <summary>
/// 暂停菜单 —— 全工程所有练习站共用一份（由 StationBase 自动装配）。
///
/// 学习要点（对应练习站 s06_ui）：
///
///   1. **暂停是 `GetTree().Paused = true`**，它会让所有 `ProcessMode = Inherit`
///      的节点停止 `_Process` / `_PhysicsProcess` / `_Input`。
///      菜单自己必须设成 `ProcessMode = Always`，否则它会把自己也冻住，
///      玩家就再也点不动了 —— 这是最常见的"一暂停就死机"。
///
///   2. **★ 暂停状态必须兜底复位。** `GetTree().Paused` 是全局的，
///      带着暂停切场景会让新场景永远冻住。所以 `_ExitTree` 里必须放开。
///      这和 S05 里 `Engine.TimeScale` 的兜底是同一类问题：
///      **全局开关忘了复位，症状是"整个游戏不对劲"，而且不报任何错。**
///
///   3. **焦点（Focus）是键盘/手柄导航的全部。** Godot 的 Button 默认
///      `focus_mode = All`，当某个按钮拿到焦点时，方向键会**自动**找几何上最近的邻居，
///      `ui_accept`（Enter / 空格 / 手柄 A）会按下它。所以"支持手柄导航"
///      其实不需要写任何代码 —— 只要你在打开菜单时 `GrabFocus()` 一下。
///
///   4. `ui_cancel` / `ui_accept` / `ui_up` 这些是**引擎内置动作**，
///      每个新工程默认就有（见 项目设置 → 输入映射 最下面那一段），
///      而且已经绑好了键盘和手柄。给它们重新定义反而会破坏手柄兼容性。
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    public bool IsOpen { get; private set; }

    /// <summary>
    /// 打开/关闭时发出。
    /// 为什么需要信号而不是让外部每帧去读 IsOpen：
    ///   **暂停会把别人的 `_Process` 冻住**，所以"菜单开了吗"这件事没法靠轮询发现 ——
    ///   轮询代码本身已经停了。凡是"暂停期间还要被知道"的事情，都必须靠信号推送。
    /// </summary>
    [Signal] public delegate void OpenedEventHandler();

    [Signal] public delegate void ClosedEventHandler();

    /// <summary>玩家是否用键盘/手柄确认过按钮。S06 用它来判定"你试过键盘导航了"。</summary>
    public int KeyboardActivationCount { get; private set; }

    private PanelContainer _mainPanel = null!;
    private PanelContainer _settingsPanel = null!;
    private Button _continueButton = null!;
    private Button _settingsButton = null!;
    private Button _hubButton = null!;
    private Button _backButton = null!;
    private Button _themeButton = null!;
    private Button _resetButton = null!;
    private HSlider _masterSlider = null!;
    private HSlider _musicSlider = null!;
    private HSlider _sfxSlider = null!;
    private CheckBox _fullscreenCheck = null!;

    /// <summary>回填滑条数值时用，避免"设值"被当成"玩家改了值"。</summary>
    private bool _syncing;

    public override void _Ready()
    {
        // 见类注释第 1 条：不设 Always 的话，暂停会把菜单自己也冻住。
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;

        _mainPanel = GetNode<PanelContainer>("Root/Stack/MainPanel");
        _settingsPanel = GetNode<PanelContainer>("Root/Stack/SettingsPanel");

        _continueButton = GetNode<Button>("Root/Stack/MainPanel/Margin/VBox/ContinueButton");
        _settingsButton = GetNode<Button>("Root/Stack/MainPanel/Margin/VBox/SettingsButton");
        _hubButton = GetNode<Button>("Root/Stack/MainPanel/Margin/VBox/HubButton");

        _backButton = GetNode<Button>("Root/Stack/SettingsPanel/Margin/VBox/BackButton");
        _themeButton = GetNode<Button>("Root/Stack/SettingsPanel/Margin/VBox/ThemeButton");
        _resetButton = GetNode<Button>("Root/Stack/SettingsPanel/Margin/VBox/ResetButton");
        _masterSlider = GetNode<HSlider>("Root/Stack/SettingsPanel/Margin/VBox/Grid/MasterSlider");
        _musicSlider = GetNode<HSlider>("Root/Stack/SettingsPanel/Margin/VBox/Grid/MusicSlider");
        _sfxSlider = GetNode<HSlider>("Root/Stack/SettingsPanel/Margin/VBox/Grid/SfxSlider");
        _fullscreenCheck = GetNode<CheckBox>("Root/Stack/SettingsPanel/Margin/VBox/FullscreenCheck");

        _continueButton.Pressed += Close;
        _settingsButton.Pressed += ShowSettings;
        _hubButton.Pressed += OnHubPressed;
        _backButton.Pressed += ShowMain;
        _themeButton.Pressed += OnThemePressed;
        _resetButton.Pressed += OnResetPressed;

        _masterSlider.ValueChanged += value => OnVolumeChanged(AudioManager.BusMaster, value);
        _musicSlider.ValueChanged += value => OnVolumeChanged(AudioManager.BusMusic, value);
        _sfxSlider.ValueChanged += value => OnVolumeChanged(AudioManager.BusSfx, value);
        _fullscreenCheck.Toggled += OnFullscreenToggled;

        SyncFromSettings();
        ShowMain();
    }

    /// <summary>用 _Input 观察"有人按了确认键"。因为 ui_accept 会被拿到焦点的按钮吃掉，
    /// 走 _UnhandledInput 是收不到的。</summary>
    public override void _Input(InputEvent @event)
    {
        if (IsOpen && @event.IsActionPressed("ui_accept")) KeyboardActivationCount++;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_cancel")) return;

        GetViewport().SetInputAsHandled();
        if (IsOpen) Close();
        else Open();
    }

    public override void _ExitTree()
    {
        // 见类注释第 2 条：带着暂停切场景，新场景会永远冻住。
        var tree = GetTree();
        if (tree is not null) tree.Paused = false;
    }

    // ---------- 开关 ----------

    public void Open()
    {
        if (IsOpen) return;

        IsOpen = true;
        Visible = true;
        ShowMain();
        GetTree().Paused = true;

        // 这一行就是"支持键盘/手柄导航"的全部秘诀
        _continueButton.GrabFocus();

        EmitSignal(SignalName.Opened);
    }

    public void Close()
    {
        if (!IsOpen) return;

        IsOpen = false;
        Visible = false;
        GetTree().Paused = false;

        EmitSignal(SignalName.Closed);
    }

    // ---------- 面板切换 ----------

    private void ShowMain()
    {
        _mainPanel.Visible = true;
        _settingsPanel.Visible = false;
        if (IsOpen) _continueButton.GrabFocus();
    }

    private void ShowSettings()
    {
        _mainPanel.Visible = false;
        _settingsPanel.Visible = true;
        SyncFromSettings();
        // 焦点直接落在第一个滑条上 —— 玩家一进来就能用左右键改音量，
        // 不用先按几次方向键找路。**焦点落在哪，决定了这个界面好不好用。**
        _masterSlider.GrabFocus();
    }

    private void OnHubPressed()
    {
        Close();
        SceneRouter.Instance.GoToHub();
    }

    private void OnThemePressed()
    {
        UiTheme.Instance.TogglePalette();
        _themeButton.Text = $"切换主题配色（当前：{(UiTheme.Instance.CurrentPalette == UiTheme.Palette.Ocean ? "冷调" : "暖调")}）";
    }

    private void OnResetPressed()
    {
        GameSettings.ResetToDefaults();
        SyncFromSettings();
    }

    // ---------- 设置 ----------

    private void OnVolumeChanged(string busName, double value)
    {
        if (_syncing) return;
        GameSettings.SetVolume(busName, (float)value);
    }

    private void OnFullscreenToggled(bool pressed)
    {
        if (_syncing) return;
        GameSettings.SetFullscreen(pressed);
    }

    private void SyncFromSettings()
    {
        _syncing = true;
        _masterSlider.Value = GameSettings.GetVolume(AudioManager.BusMaster);
        _musicSlider.Value = GameSettings.GetVolume(AudioManager.BusMusic);
        _sfxSlider.Value = GameSettings.GetVolume(AudioManager.BusSfx);
        _fullscreenCheck.ButtonPressed = GameSettings.IsFullscreen;
        _themeButton.Text = $"切换主题配色（当前：{(UiTheme.Instance.CurrentPalette == UiTheme.Palette.Ocean ? "冷调" : "暖调")}）";
        _syncing = false;
    }
}
