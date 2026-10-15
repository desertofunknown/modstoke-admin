# Building and installing Modstoke Admin

This repository currently contains documentation only; the Admin source import is pending. These instructions apply to an existing, prepared local source tree containing `OpenACAIAdminToolBepInEx.csproj`, its C# files, and `global.json`. They do not make this docs-only repository buildable, and they do not require Modstoke Manager or a downloaded Admin release.

## Prerequisites

- A local Sons of the Forest game directory, referred to below as `<GameRoot>`.
- A compatible Modstoke Loader installation with `OpenACAI\Runtime\OpenACAI.Endnight.Loader.Core.dll` under `<GameRoot>`.
- BepInEx assemblies under `<GameRoot>\BepInEx\core` and the generated game/Unity interop assemblies under `<GameRoot>\BepInEx\interop`. The project file names the required DLLs directly.
- .NET SDK **11.0.100-rc.2.26504.105**. `global.json` pins this prerelease SDK and disables roll-forward. A different installed SDK will not silently replace it.

The current project file resolves dependencies with paths two directories above the source tree. Put the prepared source in this layout (its directory can have any name):

```text
<GameRoot>/
  BepInEx/
    core/
    interop/
  OpenACAI/
    Runtime/
      OpenACAI.Endnight.Loader.Core.dll
  ModdingWorkspace/
    <AdminSource>/
      OpenACAIAdminToolBepInEx.csproj
      global.json
```

The DLLs in `BepInEx` and `OpenACAI\Runtime` come from your own game and Loader installation. Do not add game, interop, or Loader runtime binaries to the Admin source repository.

## Build

Open PowerShell in `<AdminSource>`. Check SDK selection, then compile:

```powershell
dotnet --version
dotnet build OpenACAIAdminToolBepInEx.csproj --configuration Release --framework net11.0 --nologo --verbosity minimal
```

The version check should print `11.0.100-rc.2.26504.105`. A successful build puts `OpenACAIAdminTool.dll` in `bin\Release` under the source tree. Building does not install the mod. This SDK version selects the compiler; it does not update the Loader's embedded runtime.

The project lists `net6.0`, `net10.0`, and `net11.0` targets, but the current documented build is `net11.0`. The Loader Core reference and menu integration are conditional on that target. Do not treat another target compiling as proof that it works with the installed Loader.

## Manual install

Close the game before replacing an installed DLL. Copy the newly built `bin\Release\OpenACAIAdminTool.dll` to:

```text
<GameRoot>\BepInEx\plugins\OpenACAIAdminTool.dll
```

Copying the matching PDB is optional for local debugging. Keep the filename `OpenACAIAdminTool.dll`; the Modstoke product name did not change its assembly or plugin identity. Start the game and look for the Admin load message in the BepInEx log. Press **F7** in-game to open the host console.

Admin stores `settings.ini` and `player-history.tsv` in `<GameRoot>\BepInEx\plugins\OpenACAIAdminTool`. Preserve that directory when replacing the DLL. It can contain player information and local settings; do not commit it or attach it to public bug reports without review.

## What the build confirms

At the latest local source snapshot, `Plugin.Version` is `0.5.108`. The project's build record says the .NET 11 RC2 Release build succeeded with no warnings or errors, and the resulting DLL/PDB were installed and hash-verified. That build was not launched for runtime testing. Full UI and multiplayer acceptance remain pending. Compile and installation checks alone do not verify that BepInEx loaded the plugin, that Loader services registered, or that a host action took effect.

