using Godot;
using System;
using System.Collections.Generic;
using System.IO;

namespace BitCup
{

	public struct SettingsResult
	{
		public Godot.Collections.Dictionary<string, Variant> Data;
		public bool WasFound;
	}


	public struct Settings
	{
		public const int VERSION = 2;
		public const bool FORCE_UPDATE = false;

		public float DropDelay;
		public float VelocityAmp;
		public float Force1;
		public float Force100;
		public float Force1000;
		public float Force5000;
		public float Force10000;
		public float Mass1;
		public float Mass100;
		public float Mass1000;
		public float Mass5000;
		public float Mass10000;

		public string FillTheCupTitle;
		public int FillTheCupBits;
		public int FillTheCupCost;
		public int FillTheCupCooldown;
		public bool FillTheCupPerStream;

		public bool EnableSubBits;
		public bool EnableHypeTrainRain;

		public bool ShouldAutoConnect;
		public bool ShouldSaveBits;
		public bool ExperimentalBitParsing;
		public bool CombineBits;

		public void Reload()
		{
			SettingsResult res = BitCup.Settings.Parse("settings");

			bool hasVersion = res.Data.TryGetValue("Version", out Variant version);
			if (!hasVersion || ((int)version < VERSION && FORCE_UPDATE))
			{
				// Saves persistent settings to reload because force update will
				// reset values to defaults.

				ShouldAutoConnect = (bool)res.Data.GetValueOrDefault("ShouldAutoConnect", false);
				ShouldSaveBits = (bool)res.Data.GetValueOrDefault("ShouldSaveBits", false);
				ExperimentalBitParsing = (bool)res.Data.GetValueOrDefault("ExperimentalBitParsing", false);
				CombineBits = (bool)res.Data.GetValueOrDefault("CombineBits", false);

				res.Data.Clear();

				res.Data.Add("ShouldAutoConnect", Variant.CreateFrom(ShouldAutoConnect));
				res.Data.Add("ShouldSaveBits", Variant.CreateFrom(ShouldSaveBits));
				res.Data.Add("ExperimentalBitParsing", Variant.CreateFrom(ExperimentalBitParsing));
				res.Data.Add("CombineBits", Variant.CreateFrom(CombineBits));
			}

			SetValuesOrDefault(res.Data);

			Save();

			Debug.LogInfo("(SETTINGS) Settings reloaded");
		}

		public void SetValuesOrDefault(Godot.Collections.Dictionary<string, Variant> data)
		{
			DropDelay = (float)data.GetValueOrDefault("DropDelay", .25f);
			VelocityAmp = (float)data.GetValueOrDefault("VelocityAmp", 0.50f);
			Force1 = (float)data.GetValueOrDefault("Force1", 0);
			Force100 = (float)data.GetValueOrDefault("Force100", 0);
			Force1000 = (float)data.GetValueOrDefault("Force1000", 0);
			Force5000 = (float)data.GetValueOrDefault("Force5000", 0);
			Force10000 = (float)data.GetValueOrDefault("Force10000", 0);
			Mass1 = (float)data.GetValueOrDefault("Mass1", 1);
			Mass100 = (float)data.GetValueOrDefault("Mass100", 1.4);
			Mass1000 = (float)data.GetValueOrDefault("Mass1000", 1.5);
			Mass5000 = (float)data.GetValueOrDefault("Mass5000", 1.6);
			Mass10000 = (float)data.GetValueOrDefault("Mass10000", 1.8);
			ShouldAutoConnect = (bool)data.GetValueOrDefault("ShouldAutoConnect", false);
			ShouldSaveBits = (bool)data.GetValueOrDefault("ShouldSaveBits", false);
			ExperimentalBitParsing = (bool)data.GetValueOrDefault("ExperimentalBitParsing", false);
			CombineBits = (bool)data.GetValueOrDefault("CombineBits", false);
			FillTheCupTitle = (string)data.GetValueOrDefault("FillTheCupTitle", "Fill The Cup");
			FillTheCupBits = (int)data.GetValueOrDefault("FillTheCupBits", 10);
			FillTheCupCost = (int)data.GetValueOrDefault("FillTheCupCost", 1000);
			FillTheCupCooldown = (int)data.GetValueOrDefault("FillTheCupCooldown", 0);
			FillTheCupPerStream = (bool)data.GetValueOrDefault("FillTheCupPerStream", false);
			EnableSubBits = (bool)data.GetValueOrDefault("EnableSubBits", false);
			EnableHypeTrainRain = (bool)data.GetValueOrDefault("EnableHypeTrainRain", false);
		}

		public void Save()
		{
			Godot.Collections.Dictionary<string, Variant> res = new()
		{
			{ "Version", Variant.CreateFrom(VERSION) },
			{ "DropDelay", Variant.CreateFrom(DropDelay) },
			{ "VelocityAmp", Variant.CreateFrom(VelocityAmp) },
			{ "Force1", Variant.CreateFrom(Force1) },
			{ "Force100", Variant.CreateFrom(Force100) },
			{ "Force1000", Variant.CreateFrom(Force1000) },
			{ "Force5000", Variant.CreateFrom(Force5000) },
			{ "Force10000", Variant.CreateFrom(Force10000) },
			{ "Mass1", Variant.CreateFrom(Mass1) },
			{ "Mass100", Variant.CreateFrom(Mass100) },
			{ "Mass1000", Variant.CreateFrom(Mass1000) },
			{ "Mass5000", Variant.CreateFrom(Mass5000) },
			{ "Mass10000", Variant.CreateFrom(Mass10000) },
			{ "ShouldAutoConnect", Variant.CreateFrom(ShouldAutoConnect) },
			{ "ShouldSaveBits", Variant.CreateFrom(ShouldSaveBits) },
			{ "ExperimentalBitParsing", Variant.CreateFrom(ExperimentalBitParsing) },
			{ "CombineBits", Variant.CreateFrom(CombineBits) },
			{ "FillTheCupTitle", Variant.CreateFrom(FillTheCupTitle) },
			{ "FillTheCupBits", Variant.CreateFrom(FillTheCupBits) },
			{ "FillTheCupCost", Variant.CreateFrom(FillTheCupCost) },
			{ "FillTheCupCooldown", Variant.CreateFrom(FillTheCupCooldown) },
			{ "FillTheCupPerStream", Variant.CreateFrom(FillTheCupPerStream) },
			{ "EnableSubBits", Variant.CreateFrom(EnableSubBits) },
			{ "EnableHypeTrainRain", Variant.CreateFrom(EnableHypeTrainRain) },
		};

			BitCup.Settings.Write("settings", res);

			Debug.LogInfo("(SETTINGS) Settings saved");
		}

		public static SettingsResult Parse(string filename)
		{
			Debug.Assert(!string.IsNullOrEmpty(filename));

			SettingsResult res = new();
			res.Data = new();

			string path = Path.Combine(Directory.GetCurrentDirectory(), filename + ".txt");

			res.WasFound = File.Exists(path);
			if (res.WasFound)
			{
				Debug.LogInfo("Script found");

				string[] lines = File.ReadAllLines(path);

				if (lines.Length <= 1)
				{
					res.WasFound = false;
					return res;
				}

				foreach (string line in lines)
				{
					if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
						continue;

					string[] lineSplit = line.Split(' ');

					if (lineSplit.Length >= 5 
						&& lineSplit[0] == "var"
						&& !string.IsNullOrEmpty(lineSplit[2]))
					{
						string name = lineSplit[2];
						switch (lineSplit[1])
						{
							case ("string"):
								{
									string value = lineSplit[4];
									res.Data.Add(name, value);
								} break;
							case ("int"):
								{
									int value = 0;
									if (int.TryParse(lineSplit[4], out int result))
									{
										value = result;
									}
									res.Data.Add(name, value);
								} break;
							case ("float"):
								{
									float value = 0;
									if (float.TryParse(lineSplit[4], out float result))
									{
										value = result;
									}
									res.Data.Add(name, value);
								} break;
							case ("bool"):
								{
									bool value = false;
									if (bool.TryParse(lineSplit[4], out bool result))
									{
										value = result;
									}
									res.Data.Add(name, value);
								} break;
							default:
								GD.PrintErr("Invalid type");
								break;
						}

					}
				}
			}
			else
			{
				Debug.Error("Script not found");
			}

			return res;
		}

		public static void Write(string filename, Godot.Collections.Dictionary<string, Variant> data)
		{
			Debug.Assert(!string.IsNullOrEmpty(filename));
			Debug.Assert(data != null);

			string path = Path.Combine(Directory.GetCurrentDirectory(), filename + ".txt");

			StreamWriter file = File.CreateText(path);
			foreach (var pair in data)
			{
				string type;
				switch (pair.Value.VariantType)
				{
					case (Godot.Variant.Type.Int): type = "int"; break;
					case (Godot.Variant.Type.String): type = "string"; break;
					case (Godot.Variant.Type.Bool): type = "bool"; break;
					case (Godot.Variant.Type.Float): type = "float"; break;
					default: type = null; break;
				}

				if (type == null)
					continue;

				string line = string.Format("var {0} {1} = {2}", type, pair.Key, pair.Value.Obj);
				file.WriteLine(line);
			}

			file.Close();
		}
	}
}
