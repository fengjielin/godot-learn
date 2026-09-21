using Godot;

namespace Dojo.Common;

/// <summary>
/// 极简性能监视面板（Autoload 单例）。按 F3 开关。
///
/// 学习要点（对应练习站 s14_pooling）：
///   1. 优化前一定要先测量。没有数据就改代码，99% 是在浪费时间。
///   2. 游戏性能看四个数：
///        FPS         —— 综合结果
///        帧时间      —— 7ms 和 14ms 的差别就是 144Hz 与 60Hz 的差别
///        绘制调用数  —— 太多说明没有合批（draw call 是 CPU 瓶颈的常见来源）
///        节点数      —— 暴涨说明在疯狂 Instantiate 而没有回收
///   3. 这里刻意做成纯代码创建的 UI：Autoload 通常没有自己的场景，
///      因为它必须在任何场景之前就存在。
/// </summary>
public partial class DebugHud : Node
{
    private CanvasLayer _layer = null!;
    private Label _label = null!;
    private bool _shown;
    private double _refreshAccumulator;

    /// <summary>刷新间隔。每帧刷新数字会跳得看不清。</summary>
    private const double RefreshInterval = 0.25;

    public override void _Ready()
    {
        _layer = new CanvasLayer { Name = "DebugHudLayer", Layer = 120 };

        var panel = new PanelContainer { Name = "Panel" };
        panel.Position = new Vector2(8, 8);

        _label = new Label { Name = "Stats", Text = "" };
        // 等宽字体能让数字不跳动；这里直接用手写代码加一点视觉处理
        panel.AddChild(_label);

        _layer.AddChild(panel);
        AddChild(_layer);

        SetShown(false);
    }

    public override void _Process(double delta)
    {
        if (!_shown) return;

        _refreshAccumulator += delta;
        if (_refreshAccumulator < RefreshInterval) return;
        _refreshAccumulator = 0;

        var fps = Performance.GetMonitor(Performance.Monitor.TimeFps);
        var processMs = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
        var physicsMs = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
        var drawCalls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        var nodes = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
        var staticMem = Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1048576.0;

        _label.Text =
            $"FPS      {fps,6:0}\n" +
            $"帧时间   {processMs,6:0.00} ms\n" +
            $"物理     {physicsMs,6:0.00} ms\n" +
            $"绘制调用 {drawCalls,6:0}\n" +
            $"节点数   {nodes,6:0}\n" +
            $"内存     {staticMem,6:0.0} MB\n" +
            $"F3 关闭";
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // F3 没有做进 InputMap，因为调试开关不值得占用输入映射。
        // 直接比较物理键码即可。
        if (@event is InputEventKey { Pressed: true, Echo: false } key && key.Keycode == Key.F3)
        {
            SetShown(!_shown);
            GetViewport().SetInputAsHandled();
        }
    }

    private void SetShown(bool shown)
    {
        _shown = shown;
        _layer.Visible = shown;
    }
}
