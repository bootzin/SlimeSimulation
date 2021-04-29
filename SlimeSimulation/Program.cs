using BootEngine;
using Platforms.Windows;
using Veldrid;

namespace SlimeSimulation
{
	public class Program : Application
	{
		public Program() : base(new BootEngine.Window.WindowProps("Slime Simulation", 2550, 1440, vSync: false, windowState: WindowState.Maximized), typeof(WindowsWindow), GraphicsBackend.Direct3D11)
		{
			LayerStack.PushLayer(new SlimeSimulationLayer());
		}

		public static void Main()
		{
			new Program().Run();
		}
	}
}
