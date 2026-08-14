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
  // A world item slung out of a slingshot instead of a stone (issue #190).
  SlungItem,
  // The paper airplane's ignite-then-pop, slung into you (issue #191)...
  Airplane,
  // ...or set off by stepping on the grounded one (issue #191).
  Landmine
}
