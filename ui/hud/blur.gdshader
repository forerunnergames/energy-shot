shader_type canvas_item;

// Screen blur that stacks with punches received & fades back to sharp. Max LOD is a
// near-whiteout of detail so a full stack leaves the victim barely able to see (#68).
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform float intensity : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	vec4 blurred = textureLod(screen_tex, SCREEN_UV, intensity * 7.0);
	COLOR = vec4(blurred.rgb, clamp(intensity * 20.0, 0.0, 1.0));
}
