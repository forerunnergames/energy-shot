using Godot;

namespace com.forerunnergames.energyshot.weapons;

// Banana launcher (issues #61 & #83): selected with the weapon_3 action, it replaces
// the energy weapon visually & fires one lobbed banana per quick reload (issue #70),
// staying selected through the cooldown.
public partial class BananaLauncher : Node3D
{
  [Export] public float CooldownSeconds = 1.5f;
  private static readonly Color BananaYellow = new(0.92f, 0.78f, 0.12f);
  private float _cooldownLeft;
  private AudioStreamPlayer3D _fireSound = null!;
  public bool CanFire => _cooldownLeft <= 0.0f;
  // 0..1 readiness (1 = ready) for the HUD cooldown bar (issue #70).
  public float CooldownFraction => 1.0f - _cooldownLeft / CooldownSeconds;
  public void ResetCooldown() => _cooldownLeft = 0.0f; // Fresh lives start ready (issue #299).
  public void StartCooldown() => _cooldownLeft = CooldownSeconds;
  // Real grenade-launcher thump (issue #83): positional, so every peer hears it from
  // the shooter's location via the visual-banana path.
  public void PlayFireSound() => _fireSound.Play();

  public override void _Ready()
  {
    _fireSound = GetNode <AudioStreamPlayer3D> ("FireSound");
    // A follow-up launch inside the thump's tail mixes instead of restarting it (issue #182).
    _fireSound.MaxPolyphony = 4;
    ApplyRifleVisuals();
  }
  public override void _PhysicsProcess (double delta) => _cooldownLeft = Mathf.Max (0.0f, _cooldownLeft - (float)delta);

  private void ApplyRifleVisuals()
  {
    var mesh = GetNode <MeshInstance3D> ("Mesh");
    mesh.Mesh = ResourceLoader.Load <Mesh> ("res://assets/weapons/Banana_Rifle.obj");
    mesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = BananaYellow, Roughness = 0.6f };
  }
}
