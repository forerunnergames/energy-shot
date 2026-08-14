using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Slingshot (issue #99): the slot-5 ballistic sidearm. Hold shoot to draw the band
// (like the laser's charge, but ballistic), release to sling an arcing stone; drawn
// longer = flatter, faster, & harder (draw scales speed & damage, 15-60). Hits are
// victim-authoritative with knockback, same as ReceiveHit & ReceiveBoomerangHit.
//
// Universal ammo (issue #190): with the slingshot equipped & empty, walking onto ANY
// world item loads it instead of collecting it - other weapons (another slingshot
// included), bread, banana chunks, & the grounded paper airplane. The slung item
// flies the same draw-scaled ballistics & becomes a normal pickup again wherever it
// lands, so nothing ever duplicates or vanishes. Your own equipped weapons are never
// loadable: ammo only ever comes off the ground.
public partial class Player
{
  // What's nocked right now (issue #190), None = a plain stone. Replicated like
  // HeldWeapon so every peer renders the loaded item in this player's slingshot &
  // the server can validate load/fire requests against it instead of client claims.
  [Export]
  public HeldWeapon SlingshotAmmo
  {
    get => _slingshotAmmo;
    set
    {
      _slingshotAmmo = value;
      UpdateSlingshotAmmoVisual();
    }
  }

  private HeldWeapon _slingshotAmmo = HeldWeapon.None;
  [Export] public float SlingshotMaxDrawSeconds = 1.2f;
  // Fire-rate cap (issue #163): a sub-minimum release just relaxes the band (no
  // stone), & the band needs a beat between shots, so tap-spam does nothing.
  [Export] public float SlingshotMinDrawSeconds = 0.2f;
  [Export] public float SlingshotCooldownSeconds = 0.5f;
  [Export] public float SlingshotMinSpeed = 22.0f;
  // Raised from 60 (issue #163): a full draw is a genuinely long shot.
  [Export] public float SlingshotMaxSpeed = 90.0f;
  // 15-60 damage via CalculateHealthDecrease (issue #99): a nuisance at a tap,
  // a proper wallop at full draw, never a one-hit zap-out.
  [Export] public float SlingshotMinEnergy = 0.15f;
  [Export] public float SlingshotMaxEnergy = 0.6f;
  [Export] public float SlingshotKnockbackScale = 0.6f;
  // Arc flattening (issue #163): stone gravity eases off as the draw rises, so
  // full-draw stones fly flat long shots while taps stay lobbed.
  [Export] public float SlingshotMinDrawGravity = 24.0f;
  [Export] public float SlingshotMaxDrawGravity = 10.0f;
  // A slung paper airplane flies fast & dead straight - no arc, no homing (issue #191).
  [Export] public float SlungAirplaneSpeed = 110.0f;
  // How far the held frame pulls back toward the eye at full draw (issue #99).
  private const float SlingshotDrawPullMeters = 0.25f;
  private static readonly Vector3 SlingshotRestPosition = new(0.5f, -0.5f, -0.9f);
  private Node3D _slingshotHeld = null!;
  private Node3D? _nockedAmmoVisual;
  private AudioStreamPlayer _slingshotStretchSound = null!;
  private float _slingshotDrawSeconds;
  private float _slingshotCooldownLeft;
  private SlingshotStone? _ammoStone;
  private float SlingshotDrawFraction => Mathf.Clamp (_slingshotDrawSeconds / SlingshotMaxDrawSeconds, 0.0f, 1.0f);
  // Ammo only loads while the slingshot is actually out & empty (issue #190); a
  // loaded (or holstered) slingshot leaves normal pickup rules alone.
  public bool IsLoadingAmmo => HasSlingshot && IsSlingshotSelected && SlingshotAmmo == HeldWeapon.None && !Fallen;
  // Cosmetic ammo (banana chunks) is scenery no cap tracks: it splatters & is gone.
  private static bool IsCosmeticAmmo (HeldWeapon ammo) => ammo == HeldWeapon.BananaChunk;

  // Called back by the server (WeaponSpawner.ConfirmAmmoLoad) once it has despawned
  // the claimed world item for everyone (issue #190).
  public void LoadSlingshotAmmo (HeldWeapon type)
  {
    SlingshotAmmo = type;
    _weaponPickupSound.Play(); // Same satisfying chime as a pickup, owner-local (issue #123).
    GD.Print ($"{DisplayName}: I loaded a {type} into my slingshot!");
  }

  // Banana chunks never left the cosmetic-debris world, so there's nothing for the
  // server to despawn or count - the local peer just nocks one (issue #190).
  public void LoadCosmeticAmmo (HeldWeapon type)
  {
    if (!IsLoadingAmmo) return;
    LoadSlingshotAmmo (type);
  }

  // The nocked stone is swapped for whatever is loaded, on every peer (issue #190).
  private void UpdateSlingshotAmmoVisual()
  {
    if (_slingshotHeld == null) return;

    // Detached before freeing: QueueFree is deferred, & a lingering same-named child
    // would make PoseBand pose the OLD item on the pouch for a frame.
    if (_nockedAmmoVisual != null)
    {
      _slingshotHeld.RemoveChild (_nockedAmmoVisual);
      _nockedAmmoVisual.QueueFree();
      _nockedAmmoVisual = null;
    }

    _slingshotHeld.GetNode <MeshInstance3D> ("NockedStone").Visible = _slingshotAmmo == HeldWeapon.None;
    if (_slingshotAmmo == HeldWeapon.None) return;
    _nockedAmmoVisual = SlingshotStone.CreateAmmoVisual (_slingshotAmmo);
    _nockedAmmoVisual.Name = SlingshotStone.NockedAmmoNodeName; // PoseBand rides it on the pouch.
    _nockedAmmoVisual.Scale *= 0.55f;
    _slingshotHeld.AddChild (_nockedAmmoVisual);
    ApplySlingshotDrawPose();
  }

  // Dying (or dropping off the world) with something nocked lands it where we stand;
  // over the void the server finds no ground & the caps bring the item back instead.
  private void DropLoadedAmmo()
  {
    if (SlingshotAmmo == HeldWeapon.None) return;
    if (!IsCosmeticAmmo (SlingshotAmmo)) Spawner.SendAmmoLandRequest (GlobalPosition);
    SlingshotAmmo = HeldWeapon.None;
  }

  // Held model: the same code-built Y-frame as the pickup, resting in the hand
  // (issue #99). The stretch creak is the punch whiff slowed way down - reusing an
  // existing sound instead of downloading one.
  private void CreateSlingshotHeld()
  {
    _slingshotHeld = SlingshotStone.CreateSlingshotVisual();
    _slingshotHeld.Position = SlingshotRestPosition;
    _slingshotHeld.RotationDegrees = new Vector3 (0.0f, 10.0f, -8.0f);
    GetNode <Node3D> ("Camera3D").AddChild (_slingshotHeld);
    // MaxPolyphony 4 (issue #182): quick draw-cancel-draw retriggers inside the slowed creak's tail.
    _slingshotStretchSound = new AudioStreamPlayer { Stream = ResourceLoader.Load <AudioStream> ("res://assets/sounds/punch-whiff.wav"), PitchScale = 0.55f, MaxPolyphony = 4 };
    AddChild (_slingshotStretchSound);
  }

  // Draw-&-release (issue #99): holding shoot accumulates draw, releasing fires.
  // Poll-based (not just-released) so a wedged input state self-heals, & losing the
  // slingshot (or selection) mid-draw cancels cleanly.
  private void UpdateSlingshot (double delta)
  {
    _slingshotCooldownLeft = Mathf.Max (0.0f, _slingshotCooldownLeft - (float)delta);
    var active = IsSlingshotSelected && HasSlingshot && _isInputEnabled;
    if (!active) { CancelSlingshotDraw(); return; }
    if (Input.IsActionPressed ("shoot")) { AccumulateSlingshotDraw ((float)delta); return; }
    if (_slingshotDrawSeconds <= 0.0f) return;
    FireSlingshotStone();
  }

  private void AccumulateSlingshotDraw (float dt)
  {
    if (_slingshotCooldownLeft > 0.0f) return; // Fire-rate cap (issue #163): no new draw mid-cooldown.
    if (_slingshotDrawSeconds <= 0.0f) _slingshotStretchSound.Play(); // Once per draw.
    _slingshotDrawSeconds = Mathf.Min (SlingshotMaxDrawSeconds, _slingshotDrawSeconds + dt);
    ApplySlingshotDrawPose();
  }

  private void CancelSlingshotDraw()
  {
    if (_slingshotDrawSeconds <= 0.0f) return;
    _slingshotDrawSeconds = 0.0f;
    _slingshotStretchSound.Stop();
    ApplySlingshotDrawPose();
  }

  // The frame pulls back toward the eye as the band stretches (issue #99), the
  // nocked stone & pouch pull further back with the draw, & the band halves stretch
  // with them - snapping forward when the draw resets to zero (issue #163).
  private void ApplySlingshotDrawPose()
  {
    _slingshotHeld.Position = SlingshotRestPosition + Vector3.Back * (SlingshotDrawPullMeters * SlingshotDrawFraction);
    SlingshotStone.PoseBand (_slingshotHeld, SlingshotDrawFraction);
  }

  private void FireSlingshotStone()
  {
    var draw = SlingshotDrawFraction;
    var drawSeconds = _slingshotDrawSeconds;
    CancelSlingshotDraw();
    if (drawSeconds < SlingshotMinDrawSeconds) return; // Sub-minimum release: the band just relaxes, no shot (issue #163).
    CancelSpawnArmorIfFired();
    _slingshotCooldownLeft = SlingshotCooldownSeconds; // Fire-rate cap (issue #163).
    var ammo = SlingshotAmmo;
    var speed = Mathf.Lerp (SlingshotMinSpeed, SlingshotMaxSpeed, draw);
    var energy = Mathf.Lerp (SlingshotMinEnergy, SlingshotMaxEnergy, draw);
    var gravity = Mathf.Lerp (SlingshotMinDrawGravity, SlingshotMaxDrawGravity, draw); // Flatter arc at full draw (issue #163).
    // The airplane defines its own ballistics (issue #191): fast, straight, & it
    // ignites whoever it hits instead of dealing the stone's draw-scaled damage.
    if (ammo == HeldWeapon.Airplane) { speed = SlungAirplaneSpeed; gravity = 0.0f; }
    var direction = -_camera.GlobalTransform.Basis.Z;
    var sweepStart = _camera.GlobalPosition; // First sweep covers camera->muzzle (issues #112 & #163).
    var origin = sweepStart + direction * MuzzleOffsetMeters;
    SpawnStone (origin, sweepStart, direction, speed, gravity, energy, isLive: true, ammo);
    Rpc (MethodName.SpawnVisualStone, origin, sweepStart, direction, speed, gravity, (int)ammo);
    SlingshotAmmo = HeldWeapon.None; // The item is airborne now; the server's escrow holds it until it lands.
  }

  // Visual-only copy of the shooter's stone on every other peer. Firing proves the
  // shooter's spawn armor is gone, so stale armor whitewash clears here (issue #114).
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void SpawnVisualStone (Vector3 origin, Vector3 sweepStart, Vector3 direction, float speed, float gravity, int ammo)
  {
    ClearArmorDisplayOnRemoteAttack();
    SpawnStone (origin, sweepStart, direction, speed, gravity, energy: 0.0f, isLive: false, (HeldWeapon)ammo);
  }

  private void SpawnStone (Vector3 origin, Vector3 sweepStart, Vector3 direction, float speed, float gravity, float energy, bool isLive, HeldWeapon ammo)
  {
    PlaySlingshotThwack (origin);
    var stone = new SlingshotStone { Ammo = ammo };
    GetParent().AddChild (stone);
    stone.Launch (origin, sweepStart, direction, speed, gravity, energy, isLive, this);
    if (!isLive) return;
    if (ammo == HeldWeapon.Airplane) stone.HitPlayer += (victim, _) => OnSlungAirplaneHitPlayer (stone, victim);
    else stone.HitPlayer += (victim, hitEnergy) => OnStoneHitPlayer (victim, hitEnergy, ammo);
    if (ammo == HeldWeapon.None || IsCosmeticAmmo (ammo)) return;
    // Only the newest flight owns this player's server-side ammo escrow, so an older
    // stone still arcing somewhere can never land somebody else's item (issue #190).
    _ammoStone = stone;
    stone.Landed += position => OnSlungAmmoLanded (stone, position);
  }

  // A slung item that came to rest (or clipped somebody & dropped there) becomes a
  // world pickup again. The airplane is the exception: a strike consumes it into the
  // burn sequence, & only a MISS re-arms it as the landmine (issue #191).
  private void OnSlungAmmoLanded (SlingshotStone stone, Vector3 position)
  {
    if (_ammoStone != stone) return;
    _ammoStone = null;
    Spawner.SendAmmoLandRequest (position);
  }

  // A slung airplane found a player: no stone damage, no blast - just this one
  // player, lit up & then popped (issue #191). The server validates that we really
  // had the airplane nocked before it tells the target to ignite.
  private void OnSlungAirplaneHitPlayer (SlingshotStone stone, Player victim)
  {
    if (_ammoStone == stone) _ammoStone = null; // The strike consumes it: no landing, no pickup.
    _hitmarkerSound.Play();
    ReportToServer ($"airplane: {DisplayName} slung the paper airplane into {victim.DisplayName}");
    Spawner.SendAirplaneStrikeRequest (victim.NetworkId);
  }

  // Distinct release thwack (issue #99): the punch thud replayed fast & positional
  // reads as the band snapping - reusing an existing sound, never downloading one.
  private void PlaySlingshotThwack (Vector3 origin)
  {
    var thwack = new AudioStreamPlayer3D { Stream = ResourceLoader.Load <AudioStream> ("res://assets/sounds/punch-thud.wav"), PitchScale = 1.6f };
    GetParent().AddChild (thwack);
    thwack.GlobalPosition = origin;
    thwack.Finished += thwack.QueueFree;
    thwack.Play();
  }

  // The shooter only reports the hit; the victim applies its own damage & knockback
  // (victim-authoritative, same as ReceiveHit & ReceiveBoomerangHit). Slung world
  // items sting exactly like the stone baseline (issue #190) - only the flavor & the
  // death message change.
  private void OnStoneHitPlayer (Player victim, float energy, HeldWeapon ammo)
  {
    if (victim.NetworkId == NetworkId) return;
    var what = ammo == HeldWeapon.None ? "stone" : ammo.ToString().ToLower();
    GD.Print ($"{DisplayName}: My {what} thwacked {victim.DisplayName}!");
    _hitmarkerSound.Play();
    ReportToServer ($"slingshot: {DisplayName} thwacked {victim.DisplayName} with a slung {what} (energy {energy:0.00})");
    victim.RpcId (victim.NetworkId, MethodName.ReceiveSlingshotHit, energy, DisplayName, (int)ammo);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ReceiveSlingshotHit (float energy, string slungByPlayerName, int ammo)
  {
    if (!IsMultiplayerAuthority()) return;
    if (SpawnArmor) return;
    if (Fallen) return; // A body mid-death-sequence is scenery (issue #152).
    GD.Print ($"{DisplayName}: I was thwacked by {slungByPlayerName}'s slingshot!");
    // Getting zapped out by a slung LOAF deserves its own line (issue #190).
    LastDamageKind = (HeldWeapon)ammo == HeldWeapon.None ? DamageKind.Slingshot : DamageKind.SlungItem; // Message context (issue #84).
    ApplyDamage (energy, slungByPlayerName, SlingshotKnockbackScale);
  }
}
