using Godot;

namespace Dojo.Common;

/// <summary>
/// 音频管理（Autoload 单例）。
///
/// 现状说明：
///   这里已经具备「音频总线 / SFX 复用池 / 音高随机 / 音乐淡入」的骨架，
///   但完整的实现与练习任务在练习站 s12_audio 里。
///   在音频资源还没有生成出来之前，PlaySfx/PlayMusic 会静默跳过（不会报错）。
///
/// 学习要点：
///   1. 音频总线（AudioBus）是混音器的分组。做「音乐音量 / 音效音量」两个滑条，
///      靠的就是两条总线，而不是去改每个播放器的音量。
///   2. 音效必须复用播放器。每次播放都 new 一个 AudioStreamPlayer，
///      在高频音效（脚步、子弹）下会迅速堆积节点并造成卡顿 —— 见 s14_pooling。
///   3. 同一个音效每次用同样的音高播放会非常"电子"。±5%~10% 的随机音高能显著改善听感。
/// </summary>
public partial class AudioManager : Node
{
    public static AudioManager Instance { get; private set; } = null!;

    public const string BusMaster = "Master";
    public const string BusMusic = "Music";
    public const string BusSfx = "Sfx";

    /// <summary>同时可播放的音效数量。超出后按轮转方式抢占最早的播放器。</summary>
    [Export] public int SfxVoiceCount { get; set; } = 12;

    private readonly List<AudioStreamPlayer> _sfxVoices = new();
    private readonly Dictionary<string, AudioStream> _streamCache = new();

    private AudioStreamPlayer _music = null!;
    private AudioStreamPlayer _musicAlt = null!;
    private bool _crossfadeUseAlt;
    private string _currentMusicPath = "";
    private int _nextVoice;

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        EnsureBus(BusMusic);
        EnsureBus(BusSfx);
        _music = new AudioStreamPlayer { Name = "MusicPlayerA", Bus = BusMusic };
        AddChild(_music);
        // ★ 交叉淡入淡出**必须有两个播放器**：
        //   只有一个播放器的话，换曲时旧的会「戛然而止」——
        //   你只能淡入新的，没法让旧的逐渐消失。
        _musicAlt = new AudioStreamPlayer { Name = "MusicPlayerB", Bus = BusMusic };
        AddChild(_musicAlt);

        for (var i = 0; i < SfxVoiceCount; i++)
        {
            var player = new AudioStreamPlayer { Name = $"SfxVoice{i}", Bus = BusSfx };
            AddChild(player);
            _sfxVoices.Add(player);
        }

        GD.Print($"[AudioManager] {_sfxVoices.Count} sfx voices ready");
    }

    /// <summary>播放一个音效。资源不存在时直接返回，方便在资源做好之前先跑通玩法。</summary>
    public void PlaySfx(string resPath, float volumeDb = 0f, float pitchVariation = 0.06f)
    {
        if (_sfxVoices.Count == 0) return;

        var stream = GetStream(resPath);
        if (stream is null) return;

        var player = _sfxVoices[_nextVoice];
        _nextVoice = (_nextVoice + 1) % _sfxVoices.Count;

        player.Stream = stream;
        player.VolumeDb = volumeDb;
        player.PitchScale = pitchVariation <= 0f
            ? 1f
            : 1f + (float)GD.RandRange(-pitchVariation, pitchVariation);
        player.Play();
    }

    /// <summary>切换背景音乐。同一首正在播时不会重头开始。</summary>
    public void PlayMusic(string resPath, float fadeSeconds = 1.0f)
    {
        if (_currentMusicPath == resPath && _music.Playing) return;

        var stream = GetStream(resPath);
        if (stream is null) return;

        _currentMusicPath = resPath;
        _music.Stream = stream;
        _music.VolumeDb = -60f;
        _music.Play();
        FadePlayer(_music, 0f, fadeSeconds);
    }

    public void StopMusic(float fadeSeconds = 1.0f)
    {
        _currentMusicPath = "";
        FadePlayer(_music, -60f, fadeSeconds);
        FadePlayer(_musicAlt, -60f, fadeSeconds);
    }

    /// <summary>
    /// ★ 真正的**交叉淡入淡出**：新的那个渐强，旧的那个同时渐弱。
    ///
    /// 和 `PlayMusic` 的区别：`PlayMusic` 是"淡入新曲子"，旧曲子会在
    /// 新曲子开始的那一刻直接停掉 —— 在音乐性上是一个很突兀的断点。
    /// 交叉淡入让两首曲子**重叠一两秒**，过渡是连续的。
    ///
    /// **代价**：过渡期间两条音轨同时在解码播放，CPU 和内存都是双份。
    /// 所以 `fadeSeconds` 别设太长（1~2 秒足够），也别同时交叉太多首。
    /// </summary>
    public void CrossfadeMusic(AudioStream stream, float fadeSeconds = 1.2f)
    {
        var outgoing = _crossfadeUseAlt ? _music : _musicAlt;
        var incoming = _crossfadeUseAlt ? _musicAlt : _music;
        _crossfadeUseAlt = !_crossfadeUseAlt;

        incoming.Stream = stream;
        incoming.VolumeDb = -60f;
        incoming.Play();

        FadePlayer(incoming, 0f, fadeSeconds);
        if (outgoing.Playing) FadePlayer(outgoing, -60f, fadeSeconds);
    }

    /// <summary>直接播放一个已经拿到的流 —— 比如 S12 运行时**合成**出来的音效。</summary>
    public void PlaySfxStream(AudioStream stream, float volumeDb = 0f, float pitchVariation = 0.06f)
    {
        if (_sfxVoices.Count == 0) return;

        // 轮转取一个声道。**池子里只有 12 个，超了就抢占最早那个** ——
        // 这就是任务 ② 说的"复用池"：不新建播放器，而是覆盖最旧的那个。
        var player = _sfxVoices[_nextVoice];
        _nextVoice = (_nextVoice + 1) % _sfxVoices.Count;

        player.Stream = stream;
        player.VolumeDb = volumeDb;
        player.PitchScale = pitchVariation <= 0f
            ? 1f
            : 1f + (float)GD.RandRange(-pitchVariation, pitchVariation);
        player.Play();

        TotalSfxPlayed++;
        LastPitchScale = player.PitchScale;
    }

    /// <summary>正在发声的声道数 —— S12 的面板用它显示"池子用了几个"。</summary>
    public int ActiveSfxVoices
    {
        get
        {
            var active = 0;
            foreach (var voice in _sfxVoices) if (voice.Playing) active++;
            return active;
        }
    }

    public int SfxVoiceCapacity => _sfxVoices.Count;
    public int TotalSfxPlayed { get; private set; }
    public float LastPitchScale { get; private set; } = 1f;

    public override void _ExitTree() => ReleaseAllStreams();

    /// <summary>
    /// 引擎关停路径。
    ///
    /// ★ 为什么 `_ExitTree` 不够：用 `--quit-after`（或者直接关窗口）退出时，
    ///   引擎**不会走正常的场景树拆解流程**，`_ExitTree` 根本不会被调用。
    ///   结果就是 `--verbose` 里那两行：
    ///     `Leaked instance: AudioStreamWAV ... Reference count: 1`
    ///     `Leaked instance: AudioStreamPlaybackWAV ... Reference count: 1`
    ///   —— **正好是一个正在播放的音乐流和它的播放对象**。
    ///
    ///   所以要额外接住 `NotificationPredelete`：它在对象被销毁前触发，
    ///   是"最后一次说话的机会"。
    ///
    /// ★ 通用教训：**需要"退出时释放"的资源，要同时挂在 `_ExitTree` 和
    ///   `NotificationPredelete` 上。** 只挂前者，在非正常退出路径上会漏。
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationPredelete) ReleaseAllStreams();
    }

    private void ReleaseAllStreams()
    {
        if (!IsInstanceValid(this)) return;

        foreach (var voice in _sfxVoices)
        {
            if (!IsInstanceValid(voice)) continue;
            voice.Stop();
            voice.Stream = null;
        }

        foreach (var player in new[] { _music, _musicAlt })
        {
            if (!IsInstanceValid(player)) continue;
            player.Stop();
            player.Stream = null;
        }
    }

    /// <summary>用 0~1 的线性值设置某条总线的音量。</summary>
    public void SetBusVolumeLinear(string busName, float linear)
    {
        var idx = AudioServer.GetBusIndex(busName);
        if (idx < 0) return;
        AudioServer.SetBusVolumeDb(idx, linear <= 0.0001f ? -60f : Mathf.LinearToDb(linear));
    }

    public float GetBusVolumeLinear(string busName)
    {
        var idx = AudioServer.GetBusIndex(busName);
        if (idx < 0) return 0f;
        return Mathf.DbToLinear(AudioServer.GetBusVolumeDb(idx));
    }

    public void SetBusMuted(string busName, bool muted)
    {
        var idx = AudioServer.GetBusIndex(busName);
        if (idx >= 0) AudioServer.SetBusMute(idx, muted);
    }

    // ---------- 内部 ----------

    /// <summary>
    /// 确保总线存在。注意：更「正统」的做法是编辑 default_bus_layout.tres；
    /// 这里用代码创建是为了让工程在一开始就能跑起来，s12_audio 会换成资源文件方式。
    /// </summary>
    private static void EnsureBus(string busName)
    {
        if (AudioServer.GetBusIndex(busName) >= 0) return;

        var idx = AudioServer.BusCount;
        AudioServer.AddBus(idx);
        AudioServer.SetBusName(idx, busName);
        AudioServer.SetBusSend(idx, BusMaster);
        GD.Print($"[AudioManager] created audio bus '{busName}'");
    }

    private AudioStream? GetStream(string resPath)
    {
        if (_streamCache.TryGetValue(resPath, out var cached)) return cached;
        if (!ResourceLoader.Exists(resPath)) return null;

        var stream = GD.Load<AudioStream>(resPath);
        if (stream is null)
        {
            GD.PushWarning($"[AudioManager] {resPath} is not an AudioStream");
            return null;
        }

        _streamCache[resPath] = stream;
        return stream;
    }

    private void FadePlayer(AudioStreamPlayer player, float targetDb, float seconds)
    {
        var tween = CreateTween();
        // 即使游戏被暂停，音乐淡入淡出也应该继续
        tween.SetPauseMode(Tween.TweenPauseMode.Process);
        tween.TweenProperty(player, "volume_db", targetDb, seconds);
        if (targetDb <= -60f)
        {
            tween.TweenCallback(Callable.From(() =>
            {
                if (Mathf.IsEqualApprox(player.VolumeDb, -60f)) player.Stop();
            }));
        }
    }
}
