namespace com.forerunnergames.energyshot.weapons;

// What a player is carrying (issue #72). Flags so a player can hold the laser, the
// banana launcher, the boomerang (issue #98), & the slingshot (issue #99) at the
// same time; None = unarmed (fists only).
//
// The last three flags are not weapon slots. Bread (issue #190) rides the mask so
// the one-per-life loaf flows through the same pickup, drop, & death-drop machinery
// as the guns. Airplane (issue #191) & BananaChunk (issue #190) are never carried at
// all - they exist only as world items & as slingshot ammo (Player.SlingshotAmmo),
// which reuses this enum as its ammo vocabulary.
[System.Flags]
public enum HeldWeapon
{
  None = 0,
  Laser = 1,
  Banana = 2,
  Boomerang = 4,
  Slingshot = 8,
  Bread = 16,
  Airplane = 32,
  BananaChunk = 64
}
