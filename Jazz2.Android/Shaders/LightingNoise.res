{
    "BlendMode": "Add",

    "Vertex": "#inherit MinimalC1P3T4",
    "Fragment": "
        #version 300 es 
        precision highp float;

        uniform vec2 ViewSize;
        uniform float GameTime;

        uniform sampler2D normalBuffer;
        uniform sampler2D noiseTex;

        in vec4 vTexcoord0;
        in vec4 vCornerColor;

        out vec4 vFragColor;

        void main() {
            vec2 center = vTexcoord0.xy;
            float radiusNear = vTexcoord0.z;
            float radiusFar = vTexcoord0.w;
            float intensity = vCornerColor.r;
            float brightness = vCornerColor.g;

            float dist = distance(vec2(gl_FragCoord), center);
            if (dist > radiusFar) {
                vFragColor = vec4(0, 0, 0, 0);
                return;
            }

            vec4 clrNormal = texture(normalBuffer, vec2(gl_FragCoord) / ViewSize);
            vec3 normal = normalize(clrNormal.xyz - vec3(0.5, 0.5, 0.5));
            normal.z = -normal.z;

            vec3 lightDir = vec3((center.x - gl_FragCoord.x), (center.y - gl_FragCoord.y), 0.0);

            // Diffuse lighting
            float diffuseFactor = 1.0 - max(dot(normal, normalize(lightDir)), 0.0);

            float noise = 0.3 + 0.7 * texture(noiseTex, (gl_FragCoord.xy / ViewSize.xx) + vec2(GameTime * 1.5, GameTime)).r;

            float strength = noise * diffuseFactor * clamp(1.0 - ((dist - radiusNear) / (radiusFar - radiusNear)), 0.0, 1.0);
            vFragColor = vec4(strength * intensity, strength * brightness, 0.0, 1.0);
        }"
}