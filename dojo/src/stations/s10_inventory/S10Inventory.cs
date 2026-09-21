using System.Text;
using Godot;
using Dojo.Common;
using Dojo.Entities;

namespace Dojo.Stations;

/// <summary>
/// S10 · 背包与物品
///
/// **鼠标左键点格子 = 拿起 / 放下**（放下就是交换）；**右键点格子 = 使用 / 装备**。
/// 键盘 `1`~`5` 在脚下生成五种物品。
///
/// ★ 本站的核心是**「数据模型」和「视图」的关系**：
///   `common/items/Inventory.cs` 里一行 UI 代码都没有。
///   而屏幕上**同时有两处**在显示它：
///     · 中间那 12 个格子（主视图）
///     · 底部那条快捷栏（第二个视图）
///   **它们显示的是同一份数据，改一处两处都变。**
///   这就是"数据和视图分开"最直观的证据 —— 如果数据是节点，
///   你不可能用两个视图显示它而不用手动同步。
///
/// ★ 面板上还有一块「原始数据模型」，直接把 `Inventory.Snapshot()` 印出来。
///   **当你不确定一个 bug 出在数据还是 UI 上时，先看这一块** ——
///   数据对而画面错 = 视图的问题；数据就错了 = 逻辑的问题。
/// </summary>
public partial class S10Inventory : StationBase
{
    public override string StationId => "s10_inventory";

    private static readonly Rect2 PlayArea = new(40, 130, 1200, 560);

    private const int Columns = 6;
    private const float SlotSize = 56f;
    private static readonly Vector2 GridOrigin = new(24, 462);
    private static readonly Vector2 HotbarOrigin = new(24, 596);
    private static readonly Vector2 EquipOrigin = new(420, 462);

    private Player _player = null!;
    private CanvasLayer _ui = null!;
    private Label _panel = null!;
    private Label _hpLabel = null!;

    private readonly Inventory _inventory = new();
    private readonly string?[] _equipment = new string?[2];   // 0=靴子槽 1=护甲槽
    private readonly Dictionary<int, ColorRect> _slotFills = new();
    private readonly Dictionary<int, Label> _slotLabels = new();
    private readonly List<ColorRect> _hotbarFills = new();
    private readonly List<Label> _hotbarLabels = new();

    private int _heldSlot = -1;          // 「手上拿着」的格子下标，-1 = 空手
    private int _hp = 100;
    private int _maxHp = 100;
    private int _potionUses;
    private int _swaps;
    private bool _sawStacking;
    private bool _equipped;
    private readonly HashSet<string> _seenItems = new();
    private readonly bool[] _taskDone = new bool[5];
    private string _lastAction = "（还没操作过）";

    protected override void StationReady()
    {
        _player = GetNode<Player>("Player");
        _ui = GetNode<CanvasLayer>("Ui");

        LevelKit.CreateBorderWalls(this, PlayArea);
        BuildPanel();
        BuildGrid();
        BuildHotbar();
        BuildEquipment();
        BuildStatusBar();

        _inventory.Changed += RefreshViews;
        _inventory.TryAdd(ItemDatabase.Potion, 1);
        _inventory.TryAdd(ItemDatabase.Herb, 3);
        RefreshViews();

        SetStatus("左键点格子拿起/放下；右键点格子使用或装备；1~5 在脚下生成物品走过去捡");
    }

    public override void _Process(double delta)
    {
        _ = delta;
        PickupNearby();
        ApplyEquipmentSpeed();
        UpdatePanel();
        CheckGoals();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("aux_1")) { Spawn(ItemDatabase.Herb, 3); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_2")) { Spawn(ItemDatabase.Ore, 4); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_3")) { Spawn(ItemDatabase.Potion, 2); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_4")) { Spawn(ItemDatabase.BigPotion, 1); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_5")) { Spawn(ItemDatabase.Boots, 1); GetViewport().SetInputAsHandled(); return; }

        base._UnhandledInput(@event);
    }

    // ---------- 地上的物品：**它们只是"待捡的数据"，不是 UI ----------

    private void Spawn(string itemId, int count)
    {
        var def = ItemDatabase.Get(itemId);
        // 偏移必须**小于拾取半径（42）**，否则物品掉在半径外，玩家站着不动就永远捡不到。
        // （第一次写成 +70，实测就是捡不起来 —— 生成点和判定半径是两个各自独立的常量，
        //   改一个的时候一定要回头看另一个。）
        var pickup = new Node2D { Name = "Drop", Position = _player.Position + new Vector2(0, 26) };
        pickup.SetMeta("item_id", itemId);
        pickup.SetMeta("count", count);

        LevelKit.MakeCircle(pickup, Vector2.Zero, 14f, def.Color, 18, "Body");
        LevelKit.MakeLabel(pickup, new Vector2(0, -36), $"{def.DisplayName}×{count}", 12, def.Color);
        AddChild(pickup);   // 地上的掉落物是**世界里的**对象，不该放进 UI 层
        pickup.AddToGroup("drops");
    }

    private void PickupNearby()
    {
        foreach (var node in GetTree().GetNodesInGroup("drops"))
        {
            if (node is not Node2D drop) continue;
            if (drop.GlobalPosition.DistanceTo(_player.GlobalPosition) > 42f) continue;

            var itemId = drop.GetMeta("item_id").AsString();
            var count = drop.GetMeta("count").AsInt32();
            var leftover = _inventory.TryAdd(itemId, count);

            _lastAction = leftover > 0
                ? $"捡 {itemId}×{count}，但**背包放不下 {leftover} 个**（TryAdd 返回剩余量，由玩法层决定怎么办）"
                : $"捡起 {itemId}×{count}";

            drop.QueueFree();
        }
    }

    private void ApplyEquipmentSpeed()
    {
        var bonus = 0f;
        foreach (var id in _equipment)
            if (!string.IsNullOrEmpty(id)) bonus += ItemDatabase.Get(id).MoveSpeedBonus;

        _player.MaxSpeed = 235f * (1f + bonus);
    }

    // ---------- 视图：从数据模型渲染出来 ----------

    private void BuildPanel()
    {
        _panel = LevelKit.MakeLabel(this, new Vector2(24, 56), "", 13,
            new Color(0.87f, 0.92f, 0.98f), HorizontalAlignment.Left, "InvPanel");
        _panel.Size = new Vector2(762, 400);
    }

    private void BuildGrid()
    {
        for (var i = 0; i < Inventory.SlotCount; i++)
        {
            var col = i % Columns;
            var row = i / Columns;
            var position = GridOrigin + new Vector2(col * (SlotSize + 6f), row * (SlotSize + 6f));
            BuildSlot(i, position, SlotSize, _ui);
        }
    }

    private void BuildSlot(int index, Vector2 position, float size, CanvasLayer layer)
    {
        var frame = new ColorRect
        {
            Name = $"Slot{index}",
            Position = position,
            Size = new Vector2(size, size),
            Color = new Color(0.16f, 0.18f, 0.23f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        layer.AddChild(frame);

        var fill = new ColorRect
        {
            Position = position + new Vector2(5, 5),
            Size = new Vector2(size - 10, size - 10),
            Color = new Color(0, 0, 0, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        layer.AddChild(fill);

        var label = LevelKit.MakeLabel(layer, position + new Vector2(0, size - 22f), "", 13,
            new Color(1f, 1f, 1f), HorizontalAlignment.Center, $"Count{index}");
        label.Size = new Vector2(size, 20);

        _slotFills[index] = fill;
        _slotLabels[index] = label;

        frame.GuiInput += @event => OnSlotInput(index, @event);
    }

    /// <summary>快捷栏 —— **同一份数据的第二个视图**，只显示前 12 格里靠前的几个。</summary>
    private void BuildHotbar()
    {
        for (var i = 0; i < Inventory.SlotCount; i++)
        {
            var position = HotbarOrigin + new Vector2(i * 30f, 0);

            var frame = new ColorRect
            {
                Position = position,
                Size = new Vector2(28, 28),
                Color = new Color(0.14f, 0.16f, 0.2f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _ui.AddChild(frame);

            var fill = new ColorRect
            {
                Position = position + new Vector2(3, 3),
                Size = new Vector2(22, 22),
                Color = new Color(0, 0, 0, 0),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _ui.AddChild(fill);

            var label = LevelKit.MakeLabel(_ui, position + new Vector2(-4, 30), "", 11,
                new Color(0.85f, 0.9f, 1f), HorizontalAlignment.Center, $"Hot{i}");
            label.Size = new Vector2(36, 16);

            _hotbarFills.Add(fill);
            _hotbarLabels.Add(label);
        }
    }

    private void BuildEquipment()
    {
        for (var i = 0; i < 2; i++)
        {
            var position = EquipOrigin + new Vector2(i * 76f, 0);
            var frame = new ColorRect
            {
                Position = position,
                Size = new Vector2(SlotSize, SlotSize),
                Color = new Color(0.2f, 0.16f, 0.14f),
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            _ui.AddChild(frame);

            var label = LevelKit.MakeLabel(_ui, position + new Vector2(0, SlotSize + 4f),
                i == 0 ? "靴子槽" : "护甲槽", 12, new Color(0.85f, 0.72f, 0.62f));
            label.Size = new Vector2(SlotSize, 18);

            var slotIndex = 100 + i;   // 100+ 表示装备槽，和背包格子区分开
            var fill = new ColorRect
            {
                Position = position + new Vector2(5, 5),
                Size = new Vector2(SlotSize - 10, SlotSize - 10),
                Color = new Color(0, 0, 0, 0),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _ui.AddChild(fill);

            _slotFills[slotIndex] = fill;
            frame.GuiInput += @event => OnSlotInput(slotIndex, @event);
        }

        // 手上的物品提示
        var held = LevelKit.MakeLabel(_ui, new Vector2(24, 640), "", 13,
            new Color(1f, 0.9f, 0.5f), HorizontalAlignment.Left, "Held");
        held.Size = new Vector2(500, 22);
        _slotLabels[-1] = held;
    }

    private void BuildStatusBar()
    {
        _hpLabel = LevelKit.MakeLabel(_ui, new Vector2(560, 640), "", 13,
            new Color(0.95f, 0.6f, 0.6f), HorizontalAlignment.Left, "Hp");
        _hpLabel.Size = new Vector2(380, 22);
    }

    /// <summary>
    /// **视图重画的唯一入口。** 数据一变（`Changed`）就整体重画 ——
    /// 不去做"只更新变了的那一格"这种优化，因为**在能跑之前不需要优化**，
    /// 而且差异更新是 bug 的温床（漏更新某一格 = 画面和数据不一致）。
    /// </summary>
    private void RefreshViews()
    {
        for (var i = 0; i < Inventory.SlotCount; i++)
        {
            var stack = _inventory.Get(i);
            var itemColor = stack is { IsEmpty: false } ? stack.Def.Color : new Color(0, 0, 0, 0);

            if (_slotFills.TryGetValue(i, out var fill)) fill.Color = itemColor;
            if (_slotLabels.TryGetValue(i, out var label))
                label.Text = stack is { IsEmpty: false } ? $"×{stack.Count}" : "";

            // 快捷栏是**另一个视图**，读的是同一份数据
            _hotbarFills[i].Color = itemColor;
            _hotbarLabels[i].Text = stack is { IsEmpty: false } ? stack.Count.ToString() : "";
        }

        for (var i = 0; i < 2; i++)
        {
            var id = _equipment[i];
            if (!_slotFills.TryGetValue(100 + i, out var fill)) continue;
            fill.Color = string.IsNullOrEmpty(id) ? new Color(0, 0, 0, 0) : ItemDatabase.Get(id).Color;
        }

        _slotLabels[-1].Text = _heldSlot < 0
            ? "手上：空"
            : $"手上：{_inventory.Get(_heldSlot)?.Def.DisplayName ?? "?"}（再点一个格子放下）";

        _hpLabel.Text = $"生命 {_hp}/{_maxHp}　用过药水 {_potionUses} 次　移动速度 ×{_player.MaxSpeed / 235f:0.00}";
    }

    // ---------- 交互 ----------

    private void OnSlotInput(int index, InputEvent @event)
    {
        if (@event is not InputEventMouseButton mouse || !mouse.Pressed) return;

        if (mouse.ButtonIndex == MouseButton.Right) { UseSlot(index); return; }
        if (mouse.ButtonIndex != MouseButton.Left) return;

        if (index >= 100) { HandleEquipmentClick(index - 100); return; }

        // 拿起 / 放下（放下就是交换）
        if (_heldSlot < 0)
        {
            if (_inventory.Get(index) is not { IsEmpty: false }) return;
            _heldSlot = index;
            _lastAction = $"拿起第 {index + 1} 格的 {_inventory.Get(index)!.Def.DisplayName}";
        }
        else
        {
            _inventory.Swap(_heldSlot, index);
            _swaps++;
            _lastAction = $"把第 {_heldSlot + 1} 格和第 {index + 1} 格交换了（拖拽只是 Swap 的一个调用者）";
            _heldSlot = -1;
        }
        RefreshViews();
    }

    private void HandleEquipmentClick(int equipmentIndex)
    {
        if (_heldSlot < 0 || _inventory.Get(_heldSlot) is not { IsEmpty: false } stack) return;
        if (stack.Def.Kind != ItemKind.Equipment) { _lastAction = $"{stack.Def.DisplayName} 不是装备，放不进装备槽"; return; }

        // 装备：把物品从背包移进装备槽，原来的装备退回背包
        var previous = _equipment[equipmentIndex];
        _inventory.Take(_heldSlot);
        if (!string.IsNullOrEmpty(previous)) _inventory.TryAdd(previous, 1);

        _equipment[equipmentIndex] = stack.ItemId;
        _equipped = true;
        _heldSlot = -1;
        _lastAction = $"装备了 {stack.Def.DisplayName}（移动速度 {(stack.Def.MoveSpeedBonus >= 0 ? "+" : "")}{stack.Def.MoveSpeedBonus * 100f:0}%）";
        RefreshViews();
    }

    private void UseSlot(int index)
    {
        if (index >= 100) return;

        var stack = _inventory.Get(index);
        if (stack is null || stack.IsEmpty) return;

        var def = stack.Def;
        switch (def.Kind)
        {
            case ItemKind.Potion:
            {
                var heal = def.Id == ItemDatabase.BigPotion ? 60 : 25;
                _hp = Mathf.Min(_maxHp, _hp + heal);
                _inventory.ConsumeOne(index);
                _potionUses++;
                _lastAction = $"喝下 {def.DisplayName}，回复 {heal} 点生命";
                break;
            }
            case ItemKind.Equipment:
                _lastAction = $"{def.DisplayName} 是装备 —— 先左键拿起，再左键点装备槽";
                break;
            default:
                _lastAction = $"{def.DisplayName} 是材料，不能使用";
                break;
        }
        RefreshViews();
    }

    // ---------- 判定 ----------

    private void CheckGoals()
    {
        // 下标严格对应 StationCatalog 里 S10 的任务顺序：
        //   0 堆叠 · 1 拖拽交换 · 2 装备槽 · 3 右键使用 · 4 为什么数据不该是图标节点
        for (var i = 0; i < Inventory.SlotCount; i++)
        {
            var stack = _inventory.Get(i);
            if (stack is not { IsEmpty: false }) continue;
            _seenItems.Add(stack.ItemId);
            if (stack.Count is > 1 and <= 5) _sawStacking = true;
        }

        if (_sawStacking) MarkOnce(0);
        if (_swaps > 0) MarkOnce(1);
        if (_equipped) MarkOnce(2);
        if (_potionUses > 0) MarkOnce(3);
        if (_seenItems.Count >= 3) MarkOnce(4);

        if (!Array.TrueForAll(_taskDone, done => done)) return;

        Complete("堆叠、交换、装备、使用 —— 而且你看见了同一份数据被两个视图同时渲染");
    }

    private void MarkOnce(int index)
    {
        if (_taskDone[index]) return;
        _taskDone[index] = true;
        MarkTaskDone(index);
    }

    // ---------- 面板 ----------

    private void UpdatePanel()
    {
        var sb = new StringBuilder();

        sb.Append("【数据模型】`common/items/Inventory.cs` —— **这个类里一行 UI 代码都没有**\n　");
        var snapshot = _inventory.Snapshot();
        for (var i = 0; i < snapshot.Count; i++)
        {
            sb.Append(string.IsNullOrEmpty(snapshot[i]) ? "·" : $"[{snapshot[i]}]");
            if (i % 6 == 5) sb.Append("\n　");
        }
        sb.Append('\n');
        sb.Append($"　已用 {_inventory.UsedSlots}/{Inventory.SlotCount} 格　物品种类 {_seenItems.Count}\n\n");

        sb.Append("【视图 A】中间 12 个格子　【视图 B】底部快捷栏　【视图 C】本节这段文字\n");
        sb.Append("　→ **三个视图读的是同一份数据。** 数据一变，三个一起变 ——\n");
        sb.Append("　　 因为 `Inventory.Changed` 事件通知了它们，而不是它们每帧去问数据。\n\n");

        sb.Append($"【最近一次操作】{_lastAction}\n\n");

        sb.Append("【堆叠规则】草药/矿石 最多 5 个，药水 3 个，装备 1 个 —— 超出就开新格子\n");
        sb.Append("　→ 先填已有的没满的堆，再开新格子；格子用完时 `TryAdd` **返回没放下的数量**，\n");
        sb.Append("　　 由玩法层决定怎么办。**数据模型不做玩法决定。**\n\n");

        sb.Append("【按键】1 草药×3　2 矿石×4　3 小药水×2　4 大药水×1　5 疾行靴×1（生成在脚下，走过去捡）");

        _panel.Text = sb.ToString();
    }
}
