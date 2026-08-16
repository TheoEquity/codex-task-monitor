# Windows 11 Manual Acceptance

Run these checks against the installer built from the commit recorded below. Leave a check
unchecked until the stated behavior has been observed. Evidence may cite only build metadata,
fixed diagnostic categories, process counts, file-presence results, and registry value names;
do not record task titles, thread IDs, prompts, rollout content, or user paths.

- [ ] Per-user installer completes without elevation and app starts without a separate .NET runtime.
  - Evidence: Not yet recorded. Confirm the installer runs in the standard user token, then start the installed executable.
- [ ] A second launch does not create a second floating panel and re-shows/activates the existing panel if it was hidden.
  - Evidence: Not yet recorded. Hide the panel, start the executable again, and observe one activated panel.
- [ ] Starting the monitor while Codex is already running establishes the baseline without losing an active turn.
  - Evidence: Not yet recorded. Start both apps while an active turn is visible and verify that turn remains active.
- [ ] Starting the monitor before Codex, then opening a task, launches Codex and waits up to 5 seconds for its UIA root.
  - Evidence: Not yet recorded. Start the monitor first, open a task, and time the bounded readiness behavior.
- [ ] A new running Codex task appears within 4 seconds with a blue dot.
  - Evidence: Not yet recorded. Start a new task and observe the panel within the stated bound.
- [ ] Completion and abort both change the row to green “等待处理”.
  - Evidence: Not yet recorded. Exercise one completion and one abort.
- [ ] “已处理” hides only the selected `threadID:turnID`; two consecutive later turns each reappear.
  - Evidence: Not yet recorded. Handle one turn, then start and complete two later turns in the same thread.
- [ ] 7 or more rows cap the panel at 6 visible rows and show a vertical scrollbar.
  - Evidence: Not yet recorded. Populate seven qualifying rows and inspect the panel.
- [ ] Clicking a visible unique task opens its exact thread and leaves the sidebar row visible.
  - Evidence: Not yet recorded. Confirm both body and sidebar state after one visible-row reveal.
- [ ] Clicking unique tasks above and below the current sidebar viewport opens each exact thread and reveals its row within 8 seconds.
  - Evidence: Not yet recorded. Time one target in each direction.
- [ ] A pinned task, section task, project task, and projectless task each resolve in their correct group.
  - Evidence: Not yet recorded. Verify all four group types.
- [ ] Duplicate titles produce the ambiguity warning and no sidebar click.
  - Evidence: Not yet recorded. Create duplicate visible titles and confirm the safe degradation.
- [ ] A missing session-index mapping opens the body and reports a sidebar warning.
  - Evidence: Not yet recorded. Use a controlled missing mapping and confirm the warning.
- [ ] Closing/restarting Codex during reveal ends with a bounded warning, not continued scrolling.
  - Evidence: Not yet recorded. Close or restart during an offscreen reveal and observe the bounded result.
- [ ] Login startup survives reboot; disabling it removes the HKCU Run value.
  - Evidence: The Run value can be inspected without UI interaction; reboot survival remains unverified until a real reboot. After testing the toggle, inspect `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` for the fixed value name `CodexTaskMonitor`.
- [ ] Upgrade preserves handled-item settings.
  - Evidence: Not yet recorded. Handle an item, install a newer package, and confirm the same item remains handled.
- [ ] Uninstall removes program files and the HKCU Run value while leaving local settings/logs.
  - Evidence: Program-file removal and the fixed Run-value name can be checked automatically; preservation of existing settings/logs requires pre-existing, non-sensitive test data and remains unverified.
- [ ] Logs contain categories/counts/timings only and no task title or prompt text.
  - Evidence: Not yet recorded. Inspect generated logs for the fixed schema only, without copying log content into this file.

## Evidence

| Field | Value |
| --- | --- |
| Windows build | 26200 |
| Codex/ChatGPT package version | 151.0.7922.76 |
| Installer SHA-256 | AE802CAFF7DCBE940DB0C2038512486551DBAC97431A233C8781899A71DE565B |
| Test commit SHA | 6b9d2b858e95b40c7be2e6a7e2496db28cfe0e33 |
| Pass date | 2026-08-16 |

## Automated current-machine evidence

Record only checks that were exercised non-interactively here. This does not substitute for any
unchecked visual, click, Codex-interaction, reboot, or login acceptance item above.

| Check | Evidence |
| --- | --- |
| Install / upgrade / uninstall / reinstall | All non-interactive installer exit checks succeeded; reinstall is the final state. |
| Installed executable process start | Installed executable started after first install and final reinstall. |
| HKCU Run value inspection | The fixed `CodexTaskMonitor` value was present after install, upgrade, and reinstall, and absent after uninstall. |
| Installed files inspection | Installed executable existed after install, upgrade, and reinstall; the installation directory was absent after uninstall. |
