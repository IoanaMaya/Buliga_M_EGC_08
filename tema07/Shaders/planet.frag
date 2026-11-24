#version 330 core

in vec2 vUV;
in vec3 vLocalPos;
in vec3 vWorldNormal;

uniform bool uShowUV;
uniform float uTime;

uniform vec3 uSeed;
uniform float uNoiseScale;
uniform float uSeaLevel;
uniform float uIceLatitude;
uniform float uCloudAmount;
uniform float uCloudSpeed;
uniform vec3 uLightDir;

out vec4 FragColor;

float hash(vec3 p)
{
	return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453);
}

float valueNoise(vec3 p)
{
	vec3 i = floor(p);
	vec3 f = fract(p);
	vec3 u =f * f * (3.0 - 2.0 * f);

	float c000 = hash(i + vec3(0.0, 0.0, 0.0));
	float c100 = hash(i + vec3(1.0, 0.0, 0.0));
	float c010 = hash(i + vec3(0.0, 1.0, 0.0));
	float c110 = hash(i + vec3(1.0, 1.0, 0.0));
	float c001 = hash(i + vec3(0.0, 0.0, 1.0));
	float c101 = hash(i + vec3(1.0, 0.0, 1.0));
	float c011 = hash(i + vec3(0.0, 1.0, 1.0));
	float c111 = hash(i + vec3(1.0, 1.0, 1.0));

	float x00 = mix(c000, c100, u.x);
	float x10 = mix(c010, c110, u.x);
	float x01 = mix(c001, c101, u.x);
	float x11 = mix(c011, c111, u.x);

	float y0 = mix(x00, x10, u.y);
	float y1 = mix(x01, x11, u.y);

	return mix(y0, y1, u.z);
}

float fbm(vec3 p)
{
	float sum = 0.0;
	float amplitude = 0.5;
	for(int i = 0; i < 5; i++)
	{
		sum += amplitude * valueNoise(p);
		p *= 2.0;
		amplitude *= 0.5;
	}

	return sum;
}

void main()
{
	if(uShowUV)
	{
		FragColor = vec4(vUV, 0.0, 1.0);
		return;
	}

	vec3 n = normalize(vLocalPos);

	float h = fbm(n * uNoiseScale + uSeed);
	float depth = clamp((uSeaLevel - h) / 0.15, 0.0, 1.0);
	vec3 ocean = mix(vec3(0.15, 0.55, 0.75), vec3(0.02, 0.08, 0.30), smoothstep(0.0, 1.0, depth));

	float land = clamp((h - uSeaLevel) / 0.25, 0.0, 1.0);
	vec3 ground = vec3(0.86, 0.80, 0.55);
	ground = mix(ground, vec3(0.35, 0.62, 0.25), smoothstep(0.03, 0.08, land));
	ground = mix(ground, vec3(0.12, 0.38, 0.15), smoothstep(0.25, 0.35, land));
	ground = mix(ground, vec3(0.45, 0.38, 0.32), smoothstep(0.50, 0.60, land));
	ground = mix(ground, vec3(0.95, 0.96, 0.98), smoothstep(0.75, 0.82, land));

	float edge = max(fwidth(h), 0.001);
	float isLand = smoothstep(uSeaLevel - edge, uSeaLevel + edge, h);
	vec3 color = mix(ocean, ground, isLand);

	float wobble = 0.12 * (fbm(n * 5.0 + uSeed + vec3(31.0)) - 0.5);
    float latitude = abs(n.y) + wobble;
    float iceEdge = max(fwidth(latitude), 0.005);
    float ice = smoothstep(uIceLatitude - iceEdge, uIceLatitude + iceEdge, latitude);
    color = mix(color, vec3(0.92, 0.96, 1.0), ice);

    float angle = uTime * uCloudSpeed;
    float ca = cos(angle);
    float sa = sin(angle);
    vec3 cp = vec3(ca * n.x - sa * n.z, n.y, sa * n.x + ca * n.z);
    float clouds = fbm(cp * 3.0 + uSeed.zxy + vec3(0.0, uTime * 0.03, 0.0));
    float cloudMask = smoothstep(0.52, 0.68, clouds) * uCloudAmount;
    color = mix(color, vec3(1.0), cloudMask * 0.9);

    float sun = dot(normalize(vWorldNormal), normalize(uLightDir));
    float light = 0.05 + 0.95 * smoothstep(-0.15, 0.6, sun);
    color *= light;

    FragColor = vec4(color, 1.0);
}