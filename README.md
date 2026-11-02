# Comfort View

BepInEx mod that draws each nearby comfort item's 10m radius as a ring on the
ground, colored by category (fire, chair, table, bed, banner, rug).

![Item picker menu](https://raw.githubusercontent.com/the-grizzly-bear/valheim-comfort-view/master/screenshots/item-picker.webp)

![Nearby items list](https://raw.githubusercontent.com/the-grizzly-bear/valheim-comfort-view/master/screenshots/nearby-items.webp)

![Hover highlight](https://raw.githubusercontent.com/the-grizzly-bear/valheim-comfort-view/master/screenshots/hover-highlight.webp)

## Install

Drop `ComfortView.dll` into `BepInEx/plugins/ComfortView/`. Client-side only.

## Controls

- **F6** - toggle rings on/off
- **F7** - open/close the menu (Up/Down move, Right select/pin/toggle, Esc close). Left/Right on the tab row switches between:
  - **Items** - pick which item types to show
  - **Nearby** - every comfort item in range
  - **Active** - only the items actually counting toward your comfort level right now, with each one's +value and the total
- **F8** - pin/unpin the item you're looking at

Comfort range is a sphere, not a flat circle. Use the Shape toggle in the menu to see it that way.

## Build from source

```
dotnet build -c Release
```

Pass `-p:ValheimPath=/path/to/Valheim` if it's not in the default Steam location.
