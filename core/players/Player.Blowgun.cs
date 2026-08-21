using com.forerunnergames.energyshot.ui.hud;
using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

// The blowgun (issue #194): the game's stealth weapon, funny because it has a SCOPE.
// Slot 8. Left click puffs a poison dart (no impact damage - the poison ticks are the
// weapon, see Player.Poison.cs); right click scopes. Stealth audio: the muzzle pfft
// is audible only within a few feet of the shooter, & everyone else only ever hears
// the dart's own short-range whoosh as it passes them (BlowgunDart).
public partial class Player
{
  [Export] public float BlowgunDartSpeed = 38.0f;
  [Export] public float BlowgunCooldownSeconds = 1.1f;
  [Export] public float ScopeFovDegrees = 30.0f;
  private Node3D _blowgunHeld = null!;
  private AudioStreamPlayer3D _blowgunShotSound = null!;
  private float _blowgunCooldownLeft;
  private float _unscopedFovDegrees;
  private bool _isScoped;

  public bool HasBlowgun => Holds (HeldWeapon.Blowgun);
  public bool IsBlowgunSelected => SelectedWeapon == SelectedWeapon.Blowgun;

  private void CreateBlowgunHeld()
  {
    _blowgunHeld = BlowgunDart.CreateBlowgunVisual();
    _blowgunHeld.Position = new Vector3 (0.32f, -0.22f, -0.55f);
    // Fetched directly: the held-model creators run before _Ready assigns _camera.
    var camera = GetNode <Camera3D> ("Camera3D");
    camera.AddChild (_blowgunHeld);
    _unscopedFovDegrees = camera.Fov; // Whatever the project default is - never hardcode it twice.
    // The muzzle pfft (issue #194): tiny UnitSize + hard MaxDistance cutoff = the
    // shooter & anyone within a few feet hear it; beyond that, silence.
    _blowgunShotSound = new AudioStreamPlayer3D { Stream = ProceduralSounds.DartPfft(), UnitSize = 1.2f, MaxDistance = 3.0f, MaxPolyphony = 3 };
    AddChild (_blowgunShotSound);
  }

  // Gated like every action predicate: input, stance, stun, & ritual states all block.
  private bool CanFireBlowgun() => _isInputEnabled && !Dancing && !Fallen && !Eating && IsBlowgunSelected && HasBlowgun && _blowgunCooldownLeft <= 0.0f;

  private void UpdateBlowgun (double delta)
  {
    _blowgunCooldownLeft = Mathf.Max (0.0f, _blowgunCooldownLeft - (float)delta);
    UpdateScope();
    if (!CanFireBlowgun() || !Input.IsActionJustPressed ("shoot")) return;
    FireBlowgunDart();
  }

  // Scope zoom on right click (issue #194; the button is free since #164 moved punch
  // to left click). Local camera state only - nothing about scoping replicates.
  private void UpdateScope()
  {
    var wantScope = _isInputEnabled && !Fallen && IsBlowgunSelected && HasBlowgun && Input.IsActionPressed ("scope");
    if (wantScope == _isScoped) return;
    _isScoped = wantScope;
    _camera.Fov = wantScope ? ScopeFovDegrees : _unscopedFovDegrees;
  }

  private void FireBlowgunDart()
  {
    _blowgunCooldownLeft = BlowgunCooldownSeconds;
    CancelSpawnArmorIfFired(); // Firing anything drops your spawn armor (issue #48).
    _blowgunShotSound.Play();
    var direction = -_camera.GlobalTransform.Basis.Z;
    var sweepStart = _camera.GlobalPosition;
    var origin = sweepStart + direction * MuzzleOffsetMeters;
    SpawnDart (origin, sweepStart, direction, isLive: true);
    Rpc (MethodName.SpawnVisualDart, origin, sweepStart, direction);
  }

  // Every peer flies a cosmetic copy, like SpawnVisualLaser & the visual stone: the
  // whoosh has to pass NEAR each listener locally for the stealth audio to work.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void SpawnVisualDart (Vector3 origin, Vector3 sweepStart, Vector3 direction) => SpawnDart (origin, sweepStart, direction, isLive: false);

  private void SpawnDart (Vector3 origin, Vector3 sweepStart, Vector3 direction, bool isLive)
  {
    var dart = new BlowgunDart();
    GetParent().AddChild (dart);
    dart.Launch (origin, sweepStart, direction, BlowgunDartSpeed, isLive, this);
    if (isLive) dart.HitPlayer += OnDartHitPlayer;
  }

  // Victim-authoritative like every damage path: the shooter only reports the hit.
  private void OnDartHitPlayer (Player victim)
  {
    _hitmarkerSound.Play();
    if (victim.NetworkId == Multiplayer.GetUniqueId()) return; // Can't dart yourself.
    victim.RpcId (victim.NetworkId, MethodName.ReceiveDartHit, DisplayName);
  }
}
