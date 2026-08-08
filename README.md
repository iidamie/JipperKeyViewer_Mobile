# JipperKeyViewer Mobile

This directory is a Starry/StArray ModManager Android port of the core JipperKeyViewer workflow.

The PC project creates a Unity Canvas and polls `UnityEngine.Input`. Those APIs are not a suitable
mobile plugin boundary, so this port uses `IModPlugin`, `IModSettings`, `InputEvents.OnTouch`, and
the manager's ImGui foreground draw list. It does not reference UnityEngine, UnityModManager,
MelonLoader, Harmony, or the PC AssetBundle.

Implemented mobile features:

- 8K, 10K, 12K, 14K, 16K, 20K, and 24K layouts based on the PC key ordering
- multi-touch pointer capture with press/release/cancel handling
- external keyboard polling through ImGui key events
- per-key count, optional per-key KPS, global KPS, total count, and count formatting
- configurable position, scale, key gap, bindings, labels, colors, and touch debug outlines
- lightweight ImGui rain effect with configurable speed, height, length, width, and release fade
- gameplay-only visibility using the `scrController` runtime facade
- `settings.json` persistence in the ModManager mod directory
- GitHub mobile-release checking and in-place DLL updates from the Mod settings page

Touch input is coordinate-based: Android touch events do not contain the original PC key identity.
Following YoonKeyViewer-mobile, each pointer is assigned to a full-screen touch region on `Down`
and keeps that key until `Up` or `Cancel`, so sliding fingers do not jump between keys. The visual
keyboard is independent from the touch mapping surface and can be positioned freely.

## Build

The project uses .NET 10 and the same reference layout as the existing mobile mods. In this
workspace the default reference path points at `../../../YoonKeyViewer_Mobile/References` from the
project file's directory. For another checkout, pass the references explicitly:

```bash
dotnet build MobilePlugin/JipperKeyViewer.csproj -c Release \
  -p:StarryReferences=/path/to/StArray.ModManager/references
python3 package_mod.py
```

The package contains:

```text
mods/
└── JipperKeyViewer/
    └── JipperKeyViewer.dll
```

## Built-in update

The plugin checks the `iidamie/JipperKeyViewer_Mobile` releases on load and selects the
highest version that contains a `JipperKeyViewer-*-mobile*.zip` asset. Open the
mod settings page to download and install an available update. The update replaces
only `JipperKeyViewer.dll`; `settings.json` and `counts.json` remain untouched.
A game restart is required after installation.

## GitHub Actions

Pushing to `master` builds the Starry references, compiles the plugin, validates
the package, and creates or updates the matching `v{VERSION}` GitHub Release.
The release ZIP is the asset consumed by the built-in updater.

Starry discovers the plugin through `ModEntryPoint`; no `Info.json` is required. The existing PC
projects remain unchanged and continue to provide the UnityModManager and MelonLoader builds.

## Deliberate differences

The mobile port currently uses ImGui primitives instead of the PC sprite/AssetBundle/TMP renderer.
The 108-key full keyboard, foot-layout variants beyond the mobile touch row, profiles, ghost rain,
custom fonts, and the PC per-row rain controls are not included in this first port. They can be
added without changing the Starry entry point or touch state machine.
