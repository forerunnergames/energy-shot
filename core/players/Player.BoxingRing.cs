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
  [Export] public float RopeShovePerImpactSpeed = 0.6f;
  [Export] public float RopeBounceSeconds = 0.45f;
  [Export] public float HeadBounceVelocity = 26.0f;
  // The rope tops are trampolines too (issue #240): landing on one springs you up,
  // scaled by how fast you came down, & even a standing hop still bounces.
  [Export] public float RopeTopBounceMin = 14.0f;
  [Export] public float RopeTopBouncePerFallSpeed = 1.1f;
  // Each bounce climbs 10% higher than the last, but only up to here (issue #262):
  // from the floor it takes about ten in a row to reach the cap, then no higher.
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
    Velocity = new Vector3 (bounced.X, Velocity.Y, bounced.Z);
    _stickyFlightSecondsLeft = Mathf.Max (_stickyFlightSecondsLeft, RopeBounceSeconds);
    return true;
  }

  private bool TryRopeTopBounce()
  {
    var fallSpeed = Mathf.Max (0.0f, -_preMoveVelocity.Y);
    Velocity = new Vector3 (Velocity.X, Mathf.Clamp (fallSpeed * RopeTopBouncePerFallSpeed, RopeTopBounceMin, RopeTopBounceMax), Velocity.Z);
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
