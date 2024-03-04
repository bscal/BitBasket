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
	private CheckBox CheckBoxExperimentalBitParsing;

	private Button BtnSaveSettings;
	private Button BtnResetSettings;
	private Button BtnClearBits;
	private Button BtnRunTest;

	private Control SettingsWindow;
	private Button BtnShowSettings;

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

	private Control PanelUpdateAvailable;
	private Button BtnCloseUpdateAvailable;

	private bool HasAlertedOfUpdate;

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

		CheckBoxExperimentalBitParsing = GetNode<CheckBox>(new NodePath(UI_URL + "Bools/CheckBoxShowDebug"));
		Debug.Assert(CheckBoxExperimentalBitParsing != null);
		CheckBoxExperimentalBitParsing.Pressed += () =>
		{
			BitManager.Settings.ExperimentalBitParsing = !BitManager.Settings.ExperimentalBitParsing;
			BitManager.Settings.Save();
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

		BtnRunTest = GetNode<Button>(new NodePath(UI_URL + "HBoxContainer/BtnRunTest"));
		Debug.Assert(BtnRunTest != null);
		BtnRunTest.Pressed += () =>
		{
			LineEdit lineEdit = GetNode<LineEdit>(UI_URL + "HBoxContainer/LineEdit");
			Debug.Assert(lineEdit != null);

			int bitAmount;
			if (string.IsNullOrEmpty(lineEdit.Text))
				bitAmount = 16325;
			else
				bitAmount = int.Parse(lineEdit.Text);
			
			BitManager.CreateOrderWithChecks(bitAmount);
		};

		{
			SettingsWindow = GetNode<Control>("/root/Base/Container3");
			Debug.Assert(SettingsWindow != null);
			SettingsWindow.Visible = false;

			BtnShowSettings = GetNode<Button>("/root/Base/Container/UI/BtnOpenSettings");
			Debug.Assert(BtnShowSettings != null);
			BtnShowSettings.Pressed += () =>
			{
				if (SettingsWindow.Visible)
				{
					UpdateSettings();
				}

				SettingsWindow.Visible = !SettingsWindow.Visible;
			};

			TextEditForce1 = GetNode<LineEdit>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/Force1/Label2");
			SliderForce1 = GetNode<HSlider>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/Force1/HSlider");
			Debug.Assert(TextEditForce1 != null);
			SliderForce1.Value = BitManager.Settings.Force1;
			TextEditForce1.Text = BitManager.Settings.Force1.ToString();
			TextEditForce1.TextChanged += (text) =>
			{
				double value = double.Parse(text);
				SliderForce1.SetValueNoSignal(value);
			};
			SliderForce1.ValueChanged += (double value) =>
			{
				TextEditForce1.Text = value.ToString();
			};

			TextEditForce100 = GetNode<LineEdit>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/Force100/Label2");
			SliderForce100 = GetNode<HSlider>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/Force100/HSlider");
			SliderForce100.Value = BitManager.Settings.Force100;
			TextEditForce100.Text = BitManager.Settings.Force100.ToString();
			TextEditForce100.TextChanged += (text) =>
			{
				double value = double.Parse(text);
				SliderForce100.SetValueNoSignal(value);
			};
			SliderForce100.ValueChanged += (double value) =>
			{
				TextEditForce100.Text = value.ToString();
			};

			TextEditForce1000 = GetNode<LineEdit>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/Force1000/Label2");
			SliderForce1000 = GetNode<HSlider>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/Force1000/HSlider");
			SliderForce1000.Value = BitManager.Settings.Force1000;
			TextEditForce1000.Text = BitManager.Settings.Force1000.ToString();	
			TextEditForce1000.TextChanged += (text) =>
			{
				double value = double.Parse(text);
				SliderForce1000.SetValueNoSignal(value);
			};
			SliderForce1000.ValueChanged += (double value) =>
			{
				TextEditForce1000.Text = value.ToString();
			};

			TextEditForce5000 = GetNode<LineEdit>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/Force5000/Label2");
			SliderForce5000 = GetNode<HSlider>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/Force5000/HSlider");
			SliderForce5000.Value = BitManager.Settings.Force5000;
			TextEditForce5000.Text = BitManager.Settings.Force5000.ToString();
			TextEditForce5000.TextChanged += (text) =>
			{
				double value = double.Parse(text);
				SliderForce5000.SetValueNoSignal(value);
			};
			SliderForce5000.ValueChanged += (double value) =>
			{
				TextEditForce5000.Text = value.ToString();
			};


			TextEditForce10000 = GetNode<LineEdit>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/Force10000/Label2");
			SliderForce10000 = GetNode<HSlider>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/Force10000/HSlider");
			SliderForce10000.Value = BitManager.Settings.Force10000;
			TextEditForce10000.Text = BitManager.Settings.Force10000.ToString();
			TextEditForce10000.TextChanged += (text) =>
			{
				double value = double.Parse(text);
				SliderForce10000.SetValueNoSignal(value);
			};
			SliderForce10000.ValueChanged += (double value) =>
			{
				TextEditForce10000.Text = value.ToString();
			};

			TextEditVelocityAmp = GetNode<LineEdit>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/VelocityAmp/Label2");
			SliderVelocityAmp = GetNode<HSlider>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/VelocityAmp/HSlider");
			SliderVelocityAmp.Value = BitManager.Settings.VelocityAmp;
			TextEditVelocityAmp.Text = BitManager.Settings.VelocityAmp.ToString();
			TextEditVelocityAmp.TextChanged += (text) =>
			{
				double value = double.Parse(text);
				SliderVelocityAmp.SetValueNoSignal(value);
			};
			SliderVelocityAmp.ValueChanged += (double value) =>
			{
				TextEditVelocityAmp.Text = value.ToString();
			};

			TextEditDropDelay = GetNode<LineEdit>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/DropDelay/Label2");
			SliderDropDelay = GetNode<HSlider>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/DropDelay/HSlider");
			TextEditDropDelay.Text = BitManager.Settings.DropDelay.ToString();
			SliderDropDelay.Value = BitManager.Settings.DropDelay;
			TextEditDropDelay.TextChanged += (text) =>
			{
				double value = double.Parse(text);
				SliderDropDelay.SetValueNoSignal(value);
			};
			SliderDropDelay.ValueChanged += (double value) =>
			{
				TextEditDropDelay.Text = value.ToString();
			};
		}

		BtnSaveSettings = GetNode<Button>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/HBoxContainer/SaveButton");
		BtnSaveSettings.Pressed += UpdateSettings;

		BtnResetSettings = GetNode<Button>("/root/Base/Container3/AspectRatioContainer/VSplitContainer/Margin/VBoxContainer/HBoxContainer/ResetButton");
		BtnResetSettings.Pressed += () =>
		{
			BitManager.Settings.SetValuesOrDefault(new Godot.Collections.Dictionary<string, Variant>());
			SliderDropDelay.Value = BitManager.Settings.DropDelay;
			SliderVelocityAmp.Value = BitManager.Settings.VelocityAmp;
			SliderForce1.Value = BitManager.Settings.Force1;
			SliderForce100.Value = BitManager.Settings.Force100;
			SliderForce1000.Value = BitManager.Settings.Force1000;
			SliderForce5000.Value = BitManager.Settings.Force5000;
			SliderForce10000.Value = BitManager.Settings.Force10000;
			BitManager.Settings.Save();
		};

		PanelUpdateAvailable = GetNode<Control>("/root/Base/PanelUpdateAvailable");
		Debug.Assert(PanelUpdateAvailable != null);
		PanelUpdateAvailable.Visible = false;

		BtnCloseUpdateAvailable = GetNode<Button>("/root/Base/PanelUpdateAvailable/PanelContainer/VBoxContainer/CloseUpdateAvailable");
		BtnCloseUpdateAvailable.Pressed += () => { PanelUpdateAvailable.Visible = false; };
	}

	private void UpdateSettings()
	{
		BitManager.Settings.DropDelay = (float)SliderDropDelay.Value;
		BitManager.Settings.VelocityAmp = (float)SliderVelocityAmp.Value;
		BitManager.Settings.Force1 = (float)SliderForce1.Value;
		BitManager.Settings.Force100 = (float)SliderForce100.Value;
		BitManager.Settings.Force1000 = (float)SliderForce1000.Value;
		BitManager.Settings.Force5000 = (float)SliderForce5000.Value;
		BitManager.Settings.Force10000 = (float)SliderForce10000.Value;
		BitManager.Settings.Save();
	}

	private void UpdateValues()
	{
		BitManager.User.Username = TextChannelName.Text;
		BitManager.ShouldAutoConnect = CheckBoxAutoConnect.ActionMode == BaseButton.ActionModeEnum.Press;
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		if (BitManager.IsUpdateAvailable && !HasAlertedOfUpdate)
		{
			HasAlertedOfUpdate = true;
			PanelUpdateAvailable.Visible = true;
		}
		
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
