#!/bin/bash
# crop_keys.sh - Extract individual key images from ND-246 keyboard photo
# Uses ImageMagick 7 (magick) to crop each key by grid position
# Image: 4704x2220 pixels
#
# CALIBRATED coordinates (v4 - final):
#   Left block Y:   G=700  E=1060  D=1260  C=1445  B=1645
#   Middle block Y: G=700  F=965   E=1105  D=1250  C=1390  B=1530  A=1670
#   Right block Y:  G=700  F=925   E=1065  D=1250  C=1390  B=1530  A=1670
#   Left block row spacing: ~200px (bigger keys, housing step from G)
#   Mid/Right row spacing:  ~140px (smaller keys)
#   Right block F/E rows sit ~40px higher than middle block F/E rows
#   G-row step-down gap:    ~260px (left), ~265px (mid/right)

set -e

SRC="/mnt/e/Dev/Ronny/RetroTerm/spec/Keyboards/ND246-Keyboard.jpg"
OUT="/mnt/e/Dev/Ronny/RetroTerm/spec/Keyboards/keys"
mkdir -p "$OUT"

COUNT=0

# crop NAME CX CY [W H]
crop() {
    local name="$1" cx=$2 cy=$3 w=${4:-200} h=${5:-160}
    local x=$((cx - w / 2))
    local y=$((cy - h / 2))
    [ $x -lt 0 ] && x=0
    [ $y -lt 0 ] && y=0
    magick "$SRC" -crop "${w}x${h}+${x}+${y}" +repage -strip "$OUT/${name}.png"
    echo "  $name"
    COUNT=$((COUNT + 1))
}

echo "Cropping keys from ND-246 keyboard photo (4704x2220)..."
echo ""

###############################################################################
# ROW G - Top row, orange flat keys (y=700)
###############################################################################
echo "Row G (left block)..."
crop G0   230  700  200 120    # ESC
crop G1   480  700  170 120    # P1
crop G2   655  700  170 120    # P2
crop G3   830  700  170 120    # P3
crop G4  1005  700  170 120    # P4
crop G5  1180  700  170 120    # P5
crop G6  1355  700  170 120    # P6
crop G7  1530  700  170 120    # P7
crop G8  1705  700  170 120    # P8
# Editing keys shifted 80px left from v2
crop G9  1940  700  200 120    # MERK
crop G10 2115  700  200 120    # FELT
crop G11 2290  700  200 120    # AVSN
crop G12 2465  700  200 120    # SETN
crop G13 2640  700  200 120    # ORD
crop G14 2820  700  220 120    # LOKAL

echo "Row G (middle block)..."
crop G47 3200  700  220 120    # STRYK
crop G48 3410  700  220 120    # KOPI
crop G49 3620  700  220 120    # FLYTT

echo "Row G (right block - shifted +100 from v2)..."
crop G51 3850  700  260 120    # FUNK
crop G52 4055  700  260 120    # SKRIV
crop G53 4260  700  260 120    # HJELP
crop G54 4460  700  260 120    # SLUTT

###############################################################################
# ROW F - Middle block (y=965), right block (y=925)
###############################################################################
echo "Row F..."
crop F47 3200  965  220 130    # TAB+ TAB-
crop F48 3410  965  220 130    # (... ...)
crop F49 3620  965  220 130    # /aaa aaa
crop F51 3850  925  260 130    # F1
crop F52 4055  925  260 130    # F2
crop F53 4260  925  260 130    # F3
crop F54 4460  925  260 130    # F4

###############################################################################
# ROW E - Number row (left y=1060), middle/right (y=1105)
###############################################################################
echo "Row E (left block - numbers)..."
crop E0   300 1060  200 170    # CAPS
crop E1   500 1060  185 170    # 1 !
crop E2   684 1060  185 170    # 2 "
crop E3   868 1060  185 170    # 3 #
crop E4  1052 1060  185 170    # 4 $
crop E5  1236 1060  185 170    # 5 %
crop E6  1420 1060  185 170    # 6 &
crop E7  1604 1060  185 170    # 7 /
crop E8  1788 1060  185 170    # 8 (
crop E9  1972 1060  185 170    # 9 )
crop E10 2156 1060  185 170    # 0 =
crop E11 2340 1060  185 170    # + ?
crop E12 2524 1060  185 170    # @ `
crop E13 2700 1060  200 170    # NewParagraph
crop E14 2850 1060  220 170    # DEL

echo "Row E (middle block)..."
crop E47 3200 1105  220 130    # << >>
crop E48 3410 1105  220 130    # JUST
crop E49 3620 1105  220 130    # <> ><

echo "Row E (right block)..."
crop E51 3850 1065  260 130    # F5
crop E52 4055 1065  260 130    # F6
crop E53 4260 1065  260 130    # F7
crop E54 4460 1065  260 130    # F8

###############################################################################
# ROW D - QWERTY row (left y=1260), middle/right (y=1250)
###############################################################################
echo "Row D (left block)..."
crop D99  235 1260  200 170    # EKSP/INNS
crop D0   430 1260  200 170    # CTRL
crop D11 2460 1260  200 170    # AA-ring
crop D12 2644 1260  200 170    # ^ ~
crop D13 2830 1260  220 170    # LineFeed

echo "Row D (middle block)..."
crop D47 3200 1250  220 130    # RollUp RollLeft
crop D48 3410 1250  220 130    # ANGRE
crop D49 3620 1250  220 130    # RollDown RollRight

echo "Row D (numpad)..."
crop D51 3850 1250  200 140    # 7
crop D52 4055 1250  200 140    # 8
crop D53 4260 1250  200 140    # 9
crop D54 4460 1250  200 140    # SP (numpad comma)

###############################################################################
# ROW C - Home row (left y=1445), middle/right (y=1390)
###############################################################################
echo "Row C (left block)..."
crop C99  225 1445  200 170    # Squiggle (MM)
crop C0   420 1445  200 170    # LOCK
crop C10 2266 1445  200 170    # OE (O-slash)
crop C11 2450 1445  200 170    # AE
crop C12 2634 1445  200 170    # ' *
crop C13 2920 1445  160 230    # CR (tall orange key)

echo "Row C (middle block)..."
crop C47 3200 1390  220 130    # FieldLeft
crop C48 3410 1390  220 130    # Up arrow
crop C49 3620 1390  220 130    # FieldRight

echo "Row C (numpad)..."
crop C51 3850 1390  200 140    # 4
crop C52 4055 1390  200 140    # 5
crop C53 4260 1390  200 140    # 6
crop C54 4460 1390  200 140    # - (minus)

###############################################################################
# ROW B - Bottom letter row (left y=1645), middle/right (y=1530)
###############################################################################
echo "Row B (left block)..."
crop B0   450 1645  200 170    # < >
crop B8  1922 1645  200 170    # , ;
crop B9  2106 1645  200 170    # . :
crop B10 2290 1645  200 170    # - _

echo "Row B (middle block)..."
crop B47 3200 1530  220 130    # Left arrow
crop B48 3410 1530  220 130    # Home
crop B49 3620 1530  220 130    # Right arrow

echo "Row B (numpad)..."
crop B51 3850 1530  200 140    # 1
crop B52 4055 1530  200 140    # 2
crop B53 4260 1530  200 140    # 3
crop B54 4460 1600  200 300    # ENTER (double-height)

###############################################################################
# ROW A - Bottom row: middle block (y=1670), numpad (y=1670)
###############################################################################
echo "Row A..."
crop A47 3200 1670  220 130    # TABLeft
crop A48 3410 1670  220 130    # Down arrow
crop A49 3620 1670  220 130    # TABRight
crop A51 3955 1670  420 140    # 0 (double-wide)
crop A53 4260 1670  200 140    # . (period)

echo ""
echo "Done! $COUNT key images saved to $OUT/"
