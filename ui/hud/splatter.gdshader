shader_type canvas_item;

// Banana splatter (issue #70): procedural semi-transparent yellow blobs that slide
// slowly down the screen & fade out, synced with the banana stun. No external assets.
uniform float intensity : hint_range(0.0, 1.0) = 0.0;
uniform float slide = 0.0;

float blob(vec2 uv, vec2 center, float radius) {
	return 1.0 - smoothstep(radius * 0.55, radius, distance(uv, center));
}

void fragment() {
	vec2 uv = vec2(UV.x, UV.y - slide);
	float a = 0.0;
	a = max(a, blob(uv, vec2(0.18, 0.22), 0.16));
	a = max(a, blob(uv, vec2(0.52, 0.10), 0.22));
	a = max(a, blob(uv, vec2(0.82, 0.28), 0.14));
	a = max(a, blob(uv, vec2(0.30, 0.52), 0.19));
	a = max(a, blob(uv, vec2(0.68, 0.60), 0.17));
	a = max(a, blob(uv, vec2(0.10, 0.75), 0.13));
	a = max(a, blob(uv, vec2(0.90, 0.80), 0.18));
	a = max(a, blob(uv, vec2(0.45, 0.85), 0.15));
	COLOR = vec4(0.95, 0.82, 0.15, a * intensity * 0.85);
}
