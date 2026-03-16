# Campfire Trading

A Slay the Spire 2 multiplayer mod that lets players trade cards, potions, and relics at campfire rest sites.

## Features

- Trade cards, potions, and relics with other players at campfires
- Configurable slot limits per trade
- Optional unlimited trading mode
- Blocks quest cards and relics with special on-obtain effects from being traded (configurable)

## Configuration

All options are configurable in-game via the mod settings menu.

| Option | Values | Default | Description |
|--------|--------|---------|-------------|
| MaxCardSlots | 1–5 | 3 | Max cards per trade |
| MaxPotionSlots | 1–3 | 3 | Max potions per trade |
| MaxRelicSlots | 1–3 | 1 | Max relics per trade |
| UnlimitedTrades | On/Off | Off | Allow unlimited trades per rest site |
| BlockObtainHookRelics | On/Off | On | Prevent trading relics with on-obtain effects |
| BlockQuestCards | On/Off | On | Prevent trading quest cards |

## Installation

1. Install [BaseLib](https://github.com/Alchyr/BaseLib-StS2) to your mods folder
2. Copy `STS2Trade.dll`, `STS2Trade.json`, and `STS2Trade.pck` to `Slay the Spire 2/mods/STS2Trade/`
3. Enable the mod in-game

## Dependencies

- [BaseLib](https://github.com/Alchyr/BaseLib-StS2)

## Building

```
dotnet build -c Debug
dotnet publish -c Release
```

The build automatically copies the DLL and manifest to the game's mods folder. `dotnet publish` also exports the Godot `.pck` if MegaDot is configured.
