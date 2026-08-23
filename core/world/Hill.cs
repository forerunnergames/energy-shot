using Godot;

namespace com.forerunnergames.energyshot.core.world;

// King of the Hill (issue #44): a marked ring on the arena floor. Whoever SOLELY
// stands in it earns a point per second; two or more inside is a contest & nobody
// scores. The ring is pure cosmetics built in code on every peer; the server does the
// counting with the pure Contains check below.
public partial class Hill : Node3D
{
  // The hill roams (issue #294, thepro & Caleb: the banana platform sits right
  // next to the spawn room - a dud). Every round the server rolls one of the four
  // 8x8 sky platforms spread across the arena, all a real trip from spawn; the
  // choice rides the round-clock broadcast so every peer rebuilds the same ring.
  public static readonly Vector3[] Spots =
  {
    new(22.0f, 4.25f, -10.0f),
    new(-22.0f, 5.25f, 12.0f),
    new(8.0f, 6.25f, -32.0f),
    new(-28.0f, 8.25f, 26.0f),
  };

  public const float Radius = 3.8f; // Just inside the 8x8 slab's edge, so standing on the platform is standing on the hill.
  private const float HeightTolerance = 3.0f; // Standing on it, not flying over it.
  private static readonly Color Gold = new(1.0f, 0.8f, 0.2f, 0.35f);

  public static bool Contains (int spotIndex, Vector3 position)
  {
    var spot = Spots[Mathf.Clamp (spotIndex, 0, Spots.Length - 1)];
    var flat = new Vector2 (position.X - spot.X, position.Z - spot.Z);
    return flat.Length() <= Radius && Mathf.Abs (position.Y - spot.Y) <= HeightTolerance;
  }

  public static Hill Create (int spotIndex)
  {
    var hill = new Hill { Name = "Hill", Position = Spots[Mathf.Clamp (spotIndex, 0, Spots.Length - 1)] };
    var glow = new StandardMaterial3D { AlbedoColor = Gold, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, EmissionEnabled = true, Emission = new Color (1.0f, 0.7f, 0.1f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
    hill.AddChild (new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = Radius, BottomRadius = Radius, Height = 0.1f }, Position = Vector3.Up * 0.06f, MaterialOverride = glow });
    // A tall beam so the hill reads from anywhere in the arena & the spawn room.
    hill.AddChild (new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.15f, Height = 40.0f }, Position = Vector3.Up * 20.0f, MaterialOverride = glow });
    hill.AddChild (new OmniLight3D { LightColor = new Color (1.0f, 0.8f, 0.3f), LightEnergy = 2.0f, OmniRange = 8.0f, Position = Vector3.Up * 2.0f });
    return hill;
  }
}
