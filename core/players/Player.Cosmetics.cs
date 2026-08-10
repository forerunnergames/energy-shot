using Godot;

namespace com.forerunnergames.energyshot.players;

// Cosmetics: body colors & hit flash, plus name/health tag text & distance scaling.
public partial class Player
{
  private static readonly Color NormalColor = new("0027ff");
  private static readonly Color HitColor = Colors.DarkRed;
  // White glow blended over the player's color, so armored players stay identifiable.
  private static readonly Color SpawnArmorColor = NormalColor.Lerp (Colors.White, 0.65f);
  private void SetColor (Color color) => (_mesh.GetSurfaceOverrideMaterial (0) as StandardMaterial3D)!.AlbedoColor = color;
  // Restores the player's resting color: white glow while spawn armor holds, normal blue otherwise.
  private void RestoreBaseColor() => SetColor (SpawnArmor ? SpawnArmorColor : NormalColor);

  private void FlashHitColor()
  {
    SetColor (HitColor);
    _hitRedTimer.Start();
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
  }

  private float CalculateTagScaleFactor (float distance)
  {
    if (distance <= TagScaleStartDistance) return MinNameTagScale;
    if (distance >= TagScaleStopDistance) return MaxNameTagScale;
    var t = (distance - TagScaleStartDistance) / (TagScaleStopDistance - TagScaleStartDistance);
    return Mathf.Lerp (MinNameTagScale, MaxNameTagScale, t);
  }
}
