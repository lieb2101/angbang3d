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

            // Global Sound Mute Toggle (Ctrl+M or Cmd+M) available at any game state or menu
            if ((e.ctrlKey || e.metaKey) && (e.key === 'm' || e.key === 'M')) {
                e.preventDefault();
                const soundBtn = document.getElementById('btn-sound');
                if (soundBtn) {
                    soundBtn.click();
                } else if (this.audio) {
                    this.audio.enabled = !this.audio.enabled;
                }
                return;
            }

            // Toggle Fullscreen (F11 or Alt+Enter)
            if (e.key === 'F11' || (e.key === 'Enter' && e.altKey)) {
                e.preventDefault();
                this.toggleFullscreen();
                return;
            }


            // In Death Screen Modal: support tabs 1-5, [R] reload, [N] reroll, [M / Esc] menu, [u] identify, and full interactive terminal on Tab 0
            const deathModal = document.getElementById('death-modal');
            const isDeadModal = Boolean(deathModal && !deathModal.classList.contains('hidden'));
            const lastFrame = (window.__app && window.__app.lastFrame) ? window.__app.lastFrame : null;
            const isDeadFrame = Boolean(lastFrame && lastFrame.player && (lastFrame.player.dead || (lastFrame.player.hp !== undefined && lastFrame.player.hp <= 0 && lastFrame.player.hp_max > 0)));

            if (isDeadModal || isDeadFrame) {
                if (!isDeadModal && window.__app && window.__app.hud && lastFrame) {
                    window.__app.hud.showDeathModal(lastFrame);
                }
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
                    if (window.__app && window.__app.startNewRandomHero) {
                        window.__app.startNewRandomHero();
                    } else if (window.__app && window.__app.hud && window.__app.hud.rerollCharacter) {
                        window.__app.hud.rerollCharacter();
                    } else {
                        const randomId = Math.random().toString(36).substring(2, 6).toUpperCase();
                        window.location.href = `/?char=Hero_${randomId}`;
                    }
                    return;
                }

                // [M / Esc] Main menu: Escape always returns to main menu; 'M' returns to main menu on non-terminal tabs
                if (e.key === 'Escape' || (activeTab !== 0 && (e.key === 'm' || e.key === 'M'))) {
                    e.preventDefault();
                    if (window.__app && window.__app.returnToMainMenu) {
                        window.__app.returnToMainMenu();
                    } else {
                        window.location.href = '/';
                    }
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

            const appState = (window.__app && typeof window.__app.getAppState === 'function')
                ? window.__app.getAppState()
                : 'game';

            // 1. Splash Screen Mode: Any key continues to Main Menu (matches Godot SplashContinueRequested)
            if (appState === 'splash') {
                if (e.key === '2' || e.key === 'g' || e.key === 'G') {
                    e.preventDefault();
                    if (window.__app) window.__app.showGuide(0, 'splash');
                    return;
                }
                if (e.key === '3' || e.key === 'c' || e.key === 'C') {
                    e.preventDefault();
                    if (window.__app) window.__app.showGuide(5, 'splash');
                    return;
                }
                if (e.key === 'w' || e.key === 'W') {
                    e.preventDefault();
                    window.open('https://angband.readthedocs.io/', '_blank');
                    return;
                }
                // Any key advances from splash screen to main menu
                e.preventDefault();
                if (window.__app) window.__app.showMainMenu();
                return;
            }

            // 2. Main Menu Mode
            if (appState === 'mainMenu') {
                if (e.key === 'Escape') {
                    e.preventDefault();
                    if (window.__app) window.__app.showSplash();
                    return;
                }
                if (e.key === 'ArrowUp' || e.key === 'w' || e.key === 'W') {
                    e.preventDefault();
                    if (window.__app) window.__app.navigateMenu(-1);
                    return;
                }
                if (e.key === 'ArrowDown' || e.key === 's' || e.key === 'S') {
                    e.preventDefault();
                    if (window.__app) window.__app.navigateMenu(1);
                    return;
                }
                if (['1', '2', '3', '4', '5', '6', '7'].includes(e.key)) {
                    e.preventDefault();
                    const idx = parseInt(e.key, 10) - 1;
                    if (window.__app) {
                        window.__app.selectMenuItem(idx);
                        window.__app.activateMenuItem();
                    }
                    return;
                }
                if (e.key === ' ' || e.key === 'Enter') {
                    e.preventDefault();
                    if (window.__app) window.__app.activateMenuItem();
                    return;
                }
                return;
            }

            // 3. Load Saved Game Menu Mode
            if (appState === 'loadMenu') {
                if (e.key === 'Escape') {
                    e.preventDefault();
                    if (window.__app) window.__app.hideLoadMenu();
                    return;
                }
                if (e.key === 'ArrowUp' || e.key === 'w' || e.key === 'W') {
                    e.preventDefault();
                    if (window.__app) window.__app.navigateLoadList(-1);
                    return;
                }
                if (e.key === 'ArrowDown' || e.key === 's' || e.key === 'S') {
                    e.preventDefault();
                    if (window.__app) window.__app.navigateLoadList(1);
                    return;
                }
                if (e.key === ' ' || e.key === 'Enter') {
                    e.preventDefault();
                    if (window.__app) window.__app.loadSelectedSave();
                    return;
                }
                if (e.key === 'Delete' || e.key === 'd' || e.key === 'D') {
                    e.preventDefault();
                    if (window.__app) window.__app.deleteSelectedSave();
                    return;
                }
                return;
            }

            // 4. In-Game Pause Menu Mode (Game Menu)
            if (appState === 'pauseMenu') {
                if (e.key === 'Escape') {
                    e.preventDefault();
                    if (window.__app) window.__app.resumeGame();
                    return;
                }
                if (e.key === 'ArrowUp' || e.key === 'w' || e.key === 'W') {
                    e.preventDefault();
                    if (window.__app) window.__app.navigatePauseMenu(-1);
                    return;
                }
                if (e.key === 'ArrowDown' || e.key === 's' || e.key === 'S') {
                    e.preventDefault();
                    if (window.__app) window.__app.navigatePauseMenu(1);
                    return;
                }
                if (['1', '2', '3', '4', '5', '6', '7', '8'].includes(e.key)) {
                    e.preventDefault();
                    const idx = parseInt(e.key, 10) - 1;
                    if (window.__app) {
                        window.__app.selectPauseMenuItem(idx);
                        window.__app.activatePauseMenuItem();
                    }
                    return;
                }
                if (e.key === ' ' || e.key === 'Enter') {
                    e.preventDefault();
                    if (window.__app) window.__app.activatePauseMenuItem();
                    return;
                }
                return;
            }

            // 5. Game Guide & Primer Mode
            if (appState === 'guide') {
                if (e.key === 'Escape' || e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    if (window.__app) window.__app.hideGuide();
                    return;
                }
                if (['1', '2', '3', '4', '5', '6'].includes(e.key)) {
                    e.preventDefault();
                    const idx = parseInt(e.key, 10) - 1;
                    if (window.__app) window.__app.switchGuideTab(idx);
                    return;
                }
                if (e.key === 'Tab' || e.key === 'ArrowRight') {
                    e.preventDefault();
                    if (window.__app) window.__app.cycleGuideTab(1);
                    return;
                }
                if (e.key === 'ArrowLeft') {
                    e.preventDefault();
                    if (window.__app) window.__app.cycleGuideTab(-1);
                    return;
                }
                if (e.key === 'w' || e.key === 'W') {
                    e.preventDefault();
                    window.open('https://angband.readthedocs.io/', '_blank');
                    return;
                }
                if (['ArrowUp', 'ArrowDown', 'PageUp', 'PageDown'].includes(e.key)) {
                    e.preventDefault();
                    const guideBody = document.getElementById('guide-body');
                    if (guideBody) {
                        const delta = (e.key === 'ArrowUp' ? -40 : e.key === 'ArrowDown' ? 40 : e.key === 'PageUp' ? -200 : 200);
                        guideBody.scrollTop += delta;
                    }
                    return;
                }
                return;
            }

            // Any manual key cancels automated quick birth
            if (window.__app && window.__app.cancelQuickBirth) {
                window.__app.cancelQuickBirth();
            }

            // Guide Modal Active: handle tab navigation and close
            if (window.__app && window.__app.isGuideOpen && window.__app.isGuideOpen()) {
                if (e.key === 'Escape' || e.key === 'Enter') {
                    e.preventDefault();
                    if (this.audio) this.audio.playMenuNav();
                    window.__app.hideGuide();
                    return;
                }
                if (e.key >= '1' && e.key <= '6') {
                    e.preventDefault();
                    window.__app.switchGuideTab(parseInt(e.key, 10) - 1);
                    return;
                }
                if (e.key === 'Tab' || e.key === 'ArrowRight') {
                    e.preventDefault();
                    window.__app.cycleGuideTab(1);
                    return;
                }
                if (e.key === 'ArrowLeft') {
                    e.preventDefault();
                    window.__app.cycleGuideTab(-1);
                    return;
                }
            }

            // Quick toggle between 3D world and Classic CRT Terminal
            if (e.key === 'Tab' && !e.ctrlKey && !e.altKey && !e.metaKey) {
                e.preventDefault();
                if (this.audio) this.audio.playMenuNav();
                if (this.toggleTerminalView) this.toggleTerminalView();
                return;
            }

            // Escape: reliably cancel prompts, dismiss menus/overlays, or open Pause Menu (1:1 with Godot Main.cs:1820-1835)
            if (e.key === 'Escape') {
                e.preventDefault();
                if (this.audio) this.audio.playMenuNav();

                if (window.__app && window.__app.cancelQuickBirth) {
                    window.__app.cancelQuickBirth();
                }

                const lastFrame = (window.__app && window.__app.lastFrame) ? window.__app.lastFrame : null;
                const ui = lastFrame ? lastFrame.ui : null;
                const isForcedTerm = window.__app && window.__app.isForceTerminal && window.__app.isForceTerminal();
                const inContextMenu = !lastFrame || (lastFrame.phase !== 'play') || (ui && (ui.overlay > 0 || ui.more || !ui.awaiting_command)) || this.inTerminal || isForcedTerm;

                if (inContextMenu) {
                    // Always forward Escape to the engine to exit store/menu/prompt
                    this.network.sendKey('escape');
                    if (window.__app && window.__app.setForceTerminal) {
                        window.__app.setForceTerminal(false);
                    }
                } else {
                    // In free 3D exploration with no sub-menus open, Escape opens Game Menu
                    if (window.__app && window.__app.showPauseMenu) {
                        window.__app.showPauseMenu();
                    }
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
        const lastFrame = (window.__app && window.__app.lastFrame) ? window.__app.lastFrame : null;
        const inPlay = Boolean(lastFrame && lastFrame.player && (lastFrame.player.name || lastFrame.player.hp_max > 0));
        const screenText = (lastFrame && lastFrame.term && lastFrame.term.rows)
            ? lastFrame.term.rows.map(r => r.g || '').join('\n').toLowerCase()
            : '';
        const isReviewScreen = !inPlay && (screenText.includes("use as is") || screenText.includes("'y': use") ||
                               screenText.includes("to start over") || screenText.includes("r to reroll") ||
                               screenText.includes("reroll") || screenText.includes("'s' to start"));

        // Reroll hotkey on review screen ('R') -> Re-randomize
        if (isReviewScreen && (e.key === 'r' || e.key === 'R')) {
            e.preventDefault();
            if (this.audio) this.audio.playMenuNav();
            if (window.__app && window.__app.rerollHero) {
                window.__app.rerollHero();
            } else {
                this.network.sendKey('s');
            }
            return;
        }

        // Custom creation hotkey on review screen ('C' or 'S') -> Start over without autoBirth to choose race/class
        if (isReviewScreen && (e.key === 'c' || e.key === 'C' || e.key === 's' || e.key === 'S')) {
            e.preventDefault();
            if (this.audio) this.audio.playMenuNav();
            if (window.__app && window.__app.startCustomHeroCreation) {
                window.__app.startCustomHeroCreation();
            } else {
                if (window.__app && window.__app.cancelQuickBirth) window.__app.cancelQuickBirth();
                this.network.sendKey('s');
            }
            return;
        }

        // Confirmation on review screen (Enter, Space, or 'y') — jump straight into 3D!
        if (isReviewScreen && (e.key === 'Enter' || e.key === 'y' || e.key === 'Y' || e.key === ' ')) {
            e.preventDefault();
            if (this.audio) this.audio.playWhoosh();
            this.network.sendKey(e.key === 'y' || e.key === 'Y' ? 'y' : 'enter');
            if (window.__app && window.__app.confirmHeroBirth) {
                window.__app.confirmHeroBirth();
            }
            return;
        }

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

        // Complete Controls & Commands Guide (?)
        if (e.key === '?' || (e.key === '/' && e.shiftKey)) {
            e.preventDefault();
            if (window.__app && window.__app.showGuide) {
                window.__app.showGuide(1, 'game');
            }
            return;
        }

        const lastFrame = (window.__app && window.__app.lastFrame) ? window.__app.lastFrame : null;
        const ui = lastFrame ? lastFrame.ui : null;

        // Context prompt (-more-) active: immediately dismiss with space (1:1 with Angband msg_flush)
        // and if a move or turn key was pressed, execute it so the character never gets stuck on stairs!
        if (ui && ui.more) {
            e.preventDefault();
            this.network.sendKey('space');

            if (e.key === 'ArrowLeft' || e.key === 'a' || e.key === 'A') {
                this.dungeon.turn(-1);
            } else if (e.key === 'ArrowRight' || e.key === 'd' || e.key === 'D') {
                this.dungeon.turn(1);
            } else if (e.key === 'ArrowUp' || e.key === 'w' || e.key === 'W' || e.code === 'Numpad8' || e.code === 'Digit8') {
                const moveKey = this.getRelativeDirectionKey(8);
                if (moveKey) setTimeout(() => this.network.sendKey(moveKey), 35);
            } else if (e.key === 'ArrowDown' || e.key === 's' || e.key === 'S' || e.code === 'Numpad2' || e.code === 'Digit2') {
                const moveKey = this.getRelativeDirectionKey(2);
                if (moveKey) setTimeout(() => this.network.sendKey(moveKey), 35);
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
        if (e.key === ' ' || e.code === 'Space' || e.key === 'Enter') {
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
                btn.addEventListener('click', (ev) => {
                    if (ev && ev.target && typeof ev.target.blur === 'function') ev.target.blur();
                    if (this.audio) this.audio.unlock();
                    if (key === 'attack') {
                        const lastFrame = (window.__app && window.__app.lastFrame) ? window.__app.lastFrame : null;
                        if (lastFrame && lastFrame.ui && lastFrame.ui.more) {
                            this.network.sendKey('space');
                            return;
                        }
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
        bind('btn-fire', 'f');
        bind('btn-cast', 'm');
        bind('btn-quaff', 'q');
        bind('btn-read', 'r');
        bind('btn-door', 'o');
        bind('btn-pickup', 'g');
        bind('btn-rest', 'R');
        bind('btn-inventory', 'i');
        bind('btn-equipment', 'e');
        bind('btn-terminal', 'tab');

        // Dynamic Staircase Action Button
        const stairBtn = document.getElementById('btn-stair');
        if (stairBtn) {
            stairBtn.addEventListener('click', (ev) => {
                if (ev && ev.target && typeof ev.target.blur === 'function') ev.target.blur();
                if (this.audio) this.audio.unlock();
                const key = stairBtn.dataset.key || '>';
                this.network.sendKey(key);
            });
        }

        // HUD Help / Controls Sheet Buttons
        const guideHudBtn = document.getElementById('btn-guide-hud');
        if (guideHudBtn) {
            guideHudBtn.addEventListener('click', (ev) => {
                if (ev && ev.target && typeof ev.target.blur === 'function') ev.target.blur();
                if (window.__app && window.__app.showGuide) {
                    window.__app.showGuide(1, 'game');
                }
            });
        }

        const quickHelpBtn = document.getElementById('btn-quick-help');
        if (quickHelpBtn) {
            quickHelpBtn.addEventListener('click', (ev) => {
                if (ev && ev.target && typeof ev.target.blur === 'function') ev.target.blur();
                if (window.__app && window.__app.showGuide) {
                    window.__app.showGuide(1, 'game');
                }
            });
        }

        // Minimap Explicit Size & Zoom Control Buttons
        const bindClick = (id, fn) => {
            const btn = document.getElementById(id);
            if (btn) {
                btn.addEventListener('click', (ev) => {
                    if (ev && ev.target && typeof ev.target.blur === 'function') ev.target.blur();
                    if (this.audio) this.audio.unlock();
                    fn();
                });
            }
        };

        bindClick('btn-map-size-dec', () => {
            if (window.__app && window.__app.hud) window.__app.hud.cycleMinimapSize(-1);
        });
        bindClick('btn-map-size-inc', () => {
            if (window.__app && window.__app.hud) window.__app.hud.cycleMinimapSize(1);
        });
        bindClick('btn-map-zoom-out', () => {
            if (window.__app && window.__app.hud) window.__app.hud.adjustMinimapZoom(-0.25);
        });
        bindClick('btn-map-zoom-in', () => {
            if (window.__app && window.__app.hud) window.__app.hud.adjustMinimapZoom(0.25);
        });

        // Camera Head Tilt Buttons
        bindClick('btn-tilt-up', () => {
            if (this.dungeon) {
                this.dungeon.userPitchOffset = Math.min(0.48, (this.dungeon.userPitchOffset || 0) + 0.08);
            }
        });
        bindClick('btn-tilt-reset', () => {
            if (this.dungeon) {
                this.dungeon.userPitchOffset = 0.0;
            }
        });
        bindClick('btn-tilt-down', () => {
            if (this.dungeon) {
                this.dungeon.userPitchOffset = Math.max(-0.48, (this.dungeon.userPitchOffset || 0) - 0.08);
            }
        });

        // Dismiss -more- prompt on direct click of the prompt bar
        const promptBar = document.getElementById('prompt-bar');
        if (promptBar) {
            promptBar.style.cursor = 'pointer';
            promptBar.addEventListener('click', () => {
                this.network.sendKey('space');
            });
        }

        // Clicking anywhere in world exploration while -more- prompt is up dismisses prompt
        window.addEventListener('click', (e) => {
            const lastFrame = (window.__app && window.__app.lastFrame) ? window.__app.lastFrame : null;
            if (lastFrame && lastFrame.ui && lastFrame.ui.more) {
                const inModal = e.target.closest('#pause-modal, #load-modal, #guide-modal, #death-modal');
                if (!inModal) {
                    this.network.sendKey('space');
                }
            }
        });

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
            const lastFrame = (window.__app && window.__app.lastFrame) ? window.__app.lastFrame : null;
            if (lastFrame && lastFrame.ui && lastFrame.ui.more) {
                this.network.sendKey('space');
                return;
            }
            this.dungeon.triggerAttackAnimation();
            if (this.audio) this.audio.playWhoosh();
            this.network.sendKey('enter');
        });

        // Diagonal Touch D-Pad buttons
        bindTouch('dpad-ul', () => {
            const key = this.getRelativeDirectionKey(7);
            if (key) this.network.sendKey(key);
        });

        bindTouch('dpad-ur', () => {
            const key = this.getRelativeDirectionKey(9);
            if (key) this.network.sendKey(key);
        });

        bindTouch('dpad-dl', () => {
            const key = this.getRelativeDirectionKey(1);
            if (key) this.network.sendKey(key);
        });

        bindTouch('dpad-dr', () => {
            const key = this.getRelativeDirectionKey(3);
            if (key) this.network.sendKey(key);
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
