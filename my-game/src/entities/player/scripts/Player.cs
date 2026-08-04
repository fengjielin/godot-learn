using Godot;
using MyGame.Common.StateMachine;

namespace MyGame.Entities.Player;

/// <summary>
/// 玩家主控制器 — CharacterBody2D，持有状态机并暴露运动参数。
///
/// 场景搭建（2D）：
/// Player (CharacterBody2D, 挂此脚本)
/// ├── StateMachine (NodeFiniteStateMachine)
/// │   ├── Idle   (PlayerStateIdle)
/// │   ├── Walk   (PlayerStateWalk)
/// │   ├── Run    (PlayerStateRun)
/// │   ├── Jump   (PlayerStateJump)
/// │   ├── Attack (PlayerStateAttack)
/// │   └── Hurt   (PlayerStateHurt)
/// ├── CollisionShape2D (RectangleShape2D / CapsuleShape2D)
/// ├── Sprite2D        (占位图形)
/// └── Camera2D        (跟随玩家)
/// </summary>
public partial class Player : CharacterBody2D
{
    /// <summary>水平步行速度（像素/秒）</summary>
    [Export]
    public float MoveSpeed { get; set; } = 300.0f;

    /// <summary>水平跑步速度（像素/秒）</summary>
    [Export]
    public float RunSpeed { get; set; } = 500.0f;

    /// <summary>跳跃初速度（像素/秒），2D 中向上为负</summary>
    [Export]
    public float JumpVelocity { get; set; } = -500.0f;

    /// <summary>重力加速度（像素/秒²），2D 中向下为正</summary>
    [Export]
    public float Gravity { get; set; } = 1200.0f;

    /// <summary>移动方向：-1（左）、0（停）、1（右）</summary>
    public float MoveDirection { get; private set; }

    /// <summary>是否按下跑步键（Shift）</summary>
    public bool IsRunPressed { get; private set; }

    /// <summary>状态机引用</summary>
    private NodeFiniteStateMachine _stateMachine;

    public override void _Ready()
    {
        _stateMachine = GetNode<NodeFiniteStateMachine>("StateMachine");
    }

    public override void _PhysicsProcess(double delta)
    {
        // 每物理帧缓存输入 — 各状态直接读取
        MoveDirection = Input.GetAxis("move_left", "move_right");
        IsRunPressed = Input.IsActionPressed("run");
    }
}
