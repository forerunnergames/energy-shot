using Godot;

namespace com.forerunnergames.energyshot.ui.menus;

// The QUIT button from Jonathan's main-menu design (issue #436): a leaning
// parallelogram OUTLINE, drawn - not textured - so the stroke stays crisp at any
// scale. The points are his SVG path, doubled into the 4K design space & made local
// to this control's rect. Hover brightens the stroke; his dedicated state designs
// replace that default when they land.
public partial class TrapezoidButton : Button
{
  [Export] public Color StrokeColor = new("94fcfe"); // display-p3 (0.580, 0.987, 0.994) ~ sRGB.
  [Export] public float StrokeWidth = 8.0f; // 4px in the 1080 design, x2 for the 4K viewport.

  // Closed loop (first point repeated last), pinned by MainMenuStyleTest.
  public static readonly Vector2[] OutlinePoints = { new(0.0f, 148.0f), new(90.8f, 0.0f), new(338.0f, 0.0f), new(248.9f, 147.0f), new(0.0f, 148.0f) };
  private bool _hovered;

  public override void _Ready()
  {
    MouseEntered += () => { _hovered = true; QueueRedraw(); };
    MouseExited += () => { _hovered = false; QueueRedraw(); };
  }

  public override void _Draw() => DrawPolyline (OutlinePoints, _hovered ? StrokeColor.Lightened (0.25f) : StrokeColor, StrokeWidth, antialiased: true);
}
