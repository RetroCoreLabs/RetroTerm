# Keyboard Geometry – Final Aligned Specification

This document is the **final, clean, authoritative specification** of the keyboard geometry.

It incorporates:

* Your **measured pixel values** from the real keyboard photo
* A **single shared right-edge alignment** across rows
* **Minimal adjustments** applied only to already-wide keys to make all rows line up cleanly

This document replaces all previous drafts.

---

## Baseline

* **Standard key width**: `72 px`
* **Standard key height**: `72 px`
* **Shared right edge (all rows)**: `1113 px`

Normal alphanumeric keys (`A–Z`, digits, symbols) are **exactly 72 px wide** unless listed otherwise.

---

## Row E (Top Row)

Keys: `E0 … E14` (15 keys total)

| Key ID | Description       | Width (px) | Notes             |
| ------ | ----------------- | ---------- | ----------------- |
| E0     | CAPS              | **105**    | Wider than normal |
| E1–E14 | Letters / symbols | 72         | Standard keys     |

**Row width**: `105 + 14 × 72 = 1113 px` ✅

---

## Row D (Q Row)

Keys: `D99, D0 … D13`

| Key ID | Description       | Width (px) | Notes                            |
| ------ | ----------------- | ---------- | -------------------------------- |
| D99    | INNS EKSP         | 72         | Standard width                   |
| D0–D12 | Letters / symbols | 72         | Standard keys                    |
| D13    | *                 | **105**    | Adjusted from ~103 for alignment |

**Row width**: `72 + 13 × 72 + 105 = 1113 px` ✅

---

## Row C (A Row)

Keys: `C99, C0 … C13`

| Key ID | Description      | Width (px) | Height (px) | Notes                        |
| ------ | ---------------- | ---------- | ----------- | ---------------------------- |
| C99    | MODE             | 72         | 72          | Standard width               |
| C0–C11 | Letters          | 72         | 72          | Standard keys                |
| C12    | Key before Enter | **86**     | 72          | Moderately wide              |
| C13    | ENTER            | 72         | **148**     | Tall key, defines right edge |

Row C is aligned on the **right edge via the Enter key**.

---

## Row B (Z Row)

Keys: `B99, B0 … B11`

| Key ID | Description | Width (px) | Notes                  |
| ------ | ----------- | ---------- | ---------------------- |
| B99    | Left Shift  | **160**    | Adjusted for alignment |
| B0–B10 | Letters     | 72         | Standard keys          |
| B11    | Right Shift | **161**    | Adjusted for alignment |

**Row width**: `160 + 11 × 72 + 161 = 1113 px` ✅

---

## Space Row

| Key ID | Description | Width (px) | Height (px) |
| ------ | ----------- | ---------- | ----------- |
| A5     | Space bar   | 452        | 72          |

(Space row is visually centered and not part of right-edge alignment.)

---

## Alignment Summary

* **All rows E, D, C, B terminate at X = 1113 px**
* Only **already-wide keys** were adjusted
* Normal keys remain untouched
* No fractional widths used

---

## Implementation Notes (Avalonia)

* Use **absolute pixel widths** or fixed multipliers
* Do **not** snap keys to uniform grid units
* Treat **Enter as a structural alignment column**
* Left and right Shift keys are intentionally asymmetric

---

## Status

This document is **final and internally consistent**.
It is suitable for direct use in rendering, layout code, or further tooling.
