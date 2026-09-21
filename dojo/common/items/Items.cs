using Godot;

namespace Dojo.Common;

public enum ItemKind
{
    Material,
    Potion,
    Equipment,
}

/// <summary>一件物品的**定义**（不变的数据）。</summary>
public sealed record ItemDef(
    string Id,
    string DisplayName,
    ItemKind Kind,
    int MaxStack,
    Color Color,
    string Description,
    float MoveSpeedBonus = 0f);

/// <summary>
/// 一格里的东西：**物品 id + 数量**。
/// 注意它**只是一个数据对象，没有任何节点、没有任何绘制代码** —— 这是本站的重点。
/// </summary>
public sealed class ItemStack
{
    public string ItemId { get; set; } = "";
    public int Count { get; set; }

    public ItemDef Def => ItemDatabase.Get(ItemId);
    public bool IsEmpty => Count <= 0 || string.IsNullOrEmpty(ItemId);

    public ItemStack Clone() => new() { ItemId = ItemId, Count = Count };
}

public static class ItemDatabase
{
    public const string Herb = "herb";
    public const string Ore = "ore";
    public const string Potion = "potion";
    public const string BigPotion = "big_potion";
    public const string Boots = "boots";
    public const string Armor = "armor";

    public static readonly ItemDef[] All =
    {
        new(Herb, "草药", ItemKind.Material, 5, new Color(0.55f, 0.88f, 0.5f), "没什么用，但能叠 5 个。"),
        new(Ore, "矿石", ItemKind.Material, 5, new Color(0.72f, 0.76f, 0.84f), "很重。"),
        new(Potion, "小药水", ItemKind.Potion, 3, new Color(0.95f, 0.42f, 0.5f), "右键使用：回复 25 点生命。"),
        new(BigPotion, "大药水", ItemKind.Potion, 3, new Color(0.98f, 0.62f, 0.32f), "右键使用：回复 60 点生命。"),
        new(Boots, "疾行靴", ItemKind.Equipment, 1, new Color(0.5f, 0.85f, 1f), "装备后移动速度 +45%。", 0.45f),
        new(Armor, "重甲", ItemKind.Equipment, 1, new Color(0.66f, 0.68f, 0.78f), "装备后移动速度 −20%。", -0.2f),
    };

    public static ItemDef Get(string id)
    {
        foreach (var def in All)
            if (def.Id == id) return def;
        throw new ArgumentException($"没有名为 '{id}' 的物品");
    }
}
