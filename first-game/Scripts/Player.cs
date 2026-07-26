using Godot;
using System;

public partial class Player : Area2D
{

	// Don't forget to rebuild the project so the editor knows about the new signal.
	[Signal]
	public delegate void HitEventHandler(); // 自定义信号

	[Export]
	public int Speed { get; set; } = 400; // How fast the player will move (pixels/sec).

	public Vector2 ScreenSize; // Size of the game window.
														 // Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		ScreenSize = GetViewportRect().Size;
		Hide();
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		var velocity = Vector2.Zero; // The player's movement vector.

		if (Input.IsActionPressed("Right"))
		{
			velocity.X += 1;
		}

		if (Input.IsActionPressed("Left"))
		{
			velocity.X -= 1;
		}

		if (Input.IsActionPressed("Bottom"))
		{
			velocity.Y += 1;
		}

		if (Input.IsActionPressed("Top"))
		{
			velocity.Y -= 1;
		}

		var animatedSprite2D = GetNode<AnimatedSprite2D>("AnimatedSprite2D");

		if (velocity.Length() > 0)
		{
			velocity = velocity.Normalized() * Speed;
			animatedSprite2D.Play();
		}
		else
		{
			animatedSprite2D.Stop();
		}

		Position += velocity * (float)delta;
		Position = new Vector2(
				x: Mathf.Clamp(Position.X, 0, ScreenSize.X),
				y: Mathf.Clamp(Position.Y, 0, ScreenSize.Y)
		);

		// 根据移动方向选择动画和朝向
		if (velocity.X != 0) // 水平移动
		{
			animatedSprite2D.Animation = "walk";         // 水平移动 -> 播放行走动画
			animatedSprite2D.FlipV = false;              // 垂直方向不翻转
			animatedSprite2D.FlipH = velocity.X < 0;     // 向左走时水平翻转
		}
		else if (velocity.Y != 0) // 垂直移动
		{
			animatedSprite2D.Animation = "up";           // 垂直移动 -> 播放上下动画
			animatedSprite2D.FlipV = velocity.Y > 0;     // 向下走时垂直翻转
		}
	}


	// We also specified this function name in PascalCase in the editor's connection window.
	private void OnBodyEntered(Node2D body)
	{
		Hide(); // Player disappears after being hit.
		EmitSignal(SignalName.Hit);
		// Must be deferred as we can't change physics properties on a physics callback.
		GetNode<CollisionShape2D>("CollisionShape2D").SetDeferred(CollisionShape2D.PropertyName.Disabled, true);
	}

	public void Start(Vector2 position)
	{
		Position = position;
		Show();
		GetNode<CollisionShape2D>("CollisionShape2D").Disabled = false;
	}
}
