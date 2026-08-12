using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Weapon lifecycle & selection (issues #72 & #82): players spawn with fists only &
// arm up from world pickups; death drops everything held at the death spot. Slot 1 =
// fists (always available), slot 2 = laser, slot 3 = banana; guns are selectable only
// while held. The server-side WeaponSpawner owns pickup spawning & the weapon caps.
public partial class Player
{
  // Replicated like SpawnArmor so every peer knows what this player carries & renders
  // the right weapon model (or none while unarmed).
  [Export]
  public HeldWeapon HeldWeapon
  {
    get => _heldWeapon;
    set
    {
      _heldWeapon = value;
      UpdateWeaponVisibility();
    }
  }

  // Which slot is out (issue #82). Replicated so every peer renders the right model
  // (fist hands, laser, or banana launcher) on this player.
  [Export]
  public SelectedWeapon SelectedWeapon
  {
    get => _selectedWeapon;
    set
    {
      _selectedWeapon = value;
      UpdateWeaponVisibility();
    }
  }

  private HeldWeapon _heldWeapon = HeldWeapon.None;
  private SelectedWeapon _selectedWeapon = SelectedWeapon.Fists;
  // Theft revenge (issue #84): whose dropped weapon we most recently grabbed, so
  // the HUD can gloat when we zap its previous owner with it still in hand.
  private HeldWeapon _stolenWeapon = HeldWeapon.None;
  private string _stolenFrom = string.Empty;
  private WeaponSpawner? _weaponSpawner;
  public bool Holds (HeldWeapon type) => (_heldWeapon & type) != 0;
  private bool HasLaser => Holds (HeldWeapon.Laser);
  private bool HasBanana => Holds (HeldWeapon.Banana);
  private bool IsFistsSelected => _selectedWeapon == SelectedWeapon.Fists;
  private bool IsLaserSelected => _selectedWeapon == SelectedWeapon.Laser;
  private bool IsBananaSelected => _selectedWeapon == SelectedWeapon.Banana;
  private WeaponSpawner Spawner => _weaponSpawner ??= GetNode <WeaponSpawner> ("/root/World/WeaponSpawner");

  // Falling off the world: held weapons return to the spawn pool via the caps instead
  // of dropping as unreachable pickups below the map.
  private void ClearHeldWeapons()
  {
    ForgetTheft (_heldWeapon);
    HeldWeapon = HeldWeapon.None;
    SelectedWeapon = SelectedWeapon.Fists;
  }

  // Losing the stolen weapon ends the revenge window (CodeRabbit on #96): a fresh
  // pickup of the same type must not trigger a stale gloat.
  private void ForgetTheft (HeldWeapon lostTypes)
  {
    if ((_stolenWeapon & lostTypes) == 0) return;
    _stolenWeapon = HeldWeapon.None;
    _stolenFrom = string.Empty;
  }

  // Runs on every peer via the replicated HeldWeapon & SelectedWeapon properties.
  private void UpdateWeaponVisibility()
  {
    if (_bananaLauncher == null) return;
    _energyWeapon.Visible = IsLaserSelected && HasLaser;
    _bananaLauncher.Visible = IsBananaSelected && HasBanana;
    UpdateHandsVisibility(); // Hands render only while fists are selected (issue #82).
  }

  // Fists are always selectable; guns only while held (issue #82). No CanFire gate: a
  // cooling banana stays selected, it just can't fire yet (issue #83).
  private void UpdateWeaponSelection()
  {
    if (!_isInputEnabled) return;
    if (Input.IsActionJustPressed ("weapon_1")) SelectedWeapon = SelectedWeapon.Fists;
    if (Input.IsActionJustPressed ("weapon_2") && HasLaser) SelectedWeapon = SelectedWeapon.Laser;
    if (Input.IsActionJustPressed ("weapon_3") && HasBanana) SelectedWeapon = SelectedWeapon.Banana;
  }

  // A dropped or lost gun can't stay selected: fall back to fists (issue #82).
  private void DeselectUnheldWeapon()
  {
    if (IsLaserSelected && !HasLaser) SelectedWeapon = SelectedWeapon.Fists;
    if (IsBananaSelected && !HasBanana) SelectedWeapon = SelectedWeapon.Fists;
  }

  // Called back (via the WeaponSpawner's ConfirmPickup RPC) after the server despawns
  // the claimed pickup for everyone.
  public void GrantWeapon (HeldWeapon type, string previousOwner = "")
  {
    var wasUnarmed = _heldWeapon == HeldWeapon.None;
    HeldWeapon |= type;
    if (wasUnarmed) SelectedWeapon = type == HeldWeapon.Banana ? SelectedWeapon.Banana : SelectedWeapon.Laser; // Auto-equip your first gun.
    RememberTheft (type, previousOwner);
    _weaponPickupSound.Play(); // Satisfying pickup chime, owner-local only (issue #123).
    GD.Print ($"{DisplayName}: I picked up a {type}!");
  }

  private void RememberTheft (HeldWeapon type, string previousOwner)
  {
    if (previousOwner.Length == 0 || previousOwner == DisplayName) return;
    _stolenWeapon = type;
    _stolenFrom = previousOwner;
  }

  // One-shot check (issue #84): did this kill zap the previous owner of a weapon
  // we're still carrying? Clears after reporting so repeat kills don't keep gloating.
  public bool TookRevengeOn (string victimName)
  {
    if (victimName != _stolenFrom || !Holds (_stolenWeapon)) return false;
    _stolenWeapon = HeldWeapon.None;
    _stolenFrom = string.Empty;
    return true;
  }

  // Drops the selected gun (or the other one while boxing) as a world pickup; the
  // punch branch calls this with a drop chance when a player gets punched.
  public void DropHeldWeapon()
  {
    var type = IsBananaSelected ? HeldWeapon.Banana : HeldWeapon.Laser;
    if (!Holds (type)) type = type == HeldWeapon.Banana ? HeldWeapon.Laser : HeldWeapon.Banana;
    if (!Holds (type)) return;
    HeldWeapon &= ~type;
    ForgetTheft (type);
    DeselectUnheldWeapon();
    Spawner.SendDropRequest (GlobalPosition, type);
    GD.Print ($"{DisplayName}: I dropped my {type}!");
  }

  // Death drops everything carried at the death spot (issue #72).
  private void DropAllHeldWeapons()
  {
    if (_heldWeapon == HeldWeapon.None) return;
    Spawner.SendDropRequest (GlobalPosition, _heldWeapon);
    ClearHeldWeapons();
  }
}
