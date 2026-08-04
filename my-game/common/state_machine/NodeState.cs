using Godot;

namespace MyGame.Common.StateMachine;

/// <summary>
/// 状态基类 — 所有具体状态（Idle、Run、Jump、Attack 等）继承此类。
/// 每个状态作为 NodeFiniteStateMachine 的子节点挂在场景树中，
/// 可在 Godot 编辑器里可视化编辑各状态的导出属性。
///
/// 使用方式：
/// 1. 新建脚本，继承 NodeState
/// 2. 重写 Enter / Exit / ProcessState / PhysicsProcessState
/// 3. 在状态逻辑中调用 EmitSignal(SignalName.Transition, "目标状态名") 切换状态
/// </summary>
public partial class NodeState : Node
{
    /// <summary>
    /// 状态切换信号 — 携带目标状态的节点名称（大小写不敏感）。
    /// 在具体状态中通过 EmitSignal(SignalName.Transition, "Idle") 发出。
    /// </summary>
    [Signal]
    public delegate void TransitionEventHandler(string stateName);

    /// <summary>
    /// 进入状态时调用一次。
    /// 典型用途：播放进入动画、重置计时器、启用/禁用相关组件。
    /// </summary>
    public virtual void Enter() { }

    /// <summary>
    /// 离开状态时调用一次。
    /// 典型用途：清理临时效果、保存状态数据、停止相关动画。
    /// </summary>
    public virtual void Exit() { }

    /// <summary>
    /// 每帧调用（与 _Process 同步）。
    /// 典型用途：非物理逻辑，如 UI 更新、动画参数调整。
    /// </summary>
    public virtual void ProcessState(double delta) { }

    /// <summary>
    /// 每物理帧调用（与 _PhysicsProcess 同步）。
    /// 典型用途：移动计算、重力应用、碰撞检测后的位置修正。
    /// </summary>
    public virtual void PhysicsProcessState(double delta) { }
}
