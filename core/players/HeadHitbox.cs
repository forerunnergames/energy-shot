using Godot;

namespace com.forerunnergames.energyshot.players;

// The floating sensor dome (issue #179): an Area3D child of the player on its own
// collision layer, so projectiles that sweep with CollideWithAreas can tell a head
// hit from a body hit - & movement never feels it, since it isn't part of the
// CharacterBody3D's shapes. Every peer renders it; replication rides the player's
// existing pose sync because it simply moves with the body.
public partial class HeadHitbox : Area3D
{
  public const uint Layer = 1u << 7; // Layer 8: nothing else lives there.
  public const float Radius = 0.3f;
  public static readonly Vector3 LocalOffset = new(0.0f, 2.45f, 0.0f); // Hovering just above the 2m capsule.

  public Player Player => (Player)GetParent();

  public static HeadHitbox Create()
  {
    var head = new HeadHitbox { Name = "Head", Position = LocalOffset, CollisionLayer = Layer, CollisionMask = 0, Monitoring = false, Monitorable = true };
    head.AddChild (new CollisionShape3D { Shape = new SphereShape3D { Radius = Radius } });
    return head;
  }
}
