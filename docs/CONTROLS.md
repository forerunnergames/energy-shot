# Controls

Current as of v0.8.15.

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

Slot keys select what's in your hands. You spawn with fists **and bread**; every gun is picked up in the arena (picking one up auto-equips it - bread is the one exception, so a loaf never swaps a weapon out of your hands mid-fight). Every slot uses the same primary button: left click acts with whatever slot is selected; right click is unbound (reserved).

| Key | Weapon | How it works |
|---|---|---|
| 1 | Fists | Left-click to punch. Punching walls hurts *you*. 20% chance a landed punch knocks the victim's weapon loose |
| 2 | Laser | Left-click tap = quick shot. Hold = charge - a full charge (click sound + crosshair pop) pierces walls, one-hit zaps anyone, & shows enemies through walls while held |
| 3 | Banana launcher | Left-click to fire an arcing banana. Direct hits stick, launch the victim, & detonate. Massive recoil - it can also rocket-jump you |
| 4 | Boomerang | Left-click to throw. Curves out & returns; steals weapons from anyone it clips & scoops pickups it passes; auto-catches on return |
| 5 | Slingshot | Hold left-click to draw, release to fling a stone. Longer draw = faster, flatter, harder (never a one-hit). A quick tap just relaxes the band - stones need a minimum draw, & a short cooldown separates shots. **Universal ammo**: see below |
| 6 | Paper airplane | Left-click to throw. Locks onto whoever's under your crosshair & glides slowly after them; punch an incoming one (fists out) to catch it & throw it back. Only one exists in the whole game, & it is a personal hazard - see below |
| 7 | Bread | Left-click to eat: a 3-second rooted ritual that heals you to full. One loaf per life - see below |

### Slingshot universal ammo

With the slingshot **out and empty**, walking onto any world item **loads it** instead of collecting it - another player's dropped laser, the banana launcher, the boomerang, even another slingshot, plus loose bread, banana chunks, and a grounded paper airplane. You can only ever load what's on the ground: your own equipped weapons stay in your hands.

- One item at a time. While something is nocked, normal pickup rules apply again.
- Slung items fly the same draw-scaled arc as a stone and sting about the same, with their own flavor on impact (bread bonks, banana splatters).
- Wherever the item lands, it becomes an ordinary world pickup again - nothing is ever destroyed by being fired.
- Holster the slingshot (or fill it) if you'd rather just pick things up.

### Eating bread

Bread is slot 7 and you spawn carrying one loaf per life. **Left-click with it out to eat**, and then commit:

- It takes **3 seconds**, drained on a reverse meter in the middle of your screen.
- You must be **standing still** to start. Eating while walking, sliding, or in mid-air is refused with an error cue and a line above the meter. You *can* equip the loaf mid-slide or mid-jump - the ritual just waits until the slide ends and you land and stop.
- While eating you **cannot move at all**: no walking, jumping, sliding, crouching, uncrouching, or switching slots. Looking around is all you get, and whatever stance you started in is locked in.
- **You can't cancel it. Anyone else can**: *any* hit ends the ritual, and the loaf is **wasted** - no heal, and no second loaf until you respawn.
- Finish it and you're healed to full.
- **Everyone can see you doing it**: the loaf goes up to your face, your body munches, crumbs fall, a tag pops up over your name, and the crunching is audible to anyone nearby. Eating in the open is a gamble.

## Abilities & extras

| Input | Action |
|---|---|
| F | Full-auto laser burst (3s of rapid low-power shots, 15s cooldown; needs the laser) |
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

There is exactly **one** paper airplane in the arena. It's a slot-6 weapon like any other - until you throw it, at which point it becomes a personal hazard for exactly one player.

- **Thrown**, it locks onto whoever was under your crosshair and glides after them. It's slow: a sprinting, weaving target escapes, a distracted one doesn't.
- **The target's screen only** fills with a big red ring that thickens and brightens as the airplane closes, then blinks with a beep that accelerates until impact. Nobody else sees or hears a thing.
- **If it reaches them**, that player catches fire for ~2 seconds and then pops. Strictly single-target - there is no blast radius, so standing next to a burning player is perfectly safe.
- **Punch it out of the air** (fists out) and you catch it instead: it goes straight into your hands, nobody ignites, and you can throw it back.
- **If the glide never finds anyone**, it comes down **armed** - a grounded landmine with a blinking red light. Step on it and it picks *you*: fastest beeping immediately, alight about a second later, then the same personal pop. Spawn armor keeps you safe from it.
- **With a slingshot equipped** you load an armed one as ammo instead of setting it off. A slung airplane flies fast and dead straight (no homing): hit a player and they ignite and pop exactly as a thrown hit would; hit anything else and it just falls and is a landmine again. Reload it as often as you like.
- A fresh airplane is folded somewhere in the arena every time the old one goes off - and a fresh one is a normal pickup, not a mine.

## Good to know

- White glow = spawn armor (5s of invulnerability after spawning; firing or punching cancels it).
- Getting zapped out drops your body at the death spot for ~5s - the camera pulls back so you can watch the aftermath - then you auto-respawn with spawn armor.
- Getting zapped out also drops **everything** you were carrying - weapons, your uneaten bread, and anything nocked in your slingshot - right where you fell. Dropped items expire after a few seconds. (A loaf ruined by an interrupted eat is gone, not dropped.)
- Kills heal you 50 HP. Falling off the world costs a point - your score can go negative.
- Difficulty picks your health pool (Beginner 400 / Intermediate 300 / Expert 200), and lower-tier players hit higher-tier players harder.
