using Godot;

namespace Dojo.Game;

/// <summary>
/// 一个练习站的元信息。纯数据，不含逻辑。
/// </summary>
/// <param name="Id">站 id，同时是目录名与场景名，例如 s01_lifecycle</param>
/// <param name="Order">序号，决定枢纽里的排列顺序</param>
/// <param name="Title">中文标题</param>
/// <param name="Skill">练的是什么能力（枢纽里显示，一行内）</param>
/// <param name="Summary">一句话说明这一站要解决什么问题</param>
/// <param name="Tasks">站内按 TAB 看到的练习任务（完整版在 docs/stations/ 里）</param>
/// <param name="Implemented">是否已经建设完成。未完成的站在枢纽里会显示为「待建设」</param>
public sealed record StationInfo(
    string Id,
    int Order,
    string Title,
    string Skill,
    string Summary,
    string[] Tasks,
    bool Implemented)
{
    /// <summary>场景文件路径。约定：src/stations/&lt;id&gt;/&lt;id&gt;.tscn</summary>
    public string ScenePath => $"res://src/stations/{Id}/{Id}.tscn";

    /// <summary>配套文档路径。约定：docs/stations/&lt;s01-lifecycle&gt;.md</summary>
    public string DocPath => $"res://docs/stations/{Id.Replace('_', '-')}.md";

    /// <summary>枢纽里的显示名，例如 "S01  节点与生命周期"</summary>
    public string DisplayName => $"S{Order:00}  {Title}";
}
