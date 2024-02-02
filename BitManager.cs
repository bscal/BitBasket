using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;

public enum BitTypes
{
	Bit1,
	Bit100,

	MaxBitTypes
}

public struct BitOrder
{
	public int[] BitAmounts = new int[(int)BitTypes.MaxBitTypes];

	public BitOrder() {}
}

public struct BitLifetime
{
	public int Index;
	public int TicksAlive;
}

public partial class BitManager : Node2D
{
	public const int MAX_BITS = 1024;
	public const int BIT_TIMEOUT = 60 * 60 * 5 / 10;

	public int CurrentIndex = 0;
	public RigidBody2D[] BitPool = new RigidBody2D[MAX_BITS];

	public short[] BitMap = new short[MAX_BITS];
	public List<BitLifetime> Lifetimes = new List<BitLifetime>(MAX_BITS);

	[Export]
	public Texture Bit1Texture;
	[Export]
	public Texture Bit100Texture;

	private Vector2 SpawnPosition;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		Node2D spawnNode = GetNode<Node2D>(new NodePath("./SpawnPosition"));
		if (spawnNode == null)
		{
			GD.PrintErr("No SpawnNode found under BitManager");
			return;
		}

		SpawnPosition = spawnNode.Position;

		PackedScene bitScene = GD.Load<PackedScene>("res://bit.tscn");
		if (bitScene == null)
		{
			GD.PrintErr(string.Format("Bit node could not be found"));
			return;
		}

		for (int i = 0; i < MAX_BITS; ++i)
		{
			BitPool[i] = (RigidBody2D)bitScene.Instantiate();
			AddChild(BitPool[i]);

			BitPool[i].ProcessMode = ProcessModeEnum.Disabled;
			BitPool[i].GetNode<Sprite2D>(new NodePath("./Sprite2D")).Hide();
		}
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		if (Input.IsActionJustPressed("A"))
		{
			BitOrder info = new BitOrder();
			info.BitAmounts[0] = 10;
			TestBits(info);
		}
		else if (Input.IsActionJustPressed("B"))
		{
			SpawnNode(BitTypes.Bit100);
		}

		for (int i = Lifetimes.Count - 1; i >= 0; --i)
		{
			BitLifetime lifetime = Lifetimes[i];
			++lifetime.TicksAlive;
			Lifetimes[i] = lifetime;

			if (lifetime.TicksAlive > BIT_TIMEOUT
				|| BitPool[lifetime.Index].Position.Y > 1024)
			{
				BitPool[lifetime.Index].ProcessMode = ProcessModeEnum.Disabled;
				BitPool[lifetime.Index].GetNode<Sprite2D>(new NodePath("./Sprite2D")).Hide();
				Lifetimes.RemoveAt(i);
				BitMap[Lifetimes[i].Index] = (short)i;
			}
		}
	}

	public void SpawnNode(BitTypes type)
	{
		int idx = CurrentIndex;

		CurrentIndex = (CurrentIndex + 1) % MAX_BITS;

		BitPool[idx].Position = SpawnPosition;
		BitPool[idx].ProcessMode = ProcessModeEnum.Always;

		Sprite2D sprite = BitPool[idx].GetNode<Sprite2D>(new NodePath("./Sprite2D"));

		Debug.Assert(type != BitTypes.MaxBitTypes);

		switch(type)
		{
			case BitTypes.Bit1:
				{
					BitPool[idx].Mass = .5f;
					BitPool[idx].LinearVelocity = Vector2.Zero;
					sprite.Texture = (Texture2D)Bit1Texture;
				} break;
			case BitTypes.Bit100:
				{
					BitPool[idx].Mass = 10f;
					BitPool[idx].LinearVelocity = new Vector2(0, 5);
					sprite.Texture = (Texture2D)Bit100Texture;
				} break;
		}

		sprite.Show();

		short id = BitMap[idx];

		Lifetimes.RemoveAt(id);

		BitMap[idx] = (short)Lifetimes.Count;

		BitLifetime lifetime = new BitLifetime();
		lifetime.Index = idx;
		lifetime.TicksAlive = 0;
		Lifetimes.Add(lifetime);
	}

	public void TestBits(BitOrder bitOrder)
	{
		for (int i = 0; i < (int)BitTypes.MaxBitTypes; ++i)
		{
			for (int bits = 0; bits < bitOrder.BitAmounts[i]; ++bits)
			{
				SpawnNode((BitTypes)i);
			}
		}

	}

}
