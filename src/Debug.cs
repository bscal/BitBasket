using Godot;
using System;

namespace BitCup
{
	internal class Debug
	{
		public static void Assert(bool expr)
		{
#if DEBUG
			Assert(expr, "Assertion failed.");
#endif
		}

		public static void Assert(bool expr, string msg)
		{
#if DEBUG
			if (!(expr))
			{
				GD.PushError("Assertion failed.");
				throw new ApplicationException(msg);
			} 
#endif
		}

		public static void LogInfo(string msg, params object[] args)
		{
			GD.PrintRaw("[ Info ] ");
			GD.Print(string.Format(msg, args));
		}

		public static void LogDebug(string msg, params object[] args)
		{
#if DEBUG
			GD.PrintRaw("[ Debug ] ");
			GD.Print(string.Format(msg, args));
#endif
		}

		public static void LogErr(string msg, params object[] args)
		{
			GD.PrintRaw("[ Error ] ");
			GD.Print(string.Format(msg, args));
		}
		public static void Error(string msg, params object[] args)
		{
			GD.PushError("[ Error ] " + string.Format(msg, args));
		}
	}
}
