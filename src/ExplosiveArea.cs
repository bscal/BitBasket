using BitCup;
using Godot;

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
				// Kind of little hack so that we only count sleeping or slow moving
				// bits. We don't want to count bits that are falling or flying up
				// from other explosions, or atleast minimize counting them.
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
