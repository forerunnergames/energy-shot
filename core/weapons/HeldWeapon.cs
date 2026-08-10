namespace com.forerunnergames.energyshot.weapons;

// What a player is carrying (issue #72). Flags so a player can hold the laser & the
// banana launcher at the same time; None = unarmed (fists only).
[System.Flags]
public enum HeldWeapon
{
  None = 0,
  Laser = 1,
  Banana = 2
}
