using System.Text.Json;
using Godot;

namespace Dojo.Stations;

/// <summary>
/// 一个存档槽里的数据。
///
/// ★ 版本号是**第一个**要写进去的字段，而且从第一版就要写。
///   没有它，你以后改了数据结构就没法判断"这个文件是哪个年代的"，
///   只能靠猜字段在不在 —— 那会写出一堆 if。
///
/// ★ 这个类的字段全部是**可序列化的简单类型**（数字/字符串/列表）。
///   绝对不要把 `Node`、`Resource`、`Vector2` 之类的东西直接塞进来：
///   存的时候你以为是位置，读的时候可能拿到一个已经不存在的场景引用。
///   **存"数据"，不存"对象"。**
/// </summary>
public sealed class SlotData
{
    /// <summary>当前存档格式版本。加字段就 +1。</summary>
    public int Version { get; set; } = CurrentVersion;

    public const int CurrentVersion = 2;

    public string SavedAt { get; set; } = "";

    // ---- v1 就有的字段 ----
    public float PlayerX { get; set; }
    public float PlayerY { get; set; }
    public int Coins { get; set; }
    public List<string> Unlocked { get; set; } = new();

    // ---- v2 新增的字段 ----
    // 老存档里没有这一项，反序列化后是 null —— 由 Migrate 补上默认值。
    public string LastStation { get; set; } = "";

    /// <summary>游戏设置也存进存档（任务 ③）。</summary>
    public float MasterVolume { get; set; } = 0.8f;

    /// <summary>
    /// 校验和（任务 ②）。
    ///
    /// ★ 它不是防作弊，是**防"看不懂的崩溃"**：
    ///   文件被截断、被玩家手改错了一个标点、磁盘写了一半断电 ——
    ///   这些情况 JSON 反序列化可能"成功"但内容已经不对了。
    ///   有了校验和，你能明确区分"存档坏了"和"存档是好的但数据奇怪"，
    ///   前者该退回默认值，后者该去查逻辑。
    ///
    /// 注意：这**不是**加密，玩家想改还是能改（改完重算校验和即可）。
    /// **校验和解决的是"意外损坏"，不是"恶意篡改"。**
    /// </summary>
    public string Checksum { get; set; } = "";

    public static string ComputeChecksum(SlotData d)
        => $"{d.Version}|{d.PlayerX:0.###}|{d.PlayerY:0.###}|{d.Coins}|{d.Unlocked.Count}|{d.MasterVolume:0.###}";

    public bool Verify() => Checksum == ComputeChecksum(this);

    /// <summary>
    /// 把老版本的数据补齐到当前版本。
    ///
    /// ★ 迁移的核心原则：**只往前补，不改已有的含义。**
    ///   如果连老字段的含义也要改，那就应该新加一个字段 + 在迁移里做转换，
    ///   而不是就地改原字段 —— 否则你无法同时支持新旧两种存档。
    /// </summary>
    public static void Migrate(SlotData data)
    {
        if (data.Version >= CurrentVersion) return;

        if (data.Version < 2)
        {
            // v1 → v2：补上 LastStation
            if (string.IsNullOrEmpty(data.LastStation))
                data.LastStation = "(由 v1 旧存档迁移而来，无此字段)";
        }

        data.Version = CurrentVersion;
    }
}

/// <summary>
/// 三槽位存档管理器。
///
/// ★ 为什么存到 `user://` 而不是 `res://`：
///   **导出成可执行文件之后，`res://` 是只读的**（它被打进了 .pck）。
///   往那里写文件在编辑器里能跑，导出后必然失败 —— 而且报错往往很晚才被发现。
///   规则很简单：**游戏自带的资源用 `res://`，玩家产生的数据一律用 `user://`。**
///
/// ★ 为什么用 JSON 而不是二进制：
///   存档是**你最需要调试**的东西之一。JSON 可以直接用记事本打开看、
///   可以手动改一行来复现 bug、可以在版本迁移出错时对比前后差异。
///   二进制存档体积小、难被玩家改，但会让你的调试成本显著上升。
///   对这个规模的游戏，**可读性远比那几 KB 重要**。
/// </summary>
public sealed class SaveSlots
{
    public const int SlotCount = 3;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,   // 缩进！存档是给人看的
    };

    public string DirectoryPath { get; }
    public int CurrentSlot { get; private set; } = 1;

    public SaveSlots()
    {
        DirectoryPath = Path.Combine(OS.GetUserDataDir(), "saves");
        System.IO.Directory.CreateDirectory(DirectoryPath);
    }

    public string PathFor(int slot) => Path.Combine(DirectoryPath, $"slot{slot}.json");

    /// <summary>切换当前槽位。**只是切换，不做读写** —— 读写要玩家明确按键。</summary>
    public void Select(int slot) => CurrentSlot = Mathf.Clamp(slot, 1, SlotCount);

    public void Write(int slot, SlotData data)
    {
        data.Version = SlotData.CurrentVersion;
        data.SavedAt = DateTime.Now.ToString("HH:mm:ss");
        data.Checksum = SlotData.ComputeChecksum(data);   // ★ 校验和必须在**写完所有字段之后**算
        File.WriteAllText(PathFor(slot), JsonSerializer.Serialize(data, Options));
    }

    /// <summary>读一个槽位。文件不存在或损坏都返回 null，**绝不让游戏崩掉**。</summary>
    public SlotData? Read(int slot)
    {
        var path = PathFor(slot);
        if (!File.Exists(path)) return null;

        try
        {
            var data = JsonSerializer.Deserialize<SlotData>(File.ReadAllText(path));
            if (data is null) return null;

            SlotData.Migrate(data);
            return data;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[S11] 槽位 {slot} 读取失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 只看文件里**写的**版本号，不做迁移。
    ///
    /// ★ 这里踩过一个坑：面板原本用 `Read()` 返回的 `Version` 判断"这是不是旧存档"，
    ///   但 `Read()` 内部**已经迁移过了**，所以版本永远是当前的 ——
    ///   "这是 v1 旧存档"这个信息在读取的那一刻就被抹掉了。
    ///   教训：**"文件里是什么"和"内存里是什么"是两件事，
    ///   想知道前者就必须在迁移之前先看一眼。**
    /// </summary>
    public int PeekVersion(int slot)
    {
        var path = PathFor(slot);
        if (!File.Exists(path)) return 0;

        try
        {
            var text = File.ReadAllText(path);
            var index = text.IndexOf("\"Version\"", StringComparison.Ordinal);
            if (index < 0) return 1;   // 连版本号都没有 = 最老的格式

            var colon = text.IndexOf(':', index);
            var end = colon + 1;
            while (end < text.Length && (char.IsDigit(text[end]) || text[end] == ' ')) end++;
            var digits = text[(colon + 1)..end].Trim();
            return int.TryParse(digits, out var version) ? version : 1;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>原始文本 —— 面板直接显示它，让你看清"存档里到底有什么"。</summary>
    public string ReadRaw(int slot)
    {
        var path = PathFor(slot);
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }

    public void Delete(int slot)
    {
        var path = PathFor(slot);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>
    /// 手工写一份 **v1 格式**的存档（缺少 v2 的 `LastStation` 字段），
    /// 用来演示迁移。真实场景里它就是"上一版游戏留下的存档"。
    /// </summary>
    public void WriteLegacyV1(int slot, float x, float y, int coins)
    {
        var json = $$"""
        {
          "Version": 1,
          "SavedAt": "上一版游戏存的",
          "PlayerX": {{x}},
          "PlayerY": {{y}},
          "Coins": {{coins}},
          "Unlocked": ["老玩家徽章"]
        }
        """;
        File.WriteAllText(PathFor(slot), json);
    }

    /// <summary>故意把槽位文件改坏（改一个数字但不更新校验和）—— 演示任务 ② 的检测能力。</summary>
    public void Corrupt(int slot)
    {
        var path = PathFor(slot);
        if (!File.Exists(path)) return;

        var text = File.ReadAllText(path);
        // 只动 PlayerX，不动 Checksum —— 校验和自然会不匹配
        File.WriteAllText(path, text.Replace("\"PlayerX\": 0", "\"PlayerX\": 99999"));
    }
}
