# Comfort Radius

A Valheim mod (BepInEx) that draws each nearby comfort item's 10m radius as a
ring on the ground, color-coded per item, so you can see at a glance whether
you're standing inside it.

## Requirements

- [BepInEx](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/) for Valheim

## Install

Drop `ComfortRadius.dll` into `BepInEx/plugins/ComfortRadius/`. Client-side
only; no need to install on the server.

## Controls

- **F6** - toggle the rings on/off.
- **F7** - open/close the selection menu.
- **F8** - pin/unpin the exact comfort item you're looking at.

Inside the menu: **Up/Down** to move, **Right** to select/pin/toggle a row,
**Esc** to close. The Items/Nearby tab row switches with **Left/Right**
while it's highlighted.

By default all nearby comfort items are shown. Picking any specific item -
via the menu's checklist, or F8 in the world - switches to a custom
selection made up of just what you've picked, instead of the "all nearby" or
"active only" set. Picking All/Active again returns to that mode.

## Build from source

Requires the .NET SDK.

1. If Valheim isn't installed at the default Steam location, pass
   `-p:ValheimPath=/path/to/Valheim` to the build.
2. `dotnet build -c Release`

The build copies the compiled DLL straight into your local
`BepInEx/plugins/ComfortRadius/` folder.
