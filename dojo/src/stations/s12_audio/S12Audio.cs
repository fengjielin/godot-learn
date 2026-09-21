using System.Text;
using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// S12 · 音频
///
/// 按键：
///   `1` 打击音（±10% 随机音高）　`2` 拾取音　`3` 爆炸音（持续 0.75 秒）
///   `4` 音乐交叉淡入（A ↔ B）　`5` 静音切换
///
/// ★ **本站一个音频素材文件都没有。** 所有声音都是 `SoundKit` 在运行时
///   用一串 `[-1,1]` 的浮点数合成出来的。这不只是为了"仓库干净" ——
///   写一遍合成之后你会明白：**音效 = 波形 × 包络**，剩下的技巧都建立在这上面。
///
/// ★ 这一站最值得亲手试的一件事：**快速连按 `3` 超过 12 次**。
///   爆炸音长达 0.75 秒，而 SFX 池只有 12 个声道 ——
///   第 13 次按下时会**抢占最早的那个**。面板上的"正在发声"会卡在 12。
///   如果不做池化，每次播放都新建一个播放器，你会得到：
///   几百个节点、爆音、以及在你打得最热闹的时候卡一下。
///
/// ★ 音乐交叉淡入需要**两个播放器**（见 `AudioManager.CrossfadeMusic`）：
///   只有一个播放器的话，换曲时旧曲子会戛然而止 —— 你只能淡入新的，
///   没法让旧的逐渐消失。**"同时有两个"是交叉淡入的前提，不是优化。**
/// </summary>
public partial class S12Audio : StationBase
{
    public override string StationId => "s12_audio";

    private static readonly Rect2 PlayArea = new(40, 130, 1200, 560);

    private Label _panel = null!;
    private Node2D _deco = null!;

    /// <summary>
    /// 两首曲子缓存在**站台自己身上**，而不是 `SoundKit` 的静态字段里。
    /// 站台是节点，会被正确释放；静态字段不会 —— 那会导致退出时
    /// `ObjectDB instances were leaked at exit`（本工程踩过）。
    /// **Godot 对象要缓存就缓存在节点上。**
    /// </summary>
    private AudioStreamWav _musicA = null!;
    private AudioStreamWav _musicB = null!;

    private float _initialMaster = -1f;
    private bool _volumeChanged;
    private bool _poolSaturated;
    private bool _musicSwitched;
    private bool _manualMuted;
    private bool _autoMuted;
    private bool _musicIsA = true;
    private float _minPitch = 99f;
    private float _maxPitch = 0f;
    private float _crossfadeCountdown;
    private readonly bool[] _taskDone = new bool[5];

    protected override void StationReady()
    {
        _panel = GetNode<Label>("Panel");
        _deco = GetNode<Node2D>("Deco");

        LevelKit.CreateBorderWalls(this, PlayArea);
        BuildLabels();

        _initialMaster = AudioManager.Instance.GetBusVolumeLinear(AudioManager.BusMaster);

        _musicA = SoundKit.GetMusicA();
        _musicB = SoundKit.GetMusicB();
        AudioManager.Instance.CrossfadeMusic(_musicA, 1.2f);

        SetStatus("1 打击音　2 拾取音　3 爆炸音（连按超过 12 次看池子抢占）　4 音乐交叉淡入　5 静音");
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (_crossfadeCountdown > 0f) _crossfadeCountdown = Mathf.Max(0f, _crossfadeCountdown - (float)delta);
        TrackVolume();
        UpdatePanel();
        CheckGoals();
    }

    /// <summary>
    /// 切到后台自动静音（任务 ⑤）。
    ///
    /// 这是"尊重玩家"的一个小细节：玩家 Alt+Tab 出去接个电话，
    /// 游戏在后台继续放音乐是很烦的。
    /// **注意退出时要恢复**（`FocusIn`）—— 只静音不恢复是个 bug，
    /// 而且玩家会以为是游戏的音频坏了。
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut)
        {
            AudioManager.Instance.SetBusMuted(AudioManager.BusMaster, true);
            _autoMuted = true;
        }
        else if (what == NotificationApplicationFocusIn)
        {
            if (!_manualMuted) AudioManager.Instance.SetBusMuted(AudioManager.BusMaster, false);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("aux_1")) { PlayHit(); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_2")) { AudioManager.Instance.PlaySfxStream(SoundKit.Get("pickup"), -4f, 0.04f); Flash("拾取音"); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_3")) { AudioManager.Instance.PlaySfxStream(SoundKit.Get("explosion"), -6f, 0.08f); Flash("爆炸音（0.75 秒）"); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_4")) { CrossfadeMusic(); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_5")) { ToggleMute(); GetViewport().SetInputAsHandled(); return; }

        base._UnhandledInput(@event);
    }

    private void PlayHit()
    {
        // ±10% 音高（任务 ④）。**听感上的差别比参数上看起来大得多** ——
        // 同一个音效连播十次，每次都完全一样的话，耳朵会立刻腻。
        const float variation = 0.10f;
        AudioManager.Instance.PlaySfxStream(SoundKit.Get("hit"), -3f, variation);

        var pitch = AudioManager.Instance.LastPitchScale;
        _minPitch = Mathf.Min(_minPitch, pitch);
        _maxPitch = Mathf.Max(_maxPitch, pitch);

        SpawnRipple();
    }

    private void CrossfadeMusic()
    {
        _musicIsA = !_musicIsA;
        _musicSwitched = true;
        _crossfadeCountdown = 1.2f;
        AudioManager.Instance.CrossfadeMusic(_musicIsA ? _musicA : _musicB, 1.2f);
        Flash($"交叉淡入 → {(_musicIsA ? "A（清亮）" : "B（低沉）")}　1.2 秒内两首都听得见");
    }

    private void ToggleMute()
    {
        _manualMuted = !_manualMuted;
        AudioManager.Instance.SetBusMuted(AudioManager.BusMaster, _manualMuted);
        Flash($"Master 总线：{(_manualMuted ? "静音" : "恢复")}");
    }

    private void TrackVolume()
    {
        if (_volumeChanged || _initialMaster < 0f) return;
        var current = AudioManager.Instance.GetBusVolumeLinear(AudioManager.BusMaster);
        if (Mathf.Abs(current - _initialMaster) > 0.001f) _volumeChanged = true;
    }

    private void SpawnRipple()
    {
        var ring = LevelKit.MakeRing(_deco, new Vector2(640, 400), 20f, 3f,
            new Color(1f, 0.85f, 0.5f, 0.9f), "Ripple");

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(ring, "scale", new Vector2(2.6f, 2.6f), 0.35f);
        tween.TweenProperty(ring, "modulate:a", 0f, 0.35f);
        tween.Chain().TweenCallback(Callable.From(() => ring.QueueFree()));
    }

    // ---------- 判定 ----------

    private void CheckGoals()
    {
        // 下标严格对应 StationCatalog 里 S12 的任务顺序：
        //   0 两条总线 + 音量设置界面 · 1 SFX 复用池（12 个，超出抢占）
        //   2 音乐交叉淡入 · 3 ±10% 随机音高 · 4 静音快捷键 + 切后台自动静音
        var bus = AudioManager.Instance;
        var hasBuses = AudioServer.GetBusIndex(AudioManager.BusMusic) > 0
                       && AudioServer.GetBusIndex(AudioManager.BusSfx) > 0;
        if (hasBuses && (_volumeChanged || bus.TotalSfxPlayed > 0)) MarkOnce(0);

        if (bus.ActiveSfxVoices >= bus.SfxVoiceCapacity) _poolSaturated = true;
        if (_poolSaturated) MarkOnce(1);

        if (_musicSwitched) MarkOnce(2);

        if (bus.TotalSfxPlayed >= 5 && _maxPitch - _minPitch > 0.01f) MarkOnce(3);

        if (_manualMuted || _autoMuted) MarkOnce(4);

        if (!Array.TrueForAll(_taskDone, done => done)) return;

        Complete("总线、复用池、交叉淡入、随机音高、静音 —— **而且这些声音全是一行素材都没有、代码合成的**");
    }

    private void MarkOnce(int index)
    {
        if (_taskDone[index]) return;
        _taskDone[index] = true;
        MarkTaskDone(index);
    }

    private void BuildLabels()
    {
        _panel = LevelKit.MakeLabel(this, new Vector2(24, 58), "", 14,
            new Color(0.87f, 0.92f, 0.98f), HorizontalAlignment.Left, "AudioPanel");
        _panel.Size = new Vector2(766, 400);
    }

    private void UpdatePanel()
    {
        var bus = AudioManager.Instance;
        var sb = new StringBuilder();

        sb.Append($"【音频总线】Master {bus.GetBusVolumeLinear(AudioManager.BusMaster):0.00}　");
        sb.Append($"Music {bus.GetBusVolumeLinear(AudioManager.BusMusic):0.00}　");
        sb.Append($"Sfx {bus.GetBusVolumeLinear(AudioManager.BusSfx):0.00}\n");
        sb.Append("　→ 总线是**全局**的音量层：调 Music 不会影响音效。\n");
        sb.Append("　　 （按 ESC 打开暂停菜单，那里的滑块调的就是这三条总线）\n");
        sb.Append($"　总线静音：{(bus.GetBusVolumeLinear(AudioManager.BusMaster) <= 0.001f || _manualMuted ? "**已静音**" : "正常")}");
        if (_autoMuted) sb.Append("　（切到后台时自动静音过）");
        sb.Append("\n\n");

        sb.Append("【SFX 复用池】");
        sb.Append($"容量 {bus.SfxVoiceCapacity}　正在发声 **{bus.ActiveSfxVoices}**　累计播放 {bus.TotalSfxPlayed} 次\n");
        sb.Append("　→ 池子满了之后，第 13 次播放会**抢占最早的那个声道**，而不是新建一个播放器。\n");
        sb.Append("　→ 快速连按 `3` 十几次，看「正在发声」卡在容量上 —— 那就是池在工作。\n\n");

        sb.Append("【音高随机】±10%　");
        if (bus.TotalSfxPlayed == 0) sb.Append("（还没播过）\n");
        else sb.Append($"实测波动 {_minPitch:0.000} ~ {_maxPitch:0.000}（范围 {(_maxPitch - _minPitch) * 100f:0.0}%）\n");
        sb.Append("　→ 同一个音效连播十次一模一样的话，耳朵立刻会腻。**很便宜，效果很明显。**\n\n");

        sb.Append("【音乐交叉淡入】");
        sb.Append(_crossfadeCountdown > 0f
            ? $"**淡入中**（还剩 {_crossfadeCountdown:0.0}s，两首同时在放）\n"
            : $"当前 {(_musicIsA ? "A（清亮）" : "B（低沉）")}\n");
        sb.Append("　→ 需要**两个播放器**。只有一个的话，旧曲子会戛然而止。\n\n");

        sb.Append("【素材】**零个音频文件** —— 全部由 `SoundKit` 运行时合成\n");
        sb.Append("　音效 = **波形 × 包络**：同一个噪声，包络衰减 0.05 秒是「打击」，1.5 秒是「爆炸」");
        sb.Append("\n\n【按键】1 打击音　2 拾取音　3 爆炸音　4 音乐交叉淡入　5 静音");

        _panel.Text = sb.ToString();
    }
}
