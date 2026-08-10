shader_type canvas_item;

// Screen blur that stacks with punches received & fades back to sharp.
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform float intensity : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	vec4 blurred = textureLod(screen_tex, SCREEN_UV, intensity * 4.5);
	COLOR = vec4(blurred.rgb, clamp(intensity * 20.0, 0.0, 1.0));
}
