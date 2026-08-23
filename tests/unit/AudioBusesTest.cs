using com.forerunnergames.energyshot.core.audio;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// Volume sliders (issue #301): 100 is the shipped mix, 0 is silence, the middle is quieter.
[TestSuite]
public class AudioBusesTest
{
  [TestCase]
  public void FullIsTheShippedMix() => AssertFloat (AudioBuses.PercentToDb (100)).IsEqualApprox (0.0f, 0.001f);

  [TestCase]
  public void ZeroIsSilence() => AssertFloat (AudioBuses.PercentToDb (0)).IsLessEqual (-80.0f);

  [TestCase]
  public void HalfIsQuieterButNotSilent()
  {
    AssertFloat (AudioBuses.PercentToDb (50)).IsLess (0.0f);
    AssertFloat (AudioBuses.PercentToDb (50)).IsGreater (-80.0f);
  }

  [TestCase]
  public void OverAHundredClampsToTheShippedMix() => AssertFloat (AudioBuses.PercentToDb (150)).IsEqualApprox (0.0f, 0.001f);
}
