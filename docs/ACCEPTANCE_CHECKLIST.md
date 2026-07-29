# Release and venue acceptance checklist

## Automated release gate

- [x] .NET 10 Release build completes with zero warnings.
- [x] Domain tests pass.
- [x] Application and monotonic-clock tests pass.
- [x] Infrastructure persistence and operational routing tests pass.
- [x] Active JSON round-trips typed events.
- [x] Rapid actions remain ordered in the background recovery journal.
- [x] Blocked or failed persistence does not block match commands.
- [x] Graceful shutdown flushes queued actions.
- [x] Tampered recovery snapshots are rejected.
- [x] Each match uses an ISO-date/match-ID JSON filename.
- [x] Saved games are listed newest-first and malformed JSON is isolated.
- [x] Finished games reopen on final state; unfinished games reopen paused and continue.
- [x] Coin-toss winner/choice resolves both opening and overtime possession.
- [x] Overtime finalizes immediately at two points with no win-by-two margin.
- [x] Regular-time score 21 does not trigger a winning alert during overtime.
- [x] Self-contained `win-x64` publish succeeds.
- [x] WiX MSI builds successfully.

## Physical Windows/venue gate

Perform these checks on the exact tournament laptop, HDMI chain, display, and audio system.

- [ ] Install the MSI on a clean Windows 10/11 x64 computer.
- [ ] Confirm no separate .NET runtime is required.
- [ ] Confirm Start-menu and desktop shortcuts launch the app.
- [ ] Verify controller remains on the laptop while the scoreboard covers the external taskbar.
- [ ] Verify 720p, 1080p, and available 4K/DPI modes.
- [ ] Disconnect and reconnect HDMI during a running simulated game.
- [ ] Confirm blackout and F11 work without moving controller focus.
- [ ] Confirm score, foul, and shot-clock values increase on left-click and decrease on right-click.
- [ ] Confirm all six compact game-clock adjustment buttons work.
- [ ] Confirm `Space`, `G`, `C`, and the controller clock button always start or
  pause both clocks together.
- [ ] Confirm a normal shot-clock reset preserves the shared running state and
  does not change the game-clock value.
- [ ] Confirm shot-clock expiration and **RESET SHOT + PAUSE** stop both clocks.
- [ ] Route Windows default audio to HDMI/venue output and confirm the manual,
  game-clock, shot-clock expiry, and five-second warning patterns.
- [ ] Run a complete regulation game including resets, adjustments, penalties, winning alert, and finalization.
- [ ] Run a tied regulation game through overtime.
- [ ] Create a tournament, add at least two teams, and verify it remains available after restarting the app.
- [ ] Add teams and players with a mix of present and omitted images; verify imported images and roster ownership after restarting.
- [ ] Create games by selecting tournament teams for every game type. Verify numeric group `1`/`20`, alphabetic group `A`/`Z`, and the type/group label beneath the public-display logo.
- [ ] Kill the process while both clocks run; relaunch and verify paused recovery within the most recent one-second snapshot.
- [ ] Confirm Windows does not sleep or turn off the display while a game is active.
- [ ] Upgrade from the previous MSI and confirm settings and saved games remain.
- [ ] Uninstall and confirm completed JSON, settings, recovery data, and logs remain.
