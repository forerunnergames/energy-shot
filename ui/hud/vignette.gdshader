shader_type canvas_item;

// Red screen-edge vignette that fades in as the player nears death.
uniform float intensity : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	float d = distance(UV, vec2(0.5));
	COLOR = vec4(0.7, 0.0, 0.0, smoothstep(0.3, 0.75, d) * intensity);
}
