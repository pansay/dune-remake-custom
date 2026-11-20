# No Chani kidnapping: original story flow and hooks

This optional player mod targets the original Dune CD 3.7 executable, SHA256
`5f30aeb84d67cf2e053a83c09c2890f010f2e25ee877ebec58ea15c5b30cfff9`.
All code offsets below are relative to the executable's actual runtime CS;
data offsets are relative to its DS. The CD files are never patched on disk.

## What the original game does

1. The phase `5Ch` callback at `11B3h` schedules an epidemic. `1E43h` chooses
   a known non-fortress location with rallied troops and makes its troops ill.
2. During phase `5Dh`, `1E01h` checks that Chani is at a location with ill
   troops. `1D9Fh` calls her treatment step only while Paul is away from her.
3. `1EDAh` advances the cure counter by 8 each period. On completion it runs
   the native troop cure callback, queues Chani's success vision (`0709h`),
   decrements the illness count, and scans for any remaining illness.
4. When none remains, the call at `1F0Dh` enters `11CBh`. That routine sets
   phase `60h` and moves Chani to the Harkonnen palace.
5. Talking to cured troops can advance the story to `64h` through `1EBEh`.
   Its callback at `1F13h` assigns Chani to a Harkonnen fortress.
6. Rescue dialogue is eligible during phases `64h` through `68h`. Its event
   advances the phase in groups of four. A separate Stilgar line at phase
   `68h` says the Fremen have recovered their motivation.

Phase `6Ch` is past these captivity and rescue lines. Its callback table entry
points to `10B7h`, a plain `RET`. The remaining fortress campaign, final-assault
counter at DS:`00C2h`, required companions, troops, and atomics are separate.

## Mod behaviour

- At `11CBh`, after the native cure and success vision, redirect execution to
  the native phase dispatcher at `121Fh`, requesting phase `6Ch`. Keep Chani's
  existing room and sietch. Do not relocate her or alter her follow status.
- At `121Fh`, redirect any requested captivity phase (`60h`..`6Bh`) to `6Ch`
  while this option is enabled. All other requested phases are unchanged.
- The original cured-Fremen report at DS:`B896h` shares condition `1FBh`
  with a disappearance report. That condition requires both cured troops and
  a phase within the skipped chapter. At `9FD1h`, allow only the cured-Fremen
  report beyond that chapter when the troop's original cured bit is present.
  The disappearance report and its condition remain untouched.
- For an existing captivity save, repair Chani's location before the native
  loader rebuilds the room (`B3F1h`), or on the game-loop checkpoint after
  enabling the option. Only proceed if treatment is complete. Keep a following
  or already safe Chani in place; otherwise use the surviving cured location,
  falling back to the Atreides palace. Apply the `6Ch` state directly because
  its native callback has no effects; do not replay a fictional rescue.

The repair preserves all unrelated game state. The native ownership predicate
from `5D36h` identifies safe sites. Story progress and Chani's position persist
in normal save files, so disabling the option does not undo a completed cure.

## Validation

`Cryogenic.Player.Checks --chani-mod-integration` uses isolated copies of local
CD saves. Its separate test supplier calls the original DOS period-update,
condition-evaluation, and come-with-me routines using native `CALL` instructions.
It covers treatment time, Paul's presence preventing treatment, vanilla cure
completion, skipped abduction and fortress assignment, successful cure feedback,
Chani rejoining the party, suppressed captivity dialogue, existing-save recovery,
combined map and Chani mods, persistence with the option off, and live enable.
Existing-save recovery and live enable also run through the published player.
The test supplier and its script entry points are absent from the player build.

## Research reference

The executable and extracted dialogue/condition records were checked directly
against the local CD edition. The annotated listing provided useful names:
<a href="https://github.com/OpenRakis/Dune/blob/main/cryo-dune-3.7-cd-dncdprg.chani.htm" target="_blank" rel="noopener noreferrer">OpenRakis original-game disassembly</a>.
Both dialogue phrase IDs and condition IDs are one-based in their resource
pointer tables; the native evaluator at `A396h` indexes condition ID minus one.
