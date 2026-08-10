namespace com.forerunnergames.energyshot.weapons;

// The weapon slot a player has out (issue #82). Fists are slot 1 & always available;
// guns are selectable only while carried (see HeldWeapon).
public enum SelectedWeapon
{
  Fists = 0,
  Laser = 1,
  Banana = 2
}
