namespace com.forerunnergames.energyshot.players;

// What last damaged a player (issue #84): recorded by the victim in the receive-hit
// RPCs so the HUD can pick a weapon-flavored respawn message at death time.
public enum DamageKind
{
  None,
  Laser,
  FullAuto,
  Punch,
  Banana,
  Boomerang,
  Slingshot,
  PaperAirplane
}
