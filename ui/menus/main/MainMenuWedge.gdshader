shader_type canvas_item;

// Jonathan's diagonal wedge (issue #436): the LEFT of a leaning boundary shows the
// hero art blurred, hue-rotated 70deg & dimmed; the RIGHT is transparent so the
// sharp background underneath shows; the boundary itself is the cyan edge line.
// One shader instead of Paper's nested rotated frames - the extraction notes call
// this out as the cheaper, truer build. All knobs are uniforms so the designer can
// art-direct from a live build without a shader edit.

uniform sampler2D art;
uniform float blur_px = 26.0; // 13px in the 1080 design, x2 for the 4K viewport.
uniform float hue_degrees = 70.0;
uniform float boundary_top_x : hint_range(0.0, 1.0) = 0.565; // Measured off the reference render.
uniform float boundary_bottom_x : hint_range(0.0, 1.0) = 0.327;
uniform vec4 edge_color : source_color = vec4(0.58, 0.988, 0.996, 1.0); // display-p3 (0.580, 0.987, 0.994) ~ #94FCFE sRGB.
uniform float edge_width_px = 4.0;
uniform float wedge_dim : hint_range(0.0, 1.0) = 0.88;

// Cover-fit like CSS background-size: cover, centered.
vec2 cover_uv(vec2 uv, vec2 screen, vec2 tex) {
	float screen_aspect = screen.x / screen.y;
	float tex_aspect = tex.x / tex.y;
	vec2 scale = screen_aspect > tex_aspect ? vec2(1.0, tex_aspect / screen_aspect) : vec2(screen_aspect / tex_aspect, 1.0);
	return (uv - 0.5) * scale + 0.5;
}

// CSS feColorMatrix hueRotate, per channel (matches the browser render Jonathan approved).
vec3 hue_rotate(vec3 color, float degrees) {
	float c = cos(radians(degrees));
	float s = sin(radians(degrees));
	return vec3(
		dot(color, vec3(0.213 + c * 0.787 - s * 0.213, 0.715 - c * 0.715 - s * 0.715, 0.072 - c * 0.072 + s * 0.928)),
		dot(color, vec3(0.213 - c * 0.213 + s * 0.143, 0.715 + c * 0.285 + s * 0.140, 0.072 - c * 0.072 - s * 0.283)),
		dot(color, vec3(0.213 - c * 0.213 - s * 0.787, 0.715 - c * 0.715 + s * 0.715, 0.072 + c * 0.928 + s * 0.072)));
}

void fragment() {
	vec2 screen = 1.0 / SCREEN_PIXEL_SIZE;
	vec2 tex = vec2(textureSize(art, 0));
	vec2 p = UV * screen;
	vec2 a = vec2(boundary_top_x * screen.x, 0.0);
	vec2 b = vec2(boundary_bottom_x * screen.x, screen.y);
	vec2 ab = b - a;
	float d = ((p.x - a.x) * ab.y - (p.y - a.y) * ab.x) / length(ab); // Signed px from the boundary; negative = wedge side.

	if (d > edge_width_px) {
		COLOR = vec4(0.0); // The sharp hero art below shows through.
	} else if (abs(d) <= edge_width_px) {
		COLOR = edge_color;
	} else {
		// 5x5 dilated gaussian: enough softness for art that sits behind dimming &
		// a hue shift; a menu redraws rarely, so the taps are cheap.
		vec2 uv = cover_uv(UV, screen, tex);
		vec3 sum = vec3(0.0);
		float weight_sum = 0.0;
		for (int i = -2; i <= 2; i++) {
			for (int j = -2; j <= 2; j++) {
				vec2 offset = vec2(float(i), float(j)) * (blur_px / 2.5) / screen;
				float w = exp(-float(i * i + j * j) * 0.35);
				sum += texture(art, uv + offset).rgb * w;
				weight_sum += w;
			}
		}
		COLOR = vec4(hue_rotate(sum / weight_sum, hue_degrees) * wedge_dim, 1.0);
	}
}
