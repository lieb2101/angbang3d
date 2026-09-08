# Bridge protocol v1

Line-delimited JSON over the child process's stdin/stdout. One JSON object per
line, UTF-8, no embedded newlines. Everything outside printable ASCII is emitted
as `\uXXXX`, so the stream is valid UTF-8 regardless of the host locale.

stdout carries the protocol and nothing else. Angband's own diagnostics go to
stderr.

## Lifecycle

```
engine -> client   {"t":"hello", ...}
engine -> client   {"t":"frame","seq":1, ...}      <-- game is waiting for input
client -> engine   key x
engine -> client   {"t":"frame","seq":2, ...}
```

### The synchronisation contract

**The engine emits its first frame before the client has sent anything.** A
client must consume that frame at connect, then after each command wait for a
frame whose `seq` is greater than the last one seen. Clients that assume
one-frame-per-command without checking `seq` will read stale state and appear
to have no effect on the game.

`seq` increases by at least one per frame and never resets.

## Messages from the engine

### `hello`

Sent once at startup.

| field | type | meaning |
|---|---|---|
| `protocol` | int | Protocol version. Currently `1`. |
| `build` | string | Angband build id, e.g. `"Angband 4.2.6"`. |
| `savefile` | string | Full path of the savefile in use. |
| `save_dir` | string | Directory savefiles are read from and written to. |

### `frame`

Sent whenever the game blocks for input.

| field | type | meaning |
|---|---|---|
| `seq` | int | Monotonic frame counter. |
| `phase` | string | `"setup"` during splash/birth/level generation, `"play"` once the player is placed on a level. |
| `ui` | object | Input state: what the game is currently asking for. |
| `player` | object or null | Null before a character exists. |
| `map` | object or null | Null unless `phase` is `"play"`, or if map output is disabled. |
| `monsters` | array | Currently visible monsters. |
| `objects` | array | Currently visible floor objects. |
| `messages` | array | Up to 24 recent messages, oldest first. |
| `term` | object | The raw character grid. |

Only read `map`, `monsters` and `objects` when `phase` is `"play"`.

#### `ui`

```json
{"overlay": 3, "awaiting_command": false, "more": false}
```

| field | meaning |
|---|---|
| `overlay` | Angband's `screen_save_depth`. Non-zero means a screen has been pushed: a menu, store, character sheet or item prompt. |
| `awaiting_command` | True when the game wants a normal game command. False when it is asking a question. |
| `more` | A `-more-` message pause is waiting to be acknowledged. |

**Clients should show the terminal channel whenever `overlay > 0`, `more` is
true, or `awaiting_command` is false.** Those states exist only as text. A
client that keeps rendering its own world view during them will swallow the
player's keystrokes into an invisible menu, which is indistinguishable from a
freeze.

#### `player`

| field | meaning |
|---|---|
| `name`, `race`, `class` | Character identity. |
| `y`, `x` | Position on the level. |
| `depth`, `max_depth` | Current and deepest level reached. `0` is town. |
| `level`, `exp`, `gold` | Character level, experience, gold. |
| `hp`, `hp_max`, `sp`, `sp_max` | Hit and spell points. |
| `light` | **The player's own light radius** (torch, lantern). Drives client torch lighting. |
| `grid_light` | **Ambient light of the square the player stands on.** Distinct from `light`; this is what Angband's status bar shows as "Light N". |
| `speed` | Current speed. |
| `dead` | Whether the character has died. |
| `wizard` | Whether wizard (god) mode is currently active. |
| `noscore` | Cheat flags. Non-zero means the game is unscored. `2` = wizard mode has been used. Persists in the savefile. |

#### `map`

```json
{"h": 65, "w": 178, "rows": [{"g": "...", "a": "...", "f": "...", "l": "..."}]}
```

`rows` has exactly `h` entries. Each row carries four parallel encodings:

| key | length | meaning |
|---|---|---|
| `g` | `w` chars | Glyph per cell, exactly what Angband would draw. |
| `a` | `2w` chars | Colour attribute per cell, two hex digits. |
| `f` | `2w` chars | Terrain feature index per cell, two hex digits. |
| `l` | `w` chars | Flags per cell, one hex digit. |

Flag bits:

| bit | meaning |
|---|---|
| `0x1` | Known — the player has seen or mapped this square. |
| `0x2` | In view — currently visible. |
| `0xC` | Lighting, shifted right 2: `0` line-of-sight, `1` torchlight, `2` lit, `3` dark. |

The intended rendering is: *in view* is lit, *known but not in view* is drawn
dimmed (the player's memory), and everything else is not drawn at all.

The map is produced through Angband's own `map_info()` and
`grid_data_as_text()`, so it always matches what the game itself would display,
including hallucination and detection effects.

#### `monsters` / `objects`

Each entry has `y`, `x`, `attr` and `glyph`. Monsters additionally carry `race`,
`race_level`, `hp` and `hp_max` when the monster is resolvable. Objects carry
`pile`, true when more than one item occupies the square.

Only *visible* entities are listed, which is why the list is usually empty right
after arriving on a level.

#### `term`

```json
{"w": 80, "h": 24, "cx": 12, "cy": 3, "cursor": true, "rows": [{"g": "...", "a": "..."}]}
```

The full character grid, including the map region. Clients that render the world
themselves should mask out the map area and draw only the surrounding text.
`cx`/`cy` are the cursor position; `cursor` is whether it should be shown.

### `ok` / `error` / `pong` / `bye`

Acknowledgements and diagnostics, carrying an optional `detail` string. `bye` is
the last message before the engine exits.

## Commands to the engine

One per line.

| command | effect |
|---|---|
| `key <spec>` | Queue one keypress. Advances the game. |
| `keys <text>` | Queue every character of `text` as keypresses. |
| `map on` / `map off` | Enable or disable map serialisation. Off is much cheaper when only the text UI matters. |
| `frame` | Re-emit the current frame without advancing the game. |
| `ping` | Replies `pong`. |
| `quit` | Exit immediately **without saving**. |
| `# ...` | Comment, ignored. |

Only `key` and `keys` advance the game; everything else is answered and then the
engine keeps waiting.

### Key specifications

Named: `left` `right` `up` `down` `space` `enter` `return` `escape` `tab`
`backspace` `delete` `home` `end`.

Control: `C-<letter>`, e.g. `C-s` to save, `C-w` to toggle wizard mode.

Anything else uses its first character literally, so `key >` descends and
`key @` completes character creation randomly.

## Worked example

```
key enter        # dismiss the splash screen
key @            # random race/class/etc, repeat until phase == "play"
key >            # descend
key C-w          # toggle wizard mode (then dismiss -more-, answer y)
key C-a          # debug menu, then a command key, then confirm with y
key C-s          # save
```

`tools/bridge.py` is the reference client and wraps all of this.

## Compatibility

New fields may be added to any object within protocol version 1. Clients must
ignore unknown fields. Removing or repurposing a field requires a version bump.
