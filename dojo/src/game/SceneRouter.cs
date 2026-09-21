using Godot;
using Dojo.Common;

namespace Dojo.Game;

/// <summary>
/// 场景路由（Autoload 单例）—— 负责在「枢纽」和各个「练习站」之间切换。
///
/// 为什么不直接在按钮里写 GetTree().ChangeSceneToFile()：
///   因为切场景几乎总是还要做别的事：过场淡入淡出、记住当前在哪个站、清理临时状态。
///   把这些收在一个地方，将来加「加载动画」「进度条」时只需要改这里。
///
/// 学习要点：
///   1. GetTree().ChangeSceneToFile() 是「延迟切换」：它会在当前帧结束后才真正换场景，
///      所以紧跟在后面的代码仍然运行在旧场景里。
///   2. 用 Tween 做淡入淡出时，要把 Tween 的暂停模式设为 Process，
///      否则在暂停状态下过场会卡住（见 s06_ui 的暂停菜单）。
///   3. async/await 和 Godot 的 ToSignal 结合，可以把「等动画播完再继续」写得很干净。
/// </summary>
public partial class SceneRouter : Node
{
    public static SceneRouter Instance { get; private set; } = null!;

    public const string HubScenePath = "res://src/hub/hub.tscn";

    /// <summary>当前所在的练习站 id；在枢纽里为空字符串。</summary>
    public string CurrentStationId { get; private set; } = "";

    private ColorRect _fade = null!;
    private bool _transitioning;

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        // 启动时把玩家上次的设置生效一次（音量、全屏）。
        // 放在这里而不是某个场景里，是因为它必须在**任何**玩法场景之前执行 ——
        // 而且用 -Scene 直接跑单个练习站时也要生效。
        GameSettings.ApplyAll();

        // 过场遮罩放在很高的 CanvasLayer 上，保证盖住一切（包括 HUD）。
        var layer = new CanvasLayer { Name = "FadeLayer", Layer = 128 };

        _fade = new ColorRect
        {
            Name = "Fade",
            Color = new Color(0.055f, 0.063f, 0.09f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _fade.Modulate = new Color(1, 1, 1, 0);

        layer.AddChild(_fade);
        AddChild(layer);
    }

    public void GoToHub() => ChangeScene(HubScenePath, "");

    public void EnterStation(string stationId) => EnterStation(stationId, "");

    /// <summary>进入练习站。fragment 可以带一个初始状态提示（部分站会用到）。</summary>
    public void EnterStation(string stationId, string fragment)
    {
        var info = StationCatalog.Get(stationId);
        if (info is null)
        {
            GD.PushError($"[SceneRouter] unknown station id: {stationId}");
            return;
        }

        if (!info.Implemented)
        {
            EventBus.Instance.EmitNotice($"{info.DisplayName} 还在建设中，先玩玩别的吧");
            return;
        }

        ChangeScene(info.ScenePath, stationId);
    }

    public void RestartCurrentScene()
    {
        GetTree().ReloadCurrentScene();
    }

    private async void ChangeScene(string scenePath, string stationId)
    {
        if (_transitioning)
        {
            return;
        }

        _transitioning = true;
        try
        {
            await FadeTo(1f, 0.16f);

            var err = GetTree().ChangeSceneToFile(scenePath);
            if (err != Error.Ok)
            {
                GD.PushError($"[SceneRouter] failed to change scene to {scenePath}: {err}");
                await FadeTo(0f, 0.16f);
                return;
            }

            CurrentStationId = stationId;
            if (stationId.Length > 0)
                SaveSystem.Instance.Data.LastStationId = stationId;

            // 等新场景真正进入树之后再淡入，否则会闪过一帧旧画面。
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await FadeTo(0f, 0.22f);
        }
        finally
        {
            _transitioning = false;
        }
    }

    private async System.Threading.Tasks.Task FadeTo(float alpha, double seconds)
    {
        var tween = CreateTween();
        tween.SetPauseMode(Tween.TweenPauseMode.Process);
        tween.TweenProperty(_fade, "modulate:a", alpha, seconds);
        await ToSignal(tween, Tween.SignalName.Finished);
    }
}
