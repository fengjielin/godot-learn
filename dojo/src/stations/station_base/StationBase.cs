using Godot;
using Dojo.Common;
using Dojo.Game;
using Dojo.Ui;
namespace Dojo.Stations;

/// <summary>
/// 所有练习站的基类。它负责每个站都要做的那几件事：
///   · 自动装配通用 HUD（标题、练习任务、状态行）
///   · 统一处理 TAB / R / ESC
///   · 记录通关进度
///
/// 一个新练习站只需要：
///   1. 继承 StationBase
///   2. override StationId，值与目录名 / 场景名一致
///   3. 在 StationReady() 里搭自己的玩法
///   4. 达成目标时调用 Complete()
///
/// 学习要点（对应练习站 s04_fsm 与 s13_events）：
///   1. 这是个很典型的「模板方法」模式：基类定好骨架与顺序，子类只填变化的部分。
///      比在每个站里复制粘贴一遍 HUD 装配和按键处理要可靠得多。
///   2. _Ready 的执行顺序是「子节点先，父节点后」。所以 StationReady() 里可以放心
///      GetNode 拿场景里的固定子节点 —— 它们的 _Ready 早就跑完了。
/// </summary>
public partial class StationBase : Node2D
{
    private const string HudScenePath = "res://src/ui/station_hud/station_hud.tscn";
    private const string PauseMenuScenePath = "res://src/ui/pause_menu/pause_menu.tscn";

    /// <summary>
    /// 子类必须覆盖，且必须与目录名、场景名一致，
    /// 例如 "s01_lifecycle" 对应 src/stations/s01_lifecycle/s01_lifecycle.tscn
    /// </summary>
    public virtual string StationId => "";

    protected StationHud Hud { get; private set; } = null!;
    protected StationInfo Info { get; private set; } = null!;

    /// <summary>所有练习站共用的暂停菜单（ESC 打开）。S06 会用到它来做练习判定。</summary>
    protected PauseMenu Pause { get; private set; } = null!;

    private bool _completed;

    public override void _Ready()
    {
        Info = StationCatalog.Get(StationId)
               ?? throw new InvalidOperationException(
                   $"StationId '{StationId}' 不在 StationCatalog 里。请检查拼写，或先到 StationCatalog 里登记。");

        var hudScene = GD.Load<PackedScene>(HudScenePath)
                       ?? throw new InvalidOperationException($"加载不到 {HudScenePath}");

        Hud = hudScene.Instantiate<StationHud>();
        AddChild(Hud);

        // 暂停菜单也是通用的：每个站都有，都长一样。
        // 这就是"做一个 UI 组件"和"在每个站里复制一遍"的区别。
        var pauseScene = GD.Load<PackedScene>(PauseMenuScenePath)
                         ?? throw new InvalidOperationException($"加载不到 {PauseMenuScenePath}");
        Pause = pauseScene.Instantiate<PauseMenu>();
        AddChild(Pause);

        var alreadyCompleted = SaveSystem.Instance.IsStationCompleted(StationId);
        Hud.Setup(Info, alreadyCompleted);
        if (alreadyCompleted)
            Hud.FlashStatus("这一站你已经通关过了 —— 换个做法再试一次？", 3.5);

        EventBus.Instance.EmitStationEntered(StationId);
        SaveSystem.Instance.Data.LastStationId = StationId;

        StationReady();
    }

    /// <summary>子类的初始化入口。此时 Hud 已经就绪，场景里的固定子节点也已 _Ready。</summary>
    protected virtual void StationReady()
    {
    }

    public override void _ExitTree()
    {
        // 顿帧改的是**全局**的 Engine.TimeScale。如果带着 0.05 的时间缩放离开当前站，
        // 下一个场景会以二十分之一的速度运行 —— 看起来就像游戏卡死了，
        // 而且因为没有任何报错，会非常难查。所以离开任何一站都要强制复位。
        ScreenFx.Instance.CancelAll();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("toggle_tasks"))
        {
            Hud.ToggleTasks();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("restart"))
        {
            SceneRouter.Instance.RestartCurrentScene();
            GetViewport().SetInputAsHandled();
        }

        // 注意：这里**不处理** ui_cancel（ESC）。
        // ESC 归 PauseMenu 管 —— 同一个输入只应该有一个负责人，
        // 否则"暂停菜单打开了又立刻被关掉"这类竞态会非常难查。
    }

    /// <summary>达成练习目标时调用。重复调用只有第一次生效。</summary>
    protected void Complete(string reason = "")
    {
        if (_completed) return;

        _completed = true;
        SaveSystem.Instance.MarkStationCompleted(StationId);
        EventBus.Instance.EmitStationCompleted(StationId);
        Hud.ShowCompleted(reason);
    }

    protected void SetStatus(string text) => Hud.SetStatus(text);

    protected void Flash(string text, double seconds = 2.0) => Hud.FlashStatus(text, seconds);

    protected void MarkTaskDone(int index) => Hud.SetTaskDone(index, true);
}
