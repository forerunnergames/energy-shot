using Godot;

namespace com.forerunnergames.energyshot.core.world;

// King of the Hill (issue #44): a marked ring on the arena floor. Whoever SOLELY
// stands in it earns a point per second; two or more inside is a contest & nobody
// scores. The ring is pure cosmetics built in code on every peer; the server does the
// counting with the pure Contains check below.
public partial class Hill : Node3D
{
  public static readonly Vector3 Spot = new(8.0f, 0.0f, -6.0f); // Open ground between the buildings.
  public const float Radius = 4.0f;
  private const float HeightTolerance = 3.0f; // Standing on it, not flying over it.
  private static readonly Color Gold = new(1.0f, 0.8f, 0.2f, 0.35f);

  public static bool Contains (Vector3 position)
  {
    var flat = new Vector2 (position.X - Spot.X, position.Z - Spot.Z);
    return flat.Length() <= Radius && Mathf.Abs (position.Y - Spot.Y) <= HeightTolerance;
  }

  public static Hill Create()
  {
    var hill = new Hill { Name = "Hill", Position = Spot };
    var glow = new StandardMaterial3D { AlbedoColor = Gold, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, EmissionEnabled = true, Emission = new Color (1.0f, 0.7f, 0.1f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
    hill.AddChild (new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = Radius, BottomRadius = Radius, Height = 0.1f }, Position = Vector3.Up * 0.06f, MaterialOverride = glow });
    // A tall beam so the hill reads from the spawn room 30m up.
    hill.AddChild (new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.15f, Height = 40.0f }, Position = Vector3.Up * 20.0f, MaterialOverride = glow });
    hill.AddChild (new OmniLight3D { LightColor = new Color (1.0f, 0.8f, 0.3f), LightEnergy = 2.0f, OmniRange = 8.0f, Position = Vector3.Up * 2.0f });
    return hill;
  }
}
