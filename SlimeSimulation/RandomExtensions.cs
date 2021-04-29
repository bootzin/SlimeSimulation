using System;
using System.Numerics;

namespace SlimeSimulation
{
	public static class RandomExtensions
	{
		public static Vector2 InsideUnitCircle(this Random rand)
		{
			var angle = (float)rand.NextDouble() * 2.0f * MathF.PI;
			return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
		}
	}
}
