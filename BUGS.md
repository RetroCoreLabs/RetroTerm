# Known defects in the code

Defects found in the source and not yet fixed. Each entry names the file and lines, the evidence,
and how far it has been checked. Fix it or leave it here; never work around it.

## B1 - NDSAR reads the attribute from the wrong parameter

`src\RetroTerm.Core\Terminal\Emulators\TDV\TDVEmulatorBase.cs` lines 1014 to 1027,
`HandleSetAttributeInRectangle`, reads `parameters[0]` as the attribute and `parameters[1..4]` as
the two corners.

ND-1200 (`spec\TDV1200\ND-12054-1-EN_combined.md` lines 3184 to 3187, section 5.50) gives the
sequence as `CSI l1 ; c1 ; l2 ; c2 ; a1 ; ... an z` - the two corners FIRST and the attributes
LAST, more than one allowed. So the code takes a line number as the attribute and the first
attribute as a column.

The retired `TDV-IMPLEMENTATION-REFERENCE.md` agreed with the code, and both disagree with the
manual. Not verified beyond the two texts: no real terminal has been sent an NDSAR, and no test
here pins either order against a citation. Fixing it means reading the manual's parameter list
for a1..an (which attributes, and whether they combine), then writing the test from the manual
before changing the code.

## B2 - Two dead `$y` reply helpers, and a `?` where `>` is expected

`src\RetroTerm.Core\Terminal\Emulators\TDV\TDVEmulatorBase.cs`:

- Line 1638, `HandleWorkAreaQuery`, and line 1654, `HandlePUSHKeyQuery`, both answer with a
  `CSI ? ... $ y` shape and have no callers. That shape comes from the same uncited documents
  that gave this program DECRQM on a TDV; no TDV manual held here has a `$` intermediate
  anywhere. Two sibling helpers with the same shape were deleted on 11 September 2026 (the
  comment between them says why). These two were left behind.
- Line 1578, `HandleSecondaryDeviceAttributesQuery`, returns `CSI ? 1 ; 0 ; 0 c`. A secondary
  DA reply is marked with `>`, not `?`; `?` is the primary DA reply marker. The call site at
  line 1724 only reaches this method for a `CSI > c` request, and all three TDV classes
  (`TDV1200Emulator.cs` line 230, `TDV2200Emulator.cs` line 788, `TDV2215Emulator.cs` line 575)
  override it and answer with `>`. So the wrong shape is never sent today; it is a wrong default
  waiting for a fourth TDV class. Checked 29 September 2026 by reading the callers and overrides.

Not verified beyond the two texts for the `$y` helpers: no manual, capture or host has been
consulted about what a TDV really answers for a work-area or PUSH-key question, if anything.
