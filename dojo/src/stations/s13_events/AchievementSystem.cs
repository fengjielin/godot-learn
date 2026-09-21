using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// 成就系统 —— 一个**纯粹的事件消费者**。
///
/// 它不认识拾取物、不认识敌人、不认识任何东西。
/// 它只是坐在那里听事件，然后判断"该不该解锁一个成就"。
///
/// ★ 这就是事件总线最大的价值：
///   **新增一个成就，不需要改动任何已有的代码。**
///   不是"改得少"，是**一行都不用改** —— 加一个订阅就行。
///   对比一下：如果拾取物是直接调用成就系统的，那么"加一个成就"
///   就要去改拾取物里那段 if。加十个成就，那段 if 就有十个分支。
///
/// ★ 而它的代价也在这一页代码里：
///   **你没法在代码里"看到"是谁触发了它。** 打开 `Pickup.cs`，
///   你看不到任何一行提到成就系统。想看全貌，只能全局搜索
///   `ItemPickedUp` 这个名字。**事件总线用"可追踪性"换了"可扩展性"。**
///   所以：**核心流程用直接调用，横切关注点（成就/统计/音效/提示）才用事件。**
///
/// ★ 退订纪律：`_Ready` 里连上，`_ExitTree` 里断开。
///   少一个 `-=`，被释放的节点就会留在调用列表里 —— 这正是 S13 要演示的那个坑。
/// </summary>
public partial class AchievementSystem : Node
{
    public sealed record UnlockedAchievement(string Title, string Detail);

    /// <summary>解锁成就时触发（站台用它弹提示）。</summary>
    public event Action<UnlockedAchievement>? Unlocked;

    public int Coins { get; private set; }
    public int Kills { get; private set; }
    public int Distance { get; private set; }
    public int TookDamage { get; private set; }

    public List<UnlockedAchievement> History { get; } = new();

    private bool _firstBlood;
    private bool _collector;
    private bool _killer;
    private bool _traveller;

    public override void _Ready()
    {
        var bus = EventBus.Instance;
        bus.ItemPickedUp += OnItemPickedUp;
        bus.EnemyKilled += OnEnemyKilled;
        bus.PlayerDamaged += OnPlayerDamaged;
    }

    public override void _ExitTree()
    {
        // ★ 这一段是"必须写"的，不是"最好写"。
        var bus = EventBus.Instance;
        if (!IsInstanceValid(bus)) return;

        bus.ItemPickedUp -= OnItemPickedUp;
        bus.EnemyKilled -= OnEnemyKilled;
        bus.PlayerDamaged -= OnPlayerDamaged;
    }

    /// <summary>
    /// 「直接调用」模式下由 `Pickup` 调用。
    /// 注意它是**公开方法**，而且是在 `IItemReceiver` 接口里约定的 ——
    /// 这就是耦合的形状：**为了让别人能直接叫我，我必须暴露一个公开入口。**
    /// </summary>
    public void HandleItemDirectly(string itemId, int amount)
    {
        Coins += amount;
        _ = itemId;
        CheckAchievements();
    }

    public void ReportDistance(float meters)
    {
        Distance = Mathf.RoundToInt(meters);
        CheckAchievements();
    }

    private void OnItemPickedUp(string itemId, int count)
    {
        Coins += count;
        _ = itemId;
        CheckAchievements();
    }

    private void OnEnemyKilled(string enemyType, Vector2 position)
    {
        Kills++;
        _ = enemyType; _ = position;
        CheckAchievements();
    }

    private void OnPlayerDamaged(int amount, int remainingHp)
    {
        TookDamage += amount;
        _ = remainingHp;
    }

    private void CheckAchievements()
    {
        if (!_firstBlood && Kills >= 1) { _firstBlood = true; Unlock("初次见血", "击杀第一个目标"); }
        if (!_killer && Kills >= 3) { _killer = true; Unlock("清场", "累计击杀 3 个目标"); }
        if (!_collector && Coins >= 5) { _collector = true; Unlock("收集者", "累计拾取 5 个物品"); }
        if (!_traveller && Distance >= 900) { _traveller = true; Unlock("跑者", "移动超过 900 像素"); }
    }

    private void Unlock(string title, string detail)
    {
        var achievement = new UnlockedAchievement(title, detail);
        History.Add(achievement);
        Unlocked?.Invoke(achievement);
    }
}
