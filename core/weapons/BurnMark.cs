using Godot;

namespace com.forerunnergames.energyshot.weapons;

// Scorched glowing mark left on both faces of a surface a full-charge bolt pierces
// (issue #94). Spawned locally by every peer's copy of the bolt, so no extra
// networking; fades out & frees itself after a few seconds.
public partial class BurnMark : MeshInstance3D
{
  private const float FadeSeconds = 5.0f;
  private const float SizeMeters = 0.4f;
  private const float SurfaceOffsetMeters = 0.02f;
  private static readonly Color ScorchColor = new(0.08f, 0.04f, 0.02f);
  private static readonly Color EmberColor = new(2.0f, 0.6f, 0.1f);

  public static void Spawn (Node parent, Vector3 position, Vector3 normal)
  {
    if (normal.LengthSquared() < 0.01f) return;
    var mark = new BurnMark
    {
      Mesh = new QuadMesh { Size = new Vector2 (SizeMeters, SizeMeters) },
      MaterialOverride = CreateMaterial(),
      CastShadow = ShadowCastingSetting.Off
    };
    parent.AddChild (mark);
    var up = Mathf.Abs (normal.Dot (Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
    mark.LookAtFromPosition (position + normal * SurfaceOffsetMeters, position - normal, up);
    mark.StartFade();
  }

  private static StandardMaterial3D CreateMaterial() => new()
  {
    AlbedoColor = ScorchColor,
    EmissionEnabled = true,
    Emission = EmberColor,
    Roughness = 1.0f,
    CullMode = BaseMaterial3D.CullModeEnum.Disabled
  };

  private void StartFade()
  {
    var tween = CreateTween();
    tween.TweenProperty (this, "transparency", 1.0f, FadeSeconds);
    tween.TweenCallback (Callable.From (QueueFree));
  }
}
