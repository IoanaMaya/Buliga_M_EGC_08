#version 330 core


in vec3 vViewNormal;
in vec3 vViewPos;
in vec3 vWorldNormal;

uniform bool uShowUV;               
uniform vec3 uAtmosphereColor;      
uniform float uAtmosphereStrength;  
uniform vec3 uLightDir;             

out vec4 FragColor;

void main()
{
    if (uShowUV || !gl_FrontFacing)
    {
        discard;
    }

    vec3 normal = normalize(vViewNormal);
    vec3 toCamera = normalize(-vViewPos);

    float rim = 1.0 - clamp(dot(normal, toCamera), 0.0, 1.0);

    float glow = rim * rim * (1.0 - smoothstep(0.55, 1.0, rim)) * uAtmosphereStrength;

    float sun = dot(normalize(vWorldNormal), normalize(uLightDir));
    float day = 0.25 + 0.75 * smoothstep(-0.3, 0.5, sun);

    FragColor = vec4(uAtmosphereColor, clamp(glow * day, 0.0, 1.0));
}