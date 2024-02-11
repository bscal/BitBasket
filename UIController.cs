using BitCup;
using Godot;

public partial class UIController : Node
{

	private BitManager BitManager;

	private Label LabelStatus;

	private BoxContainer DebugUI;
	private Label LabelDebugMode;
	private Label LabelDebugVersion;
	private Label LabelDebugFPS;
	private Label LabelDebugFrameTime;
	private Label LabelDebugActiveBits;
	private Label LabelDebugOrdersQueued;
	private Label LabelDebugChannel;
	private Label LabelDebugBitsInArea;

	private LineEdit TextChannelName;
	private Button BtnConnectToChannel;

	private CheckBox CheckBoxAutoConnect;
	private CheckBox CheckBoxShowDebug;

	private Button BtnClearBits;
	private Button BtnRunTest;

	private const string UI_URL = "../Container/UI/";
	private const string DEBUG_URL = "../Container2/DebugUI/";

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		BitManager = GetNode<BitManager>(new NodePath("../BitManager"));
		Debug.Assert(BitManager != null);

		LabelStatus = GetNode<Label>(new NodePath(UI_URL + "PanelContainer/MarginContainer/LabelStatus"));
		Debug.Assert(LabelStatus != null);

		CheckBoxAutoConnect = GetNode<CheckBox>(new NodePath(UI_URL +  "Bools/CheckBoxAutoConnect"));
		Debug.Assert(CheckBoxAutoConnect != null);

		CheckBoxShowDebug = GetNode<CheckBox>(new NodePath(UI_URL + "Bools/CheckBoxShowDebug"));
		Debug.Assert(CheckBoxShowDebug != null);
		CheckBoxShowDebug.Pressed += () =>
		{
			DebugUI.Visible = !DebugUI.Visible;
		};

		DebugUI = GetNode<BoxContainer>(new NodePath(DEBUG_URL));
		Debug.Assert(DebugUI != null);
		DebugUI.Hide();

		LabelDebugMode = GetNode<Label>(new NodePath(DEBUG_URL + "LabelDebugMode"));
		Debug.Assert(LabelDebugMode != null);
#if DEBUG
		LabelDebugMode.Text = "Debug";
#else
		LabelDebugMode.Text = "Release";
#endif

		LabelDebugVersion = GetNode<Label>(new NodePath(DEBUG_URL + "LabelDebugVersion"));
		Debug.Assert(LabelDebugVersion != null);
		LabelDebugVersion.Text = string.Format("Version: {0}.{1}", BitManager.VersionMajor, BitManager.VersionMinor);

		LabelDebugFPS = GetNode<Label>(new NodePath(DEBUG_URL + "LabelDebugFPS"));
		Debug.Assert(LabelDebugFPS != null);

		LabelDebugFrameTime = GetNode<Label>(new NodePath(DEBUG_URL + "LabelDebugFrameTime"));
		Debug.Assert(LabelDebugFrameTime != null);

		LabelDebugActiveBits = GetNode<Label>(new NodePath(DEBUG_URL + "LabelDebugActiveBits"));
		Debug.Assert(LabelDebugActiveBits != null);

		LabelDebugOrdersQueued = GetNode<Label>(new NodePath(DEBUG_URL + "LabelDebugOrdersQueued"));
		Debug.Assert(LabelDebugOrdersQueued != null);

		LabelDebugChannel = GetNode<Label>(new NodePath(DEBUG_URL + "LabelDebugChannel"));
		Debug.Assert(LabelDebugChannel != null);

		LabelDebugBitsInArea = GetNode<Label>(new NodePath(DEBUG_URL + "/LabelDebugBitsInArea"));
		Debug.Assert(LabelDebugBitsInArea != null);

		TextChannelName = GetNode<LineEdit>(new NodePath(UI_URL + "TextChannelName"));
		Debug.Assert(TextChannelName != null);
		if (!string.IsNullOrEmpty(BitManager.Config.Username))
		{
			TextChannelName.Text = BitManager.Config.Username; 
		}

		BtnConnectToChannel = GetNode<Button>(new NodePath(UI_URL + "BtnConnectToChannel"));
		Debug.Assert(BtnConnectToChannel != null);
		BtnConnectToChannel.Pressed += () =>
		{
			UpdateValues();
			BitManager.TwitchManager.OAuthServerStart();
		};

		BtnClearBits = GetNode<Button>(new NodePath(UI_URL + "BtnClearBits"));
		Debug.Assert(BtnClearBits != null);
		BtnClearBits.Pressed += () =>
		{
			for (int i = 0; i < BitManager.BitStatesDenseCount; ++i)
			{
				int index = BitManager.BitStatesDense[i].Index;
				BitManager.BitStatesSparse[index] = -1;

				BitManager.HideBit(index);
			}
			BitManager.BitStatesDenseCount = 0;
		};

		BtnRunTest = GetNode<Button>(new NodePath(UI_URL + "BtnRunTest"));
		Debug.Assert(BtnRunTest != null);
		BtnRunTest.Pressed += BtnRunTestPressed;
	}

	private void UpdateValues()
	{
		BitManager.Config.Username = TextChannelName.Text;
		BitManager.Config.AutoConnect = CheckBoxAutoConnect.ActionMode == BaseButton.ActionModeEnum.Press;
	}

	private void BtnRunTestPressed()
	{
		BitOrder testOrder = new BitOrder();
		testOrder.BitAmounts[(int)BitTypes.Bit1] = 50;
		testOrder.BitAmounts[(int)BitTypes.Bit100] = 20;
		testOrder.BitAmounts[(int)BitTypes.Bit1000] = 10;
		testOrder.BitAmounts[(int)BitTypes.Bit5000] = 5;
		testOrder.BitAmounts[(int)BitTypes.Bit10000] = 2;

		BitManager.BitOrders.Add(testOrder);
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		if (BitManager.TwitchManager != null
			&& BitManager.TwitchManager.Client != null
			&& BitManager.TwitchManager.Client.IsConnected)
		{
			LabelStatus.Text = "Status: Connected";
			LabelStatus.AddThemeColorOverride("font_color", Colors.Green);
		}
		else
		{
			LabelStatus.Text = "Status: Not Connected";
			LabelStatus.AddThemeColorOverride("font_color", Colors.Red);
		}

		if (DebugUI.Visible)
		{
			LabelDebugFPS.Text = string.Format("FPS: {0}",
				Performance.Singleton.GetMonitor(Performance.Monitor.TimeFps));

			LabelDebugFrameTime.Text = string.Format("FrameTime: {0:0.00}ms",
				Performance.Singleton.GetMonitor(Performance.Monitor.TimeProcess) * 1000);

			LabelDebugActiveBits.Text = string.Format("ActiveBits: {0}", BitManager.BitStatesDenseCount);

			LabelDebugOrdersQueued.Text = string.Format("QueuedOrders: {0}", BitManager.BitOrders.Count);

			string connectedChannel =
				(BitManager.TwitchManager != null
				&& BitManager.TwitchManager.Client != null
				&& BitManager.TwitchManager.Client.IsConnected
				&& BitManager.TwitchManager.Client.JoinedChannels.Count > 0) 
					? BitManager.TwitchManager.Client.JoinedChannels[0].Channel
					: "None";
			LabelDebugChannel.Text = string.Format("Channel: {0}", connectedChannel);

			LabelDebugBitsInArea.Text = string.Format("BitsInCup: {0}", BitManager.BitCount);
		}
	}
}
