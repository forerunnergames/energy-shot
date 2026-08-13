using com.forerunnergames.energyshot.core.audio;
using Godot;

namespace com.forerunnergames.energyshot.ui.hud;

// Frequency-line animation for the mini music player (issue #137): polls the
// Music bus spectrum analyzer each frame, smooths the band magnitudes, & draws
// one cheap polyline that dances with the current song.
public partial class MusicVisualizer : Control
{
  private const int BandCount = 24;
  private const float MinFrequency = 40.0f;
  private const float MaxFrequency = 8000.0f;
  private const float FloorDb = 60.0f; // Silence at -60dB maps to a flat line.
  private const float SmoothingPerSecond = 10.0f;
  private static readonly Color LineColor = new(0.55f, 0.85f, 1.0f, 0.9f);
  private readonly float[] _levels = new float[BandCount];
  private AudioEffectSpectrumAnalyzerInstance? _analyzer;
  // Log-spaced band edges so bass & treble both read across the little line.
  private static float BandFrequency (int band) => MinFrequency * Mathf.Pow (MaxFrequency / MinFrequency, band / (float)BandCount);

  public override void _Ready()
  {
    var busIndex = AudioServer.GetBusIndex (MusicManager.BusName);
    if (busIndex == -1) return;
    _analyzer = AudioServer.GetBusEffectInstance (busIndex, 0) as AudioEffectSpectrumAnalyzerInstance;
  }

  public override void _Process (double delta)
  {
    if (_analyzer == null || !IsVisibleInTree()) return;
    var smoothing = Mathf.Clamp ((float)delta * SmoothingPerSecond, 0.0f, 1.0f);
    for (var band = 0; band < BandCount; ++band) _levels[band] = Mathf.Lerp (_levels[band], TargetLevel (band), smoothing);
    QueueRedraw();
  }

  private float TargetLevel (int band)
  {
    var magnitude = _analyzer!.GetMagnitudeForFrequencyRange (BandFrequency (band), BandFrequency (band + 1)).Length();
    return Mathf.Clamp (1.0f + Mathf.LinearToDb (magnitude + 0.0000001f) / FloorDb, 0.0f, 1.0f);
  }

  public override void _Draw()
  {
    var points = new Vector2[BandCount];
    for (var band = 0; band < BandCount; ++band) points[band] = new Vector2 (Size.X * band / (BandCount - 1), Size.Y * (1.0f - 0.9f * _levels[band]) - 1.0f);
    DrawPolyline (points, LineColor, 3.0f, antialiased: true);
  }
}
