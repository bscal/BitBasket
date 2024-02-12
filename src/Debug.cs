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
				GD.PrintErr("Assertion failed.");
				throw new ApplicationException(msg);
			} 
#endif
		}

	}
}
