using Godot;

namespace com.forerunnergames.energyshot.ui.menus;

// The parallelogram button family from Jonathan's designs (issues #436, #443): a
// leaning outline (QUIT, BACK) or a filled primary (JOIN GAME), drawn - not
// textured - so the stroke stays crisp at any scale. Default points are his QUIT
// SVG path, doubled into the 4K design space & made local to this control's rect;
// wider variants override Points in their scene. Hover brightens; his dedicated
// state designs (issue #438) replace that default when they land.
public partial class TrapezoidButton : Button
{
  [Export] public Color StrokeColor = new("94fcfe"); // display-p3 (0.580, 0.987, 0.994) ~ sRGB.
  [Export] public float StrokeWidth = 8.0f; // 4px in the 1080 design, x2 for the 4K viewport.
  [Export] public bool Filled; // The primary-action variant fills with the stroke cyan (canon sheet).
  [Export] public Vector2[] Points = OutlinePoints;

  // Closed loop (first point repeated last), pinned by MainMenuStyleTest.
  public static readonly Vector2[] OutlinePoints = { new(0.0f, 148.0f), new(90.8f, 0.0f), new(338.0f, 0.0f), new(248.9f, 147.0f), new(0.0f, 148.0f) };
  public static readonly Color DisabledGray = new("888888"); // The canon sheet's disabled fill.
  private bool _hovered;

  public override void _Ready()
  {
    MouseEntered += () => { _hovered = true; QueueRedraw(); };
    MouseExited += () => { _hovered = false; QueueRedraw(); };
    FocusEntered += QueueRedraw; // Keyboard & controller nav highlight (CodeRabbit on #437).
    FocusExited += QueueRedraw;
  }

  // Antialiased OFF (mac-ops's round-2 diff): the feathered polyline's hard core
  // measured 1px where the design wants 4 - the viewport downsample supplies all the
  // smoothing a hard 8px line needs.
  public override void _Draw()
  {
    var stroke = Disabled ? DisabledGray : _hovered || HasFocus() ? StrokeColor.Lightened (0.25f) : StrokeColor;
    if (Filled) DrawColoredPolygon (Points, Disabled ? DisabledGray : StrokeColor);
    DrawPolyline (Points, stroke, StrokeWidth, antialiased: false);
  }
}
