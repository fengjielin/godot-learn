namespace Dojo.Common;

/// <summary>
/// 一条状态转移规则。
///
/// 为什么要把转移**抽成数据**，而不是写在 if-else 里：
///   状态一多，if-else 会长成这样：
///       if (state == Patrol) { if (seeTarget) state = Chase; else if (noise) ... }
///       else if (state == Chase) { if (!seeTarget &amp;&amp; t > 2.5) ... }
///       ...
///   问题不是"不好看"，而是**你没法一眼回答"从 Chase 能去哪些状态"** ——
///   而调 AI 的时候，你 90% 的时间都在问这个问题。
///
///   把转移做成一张表之后，"从 Chase 出发的所有转移"就是一个查询，
///   而且可以直接画到界面上（本站左上角那个面板就是这么来的）。
///   **可观测性不是附加功能，它是复杂系统能不能被调好的前提。**
/// </summary>
public sealed class Transition
{
    public string From { get; }
    public string To { get; }

    /// <summary>给人和给 HUD 看的说明文字，比如"丢失目标超过 2.5 秒"。</summary>
    public string Label { get; }

    private readonly Func<bool> _condition;

    public Transition(string from, string to, string label, Func<bool> condition)
    {
        From = from;
        To = to;
        Label = label;
        _condition = condition;
    }

    /// <summary>此刻条件是否满足。HUD 会实时显示它，所以它必须是只读的、无副作用的。</summary>
    public bool IsSatisfied => _condition();
}
