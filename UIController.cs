using BitCup;
using Godot;
using System;
using System.Linq;
using System.Threading.Channels;

public partial class UIController : Node
{

	private BitManager BitManager;

	private Label LabelStatus;

	private CheckButton BtnShowDebug;

	private Button BtnRunTest;

	private BoxContainer DebugUI;
	private Label LabelDebugFPS;
	private Label LabelDebugFrameTime;
	private Label LabelDebugActiveBits;
	private Label LabelDebugOrdersQueued;

	private LineEdit TextChannelName;
	private Button BtnConnectToChannel;

	private CheckBox CheckBoxAutoConnect;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		BitManager = GetNode<BitManager>(new NodePath("../BitManager"));
		Debug.Assert(BitManager != null);

		LabelStatus = GetNode<Label>(new NodePath("../UI/LabelStatus"));

		BtnShowDebug = GetNode<CheckButton>(new NodePath("../UI/GridContainer/BtnShowDebug"));
		Debug.Assert(BtnShowDebug != null);
		BtnShowDebug.ButtonDown += BtnShowDebugPressed;

		BtnRunTest = GetNode<Button>(new NodePath("../UI/VBoxContainer/BtnRunTest"));
		Debug.Assert(BtnRunTest != null);
		BtnRunTest.Pressed += BtnRunTestPressed;


		DebugUI = GetNode<BoxContainer>(new NodePath("../DebugUI"));
		Debug.Assert(DebugUI != null);
		DebugUI.Hide();

		LabelDebugFPS = GetNode<Label>(new NodePath("../DebugUI/LabelDebugFPS"));
		Debug.Assert(LabelDebugFPS != null);

		LabelDebugFrameTime = GetNode<Label>(new NodePath("../DebugUI/LabelDebugFrameTime"));
		Debug.Assert(LabelDebugFrameTime != null);

		LabelDebugActiveBits = GetNode<Label>(new NodePath("../DebugUI/LabelDebugActiveBits"));
		Debug.Assert(LabelDebugActiveBits != null);

		LabelDebugOrdersQueued = GetNode<Label>(new NodePath("../DebugUI/LabelDebugOrdersQueued"));
		Debug.Assert(LabelDebugOrdersQueued != null);

		TextChannelName = GetNode<LineEdit>(new NodePath("../UI/TextChannelName"));
		Debug.Assert(TextChannelName != null);

		BtnConnectToChannel = GetNode<Button>(new NodePath("../UI/BtnConnectToChannel"));
		Debug.Assert(BtnConnectToChannel != null);
		BtnConnectToChannel.Pressed += () =>
		{
			UpdateValues();
			BitManager.TwitchManager.OAuthServerStart();
		};

		CheckBoxAutoConnect = GetNode<CheckBox>(new NodePath("../UI/GridContainer2/CheckBoxAutoConnect"));
		Debug.Assert(CheckBoxAutoConnect != null);
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

	private void BtnShowDebugPressed()
	{
		DebugUI.Visible = !DebugUI.Visible;
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		if (BitManager.TwitchManager != null
			&& BitManager.TwitchManager.Client != null
			&& BitManager.TwitchManager.Client.IsConnected)
		{
			BtnConnectToChannel.AddThemeColorOverride("font_color", Colors.DarkGreen);
		}
		else
		{
			BtnConnectToChannel.AddThemeColorOverride("font_color", Colors.DarkRed);
		}

		if (BitManager.State == State.Running)
		{
			for (int i = 0; i < BitManager.TwitchManager.Client.JoinedChannels.Count; ++i)
			{
				if (BitManager.TwitchManager.Client.JoinedChannels[i].Channel == BitManager.Config.Username)
				{
					BtnConnectToChannel.AddThemeColorOverride("font_color", Colors.DarkGreen);
				}
				else
				{
					BtnConnectToChannel.AddThemeColorOverride("background_color", Colors.DarkRed);
				}
			}

			if (DebugUI.Visible)
			{
				LabelDebugFPS.Text = string.Format("FPS: {0}",
					Performance.Singleton.GetMonitor(Performance.Monitor.TimeFps));

				LabelDebugFrameTime.Text = string.Format("FrameTime: {0:0.00}ms",
					Performance.Singleton.GetMonitor(Performance.Monitor.TimeProcess) * 1000);

				LabelDebugActiveBits.Text = string.Format("ActiveBits: {0}", BitManager.BitStatesDenseCount);

				LabelDebugOrdersQueued.Text = string.Format("QueuedOrders: {0}", BitManager.BitOrders.Count);
			}
		}
	}
}
