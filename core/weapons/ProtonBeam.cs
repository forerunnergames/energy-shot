using Godot;

namespace com.forerunnergames.energyshot.weapons;

// The full-charge beam (issue #292): a writhing, crackling proton-pack stream that
// pours from the muzzle for a couple of seconds. Cosmetic only - the instant
// full-charge bolt still does the hitting; every peer draws its own copy (the
// SpawnVisual* pattern). A camera-facing ribbon rebuilt each frame from layered
// sine noise, pinned at the muzzle & at the far end so it reads as one stream that
// is barely under control.
public partial class ProtonBeam : Node3D
{
  public const float DurationSeconds = 1.8f;
  public const float MaxLengthMeters = 60.0f;
  public const int Segments = 32;
  public const float HalfWidthMeters = 0.14f;
  public const float WobbleMeters = 0.9f;
  private static readonly Color BeamColor = new(1.6f, 0.55f, 0.25f); // Over 1 for the bloom: proton orange.
  private Node3D? _anchor;
  private Vector3 _anchorOffset;
  private Vector3 _origin;
  private Vector3 _direction;
  private float _length;
  private float _age;
  private float _seed;
  private ImmediateMesh _mesh = null!;

  // Zero at both ends, full in the middle: the stream stays attached where it starts & lands.
  public static float Envelope (float t) => Mathf.Sin (Mathf.Pi * Mathf.Clamp (t, 0.0f, 1.0f));

  // Layered sines, never the same twice (seed). Pure & unit-tested.
  public static Vector2 Wobble (float t, float time, float seed)
  {
    var x = Mathf.Sin (time * 21.0f + t * 17.0f + seed) * 0.6f + Mathf.Sin (time * 7.0f + t * 5.0f - seed) * 0.4f;
    var y = Mathf.Cos (time * 17.0f + t * 13.0f + seed * 0.7f) * 0.6f + Mathf.Sin (time * 5.0f + t * 9.0f + seed) * 0.4f;
    return new Vector2 (x, y) * (Envelope (t) * WobbleMeters);
  }

  public void Launch (Node3D shooter, Vector3 origin, Vector3 direction, float length)
  {
    _anchor = shooter;
    _anchorOffset = origin - shooter.GlobalPosition;
    _origin = origin;
    _direction = direction.Normalized();
    _length = Mathf.Clamp (length, 1.0f, MaxLengthMeters);
    _seed = GD.Randf() * 100.0f;
    _mesh = new ImmediateMesh();
    var material = new StandardMaterial3D
    {
      ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
      BlendMode = BaseMaterial3D.BlendModeEnum.Add,
      CullMode = BaseMaterial3D.CullModeEnum.Disabled,
      Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
      VertexColorUseAsAlbedo = true,
      AlbedoColor = BeamColor,
      EmissionEnabled = true,
      Emission = BeamColor,
      EmissionEnergyMultiplier = 4.0f
    };
    AddChild (new MeshInstance3D { Mesh = _mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
  }

  public override void _Process (double delta)
  {
    _age += (float)delta;
    if (_age >= DurationSeconds) { QueueFree(); return; }
    if (_anchor != null && IsInstanceValid (_anchor)) _origin = _anchor.GlobalPosition + _anchorOffset; // Rides the shooter's body; the aim stays where it was fired.
    Rebuild();
  }

  private void Rebuild()
  {
    var camera = GetViewport().GetCamera3D();
    var fade = 1.0f - Mathf.Pow (_age / DurationSeconds, 3.0f); // Full for most of the pour, a quick fizzle at the end.
    var side1 = _direction.Cross (Mathf.Abs (_direction.Y) > 0.9f ? Vector3.Right : Vector3.Up).Normalized();
    var side2 = _direction.Cross (side1).Normalized();
    var time = Time.GetTicksMsec() / 1000.0f;
    _mesh.ClearSurfaces();
    _mesh.SurfaceBegin (Mesh.PrimitiveType.TriangleStrip);

    for (var i = 0; i <= Segments; ++i)
    {
      var t = (float)i / Segments;
      var w = Wobble (t, time, _seed);
      var p = _origin + _direction * (t * _length) + side1 * w.X + side2 * w.Y;
      var toCamera = camera != null ? camera.GlobalPosition - p : Vector3.Up;
      var ribbon = toCamera.Cross (_direction);
      ribbon = ribbon.LengthSquared() < 0.0001f ? side1 : ribbon.Normalized();
      var half = ribbon * (HalfWidthMeters * (0.6f + 0.4f * Envelope (t)) * fade);
      _mesh.SurfaceSetColor (new Color (1.0f, 1.0f, 1.0f, fade));
      _mesh.SurfaceAddVertex (ToLocal (p - half));
      _mesh.SurfaceAddVertex (ToLocal (p + half));
    }

    _mesh.SurfaceEnd();
  }
}
