# Retro Family UI Design System
**Unified Visual Design Specification for RetroCore, RetroFS, RetroCommander, and Related Products**

---

## 1. Overview

This document defines a consistent visual and interaction language for all Retro-family applications.  
It establishes standardized rules for color, typography, layout, components, and accessibility to ensure a cohesive and professional experience across all products and platforms (desktop, web, embedded).

The design philosophy combines a **modern, minimal interface** with subtle references to **retro computing heritage**, maintaining clarity, usability, and technical precision.

---

## 2. Color Palette

### Light Theme

| Element | Color | Usage |
|----------|--------|-------|
| **Background - Outer Frame** | `#F3F3F3` | Window background, outer containers |
| **Background - Panels** | `#FFFFFF` | Main panels, content areas, dialogs |
| **Background - Sidebar** | `#F0F0F0` | Navigation, toolbars |
| **Border - Light** | `#D0D0D0` | Panel separators, dividers |
| **Accent - Primary** | `#4169E1` (Commodore Blue) | Interactive elements, highlights |
| **Accent - Secondary** | `#FFB000` (Amber Gold) | Warnings, active states, key highlights |
| **Text - Primary** | `#202020` | Main text |
| **Text - Secondary** | `#555555` | Secondary labels, inactive text |
| **Selection - Background** | `#DDE8FF` | Selected rows, text selection |
| **Hover - Background** | `#EAF3FF` | Hover state for interactive elements |

### Dark Theme

| Element | Color | Usage |
|----------|--------|-------|
| **Background - Outer Frame** | `#1E1E1E` | Window background, main frame |
| **Background - Panels** | `#252526` | Content panels, dialogs |
| **Background - Sidebar** | `#2D2D30` | Side navigation, toolbars |
| **Border - Dark** | `#3C3C3C` | Separators, dividers |
| **Accent - Primary** | `#4169E1` (Commodore Blue) | Interactive highlights |
| **Accent - Secondary** | `#FFB000` (Amber Gold) | Alerts, emphasis |
| **Text - Primary** | `#CCCCCC` | Standard readable text |
| **Text - Secondary** | `#999999` | Metadata, secondary labels |
| **Selection - Background** | `#253E7A` | Selected elements |
| **Hover - Background** | `#2E2F32` | Hover state for interactive elements |

---

## 3. Typography

### Font Family
- **Primary:** Inter, Segoe UI, or other clean sans-serif font
- **Monospace:** Consolas, JetBrains Mono, or Courier New (for logs, terminal views)

### Font Sizes
| Element | Size | Weight | Usage |
|----------|------|---------|--------|
| **Heading 1** | 24px | SemiBold | Application titles |
| **Heading 2** | 18px | SemiBold | Section headers |
| **Heading 3** | 15px | Medium | Subsection headers |
| **Body Text** | 13px | Regular | Content text, labels |
| **Small Text** | 11px | Regular | Status bars, metadata |
| **Button Text** | 13px | SemiBold | Buttons, menus |

---

## 4. Spacing and Layout

### Spacing Scale
- **XS:** 4px – inner padding
- **SM:** 8px – component padding
- **MD:** 12px – default gap between elements
- **LG:** 16px – between groups
- **XL:** 24px – section breaks
- **XXL:** 32px – window margins

### Border Radius
- **Small Components:** 4px
- **Panels and Cards:** 6px
- **Dialogs and Windows:** 8px

### Border Width
- **Default:** 1px
- **Active/Focus:** 2px
- **Strong Emphasis:** 3px

---

## 5. Component Standards

### Panels
```
Background: theme background color
Border: 1px solid divider color
Corner Radius: 6px
Padding: 12px
Shadow: subtle 0 2px 8px rgba(0, 0, 0, 0.1)
```

### Buttons

#### Primary Button
```
Background: #4169E1
Text: #FFFFFF
Hover: #3254B6
Active: #273F85
```

#### Secondary Button
```
Background: #F0F0F0 (light) / #3C3C3C (dark)
Text: #202020 (light) / #CCCCCC (dark)
Border: 1px solid #D0D0D0 (light) / #555555 (dark)
```

#### Disabled Button
```
Background: #E0E0E0 (light) / #2D2D30 (dark)
Text: #A0A0A0
Opacity: 0.6
```

### Input Fields
```
Background: #FFFFFF (light) / #1E1E1E (dark)
Border: 1px solid #D0D0D0 (light) / #3C3C3C (dark)
Border-Radius: 4px
Padding: 6px 10px
Focus: 2px solid #4169E1
```

### Lists and Tables
```
Row Height: 28px
Alternate Row: #FAFAFA (light) / #2D2D30 (dark)
Selection: #DDE8FF (light) / #253E7A (dark)
Hover: #EAF3FF (light) / #2E2F32 (dark)
```

---

## 6. Visual Effects

### Shadows
- **Light:** 0 2px 8px rgba(0, 0, 0, 0.1)
- **Medium:** 0 4px 12px rgba(0, 0, 0, 0.15)
- **Heavy:** 0 8px 24px rgba(0, 0, 0, 0.2)

### Transitions
- **Fast:** 100ms
- **Normal:** 150ms
- **Slow:** 300ms
- **Easing:** ease-in-out

### Opacity
- **Disabled:** 0.6
- **Hover:** 0.85
- **Active:** 1.0

---

## 7. Iconography

### Icon Set
- Prefer **monoline vector icons** consistent in stroke weight.
- Use sizes of **16px or 20px** for toolbar icons.
- Use **24px** for key actions.

### Icon Colors
| State | Color |
|--------|--------|
| Default | `#606060` (light) / `#A0A0A0` (dark) |
| Hover | `#202020` (light) / `#CCCCCC` (dark) |
| Active | `#4169E1` |
| Disabled | `#A0A0A0` @ 0.6 opacity |

---

## 8. Layout Patterns

### Standard Window Layout
```
┌─────────────────────────────────────────┐
│ Title Bar                               │
├─────────────────────────────────────────┤
│ Menu Bar (optional)                     │
├─────────────────────────────────────────┤
│ Toolbar / Quick Access (optional)       │
├───────┬─────────────────────────────────┤
│ Side  │ Main Content Area               │
│ Bar   │                                 │
│ (opt) │                                 │
├───────┴─────────────────────────────────┤
│ Status Bar                              │
└─────────────────────────────────────────┘
```

### Responsive Guidelines
- **Compact (<800px):** Hide sidebar, stack content vertically.
- **Normal (800–1200px):** Standard desktop layout.
- **Wide (>1200px):** Add detail panels or split views.

---

## 9. Theming

- Support both **light and dark** modes.
- Provide **manual theme override** via user settings.
- Maintain **color contrast** and readability in both modes.
- Smooth transitions between themes (≈300ms fade).

---

## 10. Accessibility

### Contrast Requirements
- Text contrast ≥ 4.5:1
- Large text ≥ 3:1
- Interactive elements require 2px visible focus border.

### Keyboard Navigation
- All focusable elements reachable via Tab.
- Clear focus indicators.
- Logical tab order matching visual hierarchy.

### Screen Readers
- Descriptive labels and roles.
- Avoid decorative-only icons without `aria-hidden`.
- Announce dynamic status changes.

---

## 11. Dialogs and Modals

```
Background: #FFFFFF (light) / #252526 (dark)
Border: 1px solid #D0D0D0 (light) / #3C3C3C (dark)
Corner Radius: 8px
Padding: 24px
Shadow: 0 8px 24px rgba(0, 0, 0, 0.3)
Max Width: 600px
Backdrop: rgba(0, 0, 0, 0.5)
```

Button placement: primary actions on the right, cancel on the left, 8px spacing.

---

## 12. Best Practices

### Do
✅ Maintain consistent spacing and hierarchy  
✅ Use accent colors sparingly  
✅ Provide hover/focus feedback  
✅ Support theming and accessibility  
✅ Apply consistent typography scale  
✅ Use animations only where meaningful  

### Don’t
❌ Use arbitrary or non-standard accent colors  
❌ Break contrast ratios  
❌ Overuse shadows or gradients  
❌ Animate every element  
❌ Nest panels excessively  

---

## 13. Summary

This **Retro Family UI Design System** defines the baseline for all Retro-branded interfaces.  
It ensures that each product — whether desktop, web, or embedded — reflects a consistent visual identity grounded in:

- Unified color and typography standards
- Predictable spacing and component geometry
- Accessible and theme-aware design
- Clean, modern interaction feedback

Adherence to this specification guarantees a coherent, professional presentation across all Retro platforms.

