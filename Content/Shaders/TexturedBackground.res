{
    "Fragment": "
        #extension GL_ARB_draw_buffers : enable 

        uniform sampler2D mainTex;

        uniform vec2 ViewSize;

        uniform vec4 horizonColor;
        uniform vec2 shift;

        void main() {
            // Position of pixel to write (between 0 and 1 both directions)
            //vec2 targetCoord = gl_TexCoord[0].xy / ViewSize * vec2(256.0 * 2.5, 256.0 * 1.6);
            vec2 targetCoord = vec2(gl_FragCoord) / ViewSize;

            // Distance to center of screen from top or bottom (1: center of screen, 0: edge of screen)
            float distance = 1.3 - abs(2.0 * targetCoord.y - 1.0);
            float horizonDepth = pow(distance, 2.0);

            float yShift = (targetCoord.y > 0.5 ? 1.0 : 0.0);

            vec2 texturePos = vec2(
                (shift.x / 256.0) + (targetCoord.x - 0.5   ) * (0.5 + (1.5 * horizonDepth)),
                (shift.y / 256.0) + (targetCoord.y - yShift) * 2.0 * distance
            );

            vec4 texColor = texture2D(mainTex, texturePos);
            float horizonOpacity = clamp(pow(distance, 1.8) - 0.4, 0.0, 1.0);

            gl_FragData[0] = mix(texColor, horizonColor, horizonOpacity);
            gl_FragData[1] = vec4(0.5, 0.5, 1.0, 1.0);
        }"
}