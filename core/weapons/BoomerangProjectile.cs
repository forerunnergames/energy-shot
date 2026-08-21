using System.Collections.Generic;
using System.Linq;
using com.forerunnergames.energyshot.players;
using Godot;

namespace com.forerunnergames.energyshot.weapons;

// Thrown boomerang (issue #98): curves outbound up to ~25m (or until it meets level
// geometry), then homes straight back to the thrower, who auto-catches it on
// proximity. Only the thrower's own boomerang is "live" (reports hits, scoops,
// the catch, & losses); other peers fly visual-only copies, like BananaProjectile.
// Player hits en route are victim-authoritative; the return leg ignores geometry so
// the trip home always completes while the thrower lives. Built entirely from
// primitive boxes & an existing whiff sound - no downloaded assets.
public partial class BoomerangProjectile : Node3D
{
  [Export] public float Speed = 20.0f;
  [Export] public float OutboundMeters = 25.0f;
  [Export] public float CurveDegreesPerSecond = 40.0f;
  [Export] public float CatchRadiusMeters = 1.5f;
  [Export] public float ScoopRadiusMeters = 1.5f;
  [Export] public float MaxLifetimeSeconds = 12.0f;
  [Export] public float SpinRadiansPerSecond = 25.0f;
  [Signal] public delegate void HitPlayerEventHandler (Player victim);
  [Signal] public delegate void ScoopedPickupEventHandler (string pickupName);
  [Signal] public delegate void CaughtEventHandler();
  [Signal] public delegate void LostEventHandler (Vector3 position);
  // Players live on collision layer 2 (Player.tscn); the return leg sweeps only them.
  private const uint PlayersLayer = 2;
  private const float SurfaceClearance = 0.3f;
  private static readonly Color BoomerangOrange = new(1.0f, 0.55f, 0.1f);
  private readonly HashSet <int> _victimsHit = new();
  private readonly HashSet <string> _scoopedPickups = new();
  private Node3D _visual = null!;
  private Vector3 _direction = Vector3.Forward;
  private float _traveled;
  private float _age;
  private bool _isLive;
  private bool _returning;
  private Player? _thrower;
  private Rid _throwerRid;
  private Vector3 CatchPoint() => _thrower!.GlobalPosition + Vector3.Up * 1.2f;

  // Shared look for the projectile, the world pickup, & the held model (issue #98):
  // two bright crossed arms built from primitive boxes. Fresh materials per call so
  // the first-person overlay tweak (issue #124) can't bleed into pickups.
  // A curved boomerang (issue #245): a smooth swept V traced along a quadratic Bezier
  // from one rounded tip through the elbow to the other, built from short capsules so
  // every edge is round - no boxes, no hard angles. Shared by projectile, held model,
  // pickup, & the slingshot nock.
  public static Node3D CreateVisual()
  {
    var material = new StandardMaterial3D { AlbedoColor = BoomerangOrange, EmissionEnabled = true, Emission = BoomerangOrange * 0.6f, Roughness = 0.4f };
    var visual = new Node3D();
    var tipA = new Vector3 (0.62f, 0.0f, 0.0f);
    var elbow = new Vector3 (-0.08f, 0.0f, -0.08f);
    var tipB = new Vector3 (0.0f, 0.0f, 0.62f);
    const int segments = 10;
    var previous = Bezier (tipA, elbow, tipB, 0.0f);

    for (var i = 1; i <= segments; ++i)
    {
      var point = Bezier (tipA, elbow, tipB, (float)i / segments);
      var along = point - previous;
      var taper = 0.085f - 0.03f * Mathf.Abs ((float)i / segments - 0.5f) * 2.0f; // Fat elbow, slimmer tips.
      var capsule = new MeshInstance3D { Mesh = new CapsuleMesh { Radius = taper, Height = along.Length() + taper * 2.0f }, MaterialOverride = material, Position = (previous + point) / 2.0f };
      capsule.Basis = AlongY (along); // A capsule stands along Y; lay it along the segment (no LookAt: not in the tree yet).
      visual.AddChild (capsule);
      previous = point;
    }

    return visual;
  }

  private static Basis AlongY (Vector3 direction)
  {
    var y = direction.Normalized();
    var x = Vector3.Up.Cross (y);
    if (x.LengthSquared() < 0.001f) x = Vector3.Right;
    x = x.Normalized();
    return new Basis (x, y, x.Cross (y));
  }

  private static Vector3 Bezier (Vector3 a, Vector3 control, Vector3 b, float t) => a * (1.0f - t) * (1.0f - t) + control * 2.0f * (1.0f - t) * t + b * t * t;

  public override void _Ready()
  {
    _visual = CreateVisual();
    AddChild (_visual);
    AddWhooshLoop();
  }

  // Looping whoosh while airborne: the punch whiff replayed on a fast pitch reads as
  // a spinning throw - reusing an existing sound instead of downloading one (issue #98).
  private void AddWhooshLoop()
  {
    var whoosh = new AudioStreamPlayer3D { Stream = ResourceLoader.Load <AudioStream> ("res://assets/sounds/punch-whiff.wav"), PitchScale = 1.5f };
    AddChild (whoosh);
    whoosh.Finished += () => whoosh.Play();
    whoosh.Play();
  }

  public void Launch (Vector3 origin, Vector3 direction, bool isLive, Player thrower)
  {
    GlobalPosition = origin;
    _direction = direction.Normalized();
    _isLive = isLive;
    _thrower = thrower;
    _throwerRid = thrower.GetRid();
  }

  public override void _PhysicsProcess (double delta)
  {
    var dt = (float)delta;
    _age += dt;
    if (_thrower == null || !IsInstanceValid (_thrower)) { QueueFree(); return; } // Thrower gone (disconnect teardown).
    if (_age > MaxLifetimeSeconds) { EndLost(); return; }
    _visual.RotateY (SpinRadiansPerSecond * dt);
    UpdateDirection (dt);
    FlyStep (dt);
    if (_isLive) ScoopNearbyPickups();
    TryCatch();
  }

  private void UpdateDirection (float dt)
  {
    if (_returning) { _direction = (CatchPoint() - GlobalPosition).Normalized(); return; }
    _traveled += Speed * dt;
    if (_traveled >= OutboundMeters) { _returning = true; return; }
    _direction = _direction.Rotated (Vector3.Up, Mathf.DegToRad (CurveDegreesPerSecond) * dt); // The signature banked arc.
  }

  // Sweep the path traveled this frame, same as LaserBolt & BananaProjectile. A
  // player contact doesn't stop the flight - the boomerang clips them & carries on;
  // outbound geometry contact turns the flight around early.
  private void FlyStep (float dt)
  {
    var from = GlobalPosition;
    var to = from + _direction * Speed * dt;
    var hit = Sweep (from, to);

    if (hit.Count == 0)
    {
      GlobalPosition = to;
      return;
    }

    if (hit["collider"].AsGodotObject() is Player victim)
    {
      HandlePlayerHit (victim);
      GlobalPosition = to;
      return;
    }

    _returning = true;
    GlobalPosition = (Vector3)hit["position"] + (Vector3)hit["normal"] * SurfaceClearance;
  }

  private Godot.Collections.Dictionary Sweep (Vector3 from, Vector3 to)
  {
    var query = PhysicsRayQueryParameters3D.Create (from, to, exclude: new Godot.Collections.Array <Rid> { _throwerRid });
    if (_returning) query.CollisionMask = PlayersLayer; // The return leg passes through geometry so it always gets home.
    query.HitFromInside = true;
    return GetWorld3D().DirectSpaceState.IntersectRay (query);
  }

  // One report per victim per flight; only the live boomerang reports (issue #98).
  private void HandlePlayerHit (Player victim)
  {
    if (!_isLive || !_victimsHit.Add (victim.NetworkId)) return;
    EmitSignal (SignalName.HitPlayer, victim);
  }

  // Flying within reach of a world pickup grabs it (issue #98): the server despawns
  // it into escrow & the thrower collects the cargo on the catch.
  private void ScoopNearbyPickups()
  {
    foreach (var pickup in GetParent().GetChildren().OfType <WeaponPickup>())
    {
      if (pickup.GlobalPosition.DistanceTo (GlobalPosition) > ScoopRadiusMeters) continue;
      if (!_scoopedPickups.Add (pickup.Name)) continue;
      EmitSignal (SignalName.ScoopedPickup, pickup.Name.ToString());
    }
  }

  private void TryCatch()
  {
    if (!_returning || GlobalPosition.DistanceTo (CatchPoint()) > CatchRadiusMeters) return;
    if (_isLive) EmitSignal (SignalName.Caught);
    QueueFree();
  }

  // Safety net: a flight that somehow never completes drops the boomerang (& its
  // cargo) as pickups wherever it is, instead of orbiting forever (issue #98).
  private void EndLost()
  {
    if (_isLive) EmitSignal (SignalName.Lost, GlobalPosition);
    QueueFree();
  }
}
