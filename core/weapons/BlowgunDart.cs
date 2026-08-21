using Godot;
using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.ui.hud;

namespace com.forerunnergames.energyshot.weapons;

// The blowgun's poison dart (issue #194): a small, quiet, swept-ray projectile
// modeled on LaserBolt. The impact does no damage - the poison ticks do (see
// Player.Poison.cs). Stealth audio model: the dart carries its own whoosh with a
// tiny audible radius, so players only hear it when it flies near them; the muzzle
// pfft lives on the shooter with an even smaller radius (Player.Blowgun.cs).
public partial class BlowgunDart : Node3D
{
  [Signal] public delegate void HitPlayerEventHandler (Player player);
  private const float MaxLifetimeSeconds = 3.0f;
  // Gentle arc: a dart is breath-powered, not a laser - the droop reads as funny.
  private const float DropAcceleration = 6.0f;
  private static readonly Color DartBody = new(0.35f, 0.25f, 0.15f);
  private static readonly Color TuftGreen = new(0.4f, 0.8f, 0.3f);
  private Vector3 _velocity;
  private Vector3 _sweepStart;
  private bool _sweptFromStart;
  private bool _isLive;
  private float _age;
  private Godot.Collections.Array <Rid> _exclusions = new();

  public override void _Ready()
  {
    AddChild (CreateDartVisual());
    // The fly-by whoosh (issue #194): positional & short-range, so it IS the stealth
    // model - audible only to whoever the dart passes close to. Looped for the
    // dart's whole flight, like the boomerang's whoosh (issue #98).
    var whoosh = new AudioStreamPlayer3D { Stream = ProceduralSounds.DartWhoosh(), UnitSize = 1.5f, MaxDistance = 5.0f, Autoplay = true };
    whoosh.Finished += () => whoosh.Play();
    AddChild (whoosh);
  }

  // sweepStart is the shooter's camera position, same reasoning as LaserBolt (issue
  // #112): the first sweep covers camera->muzzle so point-blank walls can't be skipped.
  public void Launch (Vector3 origin, Vector3 sweepStart, Vector3 direction, float speed, bool isLive, CharacterBody3D shooter)
  {
    GlobalPosition = origin;
    _sweepStart = sweepStart;
    _velocity = direction.Normalized() * speed;
    _isLive = isLive;
    _exclusions = new Godot.Collections.Array <Rid> { shooter.GetRid() };
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
    // Point-blank darts can start inside the target's collider (issue #52's lesson).
    query.HitFromInside = true;
    var hit = GetWorld3D().DirectSpaceState.IntersectRay (query);

    if (hit.Count > 0)
    {
      ResolveHit (hit);
      return;
    }

    GlobalPosition = to;
    Orient();
  }

  // Players stop the dart & (on the shooter's live copy) get poisoned; geometry just
  // stops it - a dart in a wall is spent, no pierce, no pickup (only darts embedded
  // in a player fall out on death & become loadable, issue #194).
  private void ResolveHit (Godot.Collections.Dictionary hit)
  {
    if (_isLive && hit["collider"].AsGodotObject() is CharacterBody3D player && player is Player victim) EmitSignal (SignalName.HitPlayer, victim);
    QueueFree();
  }

  private void Orient()
  {
    if (_velocity.LengthSquared() < 0.001f) return;
    LookAt (GlobalPosition + _velocity, Vector3.Up);
  }

  // Code-built visuals, fresh materials per call so overlay tweaks can't bleed
  // between held models, pickups, & projectiles (the SlingshotStone convention).

  // The dart: a thin shaft with a bright tuft so victims can spot their pincushion.
  public static Node3D CreateDartVisual()
  {
    var root = new Node3D();
    root.AddChild (new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.012f, Height = 0.28f }, RotationDegrees = new Vector3 (90.0f, 0.0f, 0.0f), MaterialOverride = new StandardMaterial3D { AlbedoColor = DartBody, Roughness = 0.7f } });
    root.AddChild (new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.03f, Height = 0.06f }, Position = new Vector3 (0.0f, 0.0f, 0.16f), MaterialOverride = new StandardMaterial3D { AlbedoColor = TuftGreen, Roughness = 0.4f } });
    return root;
  }

  // The blowgun itself: a long tube - with a scope, which is the whole joke.
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
