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

        const screenText = (frame.term && frame.term.rows ? frame.term.rows.map(r => r.g || '').join('\n') : '').toLowerCase();
        const hasMore = screenText.includes('-more-') || (frame.ui && frame.ui.more);

        // Once in active play with valid map and awaiting a command with no -more- prompts,
        // birth is 100% finished: dismiss terminal and switch directly to 3D world view!
        if (frame.phase === 'play' && frame.map && !hasMore && frame.ui && frame.ui.awaiting_command) {
            quickBirthActive = false;
            forceTerminal = false;
            updateViewMode(frame);
            return;
        }

        if (++quickBirthStep > 40) {
            quickBirthActive = false;
            forceTerminal = false;
            updateViewMode(frame);
            console.warn('[QuickStart] Exceeded maximum auto-birth steps; switching to world view.');
            return;
        }

        // 1. More prompts (including town arrival -more-)
        if (hasMore) {
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
            // If already playing in world, start a fresh random hero session
            if (currentPhase === 'play') {
                startNewRandomHero();
                return;
            }
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
            forceTerminal = false;
            terminalContainer.classList.add('hidden');
            input.setTerminalMode(false);
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
    let appState = 'splash';
    let guidePreviousState = 'mainMenu';
    let selectedMenuIndex = 0;
    let latestSave = null;

    // DOM Elements for Intro & Modals
    const splashOverlay = document.getElementById('splash-overlay');
    const mainMenuOverlay = document.getElementById('main-menu-overlay');
    const guideModal = document.getElementById('guide-modal');
    const guideTabs = document.querySelectorAll('.guide-tab');
    const guideContents = document.querySelectorAll('.guide-tab-content');
    const menuOptionBtns = document.querySelectorAll('.menu-option-btn');

    // State Coordinator Methods
    function getAppState() {
        return appState;
    }

    function showSplash() {
        appState = 'splash';
        if (splashOverlay) splashOverlay.classList.remove('hidden');
        if (mainMenuOverlay) mainMenuOverlay.classList.add('hidden');
        if (guideModal) guideModal.classList.add('hidden');
        if (terminalContainer) terminalContainer.classList.add('hidden');
        if (input) input.setTerminalMode(false);
        if (audio) audio.playMenuNav();
    }

    function showMainMenu() {
        appState = 'mainMenu';
        if (splashOverlay) splashOverlay.classList.add('hidden');
        if (mainMenuOverlay) mainMenuOverlay.classList.remove('hidden');
        if (guideModal) guideModal.classList.add('hidden');
        if (terminalContainer) terminalContainer.classList.add('hidden');
        if (input) input.setTerminalMode(false);
        if (audio) audio.playMenuOpen();
        checkSaves();
        updateMenuSelectionUI();
    }

    function showGuide(tabIndex = 0) {
        guidePreviousState = appState;
        appState = 'guide';
        if (guideModal) guideModal.classList.remove('hidden');
        if (terminalContainer) terminalContainer.classList.add('hidden');
        if (input) input.setTerminalMode(false);
        switchGuideTab(tabIndex);
        if (audio) audio.playMenuOpen();
    }

    function hideGuide() {
        if (guideModal) guideModal.classList.add('hidden');
        if (audio) audio.playMenuNav();
        if (guidePreviousState === 'splash') {
            showSplash();
        } else if (guidePreviousState === 'game') {
            appState = 'game';
        } else {
            showMainMenu();
        }
    }

    function switchGuideTab(tabIndex) {
        const idx = Math.max(0, Math.min(guideTabs.length - 1, tabIndex));
        guideTabs.forEach((tab, i) => {
            if (i === idx) tab.classList.add('active');
            else tab.classList.remove('active');
        });
        guideContents.forEach((content, i) => {
            if (i === idx) content.classList.add('active');
            else content.classList.remove('active');
        });
        const body = document.getElementById('guide-body');
        if (body) body.scrollTop = 0;
    }

    function cycleGuideTab(delta = 1) {
        let activeIdx = 0;
        guideTabs.forEach((tab, i) => {
            if (tab.classList.contains('active')) activeIdx = i;
        });
        const newIdx = (activeIdx + delta + guideTabs.length) % guideTabs.length;
        if (audio) audio.playMenuNav();
        switchGuideTab(newIdx);
    }

    function updateMenuSelectionUI() {
        menuOptionBtns.forEach((btn, i) => {
            if (i === selectedMenuIndex) {
                btn.classList.add('selected');
            } else {
                btn.classList.remove('selected');
            }
        });
    }

    function navigateMenu(delta = 1) {
        selectedMenuIndex = (selectedMenuIndex + delta + menuOptionBtns.length) % menuOptionBtns.length;
        if (audio) audio.playMenuNav();
        updateMenuSelectionUI();
    }

    function selectMenuItem(index) {
        if (index >= 0 && index < menuOptionBtns.length) {
            selectedMenuIndex = index;
            updateMenuSelectionUI();
        }
    }

    async function checkSaves() {
        try {
            const res = await fetch('/api/saves');
            if (res.ok) {
                const data = await res.json();
                if (data.saves && data.saves.length > 0) {
                    latestSave = data.saves[0];
                    const label = document.getElementById('menu-continue-label');
                    const desc = document.getElementById('menu-continue-desc');
                    const btn = document.getElementById('btn-menu-continue');
                    if (label) label.textContent = `Continue Last Played (${latestSave.characterName || latestSave.filename})`;
                    if (desc) desc.textContent = `${latestSave.description || 'Saved Adventurer'} • Modified ${new Date(latestSave.lastModified).toLocaleTimeString()}`;
                    if (btn) btn.style.opacity = '1.0';
                    return;
                }
            }
        } catch (_) {}
        const desc = document.getElementById('menu-continue-desc');
        if (desc) desc.textContent = 'No saved game found on server (Choose option 2 or 3 to begin)';
        const btn = document.getElementById('btn-menu-continue');
        if (btn) btn.style.opacity = '0.6';
    }

    function activateMenuItem() {
        if (audio) audio.playMenuSelect();
        switch (selectedMenuIndex) {
            case 0: // Continue Last Played
                if (latestSave) {
                    startGame({
                        charName: latestSave.characterName || 'Adventurer',
                        saveFile: latestSave.filename,
                        isNew: false,
                        autoBirth: false
                    });
                } else {
                    // Fallback to random quick-start if no saves exist
                    startNewRandomHero();
                }
                break;
            case 1: // New Character (Random)
                startNewRandomHero();
                break;
            case 2: // New Character (Custom)
                startNewCustomHero();
                break;
            case 3: // Game Guide & Primer
                showGuide(0);
                break;
            case 4: // Summary & Credits
                showGuide(5);
                break;
            case 5: // Angband Wiki
                window.open('https://angband.readthedocs.io/', '_blank');
                break;
        }
    }

    function startNewRandomHero() {
        const randId = Math.random().toString(36).substring(2, 6).toUpperCase();
        const heroName = `Hero_${randId}`;
        startGame({
            charName: heroName,
            isNew: true,
            autoBirth: true
        });
    }

    function startNewCustomHero() {
        const randId = Math.random().toString(36).substring(2, 6).toUpperCase();
        const heroName = `Hero_${randId}`;
        startGame({
            charName: heroName,
            isNew: true,
            autoBirth: false
        });
    }

    function startGame(options) {
        appState = 'game';
        if (splashOverlay) splashOverlay.classList.add('hidden');
        if (mainMenuOverlay) mainMenuOverlay.classList.add('hidden');
        if (guideModal) guideModal.classList.add('hidden');
        if (loadingOverlay) loadingOverlay.classList.remove('hidden');

        quickBirthActive = !!options.autoBirth;
        quickBirthStep = 0;
        forceTerminal = false;

        network.connect(options.charName || 'Adventurer', !!options.isNew, options.saveFile || null);
    }

    function returnToMainMenu() {
        if (audio) audio.playMenuOpen();
        cancelQuickBirth();
        try {
            network.sendCommand('save');
            network.disconnect();
        } catch (_) {}

        if (hud && typeof hud.hideDeathModal === 'function') {
            hud.hideDeathModal();
        }
        if (terminalContainer) terminalContainer.classList.add('hidden');
        if (loadingOverlay) loadingOverlay.classList.add('hidden');

        showMainMenu();
    }

    // Attach DOM event listeners for buttons
    const btnSplashStart = document.getElementById('btn-splash-start');
    if (btnSplashStart) btnSplashStart.addEventListener('click', () => showMainMenu());

    const btnSplashMenu = document.getElementById('btn-splash-menu');
    if (btnSplashMenu) btnSplashMenu.addEventListener('click', () => showMainMenu());

    const btnSplashGuide = document.getElementById('btn-splash-guide');
    if (btnSplashGuide) btnSplashGuide.addEventListener('click', () => showGuide(0));

    const btnSplashCredits = document.getElementById('btn-splash-credits');
    if (btnSplashCredits) btnSplashCredits.addEventListener('click', () => showGuide(5));

    const btnMenu = document.getElementById('btn-menu');
    if (btnMenu) {
        btnMenu.addEventListener('click', () => returnToMainMenu());
    }

    menuOptionBtns.forEach((btn, idx) => {
        btn.addEventListener('click', () => {
            selectedMenuIndex = idx;
            updateMenuSelectionUI();
            activateMenuItem();
        });
        btn.addEventListener('mouseenter', () => {
            selectedMenuIndex = idx;
            updateMenuSelectionUI();
        });
    });

    const btnGuideClose = document.getElementById('btn-guide-close');
    if (btnGuideClose) btnGuideClose.addEventListener('click', () => hideGuide());

    const btnGuideBack = document.getElementById('btn-guide-back');
    if (btnGuideBack) btnGuideBack.addEventListener('click', () => hideGuide());

    guideTabs.forEach((tab, idx) => {
        tab.addEventListener('click', () => switchGuideTab(idx));
    });

    const btnDeathMenu = document.getElementById('btn-death-menu');
    if (btnDeathMenu) {
        btnDeathMenu.addEventListener('click', () => returnToMainMenu());
    }

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
        cancelQuickBirth,
        getAppState,
        showSplash,
        showMainMenu,
        showGuide,
        hideGuide,
        switchGuideTab,
        cycleGuideTab,
        navigateMenu,
        selectMenuItem,
        activateMenuItem,
        returnToMainMenu,
        startNewRandomHero,
        startNewCustomHero
    };

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
        if (appState !== 'game') {
            terminalContainer.classList.add('hidden');
            input.setTerminalMode(false);
            return;
        }
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

    // 3. Initial Boot: Check URL query parameters
    const urlParams = new URLSearchParams(window.location.search);
    if (urlParams.get('play') === '1' || urlParams.get('autoplay') === '1') {
        const charName = urlParams.get('char') || 'Adventurer';
        const isNew = urlParams.get('new') === '1' || urlParams.get('reroll') === '1';
        startGame({ charName, isNew, autoBirth: isNew });
    } else {
        // Show Splash Screen and fetch saves in background
        showSplash();
        checkSaves();
    }
});
