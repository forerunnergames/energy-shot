using Godot;

namespace com.forerunnergames.energyshot.players;

// Headshots (issue #179): a floating sphere head above the capsule, robot-style, with
// its own hitbox (HeadHitbox). A headshot deals a flat HeadshotDamage, unscaled by the
// difficulty handicap: one zaps an Expert (200) or Intermediate (300) outright, a
// Beginner (400) takes exactly two. Laser bolts & slingshot stones report it.
public partial class Player
{
  public const int HeadshotDamage = 300;
  // The body capsule: 2m shrank ~20% (issue #435, Aaron: a smaller change - head, tags
  // & camera come down WITH it, the neck gap & the head itself stay as they were).
  public const float BodyHeight = 1.6f;
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
    const float bodyHalfHeight = BodyHeight / 2.0f; // The capsule about its center.
    var seat = HeadHitbox.LocalOffset.Y - BodyHeight; // 0.5: head center above the capsule top - radius 0.45 + a 0.05 hover gap (Aaron: close, never touching).
    var center = _mesh.Position + _mesh.Basis.Y.Normalized() * (bodyHalfHeight * _mesh.Scale.Y + seat);
    _head.Position = center;
    _headMesh.Position = center;
    _headMesh.Basis = _mesh.Basis.Orthonormalized();
  }

  // Your own dome would fill the view when you look up: hide it in first person,
  // show it in third person & on every other peer's copy of you.
  private void UpdateHeadVisibility() { if (_headMesh != null) _headMesh.Visible = !IsMultiplayerAuthority() || _thirdPerson; }
}
