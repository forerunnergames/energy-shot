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
  [Export] public float Restitution = 0.55f;
  [Export] public float SpinRadiansPerSecond = 10.0f;
  private const float FlashRadius = 6.0f;
  private const float FlashSeconds = 0.4f;
  // Rest height (issue #132): the tumbling mesh sweeps a ~0.27m radius around the
  // node origin, so bounces & rolls keep the origin this far off the surface - the
  // visible banana sits ON the floor instead of sinking in like shallow water.
  private const float SurfaceClearance = 0.3f;
  [Signal] public delegate void ExplodedEventHandler (Vector3 origin);
  [Signal] public delegate void StuckToPlayerEventHandler (Player victim, Vector3 hitPosition);
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

  public void Launch (Vector3 origin, Vector3 direction, bool isLive, CharacterBody3D shooter)
  {
    GlobalPosition = origin;
    _velocity = direction.Normalized() * Speed;
    _isLive = isLive;
    _shooterRid = shooter.GetRid();
  }

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
    var query = PhysicsRayQueryParameters3D.Create (from, to, exclude: new Godot.Collections.Array <Rid> { _shooterRid });
    query.HitFromInside = true;
    var hit = GetWorld3D().DirectSpaceState.IntersectRay (query);

    if (hit.Count == 0)
    {
      GlobalPosition = to;
      return;
    }

    // A direct player hit doesn't explode - it sticks (issue #83): the live banana
    // reports the victim & every peer swaps to the replicated stuck banana.
    if (hit["collider"].AsGodotObject() is CharacterBody3D body)
    {
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
    _velocity = _velocity.Bounce (normal) * Restitution;
    GlobalPosition = (Vector3)hit["position"] + normal * SurfaceClearance;
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
