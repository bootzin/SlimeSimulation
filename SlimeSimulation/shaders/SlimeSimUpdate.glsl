#version 450

struct Agent {
	float angle;
	float speciesIndex;
	vec2 position;
	vec4 speciesMask;
};

struct SpeciesSettings {
	float moveSpeed;
	float turnSpeed;

	float sensorAngleDegrees;
	float sensorOffsetDst;
	float sensorSize;
};


layout(std140, set = 0, binding = 0) buffer agentsBuffer 
{
	Agent agents[];
};

layout(std140, set = 1, binding = 0) readonly buffer speciesSettingsBuffer 
{
	SpeciesSettings speciesSettings[];
};

layout(set = 2, binding = 0) uniform floatDataBuffer
{
	float trailWeight;
	float deltaTime;
	float time;
	float padding;
};

layout(set = 2, binding = 1) uniform intDataBuffer
{
	float numAgents;
	float width;
	float height;
	float padding1;
};

layout(set = 2, binding = 2, rgba32f) uniform image2D TrailMap;
layout(set = 2, binding = 3, rgba32f) uniform image2D DiffuseTrailMap;

// Hash function www.cs.ubc.ca/~rbridson/docs/schechter-sca08-turbulence.pdf
uint hash(uint state)
{
    state ^= 2747636419u;
    state *= 2654435769u;
    state ^= state >> 16;
    state *= 2654435769u;
    state ^= state >> 16;
    state *= 2654435769u;
    return state;
}

float scaleToRange01(uint state)
{
    return state / 4294967295.0;
}


float sense(Agent agent, SpeciesSettings settings, float sensorAngleOffset) {
	float sensorAngle = agent.angle + sensorAngleOffset;
	vec2 sensorDir = vec2(cos(sensorAngle), sin(sensorAngle));

	vec2 sensorPos = agent.position + sensorDir * settings.sensorOffsetDst;
	int sensorCentreX = int(sensorPos.x);
	int sensorCentreY = int(sensorPos.y);

	float sum = 0;

	vec4 senseWeight = agent.speciesMask * 2 - 1;

	int iSensorSize = int(settings.sensorSize);

	for (int offsetX = -iSensorSize; offsetX <= iSensorSize; offsetX ++) {
		for (int offsetY = -iSensorSize; offsetY <= iSensorSize; offsetY ++) {
			int sampleX = min(int(width) - 1, max(0, sensorCentreX + offsetX));
			int sampleY = min(int(height) - 1, max(0, sensorCentreY + offsetY));
			sum += dot(senseWeight, imageLoad(DiffuseTrailMap, ivec2(sampleX, sampleY)));
		}
	}

	return sum;
}

layout(local_size_x = 1, local_size_y = 1, local_size_z = 1) in;

void main()
{
	ivec3 id = ivec3(gl_GlobalInvocationID);

	if (id.x > numAgents) {
		return;
	}


	Agent agent = agents[id.x];
	SpeciesSettings settings = speciesSettings[int(agent.speciesIndex)];
	vec2 pos = agent.position;

	uint random = hash(uint(pos.y * width + pos.x + hash(uint(id.x + time * 100000))));

	// Steer based on sensory data
	float sensorAngleRad = settings.sensorAngleDegrees * (3.1415 / 180.0);
	float weightForward = sense(agent, settings, 0);
	float weightLeft = sense(agent, settings, sensorAngleRad);
	float weightRight = sense(agent, settings, -sensorAngleRad);

	
	float randomSteerStrength = scaleToRange01(random);
	float turnSpeed = settings.turnSpeed * 2 * 3.1415;

	// Continue in same direction
	if (weightForward > weightLeft && weightForward > weightRight) {
		agents[id.x].angle += 0;
	}
	else if (weightForward < weightLeft && weightForward < weightRight) {
		agents[id.x].angle += (randomSteerStrength - 0.5) * 2 * turnSpeed * deltaTime;
	}
	// Turn right
	else if (weightRight > weightLeft) {
		agents[id.x].angle -= randomSteerStrength * turnSpeed * deltaTime;
	}
	// Turn left
	else if (weightLeft > weightRight) {
		agents[id.x].angle += randomSteerStrength * turnSpeed * deltaTime;
	}


	// Update position
	vec2 direction = vec2(cos(agent.angle), sin(agent.angle));
	vec2 newPos = agent.position + direction * deltaTime * settings.moveSpeed;

	
	// Clamp position to map boundaries, and pick new random move dir if hit boundary
	if (newPos.x < 0 || newPos.x >= width || newPos.y < 0 || newPos.y >= height) {
		random = hash(random);
		float randomAngle = scaleToRange01(random) * 2 * 3.1415;

		newPos.x = min(width-1,max(0, newPos.x));
		newPos.y = min(height-1,max(0, newPos.y));
		agents[id.x].angle = randomAngle;
	}
	else {
		vec4 oldTrail = imageLoad(DiffuseTrailMap, ivec2(newPos));
		imageStore(TrailMap, ivec2(newPos), min(vec4(1.0), oldTrail + agent.speciesMask * deltaTime * trailWeight));
	}
	
	agents[id.x].position = newPos;
}