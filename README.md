# Energy Shot

A fast, friendly multiplayer laser-tag arena FPS built with Godot 4 (.NET/C#). No gore, no drama — get zapped, respawn, drop back in.

## How it plays

- **Charge-up lasers**: hold the trigger to spin up your energy weapon — the longer the charge, the faster, bigger, & meaner the bolt (blue → red). Release to fire a visible laser burst that travels (with a little drop), so shots can be dodged.
- **Punching**: with fists out (slot 1), left-click for close-range brawling. Getting punched blurs your screen for a bit.
- **Full-auto ability**: press F for 3 seconds of low-damage rapid fire (15 s cooldown).
- **Difficulty handicap**: pick Beginner / Intermediate / Expert when you join. Lower tiers get bigger health pools *and* hit higher tiers harder, so mixed-skill lobbies stay fair.
- **Spawn armor**: 5 seconds of invulnerability after every spawn (white glow). Firing or punching cancels it.
- **Spawn room**: you respawn in a room above the arena & drop back in when you're ready.
- **Leaderboard**, streak & zap messages with appropriately dry humor, low-health vignette, & more.

## Controls

Full reference: [docs/CONTROLS.md](docs/CONTROLS.md) (movement, all 6 weapon slots, abilities, music voting, & hidden mechanics).

| Input | Action |
|---|---|
| W A S D / Mouse / Space | Move, look, jump |
| Left mouse | Use selected weapon — punch with fists, hold to charge laser / draw slingshot |
| Right mouse | Unbound (reserved) |
| 1-6 | Fists, laser, banana, boomerang, slingshot, paper airplane |
| Shift / C / V | Slide, crouch, first-/third-person |
| F / B / G | Full-auto burst, eat bread, dance |
| Tab | Message history |
| . / , | Music vote up / down |
| Esc | Quit dialog |

## Multiplayer

The easiest way to play: **Join Game** — the official dedicated server address is pre-filled, so just pick a name & difficulty and jump in. (Releases deploy to the official server automatically when deploy credentials are configured.)

Prefer to host your own? Host from the main menu — UPnP discovers your address; enable UPnP on your router or forward UDP port **55556**.

Grab the latest build from [Releases](https://github.com/forerunnergames/energy-shot/releases) — macOS builds are signed & notarized.

## Development

- **Engine**: Godot 4.7.1 (.NET), C# / net9.0
- **Build**: `dotnet build`
- **Unit tests** (gdUnit4): `GODOT_BIN=<godot binary> ./addons/gdUnit4/runtest.sh -a tests`
- **Automated multiplayer playtest** (headless host + 2 clients exercising the full gameplay loop): `GODOT_BIN=<godot binary> ./tests/playtest/run-playtest.sh`

CI runs both suites on every push/PR; tagging `v*` builds, signs, notarizes, & publishes releases automatically.
