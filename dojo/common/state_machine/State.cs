using Godot;

namespace Dojo.Common;

/// <summary>
/// 状态基类。一个状态就是场景树里的一个节点 —— 这不是为了好看，是有实际好处的：
///
///   · **在编辑器的「场景」面板里能直接看到有哪几个状态**，
///     不用去翻代码找 `enum`。改 AI 的时候这个视图非常有用。
///   · **每个状态可以有自己独立的 [Export] 参数**（视野距离、攻击前摇……），
///     在检查器里单独调，互不影响。
///   · 状态的增删就是"加一个节点/删一个节点"，不需要动任何 switch。
///
/// 注意：状态节点自己的 `_Process` 是被**关掉**的（StateMachine.AddState 里设的）。
/// 由状态机统一驱动，避免"状态自己的 _Process 和状态机的 _Process 各调一次"。
/// 这是很容易踩的坑：症状是状态逻辑每帧跑两遍，时间相关的行为全都快一倍。
/// </summary>
public partial class State : Node
{
    /// <summary>所属状态机。由 StateMachine.AddState 注入。</summary>
    public StateMachine Machine { get; internal set; } = null!;

    /// <summary>状态名。默认取节点名 —— 所以节点名就是状态名，改名要两边一起改。</summary>
    public virtual string StateName => Name;

    /// <summary>进入这个状态时调用一次。**做一次性的准备工作**（播放特效、重置计时器）。</summary>
    public virtual void Enter() { }

    /// <summary>离开时调用一次。**做完事要在这里收尾**（关特效、清标记），否则会残留。</summary>
    public virtual void Exit() { }

    /// <summary>每个渲染帧。做表现层的事。</summary>
    public virtual void Tick(double delta) { }

    /// <summary>每个物理帧。做位移和判定。</summary>
    public virtual void PhysicsTick(double delta) { }

    /// <summary>
    /// 拿到这个状态机所服务的角色。
    /// 刻意不用 `GetParent().GetParent()` 那种写法 —— 一旦场景层级变一层，
    /// 全工程的状态都会静默取到错的节点。**依赖结构的位置，不如依赖显式的引用。**
    /// </summary>
    protected T Actor<T>() where T : Node => (T)Machine.Actor;
}
