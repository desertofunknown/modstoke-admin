# Modstoke Admin

Modstoke Admin is OpenACAI's in-game host console for Sons of the Forest. It brings player management, moderation, activity evidence, and world tools into one host-facing menu. It uses Modstoke Loader and its SDK interfaces for the features those components support.

Admin is intended for the player hosting a world. Installing it on a client does not grant server authority. A button reporting that a request was sent does not prove the game applied it; the console shows readiness and outcome details where they are available.

## Repository and development status

This source tree contains the Admin C# project and build instructions. It is a development snapshot, not a packaged download or a tested public release.

The snapshot identifies itself as **0.5.111**. An isolated .NET 11 RC2 Release build of this source succeeded with no warnings or errors against the locally installed Loader, BepInEx, and game interop assemblies. This staged snapshot has not been installed or launched in-game; UI and multiplayer acceptance remain open.

## Build and install

To build, you need a local Sons of the Forest installation with a compatible Modstoke Loader, BepInEx, and the game's generated interop assemblies. The project resolves those assemblies relative to this source tree when it is placed under the game's `ModdingWorkspace` directory. It does not download or include them.

Follow [Building and installing](docs/BUILDING.md) for the exact directory layout, SDK version, build command, and manual install step. See [Troubleshooting](docs/TROUBLESHOOTING.md) if a reference is missing or the menu does not open.

After a successful load, **F7** opens the Admin menu by default. The key can be changed in `BepInEx\plugins\OpenACAIAdminTool\settings.ini`.

## Compatibility names

The product name is Modstoke Admin, but the assembly remains `OpenACAIAdminTool.dll`, the plugin identifier remains `com.openacai.sotf.admin`, and settings stay under `BepInEx\plugins\OpenACAIAdminTool`. Those names are kept so existing installations and settings continue to work.

Modstoke Admin is an independent Sons of the Forest mod. It is not affiliated with or endorsed by Endnight Games.

## License status

A project source license has not been selected or published yet. Third-party Loader and BepInEx components keep their own terms and attribution when the Admin source is prepared for publication.
