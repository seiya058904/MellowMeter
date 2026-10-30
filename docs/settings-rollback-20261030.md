# Settings rollback — 2026-10-30

The user explicitly chose to roll back only settings to v0.1.0 and retain the restored monitoring functionality. The baseline is `c56d4da`, immediately before glass changes in `8a4ac0d`; its settings implementation is the one shipped in v0.1.0.

## Applied scope

- `SettingsForm.cs` and `LiteUI.cs` use the baseline shell, page switching and static control implementations. The only retained differences from that baseline are nullable guards/annotations, formatting and the theme-editor black/white swatch contrast helper.
- Monitor page row reconstruction returns to the baseline implementation. The newer row cache is removed. Data-only sorting/save fixes, follow-taskbar binding and complete monitor selection are retained.
- Settings Acrylic is disabled explicitly. The main settings page no longer offers glass/animation controls; stored flags are preserved for profile compatibility. Existing monitor themes remain selectable. The dormant motion source files are preserved but no settings/control/startup code schedules their animations.
- 32 built-ins, collectors, floating/horizontal/taskbar monitor rendering, migration, driver/FPS manual installation controls and the earlier correctness fixes remain unchanged. Source hashes for the backed-up monitoring files were compared after rollback.
- Apply follows v0.1 behavior (available directly, with no dirty-comparison timer). Settings category construction/reparenting and visuals follow that version; this is not another incremental animation optimization.

## Backup and boundaries

All 73 pending files were saved, hashed and verified before edits in `.local/rollback-backups/20261030-201911-settings-v010/working-changes.zip`, with `manifest.json` and a binary Git patch. Existing packages remain untouched. No branch reset, history rewrite, commit, push or release was made. User configuration and running processes were not touched.

## Checks

- Application and DesktopSmoke compilation: 0 warnings, 0 errors.
- Isolated console checks: 29 passed, 0 failed, including 32 metric defaults, migration, draft/save/cancel isolation and 326 filtered-order permutations. The removed automatic-dirty-indicator check no longer applies to v0.1's always-available Apply button.
- Native smoke expectations were aligned with the static rollback and compiled only. No application or hidden/offscreen window test was run, and no desktop screenshot/input, driver installation or performance probe was used.
- Source comparison against the baseline verifies that the shell/control behavior is reverted. `git diff --check` passes after trimming inherited trailing whitespace. Runtime responsiveness is not claimed as measured.

## Local candidate

`artifacts/settings-v010-rollback-20261030/` contains separate Portable/Lite folders, ZIPs, hashes and verification evidence. The app remains version 0.2.0 because the 32-metric functionality is retained; only the settings implementation rolls back to v0.1.0.

Exit any older MellowMeter via its tray before manually launching this candidate, otherwise single-instance handling may activate the older process. Keep all extracted files together. Neither edition was launched by the agent.

## Follow-up: remove Glass themes

Glass Light and Glass Dark were removed at the user's request. Their two source files are archived under `.local/removed-glass-themes-20261030/`; previous candidate packages are preserved. New profiles use DarkFlat Classic. Existing Glass Light/Dark selections resolve to Mellow Light/Dark without resetting monitoring, position or other preferences. Theme menus also ignore stale Glass files in an overwritten older installation. Actual user settings were not modified by the agent.

The updated candidate is `artifacts/settings-no-glass-20261030/`, with 12 theme files in each edition. Application/DesktopSmoke compilation reports 0 warnings and 0 errors. The isolated no-window console suite passes 30 checks, including legacy theme selection/loading and preservation of unrelated preferences. No UI was launched and nothing was committed or published.
