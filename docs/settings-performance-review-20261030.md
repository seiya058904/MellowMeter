# Settings performance follow-up review — 2026-10-30

Scope: cached page switching, pending edits, row reuse, ordering, Apply/OK/Cancel and save/reload. No application windows, hidden/offscreen UI tests, desktop input, capture, driver operations or hardware benchmarks were started. No commit, push or publication.

## Confirmed and fixed

1. **P2 — Hidden-page Save could falsely acknowledge a changed page context.** After visiting Monitor, changing Advanced Sensors on another page and applying called Save on the cached monitor page. Save previously set its display signature to the current draft even though the page had never displayed that context. Returning to Monitor skipped rebuilding its availability list. The signature now advances only when the pre-save configuration still matches the displayed context. An ordinary row edit still updates its signature and retains its cache.
2. **P2 — The cache signature omitted both orders and group aliases.** It also used ambiguous unescaped delimiters and conflated an automatic unit with the literal string `null`. The signature now serializes explicit structured fields, including both sort indices and aliases. This was an existing invalidation gap, made more important by retaining page and row controls.
3. **P2 — Filtered taskbar saving used a different tie-break from display.** The display orders equal TaskbarSortIndex values by SortIndex; filtered Save previously used underlying list order instead. Even an unchanged filtered view could move hidden items. Save now uses the same tie-break. This is a pre-existing edge case found during this review, not introduced by the latest performance change.

4. **P2 — Unchanged tied orders could falsely enable Apply.** The effective-draft comparison used alphabetical keys to break equal sort indices, whereas the displayed taskbar uses panel order and stable input order. After an unchanged visit normalized indices, the comparison reported a change. Canonical comparison now uses the same ordering semantics, including ties in both sort indices.

The existing data-only signature, save-marker and taskbar order calculation were placed in `SettingsChanger` so the production paths can be exercised without constructing any WinForms control. The UI still collects row order and text, and still saves the departing page synchronously before changing categories. Apply/OK explicitly save pending pages; they do not wait for the 120 ms dirty timer. Live state is changed only by Merge; mutable row lists and aliases are copied. Cancel closes the draft without merging.

## Evidence

- Before fixes: console regressions reproduced the first three findings: **26 passed, 3 failed** (`.local/settings-review-red.log`). A further canonical dirty-state regression reproduced the fourth: **29 passed, 1 failed** (`.local/settings-review-dirty-red.log`).
- After fixes: **30 passed, 0 failed**, isolated profile (`.local/settings-review-green.log`).
- Added tests cover unchanged-context cache reuse; changed-context invalidation; structured signature changes; tied-order fallback and false-dirty prevention; all **326** subsets/permutations of five taskbar entries (visible order, hidden relative order, complete unique indices and repeat-save idempotence); and draft edit/commit/rebase/repeated commit/persist/reload/cancel isolation.
- The persistence case checks separate panel/taskbar orders, panel labels, short labels, both units, group aliases, follow-taskbar and an immediately bound setting. Its switching/cancel steps model data flow, not actual native window events.
- Application and DesktopSmoke compilation: **0 warnings, 0 errors** (`.local/settings-review-build.log`). Native regression cases were added for switching away before the debounce fires, cached row reuse, separate panel/taskbar label/unit editing and changing availability while Monitor is hidden. These were **compiled only**.
- `git diff --check` passes. Unrelated uncommitted changes and previous candidate folders remain preserved.

## Limits and delivery

Actual button dispatch, focus/Leave event order, scroll position, category latency and native layout/rendering are not certified by this review. The earlier three-group button test remains in DesktopSmoke, but was not rerun under the user's no-window constraint. No benchmark or visual performance claim is made.

The previous `settings-performance-20261030` binaries do not include these follow-up fixes. The intermediate `settings-performance-reviewed-20261030` candidate predates the fourth fix; it was preserved but is not the final candidate. Updated local packages are under `artifacts/settings-performance-reviewed-20261030-r2/`; use its ready-to-run Portable folder when convenient. Exit an older running MellowMeter before launching it to avoid single-instance redirection. Both editions share the normal user profile; checks performed by the agent used separate test profiles.
