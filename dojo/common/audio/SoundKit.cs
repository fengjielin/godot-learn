using Godot;

namespace Dojo.Common;

/// <summary>
/// 运行时**合成**音效与音乐。
///
/// ★ 为什么要在代码里合成，而不是放几个 `.wav` 文件：
///   ① 本工程有一条硬规矩：**不引入任何第三方素材**（授权干净、仓库干净）。
///   ② 更重要的是 —— **合成是"理解音频数据"最快的方式**。
///      你写完这个文件之后就会明白：一个音效不过是
///      **一串 [-1, 1] 之间的浮点数**，经过"采样率"变成波形，
///      再经过"包络"（音量随时间变化）变成"有起有落的声音"。
///      剩下的所有音频技巧 —— 淡入淡出、混音、滤波、压缩 —— 都建立在这上面。
///
/// ★ 声音的三要素（本站每个音效都是按这个套路写的）：
///   · **音高**（每秒振动的次数）→ 决定"听起来是什么音"
///   · **包络**（音量怎么随时间衰减）→ 决定"听起来是什么材质"。
///     同样是噪声，衰减 0.05 秒是"打击"，衰减 1.5 秒是"爆炸"。
///   · **波形**（正弦 / 方波 / 噪声）→ 决定"听起来是乐器还是打击"
///
/// ★ 顺带一个真话：**用代码合成的音效做原型很够用，做正式产品通常不够。**
///   它的最大价值是"在美术还没给你素材之前，让玩法能跑起来、手感能被评估"。
/// </summary>
public static class SoundKit
{
    public const int MixRate = 22050;

    /// <summary>
    /// 合成一个音效。
    ///
    /// ★ 这里**故意不做静态缓存**。
    ///   最初的版本用一个 `static Dictionary<string, AudioStreamWav>` 缓存结果，
    ///   跑起来一切正常 —— 但退出时报了
    ///   `WARNING: 2 ObjectDB instances were leaked at exit`。
    ///   原因：**静态字段的生命周期比引擎还长**，引擎关停时字典里还握着
    ///   Godot 对象，于是它们被判定为"泄漏"。
    ///
    ///   教训是通用的：**Godot 对象不要放在静态字段里。**
    ///   要缓存就缓存在**节点**上（节点会被正确释放），
    ///   比如 S12 站台把两首曲子存在自己的实例字段里。
    ///
    ///   代价：每次调用都重新合成。0.14 秒的音效只有 3000 个采样点，
    ///   合成耗时以微秒计 —— **这点开销远比"退出时泄漏"划算。**
    /// </summary>
    public static AudioStreamWav Get(string id) => id switch
    {
        "hit" => Build(0.14f, HitSample),
        "pickup" => Build(0.16f, PickupSample),
        "explosion" => Build(0.75f, ExplosionSample),
        "blip" => Build(0.10f, BlipSample),
        "error" => Build(0.22f, ErrorSample),
        _ => Build(0.2f, BlipSample),
    };

    /// <summary>两首风格不同的循环曲，用来演示交叉淡入。</summary>
    public static AudioStreamWav GetMusicA()
        => BuildMusic(new[] { 220f, 277f, 330f, 277f }, 0.5f, saw: false);

    public static AudioStreamWav GetMusicB()
        => BuildMusic(new[] { 147f, 196f, 175f, 131f }, 0.5f, saw: true);

    // ---------- 包络 ----------

    /// <summary>
    /// 指数衰减包络：`attack` 秒内升到最大，之后按 `decayRate` 指数衰减。
    ///
    /// **包络是"音效听起来像什么"的最主要因素**，比波形本身重要得多。
    /// 把攻击音和爆炸音换成同一个噪声波形、只改包络，你依然能听出区别。
    /// </summary>
    private static float Envelope(float t, float duration, float attack, float decayRate)
    {
        if (t < 0 || t > duration) return 0f;
        if (t < attack) return t / attack;
        return Mathf.Exp(-(t - attack) * decayRate);
    }

    // ---------- 各个音效的"采样函数" ----------

    private static float HitSample(float t, RandomNumberGenerator rng)
    {
        // 噪声 + 快速衰减 = 打击感。低频正弦叠一点"重量"。
        var noise = (float)GD.RandRange(-1.0, 1.0) * 0.6f;
        var body = Mathf.Sin(Mathf.Tau * 160f * t) * 0.4f;
        return (noise + body) * Envelope(t, 0.14f, 0.003f, 42f);
    }

    private static float PickupSample(float t)
    {
        // 两个上行音 = "拿到了"的经典音型
        var frequency = t < 0.07f ? 880f : 1320f;
        return Mathf.Sin(Mathf.Tau * frequency * t) * 0.5f * Envelope(t, 0.16f, 0.005f, 18f);
    }

    private static float ExplosionSample(float t, RandomNumberGenerator rng)
    {
        // 同样的噪声，包络慢得多 → 听起来就是"爆炸"而不是"打击"
        var noise = (float)GD.RandRange(-1.0, 1.0);
        var rumble = Mathf.Sin(Mathf.Tau * 55f * t) * 0.5f;
        return (noise * 0.7f + rumble) * Envelope(t, 0.75f, 0.01f, 6.5f);
    }

    private static float BlipSample(float t)
        => Mathf.Sin(Mathf.Tau * 660f * t) * 0.45f * Envelope(t, 0.10f, 0.004f, 26f);

    private static float ErrorSample(float t)
    {
        // 下行 + 方波 = "不行"
        var square = Mathf.Sin(Mathf.Tau * (420f - 900f * t) * t) > 0f ? 1f : -1f;
        return square * 0.3f * Envelope(t, 0.22f, 0.004f, 12f);
    }

    /// <summary>
    /// 一段循环音乐：依次播放几个音，每个音有自己的衰减。
    /// 用了锯齿波（叠加奇次谐波）让它比纯正弦"厚"一点。
    /// </summary>
    private static AudioStreamWav BuildMusic(float[] notes, float noteSeconds, bool saw)
    {
        var total = notes.Length * noteSeconds;
        return Build(total, (t, _) =>
        {
            var index = Mathf.Min(notes.Length - 1, (int)(t / noteSeconds));
            var local = t - index * noteSeconds;
            var frequency = notes[index];

            var value = saw
                ? Saw(frequency, t)
                : Mathf.Sin(Mathf.Tau * frequency * t);

            // 也叠一个低八度当贝斯
            var bass = Mathf.Sin(Mathf.Tau * frequency * 0.5f * t) * 0.35f;
            return (value * 0.28f + bass) * Envelope(local, noteSeconds, 0.02f, 2.6f);
        });
    }

    /// <summary>锯齿波：把相位取小数部分再映射到 [-1,1]。比正弦"亮"。</summary>
    private static float Saw(float frequency, float t)
    {
        var phase = frequency * t;
        return (phase - Mathf.Floor(phase)) * 2f - 1f;
    }

    // ---------- 采样 → AudioStreamWav ----------

    private static AudioStreamWav Build(float seconds, Func<float, RandomNumberGenerator, float> sample)
    {
        // ★ 每个音效各用**自己**的随机数发生器。
        //   如果用全局随机，那么"合成音效"就会污染 S15 讲的那个全局随机状态 ——
        //   生成地图前多播一个音效，地图就变了。**这个坑是通用的。**
        var rng = new RandomNumberGenerator();
        rng.Randomize();

        var count = (int)(seconds * MixRate);
        var data = new byte[count * 2];   // 16 位单声道 = 每个采样 2 字节

        for (var i = 0; i < count; i++)
        {
            var t = i / (float)MixRate;
            var value = Mathf.Clamp(sample(t, rng), -1f, 1f);

            // 16 位有符号小端
            var scaled = (short)(value * 32000);
            data[i * 2] = (byte)(scaled & 0xFF);
            data[i * 2 + 1] = (byte)((scaled >> 8) & 0xFF);
        }

        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = MixRate,
            Stereo = false,
            Data = data,
        };
    }

    private static AudioStreamWav Build(float seconds, Func<float, float> sample)
        => Build(seconds, (t, _) => sample(t));
}
