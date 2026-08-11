using System.Linq;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Cosmetics: body colors & hit flash, the full-charge x-ray silhouette, plus
// name/health tag text & distance scaling. White is EXCLUSIVELY the spawn-armor
// indicator (issue #114): every other effect (hit flash, streak glow, full-auto
// tint, x-ray ghost) uses non-white colors or separate nodes.
public partial class Player
{
  private static readonly Color NormalColor = new("0027ff");
  private static readonly Color HitColor = Colors.DarkRed;
  // White glow blended over the player's color, so armored players stay identifiable.
  private static readonly Color SpawnArmorColor = NormalColor.Lerp (Colors.White, 0.65f);
  private static readonly Color XrayColor = new(1.0f, 0.1f, 0.1f, 0.55f);
  private void SetColor (Color color) => (_mesh.GetSurfaceOverrideMaterial (0) as StandardMaterial3D)!.AlbedoColor = color;
  // Restores the player's resting color: white glow while spawn armor holds, normal blue otherwise.
  private void RestoreBaseColor() => SetColor (SpawnArmor ? SpawnArmorColor : NormalColor);

  // A firing/punching player provably has no spawn armor, & those broadcasts are
  // reliable RPCs - so even if the SpawnArmor=false delta was missed, the white
  // armor glow clears the moment this puppet is seen attacking (issue #114).
  private void ClearArmorDisplayOnRemoteAttack()
  {
    if (IsMultiplayerAuthority() || !_spawnArmor) return;
    _spawnArmor = false;
    RestoreBaseColor();
  }

  // Failsafe for a missed SpawnArmor=false delta at expiry (issue #114): puppets
  // clear the white glow themselves once the known armor window (plus slack) passes.
  private void ClearStaleArmorDisplay()
  {
    if (IsMultiplayerAuthority() || !_spawnArmor || _mesh == null) return;
    if (Time.GetTicksMsec() < _armorDisplayEndMs) return;
    _spawnArmor = false;
    RestoreBaseColor();
    GD.Print ($"{DisplayName}: Cleared stale spawn-armor display");
  }

  // Full-charge x-ray (issue #105): while the local player holds a pierce-hot
  // charge, every other player shows a red silhouette through walls & floors -
  // this player's view only, no networking.
  private bool _xrayRevealActive;
  private MeshInstance3D? _xrayGhost;

  private void UpdateXrayReveal()
  {
    var active = HasLaser && IsLaserSelected && _energyWeapon.IsFullyCharged;
    // Re-apply every frame while active (it's idempotent) so players who join
    // mid-charge get revealed too; only skip when staying inactive.
    if (!active && !_xrayRevealActive) return;
    _xrayRevealActive = active;
    foreach (var player in GetParent().GetChildren().OfType <Player>().Where (player => player != this)) player.SetXrayRevealed (active);
  }

  // Cheap x-ray: one extra mesh per revealed puppet, drawn only where geometry
  // occludes it (inverted depth test), following the body's pose & scale.
  public void SetXrayRevealed (bool revealed)
  {
    if (IsMultiplayerAuthority()) return;
    _xrayGhost ??= CreateXrayGhost();
    _xrayGhost.Visible = revealed;
  }

  private MeshInstance3D CreateXrayGhost()
  {
    var ghost = new MeshInstance3D
    {
      Mesh = _mesh.Mesh,
      MaterialOverride = new StandardMaterial3D
      {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = XrayColor,
        DepthTest = BaseMaterial3D.DepthTestEnum.Inverted,
        RenderPriority = 90
      },
      CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
      Visible = false
    };
    _mesh.AddChild (ghost);
    return ghost;
  }

  // "On fire" glow visible from across the map while on a 3+ streak (see issue #77).
  private void ApplyStreakGlow()
  {
    if (_streakLight == null) return;
    _streakLight.Visible = IsOnStreak;
    if (_nameTag != null) _nameTag.Modulate = IsOnStreak ? new Color (1.0f, 0.75f, 0.2f) : Colors.White;
  }

  private void FlashHitColor()
  {
    SetColor (HitColor);
    _hitRedTimer.Start();
  }

  // Golden crown for the current score leader (issue #89): each peer computes the
  // leader locally in World from the replicated Scores & toggles this; the crown
  // floats clearly above the name tag & slowly spins while worn (issue #107).
  private const float CrownNameTagSpacing = 0.6f;
  private Node3D? _crown;
  private Tween? _crownSpin;
  public bool IsCrowned => _crown is { Visible: true };

  public void SetCrowned (bool isCrowned)
  {
    _crown ??= GetNodeOrNull <Node3D> ("Crown");
    if (_crown == null || _crown.Visible == isCrowned) return;
    _crown.Visible = isCrowned;
    UpdateCrownSpin();
  }

  private void UpdateCrownSpin()
  {
    _crownSpin?.Kill();
    _crownSpin = null;
    if (_crown is not { Visible: true }) return;
    _crownSpin = CreateTween().SetLoops();
    _crownSpin.TweenProperty (_crown, "rotation:y", Mathf.Tau, 4.0).From (0.0f);
  }

  private void UpdateNameTag()
  {
    if (_nameTag == null) return;
    _nameTag.Text = _displayName;
  }

  private void UpdatePuppetTags()
  {
    if (IsMultiplayerAuthority() || _localPlayer == null || _nameTag == null) return;
    var distanceFromLocalPlayer = GlobalPosition.DistanceTo (_localPlayer.GlobalPosition);
    var scaleFactor = CalculateTagScaleFactor (distanceFromLocalPlayer);
    var healthTagMinWidthFactor = 0.8f;
    var healthTagWidthFactor = Mathf.Max (healthTagMinWidthFactor, 0.5f * scaleFactor);
    var originalHealthTagScale = new Vector3 (0.18f, 0.101f, 0.42f);
    var healthTagScaleFactor = new Vector3 (healthTagWidthFactor, 1.0f * scaleFactor, 0.5f * scaleFactor);
    var verticalOffset = scaleFactor * 0.2f;
    var t = (distanceFromLocalPlayer - TagScaleStartDistance) / (TagScaleStopDistance - TagScaleStartDistance);
    var tagSpacing = Mathf.Lerp (HealthTagNameTagMinSpacing, HealthTagNameTagMaxSpacing, Mathf.Clamp (t, 0.0f, 1.0f));
    _nameTag.Scale = Vector3.One * scaleFactor;
    _nameTag.Position = new Vector3 (_nameTag.Position.X, NameTagBaseHeight + verticalOffset, _nameTag.Position.Z);
    _healthTag.Scale = originalHealthTagScale * healthTagScaleFactor;
    _healthTag.Position = new Vector3 (_healthTag.Position.X, NameTagBaseHeight + verticalOffset - tagSpacing, _healthTag.Position.Z);
    UpdateCrownPlacement (scaleFactor, verticalOffset);
  }

  // The crown rides clearly above the name tag & scales with it, so the leader is
  // obvious at any distance instead of hiding behind/inside the tag (issue #107).
  private void UpdateCrownPlacement (float scaleFactor, float verticalOffset)
  {
    if (_crown == null) return;
    _crown.Scale = Vector3.One * scaleFactor;
    _crown.Position = new Vector3 (_crown.Position.X, NameTagBaseHeight + verticalOffset + CrownNameTagSpacing * scaleFactor, _crown.Position.Z);
  }

  private float CalculateTagScaleFactor (float distance)
  {
    if (distance <= TagScaleStartDistance) return MinNameTagScale;
    if (distance >= TagScaleStopDistance) return MaxNameTagScale;
    var t = (distance - TagScaleStartDistance) / (TagScaleStopDistance - TagScaleStartDistance);
    return Mathf.Lerp (MinNameTagScale, MaxNameTagScale, t);
  }
}
