# 自动出牌 / 狂吼耳环 (sts2-roaring-earring)

A Slay the Spire 2 (v0.113.1) mod: an Ancient relic of Vakuu that hands every one of your turns to the
whisper - it gains you extra energy and plays your cards for you.

## Behaviour

- **Energy** — `ModifyMaxEnergy` adds `energy_per_turn` to the owner's max energy, through
  `DynamicVars.Energy` exactly like the vanilla `WhisperingEarring`.
- **Turn takeover** — `AfterAutoPrePlayPhaseEnteredLate` pushes a `VakuuCardSelector` for the owner and
  auto-plays up to `max_cards_per_turn` cards on **every** turn (the vanilla first-turn-only guard is gone).
  The loop stops when the combat is over or ending, the turn has already changed, the player is ready to end
  the turn, or no playable card is left.
- **Card order** — `play_order`: `left_to_right` (hand order), `right_to_left`, or `random` drawn from
  `RunState.Rng.CombatCardSelection` (the multiplayer-synchronised combat RNG, never `System.Random`).
- **Targets** — same rules as Vakuu's own auto-play: first hittable enemy, a random living teammate from
  `Rng.CombatTargets`, or the owner for `AnyPlayer` cards.
- **Auto end turn** — with `auto_end_turn` on, the relic ends the turn itself after reaching the card cap or
  running out of playable cards (`PlayerCmd.EndTurn(owner, canBackOut: false)`, so it cannot be taken back).
  A finished combat or an already-changed turn never triggers it.
- **Relic pools** — the relic is in **no** pool: it comes from `start_with_relic` only (and, in a later
  wave, from the Vakuu event).

## Config

`<game exe dir>/mods/config/sts2-roaring-earring.cfg` — JSON content despite the `.cfg` extension. Missing
or malformed values fall back to the defaults below; the file is created on the first `Save()`.

| key | default | notes |
| --- | --- | --- |
| `energy_per_turn` | `1` | clamped to 0..9 |
| `max_cards_per_turn` | `50` | clamped to 1..100 |
| `play_order` | `"left_to_right"` | `left_to_right` \| `right_to_left` \| `random` |
| `auto_end_turn` | `false` | end the turn once the loop runs dry |
| `start_with_relic` | `true` | grant the relic at the start of a new run |

A sample file:

```json
{
  "energy_per_turn": 1,
  "max_cards_per_turn": 50,
  "play_order": "left_to_right",
  "auto_end_turn": false,
  "start_with_relic": true
}
```

## Build

```powershell
dotnet build -c Release
```

The build stages `sts2-roaring-earring.dll` and `sts2-roaring-earring.json` into
`dist/sts2-roaring-earring/`. `GameDataDir` in the csproj points at the game install
(`...\Slay the Spire 2\data_sts2_windows_x86_64`); override it on the command line if your install differs:

```powershell
dotnet build -c Release -p:GameDataDir="D:\path\to\data_sts2_windows_x86_64"
```

## Install

```powershell
.\install.ps1            # copies dist/sts2-roaring-earring into the game's mods directory
.\install.ps1 -Yes       # no confirmation prompt
```

Check the mod's log lines after a launch:

```powershell
.\tools\check-log.ps1
```

## Art

The three relic PNGs live in `assets/icons` and are embedded as `Sts2RoaringEarring.Art.*` resources. A
missing or undecodable resource logs one line and leaves the vanilla icon in place, so the mod never fails to
load because of art.

## Status

`0.1.0-beta` — relic behaviour + skeleton. Not yet implemented: Vakuu event injection, settings UI.
