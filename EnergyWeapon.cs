using Godot;

namespace com.forerunnergames.energyshot;

public partial class EnergyWeapon : Node3D
{
  // @formatter:off

  [Export]
  public Color WeaponColor
  {
    get => _weaponColor;
    set
    {
      _weaponColor = value;
      _muzzleMaterial.AlbedoColor = _weaponColor;
    }
  }

  [Export] public float MinRotationSpeed = 1.0f;
  [Export] public float MaxRotationSpeed = 15.0f;
  [Signal] public delegate void ShotFiredEventHandler (float energy);
  public bool IsSpinningUp { get; private set; }
  private AudioStreamPlayer3D _shootingSound = null!;
  private MeshInstance3D _muzzleMeshInstance = null!;
  private Node3D _pivot = null!;
  private StandardMaterial3D _muzzleMaterial = null!;
  private Color _normalColor;
  private Color _chargedColor;
  private Color _weaponColor;
  private Tween? _tween;
  private float _currentRotationSpeed;
  public override void _PhysicsProcess (double delta) => _pivot.Rotate (Vector3.Right, _currentRotationSpeed * (float)delta);
  public void PlayShootingSound() => _shootingSound.Play();
  public void Charge() => SpinUp();
  private float CalculateEnergy() => _currentRotationSpeed / MaxRotationSpeed;
  // @formatter:on

  public override void _Ready()
  {
    _pivot = GetNode <Node3D> ("Pivot");
    _shootingSound = GetNode <AudioStreamPlayer3D> ("ShootingSound");
    _muzzleMeshInstance = GetNode <Node3D> ("Pivot/Muzzle").GetNode <MeshInstance3D> ("Cube_001");
    _muzzleMaterial = CreateCopy ((_muzzleMeshInstance.Mesh.SurfaceGetMaterial (0) as StandardMaterial3D)!);
    _muzzleMeshInstance.MaterialOverride = _muzzleMaterial;
    _normalColor = _muzzleMaterial.AlbedoColor;
    _chargedColor = new Color (3.0f, 0.0f, _muzzleMaterial.AlbedoColor.B, _muzzleMaterial.AlbedoColor.A);
    WeaponColor = _normalColor;
    _currentRotationSpeed = MinRotationSpeed;
  }

  public void Discharge()
  {
    PlayShootingSound();
    EmitSignal (SignalName.ShotFired, CalculateEnergy());
    SpinDown();
  }

  private void SpinUp()
  {
    if (IsSpinningUp) return;
    IsSpinningUp = true;
    _tween?.Kill();
    _tween = CreateTween().SetParallel();
    _tween.TweenProperty (this, "_currentRotationSpeed", MaxRotationSpeed, 2.0f).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.Out);
    _tween.TweenProperty (this, "WeaponColor", _chargedColor, 2.0f).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.In);
  }

  private void SpinDown()
  {
    IsSpinningUp = false;
    _tween?.Kill();
    _tween = CreateTween().SetParallel();
    _tween.TweenProperty (this, "_currentRotationSpeed", MinRotationSpeed, 2.0f).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.Out);
    _tween.TweenProperty (this, "WeaponColor", _normalColor, 2.0f).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.Out);
  }

  private static StandardMaterial3D CreateCopy (StandardMaterial3D material)
  {
    var copy = new StandardMaterial3D();
    copy.AlbedoColor = material.AlbedoColor;
    copy.AlbedoTexture = material.AlbedoTexture;
    copy.Metallic = material.Metallic;
    copy.MetallicSpecular = material.MetallicSpecular;
    copy.Roughness = material.Roughness;
    copy.EmissionEnabled = material.EmissionEnabled;
    copy.Emission = material.Emission;
    return copy;
  }
}
