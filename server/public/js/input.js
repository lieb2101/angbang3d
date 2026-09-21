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

            // Tab toggles 3D Dungeon vs CRT Terminal view
            if (e.key === 'Tab') {
                e.preventDefault();
                if (this.toggleTerminalView) this.toggleTerminalView();
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
        // Turning is instant camera yaw (0 engine turns)
        if (e.key === 'ArrowLeft') {
            e.preventDefault();
            this.dungeon.turn(-1);
            if (this.audio) this.audio.playFootstep();
            return;
        }

        if (e.key === 'ArrowRight') {
            e.preventDefault();
            this.dungeon.turn(1);
            if (this.audio) this.audio.playFootstep();
            return;
        }

        // Forward / Backward in current camera facing direction
        if (e.key === 'ArrowUp') {
            e.preventDefault();
            const moveKey = this.getRelativeDirectionKey(8);
            if (moveKey) this.network.sendKey(moveKey);
            if (this.audio) this.audio.playFootstep();
            return;
        }

        if (e.key === 'ArrowDown') {
            e.preventDefault();
            const moveKey = this.getRelativeDirectionKey(2);
            if (moveKey) this.network.sendKey(moveKey);
            if (this.audio) this.audio.playFootstep();
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
                if (this.audio) this.audio.playFootstep();
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
            if (this.audio) this.audio.playHit();
            this.network.sendKey('enter');
            return;
        }

        // Escape
        if (e.key === 'Escape') {
            e.preventDefault();
            this.network.sendKey('escape');
            return;
        }

        // General keys (e.g. 'i' for inventory, 'm' for cast spell, 'd' for drop, 'g' for pickup)
        if (e.key.length === 1 && !e.ctrlKey && !e.altKey && !e.metaKey) {
            e.preventDefault();
            if (e.key === 'm' && this.audio) {
                this.audio.playSpell();
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
        // Angband world direction keys: 8=N, 9=NE, 6=E, 3=SE, 2=S, 1=SW, 4=W, 7=NW
        const dirKeys = ['8', '9', '6', '3', '2', '1', '4', '7'];
        const worldIndex = (localIndex + this.dungeon.facing * 2) % 8;
        return dirKeys[worldIndex];
    }

    setupActionButtons() {
        const bind = (id, key) => {
            const el = document.getElementById(id);
            if (el) {
                el.addEventListener('click', () => {
                    if (this.audio) this.audio.unlock();
                    if (key === 'attack') {
                        this.dungeon.triggerAttackAnimation();
                        if (this.audio) this.audio.playHit();
                        this.network.sendKey('enter');
                    } else if (key === 'tab') {
                        if (this.toggleTerminalView) this.toggleTerminalView();
                    } else {
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
            if (this.audio) this.audio.playFootstep();
        });

        bindTouch('dpad-down', () => {
            const key = this.getRelativeDirectionKey(2);
            if (key) this.network.sendKey(key);
            if (this.audio) this.audio.playFootstep();
        });

        bindTouch('dpad-left', () => {
            this.dungeon.turn(-1);
            if (this.audio) this.audio.playFootstep();
        });

        bindTouch('dpad-right', () => {
            this.dungeon.turn(1);
            if (this.audio) this.audio.playFootstep();
        });

        bindTouch('dpad-center', () => {
            this.dungeon.triggerAttackAnimation();
            if (this.audio) this.audio.playHit();
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
