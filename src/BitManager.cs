using BitCup;
using Godot;
using System;
using System.Collections;
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

	public BitOrder() { }
}

public struct BitState
{
	public int Index;
	public bool HasExploded;
	public BitTypes Type;

	public BitState(int index, BitTypes type)
	{
		Index = index;
		HasExploded = false;
		Type = type;
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
	public const int VersionMinor = 2;

	public const int MAX_BITS = 256;
	public const int BIT_TIMEOUT = 60 * 60 * 5 / 10;
	public const float BIT_TIMER = 0.25f;

	public const float BIT_1_MASS = 1.0f;
	public const float BIT_100_MASS = 4.0f;
	public const float BIT_1000_MASS = 6.0f;
	public const float BIT_5000_MASS = 8.0f;
	public const float BIT_10000_MASS = 10.0f;

	//public const int NUM_OF_EXPLOSION_ZONES = 5;

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

	//public AudioStreamPlayer2D AudioPlayer;
	public Area2D BoundsArea;
	public Area2D CupArea;

	public int CurrentIndex;
	public RigidBody2D[] BitPool = new RigidBody2D[MAX_BITS];
	public Sprite2D[] SpriteCache = new Sprite2D[MAX_BITS];

	public int[] BitStatesSparse = new int[MAX_BITS];

	public int BitStatesDenseCount;
	public BitState[] BitStatesDense = new BitState[MAX_BITS];

	public List<BitOrder> BitOrders = new List<BitOrder>(32);

	public TwitchManager TwitchManager;
	public State State;
	public User User;
	public bool ShouldAutoConnect;
	public bool ShouldSaveBits;
	public int BitCount;
	public Vector2 SpawnPosition;

	private float Timer;

	public override void _Ready()
	{
		Engine.MaxFps = 60;

		State = State.PreStart;

		TwitchManager = new TwitchManager(this);

		Config.Load(this);

		if (ShouldAutoConnect)
		{
			TwitchManager.ValidateThanFetchOrConnect(User);
		}

		Node2D spawnNode = GetNode<Node2D>(new NodePath("./SpawnPosition"));
		if (spawnNode == null)
		{
			GD.PrintErr("No SpawnNode found under BitManager");
			return;
		}

		//AudioPlayer = GetNode<AudioStreamPlayer2D>("./AudioStreamPlayer2D");

		BoundsArea = GetNode<Area2D>(("../BoundsArea"));
		BoundsArea.BodyExited += BoundsArea_OnBodyExited;

		CupArea = GetNode<Area2D>("../CupArea");

		SpawnPosition = spawnNode.Position;

		// TODO add version check
		if (LoadBitNodes())
		{
			GD.Print("GameState loaded");
		}
		else
		{
			InitBitPool();
		}
	}

	public void InitBitPool()
	{
		PackedScene bitScene = GD.Load<PackedScene>("res://scenes/bit.tscn");
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
		BitStatesDenseCount = 0;
	}

	public override void _Process(double delta)
	{
		switch (State)
		{
			case (State.PreStart):
				{
				}
				break;
			case (State.OAuth):
				{
					TwitchManager.OAuthServerUpdate();
				}
				break;
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
				}
				break;

			default: break;
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

			Config.Save(this);

			if (ShouldSaveBits)
			{
				SaveBitNodes();
			}
		}
	}

	public void HideBit(int bitId)
	{
		BitPool[bitId].ProcessMode = ProcessModeEnum.Disabled;
		SpriteCache[bitId].Hide();
		BitPool[bitId].Position = SpawnPosition;
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

	private int SpawnNode(BitTypes type, Vector2 spawnPos)
	{
		int idx;
		do
		{
			idx = CurrentIndex;
			CurrentIndex = (CurrentIndex + 1) % MAX_BITS;
		} while (BitStatesSparse[idx] != -1);

		//AudioPlayer.Play();

		BitPool[idx].Position = spawnPos;

		PhysicsServer2D.BodySetState(
			BitPool[idx].GetRid(),
			PhysicsServer2D.BodyState.Transform,
			Transform2D.Identity.Translated(spawnPos));

		PhysicsServer2D.BodySetState(
			BitPool[idx].GetRid(),
			PhysicsServer2D.BodyState.LinearVelocity,
			new Vector2(GD.Randf() * 2.0f - 1.0f, 0.0f));

		BitPool[idx].ProcessMode = ProcessModeEnum.Always;
		BitPool[idx].Freeze = false;

		BitCup.Debug.Assert(type != BitTypes.MaxBitTypes);

		BitPool[idx].GetNode<CollisionPolygon2D>("./1BitCollision").Disabled = true;
		BitPool[idx].GetNode<CollisionPolygon2D>("./100BitCollision").Disabled = true;
		BitPool[idx].GetNode<CollisionPolygon2D>("./1000BitCollision").Disabled = true;
		BitPool[idx].GetNode<CollisionPolygon2D>("./5000BitCollision").Disabled = true;
		BitPool[idx].GetNode<CollisionPolygon2D>("./10000BitCollision").Disabled = true;

		switch (type)
		{
			case BitTypes.Bit1:
				{
					BitPool[idx].Mass = BIT_1_MASS;
					BitPool[idx].GetNode<CollisionPolygon2D>("./1BitCollision").Disabled = false;
					SpriteCache[idx].Texture = (Texture2D)Bit1Texture;
				}
				break;
			case BitTypes.Bit100:
				{
					BitPool[idx].Mass = BIT_100_MASS;
					BitPool[idx].GetNode<CollisionPolygon2D>("./100BitCollision").Disabled = false;
					SpriteCache[idx].Texture = (Texture2D)Bit100Texture;
				}
				break;
			case BitTypes.Bit1000:
				{
					BitPool[idx].Mass = BIT_1000_MASS;
					BitPool[idx].GetNode<CollisionPolygon2D>("./1000BitCollision").Disabled = false;
					SpriteCache[idx].Texture = (Texture2D)Bit1000Texture;
				}
				break;
			case BitTypes.Bit5000:
				{
					BitPool[idx].Mass = BIT_5000_MASS;
					BitPool[idx].GetNode<CollisionPolygon2D>("./5000BitCollision").Disabled = false;
					SpriteCache[idx].Texture = (Texture2D)Bit5000Texture;
				}
				break;
			case BitTypes.Bit10000:
				{
					BitPool[idx].Mass = BIT_10000_MASS;
					BitPool[idx].GetNode<CollisionPolygon2D>("./10000BitCollision").Disabled = false;
					SpriteCache[idx].Texture = (Texture2D)Bit10000Texture;
				}
				break;
			default:
				GD.PrintErr("Wrong type"); break;
		}

		SpriteCache[idx].Show();

		BitStatesSparse[idx] = BitStatesDenseCount;
		BitStatesDense[BitStatesDenseCount] = new BitState(idx, type);
		++BitStatesDenseCount;

		return BitStatesSparse[idx];
	}

	private bool BitOrderProcessNext(BitOrder order)
	{
		bool isFinished = false;
		for (int i = 0; i < (int)BitTypes.MaxBitTypes; ++i)
		{
			if (order.BitAmounts[i] > 0)
			{
				--order.BitAmounts[i];
				SpawnNode((BitTypes)i, SpawnPosition);

				// If last bit type to check and 0
				if (i == (int)BitTypes.MaxBitTypes - 1 && order.BitAmounts[i] == 0)
				{
					isFinished = true;
				}

				// Only do 1 bit per update
				break;
			}
			// This makes sure we dont hang on orders if last
			// amount is not > 0
			else if (i == (int)BitTypes.MaxBitTypes - 1)
			{
				isFinished = true;
				break;
			}
		}
		return isFinished;
	}

	private void BoundsArea_OnBodyExited(Node2D body)
	{
		if (body is RigidBody2D rb)
		{
			int denseIdx = -1;
			for (int i = 0; i < BitStatesDenseCount; ++i)
			{
				if (rb.GetInstanceId() == BitPool[BitStatesDense[i].Index].GetInstanceId())
				{
					denseIdx = i;
					break;
				}
			}

			if (denseIdx == -1)
			{
				GD.PrintErr("Rigidbody not found in active bodies");
			}
			else
			{
				int idx = BitStatesDense[denseIdx].Index;
				BitState lastBucket = BitStatesDense[BitStatesDenseCount - 1];

				BitStatesSparse[idx] = -1;
				BitStatesSparse[lastBucket.Index] = denseIdx;

				BitStatesDense[denseIdx] = lastBucket;

				--BitStatesDenseCount;

				HideBit(idx);
			}
		}
	}

	internal void Explode(RigidBody2D rb)
	{
		if (rb.Mass >= BIT_100_MASS - .1)
		{
			for (int i = 0; i < BitStatesDenseCount; ++i)
			{
				int idx = BitStatesDense[i].Index;
				if (BitPool[idx].GetInstanceId() == rb.GetInstanceId())
				{
					if (!BitStatesDense[i].HasExploded)
					{
						BitStatesDense[i].HasExploded = true;

						float force;
						if (rb.Mass <= BIT_100_MASS + 1)
							force = 300;
						else if (rb.Mass <= BIT_1000_MASS + 1)
							force = 5500;
						else if (rb.Mass <= BIT_5000_MASS + 1)
							force = 10000;
						else
							force = 20000;

						Vector2 velocityForce = Vector2.Up * (rb.LinearVelocity.Y * 2.0f);
						Vector2 impulse = Vector2.Up * force + velocityForce;

						foreach (var bit in CupArea.GetOverlappingBodies())
						{
							if (bit is RigidBody2D bitRB
								&& bit.GetInstanceId() != rb.GetInstanceId()
								&& bitRB.LinearVelocity.Length() < 80)
							{
								bitRB.ApplyImpulse(impulse);
							}
						}

						rb.ApplyImpulse(Vector2.Down * 100);

						break;
					}
				}
			}
		}
	}

	private Godot.Collections.Dictionary<string, Variant> SerializeBit(int index, RigidBody2D node)
	{
		var dict = new Godot.Collections.Dictionary<string, Variant>();

		dict.Add("MajorVer", VersionMajor);
		dict.Add("MinorVer", VersionMinor);
		dict.Add("PosX", Mathf.Floor(node.Position.X));
		dict.Add("PosY", Mathf.Floor(node.Position.Y));
		dict.Add("IsActive", node.ProcessMode == ProcessModeEnum.Always);

		int denseIdx = BitStatesSparse[index];
		dict.Add("HasExploded", (denseIdx == -1) ? false : BitStatesDense[denseIdx].HasExploded);
		dict.Add("BitType", (denseIdx == -1) ? 0 : (int)BitStatesDense[denseIdx].Type);

		return dict;
	}

	private void RemoveSaveFile()
	{
		string savePath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "gamestate.save");
		if (System.IO.File.Exists(savePath))
		{
			System.IO.File.Delete(savePath);
		}
	}

	private void SaveBitNodes()
	{
		RemoveSaveFile();

		string savePath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "gamestate.save");

		using var save = FileAccess.Open(savePath, FileAccess.ModeFlags.Write);
		for (int i = 0; i < MAX_BITS; ++i)
		{
			RigidBody2D saveNode = BitPool[i];

			// Check the node is an instanced scene so it can be instanced again during load.
			if (string.IsNullOrEmpty(saveNode.SceneFilePath))
			{
				GD.Print($"persistent node '{saveNode.Name}' is not an instanced scene, skipped");
				continue;
			}

			// Call the node's save function.
			var nodeData = SerializeBit(i, saveNode);

			// Json provides a static method to serialized JSON string.
			string jsonString = Json.Stringify(nodeData);

			// Store the save dictionary as a new line in the save file.
			save.StoreLine(jsonString);
		}
	}

	private bool LoadBitNodes()
	{
		InitBitPool();

		string savePath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "gamestate.save");

		if (!FileAccess.FileExists(savePath))
		{
			GD.PrintErr("Save file doesnt exist");
			return false; // Error! We don't have a save to load.
		}

		// We need to revert the game state so we're not cloning objects during loading.
		// This will vary wildly depending on the needs of a project, so take care with
		// this step.
		// For our example, we will accomplish this by deleting saveable objects.
		//var saveNodes = GetTree().GetNodesInGroup("Persistent");
		//foreach (Node saveNode in saveNodes)
		//{
		//	saveNode.QueueFree();
		//}

		// Load the file line by line and process that dictionary to restore the object
		// it represents.
		using var saveGame = FileAccess.Open(savePath, FileAccess.ModeFlags.Read);

		if (saveGame.GetLength() < MAX_BITS)
		{
			GD.Print("GameState save file does not meet size requirements");
			return false;
		}

		int lineCount = 0;
		while (saveGame.GetPosition() < saveGame.GetLength())
		{
			var jsonString = saveGame.GetLine();

			// Creates the helper class to interact with JSON
			var json = new Json();
			var parseResult = json.Parse(jsonString);
			if (parseResult != Error.Ok)
			{
				GD.Print($"JSON Parse Error: {json.GetErrorMessage()} in {jsonString} at line {json.GetErrorLine()}");
				continue;
			}

			// Get the data from the JSON object
			var nodeData = new Godot.Collections.Dictionary<string, Variant>((Godot.Collections.Dictionary)json.Data);

			if ((int)nodeData["MajorVer"] != VersionMajor
				|| (int)nodeData["MinorVer"] != VersionMinor)
			{
				GD.PrintErr(string.Format("Version mismatch. Saved: {0}.{1}, Expected: {2}.{3}",
					(int)nodeData["MajorVer"], (int)nodeData["MinorVer"], VersionMajor, VersionMinor));

				return false;
			}

			if ((bool)nodeData["IsActive"])
			{
				BitTypes type = (BitTypes)((int)nodeData["BitType"]);
				Vector2 pos = new Vector2((float)nodeData["PosX"], (float)nodeData["PosY"]);
				int denseIdx = SpawnNode(type, pos);

				BitStatesDense[denseIdx].HasExploded = (bool)nodeData["HasExploded"];
			}

			++lineCount;
		}
		return true;
	}
}
