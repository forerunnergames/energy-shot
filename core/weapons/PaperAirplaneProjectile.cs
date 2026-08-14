using com.forerunnergames.energyshot.players;
using Godot;

namespace com.forerunnergames.energyshot.weapons;

// Thrown paper airplane (issue #102): a slow, floaty glider that locks onto the
// player under the crosshair at throw time & banks gently toward them - dodgeable
// but persistent. With no target it glides straight & slowly sinks. After the glide
// window it loses lift & flutters down; wherever the flight ends (a player hit,
// level geometry, or the flutter reaching ground), it lands as a pickup. Only the
// thrower's own airplane is "live" (reports hits & landings); other peers fly
// visual-only copies, like BoomerangProjectile. Built entirely from primitive
// meshes & an existing whiff sound - no downloaded assets.
public partial class PaperAirplaneProjectile : Node3D
{
  [Export] public float Speed = 10.5f;
  // Fast enough to actually hold a turn: at 10.5 m/s, 65 deg/s couldn't sustain a
  // circle inside ~10m, so the glider physically could not follow anyone who moved
  // (issue #210). Still well short of a missile - sprinting away breaks it.
  [Export] public float TurnDegreesPerSecond = 115.0f;
  // How far ahead of a moving target the glider aims (issue #210).
  [Export] public float MaxLeadSeconds = 1.2f;
  [Export] public float MaxGlideSeconds = 10.0f;
  [Export] public float FlutterSpeed = 4.5f;
  [Export] public float MaxLifetimeSeconds = 16.0f;
  [Export] public float GlideSinkRate = 0.35f;
  [Signal] public delegate void HitPlayerEventHandler (Player victim);
  [Signal] public delegate void LandedEventHandler (Vector3 position);
  private const float SurfaceClearance = 0.2f;
  private const float WobbleRadians = 0.12f;
  private const float WobbleHertz = 1.6f;
  private static readonly Color PaperWhite = new(0.93f, 0.95f, 1.0f);
  private Node3D _visual = null!;
  private Vector3 _direction = Vector3.Forward;
  private float _age;
  private bool _isLive;
  private bool _flightEnded;
  private Player? _thrower;
  private Player? _target;
  private Rid _throwerRid;
  public int ThrowerNetworkId => _thrower?.NetworkId ?? 0;
  public Player? Thrower => _thrower;
  // How far out the locked target's warning ring starts filling in (issue #191).
  [Export] public float WarningRangeMeters = 30.0f;

  // 0 = far away (or not locked onto this player at all), 1 = about to hit. Drives
  // the targeted player's ring thickness, brightness, blink rate, & beep rate; every
  // other player's HUD always reads 0 from their own node (issue #191).
  public float ThreatFractionFor (Player player)
  {
    if (_target != player || !HasLiveTarget) return 0.0f;
    return Mathf.Clamp (1.0f - GlobalPosition.DistanceTo (TargetPoint()) / WarningRangeMeters, 0.0f, 1.0f);
  }
  private bool IsFluttering => _age > MaxGlideSeconds;
  private bool HasLiveTarget => _target != null && IsInstanceValid (_target) && _target.IsInsideTree();
  // Aim where they're GOING, not where they are (issue #210): pure pursuit trails a
  // moving player & needs an ever-tighter turn as it closes, so the glider swung wide
  // & sailed past. Leading keeps it dodgeable - CHANGING direction still breaks the
  // intercept - while a player who just runs in a straight line gets found.
  private Vector3 TargetPoint()
  {
    var body = _target!.GlobalPosition + Vector3.Up;
    var secondsToImpact = Mathf.Min (GlobalPosition.DistanceTo (body) / Speed, MaxLeadSeconds);
    var drift = new Vector3 (_target.Velocity.X, 0.0f, _target.Velocity.Z) * secondsToImpact;
    return body + drift;
  }

  // Shared look for the projectile, the world pickup, & the held model (issue #102):
  // a folded paper dart - a flat delta wing in two dihedral halves over a thin keel,
  // white with a subtle cool tint. Fresh materials per call so the first-person
  // overlay tweak (issue #124) can't bleed into pickups.
  public static Node3D CreateVisual()
  {
    var paper = new StandardMaterial3D { AlbedoColor = PaperWhite, Roughness = 0.9f, EmissionEnabled = true, Emission = PaperWhite * 0.15f };
    var visual = new Node3D();
    visual.AddChild (new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3 (0.02f, 0.1f, 0.5f) }, MaterialOverride = paper, Position = new Vector3 (0.0f, -0.05f, 0.05f) });
    visual.AddChild (WingHalf (paper, isLeft: true));
    visual.AddChild (WingHalf (paper, isLeft: false));
    return visual;
  }

  // One flat triangular half-wing: a paper-thin right-angled prism laid horizontal,
  // apex forward, rolled slightly upward for the folded dart's dihedral.
  private static MeshInstance3D WingHalf (StandardMaterial3D paper, bool isLeft)
  {
    var mesh = new PrismMesh { Size = new Vector3 (0.28f, 0.6f, 0.012f), LeftToRight = isLeft ? 1.0f : 0.0f };
    var roll = isLeft ? -8.0f : 8.0f;
    return new MeshInstance3D { Mesh = mesh, MaterialOverride = paper, Position = new Vector3 (isLeft ? -0.14f : 0.14f, 0.0f, 0.0f), RotationDegrees = new Vector3 (-90.0f, 0.0f, roll) };
  }

  public override void _Ready()
  {
    _visual = CreateVisual();
    AddChild (_visual);
    AddWhooshLoop();
  }

  // Soft looping whoosh while airborne: the punch whiff replayed high & quiet reads
  // as paper cutting air - reusing an existing sound instead of downloading one (issue #102).
  private void AddWhooshLoop()
  {
    var whoosh = new AudioStreamPlayer3D { Stream = ResourceLoader.Load <AudioStream> ("res://assets/sounds/punch-whiff.wav"), PitchScale = 1.9f, VolumeDb = -6.0f };
    AddChild (whoosh);
    whoosh.Finished += () => whoosh.Play();
    whoosh.Play();
  }

  public void Launch (Vector3 origin, Vector3 direction, bool isLive, Player thrower, Player? target)
  {
    GlobalPosition = origin;
    _direction = direction.Normalized();
    _isLive = isLive;
    _thrower = thrower;
    _throwerRid = thrower.GetRid();
    _target = target;
  }

  public override void _PhysicsProcess (double delta)
  {
    var dt = (float)delta;
    _age += dt;
    if (_thrower == null || !IsInstanceValid (_thrower)) { QueueFree(); return; } // Thrower gone (disconnect teardown).
    if (_age > MaxLifetimeSeconds) { EndLanded (GlobalPosition); return; } // Safety net: never orbit forever.
    UpdateDirection (dt);
    FlyStep (dt);
    // An explicit flag, not IsInsideTree (CodeRabbit): QueueFree defers removal to
    // the end of the frame, so the old guard was still true on a landed airplane.
    if (_flightEnded) return;
    FaceDirection();
    ApplyFlutterWobble();
  }

  // The signature gentle homing (issue #102): bank toward the locked target at a
  // capped turn rate - slow enough that a sprinting, weaving player escapes, but a
  // slow one gets found. Out of lift, it noses down & flutters; with no target it
  // glides straight & slowly sinks.
  private void UpdateDirection (float dt)
  {
    if (IsFluttering) { TurnToward (Vector3.Down, dt); return; }
    if (HasLiveTarget) { TurnToward ((TargetPoint() - GlobalPosition).Normalized(), dt); return; }
    _direction = (_direction + Vector3.Down * GlideSinkRate * dt).Normalized();
  }

  private void TurnToward (Vector3 desired, float dt)
  {
    var angle = _direction.AngleTo (desired);
    if (angle < 0.001f) return;
    var axis = _direction.Cross (desired);
    if (axis.LengthSquared() < 0.000001f) axis = Vector3.Up; // Anti-parallel edge case.
    var step = Mathf.Min (angle, Mathf.DegToRad (TurnDegreesPerSecond) * dt);
    _direction = _direction.Rotated (axis.Normalized(), step).Normalized();
  }

  // Sweep the path traveled this frame, same as LaserBolt & BoomerangProjectile: a
  // zero-width ray for level geometry, where a hair of precision doesn't matter.
  // First contact ends the flight: a live airplane that met a player reports the hit
  // (issue #102); either way it lands as a pickup where the flight stopped.
  //
  // Players get a SWEPT SPHERE instead (CodeRabbit): the airplane has real width & a
  // dodging target moves between physics samples, so the centreline ray let clean
  // hits slip past a shoulder. The sweep is checked first, so a player standing just
  // in front of a wall is still hit rather than the wall behind them.
  private void FlyStep (float dt)
  {
    var speed = IsFluttering ? FlutterSpeed : Speed;
    var from = GlobalPosition;
    var to = from + _direction * speed * dt;
    var victim = SweepForPlayer (from, to);

    if (victim != null)
    {
      if (_isLive) EmitSignal (SignalName.HitPlayer, victim);
      EndLanded (from); // Lands as a pickup near the impact (issue #102).
      return;
    }

    var query = PhysicsRayQueryParameters3D.Create (from, to, exclude: new Godot.Collections.Array <Rid> { _throwerRid });
    query.HitFromInside = true;
    var hit = GetWorld3D().DirectSpaceState.IntersectRay (query);

    if (hit.Count == 0)
    {
      GlobalPosition = to;
      return;
    }

    EndLanded ((Vector3)hit["position"] + (Vector3)hit["normal"] * SurfaceClearance);
  }

  // Players live on collision layer 2 (Player.tscn), so the sweep only ever finds
  // them - level geometry keeps its own ray above.
  private const uint PlayersLayer = 2;
  private static readonly SphereShape3D SweepShape = new() { Radius = 0.3f };

  private Player? SweepForPlayer (Vector3 from, Vector3 to)
  {
    var space = GetWorld3D().DirectSpaceState;
    var query = new PhysicsShapeQueryParameters3D
    {
      Shape = SweepShape,
      Transform = new Transform3D (Basis.Identity, from),
      Motion = to - from,
      CollisionMask = PlayersLayer,
      Exclude = new Godot.Collections.Array <Rid> { _throwerRid }
    };

    // The canonical Godot sweep: CastMotion finds how far along the step the shape
    // gets before it would touch something, & IntersectShape at that spot says what.
    var fractions = space.CastMotion (query);
    if (fractions.Length < 2 || fractions[1] >= 1.0f) return null;
    query.Transform = new Transform3D (Basis.Identity, from + query.Motion * fractions[1]);

    foreach (var found in space.IntersectShape (query, maxResults: 8))
    {
      if (found["collider"].AsGodotObject() is Player player) return player;
    }

    return null;
  }

  private void FaceDirection()
  {
    if (_direction.Cross (Vector3.Up).LengthSquared() < 0.000001f) return; // Straight up/down: keep the last heading.
    LookAt (GlobalPosition + _direction, Vector3.Up);
  }

  // A gentle papery roll wobble, faster & wilder once the lift is gone (issue #102).
  private void ApplyFlutterWobble()
  {
    var intensity = IsFluttering ? 3.0f : 1.0f;
    _visual.Rotation = new Vector3 (0.0f, 0.0f, Mathf.Sin (_age * Mathf.Tau * WobbleHertz * intensity) * WobbleRadians * intensity);
  }

  // Only the live airplane reports the landing; the thrower turns it into a
  // ray-grounded pickup via the server's validated drop path (issues #151 & #167).
  private void EndLanded (Vector3 position)
  {
    _flightEnded = true;
    if (_isLive) EmitSignal (SignalName.Landed, position);
    QueueFree();
  }
}
