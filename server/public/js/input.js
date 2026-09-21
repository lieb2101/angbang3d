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
        const facing = this.dungeon.facing; // 0=S, 1=W, 2=N, 3=E

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
            const moveKey = this.getMoveKeyForFacing(facing, true);
            this.network.sendKey(moveKey);
            if (this.audio) this.audio.playFootstep();
            return;
        }

        if (e.key === 'ArrowDown') {
            e.preventDefault();
            const moveKey = this.getMoveKeyForFacing(facing, false);
            this.network.sendKey(moveKey);
            if (this.audio) this.audio.playFootstep();
            return;
        }

        // Numpad Directional Movements & Strafing
        // 8=fwd, 2=back, 4=strafe left, 6=strafe right, 5=rest
        if (e.code === 'Numpad8' || e.code === 'Digit8') {
            e.preventDefault();
            this.network.sendKey(this.getMoveKeyForFacing(facing, true));
            if (this.audio) this.audio.playFootstep();
            return;
        }
        if (e.code === 'Numpad2' || e.code === 'Digit2') {
            e.preventDefault();
            this.network.sendKey(this.getMoveKeyForFacing(facing, false));
            if (this.audio) this.audio.playFootstep();
            return;
        }
        if (e.code === 'Numpad4' || e.code === 'Digit4') {
            e.preventDefault();
            this.network.sendKey(this.getStrafeKeyForFacing(facing, true));
            if (this.audio) this.audio.playFootstep();
            return;
        }
        if (e.code === 'Numpad6' || e.code === 'Digit6') {
            e.preventDefault();
            this.network.sendKey(this.getStrafeKeyForFacing(facing, false));
            if (this.audio) this.audio.playFootstep();
            return;
        }
        if (e.code === 'Numpad5' || e.code === 'Digit5' || e.key === '.') {
            e.preventDefault();
            this.network.sendKey('.'); // Rest 1 turn
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

    getMoveKeyForFacing(facing, forward) {
        // 0=S (down), 1=W (left), 2=N (up), 3=E (right)
        if (forward) {
            const map = ['down', 'left', 'up', 'right'];
            return map[facing];
        } else {
            const map = ['up', 'right', 'down', 'left'];
            return map[facing];
        }
    }

    getStrafeKeyForFacing(facing, strafeLeft) {
        // 0=S: left is East (right), right is West (left)
        // 1=W: left is South (down), right is North (up)
        // 2=N: left is West (left), right is East (right)
        // 3=E: left is North (up), right is South (down)
        if (strafeLeft) {
            const map = ['right', 'down', 'left', 'up'];
            return map[facing];
        } else {
            const map = ['left', 'up', 'right', 'down'];
            return map[facing];
        }
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
            const key = this.getMoveKeyForFacing(this.dungeon.facing, true);
            this.network.sendKey(key);
            if (this.audio) this.audio.playFootstep();
        });

        bindTouch('dpad-down', () => {
            const key = this.getMoveKeyForFacing(this.dungeon.facing, false);
            this.network.sendKey(key);
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
