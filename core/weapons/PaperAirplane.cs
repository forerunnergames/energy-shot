using Godot;

namespace com.forerunnergames.energyshot.weapons;

// The paper airplane's shared look & effects (issue #191). The airplane is never
// carried: it lives on the ground as an armed landmine (a WeaponPickup carrying
// HeldWeapon.Airplane), gets loaded into a slingshot as ammo (issue #190), or flies
// as a PaperAirplaneProjectile. Built entirely from primitive meshes & existing
// sounds - nothing is downloaded.
public static class PaperAirplane
{
  private static readonly Color PaperWhite = new(0.94f, 0.94f, 0.9f);
  private static readonly Color LedRed = new(1.0f, 0.15f, 0.12f);
  public const string LedNodeName = "Led";

  // A folded dart: a slim fuselage between two swept wings, with a tiny red arming
  // LED that the grounded mine & the in-flight plane both blink.
  public static Node3D CreateVisual()
  {
    var paper = new StandardMaterial3D { AlbedoColor = PaperWhite, Roughness = 0.75f };
    var visual = new Node3D();
    visual.AddChild (new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3 (0.05f, 0.09f, 0.7f) }, MaterialOverride = paper });
    visual.AddChild (Wing (paper, side: -1.0f));
    visual.AddChild (Wing (paper, side: 1.0f));
    visual.AddChild (CreateLed());
    return visual;
  }

  private static MeshInstance3D Wing (Material paper, float side) => new()
  {
    Mesh = new BoxMesh { Size = new Vector3 (0.34f, 0.02f, 0.62f) },
    MaterialOverride = paper,
    Position = new Vector3 (side * 0.17f, -0.02f, 0.04f),
    RotationDegrees = new Vector3 (0.0f, side * -12.0f, side * 14.0f)
  };

  private static MeshInstance3D CreateLed() => new()
  {
    Name = LedNodeName,
    Mesh = new SphereMesh { Radius = 0.05f, Height = 0.1f },
    MaterialOverride = new StandardMaterial3D { AlbedoColor = LedRed, EmissionEnabled = true, Emission = LedRed, EmissionEnergyMultiplier = 4.0f },
    Position = new Vector3 (0.0f, 0.07f, -0.3f)
  };

  // Blinks the arming LED at the given rate; shared by the grounded mine & the
  // in-flight plane so "armed" always reads the same way (issue #191).
  public static void BlinkLed (Node3D visual, float ageSeconds, float blinksPerSecond)
  {
    var led = visual.GetNodeOrNull <Node3D> (LedNodeName);
    if (led == null) return;
    led.Visible = Mathf.PosMod (ageSeconds * blinksPerSecond, 1.0f) < 0.5f;
  }

  // The non-gory pop (issue #191): a brief white flash & a burst of paper scraps,
  // played locally on every peer. No blast radius - the damage is single-target.
  public static void Pop (Node parent, Vector3 origin)
  {
    var flash = new OmniLight3D { LightColor = new Color (1.0f, 0.85f, 0.5f), LightEnergy = 8.0f, OmniRange = 9.0f };
    parent.AddChild (flash);
    flash.GlobalPosition = origin;
    var fade = flash.CreateTween();
    fade.TweenProperty (flash, "light_energy", 0.0f, 0.35f);
    fade.Finished += flash.QueueFree;
    BananaDebris.Scatter (parent, origin, PaperWhite);
    PlayPopSound (parent, origin);
  }

  // The banana blast replayed short & bright reads as a paper pop - reusing an
  // existing sound instead of downloading one (issue #191).
  private static void PlayPopSound (Node parent, Vector3 origin)
  {
    var pop = new AudioStreamPlayer3D { Stream = ResourceLoader.Load <AudioStream> ("res://assets/sounds/banana-explode.mp3"), PitchScale = 1.9f, VolumeDb = -4.0f };
    parent.AddChild (pop);
    pop.GlobalPosition = origin;
    pop.Finished += pop.QueueFree;
    pop.Play();
  }
}
