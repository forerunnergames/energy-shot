namespace com.forerunnergames.energyshot.weapons;

// What a player is carrying (issue #72). Flags so a player can hold the laser, the
// banana launcher, the boomerang (issue #98), the slingshot (issue #99), & the
// paper airplane (issue #102) at the same time; None = unarmed (fists only).
//
// The trailing non-slot flags: Bread (issue #190) rides the mask so the
// one-per-life loaf flows through the same pickup, drop, & death-drop machinery as
// the guns. BananaChunk (issue #190) is never carried at all - it exists only as
// slingshot ammo (Player.SlingshotAmmo), which reuses this enum as its ammo
// vocabulary so a nocked item & a held one are named the same thing. PoisonDart
// (issue #194) is likewise never a slot: it exists embedded in a victim, as a
// short-lived ground pickup off a body, & as slingshot ammo.
[System.Flags]
public enum HeldWeapon
{
  None = 0,
  Laser = 1,
  Banana = 2,
  Boomerang = 4,
  Slingshot = 8,
  PaperAirplane = 16,
  Bread = 32,
  BananaChunk = 64,
  Blowgun = 128,
  PoisonDart = 256
}
