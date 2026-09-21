using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// S13 的可拾取物。它**自己不做任何事** —— 只负责"喊一声"。
///
/// 这是解耦的关键：拾取物不知道有成就系统、不知道有统计面板、不知道有飘字。
/// 它只知道"我被捡了"，然后把这件事发出去。
/// **发送方知道得越少，它就越不容易被改坏。**
/// </summary>
public partial class Pickup : Area2D
{
    public string ItemId { get; set; } = "coin";
    public int Amount { get; set; } = 1;

    /// <summary>
    /// 是否走事件总线。false 时走"直接调用"——
    /// 这是本站要对比的两种写法（见 S13Events.OnPickupCollected）。
    /// </summary>
    public bool UseEventBus { get; set; } = true;

    /// <summary>直接调用模式下，拾取物必须自己认识接收方 —— 这就是耦合。</summary>
    public Node? DirectReceiver { get; set; }

    /// <summary>由站台订阅，用来播放拾取表现。**这是 C# event，不是 Godot 信号**（对照用）。</summary>
    public event Action<Pickup>? Collected;

    private Polygon2D _body = null!;
    private bool _taken;

    public override void _Ready()
    {
        _body = GetNode<Polygon2D>("Body");
        CollisionLayer = GameLayers.Pickup;
        CollisionMask = GameLayers.Player;
        AddToGroup("pickups");

        BodyEntered += OnBodyEntered;
    }

    private void OnBodyEntered(Node2D body)
    {
        if (_taken || !body.IsInGroup("player")) return;

        _taken = true;
        Visible = false;

        if (UseEventBus)
        {
            // ✅ 走事件总线：拾取物不认识任何人
            EventBus.Instance.EmitSignal(EventBus.SignalName.ItemPickedUp, ItemId, Amount);
        }
        else if (DirectReceiver is IItemReceiver receiver)
        {
            // ❌ 直接调用：拾取物必须知道"谁来处理这件事"
            receiver.OnItemCollected(ItemId, Amount);
        }

        Collected?.Invoke(this);
        QueueFree();
    }
}

/// <summary>
/// 「直接调用」模式下的接收方接口。
/// 注意：有了这个接口，拾取物就**被迫认识一个抽象** ——
/// 而走事件总线时，它连抽象都不需要知道。
/// </summary>
public interface IItemReceiver
{
    void OnItemCollected(string itemId, int amount);
}
