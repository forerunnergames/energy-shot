using Godot;
using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.ui.hud;

namespace com.forerunnergames.energyshot.weapons;

// The blowgun's poison dart (issues #194 & #236): a swept-ray projectile that flies
// fast but VISIBLY - you can watch it cross the arena - with next to no drop, a long
// lifetime (it's the sniper), & a proper fletched dart body so it never reads as a
// green ball. The impact does no damage; the poison does (Player.Poison.cs). Stealth
// audio: the dart carries its own short-range whoosh, audible only to whoever it
// passes close to. A miss that hits geometry LANDS as an armed ground dart (#248).
public partial class BlowgunDart : Node3D
{
  [Signal] public delegate void HitPlayerEventHandler (Player player);
  [Signal] public delegate void LandedEventHandler (Vector3 position);
  public const float ShaftLength = 0.7f;
  private const float MaxLifetimeSeconds = 6.0f;
  private const float DropAcceleration = 0.3f; // Near-zero: a sniper line, not a lob.
  private static readonly Color DartBody = new(0.2f, 0.2f, 0.22f);
  private static readonly Color Tip = new(0.85f, 0.85f, 0.9f);
  private static readonly Color Fletch = new(1.0f, 0.25f, 0.15f);
  private Vector3 _velocity;
  private Vector3 _sweepStart;
  private bool _sweptFromStart;
  private bool _isLive;
  private float _age;
  private Godot.Collections.Array <Rid> _exclusions = new();

  public override void _Ready()
  {
    AddChild (CreateDartVisual());
    // The fly-by whoosh (issue #194): positional & short-range - hearing it means the
    // dart is near YOU. Looped for the flight, like the boomerang's (issue #98).
    var whoosh = new AudioStreamPlayer3D { Stream = ResourceLoader.Load <AudioStream> ("res://assets/sounds/dart-throw.mp3"), UnitSize = 2.0f, MaxDistance = 7.0f, Autoplay = true }; // Real dart whoosh (Aaron, 2026-08-22): Pixabay; still short-range.
    whoosh.Finished += () => whoosh.Play();
    AddChild (whoosh);
  }

  public void Launch (Vector3 origin, Vector3 sweepStart, Vector3 direction, float speed, bool isLive, CharacterBody3D shooter)
  {
    GlobalPosition = origin;
    _sweepStart = sweepStart;
    _velocity = direction.Normalized() * speed;
    _isLive = isLive;
    _exclusions = new Godot.Collections.Array <Rid> { shooter.GetRid() };
    if (shooter is Player own) _exclusions.Add (own.HeadRid);
    Orient();
  }

  public override void _PhysicsProcess (double delta)
  {
    var dt = (float)delta;
    _age += dt;

    if (_age > MaxLifetimeSeconds)
    {
      QueueFree();
      return;
    }

    var from = _sweptFromStart ? GlobalPosition : _sweepStart;
    _sweptFromStart = true;
    _velocity.Y -= DropAcceleration * dt;
    var to = GlobalPosition + _velocity * dt;
    var query = PhysicsRayQueryParameters3D.Create (from, to, exclude: _exclusions);
    query.HitFromInside = true;
    query.CollideWithAreas = true; // Heads are Area3D hitboxes (issue #179) - a dart to the dome still only poisons.
    var hit = GetWorld3D().DirectSpaceState.IntersectRay (query);

    if (hit.Count > 0)
    {
      ResolveHit (hit);
      return;
    }

    GlobalPosition = to;
    Orient();
  }

  // A player stops the dart & (on the live copy) gets poisoned; geometry stops it &
  // (on the live copy) the server is told where it landed, so it becomes an armed
  // ground dart instead of vanishing (issues #236 & #248).
  private void ResolveHit (Godot.Collections.Dictionary hit)
  {
    var collider = hit["collider"].AsGodotObject();
    var victim = collider is HeadHitbox head ? head.Player : collider as Player;
    if (victim != null && _isLive) EmitSignal (SignalName.HitPlayer, victim);
    if (victim == null && _isLive) EmitSignal (SignalName.Landed, (Vector3)hit["position"] + (Vector3)hit["normal"] * 0.2f);
    QueueFree();
  }

  private void Orient()
  {
    if (_velocity.LengthSquared() < 0.001f) return;
    LookAt (GlobalPosition + _velocity, Vector3.Up);
  }

  // Code-built, fresh materials per call (the SlingshotStone convention).

  // A real dart (issue #236): a long dark shaft along -Z (the flight direction after
  // LookAt), a bright steel tip in front, & three red fletching fins at the back.
  public static Node3D CreateDartVisual()
  {
    var root = new Node3D();
    root.AddChild (new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.022f, Height = ShaftLength }, RotationDegrees = new Vector3 (90.0f, 0.0f, 0.0f), MaterialOverride = new StandardMaterial3D { AlbedoColor = DartBody, Roughness = 0.6f } });
    root.AddChild (new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.0f, BottomRadius = 0.03f, Height = 0.12f }, Position = new Vector3 (0.0f, 0.0f, -ShaftLength / 2.0f - 0.06f), RotationDegrees = new Vector3 (-90.0f, 0.0f, 0.0f), MaterialOverride = new StandardMaterial3D { AlbedoColor = Tip, Metallic = 0.8f, Roughness = 0.2f } });
    for (var i = 0; i < 3; ++i)
    {
      var fin = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3 (0.01f, 0.11f, 0.16f) }, Position = new Vector3 (0.0f, 0.055f, ShaftLength / 2.0f - 0.1f), MaterialOverride = new StandardMaterial3D { AlbedoColor = Fletch, Roughness = 0.5f } };
      var pivot = new Node3D { RotationDegrees = new Vector3 (0.0f, 0.0f, 120.0f * i) };
      pivot.AddChild (fin);
      root.AddChild (pivot);
    }
    return root;
  }

  // The blowgun itself: a long tube with a scope - the whole joke.
  public static Node3D CreateBlowgunVisual()
  {
    var root = new Node3D();
    var wood = new StandardMaterial3D { AlbedoColor = new Color (0.45f, 0.3f, 0.15f), Roughness = 0.6f };
    root.AddChild (new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.045f, Height = 1.1f }, RotationDegrees = new Vector3 (90.0f, 0.0f, 0.0f), MaterialOverride = wood });
    var scope = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.03f, Height = 0.18f }, Position = new Vector3 (0.0f, 0.075f, -0.15f), RotationDegrees = new Vector3 (90.0f, 0.0f, 0.0f), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color (0.15f, 0.15f, 0.18f), Roughness = 0.3f } };
    root.AddChild (scope);
    scope.AddChild (new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.026f, BottomRadius = 0.026f, Height = 0.01f }, Position = new Vector3 (0.0f, 0.09f, 0.0f), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color (0.4f, 0.7f, 1.0f), EmissionEnabled = true, Emission = new Color (0.2f, 0.35f, 0.5f), Roughness = 0.1f } });
    return root;
  }
}
