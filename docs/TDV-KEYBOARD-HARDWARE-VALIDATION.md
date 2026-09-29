# TDV Keyboard — Hardware Validation Checklist

**Status:** No physical ND-246 / TDV-2200/9 hardware available for wire capture (as of 2026-06-26).  
**Purpose:** Record what the published spec and ROM documentation say, what RetroTerm implements today, and exactly what must be confirmed on real hardware before we treat any byte sequence as authoritative.

This document focuses on **editing keys** (E13, E14, G47 STRYK, B47 ←, PC Backspace/Delete) and **Ctrl-modifier behaviour**, because those are the areas with spec conflicts or PC-key ambiguity.

---

## 1. Authority hierarchy (until hardware proves otherwise)

Use sources in this order:

| Priority | Source | What it tells you |
|----------|--------|-------------------|
| 1 | **TDV-2200/9 User's Guide** §7–8 | Bytes on the serial line (host view), per Extended Control switch position |
| 2 | **Keyboard EPROM layout** (`spec/Keyboards/keyboard-spec.md` §3–4) | Internal keyboard-microcontroller codes before terminal firmware translation |
| 3 | **Terminal firmware** (2215 ROM notes in `spec/TDV2200/Testing2215/`) | How received bytes affect the display (e.g. `0x08` → `handle_backspace` at `0x2a6c`) |
| 4 | **NOTIS termcap / terminfo** (`docs/TermCap.txt`, `nd246.ti`) | What Unix hosts *expected* — may disagree with each other and with the User's Guide |
| 5 | **RetroTerm implementation** | Best-effort emulation policy — not ground truth |

**Critical distinction:** Keyboard ROM bytes ≠ wire bytes. Example: layout 965313-0 stores **E13 Normal = `0xEE`**, but the User's Guide says the host receives **`0x08`** (Extended OFF) or **`CSI 86 _`** (Extended ON).

---

## 2. Spec summary (best-effort, pre-hardware)

### 2.1 Keyboard EPROM (layout 965313-0, TDV-2200/9S Norwegian)

From `spec/Keyboards/keyboard-spec.md` §4.1:

| Grid | Label | ROM Normal | ROM Shift | ROM Ctrl |
|------|-------|------------|-----------|----------|
| E13 | NewParagraph | `0xEE` | `0xFE` | — |
| E14 | Del | `0x7F` | — | `0x7F` |
| B47 | Left | `0xB4` | — | `0xB4` |
| G47 | STRYK | `0xF1` | `0xD0` | — |

Internal code map (appendix): `0xEE`/`0xFE` → E13, `0xF1` → G47 STRYK.

### 2.2 User's Guide — wire bytes

Source: `spec/TDV2200/OCR/TDV-2200_9-User-s_Guide-ND_combined.md`

**DEL key (E14) — text behaviour (§4):**  
Erases character at cursor, or moves left one column and erases there.

**Extended Control OFF** (§7.1–7.2, switch OFF):

| Key | Wire byte(s) | Notes |
|-----|--------------|-------|
| E13 | `08` | Listed as BS; same code family as B47 ← |
| E14 | `7F` | DEL |
| B47 ← | `08` | BS |
| G47 STRYK | `04` | EOT (Ctrl+D class) |

§7.1: `<08> BS … or ◄ E13 or B47`

**Extended Control ON** (§8, switch ON):

| Key | Unshift | Shift | Fixed? |
|-----|---------|-------|--------|
| E13 | `ESC X 36 36 _` (`CSI 86 _`) | `ESC X 38 37 _` (`CSI 87 _`) | No |
| E14 | `7F` | `7F` | **Yes — "E14 is always DEL"** |
| B47 ← | `08` | `08` | **Yes — "B47 is always"** |
| G47 STRYK | `CSI 10 _` | `CSI 11 _` | No |

**`<00>` NUL** in §7.1 is **CTRL @** (`CTRL <40>`), not a normal backspace or DEL press.

### 2.3 Termcap / terminfo (host expectations, not Tandberg manual)

| File | Backspace-related | Delete-related |
|------|-------------------|----------------|
| NOTIS termcap `tdv2200` | `kb=^H`, `kl=^H` → `0x08` | (no `kd` in excerpt) |
| terminfo `tdv2200.ti` / `nd246.ti` | `kbs=\177` → `0x7F` | `kdch1=\E[10_` → G47 STRYK |

**Conflict:** termcap backspace = `0x08`; terminfo `kbs` = `0x7F`. User's Guide assigns `0x08` to E13/B47 (OFF) and `0x7F` to E14 always.

### 2.4 Firmware (2215 analysis — receive side only)

From `spec/TDV2200/Testing2215/control_char_handlers_analysis.md`:

- Dispatch: `0x08` → BS handler ~`0x2a6c`
- NVRAM `backspace_enable_flag` at `0x5f22` can disable backspace handling

Does **not** document what the keyboard *sends* for E13/E14; only how the terminal reacts to `0x08` once received.

---

## 3. RetroTerm best-effort policy (current code)

Documented here so hardware validation can mark each item **confirmed / wrong / N/A**.

### 3.1 Virtual TDV keys (`TDV2200KeyRegistry`)

| Key | Extended ON | Extended OFF (2115) | Ctrl variant in registry |
|-----|-------------|---------------------|--------------------------|
| E13 | `CSI 86 _` / `CSI 87 _` | `0x08` | **none** |
| E14 | `0x7F` | `0x7F` | **none** (`AlwaysSameCode`) |
| G47 | `CSI 10 _` / `CSI 11 _` | `0x04` (via `SimpleAscii` — verify) | none |
| B47 | `0x08` | `0x08` | none |

Aliases: `BACKSPACE` → E13, `DELETE`/`DEL` → E14 (virtual keyboard labels).

### 3.2 PC keyboard (`TDV2200KeyboardMapper` + `TerminalCanvas`)

| PC key | RetroTerm sends | Rationale | HW validation |
|--------|-----------------|-----------|---------------|
| **Backspace** (VK 8) | `0x08` | No PC key on TDV; matches User's Guide BS / B47 / E13 (OFF) | ⏳ |
| **Ctrl+Backspace** | `0x00` (intercepted in UI before mapper) | **Not in User's Guide** for E13; inspired by ROM Ctrl columns in some docs | ⏳ **must validate** |
| **Delete** (VK 46) | `CSI 10 _` (G47 STRYK) | PC Delete ≈ US "DELETE" label on G47, terminfo `kdch1` | ⏳ |
| **Shift+Delete** | `CSI 11 _` | G47 shifted | ⏳ |
| **Alt+Delete** | `CSI 10 _` | Default Alt binding → G47 | ⏳ (PC convenience) |
| **Left arrow** (VK 37) | `0x08` | B47 fixed key | ⏳ |

Tests encoding this policy: `tests/RetroTerm.Tests/TDV/TDVMapperNavigationTests.cs`.

### 3.3 Known gaps / intentional guesses

| Item | Guess | Risk if wrong |
|------|-------|---------------|
| PC Delete → G47 not E14 | terminfo `kdch1` + US keycap "DELETE" on G47 | SINTRAN/NOTIS apps expecting `0x7F` on PC Delete |
| PC Backspace → `0x08` not E13 `CSI 86 _` when Extended ON | Treat PC BS as raw BS, not NewParagraph key | Word processors using NewParagraph semantics |
| Ctrl+Backspace / Ctrl+E14 → `0x00` | UI comment only; User's Guide silent | Host may ignore NUL or misinterpret |
| Extended ON + E13 | Registry has no Ctrl path; may need `0x00` or nothing | Unknown |
| `keyboard_documentation.md` E13 Ctrl=`0x00` | Secondary doc; not in User's Guide tables | May reflect 1299 layout or error |

---

## 4. Hardware validation matrix

When hardware is available, capture **raw serial bytes** (hex) for each cell. Record: terminal model, keyboard ROM layout ID (command `0x2F`), national variant, Extended Control switch, Numeric Pad mode, LOCK/CAPS/CTRL state.

### 4.1 Priority A — must capture (blocks confidence in editing)

| ID | Key | Extended OFF | Extended ON | Modifiers to test |
|----|-----|--------------|-------------|-------------------|
| **HW-A01** | E13 (¶ NewParagraph) | Expected: `08` | Expected: `1B 58 36 36 5F` | None, Shift |
| **HW-A02** | E14 (DEL) | Expected: `7F` | Expected: `7F` | None, Shift, **Ctrl** |
| **HW-A03** | B47 (←) | Expected: `08` | Expected: `08` | None, Shift, Ctrl |
| **HW-A04** | G47 (STRYK / US DELETE) | Expected: `04` | Expected: `1B 58 31 30 5F` | None, Shift |
| **HW-A05** | Ctrl+E13 | **Unknown** — doc silent | **Unknown** | Ctrl |
| **HW-A06** | Ctrl+E14 | **Unknown** | **Unknown** | Ctrl |
| **HW-A07** | Ctrl+B47 | **Unknown** | **Unknown** | Ctrl |
| **HW-A08** | Ctrl+H (if distinct from E13) | Compare to E13 | Compare to E13 | Ctrl |

**Pass criteria:** Captured bytes match User's Guide for A01–A04. A05–A08 define RetroTerm Ctrl policy.

### 4.2 Priority B — PC mapping decisions

These have **no TDV physical equivalent**; capture is optional but useful if testing with a serial sniffer on a PC-driven session:

| ID | Question | RetroTerm today | Validate by |
|----|----------|-----------------|-------------|
| **HW-B01** | What do SINTRAN/NOTIS apps expect for "rubout"? | PC BS → `08` | Run NOTIS on ND with real terminal; note which key users press |
| **HW-B02** | terminfo `kbs=177` vs termcap `kb=^H` | Unresolved | `tput kbs` on period ND Unix; capture actual key |
| **HW-B03** | PC Delete semantic | `CSI 10 _` | Compare to G47 vs E14 on real HW |

### 4.3 Priority C — mode and layout variants

| ID | Check | Why |
|----|-------|-----|
| **HW-C01** | ROM layout 965313-0 vs 961292-2 vs 961299-3 | E13/E14 differ between 5313 and 2215/1299 layouts |
| **HW-C02** | Extended switch physically OFF vs ON | Completely changes E13 and G47 |
| **HW-C03** | TDV-2215 vs TDV-2200/9 | 2215 may lack Extended mode |
| **HW-C04** | `backspace_enable_flag` / config menu | Firmware may suppress BS handling even if `08` is received |
| **HW-C05** | Keyboard ROM check command `0x2D` → `AA` | Confirms keyboard EPROM present |

### 4.4 Priority D — fixed "always" keys (User's Guide §8)

Confirm unchanged across Extended ON/OFF and Shift:

| Keys listed as "always" | Expected wire (from guide) |
|-------------------------|----------------------------|
| E14 | `7F` |
| D13 | `0A` (LF) |
| C13 | `0D` (CR) |
| B47 | `08` (note: OCR table once showed `06`/`08` — verify on hardware) |
| B48, B49, C48, A48, G0 | per §8 tables |

---

## 5. How to capture (when hardware is available)

### 5.1 Setup

1. TDV-2200/9 or ND-246 on serial (document baud, parity, 7/8 data bits).
2. Extended Control switch: test **both** positions separately.
3. Note keyboard layout sticker / read layout ID via keyboard diagnostic command `0x2F` (see `keyboard-spec.md` §7).
4. Use a **serial logger** (logic analyzer, RS-232 tap, or `RetroTerm` test server in hex-dump mode) — not the emulator's interpretation.

### 5.2 Procedure per key

1. Clear log.
2. Press key once (no repeat).
3. Record hex bytes in order.
4. Repeat with Shift, then Ctrl (if key accepts Ctrl per physical labeling).
5. For E14/DEL: note on-screen effect (erase at cursor vs rubout left).

### 5.3 Suggested log format

```text
HW-A01 | TDV-2200/9 NO | ROM 5313 | Ext=OFF | E13 | none | 08
HW-A01 | TDV-2200/9 NO | ROM 5313 | Ext=ON  | E13 | none | 1B 58 36 36 5F
```

Store captures under `spec/TDV2200/captures/keyboard/` (create when first capture exists).

### 5.4 Without TDV hardware (interim)

- **Unit tests** against User's Guide tables: extend `TDVMapperNavigationTests` for E13/E14 virtual keys.
- **Cross-check** `TDV2200KeyRegistry.GetSequence()` vs §7.2 / §8 tables.
- **Do not** treat terminfo alone as proof for `kbs`.
- **2215 firmware** analysis validates receive path for `0x08`, not keyboard transmit for E13.

---

## 6. Open questions (require hardware or ROM disassembly)

| # | Question | Spec says | RetroTerm does | Resolution |
|---|----------|-----------|----------------|------------|
| Q1 | E13 with Extended ON — is PC Backspace `08` or `CSI 86 _`? | E13 sends CSI; B47 sends `08` | PC BS → `08` | HW-A01 + PC policy review |
| Q2 | Ctrl+E13 / Ctrl+E14 wire bytes? | User's Guide silent; some docs say `00` | Ctrl+BS → `00`; E14 Ctrl not wired | HW-A05, HW-A06 |
| Q3 | PC Delete → G47 or E14? | No PC key; G47=delete char, E14=DEL | G47 `CSI 10 _` | HW-A04, HW-B03 |
| Q4 | termcap `kb=^H` vs terminfo `kbs=DEL`? | Both in repo | BS=`08` | HW-B02 + host testing |
| Q5 | B47 Extended ON: `08` or OCR typo `06`? | §8 says `08` "always" | `08` | HW-A03 |
| Q6 | Does received `7F` use same code path as `08`+erase? | Text describes DEL behaviour | Emulator handles both | HW + display test |
| Q7 | Layout 1299 E13 Normal=`08` in ROM — wire difference? | 1299 table differs from 5313 | 5313-based registry | HW-C01 |

---

## 7. After hardware capture — update checklist

For each matrix ID:

- [ ] **CONFIRMED** — matches User's Guide; update RetroTerm if needed
- [ ] **DEVIATION** — document delta; attach capture file; file issue
- [ ] **N/A** — wrong keyboard/layout for this test

Then:

1. Update `TDV2200KeyRegistry` / `TDV2200KeyboardMapper` if wire bytes differ.
2. Add regression tests with captured hex strings.
3. Note confirmed behaviour in `spec/Keyboards/keyboard-spec.md` §6.8 footnotes.
4. Record the confirmed rows in `docs/TDV-KEYBOARD-COMPLETE-REFERENCE.md`, the document the
   registry test pins. (The separate validation matrix this step used to name was retired in
   August 2026.)

---

## 8. Reference files

| Path | Content |
|------|---------|
| `spec/TDV2200/OCR/TDV-2200_9-User-s_Guide-ND_combined.md` | §7.1–7.2 (Ext OFF), §8 (Ext ON) |
| `spec/Keyboards/keyboard-spec.md` | ROM layouts, §6.8 mode tables |
| `spec/TDV2200/DOC TDV2200/keyboard_documentation.md` | SDL-oriented table (secondary) |
| `spec/TDV2200/Testing2215/control_char_handlers_analysis.md` | BS handler at `0x2a6c` |
| `docs/TermCap.txt` | NOTIS termcap `tdv2200` |
| `src/RetroTerm.Core/Terminal/Emulators/TDV/TDV2200KeyRegistry.cs` | Virtual key sequences |
| `src/RetroTerm.Core/Terminal/Input/KeyboardMapper.cs` | PC key mapping |
| `tests/RetroTerm.Tests/TDV/TDVMapperNavigationTests.cs` | Mapper regression tests |

---

*Created 2026-06-26. No hardware captures yet — all "Expected" columns derive from scanned documentation only.*
