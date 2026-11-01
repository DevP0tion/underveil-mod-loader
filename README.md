# Underveil Mod Loader

A mod loader and in-game mod manager for Underveil, built as a [BepInEx](https://github.com/BepInEx/BepInEx) 5 plugin.

This is an unofficial fan project. It is not affiliated with the game's developer, and this repository contains no game files.

Tested with Underveil 0.3.7-Demo and BepInEx 5.4.23.6 (Windows x64).

## Features

- Loads mods from `Mods/<folder>/` when the game starts
- Loads dependencies first; missing or circular dependencies are reported
- Keeps a failing mod contained: the others still load, and its Harmony patches are undone
- Adds a **Mods** button to the title screen, with a list of installed mods, their status and any error
- Turns mods on and off from that window; changes apply after a restart, because loaded mods cannot be unloaded

## Install

1. Download the release zip, which includes BepInEx.
2. Extract the whole zip into the game folder, next to `Underveil.exe`.
3. Close the game and run `install.cmd`.
4. Start the game. The loader creates a `Mods` folder next to `Underveil.exe`.

An existing BepInEx install is kept. `install.cmd` and `UnderveilModLoader_files` can be deleted afterwards.

## Build

Requires the .NET SDK and an installed copy of the game with BepInEx, which provide the referenced assemblies. The build copies `UnderveilModLoader.dll` to `BepInEx/plugins`.

```bash
dotnet build -c Release -p:GameDir="C:\path\to\Underveil Demo"
```

## Release

Raise the version in `Plugin.cs`, commit it on `main`, push it, close the game and run `release.cmd`. It builds the loader, packages it with BepInEx and publishes a GitHub Release. It does not push, and stops if `main` differs from GitHub. It needs a copy of this repository in the game folder's `_mods_src\UnderveilModLoader`, or the `GameDir` environment variable, and a logged-in GitHub CLI.

## Making a mod

A mod is a folder in `Mods` with a manifest and a DLL:

```
Mods/MyMod/
  mod.json
  MyMod.dll
```

```json
{
  "id": "my-mod",
  "name": "My Mod",
  "version": "1.0.0",
  "author": "You",
  "dll": "MyMod.dll",
  "dependencies": []
}
```

`id` and `dll` are required. The DLL must contain exactly one class implementing `IMod`:

```csharp
using UnderveilModLoader;

public class MyMod : IMod
{
    public void OnLoad(ModContext context)
    {
        context.Logger.LogInfo("Hello");
        context.Harmony.PatchAll(typeof(MyMod).Assembly);
    }
}
```

`ModContext` provides `Info`, `Logger`, `Harmony` and `Directory`.

## Settings

The window saves turned-off mods to `Mods/settings.json`:

```json
{
  "disabled": ["my-mod"]
}
```

If this file cannot be parsed, no mods load until it is fixed, so mods you turned off are never loaded by accident.

## License

[MIT](LICENSE)
