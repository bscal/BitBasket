using Godot;
using System;
using System.Diagnostics;
using System.Linq;

public partial class BitPhysics : Area2D
{
	private RigidBody2D RigidBody;
	private BitManager BitManager;

	private float Timeout;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		RigidBody = GetParent<RigidBody2D>();
		Debug.Assert(RigidBody != null);

		BitManager = GetNode<BitManager>("../BitManager");
		Debug.Assert(BitManager != null);

		this.BodyEntered += Bit_OnBodyEntered;
	}

	private void Bit_OnBodyEntered(Node2D body)
	{
/*		if (body is RigidBody2D rb && body != RigidBody)
		{
			int id = (int)rb.GetMeta("Id");
			int idx = BitManager.BitStatesSparse[id];
			Debug.Assert(idx != -1);
			BitState state = BitManager.BitStatesDense[idx];
			
			if (state.HasExploded)
				return;

			if (!BitManager.CupArea.OverlapsBody(RigidBody))
				return;

			state.HasExploded = true;

			BitManager.BitStatesDense[idx] = state;

			foreach (var explodeBody in GetOverlappingBodies())
			{
				if (explodeBody is RigidBody2D explodeRb
					&& explodeRb != RigidBody)
				{
					Vector2 force;
					explodeRb.ApplyImpulse(force);
				}
			}
		}*/
	}
}
