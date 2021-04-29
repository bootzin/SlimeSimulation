using System.Numerics;

namespace SlimeSimulation
{
	internal sealed class SlimeSettings
	{
		public int stepsPerFrame = 1;
		public int? Width;
		public int? Height;
		public int NumAgents = 100;
		public SpawnMode SpawnMode;

		public float trailWeight = 1;
		public float decayRate = 1;
		public float diffuseRate = 1;

		public SpeciesSettings[] speciesSettings;

		public struct SpeciesSettings
		{
			public float moveSpeed;
			public float turnSpeed;

			public float sensorAngleDegrees;
			public float sensorOffsetDst;
			public float sensorSize;
			public Vector3 padding;
		}
	}
}
