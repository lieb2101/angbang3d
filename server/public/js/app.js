/**
 * Angband3D Web Application Coordinator
 * Boots all subsystems, orchestrates view routing, and binds network stream to 3D scene.
 */

window.addEventListener('DOMContentLoaded', () => {
    const loadingOverlay = document.getElementById('loading-overlay');
    const terminalContainer = document.getElementById('terminal-container');

    // 1. Initialize Subsystems
    const audio = new SoundEngine();
    const network = new GameNetwork();
    const dungeon = new Dungeon3D('viewport-canvas');
    const hud = new WebHUD('minimap-canvas');

    let forceTerminal = false;
    let currentPhase = 'setup';

    const terminal = new WebTerminal('terminal-canvas', (key) => {
        if (audio) audio.unlock();
        network.sendKey(key);
    });

    const toggleTerminalView = () => {
        forceTerminal = !forceTerminal;
        updateViewMode();
    };

    let quickBirthActive = false;
    let quickBirthStep = 0;

    const cancelQuickBirth = () => {
        quickBirthActive = false;
        quickBirthStep = 0;
    };

    function stepQuickBirth(frame) {
        if (!quickBirthActive || !frame) return;
        if (frame.phase === 'play' && frame.map) {
            quickBirthActive = false;
            return;
        }
        if (++quickBirthStep > 35) {
            quickBirthActive = false;
            console.warn('[QuickStart] Exceeded maximum auto-birth steps; stopping.');
            return;
        }
        const screenText = (frame.term && frame.term.rows ? frame.term.rows.map(r => r.g || '').join('\n') : '').toLowerCase();

        // 1. More prompts
        if (screenText.includes('-more-')) {
            network.sendKey('enter');
            return;
        }
        // 2. Character confirmation / review screen ('y': use as is)
        if (screenText.includes("use as is") || screenText.includes("'y': use")) {
            network.sendKey('Y');
            return;
        }
        // 3. Quit or overwrite confirmation
        if (screenText.includes('[y/n]') || screenText.includes('are you sure')) {
            network.sendKey('y');
            return;
        }
        // 4. Press any key / space pauses
        if (screenText.includes('press any key') || screenText.includes('[press') || screenText.includes('press space')) {
            network.sendKey('enter');
            return;
        }
        // 5. Final birth review screen ('to start over' / 'r to reroll' / 'ESC to quit')
        if (screenText.includes('to start over') || screenText.includes('r to reroll') || screenText.includes('reroll')) {
            network.sendKey('enter');
            return;
        }
        // 6. Selection menu: trait, race, class, roller -> send '@' for random
        network.sendKey('@');
    }

    // Terminal Toolbar action buttons
    const quickBirthBtn = document.getElementById('btn-quick-birth');
    if (quickBirthBtn) {
        quickBirthBtn.addEventListener('click', () => {
            if (audio) audio.unlock();
            cancelQuickBirth();
            quickBirthActive = true;
            quickBirthStep = 0;
            if (lastFrame) {
                stepQuickBirth(lastFrame);
            } else {
                network.sendKey('enter');
            }
        });
    }

    const termAdvanceBtn = document.getElementById('btn-term-advance');
    if (termAdvanceBtn) {
        termAdvanceBtn.addEventListener('click', () => {
            if (audio) audio.unlock();
            cancelQuickBirth();
            network.sendKey('enter');
        });
    }

    const termEscapeBtn = document.getElementById('btn-term-escape');
    if (termEscapeBtn) {
        termEscapeBtn.addEventListener('click', () => {
            if (audio) audio.unlock();
            cancelQuickBirth();
            if (audio) audio.playMenuNav();
            network.sendKey('escape');
            if (forceTerminal) {
                toggleTerminalView();
            }
        });
    }

    dungeon.audio = audio;
    hud.audio = audio;

    const input = new InputController(network, dungeon, terminal, audio, toggleTerminalView);
    window.__app = {
        network,
        dungeon,
        hud,
        terminal,
        input,
        audio,
        isForceTerminal: () => forceTerminal,
        setForceTerminal: (val) => { forceTerminal = val; updateViewMode(); },
        toggleTerminalView,
        cancelQuickBirth
    };

    let lastFrame = null;

    function needsTerminal(frame) {
        if (!frame) return true;
        // Non-play phases (setup, menus, panic save prompts, birth) MUST route to the terminal!
        if ((frame.phase || currentPhase) !== 'play') return true;
        // During active play, if the player is dead, the atmospheric death modal handles death presentation
        if (frame.player && frame.player.dead) return false;
        if (forceTerminal) return true;
        const ui = frame.ui;
        if (!ui) return false;
        if ((ui.overlay || 0) > 0) return true;
        return !ui.awaiting_command && !ui.more;
    }

    function updateViewMode(frame) {
        const needsTerm = needsTerminal(frame || lastFrame);
        if (needsTerm) {
            terminalContainer.classList.remove('hidden');
            input.setTerminalMode(true);
        } else {
            terminalContainer.classList.add('hidden');
            input.setTerminalMode(false);
        }
    }

    // 2. Network Event Handlers
    network.onHello = (msg) => {
        console.log('[Angband3D] Cloud session established:', msg.sessionId);
        if (loadingOverlay) {
            loadingOverlay.classList.add('hidden');
        }
    };

    network.onPing = (ms) => {
        hud.setPing(ms);
    };

    network.onFrame = (frame) => {
        lastFrame = frame;
        if (window.__app) window.__app.lastFrame = frame;
        currentPhase = frame.phase || 'play';

        // Automated quick-birth advancement
        if (quickBirthActive) {
            stepQuickBirth(frame);
        }

        // Check if player died
        if (frame.player && frame.player.dead) {
            if (audio) audio.playDeathBell();
        } else if (frame.player && frame.player.hp !== undefined && frame.player.hp_max) {
            if (frame.player.hp / frame.player.hp_max <= 0.25) {
                if (audio) audio.playLowHpWarning();
            }
        }

        const showTerm = needsTerminal(frame);

        // Render Terminal if active or in setup phase
        if (frame.term && (showTerm || currentPhase !== 'play')) {
            terminal.render(frame.term);
        }

        // Render 3D Dungeon World whenever in play phase with valid map
        if (currentPhase === 'play' && frame.map && frame.player) {
            try {
                dungeon.updateDungeon(frame);
            } catch (err) {
                console.error('[Dungeon3D Error]', err);
            }
        }

        // Update HUD
        hud.update(frame);

        // Update View Mode Visibility
        updateViewMode(frame);
    };

    network.onBye = (detail) => {
        console.warn('[Angband3D] Disconnected:', detail);
        if (hud.setStatus) {
            hud.setStatus('Cloud [Disconnected]');
        } else if (hud.pingBadge) {
            hud.pingBadge.textContent = 'Cloud [Disconnected]';
        }
    };

    network.onStatus = (status) => {
        if (hud.setStatus) {
            hud.setStatus(status.includes('Connecting') ? 'Cloud [Connecting...]' : (status.includes('Connected') ? 'Cloud [Connected]' : status));
        } else if (hud.pingBadge) {
            hud.pingBadge.textContent = status;
        }
    };

    // 3. Connect to cloud engine (support ?char= query param and ?new=1 for clean birth)
    const urlParams = new URLSearchParams(window.location.search);
    const charName = urlParams.get('char') || 'Adventurer';
    const isNew = urlParams.get('new') === '1' || urlParams.get('reroll') === '1';
    network.connect(charName, isNew);
});
