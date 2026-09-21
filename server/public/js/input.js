/**
 * Angband3D Input Controller — Keyboard, Mouse, and Touch Navigation
 * Provides 1:1 parity with the Godot C# desktop client.
 */

class InputController {
    constructor(network, dungeon, terminal, audio, toggleTerminalView) {
        this.network = network;
        this.dungeon = dungeon;
        this.terminal = terminal;
        this.audio = audio;
        this.toggleTerminalView = toggleTerminalView;

        this.inTerminal = false;
        this.setupKeyboardEvents();
        this.setupActionButtons();
        this.setupTouchEvents();
    }

    setTerminalMode(active) {
        this.inTerminal = active;
    }

    setupKeyboardEvents() {
        window.addEventListener('keydown', (e) => {
            // Unlock audio on first user key interaction
            if (this.audio) this.audio.unlock();

            // Toggle Fullscreen (F11 or Alt+Enter)
            if (e.key === 'F11' || (e.key === 'Enter' && e.altKey)) {
                e.preventDefault();
                this.toggleFullscreen();
                return;
            }


            // In Death Screen Modal: support tabs 1-5, [R] reload, [N] reroll, [M / Esc] menu, [u] identify, and full interactive terminal on Tab 0
            const deathModal = document.getElementById('death-modal');
            if (deathModal && !deathModal.classList.contains('hidden')) {
                const activeTab = (window.__app && window.__app.hud) ? (window.__app.hud.activeDeathTab || 0) : 0;

                // 1-5 direct tab jump
                if (['1', '2', '3', '4', '5'].includes(e.key)) {
                    e.preventDefault();
                    if (window.__app && window.__app.hud) {
                        window.__app.hud.switchDeathTab(parseInt(e.key, 10) - 1);
                    }
                    return;
                }

                // Tab or ArrowRight to cycle forward; ArrowLeft to cycle backward (matches Godot Main.cs:1696-1709)
                if (e.key === 'Tab' || e.key === 'ArrowRight') {
                    e.preventDefault();
                    if (window.__app && window.__app.hud) {
                        window.__app.hud.switchDeathTab((activeTab + 1) % 5);
                    }
                    return;
                }
                if (e.key === 'ArrowLeft') {
                    e.preventDefault();
                    if (window.__app && window.__app.hud) {
                        window.__app.hud.switchDeathTab((activeTab + 4) % 5);
                    }
                    return;
                }

                // Identify items in death screen (matches Godot Main.cs:1680)
                if (e.key === 'u' || e.key === 'U') {
                    e.preventDefault();
                    this.network.sendKey('u');
                    return;
                }

                // [R] Reload save / restart game (matches Godot Main.cs:1684)
                if (e.key === 'r' || e.key === 'R') {
                    e.preventDefault();
                    this.network.sendKey('R');
                    return;
                }

                // [N] Reroll new character (matches Godot Main.cs:1688)
                if (e.key === 'n' || e.key === 'N') {
                    e.preventDefault();
                    if (window.__app && window.__app.hud && window.__app.hud.rerollCharacter) {
                        window.__app.hud.rerollCharacter();
                    } else {
                        const randomId = Math.random().toString(36).substring(2, 6).toUpperCase();
                        window.location.href = `/?char=Hero_${randomId}`;
                    }
                    return;
                }

                // [M] or [Escape] Main menu (matches Godot Main.cs:1692)
                if (e.key === 'm' || e.key === 'M' || e.key === 'Escape') {
                    e.preventDefault();
                    if (window.__app && window.__app.hud) {
                        window.__app.hud.hideDeathModal();
                    }
                    window.location.href = '/';
                    return;
                }
                if (e.key === 'Escape') {
                    e.preventDefault();
                    if (window.__app && window.__app.hud) {
                        window.__app.hud.hideDeathModal();
                    }
                    window.location.href = '/';
                    return;
                }

                // On Tab 0 (Tombstone & Menus), forward all navigation, arrow keys, and prompt answers (y, n, Enter, Space) to terminal
                if (activeTab === 0) {
                    this.handleTerminalKey(e);
                    return;
                }

                // On Tabs 1-4 (Equipment, Inventory, Quiver, Stats), handle scrolling (matches Godot Main.cs:1731, 1743, 1755, 1767)
                if (['ArrowUp', 'ArrowDown', 'PageUp', 'PageDown'].includes(e.key)) {
                    e.preventDefault();
                    const activeContent = document.querySelector('.death-tab-content.active');
                    if (activeContent) {
                        const delta = (e.key === 'ArrowUp' ? -40 : e.key === 'ArrowDown' ? 40 : e.key === 'PageUp' ? -200 : 200);
                        activeContent.scrollTop += delta;
                    }
                    return;
                }

                // In other tabs, consume unhandled keys so they don't leak into world
                return;
            }

            // Any manual key cancels automated quick birth
            if (window.__app && window.__app.cancelQuickBirth) {
                window.__app.cancelQuickBirth();
            }

            // Tab toggles 3D Dungeon vs CRT Terminal view (when not in death screen)
            if (e.key === 'Tab') {
                e.preventDefault();
                if (this.toggleTerminalView) this.toggleTerminalView();
                return;
            }

            // Escape: reliably cancel prompts, dismiss menus/overlays, and return from forced terminal view
            if (e.key === 'Escape') {
                e.preventDefault();
                if (this.audio) this.audio.playMenuNav();
                this.network.sendKey('escape');
                if (window.__app && window.__app.isForceTerminal && window.__app.isForceTerminal()) {
                    if (this.toggleTerminalView) this.toggleTerminalView();
                }
                return;
            }

            // In Terminal Mode (Character creation, birth choices, help, shops)
            if (this.inTerminal) {
                this.handleTerminalKey(e);
                return;
            }

            // In 3D World Exploration Mode
            this.handleWorldKey(e);
        });
    }

    handleTerminalKey(e) {
        let keySpec = null;

        if (e.key === 'ArrowUp') keySpec = 'up';
        else if (e.key === 'ArrowDown') keySpec = 'down';
        else if (e.key === 'ArrowLeft') keySpec = 'left';
        else if (e.key === 'ArrowRight') keySpec = 'right';
        else if (e.key === 'PageUp') keySpec = 'pageup';
        else if (e.key === 'PageDown') keySpec = 'pagedown';
        else if (e.key === 'Home') keySpec = 'home';
        else if (e.key === 'End') keySpec = 'end';
        else if (e.key === 'Delete') keySpec = 'delete';
        else if (e.key === 'Enter') keySpec = 'enter';
        else if (e.key === 'Escape') keySpec = 'escape';
        else if (e.key === 'Backspace') keySpec = 'backspace';
        else if (e.key === 'Tab') keySpec = 'tab';
        else if (e.key === ' ') keySpec = 'space';
        else if (e.key.length === 1) keySpec = e.key;

        if (keySpec) {
            e.preventDefault();
            this.network.sendKey(keySpec);
        }
    }

    handleWorldKey(e) {
        // Minimap controls: Screen size ([ / ]) and Grid scale zoom (PgUp / PgDn / + / -)
        // Checked BEFORE movement keys so PgUp/PgDn are never captured as numpad moves
        if (e.key === '[') {
            e.preventDefault();
            if (window.__app && window.__app.hud) {
                window.__app.hud.cycleMinimapSize(-1);
            }
            return;
        }

        if (e.key === ']') {
            e.preventDefault();
            if (window.__app && window.__app.hud) {
                window.__app.hud.cycleMinimapSize(1);
            }
            return;
        }

        // Head Tilt Up (PageUp) & Tilt Down (PageDown) & Recenter (Home)
        if (e.key === 'PageUp') {
            e.preventDefault();
            if (this.dungeon) {
                this.dungeon.userPitchOffset = Math.min(0.48, (this.dungeon.userPitchOffset || 0) + 0.08);
            }
            return;
        }

        if (e.key === 'PageDown') {
            e.preventDefault();
            if (this.dungeon) {
                this.dungeon.userPitchOffset = Math.max(-0.48, (this.dungeon.userPitchOffset || 0) - 0.08);
            }
            return;
        }

        if (e.key === 'Home') {
            e.preventDefault();
            if (this.dungeon) {
                this.dungeon.userPitchOffset = 0.0;
            }
            return;
        }

        if (e.key === '+' || e.key === '=') {
            e.preventDefault();
            if (window.__app && window.__app.hud) {
                window.__app.hud.adjustMinimapZoom(0.15);
            }
            return;
        }

        if (e.key === '-' || e.key === '_') {
            e.preventDefault();
            if (window.__app && window.__app.hud) {
                window.__app.hud.adjustMinimapZoom(-0.15);
            }
            return;
        }

        // Turning is instant camera yaw (0 engine turns)
        if (e.key === 'ArrowLeft') {
            e.preventDefault();
            this.dungeon.turn(-1);
            return;
        }

        if (e.key === 'ArrowRight') {
            e.preventDefault();
            this.dungeon.turn(1);
            return;
        }

        // Forward / Backward in current camera facing direction
        if (e.key === 'ArrowUp') {
            e.preventDefault();
            const moveKey = this.getRelativeDirectionKey(8);
            if (moveKey) this.network.sendKey(moveKey);
            return;
        }

        if (e.key === 'ArrowDown') {
            e.preventDefault();
            const moveKey = this.getRelativeDirectionKey(2);
            if (moveKey) this.network.sendKey(moveKey);
            return;
        }

        // Relative directional movement via number pad (or top-row numbers).
        // 8 = forward, 2 = back, 4 = strafe left, 6 = strafe right, diagonals 7,9,1,3, 5 = stay.
        const numpadDirMap = {
            'Numpad8': 8, 'Digit8': 8,
            'Numpad2': 2, 'Digit2': 2,
            'Numpad4': 4, 'Digit4': 4,
            'Numpad6': 6, 'Digit6': 6,
            'Numpad7': 7, 'Digit7': 7,
            'Numpad9': 9, 'Digit9': 9,
            'Numpad1': 1, 'Digit1': 1,
            'Numpad3': 3, 'Digit3': 3,
            'Numpad5': 5, 'Digit5': 5
        };

        if (numpadDirMap[e.code]) {
            e.preventDefault();
            const dir = numpadDirMap[e.code];
            const moveKey = this.getRelativeDirectionKey(dir);
            if (moveKey) {
                this.network.sendKey(moveKey);
            }
            return;
        }

        if (e.key === '.') {
            e.preventDefault();
            this.network.sendKey('5'); // Rest 1 turn
            return;
        }

        // Attack swing on Space / Enter
        if (e.key === ' ' || e.key === 'Enter') {
            e.preventDefault();
            this.dungeon.triggerAttackAnimation();
            if (this.audio) this.audio.playWhoosh();
            this.network.sendKey('enter');
            return;
        }

        // Escape
        if (e.key === 'Escape') {
            e.preventDefault();
            this.network.sendKey('escape');
            return;
        }

        // General keys (e.g. 'i' for inventory, 'm' for cast spell, 'd' for drop, 'g' for pickup, 'q' for quaff, 'r' for read)
        if (e.key.length === 1 && !e.ctrlKey && !e.altKey && !e.metaKey) {
            e.preventDefault();
            if (this.audio) {
                const k = e.key;
                const kl = k.toLowerCase();
                if (kl === 'm' || kl === 'b' || kl === 'p') this.audio.playSpell();
                else if (kl === 'q') this.audio.playQuaff();
                else if (kl === 'r') this.audio.playScroll();
                else if (kl === 'g' || kl === ',') this.audio.playItemPickup();
                else if (kl === 'd') this.audio.playItemDrop();
                else if (k === 'E') this.audio.playEat();
                else if (k === 'w') this.audio.playEquipWeapon();
                else if (k === 'W') this.audio.playEquipArmor();
                else if (k === 't') this.audio.playItemDrop();
                else if (kl === 'o') this.audio.playDoor(true);
                else if (kl === 'c') this.audio.playDoor(false);
                else if (kl === 'f' || kl === 'v') this.audio.playBowShoot();
                else if (k === 'D') this.audio.playTrapDisarm();
                else if (kl === 'i' || kl === 'e') this.audio.playMenuOpen();
            }
            this.network.sendKey(e.key);
        }
    }

    /**
     * Angband movement key for a local direction (numpad 1-9) relative to current camera facing.
     * 1:1 mathematical parity with Godot client DungeonWorld.RelativeMoveKey.
     * 8 = Forward, 2 = Backward, 4 = Strafe Left, 6 = Strafe Right,
     * 7 = Forward-Left, 9 = Forward-Right, 1 = Backward-Left, 3 = Backward-Right, 5 = Stay.
     */
    getRelativeDirectionKey(numpadDir) {
        if (numpadDir === 5) return '5';
        const localIndexMap = {
            8: 0,
            9: 1,
            6: 2,
            3: 3,
            2: 4,
            1: 5,
            4: 6,
            7: 7
        };
        const localIndex = localIndexMap[numpadDir];
        if (localIndex === undefined) return null;
        // Cardinal and diagonal Angband movement keys (1:1 with Godot DungeonWorld.cs)
        const dirKeys = ['up', 'pageup', 'right', 'pagedown', 'down', 'end', 'left', 'home'];
        const facing = (this.dungeon && typeof this.dungeon.facing === 'number') ? this.dungeon.facing : 0;
        const targetWorldSector = (localIndex + facing * 2) % 8;
        return dirKeys[targetWorldSector];
    }

    setupActionButtons() {
        const bind = (id, key) => {
            const btn = document.getElementById(id);
            if (btn) {
                btn.addEventListener('click', () => {
                    if (this.audio) this.audio.unlock();
                    if (key === 'attack') {
                        this.dungeon.triggerAttackAnimation();
                        if (this.audio) this.audio.playWhoosh();
                        this.network.sendKey('enter');
                    } else if (key === 'tab') {
                        if (this.toggleTerminalView) this.toggleTerminalView();
                    } else {
                        if (this.audio) {
                            if (key === 'm') this.audio.playSpell();
                            else if (key === 'i' || key === 'e') this.audio.playMenuOpen();
                            else if (key === 'g') this.audio.playItemPickup();
                        }
                        this.network.sendKey(key);
                    }
                });
            }
        };

        bind('btn-attack', 'attack');
        bind('btn-cast', 'm');
        bind('btn-rest', 'R');
        bind('btn-inventory', 'i');
        bind('btn-equipment', 'e');
        bind('btn-pickup', 'g');
        bind('btn-terminal', 'tab');

        const fsBtn = document.getElementById('btn-fullscreen');
        if (fsBtn) fsBtn.addEventListener('click', () => this.toggleFullscreen());

        const soundBtn = document.getElementById('btn-sound');
        if (soundBtn) {
            soundBtn.addEventListener('click', () => {
                if (this.audio) {
                    this.audio.enabled = !this.audio.enabled;
                    soundBtn.textContent = this.audio.enabled ? '🔊 Sound' : '🔇 Muted';
                }
            });
        }
    }

    setupTouchEvents() {
        const bindTouch = (id, action) => {
            const btn = document.getElementById(id);
            if (!btn) return;
            const trigger = (e) => {
                e.preventDefault();
                if (this.audio) this.audio.unlock();
                action();
            };
            btn.addEventListener('touchstart', trigger);
            btn.addEventListener('mousedown', trigger);
        };

        bindTouch('dpad-up', () => {
            const key = this.getRelativeDirectionKey(8);
            if (key) this.network.sendKey(key);
        });

        bindTouch('dpad-down', () => {
            const key = this.getRelativeDirectionKey(2);
            if (key) this.network.sendKey(key);
        });

        bindTouch('dpad-left', () => {
            this.dungeon.turn(-1);
        });

        bindTouch('dpad-right', () => {
            this.dungeon.turn(1);
        });

        bindTouch('dpad-center', () => {
            this.dungeon.triggerAttackAnimation();
            if (this.audio) this.audio.playWhoosh();
            this.network.sendKey('enter');
        });
    }

    toggleFullscreen() {
        if (!document.fullscreenElement) {
            document.documentElement.requestFullscreen().catch(() => {});
        } else {
            document.exitFullscreen().catch(() => {});
        }
    }
}

window.InputController = InputController;
