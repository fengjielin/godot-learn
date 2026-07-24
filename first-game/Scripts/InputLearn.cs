using Godot;
using System;

public partial class InputLearn : Node
{
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		// 持续性的
		// if (Input.IsKeyPressed(Key.Space))
		// {
		// 	GD.Print("Space");
		// }

		// if (Input.IsActionJustPressed("Jump"))
		// {
		// 	GD.Print("Jump 按下");
		// }else if (Input.IsActionPressed("Jump"))
		// {
		// 	GD.Print("Jump 按中");
		// }else if (Input.IsActionJustReleased("Jump"))
		// {
		// 	GD.Print("Jump 抬起");
		// }

		// float jump = Input.GetActionRawStrength("Jump");
		// GD.Print(jump);

		// float x = Input.GetAxis("Left", "Right");
		// float y = Input.GetAxis("Top", "Bottom");
		// GD.Print("按下了Left或Right：", x);
		// GD.Print("按下了Top或Bottom：", y);

		Vector2 dir = Input.GetVector("Left", "Right", "Top", "Bottom");
		GD.Print(dir);
	}

	// 事件函数
	// public override void _Input(InputEvent @event)
	// {
	// 	if (@event is InputEventKey)
	// 	{
	// 		var key = @event as InputEventKey;
	// 		if (key.Keycode == Key.Space)
	// 		{
	// 			if (key.IsReleased())
	// 			{
	// 				GD.Print("Space Released");
	// 			}
	// 			else if (key.IsEcho())
	// 			{
	// 				GD.Print("Space 按中");
	// 			}
	// 			else if (key.IsPressed())
	// 			{
	// 				GD.Print("Space 按下");
	// 			}
	// 		}
	// 	}
	// 	if (@event is InputEventMouse)
	// 	{
	// 		var key = @event as InputEventMouse;
	// 		if (key.IsPressed())
	// 		{
	// 			GD.Print("鼠标按下");
	// 			GD.Print("鼠标点击位置", key.Position);
	// 			GD.Print("鼠标按下的是",key.ButtonMask, "键");
	// 		}
	// 	}
	// }

}
