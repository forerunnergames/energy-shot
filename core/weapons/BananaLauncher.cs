using Godot;

namespace com.forerunnergames.energyshot.weapons;

// Single-use banana launcher (issue #61): selected with the weapon_2 action, it
// replaces the energy weapon visually & fires one arcing banana, then goes on a
// long cooldown (the player auto-switches back to the laser meanwhile).
public partial class BananaLauncher : Node3D
{
  [Export] public float CooldownSeconds = 10.0f;
  private static readonly Color BananaYellow = new(0.92f, 0.78f, 0.12f);
  private float _cooldownLeft;
  public bool CanFire => _cooldownLeft <= 0.0f;
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
