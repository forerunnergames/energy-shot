using Godot;

namespace com.forerunnergames.energyshot.ui.hud;

// A tiny center-screen cooldown indicator (issue #177): visible only while its
// cooldown is actually recovering, fading in & out fast, with a brief green flash
// the moment it comes back ready. Replaces the old permanent bar row.
//
// The same meter also runs in REVERSE for timed rituals (issue #192): the bar starts
// full & drains to empty, stays visible the whole way, & dies on a red flash if the
// ritual is interrupted. Same node, same fade family, so every meter matches.
public partial class CooldownMeter : HBoxContainer
{
  private const float FadeInPerSecond = 10.0f;
  private const float FadeOutPerSecond = 5.0f;
  private const float ReadyFlashSeconds = 0.35f;
  private const float InterruptFlashSeconds = 0.5f;
  private static readonly Color ReadyFlashColor = new(0.55f, 1.0f, 0.55f);
  private static readonly Color InterruptFlashColor = new(1.0f, 0.35f, 0.3f);
  private ProgressBar _bar = null!;
  private float _alpha;
  private float _flashSecondsLeft;
  private float _interruptSecondsLeft;
  private bool _isRecovering;
  private bool _isDraining;

  public override void _Ready()
  {
    _bar = GetNode <ProgressBar> ("Bar");
    Modulate = Colors.White with { A = 0.0f };
  }

  // 0..1 readiness (1 = ready); the Hud feeds this every frame.
  public void SetFraction (float fraction)
  {
    _bar.Value = fraction;
    var recovering = fraction < 1.0f;
    if (_isRecovering && !recovering) _flashSecondsLeft = ReadyFlashSeconds; // Brief ready-flash (issue #177).
    _isRecovering = recovering;
  }

  // Reverse mode (issue #192): remaining runs 1 -> 0 while isActive holds the meter
  // on screen. A drain that reaches its end gets the same green flash a recovered
  // cooldown does; an interrupted one is claimed by Interrupt() instead.
  public void SetDraining (float remaining, bool isActive)
  {
    _bar.Value = remaining;
    if (_isDraining && !isActive && _interruptSecondsLeft <= 0.0f) _flashSecondsLeft = ReadyFlashSeconds;
    _isDraining = isActive;
  }

  // The meter visibly dies (issue #192): a red flash instead of the green one, the
  // bar slammed to empty, & then the usual fade out.
  public void Interrupt()
  {
    _interruptSecondsLeft = InterruptFlashSeconds;
    _flashSecondsLeft = 0.0f;
    _isDraining = false;
    _bar.Value = 0.0f;
  }

  public override void _Process (double delta)
  {
    var dt = (float)delta;
    _flashSecondsLeft = Mathf.Max (0.0f, _flashSecondsLeft - dt);
    _interruptSecondsLeft = Mathf.Max (0.0f, _interruptSecondsLeft - dt);
    var isRelevant = _isRecovering || _isDraining || _flashSecondsLeft > 0.0f || _interruptSecondsLeft > 0.0f;
    _alpha = Mathf.Clamp (_alpha + (isRelevant ? FadeInPerSecond : -FadeOutPerSecond) * dt, 0.0f, 1.0f);
    Modulate = FlashColor() with { A = _alpha };
  }

  private Color FlashColor()
  {
    if (_interruptSecondsLeft > 0.0f) return InterruptFlashColor;
    return _flashSecondsLeft > 0.0f ? ReadyFlashColor : Colors.White;
  }
}
