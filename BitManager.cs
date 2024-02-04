using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using static Godot.WebSocketPeer;

public enum BitTypes
{
	Bit1,
	Bit100,
	Bit1000,
	Bit5000,
	Bit10000,

	MaxBitTypes
}

public struct BitOrder
{
	public byte[] BitAmounts = new byte[(int)BitTypes.MaxBitTypes];

	public BitOrder() {}
}

public struct BitState
{
	public int Index;
	public bool HasExploded;

	public BitState(int index)
	{
		Index = index;
		HasExploded = false;
	}
}

public partial class BitManager : Node2D
{
	public const int MAX_BITS = 64;
	public const int BIT_TIMEOUT = 60 * 60 * 5 / 10;
	public const float BIT_TIMER = 0.15f;
	public const int MAX_ORDERS = 32;

	public int CurrentIndex = 0;
	public RigidBody2D[] BitPool = new RigidBody2D[MAX_BITS];

	public int[] BitStatesSparse = new int[MAX_BITS];
	public int BitStatesDenseCount;
	public BitState[] BitStatesDense = new BitState[MAX_BITS];

	public List<BitOrder> BitOrders = new List<BitOrder>(MAX_ORDERS);

	[Export]
	public Texture Bit1Texture;
	[Export]
	public Texture Bit100Texture;
	[Export]
	public Texture Bit1000Texture;
	[Export]
	public Texture Bit5000Texture;
	[Export]
	public Texture Bit10000Texture;

	public Area2D ForceArea;
	public Area2D ForceTriggerArea;
	public Area2D BoundsArea;

	private Vector2 SpawnPosition;
	private float Timer;

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

		ForceArea = GetNode<Area2D>(new NodePath("../ForceArea"));
		ForceTriggerArea = GetNode<Area2D>(new NodePath("../ForceTriggerArea"));
		BoundsArea = GetNode<Area2D>(new NodePath("../BoundsArea"));

		BoundsArea.BodyExited += OnBodyExited;

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
/*		for (int i = BitStatesDenseCount - 1; i >= 0; --i)
		{
			BitState state = (BitState)BitStatesDense[i];
			if (!state.HasExploded)
			{
				if (ForceTriggerArea.OverlapsBody(BitPool[state.Index]))
				{
					BitStatesDense[i].HasExploded = true;

					float magnitude = BitPool[state.Index].LinearVelocity.Y;
					float mass = BitPool[state.Index].Mass;

					foreach (Node2D areaNode in ForceArea.GetOverlappingBodies())
					{
						if (areaNode is RigidBody2D rb && rb.Mass < mass)
						{
							Vector2 force = Vector2.Up;
							force.Y *= (magnitude) * (mass * 2.0f);
							rb.ApplyImpulse(force);
						}

					}
				}
			}
		}*/

		Timer += (float)delta;
		if (Timer > BIT_TIMER)
		{
			Timer = 0;
			if (BitOrders.Count > 0 && BitOrderProcessNext(BitOrders[0]))
			{
				BitOrders.Remove(BitOrders[0]);
			}
		}
	}

	public void SpawnNode(BitTypes type)
	{
		int idx = CurrentIndex;
		CurrentIndex = (CurrentIndex + 1) % MAX_BITS;
/*		do
		{
			idx = CurrentIndex;
			CurrentIndex = (CurrentIndex + 1) % MAX_BITS;
		} while (BitStatesSparse[idx] != -1);*/

		BitPool[idx].Position = SpawnPosition;
		BitPool[idx].AngularVelocity = 0;
		BitPool[idx].Freeze = false;


		Sprite2D sprite = BitPool[idx].GetNode<Sprite2D>(new NodePath("./Sprite2D"));

		Debug.Assert(type != BitTypes.MaxBitTypes);

		switch (type)
		{
			case BitTypes.Bit1:
				{
					BitPool[idx].Mass = 1;
					BitPool[idx].LinearVelocity = Vector2.Zero;
					sprite.Texture = (Texture2D)Bit1Texture;
				} break;
			case BitTypes.Bit100:
				{
					BitPool[idx].Mass = 10;
					BitPool[idx].LinearVelocity = Vector2.Zero;
					sprite.Texture = (Texture2D)Bit100Texture;
				} break;
			case BitTypes.Bit1000:
				{
					BitPool[idx].Mass = 50;
					BitPool[idx].LinearVelocity = Vector2.Zero;
					sprite.Texture = (Texture2D)Bit1000Texture;
				}
				break;
			case BitTypes.Bit5000:
				{
					BitPool[idx].Mass = 250;
					BitPool[idx].LinearVelocity = Vector2.Zero;
					sprite.Texture = (Texture2D)Bit5000Texture;
				}
				break;
			case BitTypes.Bit10000:
				{
					BitPool[idx].Mass = 500;
					BitPool[idx].LinearVelocity = Vector2.Zero;
					sprite.Texture = (Texture2D)Bit10000Texture;
				}
				break;
			default:
				GD.PrintErr("Wrong type"); break;
		}

		sprite.Show();
		BitPool[idx].ProcessMode = ProcessModeEnum.Always;

		BitStatesSparse[idx] = BitStatesDenseCount;
		BitStatesDense[BitStatesDenseCount] = new BitState(idx);
		++BitStatesDenseCount;
	}

	public bool BitOrderProcessNext(BitOrder order)
	{
		bool isFinished = false;
		for (int i = 0; i < (int)BitTypes.MaxBitTypes; ++i)
		{
			if (order.BitAmounts[i] > 0)
			{
				--order.BitAmounts[i];
				SpawnNode((BitTypes)i);

				if (i == (int)BitTypes.MaxBitTypes - 1 && order.BitAmounts[i] == 0)
				{
					isFinished = true;
				}

				break;
			}
		}
		return isFinished;
	}

	public bool ProcessOrder(BitOrder bitOrder)
	{
		bool isFinished = false;
		for (int i = 0; i < (int)BitTypes.MaxBitTypes; ++i)
		{
			for (int bits = 0; bits < bitOrder.BitAmounts[i]; ++bits)
			{
				SpawnNode((BitTypes)i);
				isFinished = true;
			}
		}
		return isFinished;
	}

	public void AddOrderWithCheck(BitOrder order)
	{
#if DEBUG
		bool containedNoBits = true;
		for (int i = 0; i <  order.BitAmounts.Length; ++i)
		{
			if (order.BitAmounts[i] > 0)
			{
				containedNoBits = false;
			}
		}

		if (containedNoBits)
		{
			GD.PrintErr("Order contained no bits!");
			Debug.Assert(!containedNoBits);
		}
#endif

		if (BitOrders.Count == MAX_ORDERS)
		{
			GD.PrintErr("Bit orders capacity reached!");
			Debug.Assert(BitOrders.Count < MAX_ORDERS);
			return;
		}

		BitOrders.Add(order);
	}

	private void OnBodyExited(Node2D body)
	{
		if (body is RigidBody2D rb)
		{
			int id = -1;
			for (int i = 0; i < BitStatesDenseCount; ++i)
			{
				if (rb == BitPool[BitStatesDense[i].Index])
				{
					id = i;
					break;
				}
			}

			if (id == -1)
			{
				GD.PrintErr("Rigidbody not found in active bodies");
			}
			else
			{
				int idx = BitStatesDense[id].Index;
				BitState lastBucket = BitStatesDense[BitStatesDenseCount - 1];

				BitStatesSparse[idx] = -1;
				BitStatesSparse[lastBucket.Index] = id;

				BitStatesDense[id] = lastBucket;

				--BitStatesDenseCount;

				BitPool[idx].ProcessMode = ProcessModeEnum.Disabled;
				BitPool[idx].GetNode<Sprite2D>(new NodePath("./Sprite2D")).Hide();
			}
		}
	}
}
