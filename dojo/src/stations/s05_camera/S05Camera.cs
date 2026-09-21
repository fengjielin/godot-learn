using Godot;
using Dojo.Common;
using Dojo.Entities;

namespace Dojo.Stations;

/// <summary>
/// S05 · 相机与打击感
///
/// 这是一间 1800×1200 的手感实验室（比屏幕大，所以相机会真的动起来）。
/// 左上角有五个开关，按 1~5 随时开关：
///
///   1 屏幕震动     —— 撞柱子时抖
///   2 顿帧         —— 打中假人时时间短暂变慢
///   3 平滑跟随     —— 关掉就变成"直接贴住玩家"
///   4 前瞻         —— 相机会朝鼠标方向偏移一点
///   5 相机边界     —— 关掉之后相机会拍到关卡外面的黑区
///
/// **这一站的设计意图是"可对比"**：每个效果都能单独关掉，
/// 所以你不是"读到一个参数"，而是"感觉到它值多少"。
/// 打击感这种东西，看代码是学不会的，必须手动开关几次。
///
/// 地板上有每 200 像素一个的点阵 —— 那不是装饰，
/// 没有参照物的话，相机移动你是看不出来的。
/// </summary>
public partial class S05Camera : StationBase
{
    public override string StationId => "s05_camera";

    private static readonly Rect2 Arena = new(0, 0, 1800, 1200);
    private static readonly Vector2 SpawnPoint = new(900, 600);

    /// <summary>两个对角角落。走到这两个点就能把四条边界全体验一遍。</summary>
    private static readonly Vector2 CornerNE = new(1650, 150);
    private static readonly Vector2 CornerSW = new(150, 1050);

    private const float CornerRadius = 120f;
    private const int BumpGoal = 3;
    private const int HitGoal = 3;
    private const float TraverseGoal = 300f;

    private Player _player = null!;
    private CameraRig _camera = null!;
    private Label _panel = null!;

    private BumpPost[] _posts = Array.Empty<BumpPost>();
    private TrainingDummy[] _dummies = Array.Empty<TrainingDummy>();
    private Polygon2D _cornerNeMark = null!;
    private Polygon2D _cornerSwMark = null!;

    private int _bumps;
    private int _hits;
    private int _attacks;
    private bool _reachedNe;
    private bool _reachedSw;
    private float _movedWithLookAhead;
    private float _movedWithoutSmoothing;
    private Vector2 _lastPlayerPosition;
    private double _pulse;

    private readonly bool[] _taskDone = new bool[5];

    protected override void StationReady()
    {
        _player = GetNode<Player>("Player");
        _camera = GetNode<CameraRig>("Camera");
        _lastPlayerPosition = _player.GlobalPosition;

        _posts = new[] { GetNode<BumpPost>("Post1"), GetNode<BumpPost>("Post2"), GetNode<BumpPost>("Post3"), GetNode<BumpPost>("Post4") };
        _dummies = new[] { GetNode<TrainingDummy>("Dummy1"), GetNode<TrainingDummy>("Dummy2") };

        _cornerNeMark = GetNode<Polygon2D>("CornerNE");
        _cornerSwMark = GetNode<Polygon2D>("CornerSW");

        foreach (var post in _posts)
            post.Bumped += OnPostBumped;

        LevelKit.CreateBorderWalls(this, Arena);
        _camera.SetBounds(Arena);

        BuildGrid();
        BuildLabels();
        SetStatus("按 1~5 逐个关掉这些效果，再玩一遍 —— 差值就是它们各自的价值");
    }

    public override void _Process(double delta)
    {
        TrackTraverse();
        CheckCorners();
        CheckGoals();

        _pulse += delta;
        var glow = 0.55f + 0.45f * Mathf.Sin((float)_pulse * 2.6f);
        _cornerNeMark.Color = new Color(0.35f, 0.85f, 0.95f, _reachedNe ? 0.9f : glow * 0.5f);
        _cornerSwMark.Color = new Color(0.35f, 0.85f, 0.95f, _reachedSw ? 0.9f : glow * 0.5f);

        UpdatePanel();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("aux_1")) { ScreenFx.Instance.ShakeEnabled = !ScreenFx.Instance.ShakeEnabled; FlashToggle("屏幕震动", ScreenFx.Instance.ShakeEnabled); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_2")) { ScreenFx.Instance.HitstopEnabled = !ScreenFx.Instance.HitstopEnabled; FlashToggle("顿帧", ScreenFx.Instance.HitstopEnabled); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_3")) { _camera.SmoothFollow = !_camera.SmoothFollow; FlashToggle("平滑跟随", _camera.SmoothFollow); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_4")) { _camera.LookAheadEnabled = !_camera.LookAheadEnabled; FlashToggle("前瞻", _camera.LookAheadEnabled); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("aux_5")) { ToggleLimits(); GetViewport().SetInputAsHandled(); return; }

        if (@event.IsActionPressed("attack"))
        {
            TryAttack();
            GetViewport().SetInputAsHandled();
            return;
        }

        // 滚轮缩放：顺带演示"鼠标也是输入"，而且滚轮天然适合做连续调节
        if (@event is InputEventMouseButton { Pressed: true } wheel)
        {
            if (wheel.ButtonIndex == MouseButton.WheelUp) { _camera.ZoomBy(1f); GetViewport().SetInputAsHandled(); return; }
            if (wheel.ButtonIndex == MouseButton.WheelDown) { _camera.ZoomBy(-1f); GetViewport().SetInputAsHandled(); return; }
        }

        base._UnhandledInput(@event);
    }

    // ---------- 打击 ----------

    private void TryAttack()
    {
        _attacks++;

        TrainingDummy? best = null;
        var bestDistance = float.MaxValue;

        foreach (var dummy in _dummies)
        {
            var toDummy = dummy.GlobalPosition - _player.GlobalPosition;
            var distance = toDummy.Length();
            if (distance > 100f) continue;
            // 必须大致朝着它打
            if (toDummy.Normalized().Dot(_player.Facing) < 0.35f) continue;
            if (distance >= bestDistance) continue;

            best = dummy;
            bestDistance = distance;
        }

        if (best is null)
        {
            Flash("挥空了 —— 走到假人旁边、朝着它按 J", 1.4);
            return;
        }

        best.Hit();
        _hits++;

        // 打击感三件套。三个都可以在左上角单独关掉。
        ScreenFx.Instance.Shake(0.5f);
        ScreenFx.Instance.Hitstop(0.06f);
        ScreenFx.Instance.Flash(new Color(1f, 0.94f, 0.78f), 0.3f, 0.14f);
    }

    private void OnPostBumped(float speed)
    {
        _bumps++;

        // 撞击力度直接换算成震动强度：跑得越快、抖得越猛。
        // 这里刻意按速度归一化（而不是固定值），否则"轻轻蹭一下"和"全速撞上去"手感一样。
        var trauma = Mathf.Clamp(speed / 260f, 0.25f, 0.85f);
        ScreenFx.Instance.Shake(trauma);
    }

    // ---------- 角落与位移 ----------

    private void CheckCorners()
    {
        if (!_reachedNe && _player.GlobalPosition.DistanceTo(CornerNE) < CornerRadius) _reachedNe = true;
        if (!_reachedSw && _player.GlobalPosition.DistanceTo(CornerSW) < CornerRadius) _reachedSw = true;

        if (_reachedNe && _reachedSw) MarkTaskOnce(2);
    }

    private void TrackTraverse()
    {
        var moved = _player.GlobalPosition.DistanceTo(_lastPlayerPosition);
        _lastPlayerPosition = _player.GlobalPosition;

        // 位移很小的是抖动/浮点噪声，不算"走过路"
        if (moved < 0.5f) return;

        if (_camera.LookAheadEnabled) _movedWithLookAhead += moved;
        if (!_camera.SmoothFollow) _movedWithoutSmoothing += moved;

        if (_movedWithLookAhead >= TraverseGoal) MarkTaskOnce(3);
        if (_movedWithoutSmoothing >= TraverseGoal) MarkTaskOnce(4);
    }

    // ---------- 目标 ----------

    private void CheckGoals()
    {
        if (_bumps >= BumpGoal) MarkTaskOnce(0);
        if (_hits >= HitGoal) MarkTaskOnce(1);

        if (!Array.TrueForAll(_taskDone, done => done)) return;

        Complete("五种手感效果你都亲手开关对比过了 —— 现在你知道每一个值多少");
    }

    private void MarkTaskOnce(int index)
    {
        if (_taskDone[index]) return;
        _taskDone[index] = true;
        MarkTaskDone(index);
    }

    // ---------- 场景与界面 ----------

    private void BuildGrid()
    {
        // 每 200 像素一个点。没有参照物的话，相机移动是看不出来的 ——
        // 这也是为什么真实项目里地面贴图要有细节。
        var color = new Color(0.16f, 0.185f, 0.235f);
        for (var x = 200; x < Arena.Size.X; x += 200)
        {
            for (var y = 200; y < Arena.Size.Y; y += 200)
            {
                LevelKit.MakeRect(this, new Vector2(x, y), new Vector2(6, 6), color, "Dot");
            }
        }
    }

    private void BuildLabels()
    {
        _panel = LevelKit.MakeLabel(this, new Vector2(24, 62), "", 15,
            new Color(0.85f, 0.91f, 0.98f), HorizontalAlignment.Left, "CameraPanel");
        _panel.Size = new Vector2(620, 160);

        LevelKit.MakeLabel(this, CornerNE, "东北角" + "\n走到底看相机边界", 14, new Color(0.55f, 0.9f, 0.98f));
        LevelKit.MakeLabel(this, CornerSW, "西南角" + "\n走到底看相机边界", 14, new Color(0.55f, 0.9f, 0.98f));
        LevelKit.MakeLabel(this, new Vector2(900, 180), "训练假人：走近按 J 打它（顿帧 + 震屏 + 白闪）", 15,
            new Color(0.95f, 0.78f, 0.62f));
    }

    private void ToggleLimits()
    {
        // CameraRig 没有把"边界开关"做成公开属性，因为它需要配套调用 ApplyLimits()，
        // 所以这里用一个自己的标志位来翻转。
        _limitsOn = !_limitsOn;
        _camera.SetLimitsEnabled(_limitsOn);
        FlashToggle("相机边界", _limitsOn);
    }

    private bool _limitsOn = true;

    private void FlashToggle(string name, bool on)
    {
        Flash($"{name}：{(on ? "开" : "关")}");
    }

    private void UpdatePanel()
    {
        var fx = ScreenFx.Instance;

        _panel.Text =
            $"【手感开关】按 1~5 切换 —— 关掉再玩一遍，差值就是它的价值\n" +
            $"   1 屏幕震动   [{OnOff(fx.ShakeEnabled)}]      2 顿帧       [{OnOff(fx.HitstopEnabled)}]\n" +
            $"   3 平滑跟随   [{OnOff(_camera.SmoothFollow)}]      4 前瞻       [{OnOff(_camera.LookAheadEnabled)}]\n" +
            $"   5 相机边界   [{OnOff(_limitsOn)}]\n" +
            $"鼠标滚轮缩放：{_camera.Zoom.X:0.00}x      相机基线 ({_camera.Baseline.X:0},{_camera.Baseline.Y:0})      创伤 {fx.Trauma:0.00}";

        var corners = (_reachedNe ? 1 : 0) + (_reachedSw ? 1 : 0);
        SetStatus(
            $"撞柱 {_bumps}/{BumpGoal}    打击 {_hits}/{HitGoal}（挥空 {_attacks - _hits} 次）    " +
            $"角落 {corners}/2    带动前瞻走了 {_movedWithLookAhead:0}px    关平滑走了 {_movedWithoutSmoothing:0}px");
    }

    private static string OnOff(bool value) => value ? "开" : "关";
}
