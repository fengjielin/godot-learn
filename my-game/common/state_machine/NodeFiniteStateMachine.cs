using System.Collections.Generic;
using Godot;

namespace MyGame.Common.StateMachine;

/// <summary>
/// 节点式有限状态机（Finite State Machine）。
///
/// 核心设计：
/// - 状态作为子节点挂载，_Ready 时自动扫描注册，无需手动配置。
/// - 状态切换完全由信号驱动——各状态在合适的时机发出 Transition 信号，
///   状态机收到后执行"离开旧状态 → 进入新状态"的切换流程。
/// - 不强制每帧检查转换条件，由状态自行决定何时申请切换。
///
/// 场景搭建示例：
/// Player (CharacterBody3D)
/// ├── StateMachine (NodeFiniteStateMachine)
/// │   ├── Idle    (NodeState 子类)
/// │   ├── Run     (NodeState 子类)
/// │   ├── Jump    (NodeState 子类)
/// │   └── Attack  (NodeState 子类)
///
/// 对应教程 GDScript 版本：finite_state_machine/node_finite_state_machine.gd
/// </summary>
public partial class NodeFiniteStateMachine : Node
{
    /// <summary>
    /// 初始状态 — 场景就绪后自动进入。
    /// 在 Godot 编辑器的 Inspector 中拖入一个子状态节点即可。
    /// </summary>
    [Export]
    public NodeState InitialNodeState { get; set; }

    /// <summary>
    /// 已注册的状态字典，key = 状态节点名的全小写形式。
    /// 例如子节点名为 "Idle"，则 key 为 "idle"。
    /// </summary>
    private readonly Dictionary<string, NodeState> _nodeStates = new();

    /// <summary>
    /// 当前正在运行的状态。
    /// </summary>
    private NodeState _currentNodeState;

    /// <summary>
    /// 就绪时：扫描子节点注册状态 → 连接信号 → 进入初始状态。
    /// </summary>
    public override void _Ready()
    {
        // 1. 遍历所有子节点，自动收集 NodeState 类型的状态
        foreach (var child in GetChildren())
        {
            if (child is not NodeState nodeState)
                continue;

            // 以节点名的全小写作为 key，实现大小写不敏感的切换查询
            var key = nodeState.Name.ToString().ToLower();
            _nodeStates[key] = nodeState;

            // 2. 连接每个状态的 Transition 信号到本机的切换处理函数
            //    使用 C# 事件模式替代 GDScript 的 connect()，更符合 .NET 习惯
            nodeState.Transition += OnStateTransition;
        }

        // 3. 如果配置了初始状态，立即进入
        if (InitialNodeState != null)
        {
            InitialNodeState.Enter();
            _currentNodeState = InitialNodeState;
        }
    }

    /// <summary>
    /// 每帧委托给当前状态的 ProcessState。
    /// </summary>
    public override void _Process(double delta)
    {
        _currentNodeState?.ProcessState(delta);
    }

    /// <summary>
    /// 每物理帧委托给当前状态的 PhysicsProcessState。
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        _currentNodeState?.PhysicsProcessState(delta);
    }

    /// <summary>
    /// 状态切换处理 — 响应任意状态发出的 Transition 信号。
    ///
    /// 流程：
    /// 1. 检查目标是否与当前状态相同 → 相同则忽略
    /// 2. 查找目标状态是否注册 → 不存在则忽略
    /// 3. 调用当前状态的 Exit() → 调用新状态的 Enter() → 更新引用
    /// </summary>
    /// <param name="stateName">目标状态的节点名称（大小写不敏感）</param>
    private void OnStateTransition(string stateName)
    {
        var key = stateName.ToLower();

        // 防止重复进入同一状态
        if (_currentNodeState != null && key == _currentNodeState.Name.ToString().ToLower())
            return;

        // 目标状态不存在于注册表中
        if (!_nodeStates.TryGetValue(key, out var newState))
        {
            GD.PushWarning($"状态机：未找到目标状态 \"{stateName}\"，已忽略。可用状态: {string.Join(", ", _nodeStates.Keys)}");
            return;
        }

        // 执行切换：旧状态退出 → 新状态进入
        _currentNodeState?.Exit();
        newState.Enter();
        _currentNodeState = newState;

        GD.Print($"状态切换 → {newState.Name}");
    }
}
