using com.forerunnergames.energyshot.players;
using Godot;

namespace com.forerunnergames.energyshot.weapons;

// A lobbed banana grenade (issues #61, #70, & #83): launched fast on a proper gravity
// arc, it bounces & slides off surfaces (dampened) with a short fuse lit by first
// contact, sticks to players it directly hits, & shakes every nearby camera when it
// blows. Only the shooter's own banana is "live" (reports blasts & sticks); other
// peers spawn visual-only copies. Impacts are detected by sweeping a ray along the
// path traveled each physics frame, same as LaserBolt.
public partial class BananaProjectile : Node3D
{
  [Export] public float Speed = 40.0f;
  [Export] public float GravityAcceleration = 24.0f;
  [Export] public float MaxLifetimeSeconds = 8.0f;
  [Export] public float FuseSeconds = 1.2f;
  // Split bounce (issue #235, Caleb): the banana is a banana, not a rubber ball. The
  // part of its speed going INTO a surface mostly dies (a little hop), while the part
  // sliding ALONG it keeps most of its speed - so a straight-down landing plops & a
  // glancing one skids.
  [Export] public float Restitution = 0.18f;
  [Export] public float SlideRetention = 0.85f;
  [Export] public float SpinRadiansPerSecond = 10.0f;
  private const float FlashRadius = 6.0f;
  private const float FlashSeconds = 0.4f;
  // Rest height (issue #132): the tumbling mesh sweeps a ~0.27m radius around the
  // node origin, so bounces & rolls keep the origin this far off the surface - the
  // visible banana sits ON the floor instead of sinking in like shallow water.
  private const float SurfaceClearance = 0.3f;
  [Signal] public delegate void ExplodedEventHandler (Vector3 origin);
  [Signal] public delegate void StuckToPlayerEventHandler (Player victim, Vector3 hitPosition);
  // Caught in a drawn slingshot (issue #251): the live copy reports it with the fuse
  // time left; every copy (cosmetic ones too) vanishes, since the catcher's drawing
  // state replicates & the catch is the same on every peer.
  [Signal] public delegate void CaughtBySlingshotEventHandler (Player catcher, float fuseSecondsLeft);
  private const float OwnCatchAfterSeconds = 0.4f; // Clear of the muzzle first; then your own slingshot can catch it.
  private CharacterBody3D? _shooter;
  private bool _hitsShooter; // A slung launcher's spree banana (issue #287) sticks to its own slinger too.
  private static readonly Color BananaYellow = new(0.92f, 0.78f, 0.12f);
  private static readonly Color FlashColor = new(4.0f, 3.6f, 0.4f);
  private Vector3 _velocity;
  private float _age;
  private bool _isLive;
  private bool _fuseLit;
  private float _fuseSecondsLeft;
  private Rid _shooterRid;
  private MeshInstance3D _mesh = null!;

  public override void _Ready()
  {
    _mesh = GetNode <MeshInstance3D> ("Mesh");
    _mesh.Mesh = ResourceLoader.Load <Mesh> ("res://assets/weapons/banana.obj");
    _mesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = BananaYellow, Roughness = 0.5f };
  }

  public void Launch (Vector3 origin, Vector3 direction, bool isLive, CharacterBody3D shooter, bool hitsShooter = false)
  {
    GlobalPosition = origin;
    _velocity = direction.Normalized() * Speed;
    _isLive = isLive;
    _shooter = shooter;
    _hitsShooter = hitsShooter;
    _shooterRid = shooter.GetRid();
  }

  // A caught grenade fired back (issue #251): the fuse is already lit & carries over.
  public void LaunchLit (Vector3 origin, Vector3 direction, bool isLive, CharacterBody3D shooter, float fuseSecondsLeft)
  {
    Launch (origin, direction, isLive, shooter);
    _fuseLit = true;
    _fuseSecondsLeft = fuseSecondsLeft;
  }

  public float FuseSecondsLeft => _fuseLit ? _fuseSecondsLeft : FuseSeconds;

  public override void _PhysicsProcess (double delta)
  {
    var dt = (float)delta;
    _age += dt;
    if (_fuseLit) _fuseSecondsLeft -= dt;

    if (_age > MaxLifetimeSeconds || (_fuseLit && _fuseSecondsLeft <= 0.0f))
    {
      Explode (GlobalPosition);
      return;
    }

    _mesh.RotateX (SpinRadiansPerSecond * dt);
    var from = GlobalPosition;
    _velocity.Y -= GravityAcceleration * dt;
    var to = from + _velocity * dt;
    // The shooter is excluded only until the banana has cleared the muzzle: after that
    // it can come back down onto their own drawn slingshot (issue #251).
    var query = PhysicsRayQueryParameters3D.Create (from, to, exclude: _age < OwnCatchAfterSeconds ? new Godot.Collections.Array <Rid> { _shooterRid } : new Godot.Collections.Array <Rid>());
    query.HitFromInside = true;
    var hit = GetWorld3D().DirectSpaceState.IntersectRay (query);

    if (hit.Count == 0)
    {
      GlobalPosition = to;
      return;
    }

    if (hit["collider"].AsGodotObject() is CharacterBody3D body)
    {
      if (TryCatch (body)) return;
      if (body == _shooter && !_hitsShooter) { GlobalPosition = to; return; } // Your own banana passes through you unless you're catching it - or it came off your slung launcher (issue #287).
      // A direct player hit doesn't explode - it sticks (issue #83): the live banana
      // reports the victim & every peer swaps to the replicated stuck banana.
      if (_isLive && body is Player victim) EmitSignal (SignalName.StuckToPlayer, victim, (Vector3)hit["position"]);
      QueueFree();
      return;
    }

    Bounce (hit);
  }

  // Surfaces don't stop the banana outright: it bounces & slides, dampened each
  // contact, & the first contact lights the fuse (issue #70).
  private void Bounce (Godot.Collections.Dictionary hit)
  {
    LightFuse();
    var normal = (Vector3)hit["normal"];
    var into = normal * _velocity.Dot (normal); // The component driving into the surface (negative along the normal).
    var along = _velocity - into;
    _velocity = along * SlideRetention - into * Restitution; // Flip & squash the plunge, keep the skid.
    GlobalPosition = (Vector3)hit["position"] + normal * SurfaceClearance;
  }

  // The catch (issue #251): only a slingshot being DRAWN at the moment of contact
  // catches; merely holding one is not enough (thepro & Caleb's balance rule).
  private bool TryCatch (CharacterBody3D body)
  {
    // The pouch must be ABLE to accept (CodeRabbit): DrawingSlingshot & SlingshotAmmo
    // both replicate, so the projectile's peer sees the same emptiness the catcher's
    // ReceiveBananaCatch guard will demand - an occupied pouch just gets hit normally
    // instead of vanishing the banana. The catcher-side guard stays as the backstop
    // for the sub-frame race.
    if (body is not Player catcher || !catcher.DrawingSlingshot || catcher.SlingshotAmmo != HeldWeapon.None) return false;
    if (_isLive) EmitSignal (SignalName.CaughtBySlingshot, catcher, FuseSecondsLeft);
    QueueFree();
    return true;
  }

  private void LightFuse()
  {
    if (_fuseLit) return;
    _fuseLit = true;
    _fuseSecondsLeft = FuseSeconds;
  }

  private void Explode (Vector3 origin)
  {
    if (_isLive) EmitSignal (SignalName.Exploded, origin);
    SpawnExplosionEffects (GetParent(), origin);
    QueueFree();
  }

  // Flash + squelch + debris + nearby-camera shake on whichever peer calls it; shared
  // with the sticky-banana detonation in Player.Banana.cs (issue #83).
  public static void SpawnExplosionEffects (Node parent, Vector3 origin)
  {
    SpawnFlash (parent, origin);
    PlayExplosionSound (parent, origin);
    BananaDebris.Scatter (parent, origin); // Chunks everywhere (issue #83).
    Player.NotifyExplosionAt (origin); // Every peer's own camera shakes if nearby (issue #70).
  }

  // Wet-squelch detonation (issue #83): a transient positional player at the blast
  // point, so every nearby peer hears it from the right direction & frees itself.
  private static void PlayExplosionSound (Node parent, Vector3 origin)
  {
    var sound = new AudioStreamPlayer3D { Stream = ResourceLoader.Load <AudioStream> ("res://assets/sounds/banana-explode.mp3") };
    parent.AddChild (sound);
    sound.GlobalPosition = origin;
    sound.Finished += sound.QueueFree;
    sound.Play();
  }

  // Big yellow flash: an emissive sphere that swells to the blast radius & fades out.
  private static void SpawnFlash (Node parent, Vector3 origin)
  {
    var material = new StandardMaterial3D
    {
      AlbedoColor = new Color (FlashColor, 0.8f),
      EmissionEnabled = true,
      Emission = FlashColor,
      Transparency = BaseMaterial3D.TransparencyEnum.Alpha
    };
    var flash = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.5f, Height = 1.0f }, MaterialOverride = material };
    parent.AddChild (flash);
    flash.GlobalPosition = origin;
    var tween = flash.CreateTween().SetParallel();
    tween.TweenProperty (flash, "scale", Vector3.One * FlashRadius * 2.0f, FlashSeconds);
    tween.TweenProperty (material, "albedo_color:a", 0.0f, FlashSeconds);
    tween.Chain().TweenCallback (Callable.From (flash.QueueFree));
  }
}
