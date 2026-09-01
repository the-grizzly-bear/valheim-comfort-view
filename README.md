# Comfort View

BepInEx mod that draws each nearby comfort item's 10m radius as a ring on the
ground, colored by category (fire, chair, table, bed, banner, rug).

## Install

Drop `ComfortView.dll` into `BepInEx/plugins/ComfortView/`. Client-side only.

## Controls

- **F6** - toggle rings on/off
- **F7** - open/close the selection menu (Up/Down move, Right select/pin/toggle, Esc close); includes a Circles/Spheres shape toggle for whatever's currently shown
- **F8** - pin/unpin the item you're looking at; pinned items always get a translucent 3D sphere regardless of the shape setting, since comfort range is a true sphere, not just a flat circle

## Build from source

```
dotnet build -c Release
```

Pass `-p:ValheimPath=/path/to/Valheim` if it's not in the default Steam location.
