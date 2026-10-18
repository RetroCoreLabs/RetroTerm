# RetroCore Labs — Visual Identity System
## RetroTerm Logo Design Guide
**Version:** 3.0
**Date:** September 2026

---

### Overview

The RetroTerm logo is a green terminal prompt, `>_`, in a dark rounded square, with a bright
green ring around it on a transparent outside. The master file is `assets/RetroTerm.svg`, and
every other logo file is made from it.

Two earlier looks are retired and their files deleted: the "Dark CRT" photographic bezel
(retired 28 September 2026) and a "solid" variant without the ring (retired 30 September 2026;
not the logo Ronny wanted). Nothing may reference either.

---

### Colour Palette

Measured from `assets/RetroTerm.svg`, which is the authority.

| Element | Colour |
|---------|--------|
| Ring | `#00E676`, 24 px stroke, no fill |
| Inner square | `#04140D` |
| Prompt `>_` | `#00E676` |

The terminal theme's default phosphor green is the same family of colour; see
`src/RetroTerm.Desktop/Themes/BuiltInThemes.cs`.

---

### Composition

Also measured from the SVG, on a 512 x 512 canvas.

- Ring: a rounded rectangle at (24,24), 464 x 464 px, corner radius 88 px, stroke 24 px.
- Inner square: a rounded rectangle at (76,76), 360 x 360 px, corner radius 52 px.
- Chevron: a rounded polyline from (144,176) through (231,250) to (144,324), stroke 46 px.
- Underscore: a rounded rectangle at (264,300), 110 x 34 px, corner radius 11 px.

---

### Files

| File | What it is for |
|------|----------------|
| `assets/RetroTerm.svg` | The master. Edit this, then regenerate the rest. |
| `assets/RetroTerm-512.png` | The README logo, and the window icon inside the app. |
| `assets/RetroTerm-1024.png` | For anything that needs a larger raster. |
| `assets/RetroTerm.ico` | The Windows executable icon, 16 to 256 px in nine sizes. |

The application project carries its own copies under `src/RetroTerm.Desktop/Assets/`:
`RetroTerm-512.png` is the window icon named in `MainWindow.axaml` and
`TerminalPopoutWindow.axaml`, and `RetroTerm.ico` is the `ApplicationIcon` in
`RetroTerm.Desktop.csproj`. When the master changes, regenerate both copies.
`tests/RetroTerm.Tests/Avalonia/WindowIconIsTheRingLogoTests.cs` checks that the 512 px PNG
ships inside the executable at that size, that both windows have an icon, and that no file
from a retired look is packed in.

---

### Regenerating the rasters

With ImageMagick installed:

```bash
magick -background none assets/RetroTerm.svg -resize 512x512 assets/RetroTerm-512.png
magick -background none assets/RetroTerm.svg -resize 1024x1024 assets/RetroTerm-1024.png
magick -background none assets/RetroTerm.svg -define icon:auto-resize=16,20,24,32,40,48,64,128,256 assets/RetroTerm.ico
```

Then copy the 512 px PNG and the ICO into `src/RetroTerm.Desktop/Assets/`.

---

### Incorrect Usage

- Do not recolour the green or the inner square.
- Do not drop the ring.
- Do not distort proportions or stretch.
- Do not overlay text over the icon.

---

### Licence

This icon set and guide are released under the **MIT License**.
Copyright © 2024-2026 Ronny Hansen, RetroCore Labs
