# Troubleshooting Modstoke Admin

Start by separating a build failure from a game load or host authority problem. The [build guide](BUILDING.md) shows the required source layout and manual install path.

## `dotnet` cannot find the required SDK

Run `dotnet --version` from the prepared Admin source directory. Its `global.json` requires `11.0.100-rc.2.26504.105` and disables roll-forward. Install that exact SDK from Microsoft if it is unavailable. A system `dotnet` command working in another directory does not establish that the Admin source tree can select its pinned SDK.

## Build reports missing Loader, BepInEx, or interop DLLs

Check that the prepared source tree is directly under `<GameRoot>\ModdingWorkspace` and that `<GameRoot>\OpenACAI\Runtime`, `<GameRoot>\BepInEx\core`, and `<GameRoot>\BepInEx\interop` contain the referenced assemblies. The current project file uses fixed relative paths, so moving the source tree elsewhere breaks those references. Rebuild against the compatible installed Loader and game assemblies; do not copy game DLLs into this repository to make the build pass.

## The plugin or F7 menu does not appear

Make sure the newly built DLL is named `OpenACAIAdminTool.dll` and is in `<GameRoot>\BepInEx\plugins`, not only in `bin\Release` or the source directory. Check the BepInEx log for `Modstoke Admin by OpenACAI loaded` or a load error. Admin also writes `openacai-admin-runtime.log` in the game root during startup. The default menu key is F7; its `ToggleMenuKey` setting may have been changed in `BepInEx\plugins\OpenACAIAdminTool\settings.ini`.

If the Loader menu says registration failed, inspect the Admin and Loader log entries. The code reports a duplicate provider identifier and other registration exceptions separately. A Loader menu registration problem does not, by itself, establish that F7 or the Admin console failed to load.

## A host action says unavailable, queued, or unconfirmed

Load a world on the host and check the console's Overview and readiness information. Admin requires a ready local world and network server authority for host policy routes. Installing the DLL on a joining client does not make that client the host. Individual Loader/SDK hooks can also be unavailable even while Admin itself loads.

Treat a queued request or send result as a request, not proof of delivery or a world change. The UI distinguishes observed state from requested or unknown state. Some optional runtime Harmony patches are disabled by default on .NET 10 and later; Loader shared routes are reported separately. Do not assume full moderation or guard coverage from the plugin load message alone.

## Settings or player history behave unexpectedly

The compatibility directory is `<GameRoot>\BepInEx\plugins\OpenACAIAdminTool`. Confirm that the game can write there and check the console's settings status. Keep your existing `settings.ini` and `player-history.tsv` when updating the DLL. These files, runtime logs, and telemetry can contain player information; review and redact them before sharing a report.
