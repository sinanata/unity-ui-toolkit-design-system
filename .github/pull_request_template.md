## Summary

<!-- What does this PR add or fix? Link any related issue. -->

## Screenshots

<!-- Showcase row(s) for the new or changed component, in every state. Broken-vs-fixed for bug fixes. -->

## Checklist

- [ ] Rule uses tokens, no inline hex / px / ms (except where commented as load-bearing).
- [ ] Showcase UXML updated with all states / variants of the new rule.
- [ ] No `var(...)` in inline UXML `style="..."` attributes (use a class).
- [ ] `Mobile.uss` updated if the component has a touch-tier override.
- [ ] `docs/COMPONENTS.md` line added (or relevant doc updated).
- [ ] `CHANGELOG.md` entry under the unreleased section.
- [ ] No `using LeapOfLegends.*` or product-specific imports in C# changes.
- [ ] No `Resources.Load<Texture2D>` in new C# (icons resolve via USS `resource(...)`).
- [ ] Tested in the editor with the showcase scene, at desktop and `.mobile` widths.
- [ ] Tested via `Tools\Build\Build-Showcase.ps1 -Serve` and confirmed the rendered WebGL build matches the editor.
