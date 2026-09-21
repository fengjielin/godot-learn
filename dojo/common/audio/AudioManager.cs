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
    private string _currentMusicPath = "";
    private int _nextVoice;

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        EnsureBus(BusMusic);
        EnsureBus(BusSfx);

        _music = new AudioStreamPlayer { Name = "MusicPlayer", Bus = BusMusic };
        AddChild(_music);

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
