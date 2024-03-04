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
	public const int VersionMinor = 3;

	public const int VersionBitSerialization = 1;

	public static string VERSION_STRING = string.Format("{0}.{1}", VersionMajor, VersionMinor);

	public const int MAX_BITS = 256;
	public const int MAX_ORDERS = 64;

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

	public Area2D BoundsArea;
	public Area2D CupArea;

	public int CurrentIndex;
	public RigidBody2D[] BitPool = new RigidBody2D[MAX_BITS];
	public Sprite2D[] SpriteCache = new Sprite2D[MAX_BITS];

	public int[] BitStatesSparse = new int[MAX_BITS];

	public int BitStatesDenseCount;
	public BitState[] BitStatesDense = new BitState[MAX_BITS];

	public List<BitOrder> BitOrders = new List<BitOrder>(MAX_ORDERS);

	public TwitchManager TwitchManager;
	public State State;
	public User User;
	public Settings Settings;
	public Vector2 SpawnPosition;
	public bool IsUpdateAvailable;

	private float Timer;

	public override void _Ready()
	{
		Engine.MaxFps = 60;

		State = State.PreStart;

		TwitchManager = new TwitchManager(this);

		Config.Load(this);

		Settings.Reload();

		if (Settings.ShouldAutoConnect)
		{
			TwitchManager.ValidateThanFetchOrConnect(User);
		}

		Node2D spawnNode = GetNode<Node2D>("./SpawnPosition");
		if (spawnNode == null)
		{
			GD.PrintErr("No SpawnNode found under BitManager");
			return;
		}

		BoundsArea = GetNode<Area2D>(("../BoundsArea"));
		BoundsArea.BodyExited += BoundsArea_OnBodyExited;

		CupArea = GetNode<Area2D>("../CupArea");

		SpawnPosition = spawnNode.Position;

		// TODO add version check
		InitBitPool();
		if (Settings.ShouldSaveBits && LoadBitNodes())
		{
			GD.Print("GameState loaded successfully");
		}
		CheckForUpdates();
	}

	private void CheckForUpdates()
	{
		Debug.LogInfo("Checking for updates...");

		HttpRequest request = new HttpRequest();
		AddChild(request);
		request.Timeout = 1.0;
		request.RequestCompleted += (long result, long responseCode, string[] headers, byte[] body) =>
		{
			if (responseCode == 200)
			{
				Json json = new Json();
				json.Parse(body.GetStringFromUtf8());
				
				// Note: Response comes back in array form
				var response = json.Data.AsGodotArray()[0].AsGodotDictionary();
				
				if (response.TryGetValue("tag_name", out Variant tagName))
				{
					string[] split = ((string)tagName).Split('.');
					if (split.Length != 2)
						return;

					int major = int.Parse(split[0]);
					int minor = int.Parse(split[1]);
					if (VersionMajor < major || VersionMinor < minor)
					{
						Debug.LogInfo("Needs an update");
						IsUpdateAvailable = true;
					}
					else
					{
						Debug.LogInfo("Version up to date!");
					}
				}
			}
			else
			{
				Debug.LogErr("Bad response from CheckForUpdates {0}", responseCode);
			}
		};

		string url = "https://api.github.com/repos/bscal/BitBasket/releases";
		string[] headers = new string[]
		{
			"Accept: application/vnd.github+json",
			"X-GitHub-Api-Version: 2022-11-28",
		};
		Error err = request.Request(url, headers);
		if (err != Error.Ok)
		{
			GD.PushError(err);
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
					if (Timer > Settings.DropDelay)
					{
						Timer = 0;
						if (BitOrders.Count > 0 && BitOrderProcessNext(BitOrders[0]))
						{
							BitOrders.RemoveAt(0);
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
			Settings.Save();

			if (Settings.ShouldSaveBits)
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
		{
			return;
		}

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

		// Note: I am not sure how godot handle these transform updates
		// Sometimes I need to do Physics server sometimes set the position
		BitPool[idx].Position = spawnPos;

		PhysicsServer2D.BodySetState(
			BitPool[idx].GetRid(),
			PhysicsServer2D.BodyState.Transform,
			Transform2D.Identity.Translated(spawnPos));

		PhysicsServer2D.BodySetState(
			BitPool[idx].GetRid(),
			PhysicsServer2D.BodyState.LinearVelocity,
			new Vector2((GD.Randf() * 2.0f - 1.0f) * 10.0f, 0.0f));
	
		PhysicsServer2D.BodySetState(
			BitPool[idx].GetRid(),
			PhysicsServer2D.BodyState.AngularVelocity,
			(GD.Randf() * 2.0f - 1.0f) * 10.0f);

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
					BitPool[idx].Mass = Settings.Mass1;
					BitPool[idx].GetNode<CollisionPolygon2D>("./1BitCollision").Disabled = false;
					SpriteCache[idx].Texture = (Texture2D)Bit1Texture;
				}
				break;
			case BitTypes.Bit100:
				{
					BitPool[idx].Mass = Settings.Mass100;
					BitPool[idx].GetNode<CollisionPolygon2D>("./100BitCollision").Disabled = false;
					SpriteCache[idx].Texture = (Texture2D)Bit100Texture;
				}
				break;
			case BitTypes.Bit1000:
				{
					BitPool[idx].Mass = Settings.Mass1000;
					BitPool[idx].GetNode<CollisionPolygon2D>("./1000BitCollision").Disabled = false;
					SpriteCache[idx].Texture = (Texture2D)Bit1000Texture;
				}
				break;
			case BitTypes.Bit5000:
				{
					BitPool[idx].Mass = Settings.Mass5000;
					BitPool[idx].GetNode<CollisionPolygon2D>("./5000BitCollision").Disabled = false;
					SpriteCache[idx].Texture = (Texture2D)Bit5000Texture;
				}
				break;
			case BitTypes.Bit10000:
				{
					BitPool[idx].Mass = Settings.Mass10000;
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
		if (rb.Mass >= 1.1)
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
						switch (BitStatesDense[i].Type)
						{
							case BitTypes.Bit100:
								{
									force = Settings.Force100;
									break;
								}
							case BitTypes.Bit1000:
								{
									force = Settings.Force1000;
									break;
								}
							case BitTypes.Bit5000:
								{
									force = Settings.Force5000;
									break;
								}
							case BitTypes.Bit10000:
								{
									force = Settings.Force10000;
									break;
								}
							default:
								{
									force = 0;
									break;
								}
						}

						Vector2 velocityForce = Vector2.Up * (rb.LinearVelocity.Y * Settings.VelocityAmp);
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

						break;
					}
				}
			}
		}
	}

	private Godot.Collections.Dictionary<string, Variant> SerializeBit(int index, RigidBody2D node)
	{
		var dict = new Godot.Collections.Dictionary<string, Variant>();

		dict.Add("VersionBitSerialization", VersionBitSerialization);
		dict.Add("PosX", Mathf.Floor(node.Position.X));
		dict.Add("PosY", Mathf.Floor(node.Position.Y));
		dict.Add("IsActive", node.ProcessMode == ProcessModeEnum.Always);

		int denseIdx = BitStatesSparse[index];
		dict.Add("HasExploded", (denseIdx == -1) ? false : BitStatesDense[denseIdx].HasExploded);
		dict.Add("BitType", (denseIdx == -1) ? 0 : (int)BitStatesDense[denseIdx].Type);

		return dict;
	}

	private void SaveBitNodes()
	{
		string savePath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "gamestate.save");
		if (System.IO.File.Exists(savePath))
		{
			System.IO.File.Delete(savePath);
		}

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
		string savePath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "gamestate.save");

		if (!FileAccess.FileExists(savePath))
		{
			GD.PrintErr("Save file doesnt exist");
			return false; // Error! We don't have a save to load.
		}

		// Load the file line by line and process that dictionary to restore the object
		// it represents.
		using var saveGame = FileAccess.Open(savePath, FileAccess.ModeFlags.Read);

		// Basic check that file exists but is empty
		if (saveGame.GetLength() < MAX_BITS)
		{
			GD.Print("GameState save file does not meet size requirements");
			return false;
		}

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

			if (nodeData.TryGetValue("VersionBitSerialization", out Variant bitVersion)
				&& (int)bitVersion == VersionBitSerialization
				&& (bool)nodeData["IsActive"])
			{
				BitTypes type = (BitTypes)((int)nodeData["BitType"]);
				Vector2 pos = new Vector2((float)nodeData["PosX"], (float)nodeData["PosY"]);
				int denseIdx = SpawnNode(type, pos);
				BitStatesDense[denseIdx].HasExploded = (bool)nodeData["HasExploded"];
			}
		}
		return true;
	}
}
