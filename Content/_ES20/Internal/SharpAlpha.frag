#version 100
precision mediump float;

uniform sampler2D mainTex;
uniform float smoothness;

const float Gamma = 2.2;

varying vec2 vTexcoord0;
varying vec4 vCornerColor;

void main() {
    // Retrieve base color
    vec4 texClr = texture2D(mainTex, vTexcoord0);
    
    // Do some anti-aliazing
    float w = clamp(smoothness * (abs(dFdx(vTexcoord0.s)) + abs(dFdy(vTexcoord0.t))), 0.0, 0.5);
    float a = smoothstep(0.5 - w, 0.5 + w, texClr.a);

    // Perform Gamma Correction to achieve a linear attenuation
    texClr.a = pow(a, 1.0 / Gamma);

    // Compose result color
    gl_FragColor = vCornerColor * texClr; 
}