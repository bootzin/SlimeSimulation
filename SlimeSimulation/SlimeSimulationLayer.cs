using BootEngine.Layers;
using ImGuiNET;
using System;
using System.IO;
using System.Numerics;
using System.Text;
using Veldrid;
using Veldrid.SPIRV;

namespace SlimeSimulation
{
	internal sealed class SlimeSimulationLayer : LayerBase
	{
		private Texture trailMap;
		private Texture diffuseTrailMap;
		private DeviceBuffer agentBuffer;
		private DeviceBuffer speciesSettingsBuffer;

		private DeviceBuffer intDataBuffer;
		private DeviceBuffer diffuseIntDataBuffer;
		private DeviceBuffer floatDataBuffer;
		private DeviceBuffer diffuseFloatDataBuffer;

		private Pipeline updatePipeline;
		private ResourceSet agentsResourceSet;
		private ResourceSet speciesSettingsResourceSet;
		private Pipeline diffusePipeline;
		private Pipeline graphicsPipeline;

		private ResourceSet updateResourceSet;
		private ResourceSet diffuseResourceSet;
		private ResourceSet graphicsResourceSet;
		private DeviceBuffer vertexBuffer;
		private DeviceBuffer indexBuffer;
		private SlimeSettings settings;
		private int numSpecies;
		private bool forceWhiteColor;
		private bool reduceSaturation = true;
		private Vector4[] randomColors;
		private int maxSpeed = 128;
		private int maxTurnSpeed = 4;
		private bool autoAdjustSteps;
		private bool pauseSimulation;
		private bool preserveSpecies;
		private readonly CommandList cl = ResourceFactory.CreateCommandList();
		private readonly Random rand = new Random();

		public const uint MAXIMUM_AGENT_AMOUNT = 10000000u;

		public override void OnAttach()
		{
			numSpecies = 3;
			settings = new SlimeSettings()
			{
				NumAgents = 100000,
				SpawnMode = SpawnMode.RandomCircle,
				stepsPerFrame = 7,
				trailWeight = 10f,
				decayRate = .15f,
				diffuseRate = 1.2f,
				speciesSettings = new[]
				{
					new SlimeSettings.SpeciesSettings()
					{
						sensorSize = 3,
						moveSpeed = 14,
						turnSpeed = 1f,
						sensorAngleDegrees = 30,
						sensorOffsetDst = 24
					},
					new SlimeSettings.SpeciesSettings()
					{
						sensorSize = 2,
						moveSpeed = 14,
						turnSpeed = 2,
						sensorAngleDegrees = 30,
						sensorOffsetDst = 12
					},
					new SlimeSettings.SpeciesSettings()
					{
						sensorSize = 4,
						moveSpeed = 6,
						turnSpeed = 4,
						sensorAngleDegrees = 45,
						sensorOffsetDst = 36
					},
				}
			};

			trailMap = ResourceFactory.CreateTexture(TextureDescription.Texture2D((uint)(settings.Width ?? Width), (uint)(settings.Height ?? Height), 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled | TextureUsage.Storage));
			diffuseTrailMap = ResourceFactory.CreateTexture(TextureDescription.Texture2D((uint)(settings.Width ?? Width), (uint)(settings.Height ?? Height), 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled | TextureUsage.Storage));

			vertexBuffer = ResourceFactory.CreateBuffer(new BufferDescription(16 * 4, BufferUsage.VertexBuffer));
			indexBuffer = ResourceFactory.CreateBuffer(new BufferDescription(2 * 6, BufferUsage.IndexBuffer));

			uint sizeofSlimeAgent = (uint)System.Runtime.InteropServices.Marshal.SizeOf<SlimeAgent>();
			agentBuffer = ResourceFactory.CreateBuffer(new BufferDescription(
				sizeofSlimeAgent * MAXIMUM_AGENT_AMOUNT,
				BufferUsage.StructuredBufferReadWrite,
				sizeofSlimeAgent));
			var sizeofSpeciesSettings = System.Runtime.InteropServices.Marshal.SizeOf<SlimeSettings.SpeciesSettings>();
			speciesSettingsBuffer = ResourceFactory.CreateBuffer(new BufferDescription((uint)(sizeofSpeciesSettings * 20), BufferUsage.StructuredBufferReadOnly, (uint)sizeofSpeciesSettings, true));

			floatDataBuffer = ResourceFactory.CreateBuffer(new BufferDescription(sizeof(float) * 4, BufferUsage.UniformBuffer));
			intDataBuffer = ResourceFactory.CreateBuffer(new BufferDescription(sizeof(float) * 4, BufferUsage.UniformBuffer));

			diffuseIntDataBuffer = ResourceFactory.CreateBuffer(new BufferDescription(sizeof(int) * 2 * 8, BufferUsage.UniformBuffer));
			diffuseFloatDataBuffer = ResourceFactory.CreateBuffer(new BufferDescription(sizeof(float) * 3 * 4, BufferUsage.UniformBuffer));

			var slimeSimUpdateShader = ResourceFactory.CreateFromSpirv(new ShaderDescription(ShaderStages.Compute, ReadShaderBytes("SlimeSimUpdate.glsl"), "main"));
			var agentsResourceLayout = ResourceFactory.CreateResourceLayout(new ResourceLayoutDescription(new ResourceLayoutElementDescription[]
			{
				new ResourceLayoutElementDescription("agentsBuffer", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute),
			}));
			var speciesSettingsResourceLayout = ResourceFactory.CreateResourceLayout(new ResourceLayoutDescription(new ResourceLayoutElementDescription[]
			{
				new ResourceLayoutElementDescription("speciesSettingsBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
			}));
			var updateResourceLayout = ResourceFactory.CreateResourceLayout(new ResourceLayoutDescription(new ResourceLayoutElementDescription[]
			{
				new ResourceLayoutElementDescription("floatDataBuffer", ResourceKind.UniformBuffer, ShaderStages.Compute),
				new ResourceLayoutElementDescription("intDataBuffer", ResourceKind.UniformBuffer, ShaderStages.Compute),
				new ResourceLayoutElementDescription("TrailMap", ResourceKind.TextureReadWrite, ShaderStages.Compute),
				new ResourceLayoutElementDescription("DiffuseTrailMap", ResourceKind.TextureReadWrite, ShaderStages.Compute),
			}));
			updatePipeline = ResourceFactory.CreateComputePipeline(new ComputePipelineDescription(slimeSimUpdateShader, new ResourceLayout[] { agentsResourceLayout, speciesSettingsResourceLayout, updateResourceLayout }, 1, 1, 1));
			agentsResourceSet = ResourceFactory.CreateResourceSet(new ResourceSetDescription(
				agentsResourceLayout,
				agentBuffer
			));
			speciesSettingsResourceSet = ResourceFactory.CreateResourceSet(new ResourceSetDescription(
				speciesSettingsResourceLayout,
				speciesSettingsBuffer
			));
			updateResourceSet = ResourceFactory.CreateResourceSet(new ResourceSetDescription(
				updateResourceLayout,
				floatDataBuffer,
				intDataBuffer,
				trailMap,
				diffuseTrailMap
			));

			var slimeSimDiffuseShader = ResourceFactory.CreateFromSpirv(new ShaderDescription(ShaderStages.Compute, ReadShaderBytes("SlimeSimDiffuse.glsl"), "main"));
			var diffuseResourceLayout = ResourceFactory.CreateResourceLayout(new ResourceLayoutDescription(new ResourceLayoutElementDescription[]
			{
				new ResourceLayoutElementDescription("TrailMap", ResourceKind.TextureReadWrite, ShaderStages.Compute),
				new ResourceLayoutElementDescription("DiffuseTrailMap", ResourceKind.TextureReadWrite, ShaderStages.Compute),
				new ResourceLayoutElementDescription("diffuseFloatDataBuffer", ResourceKind.UniformBuffer, ShaderStages.Compute),
				new ResourceLayoutElementDescription("diffuseIntDataBuffer", ResourceKind.UniformBuffer, ShaderStages.Compute),
			}));
			diffusePipeline = ResourceFactory.CreateComputePipeline(new ComputePipelineDescription(slimeSimDiffuseShader, diffuseResourceLayout, 8, 8, 1));
			diffuseResourceSet = ResourceFactory.CreateResourceSet(new ResourceSetDescription(
				diffuseResourceLayout,
				trailMap,
				diffuseTrailMap,
				diffuseFloatDataBuffer,
				diffuseIntDataBuffer
			));

			Shader[] shaders = ResourceFactory.CreateFromSpirv(
				new ShaderDescription(
					ShaderStages.Vertex,
					ReadShaderBytes("Vertex.glsl"),
					"main"),
				new ShaderDescription(
					ShaderStages.Fragment,
					ReadShaderBytes("Fragment.glsl"),
					"main"));
			ShaderSetDescription shaderSet = new ShaderSetDescription(
				new VertexLayoutDescription[]
				{
					new VertexLayoutDescription(
						new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
						new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2))
				},
				shaders);
			var graphicsLayout = ResourceFactory.CreateResourceLayout(new ResourceLayoutDescription(
				new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
				new ResourceLayoutElementDescription("SS", ResourceKind.Sampler, ShaderStages.Fragment)));
			graphicsResourceSet = ResourceFactory.CreateResourceSet(new ResourceSetDescription(
				graphicsLayout,
				ResourceFactory.CreateTextureView(trailMap),
				GraphicsDevice.PointSampler));
			GraphicsPipelineDescription fullScreenQuadDesc = new GraphicsPipelineDescription(
				BlendStateDescription.SingleDisabled,
				DepthStencilStateDescription.Disabled,
				new RasterizerStateDescription(FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
				PrimitiveTopology.TriangleStrip,
				shaderSet,
				new[] { graphicsLayout },
				GraphicsDevice.SwapchainFramebuffer.OutputDescription);
			graphicsPipeline = ResourceFactory.CreateGraphicsPipeline(ref fullScreenQuadDesc);

			cl.Begin();

			Vector4[] quadVerts =
			{
				new Vector4(-1f, 1f, 0, 1),
				new Vector4(1f, 1f, 1, 1),
				new Vector4(-1f, -1f, 0, 0),
				new Vector4(1f, -1f, 1, 0),
			};

			ushort[] indices = { 0, 1, 2, 3 };

			cl.UpdateBuffer(vertexBuffer, 0, quadVerts);
			cl.UpdateBuffer(indexBuffer, 0, indices);
			cl.UpdateBuffer(intDataBuffer, 0, new Vector4(settings.NumAgents, settings.Width ?? Width, settings.Height ?? Height, 0));
			cl.UpdateBuffer(diffuseIntDataBuffer, 0, new int[] { settings.Width ?? Width, settings.Height ?? Height });
			cl.End();

			GraphicsDevice.SubmitCommands(cl);
			GraphicsDevice.WaitForIdle();
			InitSimulation();
			autoAdjustSteps = true;
			settings.SpawnMode = SpawnMode.Point;
		}

		private SlimeSettings.SpeciesSettings[] GetRandomSpecies(int numSpecies)
		{
			var speciesSettings = new SlimeSettings.SpeciesSettings[numSpecies];
			for (int i = 0; i < numSpecies; i++)
			{
				speciesSettings[i] = new SlimeSettings.SpeciesSettings()
				{
					moveSpeed = (float)(rand.NextDouble() * maxSpeed),
					turnSpeed = (float)(rand.NextDouble() * maxTurnSpeed),
					sensorAngleDegrees = Math.Max(1, rand.Next(90)),
					sensorOffsetDst = rand.Next(37),
					sensorSize = Math.Max(1, rand.Next(5)),
				};
			}
			return speciesSettings;
		}

		private void InitSimulation()
		{
			var agents = new SlimeAgent[settings.NumAgents];
			var centre = new Vector2((settings.Width ?? Width) / 2, (settings.Height ?? Height) / 2);
			var startPos = Vector2.Zero;
			var angle = 0f;
			if (!preserveSpecies)
			{
				randomColors = new Vector4[settings.speciesSettings.Length];
				for (int j = 0; j < numSpecies; j++)
				{
					if (reduceSaturation)
						randomColors[j] = new Vector4((float)rand.NextDouble() / 2f, (float)rand.NextDouble() / 2f, (float)rand.NextDouble() / 2f, 1);
					else
						randomColors[j] = new Vector4((float)rand.NextDouble(), (float)rand.NextDouble(), (float)rand.NextDouble(), 1);
				}
			}
			for (int i = 0; i < agents.Length; i++)
			{
				var randomAngle = (float)rand.NextDouble() * MathF.PI * 2f;
				switch (settings.SpawnMode)
				{
					case SpawnMode.Random:
						startPos = new Vector2(rand.Next(0, settings.Width ?? Width), rand.Next(0, settings.Height ?? Height));
						angle = randomAngle;
						break;
					case SpawnMode.Point:
						startPos = centre;
						angle = randomAngle;
						break;
					case SpawnMode.InwardCircle:
						startPos = centre + (rand.InsideUnitCircle() * (settings.Height ?? Height) * 0.5f);
						angle = MathF.Atan2(Vector2.Normalize(centre - startPos).Y, Vector2.Normalize(centre - startPos).X);
						break;
					case SpawnMode.RandomCircle:
						startPos = centre + (rand.InsideUnitCircle() * (settings.Height ?? Height) * 0.15f);
						angle = randomAngle;
						break;
				}

				Vector4 speciesColor;
				int speciesIndex = 0;

				if (forceWhiteColor)
				{
					speciesColor = Vector4.One;
				}
				else
				{
					speciesIndex = rand.Next(0, settings.speciesSettings.Length);
					speciesColor = randomColors[speciesIndex];
				}

				agents[i] = new SlimeAgent(angle, startPos, speciesIndex, speciesColor);
			}

			GraphicsDevice.UpdateBuffer(agentBuffer, 0, agents);
		}

		private static byte[] ReadShaderBytes(string shaderName)
		{
			using StreamReader sr = File.OpenText(shaderName);
			return Encoding.UTF8.GetBytes(sr.ReadToEnd());
		}

		public override void OnUpdate(float deltaSeconds)
		{
			if (pauseSimulation)
				return;
			if (autoAdjustSteps && deltaSeconds > 1 / 20f)
				settings.stepsPerFrame--;
			if (autoAdjustSteps && deltaSeconds < 1 / 75f)
				settings.stepsPerFrame++;
			cl.Begin();
			for (int i = 0; i < settings.stepsPerFrame; i++)
			{
				cl.SetPipeline(updatePipeline);

				cl.UpdateBuffer(speciesSettingsBuffer, 0, settings.speciesSettings);
				cl.UpdateBuffer(floatDataBuffer, 0, new Vector4(settings.trailWeight, deltaSeconds, deltaSeconds, 0));

				cl.SetComputeResourceSet(0, agentsResourceSet);
				cl.SetComputeResourceSet(1, speciesSettingsResourceSet);
				cl.SetComputeResourceSet(2, updateResourceSet);
				cl.Dispatch((uint)settings.NumAgents, 1, 1);

				cl.SetPipeline(diffusePipeline);

				cl.UpdateBuffer(diffuseFloatDataBuffer, 0, new Vector3(settings.decayRate, settings.diffuseRate, deltaSeconds));

				cl.SetComputeResourceSet(0, diffuseResourceSet);
				cl.Dispatch((uint)((settings.Width ?? Width / 8f) + .5f), (uint)((settings.Height ?? Height / 8f) + .5f), 1);

				cl.CopyTexture(diffuseTrailMap, trailMap);
			}

			cl.SetFramebuffer(GraphicsDevice.SwapchainFramebuffer);
			cl.SetFullViewport(0);
			cl.SetFullScissorRect(0);
			cl.ClearColorTarget(0, RgbaFloat.Black);
			cl.SetPipeline(graphicsPipeline);
			cl.SetVertexBuffer(0, vertexBuffer);
			cl.SetIndexBuffer(indexBuffer, IndexFormat.UInt16);
			cl.SetGraphicsResourceSet(0, graphicsResourceSet);
			cl.DrawIndexed(4, 1, 0, 0, 0);

			cl.End();
			GraphicsDevice.SubmitCommands(cl);
		}

		public override void OnGuiRender()
		{
			ImGui.Begin("Control Panel");
			ImGui.BeginChild("Initial parameters");
			ImGui.InputInt("Num Agents", ref settings.NumAgents); Tooltip("Amount of particles simulated. Changes take effect immediatly.");
			ImGui.InputInt("Num Species", ref numSpecies); Tooltip("Number of species that will be generated in a new simulation. Only works if 'Preserve Species' is set to false.");
			if (numSpecies > 20)
				numSpecies = 20;
			if (ImGui.BeginCombo("Spawn Mode", settings.SpawnMode.ToString()))
			{
				for (int i = 0; i < Enum.GetValues<SpawnMode>().Length; i++)
				{
					var spawnMode = (SpawnMode)i;
					bool isSelected = settings.SpawnMode == spawnMode;
					if (ImGui.Selectable(spawnMode.ToString(), isSelected))
					{
						settings.SpawnMode = spawnMode;
					}

					if (isSelected)
						ImGui.SetItemDefaultFocus();
				}

				ImGui.EndCombo();
			} Tooltip("Defines how particles are initially placed in the scene.");

			ImGui.Checkbox("Force White Color", ref forceWhiteColor); Tooltip("Ignore Species color, but not other settings.");
			ImGui.Checkbox("Reduce Saturation", ref reduceSaturation); Tooltip("Helps avoiding particles from different species grouping together and forming a 'White' specie.");
			ImGui.Checkbox("Pause Simulation", ref pauseSimulation); Tooltip("Pause the simulation.");
			ImGui.Checkbox("Auto adjust Steps per Frame", ref autoAdjustSteps); Tooltip("Automatically increases/decreases steps per frame according to performance.");

			ImGui.InputInt("Max Speed", ref maxSpeed); Tooltip("Maximum assignable speed set to new species in a random simulation.");
			ImGui.InputInt("Max Turn Speed", ref maxTurnSpeed); Tooltip("Maximum assignable turn speed set to new species in a random simulation.");

			ImGui.Checkbox("Preserve Species", ref preserveSpecies); Tooltip("Preserve current species when restarting the simulation");

			if (ImGui.Button("Restart Simulation"))
			{
				if (!preserveSpecies)
					settings.speciesSettings = GetRandomSpecies(numSpecies);
				InitSimulation();
			} Tooltip("Begin a new simulation with random settings.");
			ImGui.EndChild();
			ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 20);
			ImGui.TextDisabled("Made by Bootzin.");
			ImGui.End();

			ImGui.Begin("Simulation Settings");
			ImGui.SliderInt("Steps per frame", ref settings.stepsPerFrame, 1, 16); Tooltip("Simulation speed. Has a big impact on performance. Might have to change decay rate after changing this value.");
			ImGui.DragFloat("Trail Weight", ref settings.trailWeight, 0.5f, 0, 15); Tooltip("Intensity of trail left by each particle.");
			ImGui.DragFloat("Decay Rate", ref settings.decayRate, 0.005f, 0, 4); Tooltip("Rate in which particle trail decreases.");
			ImGui.DragFloat("Diffuse Rate", ref settings.diffuseRate, 0.01f, 0, 2); Tooltip("Rate in which particle trail dissipates.");
			ImGui.Separator();
			for (int i = 0; i < settings.speciesSettings.Length; i++)
			{
				ImGui.DragFloat($"Species {i + 1} Move Speed", ref settings.speciesSettings[i].moveSpeed, 1, 0, maxSpeed); Tooltip("Particle movement speed.");
				ImGui.DragFloat($"Species {i + 1} Turn Speed", ref settings.speciesSettings[i].turnSpeed, .01f, 0, maxTurnSpeed); Tooltip("Particle turn speed.");
				ImGui.DragFloat($"Species {i + 1} Sensor Angle", ref settings.speciesSettings[i].sensorAngleDegrees, .5f, 0, 90); Tooltip("Angle in degrees between each of the 3 sensors.");
				ImGui.DragFloat($"Species {i + 1} Sensor Offset", ref settings.speciesSettings[i].sensorOffsetDst, .1f, 0, 36); Tooltip("Distance of the sensors from the Particle. 'How far the particle can see?'");
				ImGui.DragFloat($"Species {i + 1} Sensor Size", ref settings.speciesSettings[i].sensorSize, 1, 0, 4); Tooltip("Radius of each sensor. 'How much can the particle see at each sensor?'");
				ImGui.ColorButton($"##Species {i + 1} Color", randomColors[i], ImGuiColorEditFlags.NoBorder, new Vector2(ImGui.CalcItemWidth(), ImGui.GetFontSize() + 2)); Tooltip("Color that represents this species. Cannot be edited.");
				ImGui.SameLine(); ImGui.TextUnformatted($"Species {i + 1} Color");
				ImGui.Separator();
			}
			ImGui.End();
		}

		private static void Tooltip(string text)
		{
			if (ImGui.IsItemHovered())
			{
				ImGui.BeginTooltip();
				ImGui.TextUnformatted(text);
				ImGui.EndTooltip();
			}
		}

		public override void OnDetach()
		{
			trailMap.Dispose();
			diffuseTrailMap.Dispose();

			agentBuffer.Dispose();
			speciesSettingsBuffer.Dispose();
			vertexBuffer.Dispose();
			indexBuffer.Dispose();

			intDataBuffer.Dispose();
			floatDataBuffer.Dispose();
			diffuseIntDataBuffer.Dispose();
			diffuseFloatDataBuffer.Dispose();

			updatePipeline.Dispose();
			diffusePipeline.Dispose();
			graphicsPipeline.Dispose();

			updateResourceSet.Dispose();
			diffuseResourceSet.Dispose();
			graphicsResourceSet.Dispose();

			cl.Dispose();
		}
	}
}
