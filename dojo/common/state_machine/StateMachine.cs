using Godot;

namespace Dojo.Common;

/// <summary>
/// 通用有限状态机。
///
/// 用法（见 src/stations/s04_fsm/Guard.cs）：
///   1. 角色场景里放一个 StateMachine 节点，把各个 State 作为它的子节点；
///   2. 角色的 _Ready 里 AddState(每个子状态) + AddTransition(...) 建出转移表；
///   3. Start("Patrol") 开跑。
///
/// 学习要点：
///   1. **一帧只走一步转移。** 转移条件求值完立刻 return。
///      否则可能出现 A→B→C 在一帧内连跳，中间状态的 Enter/Exit 全被压掉，
///      表现层（特效、音效）就会漏播或者闪一下 —— 而且极难复现。
///   2. **先判断转移，再执行当前状态的 Tick。** 顺序反了的话，
///      状态会在"已经该走了"的那一帧还多做一次逻辑，边界行为会飘。
///   3. 状态节点自己的 `_Process` 要关掉（AddState 里做了），
///      由状态机统一驱动。**同一个东西有两个驱动源，是 bug 的温床。**
/// </summary>
public partial class StateMachine : Node
{
    /// <summary>状态切换时发出。参数：(从哪个状态, 到哪个状态, 为什么)。</summary>
    [Signal] public delegate void StateChangedEventHandler(string from, string to, string reason);

    /// <summary>这个状态机服务的角色。由角色在 _Ready 里赋值。</summary>
    public Node Actor { get; set; } = null!;

    public State? Current { get; private set; }
    public string CurrentName => Current?.StateName ?? "(无)";
    public string PreviousName { get; private set; } = "";

    /// <summary>在当前状态里待了多久（秒）。调试 AI 时用得最多的一项。</summary>
    public double TimeInState { get; private set; }

    /// <summary>累计切换过多少次。</summary>
    public int ChangeCount { get; private set; }

    private readonly Dictionary<string, State> _states = new();
    private readonly List<Transition> _transitions = new();
    private readonly List<string> _visitOrder = new();

    public IReadOnlyList<string> VisitOrder => _visitOrder;

    public void AddState(State state)
    {
        _states[state.StateName] = state;
        state.Machine = this;
        // 关键：关掉状态节点自己的处理。驱动权只归状态机。
        state.ProcessMode = ProcessModeEnum.Disabled;
    }

    /// <summary>
    /// 注册一条转移规则。
    /// 本站刻意用**表**而不是 if-else：这样"从某个状态能去哪些状态"是一个可查询的事实，
    /// 可以直接画到界面上给你看（S04 左上角那个面板）。
    /// </summary>
    public void AddTransition(string from, string to, string label, Func<bool> condition)
        => _transitions.Add(new Transition(from, to, label, condition));

    public void Start(string stateName) => ChangeState(stateName, "初始状态");

    /// <summary>某个状态出发的所有转移。HUD 用它来显示"现在有哪些出路、条件满足没"。</summary>
    public List<Transition> TransitionsFrom(string stateName)
    {
        var result = new List<Transition>();
        foreach (var t in _transitions)
            if (t.From == stateName) result.Add(t);
        return result;
    }

    public bool HasVisited(string stateName) => _visitOrder.Contains(stateName);

    public override void _Process(double delta)
    {
        if (Current is null) return;

        TimeInState += delta;

        // 顺序很重要：先看该不该走，再执行当前状态。
        EvaluateTransitions();
        Current.Tick(delta);
    }

    public override void _PhysicsProcess(double delta) => Current?.PhysicsTick(delta);

    public void ChangeState(string to, string reason)
    {
        if (!_states.TryGetValue(to, out var next))
        {
            GD.PushError($"[StateMachine] 没有名为 '{to}' 的状态。检查节点名和转移表是否对得上。");
            return;
        }

        var from = CurrentName;

        Current?.Exit();

        PreviousName = from;
        Current = next;
        TimeInState = 0;
        ChangeCount++;

        if (!_visitOrder.Contains(to)) _visitOrder.Add(to);

        next.Enter();

        EmitSignal(SignalName.StateChanged, from, to, reason);
    }

    private void EvaluateTransitions()
    {
        foreach (var transition in _transitions)
        {
            if (transition.From != CurrentName) continue;
            if (!transition.IsSatisfied) continue;

            ChangeState(transition.To, transition.Label);
            return; // 一帧只走一步，见类注释第 1 条
        }
    }
}
