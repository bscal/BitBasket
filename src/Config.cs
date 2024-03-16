using Godot;
using System.IO;

namespace BitCup
{
	public class Config
	{
		const string FILENAME = "config.txt";

		public static bool HasConfig()
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), FILENAME);
			return File.Exists(path);
		}

		public static void Load(BitManager bitManager)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), FILENAME);

			if (File.Exists(path))
			{
				Debug.LogInfo("(CONFIG) Config found");

				string[] lines = File.ReadAllLines(path);
				foreach (string line in lines)
				{
					if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
						continue;

					if (line.StartsWith("user:"))
					{
						var split = line.Split(' ')[1].Split(';');
						bitManager.User.Username = (split.Length >= 1) ? split[0] : string.Empty;
						bitManager.User.OAuth = (split.Length >= 2) ? split[1] : string.Empty;
					}
				}
			}
			else
			{
				Debug.LogInfo("(CONFIG) Config not found");
			}

			if (bitManager.User.Username == null)
				bitManager.User.Username = string.Empty;

			if (bitManager.User.OAuth == null)
				bitManager.User.OAuth = string.Empty;
		}

		public static void Save(BitManager bitManager)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), FILENAME);

			string[] lines = new string[]
			{
				string.Format("user: {0};{1}", bitManager.User.Username, bitManager.User.OAuth),
			};

			File.WriteAllLines(path, lines);
		}

	}
}
