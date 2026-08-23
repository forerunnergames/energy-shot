using com.forerunnergames.energyshot.core.audio;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Dance emote (issue #103): press G to groove - a rhythmic squash-&-stretch bounce,
// a lean sway, alternating overhead hand pumps, & a slow spin, looping until
// canceled. Dancing is a taunt with risk: firing & punching are blocked while
// dancing, & any movement or combat input - or taking damage - snaps back to normal.
public partial class Player
{
  [Export] public float DanceSpinDegreesPerSecond = 45.0f;
  [Export] public float DanceBounceScale = 0.12f;
  [Export] public float DanceLeanRadians = 0.25f;
  [Export] public float DanceHandLiftMeters = 0.6f;
  [Export] public float DanceHandPumpMeters = 0.5f;
  // Loose fixed-tempo guess (issue #103), no beat detection: ~120 BPM (4 bounces per
  // 2s loop) while the synced soundtrack plays, a lazier ~100 BPM shuffle in silence.
  private const float MusicDanceLoopSeconds = 2.0f;
  private const float QuietDanceLoopSeconds = 2.4f;
  private bool _dancing;
  private Tween? _danceTween;

  // Replicated like Sliding so every peer sees (& can still shoot at) the dancer;
  // synced ALWAYS for the same self-healing reason (issue #131), so ApplyDance must
  // stay idempotent per state.
  [Export]
  public bool Dancing
  {
    get => _dancing;
    set
    {
      _dancing = value;
      ApplyDance();
    }
  }

  // G toggles the groove (issue #103). Runs after the fire/punch updates in
  // _PhysicsProcess: those still see Dancing true & swallow the press, so a
  // canceling combat input never also attacks in the same frame.
  private void UpdateDance (double delta)
  {
    if (Dancing) { ContinueDance (delta); return; }
    if (!StartsDance()) return;
    StartDance();
  }

  // The eating ritual blocks the groove too (issue #192): no input escapes the 3s.
  private bool StartsDance() => _isInputEnabled && !IsStunned && !Sliding && !Eating && Input.IsActionJustPressed ("dance");

  private void StartDance()
  {
    if (_crouching && IsOverheadBlocked()) return; // No room to stand up & boogie.
    Crouching = false; // Dancing happens standing.
    ApplyCameraHeight();
    Dancing = true;
  }

  private void ContinueDance (double delta)
  {
    if (CanceledDance()) { Dancing = false; return; }
    RotateY (Mathf.DegToRad (DanceSpinDegreesPerSecond) * (float)delta); // Slow spin; the replicated rotation carries it to peers.
  }

  // Any movement or combat input - or another G press - ends the dance (issue #103);
  // the fire/punch gates already swallowed the attack itself this frame.
  private bool CanceledDance() =>
    !_isInputEnabled
    || Input.GetVector ("move_left", "move_right", "move_forward", "move_back") != Vector2.Zero
    || Input.IsActionPressed ("shoot")
    || Input.IsActionJustPressed ("jump")
    || Input.IsActionJustPressed ("slide")
    || Input.IsActionJustPressed ("crouch")
    || Input.IsActionJustPressed ("punch")
    || Input.IsActionJustPressed ("ability")
    // Hold mode (Aaron, 2026-08-23): release G to stop; toggle mode (default) ends
    // on another G press, as always.
    || (_holdToDance ? !Input.IsActionPressed ("dance") : Input.IsActionJustPressed ("dance"));

  // Runs on every peer via the replicated Dancing property; ALWAYS-mode sync re-fires
  // the setter every tick, so start/stop exactly once per state flip.
  private void ApplyDance()
  {
    if (_mesh == null) return; // Pre-_Ready sync; the next ALWAYS tick re-applies.
    if (_dancing && _danceTween == null) { StartDanceAnimation(); return; }
    if (!_dancing && _danceTween != null) StopDanceAnimation();
  }

  // One looping method tween drives the whole routine from a single 0..tau phase:
  // all procedural on the existing body & hand nodes, no animation assets (issue #103).
  private void StartDanceAnimation()
  {
    foreach (var tween in _handTweens) tween?.Kill(); // A punch mid-swing releases its hand to the dance.
    _danceTween = CreateTween().SetLoops();
    _danceTween.TweenMethod (Callable.From <float> (ApplyDancePose), 0.0f, Mathf.Tau, DanceLoopSeconds());
    UpdateHandsVisibility(); // Both hands wave regardless of the selected weapon.
  }

  // Full restore on every peer: the same helpers the respawn reset audit relies on
  // (issue #103) put the mesh rotation, position, & scale back to canonical.
  private void StopDanceAnimation()
  {
    _danceTween?.Kill();
    _danceTween = null;
    ApplySlidePose();
    ApplyCrouchScale();
    for (var hand = 0; hand < _hands.Length; ++hand) ResetHandRest (hand);
    UpdateHandsVisibility();
  }

  private void ResetHandRest (int hand)
  {
    if (_hands[hand] == null) return;
    _hands[hand]!.Position = HandRestOffset (hand);
  }

  private float DanceLoopSeconds()
  {
    var music = GetNodeOrNull <MusicManager> ("/root/World/MusicManager");
    return music != null && music.CurrentTrackTitle.Length > 0 ? MusicDanceLoopSeconds : QuietDanceLoopSeconds;
  }

  // phase runs 0..tau per loop: |sin 2phi| bounces on every beat (4 per loop),
  // sin phi sways the lean once per loop, & sin 2phi pumps the hands alternately.
  // Only the mesh squashes - the hitbox stays honest while dancing.
  private void ApplyDancePose (float phase)
  {
    var beat = Mathf.Sin (phase * 2.0f);
    var bounce = Mathf.Abs (beat);
    _mesh.Scale = new Vector3 (1.0f + DanceBounceScale * 0.5f * bounce, 1.0f - DanceBounceScale * bounce, 1.0f + DanceBounceScale * 0.5f * bounce);
    _mesh.Rotation = new Vector3 (0.0f, 0.0f, Mathf.Sin (phase) * DanceLeanRadians);
    _mesh.Position = new Vector3 (0.0f, 1.0f - DanceBounceScale * 0.5f * bounce, 0.0f); // Feet stay planted through the squash.
    ApplyDanceHandPose (hand: 0, beat);
    ApplyDanceHandPose (hand: 1, -beat);
  }

  // Raised hands pumping in alternation, like a crowd hyped on the beat.
  private void ApplyDanceHandPose (int hand, float pump)
  {
    if (_hands[hand] == null) return;
    _hands[hand]!.Position = HandRestOffset (hand) + Vector3.Up * (DanceHandLiftMeters + DanceHandPumpMeters * pump);
  }
}
