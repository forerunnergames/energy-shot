using Godot;

namespace com.forerunnergames.energyshot.players;

// The spawn box is a boxing ring (issue #174): its walls are rubber ropes that bounce
// you back the way you came - with a shove, so a punch into the ropes returns the
// victim to the puncher - & landing on another player's head springs you high.
// The room's spawn-protection job (spawn armor, layout) is untouched.
public partial class Player
{
  // Much bouncier (issue #240, Caleb): jumping into the ropes should fling you hard
  // enough to nearly knock you out of the ring - restitution over 1 & a shove that
  // scales with how fast you hit them.
  [Export] public float RopeRestitution = 1.3f;
  [Export] public float RopeShove = 9.0f;
  // The chain killer (issue #276): restitution 1.3 + the shove grows every wall-to-
  // wall rally ~1.9x per hit - 7 m/s walking becomes 150+ m/s in four bounces, then
  // clean through a 0.3m wall or over the top into the void. The cap keeps any legit
  // hit violently bouncy (20 m/s crosses the ring in ~0.6s) while the chain converges.
  [Export] public float RopeExitSpeedMax = 20.0f;
  [Export] public float RopeShovePerImpactSpeed = 0.6f;
  [Export] public float RopeBounceSeconds = 0.45f;
  [Export] public float HeadBounceVelocity = 26.0f;
  // The rope tops are trampolines too (issue #240): landing on one springs you up,
  // scaled by how fast you came down, & even a standing hop still bounces.
  // Growing trampoline, capped (Aaron, 2026-08-22: bouncing SHOULD gain height each
  // jump - about ten jumps to the ceiling, like the original - the overcorrection
  // was killing the fun). Each bounce returns 1.1x the landing speed up to the cap;
  // what stays dead is the old 14 m/s FLOOR, which let a passive body loop forever -
  // gentle landings (under the stand threshold) simply stand on the rope.
  [Export] public float RopeTopMinTrampolineFallSpeed = 6.0f;
  [Export] public float RopeTopBouncePerFallSpeed = 1.1f;
  // The hardest single rebound (issue #262): dives cap here, & every rebound damps
  // by RopeTopBouncePerFallSpeed, so chains only ever LOSE height (issue #276).
  [Export] public float RopeTopBounceMax = 34.0f;
  private const float MinRopeImpactSpeed = 1.0f;
  private Vector3 _preMoveVelocity;

  // The invisible backstops (issue #276) bounce exactly like the ropes they guard.
  private bool IsRope (GodotObject? collider) => collider is Node3D node && node is CsgBox3D or StaticBody3D && node.GetParent() == _spawnRoom && node.Name.ToString().StartsWith ("Wall");

  // Ropes (issue #174): reflect the velocity we arrived with (MoveAndSlide already
  // scrubbed the into-wall part from Velocity), scale by restitution, add a shove
  // along the rope's normal, & let momentum own the ride briefly - the same
  // "launched" flag the sticky banana uses, so Move() doesn't brake the bounce.
  private bool TryRopeBounce (KinematicCollision3D collision)
  {
    if (!IsRope (collision.GetCollider())) return false;
    var normal = collision.GetNormal();
    if (normal.Y > 0.5f) return TryRopeTopBounce();
    var impact = -_preMoveVelocity.Dot (normal);
    if (impact < MinRopeImpactSpeed) return false; // Leaning on it, not hitting it.
    var bounced = _preMoveVelocity.Bounce (normal) * RopeRestitution + normal * (RopeShove + impact * RopeShovePerImpactSpeed);
    var flat = new Vector3 (bounced.X, 0.0f, bounced.Z);
    if (flat.Length() > RopeExitSpeedMax) flat = flat.Normalized() * RopeExitSpeedMax; // The rally converges (issue #276).
    Velocity = new Vector3 (flat.X, Velocity.Y, flat.Z);
    _stickyFlightSecondsLeft = Mathf.Max (_stickyFlightSecondsLeft, RopeBounceSeconds);
    return true;
  }

  private bool TryRopeTopBounce()
  {
    var fallSpeed = Mathf.Max (0.0f, -_preMoveVelocity.Y);
    if (fallSpeed < RopeTopMinTrampolineFallSpeed) return false; // A gentle landing STANDS on the rope - no self-feeding loop.
    Velocity = new Vector3 (Velocity.X, Mathf.Min (fallSpeed * RopeTopBouncePerFallSpeed, RopeTopBounceMax), Velocity.Z);
    _jumpSound.Play();
    return true;
  }

  // Head bounce (issue #174): landing on top of another player springs you up, higher
  // than a jump. Only the one on top bounces; the trampoline feels nothing.
  private bool TryHeadBounce (KinematicCollision3D collision)
  {
    if (collision.GetCollider() is not Player || collision.GetNormal().Y < 0.7f) return false;
    Velocity = new Vector3 (Velocity.X, HeadBounceVelocity, Velocity.Z);
    _jumpSound.Play();
    return true;
  }
}
