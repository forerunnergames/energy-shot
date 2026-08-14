using Godot;

namespace com.forerunnergames.energyshot.ui.hud;

// Tiny code-generated sound effects (issue #160): built as 16-bit PCM in memory,
// so bread feedback needs no downloaded audio assets.
public static class ProceduralSounds
{
  private const int SampleRate = 22050;

  // A satisfying munch: three quick, soft crunches followed by a warm rising heal chime.
  public static AudioStreamWav Munch()
  {
    var samples = new float[(int)(SampleRate * 0.7f)];
    AddCrunch (samples, startSeconds: 0.0f, brightness: 0.30f);
    AddCrunch (samples, startSeconds: 0.13f, brightness: 0.22f);
    AddCrunch (samples, startSeconds: 0.26f, brightness: 0.16f);
    AddChime (samples, startSeconds: 0.38f, fromHz: 440.0f, toHz: 660.0f, seconds: 0.3f, amplitude: 0.22f);
    return FromSamples (samples);
  }

  // The paper airplane's target warning beep (issue #191): one short, bright blip.
  // Played faster & faster as the airplane closes, so it stays a single tiny sound.
  public static AudioStreamWav Beep()
  {
    var samples = new float[(int)(SampleRate * 0.07f)];
    AddChime (samples, startSeconds: 0.0f, fromHz: 1320.0f, toHz: 1320.0f, seconds: 0.06f, amplitude: 0.3f);
    return FromSamples (samples);
  }

  // A soft "uh-uh" denied cue (issue #160): two low, gentle blips - clearly not an error buzzer.
  public static AudioStreamWav Denied()
  {
    var samples = new float[(int)(SampleRate * 0.35f)];
    AddChime (samples, startSeconds: 0.0f, fromHz: 220.0f, toHz: 200.0f, seconds: 0.1f, amplitude: 0.2f);
    AddChime (samples, startSeconds: 0.16f, fromHz: 180.0f, toHz: 160.0f, seconds: 0.12f, amplitude: 0.2f);
    return FromSamples (samples);
  }

  // A short burst of low-passed noise with an exponential decay reads as a bread crunch.
  private static void AddCrunch (float[] samples, float startSeconds, float brightness)
  {
    var rng = new RandomNumberGenerator();
    rng.Seed = (ulong)(startSeconds * 1000.0f) + 42;
    var start = (int)(startSeconds * SampleRate);
    var length = (int)(0.09f * SampleRate);
    var filtered = 0.0f;

    for (var i = 0; i < length && start + i < samples.Length; i++)
    {
      var envelope = Mathf.Exp (-10.0f * i / length);
      filtered += brightness * (rng.Randf() * 2.0f - 1.0f - filtered); // One-pole low-pass tames the hiss.
      samples[start + i] += filtered * envelope * 0.9f;
    }
  }

  private static void AddChime (float[] samples, float startSeconds, float fromHz, float toHz, float seconds, float amplitude)
  {
    var start = (int)(startSeconds * SampleRate);
    var length = (int)(seconds * SampleRate);
    var phase = 0.0f;

    for (var i = 0; i < length && start + i < samples.Length; i++)
    {
      var progress = (float)i / length;
      phase += Mathf.Lerp (fromHz, toHz, progress) * Mathf.Tau / SampleRate;
      var envelope = Mathf.Sin (Mathf.Pi * progress); // Smooth fade in & out.
      samples[start + i] += Mathf.Sin (phase) * envelope * amplitude;
    }
  }

  private static AudioStreamWav FromSamples (float[] samples)
  {
    var data = new byte[samples.Length * 2];

    for (var i = 0; i < samples.Length; i++)
    {
      var value = (short)(Mathf.Clamp (samples[i], -1.0f, 1.0f) * short.MaxValue);
      data[i * 2] = (byte)(value & 0xff);
      data[i * 2 + 1] = (byte)((value >> 8) & 0xff);
    }

    return new AudioStreamWav { Data = data, Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = SampleRate, Stereo = false };
  }
}
