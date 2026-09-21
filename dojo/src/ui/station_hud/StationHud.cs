using System.Text;
using Godot;
using Dojo.Game;

namespace Dojo.Ui;

/// <summary>
/// 练习站通用 HUD：标题栏 + 练习任务面板 + 状态行 + 完成提示。
///
/// 学习要点（对应练习站 s06_ui）：
///   1. 所有 Control 节点都挂在 CanvasLayer 下，这样它们不会跟着 2D 世界一起移动/缩放。
///   2. PanelContainer 会自动给子节点套上 StyleBox 的背景；里面再放 MarginContainer
///      来控制内边距，是最常用的「卡片」组合。
///   3. 用 anchor（锚点）+ offset（偏移）来表达"贴着右上角"，
///      比写死坐标更能适应不同分辨率。
///   4. 这个 HUD 做成独立场景而不是写死在每个练习站里 —— 16 个站共用一份，
///      改一次标题栏样式，16 个站一起变。
/// </summary>
public partial class StationHud : CanvasLayer
{
    private Label _titleLabel = null!;
    private Label _skillLabel = null!;
    private PanelContainer _taskPanel = null!;
    private Label _tasksHeader = null!;
    private Label _tasksBody = null!;
    private Label _statusLabel = null!;
    private PanelContainer _completedPanel = null!;
    private Label _completedLabel = null!;

    private string[] _tasks = Array.Empty<string>();
    private readonly HashSet<int> _doneTasks = new();

    private string _baseStatus = "";
    private string _flashStatus = "";
    private double _flashRemaining;

    public override void _Ready()
    {
        _titleLabel = GetNode<Label>("TopBar/TitleLabel");
        _skillLabel = GetNode<Label>("TopBar/SkillLabel");
        _taskPanel = GetNode<PanelContainer>("TaskPanel");
        _tasksHeader = GetNode<Label>("TaskPanel/Margin/VBox/TasksHeader");
        _tasksBody = GetNode<Label>("TaskPanel/Margin/VBox/TasksBody");
        _statusLabel = GetNode<Label>("StatusLabel");
        _completedPanel = GetNode<PanelContainer>("CompletedPanel");
        _completedLabel = GetNode<Label>("CompletedPanel/Label");

        _completedPanel.Visible = false;
    }

    public void Setup(StationInfo info, bool alreadyCompleted)
    {
        _titleLabel.Text = info.DisplayName;
        _skillLabel.Text = info.Skill;
        _tasks = info.Tasks;

        if (alreadyCompleted)
            _doneTasks.Add(0);

        RefreshTasks();
        SetStatus("随便玩，玩坏了按 R 重开这一站");
    }

    public void ToggleTasks() => _taskPanel.Visible = !_taskPanel.Visible;

    /// <summary>设置常态状态文字（会被 FlashStatus 临时覆盖）。</summary>
    public void SetStatus(string text)
    {
        _baseStatus = text;
        if (_flashRemaining <= 0) ApplyStatus();
    }

    /// <summary>临时显示一条消息，seconds 秒后自动恢复常态文字。</summary>
    public void FlashStatus(string text, double seconds = 2.0)
    {
        _flashStatus = text;
        _flashRemaining = seconds;
        ApplyStatus();
    }

    /// <summary>把第 index 条任务标记为已完成（从 0 开始）。</summary>
    public void SetTaskDone(int index, bool done)
    {
        var changed = done ? _doneTasks.Add(index) : _doneTasks.Remove(index);
        if (changed) RefreshTasks();
    }

    public void ShowCompleted(string reason)
    {
        // 完成横幅位于屏幕正中，会和右上角的任务面板叠在一起。
        // 既然目标已经达成，就把任务面板收起来（按 TAB 可以随时再展开）。
        _taskPanel.Visible = false;

        _completedLabel.Text = string.IsNullOrWhiteSpace(reason)
            ? "练习完成！\n按 TAB 可以重新展开任务列表\n按 R 换个做法再来一次    ESC 打开暂停菜单回枢纽"
            : $"练习完成！\n{reason}\n\n按 TAB 重新展开任务列表\n按 R 换个做法再来一次    ESC 打开暂停菜单回枢纽";
        _completedPanel.Visible = true;
    }

    public override void _Process(double delta)
    {
        if (_flashRemaining <= 0) return;

        _flashRemaining -= delta;
        if (_flashRemaining <= 0)
        {
            _flashRemaining = 0;
            ApplyStatus();
        }
    }

    private void ApplyStatus()
        => _statusLabel.Text = _flashRemaining > 0 ? _flashStatus : _baseStatus;

    private void RefreshTasks()
    {
        if (_tasks.Length == 0)
        {
            _tasksBody.Text = "（这一站还没有写练习任务）";
            return;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < _tasks.Length; i++)
        {
            sb.Append(i + 1).Append(". ");
            sb.Append(_doneTasks.Contains(i) ? "[x] " : "[ ] ");
            sb.Append(_tasks[i]);
            if (i < _tasks.Length - 1) sb.Append('\n');
        }

        _tasksBody.Text = sb.ToString();
        _tasksHeader.Text = $"练习任务 {_doneTasks.Count}/{_tasks.Length}（TAB 收起）";
    }
}
