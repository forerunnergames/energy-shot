using com.forerunnergames.energyshot.players;
using Godot;

namespace com.forerunnergames.energyshot.weapons;

// A slung stone (issue #99): flies a gravity arc whose speed & sting scale with how
// long the shooter drew the band - drawn longer = flatter, faster, & harder. Only the
// shooter's own stone is "live" (reports the hit); other peers fly visual-only copies,
// like BananaProjectile. Impacts are detected by sweeping a ray along the path
// traveled each physics frame, so fast stones can't tunnel. Built entirely from a
// primitive sphere & existing sounds - no downloaded assets.
public partial class SlingshotStone : Node3D
{
  [Export] public float GravityAcceleration = 24.0f;
  [Export] public float MaxLifetimeSeconds = 6.0f;
  [Signal] public delegate void HitPlayerEventHandler (Player victim, float energy);
  private static readonly Color StoneGray = new(0.55f, 0.55f, 0.58f);
  private static readonly Color FrameBrown = new(0.45f, 0.28f, 0.12f);
  private static readonly Color BandTan = new(0.85f, 0.72f, 0.35f);
  private Vector3 _velocity;
  private float _energy;
  private float _age;
  private bool _isLive;
  private Rid _shooterRid;

  // Shared look for the world pickup & the held model (issue #99): a simple Y-frame
  // slingshot built from primitive boxes - a wooden handle, two angled prongs, & a
  // band across the tips. Fresh materials per call so the first-person overlay tweak
  // (issue #124) can't bleed into pickups.
  public static Node3D CreateSlingshotVisual()
  {
    var wood = new StandardMaterial3D { AlbedoColor = FrameBrown, Roughness = 0.9f };
    var band = new StandardMaterial3D { AlbedoColor = BandTan, Roughness = 0.6f };
    var visual = new Node3D();
    visual.AddChild (new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3 (0.07f, 0.4f, 0.07f) }, MaterialOverride = wood });
    visual.AddChild (new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3 (0.06f, 0.3f, 0.06f) }, MaterialOverride = wood, Position = new Vector3 (-0.1f, 0.3f, 0.0f), RotationDegrees = new Vector3 (0.0f, 0.0f, 35.0f) });
    visual.AddChild (new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3 (0.06f, 0.3f, 0.06f) }, MaterialOverride = wood, Position = new Vector3 (0.1f, 0.3f, 0.0f), RotationDegrees = new Vector3 (0.0f, 0.0f, -35.0f) });
    visual.AddChild (new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3 (0.38f, 0.035f, 0.035f) }, MaterialOverride = band, Position = new Vector3 (0.0f, 0.43f, 0.0f) });
    return visual;
  }

  public override void _Ready()
  {
    AddChild (new MeshInstance3D
    {
      Mesh = new SphereMesh { Radius = 0.09f, Height = 0.18f },
      MaterialOverride = new StandardMaterial3D { AlbedoColor = StoneGray, Roughness = 0.8f }
    });
  }

  public void Launch (Vector3 origin, Vector3 direction, float speed, float energy, bool isLive, CharacterBody3D shooter)
  {
    GlobalPosition = origin;
    _velocity = direction.Normalized() * speed;
    _energy = energy;
    _isLive = isLive;
    _shooterRid = shooter.GetRid();
  }

  public override void _PhysicsProcess (double delta)
  {
    var dt = (float)delta;
    _age += dt;
    if (_age > MaxLifetimeSeconds) { QueueFree(); return; }
    var from = GlobalPosition;
    _velocity.Y -= GravityAcceleration * dt;
    var to = from + _velocity * dt;
    var query = PhysicsRayQueryParameters3D.Create (from, to, exclude: new Godot.Collections.Array <Rid> { _shooterRid });
    query.HitFromInside = true;
    var hit = GetWorld3D().DirectSpaceState.IntersectRay (query);

    if (hit.Count == 0)
    {
      GlobalPosition = to;
      return;
    }

    // First contact ends the flight (issue #99): a live stone that met a player
    // reports the hit; anything else just stops the stone.
    if (_isLive && hit["collider"].AsGodotObject() is Player victim) EmitSignal (SignalName.HitPlayer, victim, _energy);
    QueueFree();
  }
}
