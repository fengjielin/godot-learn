using Godot;
using Dojo.Common;
using Dojo.Game;

namespace Dojo.Hub;

/// <summary>
/// 枢纽里的一座传送门 —— 通往一个练习站。
///
/// 学习要点：
///   1. Area2D 是「不参与物理阻挡的检测器」。它不会被撞开，只负责发现「谁进来了」。
///      这里的 BodyEntered / BodyExited 就是 Area2D 的信号，信号用 += 订阅。
///   2. Area2D 能发现 CharacterBody2D，靠的是：玩家在 Player 层（layer = 2），
///      而本 Area2D 的 mask 里包含 Player 层。这就是「层 vs 掩码」的实际用途。
///   3. 传送门自己处理「按 E 进入」。因为「我是不是当前被踩着的那个门」这个状态
///      只有门自己最清楚，交给 Hub 统一处理反而要来回传状态。
/// </summary>
public partial class Portal : Area2D
{
    private StationInfo _info = null!;
    private Polygon2D _pad = null!;
    private Polygon2D _inner = null!;
    private Label _label = null!;
    private Label _prompt = null!;

    private bool _playerInside;
    private bool _completed;
    private Color _baseColor = Colors.White;

    public StationInfo Info => _info;

    public override void _Ready()
    {
        _pad = GetNode<Polygon2D>("Pad");
        _inner = GetNode<Polygon2D>("Inner");
        _label = GetNode<Label>("Label");
        _prompt = GetNode<Label>("Prompt");

        _prompt.Visible = false;

        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
    }

    /// <summary>
    /// 装配数据。必须在 AddChild 之后调用（因为要用到 _Ready 里缓存好的子节点）。
    /// </summary>
    public void Configure(StationInfo info)
    {
        _info = info;
        Name = info.Id;
        _completed = SaveSystem.Instance.IsStationCompleted(info.Id);

        _label.Text = info.DisplayName;

        if (!info.Implemented)
        {
            _baseColor = new Color(0.24f, 0.26f, 0.32f);
            _label.Modulate = new Color(1, 1, 1, 0.45f);
        }
        else if (_completed)
        {
            _baseColor = new Color(0.28f, 0.82f, 0.50f);
            _label.Modulate = Colors.White;
        }
        else
        {
            _baseColor = new Color(0.26f, 0.68f, 0.95f);
            _label.Modulate = Colors.White;
        }

        _pad.Color = _baseColor;
        _inner.Color = new Color(0.07f, 0.09f, 0.13f);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_playerInside || _info is null) return;
        if (!@event.IsActionPressed("interact")) return;

        GetViewport().SetInputAsHandled();
        // 不在这里判断「建好了没」—— 交给 SceneRouter 统一处理。
        // 未建成的站会由 Router 发一条提示，这样按钮逻辑只有一处。
        SceneRouter.Instance.EnterStation(_info.Id);
    }

    private void OnBodyEntered(Node2D body)
    {
        if (!body.IsInGroup("player")) return;

        _playerInside = true;
        _prompt.Visible = true;
        _prompt.Text = BuildPromptText();
        _pad.Scale = new Vector2(1.12f, 1.12f);
        _pad.Color = _baseColor.Lightened(0.25f);
    }

    private string BuildPromptText()
    {
        if (!_info.Implemented)
            return $"{_info.Skill}    [建设中]";

        return _completed
            ? $"{_info.Skill}    [已通关] 按 E 再练一次"
            : $"{_info.Skill}    按 E 进入";
    }

    private void OnBodyExited(Node2D body)
    {
        if (!body.IsInGroup("player")) return;

        _playerInside = false;
        _prompt.Visible = false;
        _pad.Scale = Vector2.One;
        _pad.Color = _baseColor;
    }
}
