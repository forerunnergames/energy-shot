# Controls

Current as of v0.8.15.

## Movement

| Input | Action |
|---|---|
| W A S D | Move |
| Mouse | Look / aim |
| Space | Jump (jumping out of a slide keeps its momentum & skips the cooldown - land into another slide to chain for speed, up to 2x) |
| Shift | Slide (a 3.5s burst, 5s cooldown; press slide or crouch to cancel early - the timer ends standing when there's headroom) |
| C | Crouch (toggle - press again to stand; switch to hold-to-crouch in the pause dialog, saved between sessions) |
| V | Toggle first-/third-person view (saved between sessions) |
| Mouse wheel (up = previous, down = next), or Q / E | Cycle through what you're carrying (skips slots you don't have) |

## Weapons

Slot keys select what's in your hands. You spawn with fists **and bread** (bread lives on 0 - or B - just left of fists, so one notch back on the wheel is always the loaf); every gun is picked up in the arena (picking one up auto-equips it - bread is the one exception, so a loaf never swaps a weapon out of your hands mid-fight). Every slot uses the same primary button: left click acts with whatever slot is selected; right click scopes the blowgun (slot 8) & does nothing elsewhere.

| Key | Weapon | How it works |
|---|---|---|
| 1 | Fists | Left-click to punch. Punching walls hurts *you*. 20% chance a landed punch knocks the victim's weapon loose |
| 2 | Laser | Left-click tap = quick shot. Hold = charge - a full charge (click sound + crosshair pop) pierces walls, one-hit zaps anyone, & shows enemies through walls while held |
| 3 | Banana launcher | Left-click to fire an arcing banana. Direct hits stick, launch the victim, & detonate. Massive recoil - it can also rocket-jump you |
| 4 | Boomerang | Left-click to throw. Curves out & returns; steals weapons from anyone it clips & scoops pickups it passes; auto-catches on return |
| 5 | Slingshot | Hold left-click to draw, release to fling a stone. Longer draw = faster, flatter, harder (never a one-hit). A quick tap just relaxes the band - stones need a minimum draw, & a short cooldown separates shots. **Universal ammo**: see below |
| 6 | Paper airplane | Left-click to throw. Locks onto whoever's under your crosshair & glides slowly after them; punch an incoming one (fists out) to catch it & throw it back. Only one exists in the whole game, & it is a personal hazard - see below |
| 0 or B | Bread | Left-click to eat: a 3-second rooted ritual that heals you to full. One loaf per life - see below |
| 8 | Blowgun | The silenced sniper - with a scope, because of course. Right-click to look THROUGH the scope (toggle by default; Esc > SETTINGS has a "Hold to scope" toggle, like hold-to-crouch; mouse wheel zooms in and out, very far); the whole scoped view sways with your heartbeat - more the further you zoom - while the red laser dot stays centered, and it only settles for about a second between heartbeats: time the shot for when the hazy dot snaps tight with a white-hot core. Left-click puffs a poison dart: fast but visible, nearly no drop, very long range, no recoil. Darts do no impact damage - they stick in and poison: 10% health per dart every 5 seconds, health bar turns green, your screen blurs and a green vignette pulses with every tick, and you walk drunk - the more darts, the worse - and your movement controls INVERT (forward is back, left is right) until the darts clear; bread heals but doesn't remove darts. **The gun starts empty**: there are 10 darts in the whole level - find them; out of darts, left-click swings it like a club (punch reach and stun, twice a punch's damage, but it never knocks anything loose or steals). See "Darts" below |

### Slingshot universal ammo

With the slingshot **out and empty**, walking onto any world item **loads it** instead of collecting it - another player's dropped laser, the banana launcher, the boomerang, even another slingshot, plus loose bread, banana chunks, and a grounded paper airplane. You can only ever load what's on the ground: your own equipped weapons stay in your hands.

- One item at a time. While something is nocked, normal pickup rules apply again.
- **Catch a live banana**: if a fired banana lands on you while you're actively DRAWING the slingshot, you catch it - nocked, fuse still ticking. Fire it straight back or it goes off in your pouch. Just holding the slingshot doesn't catch; you can catch your own.
- Any slung hit blurs the victim's screen like a punch; slung **bread** also plasters them in brown crumbs (not banana goo - crumbs).
- Slung items fly the same draw-scaled arc as a stone. Small things sting like a stone; **big things hit hard** - a slung gun deals roughly double the damage and knockback, the banana launcher triple (never a one-hit, even at full draw).
- **Every slung gun goes berserk**: a slung laser sprays full-auto shots, a slung banana launcher lobs bananas, a slung slingshot flings stones - all in random directions as it tumbles, until it lands. Dangerous to everyone near its flight path. (A slung blowgun is always empty - its darts went back into the level when it left your hands.)
- Wherever the item lands, it becomes an ordinary world pickup again - nothing is ever destroyed by being fired.
- Holster the slingshot (or fill it) if you'd rather just pick things up.

### Eating bread

Bread is on key 0 (or B) and you spawn carrying one loaf per life. **Left-click with it out to eat**, and then commit:

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
| Tab | Toggle message history (PageUp / PageDown to scroll; it never blocks your aim). Chat lines are archived here too, in the sender's color |
| X | Drop what's in your hands (Minecraft-style): the item flies out the way you're looking and lands as a pickup anyone can grab. Fists have nothing to drop, and a boomerang or airplane that's out flying isn't in your hands to drop |
| T | Open the chat line (bottom-left). Enter sends, Esc cancels; your character holds still while you type & the mouse stays captured. Lines show for ~9 s in the sender's color, max 120 characters, 2 per second |
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

## The boxing ring

The spawn box is a boxing ring: its walls are rubber ropes. Run, slide, or get punched into one and you bounce back the way you came with a big shove - the harder you hit, the harder it flings you; jump into the ropes and you can be thrown clean across the ring. Dive onto a rope top and it trampolines you - the harder you land, the bigger the bounce (each rebound a bit smaller, so you always settle) - while a gentle step just stands on it. Land on top of another player's head and you spring high into the air. Spawn armor & the room's protection work exactly as before.

## Headshots

**Temporarily on vacation** while the head gets rebuilt properly - no head, no headshots, back soon.

Every player has a floating sensor dome above the body. A laser bolt or a slingshot stone (or slung item) that hits the dome is a headshot: a flat 300 damage that ignores the difficulty handicap - one zaps an Expert or Intermediate outright, a Beginner takes exactly two. Punches, bananas, boomerangs, airplanes & darts don't care where they land. Your hitmarker rings higher on a dome hit.

## Darts

Ten darts exist in the level, no more, no less. A **floating, spinning** dart is a spawned pickup (it hovers well off the floor - no spawned item ever touches the ground): harmless, and only a player holding the blowgun can collect it (walk over it). A dart **lying flat** on the ground has landed - off a zapped-out victim, or a miss that hit a wall - and it's a hazard: step on it without the blowgun and it poisons you as if it hit you; with the blowgun in hand you pick it up as ammo. Slingshot players can load either kind and fire it; a slung dart poisons the same. Drop or lose the blowgun and its darts go back into the level. Darts that fall off the stage respawn.

## Settings

Press Esc for the pause dialog, then **SETTINGS**: hold-to-crouch, hold-to-scope, show/hide the music player, show/hide chat, and show/hide the game-message feed - all remembered between sessions, all applied instantly. Hiding chat never loses messages (Tab history keeps every line); Esc backs out of Settings, Esc again resumes.

## Fall damage

Dropping more than about 10 m hurts: 5 health per extra metre, never an instant zap-out. The respawn drop-in from the spawn room is free while your spawn armor is up; stay up there dithering and the 30 m trip costs you.

## Good to know

- White glow = spawn armor (5s of invulnerability after spawning; firing or punching cancels it).
- Getting zapped out drops your body at the death spot for ~5s - the camera pulls back so you can watch the aftermath - then you auto-respawn with spawn armor.
- Getting zapped out also drops **everything** you were carrying - weapons, your uneaten bread, and anything nocked in your slingshot - right where you fell. Dropped items expire after a few seconds. (A loaf ruined by an interrupted eat is gone, not dropped.)
- Kills heal you 50 HP. Falling off the world costs a point - your score can go negative.
- Difficulty picks your health pool (Beginner 400 / Intermediate 300 / Expert 200), and lower-tier players hit higher-tier players harder.

## Rounds

A round ends after **5 minutes or when somebody reaches 20 zaps**, whichever comes first (the clock sits top-center while at least two players are in). Then everyone freezes for 10 seconds on the scoreboard - zaps, zap-outs, assists (you damaged them within 10 s of someone else's zap), falls - with sarcastic superlatives, and a fresh round starts: scores zeroed, everybody respawned. Hosts set both limits in the Host Game dialog (0 = no limit); the dedicated server takes `--round-minutes N` and `--zap-limit N`.

**King of the Hill** (Host Game dialog "Game Mode", or `--mode koth` on the server): a glowing gold ring on top of the banana platform - 28 m up, reached by a rocket jump, a slide-jump chain or a head bounce - with a beam you can see from anywhere. Whoever stands in it **alone** earns a point every second; two or more inside is a contest and nobody scores. The clock shows who holds it. Same round limits (first to 20 points or 5 minutes), same scoreboard and superlatives - zaps still count for the Top Zapper title, but points come from the hill.
