# RetroCore Labs — Visual Identity System
## RetroTerm Dark CRT Logo Design Guide
**Version:** 1.0  
**Date:** October 2025  

---

### 🎯 Overview
RetroTerm Dark CRT represents the signature visual identity for the Retro Family terminal suite.  
The logo merges minimal modern geometry with authentic CRT terminal aesthetics.

---

### 🎨 Color Palette

| Element | Color | Sample |
|----------|--------|--------|
| Bezel | `#1E1E1E` | ![#1E1E1E](https://via.placeholder.com/12/1E1E1E?text=+) |
| Screen | `#003322` | ![#003322](https://via.placeholder.com/12/003322?text=+) |
| Prompt “>_” | `#00FF88` | ![#00FF88](https://via.placeholder.com/12/00FF88?text=+) |
| Accent Blue | `#4169E1` | ![#4169E1](https://via.placeholder.com/12/4169E1?text=+) |
| Amber Indicator | `#FFB000` | ![#FFB000](https://via.placeholder.com/12/FFB000?text=+) |

---

### 🔤 Typography
- **Font:** JetBrains Mono (preferred) or Consolas  
- **Symbol:** `>_`  
- **Weight:** Medium (500)  
- **Kerning:** -2% for compact prompt appearance  

---

### ⚙️ Composition
- Outer shape: Rounded square (radius 60 px)  
- Screen inset: 60 px margin  
- “>_” vertically centered, slightly above midline (optical alignment)  
- Accent blue stroke: 6 px outer highlight  
- Amber indicator: 14 px diameter, offset 67 px from top-right corner  

---

### 🎨 Visual Implementation Details

**Bezel:**
- Multi-tone gray gradient with top-light and bottom shadow
- Creates metallic and thick appearance
- Subtle depth for realistic CRT housing

**Screen:**
- Dark green phosphor gradient: `#005533` (center) → `#001911` (edge)
- Radial gradient for authentic CRT phosphor appearance
- Darker edges simulate curvature and light falloff

**Curved Glass:**
- Radial highlight overlay for convex look
- Subtle reflection to simulate glass surface
- Enhances three-dimensional appearance

**CRT Glow:**
- Soft phosphor bloom effect via Gaussian blur
- Authentic CRT electron beam glow
- Subtle green halo around prompt symbol

**Blue Reflection:**
- Subtle rim tint using accent color `#4169E1`
- Represents Retro Family brand identity
- Applied as subtle edge highlight

---

### 🔧 Asset Generation

The logo files were generated from the master SVG using ImageMagick:

```bash
# Generate PNG variants at standard resolutions
magick RetroTerm-DarkCRT.svg -resize 128x128 RetroTerm-DarkCRT-128.png
magick RetroTerm-DarkCRT.svg -resize 256x256 RetroTerm-DarkCRT-256.png
magick RetroTerm-DarkCRT.svg -resize 512x512 RetroTerm-DarkCRT-512.png

# Generate multi-resolution ICO file for Windows
magick RetroTerm-DarkCRT.svg -define icon:auto-resize=16,32,48,64,128,256,512 RetroTerm-DarkCRT.ico
```

**File Locations:**
- Master: `assets/RetroTerm-DarkCRT.svg`
- PNG variants: `assets/RetroTerm-DarkCRT-{128,256,512}.png`
- Windows icon: `assets/RetroTerm-DarkCRT.ico`

---

### 🧩 Usage
| Platform | File | Notes |
|-----------|------|-------|
| **Windows** | `RetroTerm-DarkCRT.ico` | Contains all standard resolutions (16–512 px) |
| **Linux/macOS** | `RetroTerm-DarkCRT.png` | Use 512×512 for desktop launchers |
| **Avalonia UI** | SVG icons (monoline gray) | Match UI spec section 7 |
| **Web/Docs** | `RetroTerm-DarkCRT.svg` | Use full color with dark bezel |
| **Splash** | `RetroTerm-DarkCRT-512.png` | Maintain CRT realism |

---

### 🖌️ Avalonia Icon Styling
| State | Color |
|--------|--------|
| Default | `#A0A0A0` |
| Hover | `#CCCCCC` |
| Active | `#4169E1` |
| Disabled | `#A0A0A0` @ 60 % opacity |

Stroke width: 1.5 px  
Size: 16–20 px depending on context  

---

### ♿ Accessibility
- Contrast ratio (green vs. black): 14.8:1 ✅  
- No flashing animations permitted  
- Text alternatives: `aria-label="RetroTerm icon"` for accessibility frameworks  

---

### 🚫 Incorrect Usage
- ❌ Do not alter glow or recolor the CRT green.  
- ❌ Do not use transparent background for main logo.  
- ❌ Do not distort proportions or stretch.  
- ❌ Do not overlay text over the icon.  

---

### 📜 License
This icon set and guide are released under the **MIT License**.  
© 2025 **RetroCore Labs**

---

*End of Document*
