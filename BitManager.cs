using BitCup;
using Godot;
using System;
using System.Collections.Generic;

public enum State
{
	PreStart,
	OAuth,
	Running
}

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

public struct User
{
	public string Username;
	public string OAuth;
	public string UserId;
}

public partial class BitManager : Node2D
{
	public const int VersionMajor = 0;
	public const int VersionMinor = 1;

	public const int MAX_BITS = 512;
	public const int BIT_TIMEOUT = 60 * 60 * 5 / 10;
	public const float BIT_TIMER = 0.25f;

	public const float BIT_1_MASS = 1.0f;
	public const float BIT_100_MASS = 4.0f;
	public const float BIT_1000_MASS = 8.0f;
	public const float BIT_5000_MASS = 16.0f;
	public const float BIT_10000_MASS = 32.0f;


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

	public int CurrentIndex;
	public RigidBody2D[] BitPool = new RigidBody2D[MAX_BITS];
	public Sprite2D[] SpriteCache = new Sprite2D[MAX_BITS];

	public int[] BitStatesSparse = new int[MAX_BITS];

	public int BitStatesDenseCount;
	public BitState[] BitStatesDense = new BitState[MAX_BITS];

	public List<BitOrder> BitOrders = new List<BitOrder>(32);

	public TwitchManager TwitchManager;
	public Config Config;
	public State State;
	public User User;
	public int BitCount;

	private Vector2 SpawnPosition;
	private float Timer;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		Engine.MaxFps = 60;

		State = State.PreStart;

		TwitchManager = new TwitchManager(this);

		Config = Config.Load();
		if (!string.IsNullOrEmpty(Config.Username) && Config.AutoConnect)
		{
			TwitchManager.OAuthServerStart();
		}

		Node2D spawnNode = GetNode<Node2D>(new NodePath("./SpawnPosition"));
		if (spawnNode == null)
		{
			GD.PrintErr("No SpawnNode found under BitManager");
			return;
		}

		ForceArea = GetNode<Area2D>(new NodePath("../ForceArea"));
		ForceArea.BodyEntered += ForceArea_OnBodyEnter;
		ForceArea.BodyExited += ForceArea_OnBodyExited;

		ForceTriggerArea = GetNode<Area2D>(new NodePath("../ForceTriggerArea"));
		BoundsArea = GetNode<Area2D>(new NodePath("../BoundsArea"));
		BoundsArea.BodyExited += BoundsArea_OnBodyExited;

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
			BitPool[i].Position = SpawnPosition;
			AddChild(BitPool[i]);

			SpriteCache[i] = BitPool[i].GetNode<Sprite2D>(new NodePath("./Sprite2D"));
			Debug.Assert(SpriteCache[i] != null);

			HideBit(i);

			BitStatesSparse[i] = -1;
		}

	}

	private void ForceArea_OnBodyEnter(Node2D body)
	{
		if (body is RigidBody2D rb)
		{
			++BitCount;

			if ((rb.Mass > BIT_100_MASS - 1 && BitCount >= 24)
				|| (rb.Mass > BIT_1000_MASS - 1 && BitCount >= 12)
				|| (rb.Mass > BIT_10000_MASS - 1 && BitCount >= 2))
			{
				float upForceAmp = 4.0f;
				float massAmp = 96.0f;
				Vector2 force = Vector2.Up * upForceAmp * new Vector2(1.0f, rb.Mass * massAmp);

				foreach (var bit in ForceTriggerArea.GetOverlappingBodies())
				{
					if (bit is RigidBody2D bitRB)
					{
						bitRB.ApplyImpulse(force);
					}
				}
			}
		}
	}

	private void ForceArea_OnBodyExited(Node2D body)
	{
		if (body is RigidBody2D rb)
		{
			--BitCount;
		}
	}

	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest || what == NotificationCrash)
		{
			if (TwitchManager.Client != null && TwitchManager.Client.IsConnected)
			{
				TwitchManager.Client.Disconnect();
			}
		}
	}


	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		switch (State)
		{
			case (State.PreStart):
				{
				} break;
			case (State.OAuth):
				{
					TwitchManager.OAuthServerUpdate();
				} break;
			case (State.Running):
				{
					Timer += (float)delta;
					if (Timer > BIT_TIMER)
					{
						Timer = 0;
						if (BitOrders.Count > 0 && BitOrderProcessNext(BitOrders[0]))
						{
							BitOrders.Remove(BitOrders[0]);
						}
					}
				} break;

			default: break;
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

		PhysicsServer2D.BodySetState(
			BitPool[idx].GetRid(),
			PhysicsServer2D.BodyState.Transform,
			Transform2D.Identity.Translated(SpawnPosition));

		PhysicsServer2D.BodySetState(
			BitPool[idx].GetRid(),
			PhysicsServer2D.BodyState.LinearVelocity,
			new Vector2(GD.Randf() * 2.0f - 1.0f, 0.0f));

		BitPool[idx].ProcessMode = ProcessModeEnum.Always;
		BitPool[idx].Freeze = false;

		BitCup.Debug.Assert(type != BitTypes.MaxBitTypes);

		switch (type)
		{
			case BitTypes.Bit1:
				{
					BitPool[idx].Mass = BIT_1_MASS;
					SpriteCache[idx].Texture = (Texture2D)Bit1Texture;
				} break;
			case BitTypes.Bit100:
				{
					BitPool[idx].Mass = BIT_100_MASS;
					SpriteCache[idx].Texture = (Texture2D)Bit100Texture;
				} break;
			case BitTypes.Bit1000:
				{
					BitPool[idx].Mass = BIT_1000_MASS;
					SpriteCache[idx].Texture = (Texture2D)Bit1000Texture;
				}
				break;
			case BitTypes.Bit5000:
				{
					BitPool[idx].Mass = BIT_5000_MASS;
					SpriteCache[idx].Texture = (Texture2D)Bit5000Texture;
				}
				break;
			case BitTypes.Bit10000:
				{
					BitPool[idx].Mass = BIT_10000_MASS;
					SpriteCache[idx].Texture = (Texture2D)Bit10000Texture;
				}
				break;
			default:
				GD.PrintErr("Wrong type"); break;
		}

		SpriteCache[idx].Show();

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

				// If last bit type to check and 0
				if (i == (int)BitTypes.MaxBitTypes - 1 && order.BitAmounts[i] == 0)
				{
					isFinished = true;
				}

				break;
			}
		}
		return isFinished;
	}

	public void CreateOrderWithChecks(int amount)
	{
		if (amount <= 0)
			return;

		BitOrder bitOrder = new BitOrder();
		bitOrder.BitAmounts[(int)BitTypes.Bit10000] = (byte)(amount / 10000);
		amount %= 10000;

		bitOrder.BitAmounts[(int)BitTypes.Bit5000] = (byte)(amount / 5000);
		amount %= 5000;

		bitOrder.BitAmounts[(int)BitTypes.Bit1000] = (byte)(amount / 1000);
		amount %= 1000;

		bitOrder.BitAmounts[(int)BitTypes.Bit100] = (byte)(amount / 100);
		amount %= 100;

		bitOrder.BitAmounts[(int)BitTypes.Bit1] = (byte)(amount);

		if (BitOrders.Count == BitOrders.Capacity)
		{
			GD.PrintErr("Bit orders capacity reached!");
			BitCup.Debug.Assert(BitOrders.Count < BitOrders.Capacity);
			return;
		}

		BitOrders.Add(bitOrder);
	}

	public void HideBit(int bitId)
	{
		BitPool[bitId].ProcessMode = ProcessModeEnum.Disabled;
		SpriteCache[bitId].Hide();
	}

	private void BoundsArea_OnBodyExited(Node2D body)
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

				HideBit(id);
			}
		}
	}
}
