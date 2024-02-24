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

		public void Reload()
		{
			SettingsResult res = BitCup.Settings.Parse("settings");

			DropDelay = (float)res.Data.GetValueOrDefault("DropDelay", .25f);
			VelocityAmp = (float)res.Data.GetValueOrDefault("VelocityAmp", 1.25f);
			Force1 = (float)res.Data.GetValueOrDefault("Force1", 0);
			Force100 = (float)res.Data.GetValueOrDefault("Force100", 100);
			Force1000 = (float)res.Data.GetValueOrDefault("Force1000", 500);
			Force5000 = (float)res.Data.GetValueOrDefault("Force5000", 1750);
			Force10000 = (float)res.Data.GetValueOrDefault("Force10000", 5000);
			Mass1 = (float)res.Data.GetValueOrDefault("Mass1", 1);
			Mass100 = (float)res.Data.GetValueOrDefault("Mass100", 1.5);
			Mass1000 = (float)res.Data.GetValueOrDefault("Mass1000", 2);
			Mass5000 = (float)res.Data.GetValueOrDefault("Mass5000", 2.5);
			Mass10000 = (float)res.Data.GetValueOrDefault("Mass10000", 3);

			Save();

			Debug.LogInfo("Settings reloaded");
		}

		public void Save()
		{
			Godot.Collections.Dictionary<string, Variant> res = new()
		{
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
		};

			BitCup.Settings.Write("settings", res);

			Debug.LogInfo("Settings Saved");
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
				GD.Print("Script found");

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
									int value = int.Parse(lineSplit[4]);
									res.Data.Add(name, value);
								} break;
							case ("float"):
								{
									float value = float.Parse(lineSplit[4]);
									res.Data.Add(name, value);
								} break;
							case ("bool"):
								{
									bool value = bool.Parse(lineSplit[4]);
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
				GD.Print("Script not found!");
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
