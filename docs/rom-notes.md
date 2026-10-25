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

**Layout substitutes (empirical).** Some frames' layout entries do not match the tiles their graphics upload, so they draw scrambled. `KirbySprites.GetLayout` substitutes two layouts that make them draw correctly. The real lookup the game uses for these frames is still unknown.

| Frames | Substitute | Evidence |
|---|---|---|
| `$00-$21` | Layout entry `$12`: one 16×16 and five 8×8 sprites, 24×24 around the origin | Every frame in this range uploads 5+4 (or 5+5) tiles in the same shape. With this layout, idle, walk, run and fall all look right, and the result has the same bounding box as blink frame `$6A`, which uses its own layout. |
| `$CA-$D3` | 2×2 grid of 16×16 sprites, bottom edge 12 px below the origin | These frames upload 8+8 tiles. With the grid they show the puffed-up float poses cleanly. |

Graphics sources for these frames can be unaligned (e.g. `$EB:8125`). That is how the game stores them, not a bug in our reader: decoding from the exact address gives clean tiles.

**Known gaps.** Frames `$1A` (run) and `$2E` (slide) upload 6+5 tiles and fit neither layout; the prototype shows `$18` and `$52` instead. Most frames above `$80` other than the puffed ones still render partly scrambled. Frames `$14E` and up come from an ability-specific table at `[$7E]`.

## Animation scripts

Two formats, both in bank `$C4`:

- **Frame lists**, played by `$03:960B` (looping) and `$03:954E` (holds the last frame). 16-bit words: frame in bits 0-9, duration in ticks in bits 10-15; `0000` ends the list. The frame number is stored straight into `$6436,X`.
- **Object scripts**, run by `$00:937A`/`$00:9368`. A byte stream of commands; an odd command byte is followed by a wait in ticks. Command `$47` (= `$23` with a wait) is "set frame, then wait": `47 WAIT FRAME_LO FRAME_HI`. Command `$02` ends the script (carry set on return). Commands `$2C`/`$2E` (bytes `$59`/`$5C`, `$5D` with a wait) set `$765B,X` and `$7663,X`, an extra horizontal acceleration and velocity added on top of normal movement; slides use them.

Kirby's movement animations, confirmed by finding the state code that plays each one:

| Pose | Source | Frames (ticks) |
|---|---|---|
| Idle | object script `$C4:0CF3`, from idle state `$02:80EF` | `$00` 60, `$6A` (blink) 3, `$00` 20, `$6A` 3, `$00` 60, `$6A` 3, `$00` 40, loop |
| Idle on slopes | `$03:9590` adds 2 or 4 by slope | `$02`, `$04` |
| Walk | frame list `$C4:0634`, walk state `$02:81BF` | `$0C` 9, `$0E` 6, `$10` 5, `$12` 9, `$10` 6, `$0E` 5 (Data Crystal was right) |
| Run | frame list `$C4:0642`, run state `$02:828F` | `$14` 4, `$16` 3, `$18` 2, `$1A` 4, `$18` 3, `$16` 2 |
| Brake / skid | brake state `$02:837D` | `$6C` |
| Crouch | idle state with Down held, `$02:812A` | `$4E` |
| Landing | landing state `$02:8629`, 2 ticks (8 after a big fall) | `$4E` |
| Jump | object script `$C4:0B14`, jump state `$02:8430` | `$1C` 20 (rising), then tumble `$6E` 4, `$70` 4, `$6C` 4 |
| Fall | fall state `$02:867E`, table `$02:8699` | `$1E`; after 20-29 ticks `$6E` |
| Float start | frame list `$C4:0994`, float state `$02:9191` | `$CA` 4, `$CC` 6, `$CA` 8 |
| Float | frame list `$C4:099C`, float state `$02:91FE` | `$CE` 4, `$D0` 6, `$CE` 8 |
| Slide | object script `$C4:0B93`, slide state `$02:8AB3` | `$2E` |
| Inhale | `$03:9590` with X=6 | `$06` |
| Exhale | float exhale | `$164`, `$14E` (ability table; the prototype shows `$D2`) |

The crouch and landing frames sit 10 px higher in their layouts than the standing frames. The game probably corrects this with the per-object draw offset `$76AF,X`, which `$00:B1B6` adds to Y. The prototype draws them 10 px lower; the exact value is an estimate.

## Kirby's movement

Kirby is object slot X = 2; the helper (player 2) is X = 4. Speeds are **8.8 fixed point pixels per tick** at 60 ticks per second: `$0150` = 1 + `$50`/256 = 1.3125 px/tick. Evidence: `$03:AC43` splits a velocity into whole pixels (high byte) and a fraction (low byte) before `$03:A70F` adds them to the position and subpixel.

### Object RAM for movement

| RAM | Meaning | Evidence |
|---|---|---|
| `$6986,X` / `$6A00,X` | X / Y position used by physics (pixels) | `$03:A70F` adds velocity; `$00:A89D` copies to `$6892/$690C` |
| `$6A7A,X` / `$6AF4,X` | X / Y subpixel | `$03:A70F`, `$00:A802` |
| `$37A2,X` | Horizontal speed (unsigned) | Updated by the X step `$03:AF12`-`$03:AF66` |
| `$6DD0,X` | Direction of motion; negative = left | `$03:ABCE`; signs the speed in `$03:A86C` |
| `$74C9,X` | 0 = move toward facing, 2 = locked right, 4 = locked left | `$03:AF36`/`$03:AF3E`, table `$03:AC14` |
| `$6B6E,X` / `$6BE8,X` | X velocity / Y velocity (signed, negative Y = up) | `$03:A860`; jump code writes `$6BE8` |
| `$6C62,X` / `$6CDC,X` | X / Y acceleration added to the velocity every tick | `$00:A81B`, `$03:A860` |
| `$36FA,X` | Facing. Set from the d-pad every tick by the X step (`$03:AEC5`) | |
| `$633C,X` | State handler address in bank `$02` | Every state stores its own address |
| `$7491,X` / `$7495,X` | State id / previous state id | `$03:8DBA` on every state change |
| `$74BD,X` | Jump boost countdown, set to 4 on take-off | `$03:9057`, used by `$03:ADE4` |
| `$6FB8,X` | Flap cooldown while swimming (12 ticks) | `$03:B2EC` |
| `$368E,X` / `$3692,X` | Buttons held / newly pressed (SNES joypad bits) | Masks `$0800` up, `$0400` down, `$0300` left/right |
| `$369E,X` `$36A2,X` `$36A6,X` `$36AE,X` | Per-player button masks: left/right, down, attack, jump | Inferred from how each state uses them |
| `$367E,X` | Head of a 32-tick ring buffer of held buttons, used for double taps | `$03:EBC9` |

State ids (from the `X` passed to `$03:8DBA`): `$00` idle, `$02` walk, `$04` run, `$06` brake, `$08` inhale, `$0E` jump, `$10` running jump, `$12` fall, `$18`/`$1A` float, `$24` swim, `$34` slide.

### Physics routines

`JSL $03:ADD1` with X = horizontal parameter index and Y = vertical parameter index runs one tick: the horizontal step `$03:AE29`, the vertical step `$03:B04F`, then movement and collision `$03:A4A4`. `$03:ADE4` is the same but uses vertical index 2 while `$74BD,X` counts down. Index 0 skips that axis.

**Horizontal parameters.** Pointer table at `$9C`, set by `$03:8CC1` from `$C4:6392` indexed by `$32EC` (normally `$C4:5C61`). Each entry points to 5 words: pointer to the d-pad mask, pointer to a "treat as no input" mask (Down), top speed, acceleration, deceleration. Each tick, `$03:AE96` then:

- d-pad matches the direction of motion: speed moves toward the top speed by the acceleration, from either side.
- d-pad opposes the direction of motion: speed drops by the acceleration; at 0 the direction flips.
- no d-pad: speed drops by the deceleration, to 0.

| Index | Used by | Top speed | Accel | Decel |
|---|---|---|---|---|
| `$02` | Walk | `$0150` (1.3125) | `$50` (0.3125) | `$10` (0.0625) |
| `$04` | Run | `$0200` (2.0) | `$30` (0.1875) | `$10` |
| `$06` | Brake / skid (ignores the d-pad) | `0` | `$20` (0.125) | `$30` (0.1875) |
| `$08` | Inhale | `0` | `$20` | `$30` |
| `$0A` | Jump, fall | `$0170` (1.4375) | `$30` (0.1875) | `$08` (0.03125) |
| `$0E` | Running jump | `$0200` (2.0) | `$18` (0.09375) | `$04` (0.015625) |
| `$10` | Float | `$0110` (1.0625) | `$18` (0.09375) | `$04` (0.015625) |
| `$20`, `$22` | Swim | `$0200` / `$0170` | `$200` / `$30` | `$100` / `$08` |

**Vertical parameters.** Pointer table at `$A0` (normally `$C4:5C8B`). Each entry is a target speed and a step: each tick Y velocity moves toward the target by the step.

| Index | Used by | Target | Step |
|---|---|---|---|
| `$02` | Jump boost (first 4 ticks) | `$0380` (3.5) | `$0286` (2.5234) |
| `$04` | Normal gravity | `$0380` (3.5) | `$2C` (0.171875) |
| `$06` | Float | `$0160` (1.375) | `$18` (0.09375) |
| `$1E` | Swim | `$0080` (0.5) | `$0D` |

### Movement rules found in the state code

- **Jump** (`$02:83E7`): Y velocity = `$F300` (−13), then 4 boost ticks pull it toward +3.5 by 2.5234 per tick (−10.48, −7.95, −5.43, −2.91), then normal gravity. Peak: **49.9 px** above the take-off point, about 20 ticks after take-off. The jump animation lasts 32 ticks, then the fall state takes over.
- **Running jump** (`$02:9939`): a jump from the run state with a direction held. Speed is set to `$0200` and uses horizontal index `$0E`, whose acceleration is half the normal air value. That is why classic air turns feel stiff.
- **Double tap** (`$03:EBC9`, called with window `$28`): looks back 20 ticks in the input history for "held, released, held". Idle and walk use it to start running.
- **Walk** (`$02:96AF`): changing direction resets the motion lock, so Kirby turns instantly at the same speed. Releasing the d-pad goes to the brake state.
- **Run** (`$02:96F2`): releasing the d-pad or pushing the other way enters the brake state with the skid sound. On a reversal, facing is flipped back so Kirby skids facing his old direction.
- **Brake** (`$02:8347`, `$02:9722`): frame `$6C`, speed −0.1875 per tick. After a walk, any direction leaves it at once. After a run, only the same direction (back to running) or a double tap leaves it; otherwise Kirby skids until stopped.
- **Slide** (`$02:991A` → `$02:8A98`): Down + jump on the ground. The slide script sets an extra velocity of 2.75 px/tick for 16 ticks, then −0.125 per tick for 16 ticks, then clears it. Total about 70 px.
- **Float** (`$02:99B5` → `$02:9137`): pressing jump in the air re-enters the float state. Holding jump re-flaps every 16 ticks (`$6F3E,X`). Releasing it stops a climb (Y velocity = 0), but this is only checked on those 16-tick beats. The float state writes Y velocity `$FD00` (−3) at `$02:91C2`, but under a collision-flag condition I could not decode.
- **Swim flap** (`$03:B2B1`): Y velocity `$FEB0` (−1.3125) with a 12-tick cooldown; included here because it is easy to mistake for float.

### Not pinned down (estimates in the Classic preset)

| Value | Estimate | Why unsure |
|---|---|---|
| Short hop when jump is released early | Rise capped at −1 px/tick after the boost (about 29 px total) | No release check found in the jump state or its script |
| Flap strength | −3 px/tick per flap (from `$02:91C2`) | Trigger condition not decoded |
| Collision box | 16 wide, 20 tall, bottom 12 px below the origin | Hitbox index `$7549,X` and the collision code `$01:D206` not decoded |
| Landing on the ground while floating | Kirby exhales | The original keeps him puffed and walking; not implemented |
| Exhale | 18 ticks of normal gravity | Exhale state not traced |

### Measuring in Mesen 2

Open **Debug → Watch** with the **SA-1** CPU selected. `{$addr}` reads a 16-bit word.

1. **Speeds.** Walk right and watch `{$37A4}`: expect `$0150`. Double-tap to run: expect `$0200`.
2. **Jump arc.** Stand still, note `{$6A02}`, then hold the jump button. Watch `{$6BEA}` frame by frame (frame advance): expect `$F586`, `$F80C`, `$FA92`, `$FD18`, then +`$2C` per frame. The lowest `{$6A02}` should be 49-50 below the start.
3. **Short hop.** Tap jump for one frame and record the lowest `{$6A02}`. This replaces the 29 px estimate.
4. **Float.** In the air, press jump once and frame-advance, recording `{$6BEA}` for 30 frames. Then hold jump for 2 seconds and note how far `{$6A02}` drops per second. This pins the flap strength and the hold-to-climb rate.
5. **Skid.** Run, then press the other direction. Count frames while `{$7493}` is `6` and record `{$37A4}` each frame: expect −`$30` per frame.
6. **Double-tap window.** Tap right, wait N frames, tap right again. Find the largest N that still sets `{$7493}` to `4`. Expect about 19.
7. **Collision box.** Walk into a wall and stop: the wall's X minus `{$6988}` is half the box width. Stand on flat ground: the tile's top Y minus `{$6A02}` is the box bottom. Jump into a ceiling: `{$6A02}` minus the ceiling's bottom Y is the box top.
8. **Slide.** Down + jump. Record `{$6988}` before and after, and the frame count until `{$7493}` changes from `$34`. Expect about 70 px over 32 frames.

## Other references

- Bank `$FF` holds pointer tables for rooms and level graphics (Super Famicom Wiki).
- Level data and most background graphics are compressed with HAL's LZ format (exhal). No decompressor written yet.

Sources: [Super Famicom Wiki](https://wiki.superfamicom.org/kirby-super-star), [Data Crystal ROM map](https://datacrystal.tcrf.net/wiki/Kirby_Super_Star/ROM_map).

## Tools

- `tools/dis65816.py ROM BANK:ADDR [COUNT]` disassembles 65816 code.
- `Godot --path . res://viewer.tscn -- --export-sheet=out.png` saves every Kirby frame to one image.
- `Godot --path . -- --script=test/input/tour.txt --trace=trace.csv --shots=DIR` plays scripted input in the movement test scene, writes Kirby's state every tick and saves screenshots. Add `--preset=modern` for the Modern preset.
