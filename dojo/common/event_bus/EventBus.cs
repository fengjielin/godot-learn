using Godot;

namespace Dojo.Common;

/// <summary>
/// 全局事件总线（Autoload 单例）。
///
/// 为什么需要它：
///   当「玩家被打了」这件事发生时，血条、音效、成就、屏幕震动都想做出反应。
///   如果让玩家脚本逐个去引用它们，玩家就会知道全世界 —— 这叫强耦合，改一处炸一片。
///   事件总线把「谁发生了什么」（发送方）和「谁关心这件事」（接收方）彻底拆开。
///
/// 学习要点（对应练习站 s13_events）：
///   1. Autoload 是在游戏启动时创建、跨场景常驻的节点，见 project.godot 的 [autoload] 段。
///   2. C# 里用 [Signal] + delegate 声明信号；Godot 会生成 SignalName 常量供 EmitSignal 使用。
///   3. 连接信号一定要记得在对象销毁时断开，否则会留下「悬空连接」把已释放的对象钉在内存里。
///
/// 反面提醒：
///   事件总线很好用，但用过头会让「谁触发了这个事件」变得难以追踪。
///   经验法则：跨系统（玩家 ↔ UI ↔ 成就）用事件，同一系统内部直接调用更清晰。
/// </summary>
public partial class EventBus : Node
{
    /// <summary>便捷访问。注意：这只在 Autoload 就绪后有效。</summary>
    public static EventBus Instance { get; private set; } = null!;

    [Signal] public delegate void StationEnteredEventHandler(string stationId);
    [Signal] public delegate void StationCompletedEventHandler(string stationId);
    [Signal] public delegate void NoticeEventHandler(string text);
    [Signal] public delegate void PlayerDamagedEventHandler(int amount, int remainingHp);
    [Signal] public delegate void PlayerDiedEventHandler();
    [Signal] public delegate void ItemPickedUpEventHandler(string itemId, int count);

    /// <summary>有敌人被击杀。参数：敌人类型、世界坐标。</summary>
    [Signal] public delegate void EnemyKilledEventHandler(string enemyType, Vector2 position);

    /// <summary>解锁了一个成就。参数：成就标题。</summary>
    [Signal] public delegate void AchievementUnlockedEventHandler(string title);

    /// <summary>
    /// ★ 「反面教材」通道：同样的事情，用**普通 C# event** 实现一遍。
    ///
    /// 它和上面的 Godot 信号有一个致命区别：
    ///   · **Godot 信号**在任一端被释放（QueueFree）时，引擎会**自动清理连接**。
    ///   · **C# event** 不会。被释放的订阅者仍然躺在调用列表里，
    ///     下一次 Invoke 就会抛 `ObjectDisposedException`（访问已释放对象）。
    ///
    /// S13 练习站会让你**亲手制造一次**这个错误并捕获出来看。
    /// 记住结论：**用 C# event 做跨对象通信时，退订是你的责任，引擎不会帮你。**
    /// </summary>
    public event Action<string>? NoticeCSharp;

    public void EmitNoticeCSharp(string text) => NoticeCSharp?.Invoke(text);

    /// <summary>
    /// 清空所有 C# event 订阅者。
    ///
    /// 注意这个方法**必须由 EventBus 自己提供**：C# 的 `event` 关键字
    /// 只允许在声明它的类型内部赋值（外部只能 `+=` / `-=`）。
    /// 这是语言层面的保护 —— **事件的拥有者必须自己负责"清理"这件事**，
    /// 因为它没法指望别人记得退订。
    /// </summary>
    public void ClearCSharpSubscribers() => NoticeCSharp = null;

    public override void _EnterTree()
    {
        Instance = this;
        // Autoload 的 _EnterTree 早于主场景，这是「在任何场景里都能用」的原因。
        GD.Print("[EventBus] ready");
    }

    public void EmitNotice(string text) => EmitSignal(SignalName.Notice, text);

    public void EmitStationEntered(string stationId)
        => EmitSignal(SignalName.StationEntered, stationId);

    public void EmitStationCompleted(string stationId)
        => EmitSignal(SignalName.StationCompleted, stationId);
}
