using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.ui.hud;

// Looking THROUGH the blowgun's scope (issue #236): a full-screen black mask with a
// circular window, a thin rim, & a reticle that drifts with the player's scope model
// (Player.ReticleDrift) - green when the heartbeat has settled, that's the window to
// shoot. Pure cosmetics; the aim math lives in Player.AimDirection.
public partial class ScopeView : Control
{
  public const float RadiusFraction = 0.42f; // Of the shorter screen side; Player.AimDirection assumes the same.
  private static readonly Color Rim = new(0.1f, 0.1f, 0.1f);
  private static readonly Color Wandering = new(1.0f, 0.35f, 0.25f);
  private static readonly Color Settled = new(0.4f, 1.0f, 0.4f);
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
    var reticle = size / 2.0f + _player.ReticleDrift * radius;
    var color = _player.IsScopeSettled ? Settled : Wandering;
    DrawArc (size / 2.0f, radius, 0.0f, Mathf.Tau, 96, Rim, 3.0f);
    DrawLine (reticle + Vector2.Left * 28.0f, reticle + Vector2.Left * 8.0f, color, 2.0f);
    DrawLine (reticle + Vector2.Right * 8.0f, reticle + Vector2.Right * 28.0f, color, 2.0f);
    DrawLine (reticle + Vector2.Up * 28.0f, reticle + Vector2.Up * 8.0f, color, 2.0f);
    DrawLine (reticle + Vector2.Down * 8.0f, reticle + Vector2.Down * 28.0f, color, 2.0f);
    DrawCircle (reticle, 2.0f, color);
    var zoom = $"{Scope.ZoomFovs[0] / Scope.ZoomFovs[_player.ZoomStep]:0.#}x";
    DrawString (ThemeDB.FallbackFont, new Vector2 (size.X / 2.0f - 24.0f, size.Y / 2.0f + radius - 16.0f), zoom, HorizontalAlignment.Center, -1, 28, color);
  }
}
