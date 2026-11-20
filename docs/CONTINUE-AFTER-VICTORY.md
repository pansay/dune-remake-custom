# Continue after the Harkonnen defeat

This optional player mod targets the supported Dune CD 3.7 executable. Code
addresses below are relative to its actual runtime CS, and data addresses are
relative to DS. No original resources or executable files are changed on disk.

## Original victory flow

The final assault advances DS:`C2h`, removes the shield, surrenders the palace's
Harkonnen troops, and changes the strategic map through `739Eh`..`73D6h`.
Entering room `3002h` then reaches `16FCh` through `4078h`. That routine writes
story phase `C8h` and starts the original victory conversation. Its script ends
with the final artwork at `14C9h`, followed by closing credits at `167Ch`.
The credits clear the companions and normal display state, then exit to DOS.

Simply blocking the DOS exit would leave a frozen campaign: the normal game
loop rejects phase `C8h` at `1B19h`, and `1B5Eh` skips troop activity for final
assault stages at or above 7. The victory dialogue can advance that stage again.

## Continuation

- At `167Ch`, with the setting on and a completed native victory, keep the
  conversation and artwork already shown, and redirect to the original sequence
  cleanup at `1736h`. The destructive credits setup has not run at this point.
- Record the continued campaign using phase `70h`, unused by the original
  story and below its simulation stop. Reset the finished final-assault stage
  to zero so the original world and troop updates run normally.
- Mark the Harkonnen palace friendly, discovered and visited, keep its palace
  appearance, and return Paul and his travelling companions to its entrance.
  The normal scene cleanup restores the clock, music, room and interface.
  The ending artwork clears the two companion portraits; restore the valid
  selection from victory entry, or choose present companions for a loaded
  victory save, and clear the old portrait blink timers. The artwork also fades
  palette entries 224–239 to black. Save these fixed companion colours at victory
  entry and restore the version-checked native VGA driver's cache, fade target
  and dirty flags; its own palette flush presents them with the resumed scene.
- Use the game's existing off-map NPC slot (`FF80h`) for the three opponents
  whom the original victory conversation exiles. Retire imperial tribute flags
  and pending payments while preserving spice stock and unrelated event flags.
- At `16FCh`, a later visit to the conquered throne room returns to normal room
  rendering instead of starting another ending.
- At `6E28h`, skip only the conquered palace's fort-to-sietch conversion. Its
  daily troop work still runs. Other captured fortresses retain their original
  conversion behaviour.
- At `749Dh` and `73A9h`, prevent later battles or old orders from restarting
  the completed final-assault sequence.

The victory marker and world state use the existing native save layout. Saved
continuation remains active in this fork when the setting is later disabled.
Native-layout compatibility does not add continuation rules to an unmodified
original executable. Loading a victory-phase save with the option enabled can
also complete the transition before the native loader rebuilds the room.

This is continued play on the existing map. Sardaukar attacks and new Emperor
territories remain separate, unavailable future mods. Phase `70h` is the current
post-victory marker for those future changes to recognize.

## Checks

The player checks cover the setting's default, independent persistence, victory
gating, preservation of troops and other locations, party restoration, and the
native save codec. The separate native driver performs the original final attack
and advances its victory conversation and artwork. It compares continuation with
the original credits path and exercises ecology, multiple days, troop orders,
travel, throne-room revisits, palace preservation and native save/reload.
The published player is checked independently with continued and victory-phase
saves. No test scripts or driver commands are included in the playable build.

The native executable and its resources were inspected directly. For symbolic
names, see the
<a href="https://github.com/OpenRakis/Dune/blob/main/cryo-dune-3.7-cd-dncdprg.chani.htm" target="_blank" rel="noopener noreferrer">OpenRakis original-game disassembly</a>.
