using Godot;
using System;

public partial class NodeLearn : Node2D
{
	[Export]
	public Node inputNode;

	[Export]
	public Node newParent;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// 相对路径 or 绝对路径
		// TextureRect node01 = GetNode<TextureRect>("TextureRect3");
		// node01.FlipV = true;

		// Node node02 = GetNode("/root/Scene02/NodeLearn/TextureRect3/TextureRect4");
		// node02.QueueFree();

		// TextureRect node03 = GetNode<TextureRect>("../TextureRect2");
		// node03.QueueFree();

		// Node parentNode = GetParent();
		// GD.Print(parentNode);

		// TextureRect node = GetNode("%TextureRect3") as TextureRect;
		// GD.Print(node);
		// node.FlipV = true;

		// GD.Print(inputNode);
		// 新增节点 
		// Node2D node = new Node2D();
		// node.Name = "NewNode";
		// this.AddChild(node);

		// 移动节点
		// GetParent().RemoveChild(this);
		// newParent.AddChild(this);

		// 延迟执行
		// GetParent().CallDeferred("remove_child", this);
		// newParent.CallDeferred("add_child", this);

		this.CallDeferred(Node.MethodName.Reparent, newParent);
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
