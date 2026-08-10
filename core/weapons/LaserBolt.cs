using Godot;

namespace com.forerunnergames.energyshot.weapons;

// A visible laser burst that travels through the world. Damage & glow scale with the
// energy it was fired with. Only the shooter's own bolt is "live" (deals damage);
// other peers spawn visual-only copies. Hits are detected by sweeping a ray along the
// path traveled each physics frame, so fast bolts can't tunnel through thin geometry.
public partial class LaserBolt : Node3D
{
  [Export] public float Speed = 90.0f;
  [Export] public float DropAcceleration = 6.0f;
  [Export] public float MaxLifetimeSeconds = 4.0f;
  [Signal] public delegate void HitPlayerEventHandler (CharacterBody3D player, float energy);
  private static readonly Color LowEnergyColor = new(0.2f, 0.6f, 3.0f);
  private static readonly Color HighEnergyColor = new(3.0f, 0.2f, 0.2f);
  private Vector3 _velocity;
  private float _energy;
  private float _age;
  private bool _isLive;
  private Rid _shooterRid;

  public void Launch (Vector3 origin, Vector3 direction, float energy, bool isLive, CharacterBody3D shooter)
  {
    GlobalPosition = origin;
    _velocity = direction.Normalized() * Speed;
    _energy = energy;
    _isLive = isLive;
    _shooterRid = shooter.GetRid();
    Orient();
    ApplyEnergyVisuals();
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

    var from = GlobalPosition;
    _velocity.Y -= DropAcceleration * dt; // ponytail: simple laser drop, no drag
    var to = from + _velocity * dt;
    var query = PhysicsRayQueryParameters3D.Create (from, to, exclude: new Godot.Collections.Array <Rid> { _shooterRid });
    // Point-blank bolts can spawn inside the target's collider; without this the
    // sweep never registers & the bolt sails through (see issue #52).
    query.HitFromInside = true;
    var hit = GetWorld3D().DirectSpaceState.IntersectRay (query);

    if (hit.Count > 0)
    {
      if (_isLive && hit["collider"].AsGodotObject() is CharacterBody3D player) EmitSignal (SignalName.HitPlayer, player, _energy);
      QueueFree();
      return;
    }

    GlobalPosition = to;
    Orient();
  }

  private void Orient()
  {
    if (_velocity.LengthSquared() < 0.01f) return;
    var direction = _velocity.Normalized();
    LookAt (GlobalPosition + direction, direction.Abs().IsEqualApprox (Vector3.Up) ? Vector3.Forward : Vector3.Up);
  }

  private void ApplyEnergyVisuals()
  {
    var mesh = GetNode <MeshInstance3D> ("Mesh");
    var material = (StandardMaterial3D)((StandardMaterial3D)mesh.MaterialOverride).Duplicate();
    mesh.MaterialOverride = material;
    var color = LowEnergyColor.Lerp (HighEnergyColor, _energy);
    material.AlbedoColor = color;
    material.Emission = color;
    var thickness = 0.5f + _energy;
    mesh.Scale = new Vector3 (thickness, thickness, 1.0f + _energy * 2.0f);
  }
}
