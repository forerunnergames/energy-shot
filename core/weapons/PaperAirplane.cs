using Godot;

namespace com.forerunnergames.energyshot.weapons;

// The paper airplane's hazard effects (issue #191), shared by every way it can go
// off: a thrown airplane reaching its locked target, a slung one hitting somebody,
// or an armed grounded one being stepped on. Non-gory by design - a tick, a zappy
// flash, & a puff of paper scraps. Built from primitive meshes & existing sounds,
// nothing downloaded. The airplane's own look lives on PaperAirplaneProjectile.
public static class PaperAirplane
{
  private static readonly Color PaperWhite = new(0.93f, 0.95f, 1.0f);

  // The mine going live under somebody's foot (issue #191): a quick, dry tick
  // everyone nearby can hear, so a triggered mine is never silent to bystanders -
  // they get the beat they need to back away from the player who set it off.
  public static void Arm (Node parent, Vector3 origin) => PlayAt (parent, origin, "res://assets/sounds/punch-thud.wav", pitch: 2.4f, volumeDb: -6.0f);

  // The pop (issue #191): a brief warm flash & a burst of paper scraps, played
  // locally on every peer. No blast radius - the damage is strictly single-target.
  public static void Pop (Node parent, Vector3 origin)
  {
    var flash = new OmniLight3D { LightColor = new Color (1.0f, 0.85f, 0.5f), LightEnergy = 8.0f, OmniRange = 9.0f };
    parent.AddChild (flash);
    flash.GlobalPosition = origin;
    var fade = flash.CreateTween();
    fade.TweenProperty (flash, "light_energy", 0.0f, 0.35f);
    fade.Finished += flash.QueueFree;
    BananaDebris.Scatter (parent, origin, PaperWhite);
    // The banana blast replayed short & bright reads as a paper pop - reusing an
    // existing sound instead of downloading one.
    PlayAt (parent, origin, "res://assets/sounds/banana-explode.mp3", pitch: 1.9f, volumeDb: -4.0f);
  }

  private static void PlayAt (Node parent, Vector3 origin, string streamPath, float pitch, float volumeDb)
  {
    var sound = new AudioStreamPlayer3D { Stream = ResourceLoader.Load <AudioStream> (streamPath), PitchScale = pitch, VolumeDb = volumeDb };
    parent.AddChild (sound);
    sound.GlobalPosition = origin;
    sound.Finished += sound.QueueFree;
    sound.Play();
  }
}
