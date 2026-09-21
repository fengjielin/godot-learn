using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// S14 的子弹。它同时支持两种生命周期模式，用来做 A/B 对比：
///
///   · **朴素模式**（`AutoFree = true`）：每次生成都 `Instantiate`，寿命到了自己 `QueueFree`。
///     这是所有人一开始都会写的做法 —— 也是本站要你亲手量一量代价的做法。
///   · **池化模式**（`AutoFree = false`）：对象由池预先创建、反复复用，
///     子弹只把自己标记为空闲，**销毁权归池所有**。
///
/// ★ 命名教训（本工程真踩过）：
///   这个类最初叫 `Bullet`，而 S03 物理站里也有一个 `Bullet`。
///   两者都在 `Dojo.Stations` 命名空间下 —— **编译器直接报"类型已包含定义"，
///   而且报错行号指向完全无辜的字段声明**，看起来像字段重复，实际是类重名。
///   所以跨功能目录放同类东西时，名字要么带上领域前缀（`PoolBullet`），
///   要么拆到不同的命名空间。**"文件夹分开了"不等于"类型分开了"。**
///
/// 学习要点（和 S07 的飘字池是同一条，但这次你会看到数字）：
///   池化对象必须能**干净地重置**。`Launch()` 里把所有会残留的状态写一遍，
///   否则下一轮复用时它会带着上一次的痕迹出现。
/// </summary>
public partial class PoolBullet : Area2D
{
    /// <summary>是否被池租出去了。朴素模式下这个字段没意义。</summary>
    public bool InUse { get; private set; }

    /// <summary>true = 朴素模式（自己销毁）；false = 池化模式。</summary>
    public bool AutoFree { get; set; }

    private Vector2 _velocity;
    private float _age;
    private float _life;
    private float _spin;
    private Polygon2D _body = null!;

    public override void _Ready()
    {
        _body = GetNode<Polygon2D>("Body");
        CollisionLayer = GameLayers.Projectile;
        CollisionMask = 0;      // 本站只看性能，不做命中判定 —— 少一个变量
    }

    public void Launch(Vector2 position, Vector2 direction, float speed, float life, Color color)
    {
        Position = position;
        _velocity = direction * speed;
        _age = 0f;
        _life = life;
        _spin = speed * 0.01f;

        // 所有会残留的状态都在这里重置 —— 见类注释
        Modulate = Colors.White;
        Rotation = 0f;
        Scale = Vector2.One;
        _body.Color = color;

        InUse = true;
        Visible = true;
    }

    public void Release()
    {
        InUse = false;
        Visible = false;
        if (AutoFree) QueueFree();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!InUse) return;

        var dt = (float)delta;
        _age += dt;
        Position += _velocity * dt;
        Rotation += _spin * dt;

        if (_age >= _life) Release();
    }
}
