using Godot;
using Dojo.Common;

namespace Dojo.Stations;

/// <summary>
/// 眩晕：被石头砸中之后的 2 秒。
///
/// 这个状态是**最高优先级**的 —— 转移表里 Patrol / Alert / Chase / Attack
/// 四个状态都有 `→ Stun` 的出路，而且它们写在各自那组的**前面**。
///
/// 为什么优先级要在转移表里体现：
///   同一帧里可能同时满足多条转移条件（比如"进入攻击距离"和"被砸中"同时成立）。
///   `StateMachine.EvaluateTransitions` 是**按注册顺序**取第一条满足的，
///   所以**注册顺序就是优先级**。把 `→ Stun` 写在前面 = 眩晕能打断一切。
///
///   这是个很容易忘的约定。如果你发现 AI 偶尔"该被打断却没打断"，
///   先去检查转移表的顺序，而不是去查条件写得对不对。
///
/// 练习任务 ④ 问的"追击与受伤同时发生谁的优先级高"，答案就在这里：
///   **由你决定，而且是显式的、写在表里的决定** —— 这就是状态机比 if-else 强的地方。
/// </summary>
public partial class GuardStunState : State
{
    private double _spinTime;

    public override void Enter()
    {
        var guard = Actor<Guard>();

        guard.StunTimer = guard.StunDuration;
        guard.Velocity = Vector2.Zero;
        guard.SetMood(Guard.Mood.Stun);
        guard.MarkerText = "★";
        guard.HideTelegraph();
        _spinTime = 0;
    }

    public override void Exit()
    {
        var guard = Actor<Guard>();
        guard.MarkerText = "";
        guard.ResetVisualRotation();
    }

    public override void PhysicsTick(double delta) => Actor<Guard>().StopMoving();

    public override void Tick(double delta)
    {
        _spinTime += delta;
        Actor<Guard>().SpinVisual(_spinTime);
    }
}
