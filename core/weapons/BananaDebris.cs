using Godot;

namespace com.forerunnergames.energyshot.weapons;

// Visual-only banana chunks flung from every explosion (issue #83): simple gravity
// arcs that rest on the first surface they touch, fading out & despawning after ~5 s.
// Purely cosmetic - spawned locally on every peer, no collision shapes, no damage.
public partial class BananaDebris : MeshInstance3D
{
  private const int ChunkCount = 12;
  private const float LifetimeSeconds = 5.0f;
  private const float GravityAcceleration = 24.0f;
  private const float LaunchSpeedMin = 4.0f;
  private const float LaunchSpeedMax = 12.0f;
  private static readonly Color BananaYellow = new(0.92f, 0.78f, 0.12f);
  private static readonly RandomNumberGenerator Rng = new();
  private Vector3 _velocity;
  private Vector3 _spinRadiansPerSecond;
  private float _age;
  private bool _resting;
  private StandardMaterial3D _material = null!;

  public static void Scatter (Node parent, Vector3 origin)
  {
    for (var i = 0; i < ChunkCount; ++i)
    {
      var chunk = CreateChunk();
      parent.AddChild (chunk);
      chunk.GlobalPosition = origin;
    }
  }

  private static BananaDebris CreateChunk()
  {
    var size = Rng.RandfRange (0.08f, 0.2f);
    return new BananaDebris
    {
      Mesh = new BoxMesh { Size = new Vector3 (size, size * 0.6f, size * 1.6f) },
      _velocity = RandomLaunchVelocity(),
      _spinRadiansPerSecond = new Vector3 (Rng.RandfRange (-6.0f, 6.0f), Rng.RandfRange (-6.0f, 6.0f), Rng.RandfRange (-6.0f, 6.0f))
    };
  }

  private static Vector3 RandomLaunchVelocity()
  {
    var direction = new Vector3 (Rng.RandfRange (-1.0f, 1.0f), Rng.RandfRange (0.4f, 1.0f), Rng.RandfRange (-1.0f, 1.0f)).Normalized();
    return direction * Rng.RandfRange (LaunchSpeedMin, LaunchSpeedMax);
  }

  public override void _Ready()
  {
    _material = new StandardMaterial3D { AlbedoColor = BananaYellow, Roughness = 0.5f, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
    MaterialOverride = _material;
  }

  public override void _PhysicsProcess (double delta)
  {
    var dt = (float)delta;
    _age += dt;

    if (_age >= LifetimeSeconds)
    {
      QueueFree();
      return;
    }

    _material.AlbedoColor = new Color (BananaYellow, 1.0f - _age / LifetimeSeconds);
    if (_resting) return;
    Rotation += _spinRadiansPerSecond * dt;
    var from = GlobalPosition;
    _velocity.Y -= GravityAcceleration * dt;
    var to = from + _velocity * dt;
    var hit = GetWorld3D().DirectSpaceState.IntersectRay (PhysicsRayQueryParameters3D.Create (from, to));

    if (hit.Count == 0)
    {
      GlobalPosition = to;
      return;
    }

    GlobalPosition = (Vector3)hit["position"] + (Vector3)hit["normal"] * 0.05f;
    _resting = true; // Chunks stay where they land - visual-only, so no bouncing needed.
  }
}
