using System;

namespace WorkshopLabManager.Helpers
{
	public static class ConsoleHelper
	{
		private static readonly object _consoleLock = new(); 
		
		public static bool ConfirmYesNo(string message)
		{
			Console.ForegroundColor = ConsoleColor.White;
			Console.Write($"{message} (Y/N): ");
			Console.ResetColor();

			return Console.ReadLine().Trim().ToUpper() == "Y";
		}

		public static void WriteLine(string message = null, ConsoleColor color = ConsoleColor.Gray)
		{
			if (message is null)
			{
				// Still serialize blank lines so they don't interleave with colored writes
				lock (_consoleLock)
				{
					Console.WriteLine();
				}
				return;
			}

			lock (_consoleLock)
			{
				var old = Console.ForegroundColor;
				try
				{
					Console.ForegroundColor = color;
					Console.WriteLine(message);
				}
				finally
				{
					Console.ForegroundColor = old;
				}
			}
		}

	}
}
