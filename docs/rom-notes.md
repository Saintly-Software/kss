# Kirby Super Star (USA) — ROM notes

Findings from reverse engineering, with how each was confirmed. Addresses are SA-1 CPU addresses (`$BB:AAAA`) unless marked as a file offset (`0x……`).

## Cartridge

- 4 MiB, SA-1 coprocessor, 8 KiB battery SRAM. SHA-1 `cca1e67fa230bf4f60154f5ea55d06568229dc5d`.
- Banks `$C0-$FF` map 64 KiB each: file offset = `(bank - $C0) << 16 | addr`.
- Banks `$00-$3F` / `$80-$BF` map 32 KiB each at `$8000-$FFFF`: file offset = `(bank & $3F) << 15 | (addr & $7FFF)`.
- `RomImage.ToFileOffset` implements both.

## Object RAM (per object, indexed by X = slot × 2)

| RAM | Meaning | Evidence |
|---|---|---|
| `$6436,X` | Current animation frame. Bit 0 = facing left. | Read by graphics upload `$05:9A40` and layout draw `$00:B23C` |
| `$6536,X` / `$65B0,X` | Pointer (low word / bank) to the object's layout table | `$00:AA6A`, `$00:B248` |
| `$6630,X` | Draw routine (`$B19E` for players) | `$00:AA8A`, `$03:EAAF` |
| `$6892,X` / `$690C,X` | X / Y position (pixels) | Subtracted from camera `$735E/$7360` before drawing |
| `$749D,X` | Player character type; selects layout table | `$03:EA98` |
| `$36FA,X` | Facing (negative = flipped) | `$00:B23C` |

## Kirby sprite frames

A frame is drawn in two steps. Both are implemented in `src/Rom/KirbySprites.cs`.

**1. Graphics upload.** `$05:9A40` indexes the word table at `$C7:0000` with `frame & ~1` (so both facings share graphics). Each word points (same bank) to a list of 7-byte records ending in `00 00`:

| Bytes | Meaning |
|---|---|
| 0-1 | Length in bytes (multiple of 32 = whole 4bpp tiles) |
| 2-3 | VRAM word address. `$6000` = top row of Kirby's slot, `$6100` = bottom row (16 tiles later) |
| 4-6 | 24-bit source address of uncompressed 4bpp tiles (around file `0x2C0000-0x2F0000`) |

Frames ≥ `$14E` and character types `$12`/`$13` use a different table at `[$7E]` (not yet explored).

**2. Sprite layout.** `$00:B23C` reads layout table entry `frame × 2`. The table for character type T is at bank `$C4:DEF3+T`, address `$C4:DECC+2T`. Normal Kirby is type 2 (`$C5:D748`); this was chosen because its layouts use only the tiles that the graphics uploads provide, for 184 of 300 frames. Layout data is in bank `$C5` for entries `< $44`, otherwise `$C6` (`$C7` for types ≥ `$11`).

Each layout is a list of 5-byte hardware sprites, front-most first, written to OAM by `$00:ACEF`:

| Byte | Meaning |
|---|---|
| 0 | Bit 0: 16×16 (else 8×8). Bit 1: last sprite |
| 1 | Signed X offset |
| 2 | Signed Y offset |
| 3-4 | OAM attribute word: tile (bits 0-8), palette (9-11), priority (12-13), H flip (14), V flip (15) |

Tile numbers point into Kirby's VRAM slot, which starts at tile `$20`, so `tile & $1F` gives the index within the uploaded graphics. Frames `< $1C` store the tile without `$20` and the draw code ORs in `$0420`.

**Palette.** Kirby's 16 colors are at `$C4:71F6` (file `0x0471F6`). Many palettes in `$C4:6800-$C4:7200` share Kirby's first 11 colors and differ in the last 5, which are probably the copy-ability hat colors.

**Known gaps.** Frames `$00-$21` and most frames above `$80` render partly scrambled in the viewer. They probably belong to effects or copy abilities that use different palettes, layout tables or tile slots.

## Animation scripts

Per Data Crystal, Kirby's animations live around file `0x040634`. Words with frame in bits 0-9 and duration in bits 10-15, e.g. walking: frames `$0C, $0E, $10, $12, $10, $0E`. Not yet verified: in our layout table those frame numbers do not look like walking, so the script may use a different numbering.

## Other references

- Bank `$FF` holds pointer tables for rooms and level graphics (Super Famicom Wiki).
- Level data and most background graphics are compressed with HAL's LZ format (exhal). No decompressor written yet.

Sources: [Super Famicom Wiki](https://wiki.superfamicom.org/kirby-super-star), [Data Crystal ROM map](https://datacrystal.tcrf.net/wiki/Kirby_Super_Star/ROM_map).

## Tools

- `tools/dis65816.py ROM BANK:ADDR [COUNT]` disassembles 65816 code.
- `Godot --path . -- --export-sheet=out.png` saves every Kirby frame to one image.
