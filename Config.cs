using System;
using System.IO;


namespace BitCup
{
	public class Config
	{
		const string FILENAME = "config.txt";

		public string Username;
		public bool AutoConnect;

		public static bool HasConfig()
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), FILENAME);
			return File.Exists(path);
		}

		public static Config Load()
		{
			Config res = new Config();

			string path = Path.Combine(Directory.GetCurrentDirectory(), FILENAME);

			if (File.Exists(path))
			{
				string[] lines = File.ReadAllLines(path);
				foreach (string line in lines)
				{
					if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
						continue;

					if (line.StartsWith("username: "))
						res.Username = line.Split(' ')[1];
					else if (line.StartsWith("auto_connect: "))
						res.AutoConnect = bool.Parse(line.Split(' ')[1]);

				}
			}

			if (res.Username == null)
				res.Username = string.Empty;

			return res;
		}

		public static void Save(Config config)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), FILENAME);

			string[] lines = new string[2];
			lines[0] = "username: " + config.Username;
			lines[1] = "auto_connect: " + config.AutoConnect;

			File.WriteAllLines(path, lines);
		}

	}
}
