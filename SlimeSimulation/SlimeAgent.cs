using System.Numerics;
using System.Runtime.InteropServices;

namespace SlimeSimulation
{
	[StructLayout(LayoutKind.Sequential)]
	public readonly struct SlimeAgent
	{
		public readonly float angle;
		public readonly float speciesIndex;
		public readonly float positionX;
		public readonly float positionY;
		public readonly float speciesMaskX;
		public readonly float speciesMaskY;
		public readonly float speciesMaskZ;
		public readonly float speciesMaskW;

		public SlimeAgent(float angle, Vector2 position, float speciesIndex, Vector4 speciesMask)
		{
			this.angle = angle;
			positionX = position.X;
			positionY = position.Y;
			this.speciesIndex = speciesIndex;
			speciesMaskX = speciesMask.X;
			speciesMaskY = speciesMask.Y;
			speciesMaskZ = speciesMask.Z;
			speciesMaskW = speciesMask.W;
		}
	}
}
