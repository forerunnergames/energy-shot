using System.Threading.Tasks;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Death sequence (issue #152): a zapped-out body tips over (non-gory, lights-out
// robot style) & lies at the death spot for DeathSequenceSeconds while the victim's
// camera pulls back & up (Player.View.cs) & the HUD shows the death message with a
// countdown (Hud.cs); then the usual auto-respawn (spawn armor included) runs.
public partial class Player
{
  private const float FallOverSeconds = 0.7f;
  private bool _fallen;
  private Tween? _fallTween;

  // Replicated like Sliding so every peer sees the fallen body at the death spot;
  // synced ALWAYS for the same self-healing reason (issue #131), so ApplyFallen must
  // stay idempotent per state.
  [Export]
  public bool Fallen
  {
    get => _fallen;
    set
    {
      _fallen = value;
      ApplyFallen();
    }
  }

  // The authority's side of the sequence: lock input, drop the pose flags so every
  // peer's restore helpers agree, & lie there until the respawn timer fires.
  private async Task LieFallen()
  {
    Sliding = false;
    Crouching = false;
    Dancing = false;
    Fallen = true;
    _slideJumpCarrying = false; // A corpse (& the next life) inherits no slide-jump momentum (issue #149).
    SetInputEnabled (isEnabled: false);
    EnterDeathView(); // Watch the aftermath from above (issue #152).
    GD.Print ($"{DisplayName}: I'm down for {DeathSequenceSeconds}s...");
    await ToSignal (GetTree().CreateTimer (DeathSequenceSeconds), SceneTreeTimer.SignalName.Timeout);
    // A disconnect can free this node mid-wait (CodeRabbit on #185): never touch
    // disposed children after the await, same as the sticky-banana fuse.
    if (!IsInstanceValid (this) || !IsInsideTree()) return;
    Fallen = false;
    ExitDeathView();
  }

  // Runs on every peer via the replicated Fallen property; ALWAYS-mode sync re-fires
  // the setter every tick, so start/stop exactly once per state flip (like Dancing).
  private void ApplyFallen()
  {
    if (_mesh == null) return; // Pre-_Ready sync; the next ALWAYS tick re-applies.
    if (_fallen && _fallTween == null) { StartFallAnimation(); return; }
    if (!_fallen && _fallTween != null) StopFallAnimation();
  }

  // A powered-down robot tipping over: the mesh eases to its side at the death spot.
  // Mesh only - no hitbox changes, the body is scenery until the respawn.
  private void StartFallAnimation()
  {
    _danceTween?.Kill(); // A mid-dance death releases the mesh to the fall.
    _danceTween = null;
    _fallTween = CreateTween().SetParallel();
    _fallTween.TweenProperty (_mesh, "rotation_degrees", new Vector3 (0.0f, 0.0f, 90.0f), FallOverSeconds).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.In);
    _fallTween.TweenProperty (_mesh, "position", new Vector3 (0.0f, 0.5f, 0.0f), FallOverSeconds).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.In);
    _fallTween.TweenProperty (_mesh, "scale", Vector3.One, FallOverSeconds);
  }

  // Same canonical restore helpers the dance & respawn reset audits rely on (issue #103).
  private void StopFallAnimation()
  {
    _fallTween?.Kill();
    _fallTween = null;
    ApplySlidePose();
    ApplyCrouchScale();
  }
}
