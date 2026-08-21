using Godot;

namespace com.forerunnergames.energyshot.weapons;

// The blowgun's scope model (issue #236), pure & unit-tested: a zoom ladder you step
// with the wheel, & the drift that makes aiming a skill - the reticle wanders with an
// amplitude that grows with zoom, spiking on each heartbeat & settling for about a
// second in between. The settled window is the shot window.
public static class Scope
{
  // Camera FOVs per zoom step; the last is "very far".
  public static readonly float[] ZoomFovs = { 40.0f, 25.0f, 14.0f, 7.0f, 3.5f };
  public const float BeatPeriodSeconds = 2.0f;
  // Drift, as a fraction of the scope radius, at the first & last zoom steps.
  public const float MinDriftFraction = 0.08f;
  public const float MaxDriftFraction = 0.45f;
  // The thump spikes the drift; by SettleSeconds it's down to SettledFraction of the
  // step's amplitude & stays there until the next beat - the window you shoot in.
  public const float SettleSeconds = 0.9f;
  public const float SettledFraction = 0.2f;

  public static int StepIn (int step) => Mathf.Min (step + 1, ZoomFovs.Length - 1);
  public static int StepOut (int step) => Mathf.Max (step - 1, 0);

  // Amplitude for a zoom step, before the heartbeat envelope.
  public static float DriftFraction (int step) => Mathf.Lerp (MinDriftFraction, MaxDriftFraction, ZoomFovs.Length <= 1 ? 0.0f : (float)step / (ZoomFovs.Length - 1));

  // 1.0 at the beat, easing to SettledFraction by SettleSeconds, flat after.
  public static float BeatEnvelope (float secondsSinceBeat)
  {
    if (secondsSinceBeat >= SettleSeconds) return SettledFraction;
    var t = secondsSinceBeat / SettleSeconds;
    return Mathf.Lerp (1.0f, SettledFraction, t * t);
  }

  public static bool IsSettled (float secondsSinceBeat) => secondsSinceBeat >= SettleSeconds;

  // Smooth wander: two incommensurate sines per axis, so it never looks like a circle.
  public static Vector2 Wander (float time) => new(Mathf.Sin (time * 1.7f) * 0.6f + Mathf.Sin (time * 2.9f + 1.3f) * 0.4f, Mathf.Sin (time * 1.3f + 0.7f) * 0.6f + Mathf.Sin (time * 3.7f + 2.1f) * 0.4f);
}
