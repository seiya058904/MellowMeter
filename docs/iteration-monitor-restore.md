# Monitor restoration and settings motion - local delivery

This iteration restores LiteMonitor's built-in monitoring and fixes three audited settings issues. It remains a local v0.2.0 build; no commit, push, tag or release was made.

The subsequent [source parity and motion quality review](quality-review-20261030.md) supersedes the packages below. Its changes have compile/static evidence only; the earlier interaction captures do not validate the reviewed build.

## Behavior

- All 32 built-in metric definitions, sort defaults and visibility defaults exactly match pinned upstream `3b91b5ca6d70a9056b8fdd01c9acddc985721240`: 13 default panel readings and six taskbar readings. FPS, CPU pump, case fan, manual source selection and calibration are restored. Plugins, web monitoring, speed testing and automatic updates remain excluded.
- New profiles use the upstream vertical layout with Glass Light. Schema 3 migrates legacy four-item selections, preserves existing orientation/theme/labels/relative order and other custom selections, and keeps `settings.pre-monitor-restore.json` without replacing older backups. Full hardware sampling is enabled by default; unavailable values remain dashes.
- FPS helper and sensor-driver installation are explicit settings actions. Monitoring does not download or elevate at startup. Components use the per-user data directory, and FPS keeps the derivative-specific session name. The restored archive extraction checks destination containment. Actual driver installation and FPS hardware operation were not exercised.
- Monitor surfaces use flat upstream fills and layouts with retained missing-value, DPI, text-overlap, theme-color and repaint fixes.
- Group sort buttons resolve the same GroupBlock as the move handler. Hidden taskbar-follow controls cannot overwrite the draft. Theme-editor hex labels select black/white using contrast against the actual swatch.
- Settings use animated switches, painted button press/release, selected navigation pills, temporary cross-fading page snapshots, clipped disclosure and smooth wheel scrolling. One clock stops when idle. The independent animation switch respects Windows client-area animations. Focus transitions and sort-button press animation share the same mechanism.
- Windows 11 22621+ selects native Desktop Acrylic for the settings sidebar. Transparent material remains bounded to that area; opaque data controls preserve text contrast. Disabled transparency, high contrast, battery saver and explicit app opt-out select the fallback. The theme system and dependency versions are unchanged.

## Verification actually completed

| Check | Result |
| --- | --- |
| Final Release build | Passed, 0 errors; 163 compiler warnings remain |
| Upstream default definitions | Exact match for all 32 definitions |
| Native sampler regression | Passed |
| Desktop smoke before desktop testing was paused | 996 assertions; 53 captures; native host DPI 168 (175%) |
| Core regression | 18 passed, 0 failed |
| Remaining security regression | 4 passed, 0 failed |
| Visible native interaction recording | Completed using isolated profile; physical sensors disabled |
| Portable and Lite publish | Both passed |
| ZIP integrity and SHA-256 | Both passed; identical application assembly in both variants |
| ZIP extraction/startup | Not run: user paused desktop testing while working |
| Final diff whitespace check | Passed |

The original 60-second desktop-suite watchdog expired during the added motion checks. The expanded full-metric settings matrix was rerun with a 120-second watchdog and passed. No desktop test was started after the user requested a stop. At that point the owned probe and recording processes had already exited.

After the passing desktop suite, the final source added input focus animation, applied the existing button-animation implementation to sort buttons, synchronized external animation-setting changes into unedited drafts, and guarded null entries during legacy-profile recognition. Those finishing changes were compiled and packaged but were **not** rerun through desktop interaction tests. The evidence records measured and delivered assembly hashes separately; screenshots/recording are not claimed as final-build pixel verification.

## Motion and material observations

The recorded off/on probe had zero active animation tracks after settling and again after two seconds idle; the sampled process CPU increment over each idle interval was zero at the counter's resolution. Both passes ended with 248 GDI and 567 USER objects. This is a short observation, not a leak certification or a zero-cost guarantee.

UI-loop p95 intervals were 23.6 ms with motion disabled and 30.4 ms enabled. The disabled pass included first page construction, while the enabled pass reused cached pages. Switching times therefore cannot establish a speedup. UI-loop latency and a 30 fps recording do not prove 60 fps GPU presentation or universally smooth animation.

The current user's Windows transparency preference was **off** (`EnableTransparency=0`). It was read, never changed. The captured light/dark surfaces exercise the solid fallback; this iteration has not visually certified active Acrylic composition. Reduced-animation, high-contrast and battery-saver system toggling, physical mixed-DPI transitions, real Explorer restart, broad hardware coverage, and final-package startup remain unverified.

## Local artifacts

Delivery folder: `artifacts/monitor-restore-20261030/`.

- `MellowMeter-0.2.0-win-x64.zip`: self-contained Portable, approximately 70.56 MiB.
- `MellowMeter-0.2.0-win-x64-lite.zip`: approximately 2.73 MiB; requires .NET 8 Desktop Runtime x64.
- `SHA256SUMS.txt` and `verification.json`: archive hashes, runtime variant and application-assembly parity.
- `evidence/`: native light/dark/metrics/fallback screenshots, a short settings-only MP4, probe JSON, desktop assertions and measured assembly hashes.

The full native/synthetic smoke images and logs remain in `.local/restore-iteration/`. No cleanup was performed. Existing user settings and history were not used by the tests.
