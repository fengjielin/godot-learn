using Godot;
using System;

/// <summary>
/// 旋转的玩家角色 — 按左右键旋转朝向，按上下键朝当前方向移动。
/// 继承自 Sprite2D，所以能显示一张图片并控制它的旋转和位置。
/// </summary>
public partial class RotatingPlayer : Sprite2D
{
  // 移动速度（像素/秒），数值越大跑得越快
  private int _speed = 400;

  // 旋转速度（弧度/秒），Pi 约等于 180°/秒
  private float _angularSpeed = Mathf.Pi;

  // 缩放速度：每次滚轮缩放的比例（0.1 = 每次缩放 ±10%）
  private float _zoomSpeed = 0.1f;

  // 缩放范围限制（避免缩到看不见或大到出屏）
  private float _minScale = 0.1f;
  private float _maxScale = 5.0f;

  public override void _Ready()
  {
    var blinkingTimer = GetNode<Timer>("BlinkingTimer");
    blinkingTimer.Timeout += OnTimerTimeout;
  }

  /// <summary>
  /// _Process 每帧都会自动调用一次。
  /// delta 是上一帧到这一帧经过的时间（秒），用来让移动速度与帧率无关。
  /// </summary>
  public override void _Process(double delta)
  {
    // ---------- 左右方向输入：控制旋转 ----------
    var direction = 1;  // 0 = 不转，-1 = 左转，1 = 右转
    if (Input.IsActionPressed("ui_left"))       // 按下 ← 键
    {
      direction = -1;   // 负值 = 逆时针旋转
    }
    else if (Input.IsActionPressed("ui_right")) // 按下 → 键
    {
      direction = 1;    // 正值 = 顺时针旋转
    }

    // 让精灵的旋转角度累加：角速度 × 方向 × 时间增量
    Rotation += _angularSpeed * direction * (float)delta;

    // ---------- 上下方向输入：控制移动 ----------
    var velocity = Vector2.Zero;  // 初始速度为零
    if (Input.IsActionPressed("ui_up"))    // 按下 ↑ 键
    {
      // Vector2.Up 默认朝上 (0, -1)，Rotated(Rotation) 让它朝向当前旋转方向
      velocity = Vector2.Up.Rotated(Rotation) * _speed;
    }
    else if (Input.IsActionPressed("ui_down"))  // 按下 ↓ 键
    {
      // Vector2.Down 默认朝下 (0, 1)，同样旋转到当前朝向
      velocity = Vector2.Down.Rotated(Rotation) * _speed;
    }

    // 用速度 × 时间增量 来更新位置，实现与帧率无关的平滑移动
    Position += velocity * (float)delta;
  }

  /// <summary>
  /// _Input 在有输入事件（键盘、鼠标、滚轮等）时自动调用。
  /// 这里用它来检测鼠标滚轮，实现精灵的缩放。
  /// </summary>
  public override void _Input(InputEvent @event)
  {
    // 判断当前事件是不是鼠标滚轮事件
    if (@event is InputEventMouseButton mouseEvent)
    {
      // 如果滚轮向上滚动 → 放大
      if (mouseEvent.ButtonIndex == MouseButton.WheelUp)
      {
        // Scale 是 Vector2 类型，.X 和 .Y 同时乘一个系数（>1 放大）
        // 比如 1.0 → 1.1 → 1.21… 每次都放大 10%
        Scale *= 1.0f + _zoomSpeed;
      }
      // 如果滚轮向下滚动 → 缩小
      else if (mouseEvent.ButtonIndex == MouseButton.WheelDown)
      {
        // 除以 1.1 → 0.909… 每次都缩小约 9%
        Scale /= 1.0f + _zoomSpeed;
      }

      // 把 Scale 的 X 和 Y 限制在 [_minScale, _maxScale] 范围内
      // Mathf.Clamp 把值限制在最小值和最大值之间
      Scale = new Vector2(
        Mathf.Clamp(Scale.X, _minScale, _maxScale),
        Mathf.Clamp(Scale.Y, _minScale, _maxScale)
      );
    }
  }


  // We also specified this function name in PascalCase in the editor's connection window.
  private void OnButtonPressed()
  {
    GD.Print("按下按钮");
    SetProcess(!IsProcessing());
  }
  private void OnTimerTimeout()
  {
    Visible = !Visible;
  }
}


