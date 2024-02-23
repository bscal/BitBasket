using BitCup;
using Godot;

public partial class ExplosiveArea : Area2D
{
	public const float SPD_THRESHOLD = 50;
	public const int BIT_THRESHOLD = 10;

	[Export]
	public bool AlwaysExplode;

	public BitManager BitManager;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		BitManager = GetNode<BitManager>("../BitManager");
		Debug.Assert(BitManager != null);

		this.BodyEntered += OnBodyEntered;
	}

	private void OnBodyEntered(Node2D body)
	{
		if (body is RigidBody2D rb
			&& (AlwaysExplode || GetOverlappingBodies().Count > BIT_THRESHOLD))
		{
			if (AlwaysExplode)
			{
				BitManager.Explode(rb);
			}
			else
			{
				int count = 0;
				foreach (var bit in GetOverlappingBodies())
				{
					if (bit is RigidBody2D bitRB
						&& bitRB.LinearVelocity.Length() <= SPD_THRESHOLD)
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
