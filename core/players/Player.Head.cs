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

  // PARKED (Aaron, 2026-08-22): the floating ball is gone until the real head
  // rework (#238) lands as a complete PR - the body is back to its pre-head look &
  // headshots are dormant (no hitbox, so nothing ever reports one). The class &
  // its constants stay so the code & tests keep compiling.
  private void CreateHead() { }

  private void UpdateHeadVisibility() { if (_headMesh != null) _headMesh.Visible = !IsMultiplayerAuthority() || _thirdPerson; }
}
