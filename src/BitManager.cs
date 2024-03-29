using BitCup;
using Godot;
using System.Collections.Generic;

public enum State
{
	PreStart,
	OAuth,
	WaitValidating,
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

public enum OrderType
{
	Bits,
	Rain,
}

public class BitOrder
{
	public OrderType Type = OrderType.Bits;
	public short[] BitAmounts = new short[(int)BitTypes.MaxBitTypes];
	public ImageTexture[] Texture = new ImageTexture[(int)BitTypes.MaxBitTypes];
	public string[] TextureId = new string[(int)BitTypes.MaxBitTypes];
}

public struct BitState
{
	public int Index;
	public short BitPower;
	public bool HasExploded;
	public BitTypes Type;
	public string TextureId;

	public BitState(int index, short bitPower, BitTypes type, string textureId)
	{
		Index = index;
		BitPower = bitPower;
		HasExploded = false;
		Type = type;
		TextureId = textureId;
	}
}

public struct User
{
	public string Username;
	public string OAuth;
	public string BroadcasterId;
}

public partial class BitManager : Node2D
{
	public const int VersionMajor = 0;
	public const int VersionMinor = 6;
	public static string VERSION_STRING = string.Format("{0}.{1}", VersionMajor, VersionMinor);

	public const int MAX_BITS = 512;

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

	public List<BitOrder> BitOrders = new List<BitOrder>(128);

	public TwitchManager TwitchManager;
	public TwitchAPI TwitchAPI;
	public EventSub EventSub;
	public State State;
	public User User;
	public Settings Settings;
	public Vector2 SpawnPosition;
	public bool IsUpdateAvailable;
	public bool IsDebugEnabled;

	private float Timer;

	const int VersionBitSerialization = 4;
	const string KEY_SERIAL_VERSION = "VersionBitSerialization";
	const string KEY_TEXTURE = "Texture";
	const string KEY_TYPE = "BitType";
	const string KEY_POS_X = "PosX";
	const string KEY_POS_Y = "PosY";
	const string KEY_BIT_POWER = "BitPower";
	const string KEY_HAS_EXPLODED = "HasExploded";
	const string KEY_IS_ACTIVE = "IsActive";

	public void InvalidateTwitchState()
	{
		ResetAllState();
		TwitchAPI.OAuthState = TwitchOAuthState.Invalid;
	}

	public void ResetAllState()
	{
		TwitchAPI.OAuthState = TwitchOAuthState.Unknown;
		State = State.PreStart;

		Config.Load(this);

		TwitchAPI.ValidateOAuth();
	}

	public override void _Ready()
	{
		Engine.MaxFps = 60;

		//string logFileDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "logs");
		//string logFilePath = System.IO.Path.Combine(logFileDir, "bitbucket.log");
		//if (!System.IO.Directory.Exists(logFileDir))
		//{
		//	System.IO.Directory.CreateDirectory(logFileDir);
		//}
		//ProjectSettings.SetSetting("debug/file_logging/log_path", logFilePath);
		//ProjectSettings.SetSetting("debug/file_logging/enable_file_logging", true);
		//ProjectSettings.SetSetting("debug/file_logging/enable_file_logging.pc", true);

		State = State.PreStart;

		Config.Load(this);

		Settings.Reload();

		TwitchManager = new TwitchManager(this);
		TwitchManager.LoadImages();

		EventSub = new EventSub(this);

		TwitchAPI = new TwitchAPI(this);
		TwitchAPI.ValidateOAuth();

		Node2D spawnNode = GetNode<Node2D>("./SpawnPosition");
		if (spawnNode == null)
		{
			Debug.Error("No SpawnNode found under BitManager");
			return;
		}
		SpawnPosition = spawnNode.Position;

		BoundsArea = GetNode<Area2D>(("../BoundsArea"));
		BoundsArea.BodyExited += BoundsArea_OnBodyExited;

		CupArea = GetNode<Area2D>("../CupArea");

		InitBitPool();
		if (Settings.ShouldSaveBits && LoadBitNodes())
		{
			Debug.LogInfo("GameState loaded successfully");
		}
		CheckForUpdates();

		Debug.LogInfo("BitManager ready!");
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
			AddChild(BitPool[i]);
			BitPool[i].Position = SpawnPosition;
			BitPool[i].SetMeta("id", i);

			SpriteCache[i] = BitPool[i].GetNode<Sprite2D>(new NodePath("./Sprite2D"));
			Debug.Assert(SpriteCache[i] != null);

			HideBit(i);

			BitStatesSparse[i] = -1;
		}
		BitStatesDenseCount = 0;
	}

	private void ClientFullyReady()
	{
		Debug.LogInfo("(ClientFullyReady)");
		Debug.LogInfo("Username = " + User.Username);
		Debug.LogInfo("BroadcasterId = " + User.BroadcasterId);
		Debug.LogInfo("Valid OAuth = " + TwitchAPI.IsOAuthValid());
		Debug.LogInfo("TwitchClient = " + TwitchManager.Client.IsConnected);
		Debug.LogInfo(" ");

		if (string.IsNullOrEmpty(User.Username)
			|| string.IsNullOrEmpty(User.OAuth)
			|| string.IsNullOrEmpty(User.BroadcasterId))
		{
			Debug.Error("(ERROR) ClientFullyReady User");
			State = State.PreStart;
			return;
		}

		State = State.Running;

		TwitchAPI.RequestGetRewards();

#if DEBUG
		// Tests
		EventSub.TestHypeTrain();
#endif
	}

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
			case (State.WaitValidating):
				{
					if (TwitchManager.Client != null
						&& TwitchManager.Client.IsConnected
						&& !string.IsNullOrEmpty(User.BroadcasterId))
					{
						ClientFullyReady();
					}
				} break;
			case (State.Running):
				{
					TwitchManager.ProcessQueues((float)delta);
					TwitchAPI.UpdateEventServer((float)delta);
					EventSub.UpdateEvents((float)delta);

					if (BitOrderProcessNext((float)delta))
					{
						BitOrders.RemoveAt(0);
					}
				}
				break;

			default: break;
		}

		if (Input.IsActionJustPressed("show_debug"))
		{
			IsDebugEnabled = !IsDebugEnabled;
		}

		if (DisplayServer.WindowGetMode(0) == DisplayServer.WindowMode.Minimized)
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed, 0);
		}
	}

	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest || what == NotificationCrash)
		{
			TwitchManager.SaveImages();

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

	private BitOrder CreateBitOrder(int amount)
	{
		BitOrder res = new();

		res.BitAmounts[(int)BitTypes.Bit10000] = (short)(amount / 10000);
		amount %= 10000;

		res.BitAmounts[(int)BitTypes.Bit5000] = (short)(amount / 5000);
		amount %= 5000;

		res.BitAmounts[(int)BitTypes.Bit1000] = (short)(amount / 1000);
		amount %= 1000;

		res.BitAmounts[(int)BitTypes.Bit100] = (short)(amount / 100);
		amount %= 100;

		res.BitAmounts[(int)BitTypes.Bit1] = (short)(amount);

		res.Texture[(int)BitTypes.Bit10000] = TwitchManager.TextureCache[TwitchManager.DEFAULT_10000];
		res.Texture[(int)BitTypes.Bit5000] = TwitchManager.TextureCache[TwitchManager.DEFAULT_5000];
		res.Texture[(int)BitTypes.Bit1000] = TwitchManager.TextureCache[TwitchManager.DEFAULT_1000];
		res.Texture[(int)BitTypes.Bit100] = TwitchManager.TextureCache[TwitchManager.DEFAULT_100];
		res.Texture[(int)BitTypes.Bit1] = TwitchManager.TextureCache[TwitchManager.DEFAULT_1];

		return res;
	}

	public void CreateOrderWithCustomTexture(int amount, string textureId, ImageTexture texture)
	{
		amount = Mathf.Clamp(amount, 1, short.MaxValue);

		BitOrder bitOrder = CreateBitOrder(amount);

		// Loop backwards to find largest bit
		for (int i = (int)BitTypes.MaxBitTypes - 1; i >= 0; --i)
		{
			if (bitOrder.BitAmounts[i] > 0) 
			{
				bitOrder.Texture[i] = texture;
				bitOrder.TextureId[i] = textureId;
				break;
			}
		}

		BitOrders.Add(bitOrder);
	}

	public void CreateOrderWithChecks(int amount)
	{
		amount = Mathf.Clamp(amount, 1, short.MaxValue);

		BitOrder bitOrder = CreateBitOrder(amount);

		BitOrders.Add(bitOrder);
	}

	public void CreateRainOrder(int level)
	{
		level = Mathf.Clamp(level, 1, 5);

		BitOrder order = new BitOrder();
		order.Type = OrderType.Rain;

		order.BitAmounts[(int)BitTypes.Bit1] = 50;

		switch (level)
		{
			case 1: order.BitAmounts[(int)BitTypes.Bit1] += 25; break;
			case 2: order.BitAmounts[(int)BitTypes.Bit1] += 100; break;
			case 3: order.BitAmounts[(int)BitTypes.Bit100] = 150; break;
			case 4: order.BitAmounts[(int)BitTypes.Bit1000] = 150; break;
			case 5:
				{
					order.BitAmounts[(int)BitTypes.Bit5000] = 150;
					order.BitAmounts[(int)BitTypes.Bit10000] = 1;
				} break;
		}
		BitOrders.Add(order);
	}

	public int SpawnNode(BitTypes type, Vector2 spawnPos, short bitPower, string textureId, ImageTexture texture)
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

		if (BitOrders.Count > 0)
		{
			float rotation;
			if (BitOrders[0].Type == OrderType.Rain)
				rotation = (GD.Randf() * 2.0f - 1.0f) * 32.0f;
			else
				rotation = (GD.Randf() * 2.0f - 1.0f) * 10.0f;

			PhysicsServer2D.BodySetState(
				BitPool[idx].GetRid(),
				PhysicsServer2D.BodyState.AngularVelocity,
				rotation);
		}

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
					//SpriteCache[idx].Texture = (Texture2D)Bit1Texture;
				}
				break;
			case BitTypes.Bit100:
				{
					BitPool[idx].Mass = Settings.Mass100;
					BitPool[idx].GetNode<CollisionPolygon2D>("./100BitCollision").Disabled = false;
					//SpriteCache[idx].Texture = (Texture2D)Bit100Texture;
				}
				break;
			case BitTypes.Bit1000:
				{
					BitPool[idx].Mass = Settings.Mass1000;
					BitPool[idx].GetNode<CollisionPolygon2D>("./1000BitCollision").Disabled = false;
					//SpriteCache[idx].Texture = (Texture2D)Bit1000Texture;
				}
				break;
			case BitTypes.Bit5000:
				{
					BitPool[idx].Mass = Settings.Mass5000;
					BitPool[idx].GetNode<CollisionPolygon2D>("./5000BitCollision").Disabled = false;
					//SpriteCache[idx].Texture = (Texture2D)Bit5000Texture;
				}
				break;
			case BitTypes.Bit10000:
				{
					BitPool[idx].Mass = Settings.Mass10000;
					BitPool[idx].GetNode<CollisionPolygon2D>("./10000BitCollision").Disabled = false;
					//SpriteCache[idx].Texture = (Texture2D)Bit10000Texture;
				}
				break;
			default:
				GD.PrintErr("Wrong type"); break;
		}

		SpriteCache[idx].Texture = texture;
		SpriteCache[idx].Show();

		BitStatesSparse[idx] = BitStatesDenseCount;
		BitStatesDense[BitStatesDenseCount] = new BitState(idx, bitPower, type, textureId);
		++BitStatesDenseCount;

		return BitStatesSparse[idx];
	}

	private bool BitOrderProcessNext(float dt)
	{
		if (BitOrders.Count == 0)
			return false;

		bool isFinished = false;
		BitOrder order = BitOrders[0];

		float delay;
		if (order.Type == OrderType.Bits)
			delay = Settings.DropDelay + 0.01f;
		else
			delay = 0.05f;

		Timer += dt;
		if (Timer < delay)
			return false;

		Timer = 0;

		for (int i = 0; i < (int)BitTypes.MaxBitTypes; ++i)
		{
			if (order.BitAmounts[i] > 0)
			{
				short bitPower;
				if (Settings.CombineBits && i != (int)BitTypes.Bit1 && order.Type == OrderType.Bits)
				{
					bitPower = order.BitAmounts[i];
					order.BitAmounts[i] = 0;
				}
				else
				{
					bitPower = 1;
					--order.BitAmounts[i];
				}

				if (order.BitAmounts[i] <= 0)
					Timer = -2;

				SpawnNode((BitTypes)i, SpawnPosition, bitPower, order.TextureId[i], order.Texture[i]);

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

		if (isFinished)
			Timer = -3;

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
		if (rb.HasMeta("id"))
		{
			int id = rb.GetMeta("id").AsInt32();
			int idx = BitStatesSparse[id];
			BitState state = BitStatesDense[idx];

			if ((int)state.Type > (int)BitTypes.Bit1
				&& (!state.HasExploded
				|| rb.LinearVelocity.Y > 1024))
			{
				BitStatesDense[idx].HasExploded = true;

				float force;
				switch (state.Type)
				{
					case BitTypes.Bit100:
						{
							force = 500 + Settings.Force100;
							break;
						}
					case BitTypes.Bit1000:
						{
							force = 1000 + Settings.Force1000;
							break;
						}
					case BitTypes.Bit5000:
						{
							force = 1450 + Settings.Force5000;
							break;
						}
					case BitTypes.Bit10000:
						{
							force = 2500 + Settings.Force10000;
							break;
						}
					default:
						{
							force = 0;
							break;
						}
				}

				if (state.BitPower > 1)
				{
					// Note:	cheer900 is around 1.9 power max
					//			cheer4000 is 1.4 power max
					// cheer5000 cannot increase.
					// cheer10000 doesn't scale well.
					force += Mathf.Lerp(0, force, (float)(state.BitPower) / 10);
				}

				Vector2 velocityForce = Vector2.Up * (rb.LinearVelocity.Y * Settings.VelocityAmp);
				Vector2 impulse = Vector2.Up * force + velocityForce;

				foreach (var bit in CupArea.GetOverlappingBodies())
				{
					if (bit is RigidBody2D bitRB
						&& bit.GetInstanceId() != rb.GetInstanceId()
						&& bitRB.LinearVelocity.Length() < 128)
					{
						bitRB.ApplyImpulse(impulse);
					}
				}

				rb.ApplyImpulse(Vector2.Down * 300.0f);
			}
		}
	}

	private Godot.Collections.Dictionary<string, Variant> SerializeBit(int index, RigidBody2D node)
	{
		var dict = new Godot.Collections.Dictionary<string, Variant>();

		dict.Add(KEY_SERIAL_VERSION, VersionBitSerialization);
		dict.Add(KEY_POS_X, Mathf.Floor(node.Position.X));
		dict.Add(KEY_POS_Y, Mathf.Floor(node.Position.Y));
		dict.Add(KEY_IS_ACTIVE, node.ProcessMode == ProcessModeEnum.Always);

		int denseIdx = BitStatesSparse[index];
		dict.Add(KEY_HAS_EXPLODED, (denseIdx == -1) ? false : BitStatesDense[denseIdx].HasExploded);
		dict.Add(KEY_TYPE, (denseIdx == -1) ? 0 : (int)BitStatesDense[denseIdx].Type);
		dict.Add(KEY_TEXTURE, (denseIdx == -1) ? string.Empty : BitStatesDense[denseIdx].TextureId);

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

			if (nodeData.TryGetValue(KEY_SERIAL_VERSION, out Variant bitVersion)
				&& (int)bitVersion == VersionBitSerialization
				&& (bool)nodeData[KEY_IS_ACTIVE])
			{
				string textureId = (string)nodeData.GetValueOrDefault(KEY_TEXTURE, string.Empty);
				BitTypes type = (BitTypes)(int)nodeData[KEY_TYPE];
				Vector2 pos = new Vector2((float)nodeData[KEY_POS_X], (float)nodeData[KEY_POS_Y]);
				short bitPower = (short)nodeData.GetValueOrDefault(KEY_BIT_POWER, 1);
				bool hasExploded = (bool)nodeData[KEY_HAS_EXPLODED];

				ImageTexture texture = TwitchManager.GetTextureOrDefault(textureId, type);

				int denseIdx = SpawnNode(type, pos, bitPower, textureId, texture);
				BitStatesDense[denseIdx].HasExploded = hasExploded;
			}
		}
		return true;
	}
}
