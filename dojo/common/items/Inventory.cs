using Godot;

namespace Dojo.Common;

/// <summary>
/// 背包的**数据模型**。
///
/// ★ 本站唯一想让你记住的一句话：
///   **背包的难点从来不是 UI，而是「数据模型」和「它怎么显示」必须分开。**
///
///   这个类里**一行 UI 代码都没有** —— 它不知道有没有格子、格子长什么样、
///   是用图标还是用文字。它只回答四个问题：
///     ① 第 i 格是什么？ ② 能不能放进去？ ③ 交换两格 ④ 用掉一个
///
/// ★ 为什么不该把「图标节点」本身当作数据（任务 ⑤）：
///   如果你把 `TextureRect` 存进背包格子，那么：
///     · **存档没法存** —— 节点不能被 JSON 序列化
///     · **排序/搜索没法做** —— 你得去问每个节点"你是什么物品"
///     · **换一种显示方式就要重写全部逻辑** —— 想加个"列表视图"就得改背包
///     · **数据会被节点的生命周期绑住** —— 界面一刷新，数据可能就没了
///   而分开之后，同一份数据可以**同时**被两个视图渲染（本站的格子视图和快捷栏），
///   面板上那两处显示的就是**同一份数据**。
///
/// ★ 视图怎么知道要刷新？用事件，不是轮询：
///   `Changed` 一触发，所有视图自己重画。**数据变了通知视图，而不是视图去问数据。**
/// </summary>
public sealed class Inventory
{
    public const int SlotCount = 12;

    /// <summary>数据变了。**所有视图都订阅它，而不是每帧去读。**</summary>
    public event Action? Changed;

    private readonly ItemStack?[] _slots = new ItemStack?[SlotCount];

    public ItemStack? Get(int index)
        => index < 0 || index >= SlotCount ? null : _slots[index];

    /// <summary>已占用的格子数 —— 视图和统计都用它。</summary>
    public int UsedSlots
    {
        get
        {
            var used = 0;
            foreach (var slot in _slots) if (slot is { IsEmpty: false }) used++;
            return used;
        }
    }

    /// <summary>
    /// 加物品。**堆叠逻辑就在这里**（任务 ①）：
    ///   ① 先往已有的、没满的同类堆里塞
    ///   ② 塞不下的部分再开新格子
    ///   ③ 格子用完了，剩下的**加不进去**（返回没加进去的数量）
    ///
    /// 返回"没能放进去的数量" —— 让调用方决定怎么办（丢地上？提示背包满？）。
    /// **不要把"背包满了"这个决定写进数据模型里**，那是玩法层的选择。
    /// </summary>
    public int TryAdd(string itemId, int count)
    {
        var def = ItemDatabase.Get(itemId);
        var remaining = count;

        // ① 先填已有的堆
        for (var i = 0; i < SlotCount && remaining > 0; i++)
        {
            var slot = _slots[i];
            if (slot is null || slot.IsEmpty || slot.ItemId != itemId) continue;

            var space = def.MaxStack - slot.Count;
            if (space <= 0) continue;

            var moved = Mathf.Min(space, remaining);
            slot.Count += moved;
            remaining -= moved;
        }

        // ② 再开新格子
        for (var i = 0; i < SlotCount && remaining > 0; i++)
        {
            if (_slots[i] is { IsEmpty: false }) continue;

            var moved = Mathf.Min(def.MaxStack, remaining);
            _slots[i] = new ItemStack { ItemId = itemId, Count = moved };
            remaining -= moved;
        }

        if (remaining != count) Changed?.Invoke();
        return remaining;
    }

    /// <summary>拿走第 i 格（返回原来的内容，格子清空）。</summary>
    public ItemStack? Take(int index)
    {
        if (index < 0 || index >= SlotCount) return null;

        var stack = _slots[index];
        _slots[index] = null;
        if (stack is { IsEmpty: false }) Changed?.Invoke();
        return stack;
    }

    public void Put(int index, ItemStack? stack)
    {
        if (index < 0 || index >= SlotCount) return;
        _slots[index] = stack;
        Changed?.Invoke();
    }

    /// <summary>交换两格（任务 ② 的数据层实现）。**拖拽只是它的一个调用者。**</summary>
    public void Swap(int a, int b)
    {
        if (a == b) return;
        if (a < 0 || b < 0 || a >= SlotCount || b >= SlotCount) return;

        (_slots[a], _slots[b]) = (_slots[b], _slots[a]);
        Changed?.Invoke();
    }

    /// <summary>用掉第 i 格的一个（任务 ④）。返回被用掉的物品定义，没用到就返回 null。</summary>
    public ItemDef? ConsumeOne(int index)
    {
        var stack = Get(index);
        if (stack is null || stack.IsEmpty) return null;

        var def = stack.Def;
        stack.Count--;
        if (stack.Count <= 0) _slots[index] = null;

        Changed?.Invoke();
        return def;
    }

    public void Clear()
    {
        for (var i = 0; i < SlotCount; i++) _slots[i] = null;
        Changed?.Invoke();
    }

    /// <summary>给存档用的一份纯数据快照（S11 教的：存数据，不存对象）。</summary>
    public List<string> Snapshot()
    {
        var list = new List<string>();
        for (var i = 0; i < SlotCount; i++)
        {
            var slot = _slots[i];
            list.Add(slot is null || slot.IsEmpty ? "" : $"{slot.ItemId}:{slot.Count}");
        }
        return list;
    }
}
