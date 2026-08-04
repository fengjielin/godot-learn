using Godot;
using MyGame.Common.StateMachine;

namespace MyGame.Entities.Player;

/// <summary>
/// 玩家状态基类 — 所有玩家具体状态继承此类。
/// 相比普通 NodeState，多了对父节点 Player 的缓存引用，
/// 方便状态直接操控玩家的速度、动画等属性。
/// </summary>
public partial class PlayerState : NodeState
{
    /// <summary>
    /// 玩家引用 — 在 Enter 时自动缓存。
    /// 场景结构: Player → StateMachine → 当前State
    /// 所以 Player = GetParent().GetParent()
    /// </summary>
    protected Player Player { get; private set; }

    public override void Enter()
    {
        // 缓存 Player 引用 — 只需在场景树中向上两级
        // StateMachine(父) → Player(祖父)
        var stateMachine = GetParent();
        if (stateMachine != null)
        {
            Player = stateMachine.GetParent<Player>();
        }
    }
}
