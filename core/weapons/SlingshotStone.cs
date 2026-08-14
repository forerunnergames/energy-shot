using com.forerunnergames.energyshot.players;
using Godot;

namespace com.forerunnergames.energyshot.weapons;

// A slung stone (issue #99): flies a gravity arc whose speed & sting scale with how
// long the shooter drew the band - drawn longer = flatter, faster, & harder (the
// shooter passes a draw-scaled gravity, issue #163). Only the shooter's own stone is
// "live" (reports the hit); other peers fly visual-only copies, like BananaProjectile.
// Impacts are detected by sweeping a ray along the path traveled each physics frame,
// so fast stones can't tunnel; the first frame sweeps from the camera, so a wall
// closer than the muzzle offset can't be skipped either (issues #112 & #163). Built
// entirely from primitive meshes & existing sounds - no downloaded assets.
public partial class SlingshotStone : Node3D
{
  [Export] public float GravityAcceleration = 24.0f;
  // Doubled from 6 (issue #163): stones fly their full arc & only despawn on impact
  // or well past relevance.
  [Export] public float MaxLifetimeSeconds = 12.0f;
  [Signal] public delegate void HitPlayerEventHandler (Player victim, float energy);
  private static readonly Color StoneGray = new(0.55f, 0.55f, 0.58f);
  private static readonly Color FrameBrown = new(0.45f, 0.28f, 0.12f);
  private static readonly Color BandTan = new(0.85f, 0.72f, 0.35f);
  // Prong-tip band anchors & the pouch's rest spot, in the visual's local space; the
  // pouch (with the nocked stone) pulls straight back with the draw (issue #163).
  private static readonly Vector3 BandTipLeft = new(-0.19f, 0.42f, 0.0f);
  private static readonly Vector3 BandTipRight = new(0.19f, 0.42f, 0.0f);
  private static readonly Vector3 PouchRest = new(0.0f, 0.42f, 0.03f);
  private const float PouchPullMeters = 0.4f;
  private Vector3 _velocity;
  private Vector3 _sweepStart;
  private bool _sweptFromStart;
  private float _energy;
  private float _age;
  private bool _isLive;
  private Rid _shooterRid;

  // Shared look for the world pickup & the held model (issue #99): a simple Y-frame
  // slingshot built from primitive boxes - a wooden handle, two angled prongs, & a
  // two-half band running from the prong tips to a pouch holding a nocked stone, so
  // the held model can stretch the band back with the draw (issue #163). Fresh
  // materials per call so the first-person overlay tweak (issue #124) can't bleed
  // into pickups.
  public static Node3D CreateSlingshotVisual()
  {
    var wood = new StandardMaterial3D { AlbedoColor = FrameBrown, Roughness = 0.9f };
    var band = new StandardMaterial3D { AlbedoColor = BandTan, Roughness = 0.6f };
    var stone = new StandardMaterial3D { AlbedoColor = StoneGray, Roughness = 0.8f };
    var visual = new Node3D();
    visual.AddChild (new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3 (0.07f, 0.4f, 0.07f) }, MaterialOverride = wood });
    visual.AddChild (new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3 (0.06f, 0.3f, 0.06f) }, MaterialOverride = wood, Position = new Vector3 (-0.1f, 0.3f, 0.0f), RotationDegrees = new Vector3 (0.0f, 0.0f, 35.0f) });
    visual.AddChild (new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3 (0.06f, 0.3f, 0.06f) }, MaterialOverride = wood, Position = new Vector3 (0.1f, 0.3f, 0.0f), RotationDegrees = new Vector3 (0.0f, 0.0f, -35.0f) });
    visual.AddChild (new MeshInstance3D { Name = "BandLeft", Mesh = new BoxMesh { Size = new Vector3 (0.035f, 0.035f, 1.0f) }, MaterialOverride = band });
    visual.AddChild (new MeshInstance3D { Name = "BandRight", Mesh = new BoxMesh { Size = new Vector3 (0.035f, 0.035f, 1.0f) }, MaterialOverride = band });
    visual.AddChild (new MeshInstance3D { Name = "NockedStone", Mesh = new SphereMesh { Radius = 0.05f, Height = 0.1f }, MaterialOverride = stone });
    PoseBand (visual, 0.0f);
    return visual;
  }

  // Draw pose for the shared visual (issue #163): the pouch & nocked stone pull back
  // toward the eye with the draw, & both band halves stretch from the prong tips to
  // meet them; drawFraction 0 is the relaxed rest pose (used by pickups & on release,
  // so the band visibly snaps forward).
  public static void PoseBand (Node3D visual, float drawFraction)
  {
    var pouch = PouchRest + Vector3.Back * (PouchPullMeters * drawFraction);
    visual.GetNode <Node3D> ("NockedStone").Position = pouch;
    StretchBandSegment (visual.GetNode <MeshInstance3D> ("BandLeft"), BandTipLeft, pouch);
    StretchBandSegment (visual.GetNode <MeshInstance3D> ("BandRight"), BandTipRight, pouch);
  }

  // Orients & scales a unit-length band box to connect two local-space points.
  private static void StretchBandSegment (MeshInstance3D segment, Vector3 from, Vector3 to)
  {
    var span = to - from;
    segment.Position = (from + to) * 0.5f;
    segment.Basis = Basis.LookingAt (span.Normalized(), Vector3.Up) * Basis.FromScale (new Vector3 (1.0f, 1.0f, span.Length()));
  }

  public override void _Ready()
  {
    AddChild (new MeshInstance3D
    {
      Mesh = new SphereMesh { Radius = 0.09f, Height = 0.18f },
      MaterialOverride = new StandardMaterial3D { AlbedoColor = StoneGray, Roughness = 0.8f }
    });
  }

  // sweepStart is the shooter's camera position: the stone spawns at the muzzle, but
  // the first sweep covers camera->muzzle too, so a wall closer than the muzzle
  // offset can't be skipped (issues #112 & #163).
  public void Launch (Vector3 origin, Vector3 sweepStart, Vector3 direction, float speed, float gravity, float energy, bool isLive, CharacterBody3D shooter)
  {
    GlobalPosition = origin;
    _sweepStart = sweepStart;
    _velocity = direction.Normalized() * speed;
    GravityAcceleration = gravity; // Draw-scaled (issue #163): full draws fly flatter arcs.
    _energy = energy;
    _isLive = isLive;
    _shooterRid = shooter.GetRid();
  }

  public override void _PhysicsProcess (double delta)
  {
    var dt = (float)delta;
    _age += dt;
    if (_age > MaxLifetimeSeconds) { QueueFree(); return; }
    var from = _sweptFromStart ? GlobalPosition : _sweepStart;
    _sweptFromStart = true;
    _velocity.Y -= GravityAcceleration * dt;
    var to = GlobalPosition + _velocity * dt;
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
