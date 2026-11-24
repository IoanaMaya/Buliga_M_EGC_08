#version 330 core

layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec3 aColor;
layout (location = 2) in vec2 aUV;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

out vec2 vUV;
out vec3 vLocalPos;
out vec3 vWorldNormal;

void main(){

	vUV = aUV;
	vLocalPos = aPosition;
	vWorldNormal = mat3(uModel) * aPosition;
	gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
}