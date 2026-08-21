shader_type canvas_item;

// Red screen-edge vignette that fades in as the player nears death, & a green one
// (issue #261) that pulses with every poison tick - the two blend, so a poisoned
// player at death's door reads as both.
uniform float intensity : hint_range(0.0, 1.0) = 0.0;
uniform float poison : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	float d = distance(UV, vec2(0.5));
	float edge = smoothstep(0.3, 0.75, d);
	vec3 color = mix(vec3(0.35, 0.8, 0.2), vec3(0.7, 0.0, 0.0), intensity / max(intensity + poison, 0.001));
	COLOR = vec4(color, edge * clamp(intensity + poison, 0.0, 1.0));
}
