using Godot;

namespace com.forerunnergames.energyshot.players;

// Fall damage (issue #263): a big drop hurts. The highest point since we last stood on
// something is remembered; landing from more than FallDamageFreeMeters below it costs
// health per extra metre - self-inflicted & ownerless, like a fall off the world, so
// nobody scores it. Spawn armor exempts the respawn drop-in from the spawn room, & the
// bounce cap (#262) keeps rope-bouncing from turning into a free ride up & a long way down.
public partial class Player
{
  // 36m, up from 10 (Aaron, 2026-08-23): jumping off the spawn platform (a ~34m
  // peak with the hop) must be free, & so must a jump plus one full-charge
  // rocket boost (~13m peak). Fall damage is for the truly spectacular drops.
  [Export] public float FallDamageFreeMeters = 36.0f;
  [Export] public float FallDamagePerMeter = 0.05f; // Energy per metre past the free drop: 0.05 = 5 health per metre.
  public const float MaxFallEnergy = 0.9f;
  private const float TeleportMeters = 6.0f; // More than any physics frame can move you.
  private float _fallPeakY;
  private Vector3 _lastFallSample;
  private bool _wasOnFloor = true;

  // Called after MoveAndSlide each physics frame: track the apex, settle the bill on landing.
  // A position that jumped further than physics could carry it in one frame is a
  // teleport (a respawn, the playtest driver) - no bill for a trip you didn't fall.
  private void UpdateFallDamage()
  {
    if (GlobalPosition.DistanceTo (_lastFallSample) > TeleportMeters) ResetFallTracking();
    _lastFallSample = GlobalPosition;
    var onFloor = IsOnFloor();
    if (!onFloor) _fallPeakY = Mathf.Max (_fallPeakY, GlobalPosition.Y);
    if (onFloor && !_wasOnFloor) LandFrom (_fallPeakY);
    if (onFloor) _fallPeakY = GlobalPosition.Y;
    _wasOnFloor = onFloor;
  }

  private void LandFrom (float peakY)
  {
    var drop = peakY - GlobalPosition.Y;
    if (drop <= FallDamageFreeMeters || SpawnArmor || Fallen) return;
    // Capped under the full-charge threshold: a drop is never an instant zap-out, it
    // wears you down (the 30m spawn-room drop-in costs 90 without armor).
    var energy = Mathf.Min (MaxFallEnergy, (drop - FallDamageFreeMeters) * FallDamagePerMeter);
    GD.Print ($"{DisplayName}: that was a {drop:0.#}m drop. Ouch.");
    LastDamageKind = DamageKind.None; // No special pool: a fatal drop reads as a plain zap by "a long drop".
    ApplyDamageFrom (0, energy, "a long drop", knockbackScale: 0.0f);
  }

  // A respawn lands us in the spawn room: no bill for that.
  private void ResetFallTracking()
  {
    _fallPeakY = GlobalPosition.Y;
    _lastFallSample = GlobalPosition;
    _wasOnFloor = true;
  }
}
