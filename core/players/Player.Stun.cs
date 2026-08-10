using Godot;

namespace com.forerunnergames.energyshot.players;

// Shared stun system (issues #70 & #71): while stunned, walking slows & jumping &
// sliding are blocked. Punch stuns stack (heavier stack = slower); banana blasts
// fully stun for a flat window synced with the splatter overlay.
public partial class Player
{
  // Replicated so every peer can see how stunned this player is; only the victim writes it.
  [Export] public float StunFactor { get; set; }
  [Export] public float StunMaxSlow = 0.6f;
  [Export] public float PunchStunFactorStep = 0.34f;
  [Export] public float PunchStunSeconds = 3.0f;
  [Export] public float BananaStunSeconds = 5.0f;
  private float _stunSecondsLeft;
  private bool IsStunned => _stunSecondsLeft > 0.0f;
  private float StunSpeedMultiplier() => 1.0f - StunFactor * StunMaxSlow;

  // Punch stuns stack: every connect adds slow, so a flurry grinds the victim down (issue #71).
  private void ApplyPunchStun()
  {
    StunFactor = Mathf.Min (1.0f, StunFactor + PunchStunFactorStep);
    _stunSecondsLeft = Mathf.Max (_stunSecondsLeft, PunchStunSeconds);
  }

  // Banana blasts fully stun for a flat window, synced with the splatter overlay (issue #70).
  private void ApplyBananaStun()
  {
    StunFactor = 1.0f;
    _stunSecondsLeft = Mathf.Max (_stunSecondsLeft, BananaStunSeconds);
  }

  private void UpdateStun (double delta)
  {
    if (!IsStunned) return;
    _stunSecondsLeft -= (float)delta;
    if (IsStunned) return;
    ClearStun();
  }

  private void ClearStun()
  {
    _stunSecondsLeft = 0.0f;
    StunFactor = 0.0f;
  }
}
