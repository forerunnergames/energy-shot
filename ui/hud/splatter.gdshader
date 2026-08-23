shader_type canvas_item;

// Banana splatter (issues #70 & #165): a dense layer of goopy splat chunks at
// randomized positions & sizes (re-seeded per hit), opaque enough to genuinely
// block the view - getting banana'd should be brutal to see through, but the tone
// stays goofy banana goo. Chunks slide slowly down at varied speeds & drop off
// piece by piece as the 5s stun fades. No external assets.
uniform float intensity : hint_range(0.0, 1.0) = 0.0;
uniform float slide = 0.0;
uniform float seed = 0.0;
// 1.0 = bread crumbs (issue #247): many small, jagged, brown & crusty bits instead of
// big yellow goo - a different splat, not a recolored one.
uniform float crumbs = 0.0;

const int CHUNKS = 24;

// Sinless hash (issue #344): the old sin() hash fed arguments in the tens of
// thousands, where fp32 sin precision collapses on some Windows GPU drivers - the
// worst chunks re-rolled every frame & flickered wildly. This form (Dave Hoskins'
// hash-without-sine) stays in [0,1) territory the whole way & renders identically
// on every platform, every frame.
float hash(float n) {
	n = fract(n * 0.1031 + fract(seed * 0.1137));
	n *= n + 33.33;
	n *= n + n;
	return fract(n);
}

vec2 hash2(float n) {
	return vec2(hash(n), hash(n + 57.31));
}

// An irregular blob: the radius wobbles around the rim so chunks read as goo, not circles.
float splat(vec2 uv, vec2 center, float radius, float wobble) {
	vec2 d = uv - center;
	float angle = atan(d.y, d.x);
	float rim = radius * (0.68 + 0.20 * sin(angle * 5.0 + wobble * 6.28) + 0.12 * sin(angle * 9.0 + wobble * 12.6) + crumbs * 0.22 * sin(angle * 14.0 + wobble * 20.0)); // Crumbs are jagged.
	return 1.0 - smoothstep(rim * 0.75, rim, length(d));
}

void fragment() {
	// Nothing on screen: bail before the chunk loop (issue #200). The node is also
	// hidden while idle, so this is belt-and-braces for the fade edges.
	if (intensity <= 0.0) {
		COLOR = vec4(0.0);
	} else {
	float goo = 0.0;
	float shade = 0.0;
	// 24 chunks, not 48: this loop runs per pixel, & at retina resolution the old
	// count cost over a billion sin/atan calls a frame while the goo was up (#200).
	for (int i = 0; i < CHUNKS; i++) {
		float fi = float(i);
		vec2 rnd = hash2(fi * 7.31);
		float sizeRnd = hash(fi * 3.77);
		float radius = mix(mix(0.10, 0.034, crumbs), mix(0.34, 0.11, crumbs), sizeRnd * sizeRnd); // Big gobs with a few enormous ones (issue #202); small bits & a few crusts for crumbs (#247).
		float fall = 0.5 + 1.0 * hash(fi * 5.13); // Heavier goo slides at its own pace.
		vec2 center = vec2(rnd.x * 1.15 - 0.075, rnd.y * 1.15 - 0.075 + slide * fall);
		// Chunks let go one at a time through the stun instead of one uniform fade.
		float dropAt = 0.45 * hash(fi * 9.29);
		float life = smoothstep(dropAt, dropAt + 0.12, intensity);
		float body = splat(UV, center, radius, hash(fi * 11.71)) * life;
		goo = max(goo, body);
		shade = max(shade, body * hash(fi * 2.71));
	}
	// Mostly-opaque goo with per-chunk tonal variety; a final global fade near the end.
	vec3 bright = mix(vec3(0.98, 0.87, 0.30), vec3(0.82, 0.64, 0.36), crumbs); // Banana goo, or crust.
	vec3 dark = mix(vec3(0.78, 0.62, 0.08), vec3(0.42, 0.26, 0.12), crumbs); // ...& the soft brown inside.
	vec3 color = mix(bright, dark, shade);
	float alpha = smoothstep(0.05, 0.6, goo) * 0.96 * smoothstep(0.0, 0.22, intensity);
	COLOR = vec4(color, alpha);
	}
}
