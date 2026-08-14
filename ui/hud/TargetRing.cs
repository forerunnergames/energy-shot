using Godot;

namespace com.forerunnergames.energyshot.ui.hud;

// The paper airplane's target warning (issue #191), drawn on the TARGETED player's
// screen only: a large red ring that thickens & brightens as the airplane closes in,
// then blinks with an accelerating beep once it's a couple of seconds out. Nothing
// here is networked - the local player's own threat reading drives it - & the beep is
// code-generated, so no audio is downloaded.
public partial class TargetRing : Control
{
  private const float RadiusScreenFraction = 0.34f;
  private const float MinThickness = 5.0f;
  private const float MaxThickness = 26.0f;
  private const float MinAlpha = 0.25f;
  // Past this the ring stops being a steady warning & starts blinking + beeping.
  private const float BlinkThreshold = 0.45f;
  private const float MinBlinksPerSecond = 3.0f;
  private const float MaxBlinksPerSecond = 14.0f;
  private static readonly Color WarningRed = new(1.0f, 0.15f, 0.12f);
  // Lock-on (issue #205): the THROWER's confirmation that the airplane will home
  // on whoever is under the crosshair. Same ring, steady & thinner, so the two
  // readings can't be confused: a lock is something you chose, a threat is not.
  private static readonly Color LockRed = new(1.0f, 0.25f, 0.2f);
  private const float LockThickness = 8.0f;
  private const float LockAlpha = 0.75f;
  private bool _locked;
  private float _threat;
  private float _blinkPhase;
  private bool _blinkOn = true;
  private AudioStreamPlayer _beep = null!;
  private bool IsBlinking => _threat >= BlinkThreshold;
  private float BlinksPerSecond() => Mathf.Lerp (MinBlinksPerSecond, MaxBlinksPerSecond, _threat);

  public override void _Ready()
  {
    MouseFilter = MouseFilterEnum.Ignore;
    SetAnchorsPreset (LayoutPreset.FullRect);
    _beep = new AudioStreamPlayer { Stream = ProceduralSounds.Beep(), MaxPolyphony = 4 };
    AddChild (_beep);
    Visible = false;
  }

  // 0 = nothing incoming, 1 = impact. Called every frame by the HUD.
  public void SetThreat (float threat)
  {
    var was = _threat;
    _threat = Mathf.Clamp (threat, 0.0f, 1.0f);
    if (_threat > 0.0f) { Visible = true; return; }
    // The warning just ended (CodeRabbit on #206): with a lock still held the ring
    // stays up, so it has to redraw as the steady lock ring instead of the last
    // warning frame - nothing else will ask it to while the threat is zero.
    if (was > 0.0f) QueueRedraw();
    Reset();
  }

  // An incoming airplane always outranks a lock: being hunted matters more than
  // what you're aiming at.
  public void SetLocked (bool locked)
  {
    if (_locked == locked) return;
    _locked = locked;
    if (_threat <= 0.0f) Visible = locked;
    QueueRedraw();
  }

  private void Reset()
  {
    Visible = _locked;
    _blinkPhase = 0.0f;
    _blinkOn = true;
  }

  public override void _Process (double delta)
  {
    if (!Visible) return;
    if (_threat <= 0.0f) return; // A steady lock ring needs no per-frame work.
    UpdateBlink ((float)delta);
    QueueRedraw();
  }

  // Blink & beep together, both accelerating as the airplane closes: below the blink
  // threshold the ring is simply steady & silent.
  private void UpdateBlink (float delta)
  {
    if (!IsBlinking) { _blinkOn = true; return; }
    _blinkPhase += BlinksPerSecond() * delta;
    if (_blinkPhase < 1.0f) return;
    _blinkPhase = 0.0f;
    _blinkOn = !_blinkOn;
    if (_blinkOn) _beep.Play();
  }

  public override void _Draw()
  {
    var size = Size;
    var radius = Mathf.Min (size.X, size.Y) * RadiusScreenFraction;

    if (_threat <= 0.0f)
    {
      if (_locked) DrawArc (size * 0.5f, radius, 0.0f, Mathf.Tau, 96, new Color (LockRed, LockAlpha), LockThickness, antialiased: true);
      return;
    }

    if (!_blinkOn) return;
    var thickness = Mathf.Lerp (MinThickness, MaxThickness, _threat);
    var color = new Color (WarningRed, Mathf.Lerp (MinAlpha, 1.0f, _threat));
    DrawArc (size * 0.5f, radius, 0.0f, Mathf.Tau, 96, color, thickness, antialiased: true);
  }
}
