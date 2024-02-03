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

public struct BitState
{
	public int Index;

	public BitState(int index) { Index = index; }
}

public partial class BitManager : Node2D
{
	public const int MAX_BITS = 256;
	public const int BIT_TIMEOUT = 60 * 60 * 5 / 10;

	public int CurrentIndex = 0;
	public RigidBody2D[] BitPool = new RigidBody2D[MAX_BITS];

	public int[] BitStatesSparse = new int[MAX_BITS];
	public List<BitState> BitStatesDense = new List<BitState>(MAX_BITS);

	[Export]
	public Texture Bit1Texture;
	[Export]
	public Texture Bit100Texture;

	private Vector2 SpawnPosition;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		Engine.MaxFps = 60;

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

			BitStatesSparse[i] = -1;
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

		Rect2 screenRect = GetViewportRect();

		for (int i = BitStatesDense.Count - 1; i >= 0; --i)
		{
			BitState state = BitStatesDense[i];

			if (!screenRect.HasPoint(BitPool[state.Index].Position))
			{
				BitPool[state.Index].ProcessMode = ProcessModeEnum.Disabled;
				BitPool[state.Index].GetNode<Sprite2D>(new NodePath("./Sprite2D")).Hide();

				BitState lastState = BitStatesDense[BitStatesDense.Count - 1];

				BitStatesDense[i] = lastState;
				BitStatesDense.RemoveAt(BitStatesDense.Count - 1);

				BitStatesSparse[lastState.Index] = i;
				BitStatesSparse[state.Index] = -1;
			}
		}
	}

	public void SpawnNode(BitTypes type)
	{
		int idx;
		do
		{
			idx = CurrentIndex;
			CurrentIndex = (CurrentIndex + 1) % MAX_BITS;
		} while (BitStatesSparse[idx] != -1);

		BitPool[idx].Position = SpawnPosition;
		BitPool[idx].ProcessMode = ProcessModeEnum.Always;

		Sprite2D sprite = BitPool[idx].GetNode<Sprite2D>(new NodePath("./Sprite2D"));

		Debug.Assert(type != BitTypes.MaxBitTypes);

		switch (type)
		{
			case BitTypes.Bit1:
				{
					BitPool[idx].Mass = .5f;
					BitPool[idx].LinearVelocity = Vector2.Zero;
					sprite.Texture = (Texture2D)Bit1Texture;
				} break;
			case BitTypes.Bit100:
				{
					BitPool[idx].Mass = 20f;
					BitPool[idx].LinearVelocity = Vector2.Zero;
					sprite.Texture = (Texture2D)Bit100Texture;
				} break;
		}

		sprite.Show();

		int insertIdx = BitStatesDense.Count;
		BitStatesDense.Add(new BitState(idx));
		BitStatesSparse[idx] = (short)insertIdx;
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
