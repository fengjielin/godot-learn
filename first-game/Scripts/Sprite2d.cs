using Godot;
using System;

public partial class Sprite2d : Sprite2D
{
  private int _speed = 400;
  private float _angularSpeed = Mathf.Pi;

  public override void _Process(double delta)
  {

    var direction = 0;
    if (Input.IsActionPressed("ui_left"))
    {
      direction = -1;
    }
    else if (Input.IsActionPressed("ui_right"))
    {
      direction = 1;
    }

    Rotation += _angularSpeed * direction * (float)delta;
    // Rotation += _angularSpeed * (float)delta;

    var velocity = Vector2.Zero;
    if (Input.IsActionPressed("ui_up"))
    {
      velocity = Vector2.Up.Rotated(Rotation) * _speed;
    }
    else if (Input.IsActionPressed("ui_down"))
    {
      velocity = Vector2.Down.Rotated(Rotation) * _speed;
    }
    // var velocity = Vector2.Up.Rotated(Rotation) * _speed;

    Position += velocity * (float)delta;
  }
}


