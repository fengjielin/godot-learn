namespace Dojo.Common;

/// <summary>
/// 物理层（collision layer / mask）的集中定义。
///
/// 为什么需要它：
///   Godot 里 collision_layer 和 collision_mask 都是「位掩码」：
///   第 1 层的值是 1，第 2 层是 2，第 3 层是 4 …… 第 N 层是 1 &lt;&lt; (N-1)。
///   在场景里直接写 2、32、128 这种魔数，三个月后没人看得懂，也没人敢改。
///   这里给每一层起个名字，场景文件里写数字、代码里写常量，两边一一对应。
///
/// 约定（务必与各 .tscn 里的 collision_layer / collision_mask 数字保持一致）：
///
///   层号  值    名称          谁在这一层
///   ----  ----  ------------  --------------------------------------------
///   1     1     World         墙、地形、障碍（StaticBody2D）
///   2     2     Player        玩家角色本体（CharacterBody2D）
///   3     4     Enemy         敌人本体（CharacterBody2D）
///   4     8     PlayerAttack  玩家攻击判定（Area2D，hitbox）
///   5     16    EnemyHurtbox  敌人受击判定（Area2D，hurtbox）
///   6     32    Interactable  可交互物：传送门、按钮、宝箱（Area2D）
///   7     64    Pickup        掉落物（Area2D）
///   8     128   Projectile    子弹、投射物（Area2D）
///
/// 理解要点：「层」是"我是什么"，「掩码」是"我要检测什么"。
///   一个 Area2D 想发现玩家，就必须让自己的 mask 包含 Player 层，
///   而玩家本体必须把自己的 layer 设为 Player。
/// </summary>
public static class GameLayers
{
    public const uint World = 1 << 0;         // 1
    public const uint Player = 1 << 1;        // 2
    public const uint Enemy = 1 << 2;         // 4
    public const uint PlayerAttack = 1 << 3;  // 8
    public const uint EnemyHurtbox = 1 << 4;  // 16
    public const uint Interactable = 1 << 5;  // 32
    public const uint Pickup = 1 << 6;        // 64
    public const uint Projectile = 1 << 7;    // 128

    /// <summary>把位掩码翻译成人类可读的名字，调试用。</summary>
    public static string Describe(uint mask)
    {
        var names = new List<string>();
        if ((mask & World) != 0) names.Add(nameof(World));
        if ((mask & Player) != 0) names.Add(nameof(Player));
        if ((mask & Enemy) != 0) names.Add(nameof(Enemy));
        if ((mask & PlayerAttack) != 0) names.Add(nameof(PlayerAttack));
        if ((mask & EnemyHurtbox) != 0) names.Add(nameof(EnemyHurtbox));
        if ((mask & Interactable) != 0) names.Add(nameof(Interactable));
        if ((mask & Pickup) != 0) names.Add(nameof(Pickup));
        if ((mask & Projectile) != 0) names.Add(nameof(Projectile));
        return names.Count == 0 ? "(none)" : string.Join("|", names);
    }
}
