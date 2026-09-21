using Godot;

namespace Dojo.Stations;

/// <summary>
/// S01 用的「游走方块」——本站唯一会被反复创建和销毁的对象。
///
/// 它的存在意义就是让你亲眼看到节点的一生：
///   _EnterTree → _Ready → _PhysicsProcess（很多帧）→ _ExitTree → 真正释放
/// 打开 Godot 的「输出」面板，每次生成/销毁都会打印一行。
///
/// 学习要点：
///   1. _EnterTree / _ExitTree 可能被调用多次（节点被移出再放回场景树时）。
///      所以「只应该发生一次」的事情要放在 _Ready，或者用标志位自己保护。
///   2. 在 _Ready 里 GetNode 是安全的：此时所有子节点都已在树中且已 _Ready。
///      在 _EnterTree 里就不一定 —— 那时子节点可能还没被加上去。
///   3. QueueFree() 是「排队销毁」：它不会立刻执行，而是在本帧末尾安全地释放。
///      调用它之后的代码仍然会运行，所以别写「QueueFree 之后接着用这个对象」。
///   4. 自定义信号让这个方块不需要知道「谁在听」。本站只订阅它，它完全不认识本站。
/// </summary>
public partial class Wanderer : CharacterBody2D
{
    /// <summary>撞墙时发出，参数是累计弹跳次数。</summary>
    [Signal] public delegate void BouncedEventHandler(int bounceCount);

    /// <summary>寿命耗尽、即将销毁时发出。</summary>
    [Signal] public delegate void ExpiredEventHandler(int id, float survivedSeconds);

    [Export] public float Speed { get; set; } = 200f;

    /// <summary>存活时间（秒）。改这个值就能观察到 _ExitTree 的时机变化。</summary>
    [Export] public float Lifetime { get; set; } = 3f;

    /// <summary>由生成者赋值。要在 AddChild 之前设好，因为 _EnterTree 会立刻打印它。</summary>
    public int Id { get; set; }

    public int BounceCount { get; private set; }
    public float Age { get; private set; }

    private Polygon2D _body = null!;
    private bool _expired;

    public override void _EnterTree()
    {
        GD.Print($"[Wanderer#{Id}] _EnterTree   ← 刚进入场景树。此时子节点还不保证齐全");
    }

    public override void _Ready()
    {
        _body = GetNode<Polygon2D>("Body");
        AddToGroup("wanderers");

        var rng = new RandomNumberGenerator();
        rng.Randomize();

        var angle = rng.RandfRange(0f, Mathf.Tau);
        Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Speed;
        _body.Color = Color.FromHsv(rng.Randf(), 0.5f, 0.95f);

        GD.Print($"[Wanderer#{Id}] _Ready        ← 子节点齐全，可以安全 GetNode。最常用的初始化点");
    }

    public override void _PhysicsProcess(double delta)
    {
        Age += (float)delta;

        // MoveAndSlide 会移动节点并记录本帧的碰撞。必须在它之后才能读 GetSlideCollision。
        MoveAndSlide();
        ReflectIfHitWall();

        if (Age >= Lifetime) Expire();
    }

    public override void _ExitTree()
    {
        GD.Print($"[Wanderer#{Id}] _ExitTree     ← 正在离开场景树。QueueFree 在这之后才真正回收内存");
    }

    /// <summary>供 GetTree().CallGroup(...) 调用的「分组指令」。</summary>
    public void RetreatTo(Vector2 target)
    {
        Velocity = (target - GlobalPosition).Normalized() * Speed * 1.8f;
    }

    private void ReflectIfHitWall()
    {
        if (GetSlideCollisionCount() == 0) return;

        var normal = GetSlideCollision(0).GetNormal();
        Velocity = Velocity.Bounce(normal);
        BounceCount++;
        EmitSignal(SignalName.Bounced, BounceCount);
    }

    private void Expire()
    {
        if (_expired) return;
        _expired = true;

        EmitSignal(SignalName.Expired, Id, Age);
        QueueFree();
    }
}
