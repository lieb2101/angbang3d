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

    // Terminal Toolbar action buttons
    const quickBirthBtn = document.getElementById('btn-quick-birth');
    if (quickBirthBtn) {
        quickBirthBtn.addEventListener('click', () => {
            if (audio) audio.unlock();
            // Advance past splash screen if needed, trigger quick-roll '@' and confirm
            network.sendKey('enter');
            setTimeout(() => network.sendKey('@'), 120);
            setTimeout(() => network.sendKey('enter'), 300);
        });
    }

    const termAdvanceBtn = document.getElementById('btn-term-advance');
    if (termAdvanceBtn) {
        termAdvanceBtn.addEventListener('click', () => {
            if (audio) audio.unlock();
            network.sendKey('enter');
        });
    }

    const termEscapeBtn = document.getElementById('btn-term-escape');
    if (termEscapeBtn) {
        termEscapeBtn.addEventListener('click', () => {
            if (audio) audio.unlock();
            network.sendKey('escape');
        });
    }

    const input = new InputController(network, dungeon, terminal, audio, toggleTerminalView);

    let lastFrame = null;

    function needsTerminal(frame) {
        if (!frame) return true;
        if (forceTerminal) return true;
        if ((frame.phase || currentPhase) !== 'play') return true;
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
        currentPhase = frame.phase || 'play';

        // Check if player died
        if (frame.player && frame.player.dead) {
            if (audio) audio.playDeathBell();
        }

        const showTerm = needsTerminal(frame);

        // Render Terminal if active or in setup phase
        if (frame.term && (showTerm || currentPhase !== 'play')) {
            terminal.render(frame.term);
        }

        // Render 3D Dungeon World whenever in play phase with valid map
        if (currentPhase === 'play' && frame.map && frame.player) {
            dungeon.updateDungeon(frame);
        }

        // Update HUD
        hud.update(frame);

        // Update View Mode Visibility
        updateViewMode(frame);
    };

    network.onBye = (detail) => {
        console.warn('[Angband3D] Disconnected:', detail);
        if (hud.messageText) {
            hud.messageText.textContent = `Disconnected: ${detail}`;
        }
    };

    network.onStatus = (status) => {
        if (hud.messageText) {
            hud.messageText.textContent = status;
        }
    };

    // 3. Connect to cloud engine
    network.connect('Hero');
});
