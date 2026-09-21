using Godot;

namespace Dojo.Common;

/// <summary>
/// 游戏设置的读写与生效（静态工具类）。
///
/// 学习要点：
///   1. **设置和存档是同一件事**：都是"玩家改过的状态要活过这次运行"。
///      所以它们共用 SaveSystem 的 Numbers 字典，不需要另开一套文件格式。
///   2. 设置分两步：**改**（写进存档）和**生效**（调 AudioServer / DisplayServer）。
///      这两步很容易漏掉其中一步 —— 典型 bug 是"滑条动了但音量没变"
///      或者"音量变了但重启又回去了"。所以统一收在 Set 系列方法里，两边一起做。
///   3. 启动时要 ApplyAll() 一次，否则玩家上次的设置不会生效。
///      本工程在 SceneRouter._Ready() 里调用（那时 SaveSystem 已经读完存档）。
/// </summary>
public static class GameSettings
{
    public const string MasterVolumeKey = "settings.master_volume";
    public const string MusicVolumeKey = "settings.music_volume";
    public const string SfxVolumeKey = "settings.sfx_volume";
    public const string FullscreenKey = "settings.fullscreen";

    public const float DefaultVolume = 0.8f;

    /// <summary>
    /// 本次运行里玩家改过几次音量。
    /// S06 用它来判断"你确实动过设置" —— 比去比较数值更可靠
    /// （否则玩家改完又改回 0.8，就检测不到了）。
    /// </summary>
    public static int VolumeChangeCount { get; private set; }

    // ---------- 音量 ----------

    public static float GetVolume(string busName)
        => SaveSystem.Instance.Data.Numbers.TryGetValue(VolumeKey(busName), out var value)
            ? value
            : DefaultVolume;

    public static void SetVolume(string busName, float linear)
    {
        linear = Mathf.Clamp(linear, 0f, 1f);
        SaveSystem.Instance.SetNumber(VolumeKey(busName), linear);
        AudioManager.Instance.SetBusVolumeLinear(busName, linear);
        SaveSystem.Instance.Save();
        VolumeChangeCount++;
    }

    // ---------- 全屏 ----------

    public static bool IsFullscreen
        => SaveSystem.Instance.Data.Numbers.TryGetValue(FullscreenKey, out var value) && value > 0.5f;

    public static void SetFullscreen(bool fullscreen)
    {
        SaveSystem.Instance.SetNumber(FullscreenKey, fullscreen ? 1f : 0f);
        ApplyFullscreen(fullscreen);
        SaveSystem.Instance.Save();
    }

    // ---------- 启动时统一生效 ----------

    /// <summary>把存档里的设置全部应用一遍。启动时调用一次。</summary>
    public static void ApplyAll()
    {
        AudioManager.Instance.SetBusVolumeLinear(AudioManager.BusMaster, GetVolume(AudioManager.BusMaster));
        AudioManager.Instance.SetBusVolumeLinear(AudioManager.BusMusic, GetVolume(AudioManager.BusMusic));
        AudioManager.Instance.SetBusVolumeLinear(AudioManager.BusSfx, GetVolume(AudioManager.BusSfx));
        ApplyFullscreen(IsFullscreen);

        GD.Print($"[GameSettings] applied: master={GetVolume(AudioManager.BusMaster):0.00} " +
                 $"music={GetVolume(AudioManager.BusMusic):0.00} sfx={GetVolume(AudioManager.BusSfx):0.00} " +
                 $"fullscreen={IsFullscreen}");
    }

    public static void ResetToDefaults()
    {
        SetVolume(AudioManager.BusMaster, DefaultVolume);
        SetVolume(AudioManager.BusMusic, DefaultVolume);
        SetVolume(AudioManager.BusSfx, DefaultVolume);
        SetFullscreen(false);
    }

    private static string VolumeKey(string busName) => busName switch
    {
        AudioManager.BusMusic => MusicVolumeKey,
        AudioManager.BusSfx => SfxVolumeKey,
        _ => MasterVolumeKey,
    };

    private static void ApplyFullscreen(bool fullscreen)
    {
        // 无头模式（自动化验证）下没有真正的窗口，改了也没意义，直接跳过。
        if (DisplayServer.GetName() == "headless") return;

        DisplayServer.WindowSetMode(fullscreen
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed);
    }
}
