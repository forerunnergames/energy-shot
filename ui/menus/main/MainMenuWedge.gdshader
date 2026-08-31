shader_type canvas_item;

// Jonathan's diagonal wedge (issue #436): the LEFT of a leaning boundary shows the
// hero art blurred, hue-rotated 70deg, dimmed & tone-matched; the RIGHT is
// transparent so the sharp background underneath shows; the boundary itself is the
// cyan edge line. The blur is BAKED into a small pre-blurred copy of the art
// (CodeRabbit on #437: 81 in-shader taps were ~300M samples/frame at 4K) - one
// sample per fragment now, & smoother than taps ever were. Hue, dim & tint stay
// live uniforms so the designer can art-direct a running build.

uniform sampler2D art_blurred;
// The art's PLACEMENT rect in design px - the design renders the backdrop at 122.5%
// of the viewport, center (2208, 1180), not a plain cover fit (mac-ops's pixel diff):
// the wedge must show the SAME pixels the backdrop shows beneath it.
uniform vec2 art_rect_position = vec2(-143.36, -81.94);
uniform vec2 art_rect_size = vec2(4704.0, 2522.0);
uniform float hue_degrees = 70.0;
uniform float boundary_top_x : hint_range(0.0, 1.0) = 0.565; // Measured off the reference render.
uniform float boundary_bottom_x : hint_range(0.0, 1.0) = 0.327;
uniform vec4 edge_color : source_color = vec4(0.58, 0.988, 0.996, 1.0); // display-p3 (0.580, 0.987, 0.994) ~ #94FCFE sRGB.
uniform float edge_width_px = 4.0;
uniform float wedge_dim : hint_range(0.0, 1.0) = 0.8;
// Channel gains from mac-ops's round-2 patch means (design vs actual): the design's
// wedge reads greener & a touch brighter than the raw hue-rotate lands.
uniform vec3 wedge_tint = vec3(0.88, 1.27, 1.22);

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
		vec2 uv = (p - art_rect_position) / art_rect_size;
		vec3 c = hue_rotate(texture(art_blurred, uv).rgb, hue_degrees);
		COLOR = vec4(c * wedge_dim * wedge_tint, 1.0);
	}
}
