# Cryogenic Player edition

This local fork adds a player menu to Cryogenic 0.1.9 while keeping the original Dune CD game, its resources, and the working Mac audio correction. The complete upstream Git history is retained.

## Play

On macOS, open **Play Dune.command**. The menu offers **New game**, **Load game**, **Play intro**, **Settings**, and **Exit**. New game goes directly to the palace. Play intro runs the original introduction once and returns to the menu; Escape can skip its current sequence.

During the game:

| Action | Key |
| --- | --- |
| Save in one of 100 named slots | F5 |
| Load a saved journey | F9 |
| Pause and open the menu | F10 |

Mac keyboards may require Fn together with a function key. The original game's Save/Load commands also open the new slot browser. Returning to the menu pauses the game and its audio. Resume continues the current session; loading or starting another game replaces that session after confirmation.

Dialogue balloons are enabled by default. Settings offers the original **Subtitles**, **Balloons**, and **No subtitles** radio choices and remembers the text mode and game speed. Changes to subtitles, balloons, or no text made in the original mixer are also remembered. The default speed is **5500 cycles**. The Mac playback buffer defaults to **120 ms**, with the corrected single CoreAudio playback consumer.

## Planned game mods

Settings → Game mods lists these ideas as disabled, unchecked options until implemented:

- No Chani kidnapping.
- Once Paul has full visions, reveal all sietches and fortresses across the whole map.
- Continue playing after conquering the Harkonnen palace.
- Sardaukar arrive late in the game through surprise attacks.
- After the Harkonnen defeat, open new territories to conquer, including a Sardaukar palace with the Emperor.

These are future optional game mods. The current placeholders do not alter gameplay.

## Original game data and saves

Supply your own **Dune CD 3.7** files, `DNCDPRG.EXE` and `DUNE.DAT`, in `dune/`, or choose their folder in Settings. The supported executable has SHA-256 `5f30aeb84d67cf2e053a83c09c2890f010f2e25ee877ebec58ea15c5b30cfff9`. The floppy edition is not supported.

The launcher keeps preferences, saves, and working files under `.local/user/`. It copies original game resources into a separate working directory and imports existing `DUNE37S*.SAV` files into the new slots on the first run. Original files and saves are left intact. `CRYOGENIC_IMPORT_SAVES` can point to a different directory for that first import. Set `CRYOGENIC_PLAYER_HOME` to choose a different player-data location.

There are 100 slots, with names, dates, and the in-game day. Replacing a slot retains one `.previous` backup. Slot files use the original game's native save format rather than emulator snapshots. A selected save is staged in the working folder and loaded by the game's original loader in a fresh engine process.

CD data, ISO images, personal saves, preferences, local binaries, and logs are ignored by Git. Do not add them with `git add -f`. No original game resources are included in this source fork.

## Build and check

Install the .NET 10 SDK, then run:

```sh
./build-player.sh
dotnet run --project src/Cryogenic.Player.Checks -c Release
```

The build produces a self-contained application in `.local/app/`. The checks exercise save encoding, malformed saves, map packing, slot replacement, setting persistence, and the actual menu controls and layout. Optional local fixtures and an output directory for menu captures can be passed after `--`:

```sh
dotnet run --project src/Cryogenic.Player.Checks -c Release -- /path/to/original/dune /path/to/captures
```

After publishing, the optional end-to-end check launches an isolated headless engine, imports local saves, creates slot 100, pauses and resumes, reloads that save, checks its palette, interface artwork, saved time and map flags, loads an original imported save, and exits. It needs the original CD data and at least one existing native save in the supplied folder:

```sh
dotnet run --project src/Cryogenic.Player.Checks -c Release -- --integration .local/app/Cryogenic /path/to/original/dune
```

For Windows, publish `src/Cryogenic/Cryogenic.csproj` with `-r win-x64 --self-contained true` and run `Cryogenic.exe --player`. The player code is portable; this fork's end-to-end game and audio validation was performed on Apple Silicon macOS.

The original command-line engine remains available: launch with the normal Spice86 arguments (or the explicit `--engine` marker). Its normal debugging options are unchanged. The player launches a separate engine process and uses local session files for pause, save, resume, and exit; it does not enable fixed network control ports during normal play.

## Fork details

Base: upstream tag `v0.1.9`, commit `02af0c6`. Branch: `feature/game-menu-and-saves`. Remote `upstream` points to OpenRakis/Cryogenic. No personal remote has been added and nothing has been pushed.

To upload later, create your repository and add its address as `origin`, then push this branch. Preserve the upstream license, notices, and vendored audio license files. Update the repository's description and release notes to identify it as a modified fork.

The native Avalonia menu lives in `src/Cryogenic.Player`. It manages one engine child per player-data directory. The game bridge installs hooks after DOS relocation, checks the expected instruction bytes, and makes changes in emulated RAM only. New-game and load startup keep the original resource and palette initialization while bypassing the movie. Loading also finishes the persistent interface setup before restoring the saved world state, so the interface artwork is present when the scene is redrawn. Save capture occurs at the central input loop before pausing; it packs the same map and game-state regions as the original save routine. The original mixer commit callback persists text choices without recording temporary cutscene overrides.

The patched `Spice86.Audio` source is included with its upstream commit and license notices, so the audio fix is reproducible. See `src/Spice86.Audio/UPSTREAM.txt`. The main project keeps semantic analyzers enabled but does not enforce formatting of the inherited disassembly during builds.

Upstream project documentation remains in `README.md`. Source references with explicit browser link targets are in `docs/player-sources.html`.
