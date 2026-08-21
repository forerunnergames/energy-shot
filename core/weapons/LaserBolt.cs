using Godot;

namespace com.forerunnergames.energyshot.weapons;

// A visible laser burst that travels through the world. Damage & glow scale with the
// energy it was fired with. Only the shooter's own bolt is "live" (deals damage);
// other peers spawn visual-only copies. Hits are detected by sweeping a ray along the
// path traveled each physics frame, so fast bolts can't tunnel through thin geometry.
// The first frame sweeps from the camera, so geometry closer than the muzzle offset
// still stops sub-threshold shots (issue #112).
public partial class LaserBolt : Node3D
{
  // Doubled from 90 (issue #129): the per-frame ray sweep below keeps hit detection
  // exact at any speed, so faster bolts can't tunnel.
  [Export] public float Speed = 180.0f;
  // Drop at zero charge; the curve scales it down as charge rises (issue #106).
  [Export] public float DropAcceleration = 24.0f;
  // Drop multiplier by charge (issue #106): full-charge bolts fly nearly flat for
  // long-range sniping, low-charge & full-auto shots droop noticeably.
  [Export] public Curve? DropCurve;
  [Export] public float MaxLifetimeSeconds = 4.0f;
  public const float PierceEnergyThreshold = EnergyWeapon.FullChargeEnergyThreshold;
  // How far past the entry point the exit-side burn mark is searched for (issue #94).
  [Export] public float MaxPierceDepthMeters = 4.0f;
  // Burn marks only where the bolt slams into a face at speed (issue #125): the
  // incidence-scaled impact speed must be this fraction of the launch speed, so a
  // bolt drooping onto the ground at the end of its arc leaves nothing, while
  // deliberately shooting at the ground still scorches it.
  [Export] public float BurnMarkMinImpactSpeedFraction = 0.5f;
  [Signal] public delegate void HitPlayerEventHandler (CharacterBody3D player, float energy, bool throughBarrier);
  // Baseline bright red at every charge level (issue #92); charge only makes it hotter.
  private static readonly Color LowEnergyColor = new(3.0f, 0.12f, 0.1f);
  private static readonly Color HighEnergyColor = new(6.0f, 0.3f, 0.15f);
  private Vector3 _velocity;
  private Vector3 _sweepStart;
  private bool _sweptFromStart;
  private float _energy;
  private float _age;
  private bool _isLive;
  private bool _piercedBarrier;
  private Godot.Collections.Array <Rid> _exclusions = new();
  private float DropFor (float energy) => DropAcceleration * (DropCurve?.Sample (energy) ?? 1.0f - energy);

  // sweepStart is the shooter's camera position: the bolt spawns at the muzzle, but
  // the first sweep covers camera->muzzle too, so a wall closer than the muzzle
  // offset can't be skipped (issue #112).
  // A null shooter (issue #208: a slung laser's spree) excludes nobody - the gun is
  // out of control & owes no one safety.
  public void Launch (Vector3 origin, Vector3 sweepStart, Vector3 direction, float energy, bool isLive, CharacterBody3D? shooter)
  {
    GlobalPosition = origin;
    _sweepStart = sweepStart;
    _velocity = direction.Normalized() * Speed;
    _energy = energy;
    _isLive = isLive;
    _exclusions = shooter == null ? new Godot.Collections.Array <Rid>() : new Godot.Collections.Array <Rid> { shooter.GetRid() };
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

    var from = _sweptFromStart ? GlobalPosition : _sweepStart;
    _sweptFromStart = true;
    _velocity.Y -= DropFor (_energy) * dt; // ponytail: simple laser drop, no drag
    var to = GlobalPosition + _velocity * dt;

    // Re-query the same segment after each pierce so a player behind pierced
    // geometry still gets hit in the same physics frame.
    for (var pierces = 0; pierces < 8; ++pierces)
    {
      var query = PhysicsRayQueryParameters3D.Create (from, to, exclude: _exclusions);
      // Point-blank bolts can spawn inside the target's collider; without this the
      // sweep never registers & the bolt sails through (see issue #52).
      query.HitFromInside = true;
      var hit = GetWorld3D().DirectSpaceState.IntersectRay (query);
      if (hit.Count == 0) break;
      if (!ResolveHit (hit)) return;
    }

    GlobalPosition = to;
    Orient();
  }

  // Resolves a sweep hit; returns whether the bolt keeps flying. Players always stop
  // the bolt; (near-)full-charge bolts punch through world geometry, excluding the
  // pierced collider so the sweep doesn't re-hit it (see issue #50).
  private bool ResolveHit (Godot.Collections.Dictionary hit)
  {
    if (hit["collider"].AsGodotObject() is CharacterBody3D player)
    {
      if (_isLive) EmitSignal (SignalName.HitPlayer, player, _energy, _piercedBarrier);
      QueueFree();
      return false;
    }

    if (_energy < PierceEnergyThreshold)
    {
      QueueFree();
      return false;
    }

    Pierce (hit);
    return true;
  }

  // Punch through: scorch both faces so the pierce reads on every peer (issue #94),
  // then exclude the collider so the sweep doesn't re-hit it.
  private void Pierce (Godot.Collections.Dictionary hit)
  {
    _piercedBarrier = true;
    SpawnBurnMarks (hit);
    _exclusions.Add (hit["rid"].AsRid());
  }

  // Velocity/angle gate (issue #125): marks spawn only when the bolt pierces the
  // face at speed, not where it merely lands after its ballistic drop.
  private void SpawnBurnMarks (Godot.Collections.Dictionary hit)
  {
    var normal = hit["normal"].AsVector3();
    if (-_velocity.Dot (normal) < Speed * BurnMarkMinImpactSpeedFraction) return;
    var entry = hit["position"].AsVector3();
    BurnMark.Spawn (GetParent(), entry, normal);
    SpawnExitBurnMark (hit["rid"].AsRid(), entry);
  }

  // Finds the far face of the pierced collider by casting back toward the entry
  // point; skipped when the geometry is thicker than the search depth.
  private void SpawnExitBurnMark (Rid pierced, Vector3 entry)
  {
    var direction = _velocity.Normalized();
    var query = PhysicsRayQueryParameters3D.Create (entry + direction * MaxPierceDepthMeters, entry, exclude: _exclusions);
    var hit = GetWorld3D().DirectSpaceState.IntersectRay (query);
    if (hit.Count == 0 || hit["rid"].AsRid() != pierced) return;
    BurnMark.Spawn (GetParent(), hit["position"].AsVector3(), hit["normal"].AsVector3());
  }

  private void Orient()
  {
    if (_velocity.LengthSquared() < 0.01f) return;
    var direction = _velocity.Normalized();
    LookAt (GlobalPosition + direction, direction.Abs().IsEqualApprox (Vector3.Up) ? Vector3.Forward : Vector3.Up);
  }

  // Thin elongated tracer (issue #129), bright & strongly emissive even at minimum
  // charge (issue #92), with some charge scaling kept on top. The mesh's local Y is
  // the capsule's long axis & points along the flight direction (kept there by
  // Orient() each frame), so Y stretches the tracer & X/Z set its thickness.
  private void ApplyEnergyVisuals()
  {
    var mesh = GetNode <MeshInstance3D> ("Mesh");
    var material = (StandardMaterial3D)((StandardMaterial3D)mesh.MaterialOverride).Duplicate();
    mesh.MaterialOverride = material;
    var color = LowEnergyColor.Lerp (HighEnergyColor, _energy);
    material.AlbedoColor = color;
    material.Emission = color;
    material.EmissionEnergyMultiplier = 3.0f + _energy * 2.0f;
    var thickness = 1.0f + _energy * 0.6f;
    mesh.Scale = new Vector3 (thickness, 1.0f + _energy, thickness);
  }
}
