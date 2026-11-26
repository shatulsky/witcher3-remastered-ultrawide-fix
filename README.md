# The Witcher 3 Remastered - Ultrawide Cutscene Fix

Removes the black bars (pillarboxing) from cutscenes in **The Witcher 3: Wild Hunt - Remastered (5.x)** on ultrawide and super-ultrawide monitors.

The older NextGen (4.0x) patchers no longer work on Remastered: they either crash because the DirectX 11 executable is gone, or patch the wrong spot. This tool is built and tested against the Remastered executable.

| Before | After |
|---|---|
| ![before](docs/before.png) | ![after](docs/after.png) |

## Download

Grab `W3UltrawideFix.exe` from the [latest release](../../releases/latest) or from [Nexus Mods](https://www.nexusmods.com/witcher3/mods/13876).
For patch 5.02, use a release that lists 5.02 support or build the executable from source using the instructions below.

## Usage

1. Close the game.
2. Run `W3UltrawideFix.exe` (it asks for administrator rights because Steam installs the game under `Program Files`).
3. The tool finds the game automatically (Steam libraries, GOG). If it can't, put the exe in the game folder or paste the path when asked.
4. Pick your resolution:
   - 2560x1080, 3440x1440, 3840x1600, 5120x2160, 6880x2880 (21:9)
   - 3840x1200 (32:10)
   - 5120x1440 (32:9)
   - or enter any custom resolution
5. Launch the game.

To change resolution, just run the tool again. To remove the fix, run it and choose **R) Restore original**, or use *Verify integrity of game files* in Steam/GOG.

Command line, for scripting:

```
W3UltrawideFix.exe --res 3440x1440
W3UltrawideFix.exe --restore
W3UltrawideFix.exe --path "D:\Games\The Witcher 3" --res 5120x1440
```

> Game updates and *Verify integrity of game files* can replace `witcher3.exe` and undo the fix. Check the new build's compatibility before applying the patch again.

## How it works

Cutscenes are letterboxed to a hard-coded 16:9 aspect ratio stored in the executable. For Steam patch 5.02, the tool verifies the executable against the original build and replaces the three confirmed values in `bin\x64_dx12\witcher3.exe` with your monitor's aspect ratio. It refuses 5.02 executables that differ outside these values. Earlier builds use the existing aspect-ratio scan.

For 5.02, the original is saved as `witcher3.exe.uwfix-1145604-backup`. The tool preserves an existing `witcher3.exe.uwfix-backup` and can restore from it if it matches the verified 5.02 executable.

Applying and restoring the fix at 3440x1440 was verified on copies of the Steam DirectX 12 executables for `5.0.0.1145604` (patch 5.02) and `5.0.0.1048522` (patch 5.01).

## Building

Requires the .NET SDK (8+). Targets .NET Framework 4.8, which ships with Windows 10/11, so users don't need to install anything.

```
dotnet build src/W3UltrawideFix -c Release -o out
```

## Credits

- [vulkk.com](https://vulkk.com/2021/06/27/how-to-fix-witcher-3-ultrawide-cutscenes-no-black-bars/) - original hex-edit guide
- [mrHalfer/The-Witcher-3-Ultrawide-NoBlackBars-Fix-NextGen](https://github.com/mrHalfer/The-Witcher-3-Ultrawide-NoBlackBars-Fix-NextGen) - replace-all approach and resolution presets
- [lepoco/w3.ws](https://github.com/lepoco/w3.ws) - NextGen widescreen cutscene patcher

## License

MIT
