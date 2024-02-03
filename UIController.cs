using Godot;
using System;
using System.Diagnostics;

public partial class UIController : Node
{

	private BitManager BitManager;

	private CheckButton BtnShowDebug;

	private BoxContainer DebugUI;
	private Label LabelDebugFPS;
	private Label LabelDebugFrameTime;
	private Label LabelDebugActiveBits;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		BitManager = GetNode<BitManager>(new NodePath("../BitManager"));
		Debug.Assert(BitManager != null);

		BtnShowDebug = GetNode<CheckButton>(new NodePath("../UI/GridContainer/BtnShowDebug"));
		Debug.Assert(BtnShowDebug != null);

		BtnShowDebug.ButtonDown += BtnShowDebugPressed;

		DebugUI = GetNode<BoxContainer>(new NodePath("../DebugUI"));
		Debug.Assert(DebugUI != null);
		DebugUI.Hide();

		LabelDebugFPS = GetNode<Label>(new NodePath("../DebugUI/LabelDebugFPS"));
		Debug.Assert(LabelDebugFPS != null);

		LabelDebugFrameTime = GetNode<Label>(new NodePath("../DebugUI/LabelDebugFrameTime"));
		Debug.Assert(LabelDebugFrameTime != null);

		LabelDebugActiveBits = GetNode<Label>(new NodePath("../DebugUI/LabelDebugActiveBits"));
		Debug.Assert(LabelDebugActiveBits != null);
	}

	private void BtnShowDebugPressed()
	{
		DebugUI.Visible = !DebugUI.Visible;
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		if (DebugUI.Visible)
		{
			LabelDebugFPS.Text = string.Format("FPS: {0}",
				Performance.Singleton.GetMonitor(Performance.Monitor.TimeFps));

			LabelDebugFrameTime.Text = string.Format("FrameTime: {0:0.00}ms",
				Performance.Singleton.GetMonitor(Performance.Monitor.TimeProcess) * 1000);

			LabelDebugActiveBits.Text = string.Format("ActiveBits: {0}", BitManager.BitStatesDense.Count);
		}
	}
}
