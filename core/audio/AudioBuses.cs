using Godot;

namespace com.forerunnergames.energyshot.core.audio;

// Volume sliders (issue #301, Escendrix: the effects are too loud): music already
// rides its own bus (MusicManager); every OTHER AudioStreamPlayer in a subtree is
// routed onto an SFX bus here, & the two sliders drive the two buses live. 100 = the
// mix as it was, so the defaults change nothing.
public static class AudioBuses
{
  public const string SfxBusName = "SFX";

  public static void EnsureSfxBus()
  {
    if (AudioServer.GetBusIndex (SfxBusName) != -1) return;
    var busIndex = AudioServer.BusCount;
    AudioServer.AddBus (busIndex);
    AudioServer.SetBusName (busIndex, SfxBusName);
    AudioServer.SetBusSend (busIndex, "Master");
  }

  // Every player under root that isn't already on the music bus becomes an effect.
  public static void RouteSfx (Node root)
  {
    foreach (var node in Descendants (root))
    {
      if (node is AudioStreamPlayer player && player.Bus != MusicManager.BusName) player.Bus = SfxBusName;
      if (node is AudioStreamPlayer3D player3D && player3D.Bus != MusicManager.BusName) player3D.Bus = SfxBusName;
    }
  }

  public static void ApplyVolumes (int sfxPercent, int musicPercent)
  {
    SetBusPercent (SfxBusName, sfxPercent);
    SetBusPercent (MusicManager.BusName, musicPercent);
  }

  public static float PercentToDb (int percent) => percent <= 0 ? -80.0f : Mathf.LinearToDb (Mathf.Clamp (percent, 0, 100) / 100.0f);

  private static void SetBusPercent (string bus, int percent)
  {
    var index = AudioServer.GetBusIndex (bus);
    if (index != -1) AudioServer.SetBusVolumeDb (index, PercentToDb (percent));
  }

  private static System.Collections.Generic.IEnumerable <Node> Descendants (Node root)
  {
    foreach (var child in root.GetChildren())
    {
      yield return child;
      foreach (var grandchild in Descendants (child)) yield return grandchild;
    }
  }
}
