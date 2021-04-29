#version 450

layout(set = 0, binding = 0, rgba32f) uniform image2D TrailMap;
layout(set = 0, binding = 1, rgba32f) uniform image2D DiffuseTrailMap;

layout(set = 0, binding = 2) uniform diffuseFloatDataBuffer
{
	float decayRate;
	float diffuseRate;
	float deltaTime;
};

layout(set = 0, binding = 3) uniform diffuseIntDataBuffer
{
	int width;
	int height;
};

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

void main()
{
	ivec3 id = ivec3(gl_GlobalInvocationID);
	if (id.x < 0 || id.x >= uint(width) || id.y < 0 || id.y >= uint(height)) {
		return;
	}

	vec4 sum = vec4(0);
	vec4 originalCol = imageLoad(TrailMap, id.xy);
	// 3x3 blur
	for (int offsetX = -1; offsetX <= 1; offsetX ++) {
		for (int offsetY = -1; offsetY <= 1; offsetY ++) {
			int sampleX = min(width-1, max(0, id.x + offsetX));
			int sampleY = min(height-1, max(0, id.y + offsetY));
			sum += imageLoad(TrailMap, ivec2(sampleX,sampleY));
		}
	}

	vec4 blurredCol = sum / 9;
	float diffuseWeight = clamp(diffuseRate * deltaTime, 0, 1);
	blurredCol = originalCol * (1 - diffuseWeight) + blurredCol * (diffuseWeight);

	imageStore(DiffuseTrailMap, id.xy, max(vec4(0.0), blurredCol - decayRate * deltaTime));
}