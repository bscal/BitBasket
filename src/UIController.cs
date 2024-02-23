using BitCup;
using Godot;
using System.Runtime.CompilerServices;

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
	private Button ButtonDebugReload;

	private LineEdit TextChannelName;
	private Button BtnConnectToChannel;

	private CheckBox CheckBoxAutoConnect;
	private CheckBox CheckBoxCheckBoxSaveBits;
	private CheckBox CheckBoxShowDebug;

	private Button BtnClearBits;
	private Button BtnRunTest;
	private Button BtnReloadSettings;

	private Control SettingsWindow;

	private LineEdit TextEditForce1;
	private HSlider SliderForce1;

	private LineEdit TextEditForce100;
	private HSlider SliderForce100;

	private LineEdit TextEditForce1000;
	private HSlider SliderForce1000;

	private LineEdit TextEditForce5000;
	private HSlider SliderForce5000;

	private LineEdit TextEditForce10000;
	private HSlider SliderForce10000;

	private LineEdit TextEditVelocityAmp;
	private HSlider SliderVelocityAmp;

	private LineEdit TextEditDropDelay;
	private HSlider SliderDropDelay;


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
		CheckBoxAutoConnect.ButtonPressed = BitManager.ShouldAutoConnect;
		CheckBoxAutoConnect.Pressed += () =>
		{
			BitManager.ShouldAutoConnect = !BitManager.ShouldAutoConnect;
		};


		CheckBoxCheckBoxSaveBits = GetNode<CheckBox>(new NodePath(UI_URL + "Bools/CheckBoxSaveBits"));
		Debug.Assert(CheckBoxCheckBoxSaveBits != null);
		CheckBoxCheckBoxSaveBits.ButtonPressed = BitManager.ShouldSaveBits;
		CheckBoxCheckBoxSaveBits.Pressed += () =>
		{
			BitManager.ShouldSaveBits = !BitManager.ShouldSaveBits;
		};

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

		ButtonDebugReload = GetNode<Button>(new NodePath(DEBUG_URL + "/ButtonDebugReload"));
		Debug.Assert(ButtonDebugReload != null);
		ButtonDebugReload.Pressed += () =>
		{
			var saveNodes = GetTree().GetNodesInGroup("Persistent");
			foreach (Node saveNode in saveNodes)
			{
				saveNode.Free();
			}

			BitManager.InitBitPool();
		};

		TextChannelName = GetNode<LineEdit>(new NodePath(UI_URL + "TextChannelName"));
		Debug.Assert(TextChannelName != null);
		TextChannelName.TextChanged += TextChannelName_OnTextChanged;
		if (!string.IsNullOrEmpty(BitManager.User.Username))
		{
			TextChannelName.Text = BitManager.User.Username;
		}
		// Note: Make sure color updates at startup
		TextChannelName_OnTextChanged(TextChannelName.Text);

		BtnConnectToChannel = GetNode<Button>(new NodePath(UI_URL + "BtnConnectToChannel"));
		Debug.Assert(BtnConnectToChannel != null);
		BtnConnectToChannel.Pressed += () =>
		{
			UpdateValues();
			BitManager.TwitchManager.ValidateThanFetchOrConnect(BitManager.User);
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

		BtnReloadSettings = GetNode<Button>(UI_URL + "BtnReloadSettings");
		Debug.Assert(BtnReloadSettings != null);
		BtnReloadSettings.Pressed += () =>
		{
			BitManager.Settings.Reload();
		};
		
		SettingsWindow = GetNode<Control>("/root/Base/Container3");
		Debug.Assert(SettingsWindow != null);
		SettingsWindow.Visible = false;
		/*
		TextEditForce1 = GetNode<LineEdit>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/Force1/Label2");
		SliderForce1 = GetNode<HSlider>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/Force1/HSlider");

		Debug.Assert(TextEditForce1 != null);

		TextEditForce1.Text = BitManager.Settings.Force1.ToString();
		SliderForce1.Value = BitManager.Settings.Force1;

		TextEditForce1.TextChanged += (text) => 
		{
			float value = float.Parse(text);
			SliderForce1.Value = value;
			BitManager.Settings.Force1 = value;
		};
		SliderForce1.ValueChanged += (double value) =>
		{
			TextEditForce1.Text = value.ToString();
			BitManager.Settings.Force1 = (float)value;
		};

		TextEditForce100 = GetNode<LineEdit>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/Force100/Label2");
		SliderForce100 = GetNode<HSlider>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/Force100/HSlider");

		TextEditForce100.Text = BitManager.Settings.Force100.ToString();
		SliderForce100.Value = BitManager.Settings.Force100;

		TextEditForce100.TextChanged += (text) =>
		{
			float value = float.Parse(text);
			SliderForce100.Value = value;
			BitManager.Settings.Force100 = value;
		};
		SliderForce100.ValueChanged += (double value) =>
		{
			TextEditForce100.Text = value.ToString();
			BitManager.Settings.Force100 = (float)value;
		};

		TextEditForce1000 = GetNode<LineEdit>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/Force1000/Label2");
		SliderForce1000 = GetNode<HSlider>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/Force1000/HSlider");

		TextEditForce1000.Text = BitManager.Settings.Force1000.ToString();
		SliderForce1000.Value = BitManager.Settings.Force1000;

		TextEditForce1000.TextChanged += (text) =>
		{
			float value = float.Parse(text);
			SliderForce1000.Value = value;
			BitManager.Settings.Force1000 = value;
		};
		SliderForce1000.ValueChanged += (double value) =>
		{
			TextEditForce1000.Text = value.ToString();
			BitManager.Settings.Force1000 = (float)value;
		};

		TextEditForce5000 = GetNode<LineEdit>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/Force5000/Label2");
		SliderForce5000 = GetNode<HSlider>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/Force5000/HSlider");
		
		TextEditForce5000.Text = BitManager.Settings.Force5000.ToString();
		SliderForce5000.Value = BitManager.Settings.Force5000;
		
		TextEditForce5000.TextChanged += (text) =>
		{
			float value = float.Parse(text);
			SliderForce5000.Value = value;
			BitManager.Settings.Force5000 = value;
		};
		SliderForce5000.ValueChanged += (double value) =>
		{
			TextEditForce5000.Text = value.ToString();
			BitManager.Settings.Force5000 = (float)value;
		};

		TextEditForce10000 = GetNode<LineEdit>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/Force10000/Label2");
		SliderForce10000 = GetNode<HSlider>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/Force10000/HSlider");

		TextEditForce10000.Text = BitManager.Settings.Force10000.ToString();
		SliderForce10000.Value = BitManager.Settings.Force10000;

		TextEditForce10000.TextChanged += (text) =>
		{
			float value = float.Parse(text);
			SliderForce10000.Value = value;
			BitManager.Settings.Force10000 = value;
		};
		SliderForce10000.ValueChanged += (double value) =>
		{
			TextEditForce10000.Text = value.ToString();
			BitManager.Settings.Force10000 = (float)value;
		};

		TextEditVelocityAmp = GetNode<LineEdit>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/VelocityAmp/Label2");
		SliderVelocityAmp = GetNode<HSlider>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/VelocityAmp/HSlider");

		TextEditVelocityAmp.Text = BitManager.Settings.VelocityAmp.ToString();
		SliderVelocityAmp.Value = BitManager.Settings.VelocityAmp;

		TextEditVelocityAmp.TextChanged += (text) =>
		{
			float value = float.Parse(text);
			SliderVelocityAmp.Value = value;
			BitManager.Settings.VelocityAmp = value;
		};
		SliderVelocityAmp.ValueChanged += (double value) =>
		{
			TextEditVelocityAmp.Text = value.ToString();
			BitManager.Settings.VelocityAmp = (float)value;
		};

		TextEditDropDelay = GetNode<LineEdit>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/DropDelay/Label2");
		SliderDropDelay = GetNode<HSlider>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/DropDelay/HSlider");

		TextEditDropDelay.Text = BitManager.Settings.DropDelay.ToString();
		SliderDropDelay.Value = BitManager.Settings.DropDelay;

		TextEditDropDelay.TextChanged += (text) =>
		{
			float value = float.Parse(text);
			SliderDropDelay.Value = value;
			BitManager.Settings.DropDelay = value;
		};
		SliderDropDelay.ValueChanged += (double value) =>
		{
			TextEditDropDelay.Text = value.ToString();
			BitManager.Settings.DropDelay = (float)value;
		};
		*/
	}

	private void UpdateValues()
	{
		BitManager.User.Username = TextChannelName.Text;
		BitManager.ShouldAutoConnect = CheckBoxAutoConnect.ActionMode == BaseButton.ActionModeEnum.Press;
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

			LabelDebugBitsInArea.Text = string.Format("BitsInCup: {0}", BitManager.CupArea.GetOverlappingBodies().Count);
		}
	}

	private void TextChannelName_OnTextChanged(string newText)
	{
		StyleBox style = TextChannelName.GetThemeStylebox("normal");
		Debug.Assert(style != null);
		if (style is StyleBoxFlat styleFlat)
		{
			if (string.IsNullOrEmpty(newText)
				|| (BitManager.TwitchManager != null
				&& BitManager.TwitchManager.Client != null
				&& BitManager.TwitchManager.Client.TwitchUsername != newText))
			{
				styleFlat.BorderColor = Colors.Red;
			}
			else
			{
				styleFlat.BorderColor = Colors.Green;
			}
		}
	}
}
