using BootEngine;
using Platforms.Windows;
using System;
using Veldrid;

namespace SlimeSimulation
{
	public class Program : Application
	{
		public Program(int width, int height, WindowState state) : base(new BootEngine.Window.WindowProps("Slime Simulation", width, height, vSync: false, windowState: state), typeof(WindowsWindow), GraphicsBackend.Direct3D11)
		{
			LayerStack.PushLayer(new SlimeSimulationLayer());
		}

		public static void Main(string[] args)
		{
			var width = 2560;
			var height = 1440;
			var windowState = WindowState.Maximized;
			if (args.Length == 2)
			{
				windowState = WindowState.Normal;
				if (!int.TryParse(args[0], out width) || !int.TryParse(args[1], out height))
				{
					Console.WriteLine("Failed to parse width and height! Make sure to pass them as two integers followed by spaces. Launching with default width and height now");
					windowState = WindowState.Maximized;
				}
			}
			new Program(width, height, windowState).Run();
		}
	}
}
