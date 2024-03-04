using BitCup;
using Godot;

public partial class ExplosiveArea : Area2D
{
	public const int BIT_THRESHOLD = 2;

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
				BitManager.Explode(rb);
			}
		}
	}
}
