using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.ui.hud;

// Looking THROUGH the blowgun's scope (issue #236): a full-screen black mask with a
// circular window, a thin rim, & a laser-dot reticle (Aaron, 2026-08-21: a laser
// reticle, not crosshairs) that drifts with the player's scope model
// (Player.ReticleDrift) - a diffuse red glow while the heartbeat wanders, snapping
// tight with a white-hot core when it settles: that's the window to shoot. Pure
// cosmetics; the aim math lives in Player.AimDirection.
public partial class ScopeView : Control
{
  public const float RadiusFraction = 0.42f; // Of the shorter screen side; Player.AimDirection assumes the same.
  private static readonly Color Rim = new(0.1f, 0.1f, 0.1f);
  private static readonly Color LaserRed = new(1.0f, 0.12f, 0.08f);
  private static readonly Color LaserCore = new(1.0f, 0.85f, 0.8f);
  private const string MaskShader = @"
shader_type canvas_item;
uniform vec2 center;
uniform float radius;
void fragment() {
  float d = distance(FRAGCOORD.xy, center);
  float a = d > radius ? 1.0 : 0.0;
  if (abs(d - radius) < 6.0) a = 1.0;
  COLOR = vec4(0.0, 0.0, 0.0, a);
}";
  private ColorRect _mask = null!;
  private ShaderMaterial _material = null!;
  private Player? _player;

  public override void _Ready()
  {
    MouseFilter = MouseFilterEnum.Ignore;
    SetAnchorsPreset (LayoutPreset.FullRect);
    _material = new ShaderMaterial { Shader = new Shader { Code = MaskShader } };
    _mask = new ColorRect { Material = _material, MouseFilter = MouseFilterEnum.Ignore };
    _mask.SetAnchorsPreset (LayoutPreset.FullRect);
    AddChild (_mask);
    Visible = false;
  }

  public void Track (Player? player) => _player = player;

  public override void _Process (double delta)
  {
    var scoped = _player != null && _player.IsScoped;
    Visible = scoped;
    if (!scoped) return;
    var size = GetViewportRect().Size;
    _material.SetShaderParameter ("center", size / 2.0f);
    _material.SetShaderParameter ("radius", Radius (size));
    QueueRedraw();
  }

  private static float Radius (Vector2 size) => Mathf.Min (size.X, size.Y) * RadiusFraction;

  public override void _Draw()
  {
    if (_player == null || !_player.IsScoped) return;
    var size = GetViewportRect().Size;
    var radius = Radius (size);
    var reticle = size / 2.0f; // The dot stays centered; the CAMERA sways instead (issue #279).
    var settled = _player.IsScopeSettled;
    DrawArc (size / 2.0f, radius, 0.0f, Mathf.Tau, 96, Rim, 3.0f);
    // The laser dot: layered discs fake the bloom. Wandering it breathes wide &
    // hazy; settled it snaps tight with a white-hot core - the shot window.
    DrawCircle (reticle, settled ? 7.0f : 12.0f, LaserRed with { A = 0.18f });
    DrawCircle (reticle, settled ? 4.5f : 7.0f, LaserRed with { A = 0.45f });
    DrawCircle (reticle, settled ? 3.0f : 4.0f, LaserRed);
    if (settled) DrawCircle (reticle, 1.6f, LaserCore);
    var zoom = $"{Scope.ZoomFovs[0] / Scope.ZoomFovs[_player.ZoomStep]:0.#}x";
    DrawString (ThemeDB.FallbackFont, new Vector2 (size.X / 2.0f - 24.0f, size.Y / 2.0f + radius - 16.0f), zoom, HorizontalAlignment.Center, -1, 28, LaserRed);
  }
}
