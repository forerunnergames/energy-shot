using Godot;

namespace com.forerunnergames.energyshot.players;

// The spawn box is a boxing ring (issue #174): its walls are rubber ropes that bounce
// you back the way you came - with a shove, so a punch into the ropes returns the
// victim to the puncher - & landing on another player's head springs you high.
// The room's spawn-protection job (spawn armor, layout) is untouched.
public partial class Player
{
  [Export] public float RopeRestitution = 0.9f;
  [Export] public float RopeShove = 6.0f;
  [Export] public float RopeBounceSeconds = 0.35f;
  [Export] public float HeadBounceVelocity = 26.0f;
  private const float MinRopeImpactSpeed = 1.0f;
  private Vector3 _preMoveVelocity;

  private bool IsRope (GodotObject? collider) => collider is CsgBox3D box && box.GetParent() == _spawnRoom && box.Name.ToString().StartsWith ("Wall");

  // Ropes (issue #174): reflect the velocity we arrived with (MoveAndSlide already
  // scrubbed the into-wall part from Velocity), scale by restitution, add a shove
  // along the rope's normal, & let momentum own the ride briefly - the same
  // "launched" flag the sticky banana uses, so Move() doesn't brake the bounce.
  private bool TryRopeBounce (KinematicCollision3D collision)
  {
    if (!IsRope (collision.GetCollider())) return false;
    var normal = collision.GetNormal();
    if (Mathf.Abs (normal.Y) > 0.5f) return false; // The top edge isn't a rope.
    if (-_preMoveVelocity.Dot (normal) < MinRopeImpactSpeed) return false; // Leaning on it, not hitting it.
    var bounced = _preMoveVelocity.Bounce (normal) * RopeRestitution + normal * RopeShove;
    Velocity = new Vector3 (bounced.X, Velocity.Y, bounced.Z);
    _stickyFlightSecondsLeft = Mathf.Max (_stickyFlightSecondsLeft, RopeBounceSeconds);
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
