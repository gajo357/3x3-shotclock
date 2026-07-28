# Detailed implementation plan

This plan records the staged implementation of the .NET 10 WPF application. Each stage has a buildable exit condition so clock correctness and recovery can be verified independently from presentation work.

## 1. Foundation

Status: complete.

- Pin the .NET 10 SDK and Microsoft Testing Platform in `global.json`.
- Create Domain, Application, Infrastructure, WPF, and three test projects.
- Enable nullable references, recommended analyzers, deterministic builds, warnings as errors, package lock files, and Central Package Management.
- Build the WPF Generic Host composition root and shared dark theme.
- Create separate controller and scoreboard windows with per-monitor-v2 DPI awareness.

Exit condition: the full solution restores and builds without warnings.

## 2. Domain and event engine

Status: complete.

- Model match metadata, teams, rules, clocks, stages, pending operator decisions, penalties, and derived status.
- Represent every operator action as a command.
- Validate lifecycle, score/foul bounds, clock limits, names, colors, and overtime transitions in `MatchEngine`.
- Persist the coin-toss winner/choice, derive opening and overtime possession,
  and finalize overtime immediately at two overtime points without applying
  the regular-time 21-point trigger.
- Emit immutable typed events with ID, sequence, UTC time, monotonic session elapsed time, and command source.
- Rebuild state deterministically with `MatchReducer`.
- Implement undo as a new event referencing the reverted event.

Exit condition: reducer replay produces the same state regardless of input event ordering, and domain/engine boundary tests pass.

## 3. Accurate clocks

Status: complete.

- Anchor running clocks to `TimeProvider.GetTimestamp()` instead of subtracting a fixed amount on UI ticks.
- Project remaining time on demand.
- Use one-shot timers only for expiration scheduling.
- Emit expiration and buzzer events exactly once.
- Capture projected values before any new command so unrelated commands cannot rewind time.
- Keep game-clock and shot-clock running state synchronized across start, pause,
  zero, and expiration paths while retaining independent shot-clock reset,
  set, adjustment, and recovery behavior.

Exit condition: deterministic fake-time tests cover elapsed projection, pausing, reset rescheduling, expiration, and undo while running.

## 4. Operator controller

Status: complete.

- Implement `ControllerViewModel` with CommunityToolkit.Mvvm observable properties and commands.
- Add score, foul, team, clock, buzzer, overtime, end-game, undo, new-game, and alert controls.
- Add manual clock and new-game dialogs.
- Map keyboard shortcuts to the same generated commands used by buttons.
- Ignore global shortcuts while a text editor has focus.
- Show recent history, full history, save state, app version, audio status, export, and games-folder actions.

Exit condition: all commands flow through `MatchSession`; code-behind contains only window/input plumbing.

## 5. Public scoreboard and displays

Status: complete.

- Build a read-only scoreboard view model.
- Render a responsive 1920×1080 logical surface inside a `Viewbox`.
- Show team colors with calculated black/white contrast, scores, fouls, penalties, clocks, status, decisions, and shot-clock expiration flash.
- Enumerate Windows displays and remember the selected device.
- Use native pixel placement for mixed-DPI fullscreen.
- Handle `WM_DISPLAYCHANGE`; return to preview if the selected HDMI display disappears.
- Support blackout, topmost selection, hidden cursor, and controller/F11 fullscreen toggling.

Exit condition: secondary-display and single-display paths compile and safely handle monitor refresh.

## 6. Persistence and recovery

Status: complete.

- Serialize the typed event hierarchy with an explicit discriminator and schema version.
- Queue every committed action as an immutable event delta with its projected state.
- Add one-second snapshots only while a clock runs; never write 25/40 ms render ticks.
- Reconstruct the complete ordered journal and serialize writes through one
  background channel; do not enumerate history or perform file I/O on the game thread.
- Flush the final checkpoint during graceful shutdown and let later writes heal
  over transient save failures by including the complete event stream.
- Write a temporary file on the same volume and atomically replace the active file.
- Validate event IDs, sequences, game identity, scores, fouls, stage, and clock ranges before recovery.
- Recover clock values paused and append recovery-source clock events.
- Keep one atomically updated `YYYY-MM-DD_match-id.json` document per match.
- List valid saved matches newest-first, with malformed files isolated from the
  rest of the catalog.
- Open finished matches as final/read-only and unfinished matches paused with
  safe in-memory session replacement.
- Support export and folder access.

Exit condition: recovery, tamper detection, JSON polymorphism, atomic
replacement, catalog ordering, and finished/unfinished opening tests pass.

## 7. Operational services

Status: complete.

- Persist audio, volume, display, and topmost settings.
- Embed the recorded shot-clock expiry buzzer and synthesize distinct manual,
  five-second warning, and game-clock PCM sounds for the Windows default output.
- Route only committed buzzer events to audio.
- Hold `ES_SYSTEM_REQUIRED` and `ES_DISPLAY_REQUIRED` while a game is active.
- Write rolling daily diagnostics under local application data.
- Capture match scheduler, unhandled UI, unobserved task, and process-level exceptions.

Exit condition: operational routing tests prove game lifecycle power changes and buzzer delivery.

## 8. Release and tournament validation

Status: complete for buildable artifacts; physical venue checks remain operator acceptance tasks.

- Publish self-contained `win-x64` output without trimming.
- Produce a portable ZIP.
- Build a WiX 6 MSI with embedded cabinet, per-machine binaries, desktop/Start shortcuts, and major-upgrade identity.
- Preserve all user data outside the installer component tree.
- Run all automated tests and a full Release build.
- Document keyboard operation, recovery, storage, audio routing, and venue checks.

Exit condition: Release build, test suite, self-contained publish, and MSI build succeed with no warnings.
