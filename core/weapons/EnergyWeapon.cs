using Godot;

namespace com.forerunnergames.energyshot.weapons;

public partial class EnergyWeapon : Node3D
{
  [Export] public Color WeaponColor
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
  // Caps fire rate: after a shot, the weapon can't begin a new charge until the
  // cooldown elapses, so fast clicks & auto-clickers can't spam shots.
  [Export] public float ShotCooldownSeconds = 0.5f;
  [Export] public float RecoilStrength = 5.0f;
  [Export] public float RecoilRecoverySpeed = 5.0f;
  // Matches LaserBolt.PierceEnergyThreshold: at this energy the shot pierces & one-hit-kills.
  [Export] public float FullChargeEnergyThreshold = 0.95f;
  [Signal] public delegate void ShotFiredEventHandler (float energy, bool isFullAuto);
  // Fired once per charge when the spin-up crosses the full-charge threshold (issue #113).
  [Signal] public delegate void FullChargeReachedEventHandler();
  public bool IsSpinningUp { get; private set; }
  // True while holding a charge hot enough to pierce & one-hit-kill (issues #93, #105).
  public bool IsFullyCharged => IsSpinningUp && CalculateEnergy() >= FullChargeEnergyThreshold;
  private static readonly Color FullAutoColor = new(3.0f, 0.1f, 0.1f);
  private AudioStreamPlayer3D _shootingSound = null!;
  private AudioStreamPlayer3D _chargingSound = null!;
  private AudioStreamPlayer3D _fullAutoSwitchSound = null!;
  private AudioStreamPlayer3D _fullAutoReadySound = null!;
  private AudioStreamPlayer3D _fullChargeClickSound = null!;
  private bool _isFullAutoMode;
  private bool _fullChargeAnnounced;
  private MeshInstance3D _muzzleMeshInstance = null!;
  private Node3D _pivot = null!;
  private StandardMaterial3D _muzzleMaterial = null!;
  private Color _normalColor;
  private Color _chargedColor;
  private Color _weaponColor;
  private Tween? _tween;
  private float _currentRotationSpeed;
  private Vector3 _initialPosition;
  private Vector3 _recoilOffset = Vector3.Zero;
  private bool _isRecoiling;
  private float _cooldownLeft;
  public void PlayShootingSound() => _shootingSound.Play();
  public void PlayFullAutoReadySound() => _fullAutoReadySound.Play();
  public float CooldownFraction => 1.0f - _cooldownLeft / ShotCooldownSeconds;

  // Full-auto mode feedback (#58): red gun while active, switch sound on entry.
  // Entering full-auto also cancels any in-progress charge so its state, sound, &
  // spin speed don't linger through the burst.
  public void SetFullAutoMode (bool active)
  {
    _isFullAutoMode = active;
    if (active) _fullAutoSwitchSound.Play();
    IsSpinningUp = false;
    _fullChargeAnnounced = false;
    _chargingSound.Stop();
    _tween?.Kill();
    _currentRotationSpeed = MinRotationSpeed;
    WeaponColor = active ? FullAutoColor : _normalColor;
  }

  public void Charge()
  {
    if (_cooldownLeft > 0.0f || _isFullAutoMode) return;
    SpinUp();
  }

  // Cold-starts the weapon on respawn (issue #67): cancels the charge state, spin
  // tween, charging sound, & charged color instantly, so dying mid-charge can't
  // carry a max-energy shot into the next life.
  public void ResetCharge()
  {
    IsSpinningUp = false;
    _fullChargeAnnounced = false;
    _chargingSound.Stop();
    _tween?.Kill();
    _currentRotationSpeed = MinRotationSpeed;
    WeaponColor = _isFullAutoMode ? FullAutoColor : _normalColor;
  }
  private void Rotate (double delta) => _pivot.Rotate (Vector3.Right, _currentRotationSpeed * (float)delta);
  private bool IsRecoilRecovered() => _recoilOffset.Length() <= 0.01f;
  private float CalculateEnergy() => _currentRotationSpeed / MaxRotationSpeed;

  public override void _Ready()
  {
    _pivot = GetNode <Node3D> ("Pivot");
    _shootingSound = GetNode <AudioStreamPlayer3D> ("ShootingSound");
    _chargingSound = GetNode <AudioStreamPlayer3D> ("ChargingSound");
    _fullAutoSwitchSound = GetNode <AudioStreamPlayer3D> ("FullAutoSwitchSound");
    _fullAutoReadySound = GetNode <AudioStreamPlayer3D> ("FullAutoReadySound");
    _fullChargeClickSound = GetNode <AudioStreamPlayer3D> ("FullChargeClickSound");
    _muzzleMeshInstance = GetNode <Node3D> ("Pivot/Muzzle").GetNode <MeshInstance3D> ("Cube_001");
    _muzzleMaterial = CreateCopy ((_muzzleMeshInstance.Mesh.SurfaceGetMaterial (0) as StandardMaterial3D)!);
    _muzzleMeshInstance.MaterialOverride = _muzzleMaterial;
    _normalColor = _muzzleMaterial.AlbedoColor;
    _chargedColor = new Color (3.0f, 0.0f, _muzzleMaterial.AlbedoColor.B, _muzzleMaterial.AlbedoColor.A);
    WeaponColor = _normalColor;
    _currentRotationSpeed = MinRotationSpeed;
    _initialPosition = Position;
  }

  public override void _PhysicsProcess (double delta)
  {
    _cooldownLeft = Mathf.Max (0.0f, _cooldownLeft - (float)delta);
    Rotate (delta);
    Recoil (delta);
    AnnounceFullCharge();
  }

  // Crisp lock-in click the moment the charge maxes out, once per charge (issue #113).
  private void AnnounceFullCharge()
  {
    if (_fullChargeAnnounced || !IsFullyCharged) return;
    _fullChargeAnnounced = true;
    _fullChargeClickSound.Play();
    EmitSignal (SignalName.FullChargeReached);
  }

  private void Recoil (double delta)
  {
    if (!_isRecoiling) return;
    RecoverRecoil (delta);
    if (!IsRecoilRecovered()) return;
    ResetRecoil();
  }

  public void Discharge()
  {
    if (!IsSpinningUp) return;
    _cooldownLeft = ShotCooldownSeconds;
    PlayShootingSound();
    var energy = CalculateEnergy();
    EmitSignal (SignalName.ShotFired, energy, false);
    StartRecoil (energy);
    SpinDown();
  }

  // Full-auto shots: fixed low energy, no charge-up required.
  public void FireLowPower (float energy)
  {
    PlayShootingSound();
    EmitSignal (SignalName.ShotFired, energy, true);
    StartRecoil (energy);
  }

  private void SpinUp()
  {
    if (IsSpinningUp) return;
    IsSpinningUp = true;
    _fullChargeAnnounced = false;
    _chargingSound.Play();
    _tween?.Kill();
    _tween = CreateTween().SetParallel();
    _tween.TweenProperty (this, "_currentRotationSpeed", MaxRotationSpeed, 2.0f).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.Out);
    _tween.TweenProperty (this, "WeaponColor", _chargedColor, 2.0f).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.In);
  }

  private void SpinDown()
  {
    IsSpinningUp = false;
    _fullChargeAnnounced = false;
    _chargingSound.Stop();
    _tween?.Kill();
    _tween = CreateTween().SetParallel();
    _tween.TweenProperty (this, "_currentRotationSpeed", MinRotationSpeed, 2.0f).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.Out);
    _tween.TweenProperty (this, "WeaponColor", _normalColor, 2.0f).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.Out);
  }

  private void StartRecoil (float energy)
  {
    _isRecoiling = true;
    _recoilOffset = Transform.Basis.Z * RecoilStrength * energy;
  }

  private void RecoverRecoil (double delta)
  {
    if (!_isRecoiling) return;
    Position = _initialPosition + _recoilOffset;
    _recoilOffset = _recoilOffset.Lerp (Vector3.Zero, RecoilRecoverySpeed * (float)delta);
  }

  private void ResetRecoil()
  {
    Position = _initialPosition;
    _isRecoiling = false;
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
