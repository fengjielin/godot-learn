using Godot;
using System;

public partial class MyScript : TextureRect
{

	public override void _EnterTree()
	{
		GD.Print("EnterTree");
	}


	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		GD.Print("Ready");

		// this.QueueFree();
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		// GD.Print("Process");
	}

	public override void _PhysicsProcess(double delta)
	{
		// GD.Print("PhysicsProcess");
	}

	public override void _ExitTree()
	{
		GD.Print("ExitTree");
	}
}
