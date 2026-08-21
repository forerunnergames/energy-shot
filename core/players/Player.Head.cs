using Godot;

namespace com.forerunnergames.energyshot.players;

// Headshots (issue #179): a floating sphere head above the capsule, robot-style, with
// its own hitbox (HeadHitbox). A headshot deals a flat HeadshotDamage, unscaled by the
// difficulty handicap: one zaps an Expert (200) or Intermediate (300) outright, a
// Beginner (400) takes exactly two. Laser bolts & slingshot stones report it.
public partial class Player
{
  public const int HeadshotDamage = 300;
  private HeadHitbox _head = null!;
  private MeshInstance3D _headMesh = null!;

  public Rid HeadRid => _head.GetRid();

  private void CreateHead()
  {
    _head = HeadHitbox.Create();
    AddChild (_head);
    // Same tinted material as the body (duplicated, so the color setters drive both).
    _headMesh = new MeshInstance3D { Mesh = new SphereMesh { Radius = HeadHitbox.Radius, Height = HeadHitbox.Radius * 2.0f }, Position = HeadHitbox.LocalOffset };
    _headMesh.SetSurfaceOverrideMaterial (0, (Material)_mesh.GetSurfaceOverrideMaterial (0).Duplicate());
    AddChild (_headMesh);
  }

  // Your own dome would fill the view when you look up: hide it in first person,
  // show it in third person & on every other peer's copy of you.
  private void UpdateHeadVisibility() => _headMesh.Visible = !IsMultiplayerAuthority() || _thirdPerson;
}
