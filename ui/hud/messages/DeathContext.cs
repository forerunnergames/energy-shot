using com.forerunnergames.energyshot.players;

namespace com.forerunnergames.energyshot.ui.hud;

// Everything the victim's peer knows about how it got zapped out (issue #84), so
// MessageGenerator can pick the right scenario pool. Built by the HUD at message
// time from the victim's own death snapshot + the killer's replicated node.
public readonly record struct DeathContext (
  DamageKind Kind,
  float Energy,
  bool VictimSliding,
  bool VictimArmed,
  bool VictimHeldBananaGun,
  // Caught mid-bread-ritual (issue #192): rooted, defenceless, & holding lunch.
  bool VictimEating,
  int VictimLostStreak,
  bool KillerSliding,
  bool KillerAirborne,
  bool KillerUnarmed,
  bool SplatterActive,
  bool BlurActive,
  bool ThroughBarrier);
