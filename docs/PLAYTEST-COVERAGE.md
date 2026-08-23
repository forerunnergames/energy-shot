# Playtest coverage matrix (issue #316)

Every weapon x every mode it supports, mapped onto the CI playtest driver's actual
asserts (`tests/playtest/PlaytestDriver.cs`). Hand-audited at v0.8.112; **kept
current per feature PR** - a new weapon or mode lands with its row/cells updated
in the same PR (the feature-tests-required rule).

Legend: ✅ covered (a driver assert proves it) · 🟡 partial (some of the cell is
proven, the named remainder is not) · ❌ missing (no driver assert; unit tests
alone don't count as playtest coverage) · — n/a (the weapon has no such mode).
Evidence cells quote the assert's issue refs.

## Fists

| Mode | Status | Evidence / gap |
|---|---|---|
| spawned / loose / projectile | — | Fists are never a pickup |
| equipped firing (punch) | ✅ | calibration punch (#269), theft punch (#193), punch-cancels-eat (#192), wall self-damage is unit-tested |
| punch theft | ✅ | direct steal & the already-holding fly-out (#193) |
| punch knockback | ❌ | The 2.5x shove (#335) moves bodies in other phases' logs but no assert measures it |
| punch stun & cooldown | ❌ | Stacking slow (#68/#71) & the punch cooldown gate are unasserted |
| blunt hit | ✅ | The punch IS the fists' blunt; club comparisons calibrate against it (#269) |

## Laser

| Mode | Status | Evidence / gap |
|---|---|---|
| spawned | ✅ | spot restock (#72), pickup auto-equip (#128) |
| loose safe | ✅ | X-drop tossed pickup (#242), slung laser re-lands as pickup (#190) |
| loose armed | — | A grounded laser is never dangerous |
| projectile (bolt) | ✅ | bolt lands on the victim (#179), headshot exact-damage (#179) |
| equipped charging / charged | 🟡 | Full-charge one-hit kill is asserted (#93, the assert that caught the #268 recoil break); the charge LADDER (partial-charge damage scaling #67) is not |
| equipped firing | ✅ | fire-rate cap under spam, full-auto burst (#218), recoil climb & settle (#237/#351-era asserts) |
| equipped cooldown | ✅ | full-auto long cooldown (#299), respawn resets every cooldown (#299) |
| as slingshot ammo | ✅ | walk-load, slingshot-emptying shot, slung spray re-land (#190) |
| blunt | — | No club mode |

## Banana launcher

| Mode | Status | Evidence / gap |
|---|---|---|
| spawned | ✅ | playtest banana collected pre-kill (#169) |
| loose safe | ✅ | victim's dropped banana claimable at the death spot (#169) |
| projectile (lobbed banana) | ✅ | fired at the deck, bounce-fuse-boom hurts the parked victim (#83) |
| sticky banana & fuse | ✅ | direct hit launches the victim >8m, the fuse one-hit-kills (#83) |
| survivable-at-full-health blast | ✅ | full-health victim survives with >=1 & a stagger (#61/#70) |
| banana-grenade catch | ✅ | drawn slingshot nocks the live banana, fires it out, catcher lives (#251) |
| equipped firing feel | 🟡 | The launcher kick rides the recoil ledger (#237, unit-tested); the shooter knockback shove is unasserted |
| blunt | — | No club mode |

## Boomerang

| Mode | Status | Evidence / gap |
|---|---|---|
| spawned | ✅ | pickup collected, slot 4 select (#98) |
| loose safe | ✅ | nocked boomerang drops as its OWN pickup at the death spot (#212) |
| projectile (throw & return) | ✅ | thrown, returned & auto-caught (#98) |
| steal-on-hit | ❌ | The victim's held-item theft on a boomerang clip (#98/#106) is unasserted |
| as slingshot ammo | ✅ | nocked in the slingshot pre-kill, escrow drop (#212) |
| equipped cooldown | ❌ | The one-out-at-a-time gate is unasserted |
| blunt | — | No club mode |

## Slingshot

| Mode | Status | Evidence / gap |
|---|---|---|
| spawned | ✅ | pickup collect & auto-equip (#99, #128) |
| loose safe | ✅ | drops at the death spot (#212), respawn auto-claim ditch (#128) |
| equipped empty | ✅ | starts empty (#190), open-empty pouch is the armed-pickup loader (#286/#325) |
| equipped loaded | ✅ | laser load (#190), boomerang nock (#212), ARMED airplane load with fuse-window proof (#286/#325) |
| equipped charging (draw) | 🟡 | draw & release fires (#99); the full-draw LOCK-IN cue & tremble are #288 (pending feature - lands with its cells) |
| stone projectile | ✅ | long-flight & wall-stop stones (#163), full-draw stone flies 4s |
| slung-gun ammo spray | 🟡 | the slung LASER provably sprays bolts mid-flight (#208/#244); the launcher lob & stone spree remain unasserted |
| equipped cooldown | 🟡 | respawn reset covers it generically (#299); no slingshot-specific gate assert |
| blunt | — | No club mode |

## Paper airplane

| Mode | Status | Evidence / gap |
|---|---|---|
| spawned | ✅ | exactly-one census & restock (#102/#191) |
| loose safe (landed from a throw) | ✅ | landed airplane becomes a grounded pickup (#102) |
| loose armed (come-down mine) | ✅ | come-down is ARMED (#191), warning ring raise/pin/clear (#191), fuse ignite, burn ticks, pop, fire-out (#191) |
| projectile (glide) | 🟡 | thrown & replicated as a flying copy (#102); the homing lock (#191 targeting) is exercised (the mine-phase lane exists because gliders home) but never asserted directly |
| punch-catch | ✅ | catch confirmed by signal, catcher never ignites, warning clears (#102/#191) |
| as slingshot ammo (armed!) | ✅ | the OPEN slingshot loads the ARMED airplane, no detonation through the fuse window (#286/#325) |
| equipped | ✅ | slot 6 select (#102), equip for the mine-load phase (#325) |
| blunt | — | No club mode |

## Bread

| Mode | Status | Evidence / gap |
|---|---|---|
| spawned (per-life loaf) | ✅ | spawn carry, per-life restock (#62/#190) |
| loose safe | ✅ | uneaten loaf drops at the death spot (#190) |
| equipped | ✅ | own slot select, emptied slot falls back to fists (#209) |
| the eat ritual | ✅ | full-stop precondition, three-second ritual, no input escapes, completion consumes & heals (#192/#190) |
| eat interrupts | ✅ | punch cancels (attacker & victim sides), interrupted loaf wasted, rejected attempt costs nothing (#192) |
| theft target | ✅ | the loaf is the canonical theft item (#193) |
| as slingshot ammo (slung loaf) | ✅ | the pouch loads the victim's death-dropped loaf (own loaf blocks the normal collect, #190) & the slung hit lands like a punch, never a zap (#229/#247) |
| blunt | — | No club mode |

## Blowgun

| Mode | Status | Evidence / gap |
|---|---|---|
| spawned | ✅ | auto-equip into slot 8 (#128/#236), starts EMPTY (#236) |
| loose safe | ✅ | X-drop rests as a pickup, chase-park re-collect (#242/#316) |
| equipped empty | ✅ | fires nothing (#236), IS the club (#269) |
| equipped loaded | ✅ | floating-dart walk-load, fixture reload (#236) |
| equipped firing | ✅ | firing spends a dart (#236), dart sticks & count replicates (#194/#236) |
| equipped cooldown | 🟡 | club-phase retries exist BECAUSE of the fire cooldown; no direct gate assert |
| scope | ✅ | open/close, zoom ladder step, cycling suspended, reticle drift, own-hands hide (#236/#351) |
| blunt swing & hit | ✅ | club == exactly 2x the observed punch, gun never leaves the hands (#269) |
| drop scatter | ✅ | losing the blowgun returns its darts to the level (#236) |

## Poison darts (sub-item)

| Mode | Status | Evidence / gap |
|---|---|---|
| spawned (floating, unarmed) | ✅ | floating dart is blowgun ammo on walk-over (#236); hover height is unit-tested (#340) |
| loose armed (landed) | ✅ | floor hit lands ARMED (#236/#248), bystander-aware step phase (#248/#316) |
| embed & poison | ✅ | stick & replicate (#194), 10% tick with exact number (#194/#236), armor shrugs it off (#48 waits) |
| scatter on death | ✅ | dart scatter observed & asserted via the litter phases (#236) |
| drunk-walk, inverted steer, green vignette | ❌ | Poison's movement effects (#261/#277) are unit-tested (PoisonSteer) but have no driver assert |

## Banana chunks (sub-item)

| Mode | Status | Evidence / gap |
|---|---|---|
| all modes | ❌ | The chunk lifecycle (#284-#287 family) has no driver asserts at all - the emptiest row in the matrix |

## Cross-mechanic combos (the crazy-combinations doctrine)

Shipped combos with asserts: armed-airplane-into-open-pouch (#286/#325), punch-catch
of a wild airplane mid-flight (#102/#274), theft-during-eat (#192/#193), club on a
poisoned-then-cured host (#269/#316).

Queued combos, unasserted (each becomes a phase when its cell's weapon PR lands):

- A slung banana gun fired AT a jumping player must HIT them, not be picked up (Aaron's canonical example).
- Punch-catching an airplane while poisoned (wobble aim vs the 4m catch window).
- Scoped blowgun shot at a sliding player.
- Full-auto burst while riding a ring-rope bounce (recoil + rally physics).
- Eating bread inside the KOTH ring while under dart fire.

## Gap queue (risk-ordered, one weapon per PR, per the #316 plan)

1. **Slung launcher-lob & stone sprees** - the laser spray & slung loaf are proven; the launcher & slingshot payloads (#229/#270) are not.
2. **Boomerang steal-on-hit & cooldown** (#98/#106).
3. **Laser charge ladder** (#67).
4. **Fists knockback, stun & cooldown** (#68/#71/#335).
5. **Poison movement effects** (#261/#277).
6. **Banana chunks** (#284-#287) - lands with that epic.
7. **Combo phases** - one per queued combo above.
