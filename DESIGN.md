---
# FileShare — Design System
# Authored in the DESIGN.md format (https://github.com/google-labs-code/design.md)
# Adapted from Apple's Human Interface Guidelines / apple.com visual language
# (https://developer.apple.com/jp/design/, VoltAgent/awesome-design-md apple/DESIGN.md)
# for a native Windows WPF desktop app.

meta:
  name: FileShare
  version: 1.0.0
  description: A calm, editorial, light-first desktop app for publishing local files and folders for fast download.

colors:
  # Canvas (Apple's light, airy surfaces)
  canvas:            "#FFFFFF"   # app background
  canvas-parchment:  "#F5F5F7"   # secondary surface, cards
  surface-pearl:     "#FAFAFC"   # recessed wells (inputs)
  hairline:          "#E0E0E0"   # borders
  divider-soft:      "#F0F0F0"

  # Text
  ink:            "#1D1D1F"   # primary text
  ink-muted-80:   "#333333"   # secondary text
  ink-muted-48:   "#7A7A7A"   # tertiary / metadata text
  text-inverse:   "#FFFFFF"

  # Brand accent (Apple Action Blue) + interaction states
  primary:        "#0066CC"
  primary-hover:  "#0071E3"
  primary-press:  "#0058B0"
  primary-soft:   "#E8F1FC"   # tinted surface behind accent content

  # Semantic
  success: "#2FA84F"
  warning: "#D9822B"
  danger:  "#E0433D"
  info:    "#0071E3"

  # Dark chrome accents (used sparingly, e.g. code/log panel)
  surface-black: "#1D1D1F"

typography:
  font-family: "Segoe UI Variable Display, SF Pro Display, Segoe UI, system-ui, sans-serif"
  font-family-text: "Segoe UI Variable Text, SF Pro Text, Segoe UI, system-ui, sans-serif"
  font-family-mono: "Cascadia Code, Consolas, ui-monospace, monospace"

  hero:       { size: 32, weight: 600, line-height: 38, letter-spacing: -0.4 }
  display:    { size: 24, weight: 600, line-height: 30, letter-spacing: -0.2 }
  h1:         { size: 19, weight: 600, line-height: 24, letter-spacing: -0.1 }
  h2:         { size: 15, weight: 600, line-height: 20, letter-spacing: 0 }
  body:       { size: 14, weight: 400, line-height: 20, letter-spacing: 0 }
  body-strong:{ size: 14, weight: 600, line-height: 20, letter-spacing: 0 }
  caption:    { size: 12, weight: 400, line-height: 16, letter-spacing: 0 }
  label-caps: { size: 11, weight: 600, line-height: 14, letter-spacing: 1.0 } # UPPERCASE

rounded:
  sm: 8
  md: 11
  lg: 18
  pill: 999

spacing:
  xxs: 4
  xs: 8
  sm: 12
  md: 17
  lg: 24
  xl: 32
  xxl: 48

components:
  card:
    bg: canvas
    border: hairline
    radius: rounded.lg
    padding: spacing.lg
  card-parchment:
    bg: canvas-parchment
    radius: rounded.lg
    padding: spacing.lg
  button-primary:
    bg: primary
    text: text-inverse
    radius: rounded.pill
  button-secondary:
    bg: canvas
    text: primary
    border: hairline
    radius: rounded.pill
  button-danger-ghost:
    bg: transparent
    text: danger
    radius: rounded.pill
  input:
    bg: surface-pearl
    border: hairline
    radius: rounded.md
    text: ink
  chip:
    bg: canvas-parchment
    text: ink-muted-80
    radius: rounded.pill
  statusdot:
    online: success
    offline: ink-muted-48
    error: danger
---

# FileShare — Design Rationale

FileShare is a Windows desktop utility for publishing local files and folders for
fast, resumable download over the internet. The visual identity borrows Apple's
editorial calm: light canvases, a single restrained accent color, generous
whitespace, and tight, confident typography — so the one thing that matters
(what you're sharing, and the link to it) stays the loudest thing on screen.

## Principles

1. **Light, airy canvases.** The palette is white-to-parchment
   (`canvas` → `canvas-parchment` → `surface-pearl`). Elevation comes from a
   surface-color step plus a `hairline` border, never a heavy shadow.
2. **One accent, used sparingly.** Action Blue (`primary`) marks the single
   most important action — Start Sharing, a link, a selected chip. It is
   never used for decoration.
3. **Legible at a glance.** Text uses a three-step ramp — `ink` for content,
   `ink-muted-80` for supporting labels, `ink-muted-48` for metadata.
4. **Semantic status is unambiguous.** Green = sharing/online, amber =
   warning, red = error/danger, blue = informational — reused everywhere a
   status dot or chip appears.
5. **Pill-first controls.** Primary and secondary buttons, chips, and search
   inputs use `rounded.pill`; cards use `rounded.lg`; inputs use `rounded.md`.
   Consistent radii make the app read as one system.

## Application

- **Hero header** states what the app does in one line (`hero` type) with a
  status dot (共有中 / 停止中) next to it.
- **Cards** on `canvas` with a `hairline` border group related content: the
  public URL + QR + credentials, and the shared-items list.
- **Primary button** (共有を開始/停止) is a large `primary`-filled pill —
  the one loud element on screen.
- **Secondary buttons** (ファイルを追加, フォルダを追加, コピー) are outline
  pills on `canvas`.
- **Quick-add chips** for pinned folders sit on `canvas-parchment`, pill
  radius, and turn `primary-soft` when already added.
- Inputs (credential fields) sit on `surface-pearl` so they read as recessed
  wells.

This file is the single source of truth for the WPF theme in
`src/FileShare/Themes/DesignTokens.xaml`, which mirrors these tokens 1:1.
