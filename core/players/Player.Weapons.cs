using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Weapon lifecycle (issue #72): players spawn unarmed & arm up from world pickups;
// death drops everything held at the death spot. The server-side WeaponSpawner owns
// pickup spawning, despawning, & the laser/banana caps.
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

  private HeldWeapon _heldWeapon = HeldWeapon.None;
  // Theft revenge (issue #84): whose dropped weapon we most recently grabbed, so
  // the HUD can gloat when we zap its previous owner with it still in hand.
  private HeldWeapon _stolenWeapon = HeldWeapon.None;
  private string _stolenFrom = string.Empty;
  private WeaponSpawner? _weaponSpawner;
  public bool Holds (HeldWeapon type) => (_heldWeapon & type) != 0;
  private bool HasLaser => Holds (HeldWeapon.Laser);
  private bool HasBanana => Holds (HeldWeapon.Banana);
  private WeaponSpawner Spawner => _weaponSpawner ??= GetNode <WeaponSpawner> ("/root/World/WeaponSpawner");
  // Falling off the world: held weapons return to the spawn pool via the caps instead
  // of dropping as unreachable pickups below the map.
  private void ClearHeldWeapons()
  {
    ForgetTheft (_heldWeapon);
    HeldWeapon = HeldWeapon.None;
  }

  // Losing the stolen weapon ends the revenge window (CodeRabbit on #96): a fresh
  // pickup of the same type must not trigger a stale gloat.
  private void ForgetTheft (HeldWeapon lostTypes)
  {
    if ((_stolenWeapon & lostTypes) == 0) return;
    _stolenWeapon = HeldWeapon.None;
    _stolenFrom = string.Empty;
  }

  // Called back (via the WeaponSpawner's ConfirmPickup RPC) after the server despawns
  // the claimed pickup for everyone.
  public void GrantWeapon (HeldWeapon type, string previousOwner = "")
  {
    var wasUnarmed = _heldWeapon == HeldWeapon.None;
    HeldWeapon |= type;
    if (wasUnarmed) IsBananaEquipped = type == HeldWeapon.Banana; // Auto-equip your first weapon.
    RememberTheft (type, previousOwner);
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

  // Drops the currently equipped weapon as a world pickup; the punch branch calls
  // this with a drop chance when a player gets punched.
  public void DropHeldWeapon()
  {
    var type = IsBananaEquipped ? HeldWeapon.Banana : HeldWeapon.Laser;
    if (!Holds (type)) type = IsBananaEquipped ? HeldWeapon.Laser : HeldWeapon.Banana;
    if (!Holds (type)) return;
    HeldWeapon &= ~type;
    ForgetTheft (type);
    Spawner.SendDropRequest (GlobalPosition, type);
    GD.Print ($"{DisplayName}: I dropped my {type}!");
  }

  // Death drops everything carried at the death spot (issue #72).
  private void DropAllHeldWeapons()
  {
    if (_heldWeapon == HeldWeapon.None) return;
    Spawner.SendDropRequest (GlobalPosition, _heldWeapon);
    ForgetTheft (_heldWeapon);
    HeldWeapon = HeldWeapon.None;
  }
}
