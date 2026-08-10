using Godot;

namespace com.forerunnergames.energyshot.weapons;

// Single-use banana launcher (issue #61): selected with the weapon_2 action, it
// replaces the energy weapon visually & fires one lobbed banana, then reloads
// quickly (issue #70) while the player auto-switches back to the laser.
public partial class BananaLauncher : Node3D
{
  [Export] public float CooldownSeconds = 1.5f;
  private static readonly Color BananaYellow = new(0.92f, 0.78f, 0.12f);
  private float _cooldownLeft;
  public bool CanFire => _cooldownLeft <= 0.0f;
  // 0..1 readiness (1 = ready) for the HUD cooldown bar (issue #70).
  public float CooldownFraction => 1.0f - _cooldownLeft / CooldownSeconds;
  public void StartCooldown() => _cooldownLeft = CooldownSeconds;
  public override void _Ready() => ApplyRifleVisuals();
  public override void _PhysicsProcess (double delta) => _cooldownLeft = Mathf.Max (0.0f, _cooldownLeft - (float)delta);

  private void ApplyRifleVisuals()
  {
    var mesh = GetNode <MeshInstance3D> ("Mesh");
    mesh.Mesh = ResourceLoader.Load <Mesh> ("res://assets/weapons/Banana_Rifle.obj");
    mesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = BananaYellow, Roughness = 0.6f };
  }
}
