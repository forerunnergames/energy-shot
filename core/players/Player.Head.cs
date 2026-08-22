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

  public Rid HeadRid => _head?.GetRid() ?? default; // No head while parked (#238): callers skip an invalid rid.

  private void CreateHead()
  {
    _head = HeadHitbox.Create();
    AddChild (_head);
    // Same tinted material as the body (duplicated, so the color setters drive both).
    _headMesh = new MeshInstance3D { Mesh = new SphereMesh { Radius = HeadHitbox.Radius, Height = HeadHitbox.Radius * 2.0f }, Position = HeadHitbox.LocalOffset };
    _headMesh.SetSurfaceOverrideMaterial (0, (Material)_mesh.GetSurfaceOverrideMaterial (0).Duplicate());
    AddChild (_headMesh);
  }

  // The head RIDES the body mesh (issue #238's founding bug: it hovered in place
  // through slides, crouches, & the lie-down): mesh & hitbox track the body's
  // transform every frame - position, scale, & the death tween's rotation alike.
  // Head center = body top along the body's own up axis, minus the neck overlap.
  private void UpdateHeadPose()
  {
    if (_head == null) return;
    const float bodyHalfHeight = 1.0f; // The 2m capsule about its center.
    var seat = HeadHitbox.LocalOffset.Y - 2.0f; // 0.25: how far the head center rises above the capsule top (radius 0.45 - 0.2 of neck overlap).
    var center = _mesh.Position + _mesh.Basis.Y.Normalized() * (bodyHalfHeight * _mesh.Scale.Y + seat);
    _head.Position = center;
    _headMesh.Position = center;
    _headMesh.Basis = _mesh.Basis.Orthonormalized();
  }

  // Your own dome would fill the view when you look up: hide it in first person,
  // show it in third person & on every other peer's copy of you.
  private void UpdateHeadVisibility() { if (_headMesh != null) _headMesh.Visible = !IsMultiplayerAuthority() || _thirdPerson; }
}
