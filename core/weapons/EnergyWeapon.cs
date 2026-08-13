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
  // Linear ramp to max rotation speed (issue #117): with the old ease-out tail the
  // full-charge click landed ~0.5s before the spin looked finished. Kept comfortably
  // under the 2.3s the playtest holds the trigger for a guaranteed full charge.
  [Export] public float ChargeUpSeconds = 1.8f;
  // Quick uncharged taps must never start the charging sound (issue #133); the
  // charge itself still begins immediately.
  [Export] public float ChargingSoundDelaySeconds = 0.2f;
  // The audible ramp tracks the charge itself (issue #117): pitch & volume follow
  // the spin-up so the sound peaks exactly when the rotation & lock-in do, instead
  // of the stream's own slow multi-second ramp trailing past the click.
  [Export] public float MinChargePitch = 0.9f;
  [Export] public float MaxChargePitch = 1.8f;
  [Export] public float MinChargeVolumeDb = -9.0f;
  [Export] public float MaxChargeVolumeDb = 0.0f;
  // Caps fire rate: after a shot, the weapon can't begin a new charge until the
  // cooldown elapses, so fast clicks & auto-clickers can't spam shots.
  [Export] public float ShotCooldownSeconds = 0.5f;
  [Export] public float RecoilStrength = 5.0f;
  [Export] public float RecoilRecoverySpeed = 5.0f;
  // Single source for the full-charge threshold (#93, #94, #105, #113): at this
  // energy the shot pierces, one-hit-kills, x-rays, & gets its own message pool.
  public const float FullChargeEnergyThreshold = 0.95f;
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
  private float _chargeAge;
  private bool _chargingSoundStarted;
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
  // 0..1 spin-up progress, for the audible pitch/volume ramp (issue #117).
  private float ChargeFraction() => (_currentRotationSpeed - MinRotationSpeed) / (MaxRotationSpeed - MinRotationSpeed);

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
    UpdateChargingSound (delta);
    AnnounceFullCharge();
  }

  // Charging sound (issues #117 & #133): starts only after a short held charge so
  // quick taps stay silent, then rides the spin-up with pitch & volume so it peaks
  // exactly when the rotation maxes out & the lock-in click fires.
  private void UpdateChargingSound (double delta)
  {
    if (!IsSpinningUp) return;
    _chargeAge += (float)delta;
    StartChargingSound();
    if (!_chargingSound.Playing) return;
    var ramp = Mathf.Clamp (ChargeFraction(), 0.0f, 1.0f);
    _chargingSound.PitchScale = Mathf.Lerp (MinChargePitch, MaxChargePitch, ramp);
    _chargingSound.VolumeDb = Mathf.Lerp (MinChargeVolumeDb, MaxChargeVolumeDb, ramp);
  }

  // Once per charge (issue #133): quick taps release before the delay elapses &
  // never hear it; holding a full charge past the stream's end doesn't loop it.
  private void StartChargingSound()
  {
    if (_chargingSoundStarted || _chargeAge < ChargingSoundDelaySeconds) return;
    _chargingSoundStarted = true;
    _chargingSound.Play();
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
    _chargeAge = 0.0f; // The sound starts later, gated by ChargingSoundDelaySeconds (issue #133).
    _chargingSoundStarted = false;
    _tween?.Kill();
    _tween = CreateTween().SetParallel();
    // Linear (issue #117): the threshold crossing, the rotation maxing out, & the
    // sound peak all land together at the end of the ramp.
    _tween.TweenProperty (this, "_currentRotationSpeed", MaxRotationSpeed, ChargeUpSeconds);
    _tween.TweenProperty (this, "WeaponColor", _chargedColor, ChargeUpSeconds).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.In);
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
