# Fonts

Any font on Google Fonts, with real weights and every language it supports — imported at edit
time, or fetched live at runtime, in a WebGL build, shaped.

---

## Use a Google font — the two easy ways

### 1. Import at edit time (bundled, offline, zero-latency)

1. **Design System > Google Fonts**, search a family, pick your weights, **Import**.
2. Add the generated sheet to your root, **after** `DesignSystem.uss`:

```xml
<Style src="project://database/Assets/DesignSystem/Resources/UI/Styles/DesignSystem/DesignSystem.uss" />
<Style src="project://database/Assets/Resources/DsFonts/Inter/Inter.uss" />
<ui:VisualElement class="ds-root">
  ...
</ui:VisualElement>
```

That is the whole setup. `-unity-font-definition` is an inherited property, so one declaration on
`.ds-root` reaches every text element under it, and the whole design system changes face.

### 2. Fetch at runtime (any of ~2000 families, live, no rebuild)

```csharp
// One-time: load the manifest that maps a family name to its files.
var json     = Resources.Load<TextAsset>("GoogleFontsManifest");
var manifest = DsGoogleFonts.Manifest.Parse(json.text);

// Any time after: fetch a family and apply it.
var entry = manifest.Find("Cairo");
StartCoroutine(DsGoogleFonts.Load(entry, new[] { 400, 700 }, fallbacks,
    family => DsFonts.Apply(root, family),      // it is now a live typeface, weights and all
    error  => Debug.LogError(error)));
```

This works in the Editor, on desktop, and **in a browser tab** — and the fetched font draws every
language it covers, Arabic and Devanagari included, correctly shaped. Fetching **Cairo** gives you
Arabic in Cairo, joined; fetching **Noto Sans JP** gives you kanji. See
[Downloading a font at runtime](#downloading-a-font-at-runtime).

The live showcase's **TYPEFACE** card is this, wired to a search box, with a language-availability
table underneath that updates as you fetch.

---

## Why the importer exists

Point Unity's own Font Asset Creator at Inter, Roboto, or any Noto family and you get **one
weight**. Not because Unity is broken, but because those families now ship as a single *variable*
font file, and FreeType opens a variable font at its default instance. Whatever you asked for, you
get Regular.

The escape is in the font's own `fvar` table, which lists its **named instances** — Thin, Light,
SemiBold, Bold, Black. FreeType addresses them through the *upper 16 bits* of its face index, and
`FontAsset.CreateFontAsset(path, faceIndex, …)` forwards that index untouched. So one 850 KB file
yields nine typefaces with genuinely different outlines:

| weight | style | `'H'` width |
|---|---|---|
| 100 | Thin | 45.70 |
| 400 | Regular | 51.06 |
| 700 | Bold | 55.38 |
| 900 | Black | 59.06 |

The importer reads `fvar` itself (`OpenTypeFace`), so it works on any font, not only Google's.

---

## Real bold, not fake bold

USS has **no `font-weight` property**. UI Toolkit gives you exactly two style slots,
`-unity-font-style: normal` and `bold`, and nothing in between.

When you ask for bold, TextCore looks up slot 7 of the current font's `fontWeightTable`. If that
slot is empty it *synthesizes* bold by dilating the Regular outline — the typographic equivalent of
a bold button in a word processor. The importer wires the table across the whole family, so every
`-unity-font-style: bold` rule already in the design system starts resolving to the family's real
Bold face. **You change nothing.** All 42 components get it.

For the weights in between, the generated sheet adds one class each:

```xml
<ui:Label text="Medium" class="ds-body-1 ds-weight-500" />
<ui:Label text="SemiBold" class="ds-body-1 ds-weight-600" />
```

and re-points the type ramp at the weights it was always specified at — `.ds-h3` to the real 600,
`.ds-caption` to the real 500. (Turn that off with **Upgrade type ramp** in the importer.)

> One face, and only one, may carry the weight table, and it must not name itself. Unity's advanced
> text generator walks the table hunting for cycles and, finding a face that references itself,
> throws the whole table away and floods the console with *"Circular reference detected."* — which
> silently kills real bold while every bold check stays green. The importer enforces this;
> `Design System > Showcase > Verify Fonts` asserts it.

---

## Multilingual

### The trap

Unity has an OS-font fallback. With no fallback chain configured, a missing glyph is quietly served
from a font on the machine: on Windows, Arabic resolves to **Arial** and Japanese to **Microsoft
YaHei**. So your multilingual UI looks perfect in the Editor.

**A WebGL build has no OS fonts.** The same screen renders as a row of empty boxes in the browser.
Nothing logs a warning, in either place. It is the purest form of "works on my machine".

### The fix: a fallback chain

Give the family a fallback chain. The importer wires it onto *every* face (a bold heading is laid
out through a different `FontAsset`, and that one needs the fallbacks too):

```
Noto Sans          <- Latin / Greek / Cyrillic catch-all. Put it FIRST.
Noto Sans Arabic
Noto Sans Hebrew
Noto Sans Devanagari
Noto Sans Tamil
Noto Sans Thai
Noto Sans Khmer
```

**Order is load-bearing.** Without a Latin/Greek/Cyrillic catch-all at the head, a family that ships
no Greek falls all the way through to the first font that has *any* of it — in this repo's own
showcase, that once meant Cyrillic drawn by a **Japanese** font.

### The generator: shaping vs. not

A glyph is not enough. Arabic letters must **join** into initial/medial/final forms, Arabic and
Hebrew must be laid out **right to left**, and Devanagari, Bengali, Tamil, Thai and Khmer must have
their clusters **reordered**. That work is *shaping*, and it lives only in Unity's **advanced** text
generator. The **standard** generator draws one glyph per codepoint in memory order — correct for
Latin and CJK, unreadable for Arabic.

So the generator is chosen by the **content**, not the font, and `DsFonts` does it for you:

```csharp
DsFonts.Apply(root, family);                       // subtree: advanced if the font is bundled
DsFonts.ApplyFace(label, face, contentNeedsShaping: true);   // this Arabic line: advanced
DsFonts.ApplyFace(label, face, contentNeedsShaping: false);  // this CJK line: standard
```

Two things make this the right default rather than a footgun:

- `-unity-text-generator` is an **inherited** property whose initial value is `Advanced`. Force a
  subtree to `Standard` and every element under it inherits `Standard` too — and an element cannot
  *decline* an inherited value, only overwrite it. `DsFonts` writes the property **inline, per
  element**, which beats inheritance and also reaches a dropdown popup (parented at panel scope,
  outside every stylesheet, where a class would match nothing).
- CJK never needs shaping, and routing a **downloaded** CJK font through the advanced generator can
  draw a black or stale block on its first frame. Sending non-shaping scripts to the standard
  generator is both correct and a bug dodge.

If you set the font in UXML by hand instead of through `DsFonts`, two classes do the same job
declaratively: `ds-intl` (advanced — the default, so it states intent) and `ds-intl--off`
(standard). `DsFonts` overrides both, inline.

UI Toolkit has no `direction` property, so paragraph alignment is still yours to set
(`-unity-text-align: middle-right` for an RTL block). The generator reorders the run; it cannot know
which edge the paragraph hangs off.

### A downloaded font can be shaped too

For a long time the rule here was *"a script that needs shaping must be bundled"*, because a font
downloaded at runtime has no `UnityEngine.Font` object — nothing in Unity's API builds one from
bytes — only a file path, and the advanced generator appeared to refuse a font without a `Font`
object: *"FontAsset is invalid. Please assign a Source Font File."*

That rule is **no longer true.** `DsFonts.TryEnableShaping` gets a native font face out of the
downloaded *file*: the native creator is handed the file path all along, and only a managed guard
keyed on the `Dynamic` atlas mode stops it ever trying, so a font whose glyph table is still empty
walks past the guard by being `Static` for the length of one call. The verifier proves it every run
by rendering — an Arabic line comes out **175 px** through the advanced generator versus **213 px**
through the standard one, and that narrowing *is* the joining.

`DsGoogleFonts` calls this at birth for every fetched font, so **any Google font serves any language
it covers, shaped**. `DsFonts.CanShape(face)` reports whether it took.

> **Caveat.** The trick reaches into a Unity internal by reflection. If a future Unity removes the
> gap, `TryEnableShaping` returns `false` and everything degrades to the old behaviour: fetched
> shaping-scripts fall back to a bundled Noto (which still shapes), and `DsFonts.Resolve` refuses to
> hand a shaping run to a font that cannot shape it. `Verify Fonts` fails loudly if the gap ever
> closes, so it can never rot silently.

### Han unification, or: why the chain is not enough

Chinese, Japanese and Korean share Unicode codepoints for characters they **draw differently**. A
fallback chain resolves per *codepoint*, not per *language*, so whichever CJK font sits first wins
every shared Han character. Put Noto Sans JP ahead of Noto Sans SC and your Chinese renders in
Japanese letterforms.

This is the nastiest bug in the file, because **every check passes**. Nothing is missing, so
`DsFonts.Coverage` reports a clean sweep and the verifier goes green. The text is legible. It is also
wrong, and a Chinese reader spots it at a glance.

No ordering fixes it. The right face depends on the language of the run, and codepoints do not carry
one. So the fix is the one a localized app is already making anyway: **it knows what language it is
showing, so it names the face.**

```csharp
// When the locale changes, so does the face. The chain stays behind it for everything else.
DsFonts.ApplyFace(root, locale == "zh" ? notoSansSC
                      : locale == "ja" ? notoSansJP
                      : locale == "ko" ? notoSansKR
                      : null);
```

The showcase's language table shows all seventeen scripts at once, which no real app does, so its
three CJK lines pin themselves this way and are tinted **amber** to say so.

### Checking it honestly

`DsFonts.Coverage` resolves text through the explicit chain only, and **never** asks the OS. What it
reports is what a *build* will do.

```csharp
var hit = DsFonts.Coverage(family, "私はガラスを食べられます", out int missing);
// hit.Font         -> the FontAsset that will actually draw it
// hit.FromFallback -> true if the family itself had no glyph
// missing          -> characters nothing in the chain covers: boxes, in a build

// Pass requireShaping for a script that must join/reorder: a font that cannot shape is skipped
// even when it has the glyphs, so the answer is a face that will actually draw the word.
var arabic = DsFonts.Coverage(family, text, out int miss, requireShaping: true);
```

`Design System > Showcase > Verify Fonts` runs this over every bundled family, asserts real bold and
the weight-table shape, renders Arabic from a bare file path to prove runtime shaping still works,
and fails loudly on any gap.

---

## Runtime API

```csharp
// Swap the typeface for a whole subtree.
DsFonts.Apply(root, family);

// One element at one weight, bypassing the weight table.
DsFonts.ApplyWeight(label, family, 600);

// Name an exact face, and say whether its content needs shaping (picks the generator).
DsFonts.ApplyFace(label, notoSansSC, contentNeedsShaping: false);

// Can the advanced generator draw (and therefore shape) this face? True for any bundled font,
// and for a fetched one once TryEnableShaping has taken.
bool shapes = DsFonts.CanShape(face);

// Rasterize glyphs up front. A dynamic atlas fills on demand, and the first frame that shows a
// CJK screen pays for every glyph in it at once.
DsFonts.Warm(family, japaneseText);

// Cover fonts the family never heard of, including one downloaded at runtime.
DsFonts.InstallFallbacks(panelSettings, fallbacks);

// What will actually draw this text? Asks the explicit chain only, never the OS.
var hit = DsFonts.Coverage(family, text, out int missing);   // or a single face
```

### Downloading a font at runtime

`DsGoogleFonts.Load` fetches a family and builds a live `DsFontFamily` — in the Editor, on desktop,
and in a browser tab.

```csharp
var entry = manifest.Find("Lobster");
StartCoroutine(DsGoogleFonts.Load(entry, new[] { 400, 700 }, fallbacks,
    family => DsFonts.Apply(root, family),
    error  => Debug.LogError(error)));
```

- The fonts come from the upstream **`google/fonts` git repository**, not the Google Fonts website.
  Both CSS endpoints and `fonts.google.com/download` answer a script with HTML rather than a font;
  they are built for browsers. The repository serves raw `.ttf` over HTTPS with
  `Access-Control-Allow-Origin: *`, which is the only reason the WebGL path works at all.
- A family's folder and filenames cannot be derived from its name, so this needs a manifest —
  generate one with `GoogleFontsCatalog.ExportRuntimeManifest` (~2045 families, ~260 KB) and load it
  from `Resources`.
- The fetched font is born with your fallback chain wired and shaping enabled, so it draws every
  language it covers straight away. Point it at a subtree with `DsFonts.Apply`, or name it on one
  element with `DsFonts.ApplyFace`.
- This is the only part of the package that touches the network. It is behind a `DS_WEBREQUEST`
  version define, so the package still compiles into a project that has stripped
  `com.unity.modules.unitywebrequest`.

---

## Themes

A `ThemeData` can carry a typeface, making a theme a complete look rather than a palette:

```
Theme > Typography > Typeface   ->   your DsFontFamily
```

Leave it empty and the theme changes only colours and sizes, inheriting whatever face is active.

---

## Size, and what to bundle

A variable family costs **one file** no matter how many weights you take — nine `FontAsset`s share
one `.ttf`, and each asset is a few KB of metadata. Taking the full 100–900 ramp is free. The build
report proves it: a variable `.ttf` appears once and serves all nine weights.

CJK is the exception, and it is not close: Noto Sans JP is 9 MB, KR 10 MB, SC 17 MB — **36 MB** for
the three. Everything that needs *shaping*, by contrast, is small: the whole Arabic-through-Khmer set
of Noto fallbacks is about 5 MB.

That split decides what to bundle. Because a downloaded font can now be shaped, bundling is a choice
about the **default experience** — what renders offline, on the first frame, with no network — not a
hard limit:

- **Bundle** a base family plus the small shaping fallbacks, so those scripts work with no download.
- **Fetch** the big ones (CJK) and anything a user picks by name.

This repo's showcase does exactly that: it ships **Noto Sans** as the one bundled family with the
~5 MB shaping chain behind it, and fetches everything else — CJK, Armenian, Georgian, and whatever
you type into the box. That took the WebGL demo from **35 MB down to 15 MB** while covering *more*
languages than before.

---

## Licensing

The importer copies the family's `OFL.txt` / `LICENSE.txt` next to the fonts. Google Fonts are open
source (mostly SIL Open Font License), but they are not public domain: keep the licence file with
the font.
