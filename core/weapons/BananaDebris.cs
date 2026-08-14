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
  // A resting chunk this close to a slingshot-equipped player gets loaded as ammo (issue #190).
  private const float ScoopRangeMeters = 1.8f;
  private static readonly Color BananaYellow = new(0.92f, 0.78f, 0.12f);
  private static readonly RandomNumberGenerator Rng = new();
  private Vector3 _velocity;
  private Vector3 _spinRadiansPerSecond;
  private Color _color = BananaYellow;
  private float _age;
  private bool _resting;
  private StandardMaterial3D _material = null!;

  public static void Scatter (Node parent, Vector3 origin) => Scatter (parent, origin, BananaYellow);

  // Recoloring alone wasn't enough (issue #203): the paper airplane's pop was
  // throwing banana-shaped chunks around, just painted white. Paper scraps are flat
  // & wide, so the burst reads as shredded paper instead of fruit.
  public static void Scatter (Node parent, Vector3 origin, Color color, bool isPaper = false)
  {
    for (var i = 0; i < ChunkCount; ++i)
    {
      var chunk = CreateChunk (color, isPaper);
      parent.AddChild (chunk);
      chunk.GlobalPosition = origin;
    }
  }

  // Paper scraps are cosmetic only (CodeRabbit on #206): a slingshot must not scoop
  // one up & fire it as a banana chunk.
  private bool _isPaper;

  private static BananaDebris CreateChunk (Color color, bool isPaper = false)
  {
    var size = Rng.RandfRange (0.08f, 0.2f);
    var shape = isPaper
      ? new Vector3 (size * 1.7f, size * 0.06f, size * 1.3f) // Flat, wide scraps.
      : new Vector3 (size, size * 0.6f, size * 1.6f);        // Chunky banana pieces.

    return new BananaDebris
    {
      Mesh = new BoxMesh { Size = shape },
      _color = color,
      _isPaper = isPaper,
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
    _material = new StandardMaterial3D { AlbedoColor = _color, Roughness = 0.5f, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
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

    _material.AlbedoColor = new Color (_color, 1.0f - _age / LifetimeSeconds);
    if (_resting) { TryLoadIntoSlingshot(); return; }
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

  // Universal ammo (issue #190): a slingshot-equipped player walking over a resting
  // chunk nocks it. Purely local - debris is cosmetic scenery that no cap tracks &
  // that no player ever "holds", so there's nothing for the server to grant, & the
  // chunk each peer sees simply fades out on its own timer.
  private void TryLoadIntoSlingshot()
  {
    if (_isPaper) return; // Paper scraps aren't ammo (CodeRabbit on #206).
    var local = players.Player.Local;
    if (local == null || !local.IsLoadingAmmo) return;
    if (local.GlobalPosition.DistanceTo (GlobalPosition) > ScoopRangeMeters) return;
    local.LoadCosmeticAmmo (HeldWeapon.BananaChunk);
    QueueFree();
  }
}
