using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// S03 的靶子 —— 一个标准的「受击框（Hurtbox）」。
///
/// 学习要点（这个是碰撞层里最容易被忽略、但最实用的一个设置）：
///
///   monitoring   = 我主动检测别人吗？
///   monitorable  = 别人能检测到我吗？
///
///   受击框的正确答案是：**monitoring = false，monitorable = true**。
///   它只被动挨打，从不主动找别人。把它设成这样有两个好处：
///     · 少一份检测开销（场上可能有几百个受击框）
///     · 从代码上表明意图 —— 读到这里的人立刻知道"这个 Area2D 是挨打用的"
///
///   反过来，攻击判定框（hitbox）通常是 monitoring = true, monitorable = false：
///   它主动去撞别人，但不需要被别人撞。
///
///   对比一下本站子弹的设置（见 Bullet.cs）：子弹只检测别人、不需要被检测，
///   所以它是 monitoring = true, monitorable = false。
/// </summary>
public partial class Target : Area2D
{
    [Signal] public delegate void HitRegisteredEventHandler();

    public int HitCount { get; private set; }
    public bool WasHit => HitCount > 0;

    private Polygon2D _body = null!;
    private Polygon2D _core = null!;

    public override void _Ready()
    {
        _body = GetNode<Polygon2D>("Body");
        _core = GetNode<Polygon2D>("Core");

        CollisionLayer = GameLayers.Enemy; // 我是什么：敌人
        CollisionMask = 0;                 // 我不检测任何人
        Monitoring = false;                // 不主动检测
        Monitorable = true;                // 但允许别人检测我
    }

    /// <summary>由子弹调用。</summary>
    public void RegisterHit()
    {
        HitCount++;

        _core.Color = new Color(1f, 0.95f, 0.62f);

        // 中弹反馈：弹一下 + 颜色变化。手感很小，但"有没有反馈"差别很大。
        var tween = CreateTween();
        tween.TweenProperty(_body, "scale", new Vector2(1.3f, 1.3f), 0.05);
        tween.TweenProperty(_body, "scale", Vector2.One, 0.18);

        EmitSignal(SignalName.HitRegistered);
    }
}
