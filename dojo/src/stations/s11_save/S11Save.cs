using System.Text;
using Godot;
using Dojo.Common;
using Dojo.Entities;

namespace Dojo.Stations;

/// <summary>
/// S11 · 存档与读档
///
/// 一个房间里散着金币，你走过去就捡。把位置和金币数存进三个独立槽位之一，
/// 再读回来 —— 玩家会被**瞬移回存档时的位置**，金币数也一起还原。
///
/// 按键：
///   `1` `2` `3`  选择槽位
///   `4`          保存到当前槽位
///   `5`          从当前槽位读取
///   `6`          删除当前槽位
///
/// ★ **槽位 3 里预置了一份 v1 格式的旧存档**（缺少 v2 新加的字段）。
///   读它，你会看到版本迁移把缺的字段补上默认值 —— 这就是任务 ⑤ 的答案。
///
/// ★ 本站想让你记住的四件事：
///   ① 存到 `user://`，**不要存 `res://`** —— 导出后 `res://` 是只读的。
///   ② 存档用 JSON，**可读性就是可调试性**。面板会把原始 JSON 直接显示出来。
///   ③ **版本号从第一版就要写**，迁移逻辑只往前补、不改旧字段含义。
///   ④ **存档损坏时不能崩** —— 退回默认值并保留坏文件，而不是抛异常。
/// </summary>
public partial class S11Save : StationBase
{
    public override string StationId => "s11_save";

    private static readonly Rect2 PlayArea = new(40, 130, 1200, 560);

    private static readonly Vector2[] CoinSpots =
    {
        new(300, 250), new(460, 320), new(620, 250), new(780, 350),
        new(940, 260), new(1080, 420), new(520, 560), new(860, 580),
    };

    private const float AutoSaveInterval = 12f;
    private const float PickupRadius = 34f;

    private Player _player = null!;
    private Node2D _coinsRoot = null!;
    private Label _panel = null!;

    private readonly List<Node2D> _coins = new();
    private SaveSlots _slots = null!;
    private int _coinCount;
    private readonly List<string> _unlocked = new();

    private float _autoSaveTimer;
    private int _saveCount;
    private int _loadCount;
    private int _autoSaves;
    private int _legacyLoaded;
    private int _checksumOk;
    private int _checksumBad;
    private int _settingsCarried;
    private bool _autoCorrupted;
    private bool _pathSeen;
    private string _lastMessage = "";
    private readonly bool[] _savedSlots = new bool[SaveSlots.SlotCount + 1];
    private readonly bool[] _taskDone = new bool[5];

    protected override void StationReady()
    {
        _player = GetNode<Player>("Player");
        _coinsRoot = GetNode<Node2D>("Coins");
        _coinsRoot.Name = "Coins";

        _slots = new SaveSlots();

        // 槽位 3 预置一份 v1 旧存档（只在不存在时写，免得覆盖你自己的实验结果）
        if (!File.Exists(_slots.PathFor(3)))
            _slots.WriteLegacyV1(3, 640f, 620f, 99);

        LevelKit.CreateBorderWalls(this, PlayArea);
        BuildCoins();
        BuildLabels();
        AutoSave("进入本站（自动存档：每次进入练习站存一次）");

        SetStatus("走动捡金币；1/2/3 选槽位，4 保存，5 读取，6 删除。槽位 3 里预置了一份 v1 旧存档");
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        CheckPickups();
        UpdateAutoSave(dt);
        UpdatePanel();
        CheckGoals();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("aux_1")) { SelectSlot(1); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_2")) { SelectSlot(2); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_3")) { SelectSlot(3); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_4")) { DoSave(); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_5")) { DoLoad(); GetViewport().SetInputAsHandled(); return; }

        base._UnhandledInput(@event);
    }

    // ---------- 房间内容 ----------

    private void BuildCoins()
    {
        foreach (var spot in CoinSpots)
        {
            var coin = new Node2D { Name = "Coin", Position = spot };
            LevelKit.MakeCircle(coin, Vector2.Zero, 12f, new Color(1f, 0.84f, 0.34f), 16, "Body");
            LevelKit.MakeCircle(coin, Vector2.Zero, 5f, new Color(1f, 0.96f, 0.76f), 12, "Shine");
            _coinsRoot.AddChild(coin);
            _coins.Add(coin);
        }
    }

    private void CheckPickups()
    {
        for (var i = _coins.Count - 1; i >= 0; i--)
        {
            var coin = _coins[i];
            if (!coin.Visible) continue;
            if (coin.GlobalPosition.DistanceTo(_player.GlobalPosition) > PickupRadius) continue;

            coin.Visible = false;
            _coinCount++;
            _lastMessage = $"捡到金币（共 {_coinCount}）";
            AutoSave("捡到金币");
        }

        if (_coinCount > 0 && _coinCount % 3 == 0 && !_unlocked.Contains($"金币×{_coinCount}"))
            _unlocked.Add($"金币×{_coinCount}");
    }

    /// <summary>
    /// 自动存档：**时机比频率重要得多**。
    ///
    /// 本站选了两个时机：**进入关卡时**（保证"至少有一个存档点"）
    /// 和**获得重要物品时**（保证玩家不会因为崩溃丢掉成果）。
    ///
    /// 反面做法是"每 N 秒无脑存一次" —— 它会在玩家正要死的时候把濒死状态存下来。
    /// **自动存档要绑在"值得记住的时刻"上，不是绑在计时器上。**
    /// （本站保留了一个 12 秒的兜底计时器，只是为了让你在演示里不用等太久。）
    /// </summary>
    private void UpdateAutoSave(float dt)
    {
        _autoSaveTimer += dt;
        if (_autoSaveTimer < AutoSaveInterval) return;
        _autoSaveTimer = 0f;
        AutoSave("兜底计时器（每 12 秒）");
    }

    private void AutoSave(string reason)
    {
        _slots.Write(1, Capture());
        _savedSlots[1] = true;
        _saveCount++;
        _autoSaves++;
        _lastMessage = $"【自动存档】{reason}";
    }

    private SlotData Capture() => new()
    {
        PlayerX = _player.GlobalPosition.X,
        PlayerY = _player.GlobalPosition.Y,
        Coins = _coinCount,
        Unlocked = new List<string>(_unlocked),
        LastStation = StationId,
        MasterVolume = (float)AudioServer.GetBusVolumeLinear(AudioServer.GetBusIndex("Master")),   // 任务 ③：设置也进存档
    };

    // ---------- 存档操作 ----------

    private void SelectSlot(int slot)
    {
        _slots.Select(slot);
        _lastMessage = $"当前槽位 → {slot}";
        Flash($"当前槽位：{slot}");
    }

    private void DoSave()
    {
        var slot = _slots.CurrentSlot;
        _slots.Write(slot, Capture());
        _savedSlots[slot] = true;
        _saveCount++;
        _lastMessage = $"已保存到槽位 {slot}";
        Flash($"已保存到槽位 {slot}", 2.5);
    }

    private void DoLoad()
    {
        var slot = _slots.CurrentSlot;

        // ★ 先看文件里写的版本号（迁移之前）—— 否则"这是旧存档"的信息会被迁移抹掉
        var fileVersion = _slots.PeekVersion(slot);

        var data = _slots.Read(slot);

        if (data is null)
        {
            _lastMessage = $"槽位 {slot} 是空的（或文件损坏）";
            Flash($"槽位 {slot} 没有存档", 2.5);
            return;
        }

        _loadCount++;
        if (fileVersion < SlotData.CurrentVersion) _legacyLoaded++;

        // ★ 校验和检查（任务 ②）。注意顺序：**先验完整性，再使用数据**。
        //   本站的策略是"坏了也让你读，但明确告诉你坏了" ——
        //   换成"坏了就拒绝读取"同样合理，取决于你想让玩家损失多少。
        var verified = data.Verify();
        if (verified) _checksumOk++; else _checksumBad++;
        if (data.MasterVolume > 0f) _settingsCarried++;

        // 玩家位置被还原 —— 这就是"读档"最直观的表现
        _player.GlobalPosition = new Vector2(data.PlayerX, data.PlayerY);
        _player.Velocity = Vector2.Zero;

        _coinCount = data.Coins;
        _unlocked.Clear();
        _unlocked.AddRange(data.Unlocked);

        // 金币的显隐也跟着还原：本站的规则是"已收集数 = 前 N 个金币被吃掉"
        for (var i = 0; i < _coins.Count; i++)
            _coins[i].Visible = i >= data.Coins;

        if (!verified)
            _lastMessage = $"⚠ 已读取槽位 {slot}，但**校验和不匹配** —— 文件被改过或损坏（位置被改成了 {data.PlayerX:0}）";
        else if (data.Version < SlotData.CurrentVersion)
            _lastMessage = $"已读取槽位 {slot}（**从 v{data.Version} 迁移到 v{SlotData.CurrentVersion}**，缺失字段已补默认值）";
        else
            _lastMessage = $"已读取槽位 {slot}（v{data.Version}，校验和通过）";

        Flash($"读取槽位 {slot}", 2.5);

        // 演示循环：第一次校验通过之后，**自动**把它改坏一次，
        // 这样你再按一次 5 就能看到"校验和不匹配"的警告。
        if (verified && !_autoCorrupted) CorruptCurrentSlot();
    }

    private void DoDelete()
    {
        var slot = _slots.CurrentSlot;
        _slots.Delete(slot);
        _lastMessage = $"已删除槽位 {slot}";
        Flash($"已删除槽位 {slot}", 2.5);
    }

    /// <summary>
    /// 把当前槽位的文件**改坏一个数字但不更新校验和**。
    ///
    /// ★ 这里踩过一个坑：本站原本用 `aux_6` / `aux_7` 做"删除"和"改坏"，
    ///   但工程的 InputMap 只定义了 `aux_1`~`aux_5` —— 运行起来**每一次按键都报错**
    ///   （`The InputMap action "aux_6" doesn't exist`），headless 验证直接 FAIL。
    ///   教训：**加了新按键，先确认 InputMap 里真的有这个动作**
    ///   （本工程用 `tools/gen-inputmap.ps1` 统一生成，别手写 InputEvent）。
    ///   改成"读取成功后自动改坏一次"，就不用额外按键了。
    /// </summary>
    private void CorruptCurrentSlot()
    {
        var slot = _slots.CurrentSlot;
        _slots.Corrupt(slot);
        _autoCorrupted = true;
        _lastMessage = $"已把槽位 {slot} 的 PlayerX 改成 99999（校验和没跟着改）—— 再按一次 5 读取看看";
    }

    // ---------- 判定 ----------

    private void CheckGoals()
    {
        // 下标严格对应 StationCatalog 里 S11 的任务顺序：
        //   0 加字段+迁移旧存档 · 1 三个槽位+自动存档 · 2 写入前做校验
        //   3 把游戏设置也存进去 · 4 找到 user:// 真实路径看一眼
        if (_legacyLoaded > 0) MarkOnce(0);

        var used = 0;
        for (var i = 1; i <= SaveSlots.SlotCount; i++) if (_savedSlots[i]) used++;
        if (used >= 2 && _autoSaves > 0) MarkOnce(1);

        if (_checksumOk > 0 && _checksumBad > 0) MarkOnce(2);

        if (_settingsCarried > 0) MarkOnce(3);

        if (_pathSeen) MarkOnce(4);

        if (!Array.TrueForAll(_taskDone, done => done)) return;

        Complete("迁移、多槽位、自动存档、校验和、设置持久化 —— 存档真正难的地方你都碰过了");
    }

    private void MarkOnce(int index)
    {
        if (_taskDone[index]) return;
        _taskDone[index] = true;
        MarkTaskDone(index);
    }

    // ---------- 面板 ----------

    private void BuildLabels()
    {
        _panel = LevelKit.MakeLabel(this, new Vector2(24, 58), "", 14,
            new Color(0.87f, 0.92f, 0.98f), HorizontalAlignment.Left, "SavePanel");
        _panel.Size = new Vector2(768, 470);
    }

    private void UpdatePanel()
    {
        var sb = new StringBuilder();

        sb.Append($"【存档目录】{_slots.DirectoryPath}\n");
        sb.Append("　（`user://` 的真实路径 —— **导出后 `res://` 是只读的，玩家数据只能写这里**）\n\n");

        sb.Append("【三个槽位】\n");
        for (var slot = 1; slot <= SaveSlots.SlotCount; slot++)
        {
            var data = _slots.Read(slot);
            var mark = slot == _slots.CurrentSlot ? "▶" : "　";
            if (data is null)
            {
                sb.Append($"{mark} 槽位 {slot}：（空）\n");
                continue;
            }

            var fileVersion = _slots.PeekVersion(slot);
            var legacy = fileVersion < SlotData.CurrentVersion ? $"　← 文件里写的是 v{fileVersion} 旧版本！" : "";
            sb.Append($"{mark} 槽位 {slot}：v{data.Version}　金币 {data.Coins}　");
            sb.Append($"位置 ({data.PlayerX:0}, {data.PlayerY:0})　解锁 {data.Unlocked.Count} 项　{data.SavedAt}{legacy}\n");
        }

        sb.Append('\n');
        sb.Append($"【最近一次操作】{_lastMessage}\n");
        sb.Append($"【本局】自动/手动存档 {_saveCount} 次（其中自动 {_autoSaves}）　读取 {_loadCount} 次　");
        sb.Append($"金币 {_coinCount}　解锁 {_unlocked.Count} 项\n");
        sb.Append($"【校验和】通过 {_checksumOk} 次　失败 {_checksumBad} 次\n\n");

        var raw = _slots.ReadRaw(_slots.CurrentSlot);
        sb.Append($"【槽位 {_slots.CurrentSlot} 的原始 JSON】");
        if (raw.Length == 0) sb.Append("（空）\n");
        else
        {
            sb.Append("← **可以直接用记事本改它**\n");
            var lines = raw.Split('\n');
            for (var i = 0; i < Mathf.Min(lines.Length, 9); i++)
                sb.Append($"　{lines[i].TrimEnd()}\n");
        }

        sb.Append("\n【按键】1/2/3 选槽位　4 保存　5 读取");
        sb.Append("\n　→ 第一次读取校验通过后，程序会**自动把它改坏一次**，再按 5 就能看到校验失败");

        _pathSeen = true;   // 面板第一帧就把 user:// 的真实路径显示出来了
        _panel.Text = sb.ToString();
    }
}
