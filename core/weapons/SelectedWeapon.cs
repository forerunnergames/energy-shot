namespace com.forerunnergames.energyshot.weapons;

// The weapon slot a player has out (issue #82). Fists are slot 1 & always available;
// guns are selectable only while carried (see HeldWeapon). Bread is slot 7 (issue
// #209): not a gun, but a real slot you spawn with, equip, & use with primary fire.
public enum SelectedWeapon
{
  Fists = 0,
  Laser = 1,
  Banana = 2,
  Boomerang = 3,
  Slingshot = 4,
  PaperAirplane = 5,
  Bread = 6,
  Blowgun = 7 // The scoped stealth weapon (issue #194), key 8.
}
