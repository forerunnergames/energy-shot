# Controls

Current as of v0.8.14.

## Movement

| Input | Action |
|---|---|
| W A S D | Move |
| Mouse | Look / aim |
| Space | Jump (jumping out of a slide keeps its momentum & skips the cooldown - land into another slide to chain for speed, up to 2x) |
| Shift | Slide (up to 7s, 5s cooldown; press slide or crouch to cancel early - the timer ends standing when there's headroom) |
| C | Crouch (toggle - press again to stand; switch to hold-to-crouch in the pause dialog, saved between sessions) |
| V | Toggle first-/third-person view (saved between sessions) |

## Weapons

Slot keys select what's in your hands. You spawn with fists only; everything else is picked up in the arena (picking one up auto-equips it). Every weapon uses the same primary button: left click acts with whatever slot is selected; right click is unbound (reserved).

| Key | Weapon | How it works |
|---|---|---|
| 1 | Fists | Left-click to punch. Punching walls hurts *you*. 20% chance a landed punch knocks the victim's weapon loose |
| 2 | Laser | Left-click tap = quick shot. Hold = charge - a full charge (click sound + crosshair pop) pierces walls, one-hit zaps anyone, & shows enemies through walls while held |
| 3 | Banana launcher | Left-click to fire an arcing banana. Direct hits stick, launch the victim, & detonate. Massive recoil - it can also rocket-jump you |
| 4 | Boomerang | Left-click to throw. Curves out & returns; steals weapons from anyone it clips & scoops pickups it passes; auto-catches on return |
| 5 | Slingshot | Hold left-click to draw, release to fling a stone. Longer draw = faster, flatter, harder (never a one-hit). A quick tap just relaxes the band - stones need a minimum draw, & a short cooldown separates shots. **Universal ammo**: see below |

### Slingshot universal ammo

With the slingshot **out and empty**, walking onto any world item **loads it** instead of collecting it - another player's dropped laser, the banana launcher, the boomerang, even another slingshot, plus loose bread, banana chunks, and the grounded paper airplane. You can only ever load what's on the ground: your own equipped weapons stay in your hands.

- One item at a time. While something is nocked, normal pickup rules apply again.
- Slung items fly the same draw-scaled arc as a stone and sting about the same, with their own flavor on impact (bread bonks, banana splatters).
- Wherever the item lands, it becomes an ordinary world pickup again - nothing is ever destroyed by being fired.
- Holster the slingshot (or fill it) if you'd rather just pick things up.

## Abilities & extras

| Input | Action |
|---|---|
| F | Full-auto laser burst (3s of rapid low-power shots, 15s cooldown; needs the laser) |
| B | Eat bread - full heal, once per life. Get zapped out before you eat it & the loaf drops with everything else (grab someone else's & you're stocked again) |
| G | Dance. Blocks your weapons while grooving; any movement or taking a hit cancels it |
| Shoot the ground beneath you | Rocket boost upward, scaling with charge. Unlimited from the ground; while airborne it works once per airtime, re-armed when you land |

## Communication & music

| Input | Action |
|---|---|
| Tab | Toggle message history (PageUp / PageDown to scroll; it never blocks your aim) |
| . (period) | Thumbs-up the current music track |
| , (comma) | Thumbs-down (enough downvotes skips the track) |
| Esc | Pause / quit dialog |

## The paper airplane

There is exactly **one** paper airplane in the arena, and it is never a weapon you carry. It sits on the ground with a slowly blinking red light: an armed landmine.

- **Step on it** and it picks *you*, and only you. It flips into the air and swoops down: a big red ring fills your screen and beeps faster and faster, then you catch fire for ~2 seconds and pop. Nobody standing next to you is harmed - no blast radius at all.
- **Sprint away** and you can genuinely outrun the swoop; the airplane comes down wherever it gave up and re-arms itself there.
- **With a slingshot equipped** you load it instead of setting it off. A slung airplane flies fast and dead straight (no homing): hit a player and they ignite and pop exactly as if it had targeted them; hit anything else and it just deflects, falls, and is a landmine again. Reload it as often as you like.
- Spawn armor keeps you safe from it, so walking over one on the way out of the spawn room is free.
- Every time it goes off, a fresh one is folded somewhere in the arena.

## Good to know

- White glow = spawn armor (5s of invulnerability after spawning; firing or punching cancels it).
- Getting zapped out drops your body at the death spot for ~5s - the camera pulls back so you can watch the aftermath - then you auto-respawn with spawn armor.
- Getting zapped out also drops **everything** you were carrying - weapons, your uneaten bread, and anything nocked in your slingshot - right where you fell. Dropped items expire after a few seconds.
- Kills heal you 50 HP. Falling off the world costs a point - your score can go negative.
- Difficulty picks your health pool (Beginner 400 / Intermediate 300 / Expert 200), and lower-tier players hit higher-tier players harder.
