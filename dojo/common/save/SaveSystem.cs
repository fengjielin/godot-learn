using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Dojo.Common;

/// <summary>
/// 存档数据结构。用普通的 C# 类 + System.Text.Json 序列化。
///
/// 设计取舍：
///   - 之所以不用 Godot 的 Resource 存档，是因为 JSON 更透明：出问题时用记事本就能看明白。
///   - Version 字段是「存档版本号」。以后字段变了，可以据此写迁移逻辑，而不是让老存档直接崩。
///   - Numbers / Texts 是给各练习站留的「通用便签」，避免每加一个站就要改一次存档结构。
/// </summary>
public sealed class SaveData
{
    public int Version { get; set; } = 1;

    /// <summary>最后进入的练习站 id，用于「继续上次」。</summary>
    public string LastStationId { get; set; } = "";

    /// <summary>已完成的练习站 id 列表。</summary>
    public List<string> CompletedStations { get; set; } = new();

    /// <summary>各练习站自己存的数值（完成次数、最好成绩……）。</summary>
    public Dictionary<string, float> Numbers { get; set; } = new();

    /// <summary>各练习站自己存的小文本。</summary>
    public Dictionary<string, string> Texts { get; set; } = new();
}

/// <summary>
/// 存档系统（Autoload 单例）。
///
/// 学习要点（对应练习站 s11_save）：
///   1. user:// 是「可写」目录。res:// 打包后是只读的，绝对不能往里写东西。
///      Windows 上 user:// 实际位于
///      %APPDATA%\Godot\app_userdata\&lt;项目名&gt;\
///   2. 存读档就是「序列化 → 写文件」和「读文件 → 反序列化」。难点不在 API，
///      而在于：版本迁移、损坏存档的处理、以及「什么时候存」（别在每帧存）。
///   3. 用 ProjectSettings.GlobalizePath() 可以把 user:// 换成真实的系统路径，
///      这样就能用标准 .NET 文件 API，方便单元测试。
///
/// 关于 --save-dir= 参数：
///   本工程支持命令行覆盖存档目录（见 docs/01-如何使用练功房.md）。
///   这是「让代码可测试」的一个小示范 —— 把外部依赖变成可注入的参数。
/// </summary>
public partial class SaveSystem : Node
{
    public static SaveSystem Instance { get; private set; } = null!;

    private const string SaveFileName = "dojo_save.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // 保留中文字符，不要转成 \uXXXX，这样存档文件人类可读
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private string _directory = "user://";

    public SaveData Data { get; private set; } = new();

    /// <summary>存档文件的真实系统路径。</summary>
    public string SaveFilePath => Path.Combine(
        ProjectSettings.GlobalizePath(_directory).TrimEnd('/', '\\'),
        SaveFileName);

    public override void _EnterTree()
    {
        Instance = this;

        // 允许用 --save-dir=<路径> 覆盖存档位置（自动化测试、多存档对比时很有用）。
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--save-dir="))
            {
                _directory = arg["--save-dir=".Length..];
                GD.Print($"[SaveSystem] save dir overridden by cmdline: {_directory}");
            }
        }

        Load();
    }

    public void Load()
    {
        var path = SaveFilePath;
        try
        {
            if (!File.Exists(path))
            {
                Data = new SaveData();
                GD.Print($"[SaveSystem] no save file yet ({path}), starting fresh");
                return;
            }

            var json = File.ReadAllText(path);
            Data = JsonSerializer.Deserialize<SaveData>(json, JsonOptions) ?? new SaveData();
            Migrate(Data);
            GD.Print($"[SaveSystem] loaded {Data.CompletedStations.Count} completed station(s) from {path}");
        }
        catch (Exception ex)
        {
            // 存档损坏时不应该让游戏崩掉 —— 退回全新存档，并保留坏文件供排查。
            GD.PushWarning($"[SaveSystem] failed to load save, starting fresh. {ex.Message}");
            TryBackupBrokenSave(path);
            Data = new SaveData();
        }
    }

    public void Save()
    {
        var path = SaveFilePath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(Data, JsonOptions));
        }
        catch (Exception ex)
        {
            GD.PushError($"[SaveSystem] failed to write save to {path}: {ex.Message}");
        }
    }

    // ---------- 练习进度 ----------

    public bool IsStationCompleted(string stationId)
        => Data.CompletedStations.Contains(stationId);

    public int CompletedCount => Data.CompletedStations.Count;

    /// <summary>标记练习站完成。重复完成不会重复记录，但会累加完成次数。</summary>
    public void MarkStationCompleted(string stationId)
    {
        if (!Data.CompletedStations.Contains(stationId))
            Data.CompletedStations.Add(stationId);

        var key = $"{stationId}.clear_count";
        Data.Numbers.TryGetValue(key, out var n);
        Data.Numbers[key] = n + 1f;

        Save();
    }

    /// <summary>清空全部进度（Hub 里按 Delete 会用到）。</summary>
    public void ResetAll()
    {
        Data = new SaveData();
        Save();
        GD.Print("[SaveSystem] progress reset");
    }

    // ---------- 通用便签 ----------

    public float GetNumber(string key, float fallback = 0f)
        => Data.Numbers.TryGetValue(key, out var v) ? v : fallback;

    public void SetNumber(string key, float value)
    {
        Data.Numbers[key] = value;
    }

    public string GetText(string key, string fallback = "")
        => Data.Texts.TryGetValue(key, out var v) ? v : fallback;

    public void SetText(string key, string value)
    {
        Data.Texts[key] = value;
    }

    // ---------- 内部 ----------

    /// <summary>
    /// 存档版本迁移。现在只有 v1，所以什么都不用做；
    /// 但这个函数的位置必须先留出来，以后加字段时才不会手忙脚乱。
    /// </summary>
    private static void Migrate(SaveData data)
    {
        if (data.Version < 1)
        {
            data.Version = 1;
        }
    }

    private static void TryBackupBrokenSave(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var backup = path + ".broken";
            File.Copy(path, backup, overwrite: true);
            GD.PushWarning($"[SaveSystem] broken save backed up to {backup}");
        }
        catch
        {
            // 备份失败也不该影响游戏启动
        }
    }
}
