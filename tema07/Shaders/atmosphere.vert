#version 330 core


layout (location = 0) in vec3 aPosition;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

out vec3 vViewNormal;    
out vec3 vViewPos;       
out vec3 vWorldNormal;   

void main()
{
    vec4 viewPos = uView * uModel * vec4(aPosition, 1.0);
    vViewPos = viewPos.xyz;
    vViewNormal = mat3(uView * uModel) * aPosition;
    vWorldNormal = mat3(uModel) * aPosition;
    gl_Position = uProjection * viewPos;
}