using System.Globalization;
using Godot;

namespace Dojo.Dev;

/// <summary>
/// 开发期工具：输入回放（Autoload）。
///
/// 只有命令行带上 --dev-input=... 时才生效；正常游玩时它对游戏毫无影响。
///
/// 为什么需要它：
///   要以 --write-movie 把游戏渲染成 PNG 来检查画面时，Godot 并不会帮你按键，
///   所以截到的永远是"静止的初始画面"。有了输入回放，就能让引擎自己"玩"一段，
///   截到真正有内容的帧 —— 这其实也是自动化回归测试的雏形。
///
/// 用法（注意 -- 分隔符，Godot 只把 -- 之后的参数当作"用户参数"）：
///   godot --path . --write-movie shot.png --quit-after 180 -- --dev-input=spawn:0.3:1.6,command:2.2:2.4
///
/// 语法：--dev-input=动作名:按下秒数[:松开秒数]，多条用逗号分隔。
///
/// 学习要点：
///   Input.ActionPress() / Input.ActionRelease() 可以"假装"某个动作被按下，
///   但它们只改「动作状态」，不会产生 InputEvent —— 也就是说只对
///   Input.IsActionPressed 这类「轮询」写法有效。
///   要想同时触发 _Input / _UnhandledInput 这类「事件」写法，必须走
///   Input.ParseInputEvent()。两种方式这里都做了一遍，正好对照。
/// </summary>
public partial class DevAutomation : Node
{
    private sealed class Cue
    {
        public string Action = "";
        public double Start;
        public double End;
        public bool Held;
        /// <summary>释放过的提示不再重复触发。少了这个标志，松开后下一帧会立刻又按下。</summary>
        public bool Done;
    }

    private readonly List<Cue> _cues = new();
    private double _time;
    private double _latestEnd;
    private bool _finished;

    public override void _Ready()
    {
        // 必须设成 Always：暂停菜单会 GetTree().Paused = true，
        // 如果这个工具也跟着被冻住，那"暂停之后"的输入（方向键逛菜单、回车确认）
        // 就再也送不进去了 —— 自动化验证会在这里静默停住，而且看起来像"游戏卡了"。
        ProcessMode = ProcessModeEnum.Always;

        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (!arg.StartsWith("--dev-input=")) continue;

            var payload = arg["--dev-input=".Length..];
            foreach (var piece in payload.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = piece.Split(':');
                if (fields.Length < 2) continue;
                if (!double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var start))
                    continue;

                var end = fields.Length > 2 &&
                          double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedEnd)
                    ? parsedEnd
                    : start + 0.06;

                _cues.Add(new Cue { Action = fields[0], Start = start, End = end });
                _latestEnd = Math.Max(_latestEnd, end);
            }
        }

        if (_cues.Count > 0)
            GD.Print($"[DevAutomation] {_cues.Count} input cue(s) loaded: {string.Join(", ", _cues.ConvertAll(c => $"{c.Action}@{c.Start}-{c.End}"))}");
    }

    public override void _Process(double delta)
    {
        if (_cues.Count == 0 || _finished) return;

        _time += delta;

        foreach (var cue in _cues)
        {
            if (cue.Done) continue;

            if (!cue.Held && _time >= cue.Start)
            {
                Inject(cue.Action, true);
                cue.Held = true;
                GD.Print($"[DevAutomation] press '{cue.Action}' at t={_time:0.00}s");
            }

            if (cue.Held && _time >= cue.End)
            {
                Inject(cue.Action, false);
                cue.Held = false;
                cue.Done = true;
                GD.Print($"[DevAutomation] release '{cue.Action}' at t={_time:0.00}s");
            }
        }

        // 只有所有提示都过了各自的结束时间才算跑完。
        // （早先的版本用「当前没有任何按键被按住」来判断，会在两个提示的间隙里提前收工。）
        if (_time > _latestEnd + 0.1)
        {
            _finished = true;
            GD.Print($"[DevAutomation] all cues finished at t={_time:0.00}s");
        }
    }

    public override void _ExitTree()
    {
        // 别把动作留在按下状态，否则退出后可能影响其它测试。
        foreach (var cue in _cues)
            if (cue.Held) Inject(cue.Action, false);
    }

    /// <summary>
    /// 同时走两条路：
    ///   ParseInputEvent → 产生真正的 InputEvent，能触发 _Input / _UnhandledInput 等事件式写法
    ///   ActionPress/Release → 直接改动作状态，保证 Input.IsActionPressed 这类轮询写法一定看到
    /// </summary>
    private static void Inject(string action, bool pressed)
    {
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = pressed });

        if (pressed) Input.ActionPress(action);
        else Input.ActionRelease(action);
    }
}
