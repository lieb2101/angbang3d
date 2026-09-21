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

    const input = new InputController(network, dungeon, terminal, audio, toggleTerminalView);

    function updateViewMode() {
        // View routing matching Godot NeedsTerminal(frame):
        // If phase !== "play" OR ui.overlay > 0 OR forceTerminal -> show Terminal
        const needsTerm = forceTerminal || (currentPhase !== 'play');
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
        currentPhase = frame.phase || 'play';

        // Check if player died
        if (frame.player && frame.player.dead) {
            if (audio) audio.playDeathBell();
        }

        // Render Terminal if active or in setup phase
        if (frame.term && (currentPhase !== 'play' || forceTerminal || (frame.ui && frame.ui.overlay > 0))) {
            terminal.render(frame.term);
        }

        // Render 3D Dungeon World if in play
        if (currentPhase === 'play' && frame.map) {
            dungeon.updateDungeon(frame);
        }

        // Update HUD
        hud.update(frame);

        // Update View Mode Visibility
        updateViewMode();
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
