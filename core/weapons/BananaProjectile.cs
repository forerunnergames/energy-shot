using Godot;

namespace com.forerunnergames.energyshot.weapons;

// A lobbed banana that arcs under gravity & explodes on impact with a big yellow
// flash (issue #61). Only the shooter's own banana is "live" (reports the blast);
// other peers spawn visual-only copies. Impacts are detected by sweeping a ray
// along the path traveled each physics frame, same as LaserBolt.
public partial class BananaProjectile : Node3D
{
  [Export] public float Speed = 22.0f;
  [Export] public float GravityAcceleration = 30.0f;
  [Export] public float MaxLifetimeSeconds = 6.0f;
  [Export] public float SpinRadiansPerSecond = 10.0f;
  [Export] public float FlashRadius = 6.0f;
  [Export] public float FlashSeconds = 0.4f;
  [Signal] public delegate void ExplodedEventHandler (Vector3 origin);
  private static readonly Color BananaYellow = new(0.92f, 0.78f, 0.12f);
  private static readonly Color FlashColor = new(4.0f, 3.6f, 0.4f);
  private Vector3 _velocity;
  private float _age;
  private bool _isLive;
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

    if (_age > MaxLifetimeSeconds)
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

    if (hit.Count > 0)
    {
      Explode ((Vector3)hit["position"]);
      return;
    }

    GlobalPosition = to;
  }

  private void Explode (Vector3 origin)
  {
    if (_isLive) EmitSignal (SignalName.Exploded, origin);
    SpawnFlash (origin);
    QueueFree();
  }

  // Big yellow flash: an emissive sphere that swells to the blast radius & fades out.
  private void SpawnFlash (Vector3 origin)
  {
    var material = new StandardMaterial3D
    {
      AlbedoColor = new Color (FlashColor, 0.8f),
      EmissionEnabled = true,
      Emission = FlashColor,
      Transparency = BaseMaterial3D.TransparencyEnum.Alpha
    };
    var flash = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.5f, Height = 1.0f }, MaterialOverride = material };
    GetParent().AddChild (flash);
    flash.GlobalPosition = origin;
    var tween = flash.CreateTween().SetParallel();
    tween.TweenProperty (flash, "scale", Vector3.One * FlashRadius * 2.0f, FlashSeconds);
    tween.TweenProperty (material, "albedo_color:a", 0.0f, FlashSeconds);
    tween.Chain().TweenCallback (Callable.From (flash.QueueFree));
  }
}
