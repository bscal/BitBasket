using BitCup;
using Godot;
using TwitchLib.Api.Helix;

public partial class ExplosiveArea : Area2D
{
	public const int BIT_THRESHOLD = 4;

	[Export]
	public bool AlwaysExplode;

	public BitManager BitManager;

	public override void _Ready()
	{
		BitManager = GetNode<BitManager>("../BitManager");
		Debug.Assert(BitManager != null);

		this.BodyEntered += OnBodyEntered;
	}

	private void OnBodyEntered(Node2D body)
	{
		if (body is RigidBody2D rb)
		{
			if (AlwaysExplode)
			{
				BitManager.Explode(rb);
			}
			else if (GetOverlappingBodies().Count > BIT_THRESHOLD)
			{
				int count = 0;
				foreach(var node in GetOverlappingBodies())
				{
					if (node is RigidBody2D nodeRb
						&& (nodeRb.Sleeping
						|| nodeRb.LinearVelocity.Length() < 64))
					{
						++count;
					}
				}
				if (count > BIT_THRESHOLD)
				{
					BitManager.Explode(rb);
				}
			}
		}
	}
}
