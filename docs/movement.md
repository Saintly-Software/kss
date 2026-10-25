# Movement prototype

The main scene (`main.tscn`) is a test room for Kirby's movement. The sprite viewer is still available as `viewer.tscn`.

## Controls

| Action | Keyboard | Gamepad |
|---|---|---|
| Move | Arrows or WASD | D-pad or left stick |
| Jump (press again in the air to float) | Z, Space or K | A (bottom) |
| Float flap | Jump or Up | A or d-pad up |
| Exhale | X or J | X (left) |
| Slide | Down + jump | Down + A |
| Run (Modern only) | Shift or C | B (right) or RB |
| Switch preset | Tab | Back |
| Toggle always run | R | LB |
| Reset position | Backspace | Start |

## Presets

All values live in a `MovementProfile` resource (`src/Movement/MovementProfile.cs`). The two presets are `profiles/classic.tres` and `profiles/modern.tres`. While the game runs from the editor, select the `Playground` node in the **Remote** scene tree and edit `Classic` or `Modern` in the inspector; changes apply on the next tick.

Both presets share the level-critical values: walk and run top speed, air top speed, jump arc and maximum height (49.9 px), gravity, terminal fall speed, float fall speed and flap, slide, and the collision box.

| | Classic | Modern |
|---|---|---|
| Start running | Double tap within 20 ticks | Double tap, or hold a direction for 30 ticks (0.5 s), or hold Run; optional always run |
| Release the d-pad | Brake pose; speed drops 0.1875/tick | Speed drops 0.25/tick, no special pose |
| Reverse while walking | Instant, keeps speed | Speed reverses at 0.5/tick |
| Reverse while running | Skid without control until stopped, facing the old way | Faces the new way at once; skid pose while reversing from full speed, control kept |
| Air steering | 0.1875/tick accel and turn | 0.25 accel, 0.375 turn |
| Running jump | 2.0 top speed with half the air control, drops to 1.4375 after 32 ticks | Keeps 2.0 top speed until landing, normal air control |
| Top of a jump | Constant gravity | Gravity ×0.6 for the first ticks of falling while jump is held (same peak height, longer hang) |
| Float sideways | 0.09375/tick accel and turn | 0.1875 accel, 0.25 turn |
| Releasing jump while flapping | Climb stops on the next 16-tick beat | Climb stops at once |

`docs/rom-notes.md` lists where every Classic number comes from and which ones are estimates.

## Automated checks

`test/input/*.txt` are input scripts (format in `src/Game/InputScript.cs`):

```bash
/Applications/Godot.app/Contents/MacOS/Godot --path . -- --script=test/input/tour.txt --trace=trace.csv --shots=/tmp/shots --preset=modern
```

The trace has one CSV row per tick: state, pose, frame, position, velocity, grounded, facing and input.
