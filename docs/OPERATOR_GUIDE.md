# Operator guide

## Before the event

1. Connect the HDMI display and set Windows to extend the desktop.
2. In Windows sound settings, choose HDMI or the venue sound system as the default output.
3. Open the application. The controller stays on the primary display; the public scoreboard opens fullscreen on the selected secondary display.
4. Open **Settings**, select the public display, set volume, test the shot-clock
   buzzer, and choose whether the scoreboard remains topmost.
5. Select **Tournaments**, create the tournament, and add at least two teams.
   Team and player images are optional; players can also be omitted.
6. Select **New Game**, choose the tournament, home and away teams, game type,
   and a group from 1–20 or A–Z when applicable. Record the coin-toss winner and
   whether that team chooses the ball at game start or at a potential overtime,
   then verify the public display before starting.

## Main workflow

- **START CLOCKS / PAUSE CLOCKS** always controls the game and shot clocks together.
- **TOURNAMENTS** manages tournament teams, optional players, and optional
  uploaded images. Imported images are copied into the application's local data.
- **NEW GAME** selects teams from the chosen tournament. The game type (and group
  for group games) appears beneath the 3x3 Centar logo on the public display.
- `Space`, `G`, and `C` are aliases for the same synchronized clock control.
- Left-click either score or foul count to add one; right-click to subtract one.
- Left-click the shot-clock value to add one second; right-click to subtract one.
- The compact game-clock buttons adjust by one second, ten seconds, or one minute.
- **RESET SHOT · 12** is the prominent left-click reset and preserves the shared
  running state without changing the game-clock value.
- **RESET SHOT + PAUSE** returns the shot clock to its configured duration and
  pauses both clocks.
- A short beep sounds once at five seconds remaining. The full shot-clock buzzer
  sounds once at expiration and pauses both clocks; resetting the shot clock
  re-arms both cues.
- Setting or adjusting either running clock to zero pauses both clocks.
- Score and foul reductions cannot go below zero.
- Winning score and regulation expiration produce an operator decision banner; they do not irreversibly finalize the game.
- For a tied regulation expiration, select **START OVERTIME**.
- The coin-toss choice determines the overtime opening possession: choosing the
  ball at game start gives potential overtime possession to the other team;
  reserving overtime gives it to the coin-toss winner.
- Overtime ends immediately when either team reaches two overtime points. No
  two-point winning margin is required, and the regular-time 21-point rule does
  not apply in overtime.
- **END GAME** confirms finalization, updates the match JSON, and leaves the final score visible.
- **SAVED GAMES** pauses running clocks and shows every valid stored match,
  newest first. Finished matches open on the final screen; unfinished matches
  open paused and can continue.
- **UNDO** appends a reversion event. It does not delete history.

## Keyboard map

Keyboard shortcuts are ignored while editing a text field.

| Key | Action |
|---|---|
| `Space` | Start/pause both clocks |
| `G` | Start/pause both clocks |
| `C` | Start/pause both clocks |
| `R` | Reset shot clock and preserve running state |
| `Shift+R` | Reset shot clock and pause both clocks |
| `[` / `]` | Game clock −1 / +1 second |
| `Shift+[` / `Shift+]` | Game clock −10 / +10 seconds |
| `,` / `.` | Shot clock −1 / +1 second |
| `Shift+,` / `Shift+.` | Shot clock −5 / +5 seconds |
| `B` | Manual buzzer |
| `Q` / `W` | Home +1 / +2 |
| `A` / `S` | Home −1 / −2 |
| `E` / `D` | Home foul +1 / −1 |
| `O` / `P` | Away +1 / +2 |
| `K` / `L` | Away −1 / −2 |
| `I` / `J` | Away foul +1 / −1 |
| `Ctrl+Z` | Undo |
| `Ctrl+N` | New game |
| `Ctrl+O` | Open saved games |
| `Ctrl+Shift+E` | End game |
| `Ctrl+B` | Blackout public display |
| `F11` | Toggle public-display fullscreen |

## Recovery

Every accepted game action is added to the ordered event list in its stable
`YYYY-MM-DD_match-id.json` file. The controller only queues the action; JSON
serialization, flushing, and atomic file replacement happen on a background
worker. While the clocks are running, their projected values are also saved
every second.

If the app or computer stops unexpectedly, the next launch shows the recovered
teams, score, fouls, clock values, and action history from the most recent
completed atomic write.

Choose **Yes** to load the latest unfinished game paused. Choose **No** to
leave it in **Saved Games** and open it later.

## Display failure

If HDMI disconnects, the public window returns to a resizable preview on the primary display. Match state and clocks continue. Reconnect the display, open **Settings** if needed, and use **TV FULL SCREEN**.

## Saved data

Use **HISTORY** for the current match audit, **EXPORT** for a manual JSON copy,
**SAVED GAMES** to browse/open matches, and **GAMES FOLDER** to inspect the JSON
files directly. Save failures are shown in the controller status line and
diagnostics are written under `%LOCALAPPDATA%\3x3 Centar Scoreboard\Logs`.
