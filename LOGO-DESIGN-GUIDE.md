# RetroCore Labs — Visual Identity System
## RetroTerm Logo Design Guide
**Version:** 2.0
**Date:** September 2026

---

### Overview

The RetroTerm logo is a green terminal prompt, `>_`, on a dark rounded square. The master
file is `assets/RetroTerm-solid.svg`, and every other logo file is made from it.

The earlier "Dark CRT" logo, a photographic bezel with a glowing screen, was retired on
28 September 2026. Its files were deleted from the repository, and nothing may reference
it any more.

---

### Colour Palette

Measured from `assets/RetroTerm-solid.svg`, which is the authority.

| Element | Colour |
|---------|--------|
| Background | `#0A1912` |
| Inner rim | `#00E676` at 32 % opacity, 10 px stroke |
| Prompt `>_` | `#00E676` |

The terminal theme's default phosphor green is the same family of colour; the values actually
applied are in `src/RetroTerm.Desktop/Themes/BuiltInThemes.cs` and
`src/RetroTerm.Desktop/Styles/DarkTheme.axaml`.

---

### Composition

Also measured from the SVG, on a 512 x 512 canvas.

- Outer shape: rounded square, corner radius 104 px.
- Inner rim: a second rounded rectangle inset 12 px, corner radius 96 px.
- Chevron: a rounded polyline from (144,176) through (231,250) to (144,324), stroke 46 px.
- Underscore: a rounded rectangle at (264,300), 110 x 34 px, corner radius 11 px.

---

### Files

| File | What it is for |
|------|----------------|
| `assets/RetroTerm-solid.svg` | The master. Edit this, then regenerate the rest. |
| `assets/RetroTerm-solid-512.png` | The README logo, and the window icon inside the app. |
| `assets/RetroTerm-solid-1024.png` | For anything that needs a larger raster. |
| `assets/RetroTerm-solid.ico` | The Windows executable icon, 16 to 256 px in nine sizes. |

The application project carries its own copies under `src/RetroTerm.Desktop/Assets/`:
`RetroTerm-solid-512.png` is the window icon named in `MainWindow.axaml` and
`TerminalPopoutWindow.axaml`, and `RetroTerm-solid.ico` is the `ApplicationIcon` in
`RetroTerm.Desktop.csproj`. When the master changes, regenerate both copies.

`assets/RetroTerm.svg`, `RetroTerm.ico` and the `RetroTerm-512.png` and `RetroTerm-1024.png`
rasters are a second variant of the same mark with a bright green ring around a transparent
outside. The application does not use it.

---

### Regenerating the rasters

With ImageMagick installed:

```bash
magick assets/RetroTerm-solid.svg -resize 512x512 assets/RetroTerm-solid-512.png
magick assets/RetroTerm-solid.svg -resize 1024x1024 assets/RetroTerm-solid-1024.png
magick assets/RetroTerm-solid.svg -define icon:auto-resize=16,20,24,32,40,48,64,128,256 assets/RetroTerm-solid.ico
```

Then copy the 512 px PNG and the ICO into `src/RetroTerm.Desktop/Assets/`.

---

### Incorrect Usage

- Do not recolour the green or the background.
- Do not distort proportions or stretch.
- Do not overlay text over the icon.

---

### Licence

This icon set and guide are released under the **MIT License**.
Copyright © 2024-2026 Ronny Hansen, RetroCore Labs
