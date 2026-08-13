using Godot;

namespace com.forerunnergames.energyshot.ui.hud;

// A tiny center-screen cooldown indicator (issue #177): visible only while its
// cooldown is actually recovering, fading in & out fast, with a brief green flash
// the moment it comes back ready. Replaces the old permanent bar row.
public partial class CooldownMeter : HBoxContainer
{
  private const float FadeInPerSecond = 10.0f;
  private const float FadeOutPerSecond = 5.0f;
  private const float ReadyFlashSeconds = 0.35f;
  private static readonly Color ReadyFlashColor = new(0.55f, 1.0f, 0.55f);
  private ProgressBar _bar = null!;
  private float _alpha;
  private float _flashSecondsLeft;
  private bool _isRecovering;

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

  public override void _Process (double delta)
  {
    var dt = (float)delta;
    _flashSecondsLeft = Mathf.Max (0.0f, _flashSecondsLeft - dt);
    var isRelevant = _isRecovering || _flashSecondsLeft > 0.0f;
    _alpha = Mathf.Clamp (_alpha + (isRelevant ? FadeInPerSecond : -FadeOutPerSecond) * dt, 0.0f, 1.0f);
    var color = _flashSecondsLeft > 0.0f ? ReadyFlashColor : Colors.White;
    Modulate = color with { A = _alpha };
  }
}
