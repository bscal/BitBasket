using Godot;
using System;
using System.Collections.Generic;

public partial class ForceTriggerArea : Area2D
{
	public List<Node2D> Bodies = new List<Node2D>(32);

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}
}
