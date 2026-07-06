# GitHub Copilot instructions

This repository follows the cross-tool `AGENTS.md` standard. Read [`/AGENTS.md`](../AGENTS.md) for the full guide: the `ds-` class naming, the token rules, the load-bearing USS import order, build and preview commands, and the pull-request checklist.

The rules that matter most:

- Style with `ds-`-prefixed BEM classes and `var(--...)` tokens. Never hardcode hex, px, or ms in component rules.
- Never put `var(...)` in an inline UXML `style="..."` attribute (it crashes the Unity 6 clone). Use a USS class instead.
- Class naming is BEM: block `.ds-btn`, element `.ds-btn__icon`, modifier `.ds-btn--primary`, state `.is-active`.
- Every component and state must be rendered in `DesignSystemShowcase.uxml`. The showcase is the test suite.
- `.meta` files are tracked on purpose; do not add them to `.gitignore`.
- The shippable package is `Assets/DesignSystem/` only. `Assets/Showcase/`, `Assets/Editor/`, and `Tools/` are the host project that builds the demo.
