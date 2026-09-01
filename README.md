# Comfort View

BepInEx mod that draws each nearby comfort item's 10m radius as a ring on the
ground.

## Install

Drop `ComfortView.dll` into `BepInEx/plugins/ComfortView/`. Client-side only.

## Controls

- **F6** - toggle rings on/off
- **F7** - open/close the selection menu (Up/Down move, Right select/pin/toggle, Esc close)
- **F8** - pin/unpin the item you're looking at; pinned items also get a translucent 3D sphere, since comfort range is a true sphere, not just a flat circle

## Build from source

```
dotnet build -c Release
```

Pass `-p:ValheimPath=/path/to/Valheim` if it's not in the default Steam location.
