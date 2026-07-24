using Godot;
using System;

public partial class SceneLearn : Node2D
{

[Export]
public PackedScene otherScene;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		if (Input.IsActionJustPressed("Jump"))
		{
			// 获取场景树
			SceneTree st = this.GetTree();
			// 场景跳转
			// st.ChangeSceneToFile("res://Scene/Scene03.tscn");
			// st.ChangeSceneToPacked(otherScene);
			// 场景加载
			Node node = otherScene.Instantiate();
			st.CurrentScene.AddChild(node);
		}
		else if (Input.IsActionPressed("Jump"))
		{
		}
		else if (Input.IsActionJustReleased("Jump"))
		{
		}
	}
}
