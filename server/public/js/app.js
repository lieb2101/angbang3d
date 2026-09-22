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

    let birthReviewActive = false;

    const toggleTerminalView = () => {
        forceTerminal = !forceTerminal;
        window.__manualTerminalOpen = forceTerminal;
        updateViewMode();
    };

    let quickBirthActive = false;
    let quickBirthStep = 0;

    const cancelQuickBirth = () => {
        quickBirthActive = false;
        quickBirthStep = 0;
    };

    const confirmHeroBirth = () => {
        cancelQuickBirth();
        birthReviewActive = false;
        forceTerminal = false;
        window.__manualTerminalOpen = false;
        if (terminalContainer) terminalContainer.classList.add('hidden');
        if (input) input.setTerminalMode(false);
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

        // 1. Character confirmation / review screens ('use as is' or 'S' to start over)
        // PAUSE HERE: Give the player a moment to inspect stats and choose whether to accept or reroll!
        if (screenText.includes("use as is") || screenText.includes("'y': use") ||
            screenText.includes("to start over") || screenText.includes("r to reroll") ||
            screenText.includes("reroll") || screenText.includes("'s' to start")) {
            quickBirthActive = false;
            birthReviewActive = true;
            forceTerminal = true;
            updateTerminalToolbar(frame);
            updateViewMode(frame);
            console.log('[QuickStart] Reached character review screen; pausing for player inspection.');
            return;
        }

        // 2. More prompts during setup/selection
        if (hasMore) {
            network.sendKey('enter');
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
        // 5. Selection menu: trait, race, class, roller -> send '@' for random
        network.sendKey('@');
    }

    const rerollHero = () => {
        cancelQuickBirth();
        if (audio) audio.playMenuNav();
        network.sendKey('s');
        quickBirthActive = true;
        quickBirthStep = 0;
    };

    const startCustomHeroCreation = () => {
        cancelQuickBirth();
        if (audio) audio.playMenuNav();
        network.sendKey('s');
        quickBirthActive = false;
        quickBirthStep = 0;
    };

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

    const termRerollBtn = document.getElementById('btn-term-reroll');
    if (termRerollBtn) {
        termRerollBtn.addEventListener('click', () => {
            if (audio) audio.unlock();
            rerollHero();
        });
    }

    const termCustomBtn = document.getElementById('btn-term-custom');
    if (termCustomBtn) {
        termCustomBtn.addEventListener('click', () => {
            if (audio) audio.unlock();
            startCustomHeroCreation();
        });
    }

    const termAdvanceBtn = document.getElementById('btn-term-advance');
    if (termAdvanceBtn) {
        termAdvanceBtn.addEventListener('click', () => {
            if (audio) audio.unlock();
            confirmHeroBirth();
            if (audio) audio.playWhoosh();
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
        cancelQuickBirth,
        confirmHeroBirth,
        rerollHero
    };

    let lastFrame = null;
    let appState = 'splash';
    let guidePreviousState = 'mainMenu';
    let loadPreviousState = 'mainMenu';
    let selectedMenuIndex = 0;
    let selectedPauseIndex = 0;
    let selectedSaveIndex = 0;
    let loadedSaves = [];
    let latestSave = null;

    // DOM Elements for Intro & Modals
    const splashOverlay = document.getElementById('splash-overlay');
    const mainMenuOverlay = document.getElementById('main-menu-overlay');
    const guideModal = document.getElementById('guide-modal');
    const guideTabs = document.querySelectorAll('.guide-tab');
    const guideContents = document.querySelectorAll('.guide-tab-content');
    const menuOptionBtns = document.querySelectorAll('#main-menu-options .menu-option-btn');

    const loadModal = document.getElementById('load-modal');
    const loadSaveList = document.getElementById('load-save-list');
    const btnLoadBack = document.getElementById('btn-load-back');

    const pauseModal = document.getElementById('pause-modal');
    const pauseCharDisplay = document.getElementById('pause-char-display');
    const pauseOptionBtns = document.querySelectorAll('#pause-menu-options .menu-option-btn');

    // State Coordinator Methods
    function getAppState() {
        return appState;
    }

    function showSplash() {
        appState = 'splash';
        if (splashOverlay) splashOverlay.classList.remove('hidden');
        if (mainMenuOverlay) mainMenuOverlay.classList.add('hidden');
        if (guideModal) guideModal.classList.add('hidden');
        if (loadModal) loadModal.classList.add('hidden');
        if (pauseModal) pauseModal.classList.add('hidden');
        if (terminalContainer) terminalContainer.classList.add('hidden');
        if (input) input.setTerminalMode(false);
        if (audio) audio.playMenuNav();
    }

    function showMainMenu() {
        appState = 'mainMenu';
        if (splashOverlay) splashOverlay.classList.add('hidden');
        if (mainMenuOverlay) mainMenuOverlay.classList.remove('hidden');
        if (guideModal) guideModal.classList.add('hidden');
        if (loadModal) loadModal.classList.add('hidden');
        if (pauseModal) pauseModal.classList.add('hidden');
        if (terminalContainer) terminalContainer.classList.add('hidden');
        if (input) input.setTerminalMode(false);
        if (audio) audio.playMenuOpen();
        checkSaves();
        updateMenuSelectionUI();
    }

    // Load Menu Handler
    async function showLoadMenu(fromPause = false) {
        loadPreviousState = fromPause ? 'pauseMenu' : 'mainMenu';
        appState = 'loadMenu';
        if (splashOverlay) splashOverlay.classList.add('hidden');
        if (mainMenuOverlay) mainMenuOverlay.classList.add('hidden');
        if (guideModal) guideModal.classList.add('hidden');
        if (pauseModal) pauseModal.classList.add('hidden');
        if (terminalContainer) terminalContainer.classList.add('hidden');
        if (loadModal) loadModal.classList.remove('hidden');
        if (input) input.setTerminalMode(false);
        if (audio) audio.playMenuOpen();
        selectedSaveIndex = 0;
        await fetchAndRenderSaves();
    }

    function hideLoadMenu() {
        if (loadModal) loadModal.classList.add('hidden');
        if (audio) audio.playMenuNav();
        if (loadPreviousState === 'pauseMenu') {
            showPauseMenu();
        } else {
            showMainMenu();
        }
    }

    async function fetchAndRenderSaves() {
        if (!loadSaveList) return;
        loadSaveList.innerHTML = `
            <div class="save-loading-state">
                <div class="spinner"></div>
                <span>Scanning realm archives...</span>
            </div>
        `;
        try {
            const res = await fetch('/api/saves');
            if (res.ok) {
                const data = await res.json();
                loadedSaves = data.saves || [];
            } else {
                loadedSaves = [];
            }
        } catch (err) {
            console.warn('[LoadMenu] Error fetching saves:', err);
            loadedSaves = [];
        }
        renderSaveListUI();
    }

    function renderSaveListUI() {
        if (!loadSaveList) return;
        loadSaveList.innerHTML = '';

        if (loadedSaves.length === 0) {
            loadSaveList.innerHTML = `
                <div class="save-empty-state">
                    <div class="save-empty-icon">📜</div>
                    <div class="save-empty-title">No Saved Games Found</div>
                    <div class="save-empty-desc">You do not have any saved adventurers yet. Begin a new quest from the Main Menu!</div>
                    <button id="btn-empty-start-scratch" class="btn-gold" style="margin-top: 10px;">⚔ Start from Scratch</button>
                </div>
            `;
            const btnScratch = document.getElementById('btn-empty-start-scratch');
            if (btnScratch) {
                btnScratch.addEventListener('click', () => {
                    hideLoadMenu();
                    startNewRandomHero();
                });
            }
            return;
        }

        if (selectedSaveIndex >= loadedSaves.length) {
            selectedSaveIndex = Math.max(0, loadedSaves.length - 1);
        }

        loadedSaves.forEach((save, idx) => {
            const card = document.createElement('div');
            card.className = `save-item-card ${idx === selectedSaveIndex ? 'selected' : ''}`;
            card.dataset.index = idx;

            const modDate = save.lastModified ? new Date(save.lastModified).toLocaleString() : 'Unknown';
            const sizeKb = save.sizeBytes ? `${Math.round(save.sizeBytes / 1024)} KB` : '';

            card.innerHTML = `
                <div class="save-item-left">
                    <span class="save-bullet">►</span>
                    <div class="save-item-details">
                        <div class="save-item-name">${save.characterName || save.filename}</div>
                        <div class="save-item-summary">${save.description || 'Saved Adventurer'}</div>
                        <div class="save-item-meta">Saved ${modDate} • ${sizeKb}</div>
                    </div>
                </div>
                <div class="save-item-actions">
                    <button class="btn-save-load btn-gold">Load [Enter]</button>
                    <button class="btn-save-delete btn-danger-outline" title="Delete save file">🗑 Delete</button>
                </div>
            `;

            card.addEventListener('click', (e) => {
                if (e.target.closest('.btn-save-delete')) return;
                selectedSaveIndex = idx;
                updateSaveSelectionUI();
                loadSelectedSave(save);
            });

            card.addEventListener('mouseenter', () => {
                selectedSaveIndex = idx;
                updateSaveSelectionUI();
            });

            const btnLoad = card.querySelector('.btn-save-load');
            if (btnLoad) {
                btnLoad.addEventListener('click', (e) => {
                    e.stopPropagation();
                    loadSelectedSave(save);
                });
            }

            const btnDelete = card.querySelector('.btn-save-delete');
            if (btnDelete) {
                btnDelete.addEventListener('click', (e) => {
                    e.stopPropagation();
                    deleteSelectedSave(save);
                });
            }

            loadSaveList.appendChild(card);
        });
    }

    function updateSaveSelectionUI() {
        const cards = loadSaveList ? loadSaveList.querySelectorAll('.save-item-card') : [];
        cards.forEach((card, i) => {
            if (i === selectedSaveIndex) {
                card.classList.add('selected');
                card.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
            } else {
                card.classList.remove('selected');
            }
        });
    }

    function navigateLoadList(delta = 1) {
        if (loadedSaves.length === 0) return;
        selectedSaveIndex = (selectedSaveIndex + delta + loadedSaves.length) % loadedSaves.length;
        if (audio) audio.playMenuNav();
        updateSaveSelectionUI();
    }

    function loadSelectedSave(save = null) {
        const target = save || (loadedSaves.length > 0 ? loadedSaves[selectedSaveIndex] : null);
        if (!target) return;
        if (audio) audio.playMenuSelect();
        if (loadModal) loadModal.classList.add('hidden');
        startGame({
            charName: target.characterName || 'Adventurer',
            saveFile: target.filename,
            isNew: false,
            autoBirth: false
        });
    }

    async function deleteSelectedSave(save = null) {
        const target = save || (loadedSaves.length > 0 ? loadedSaves[selectedSaveIndex] : null);
        if (!target) return;
        const confirmName = target.characterName || target.filename;
        if (!window.confirm(`Are you sure you want to delete '${confirmName}' permanently from the server?`)) {
            return;
        }
        try {
            const res = await fetch(`/api/saves/${encodeURIComponent(target.filename)}`, { method: 'DELETE' });
            if (res.ok) {
                if (audio) audio.playMenuNav();
                await fetchAndRenderSaves();
                checkSaves();
            } else {
                alert(`Failed to delete save: ${res.statusText}`);
            }
        } catch (err) {
            alert(`Error deleting save: ${err.message}`);
        }
    }

    // In-Game Pause Menu Handlers
    function showPauseMenu() {
        appState = 'pauseMenu';
        if (pauseModal) pauseModal.classList.remove('hidden');
        if (mainMenuOverlay) mainMenuOverlay.classList.add('hidden');
        if (splashOverlay) splashOverlay.classList.add('hidden');
        if (loadModal) loadModal.classList.add('hidden');
        if (guideModal) guideModal.classList.add('hidden');
        if (input) input.setTerminalMode(false);

        if (pauseCharDisplay && lastFrame && lastFrame.player) {
            const p = lastFrame.player;
            const charDesc = p.name ? `${p.name} (${p.race || ''} ${p.class || ''})` : 'Hero of Angband';
            pauseCharDisplay.textContent = `Current Character: ${charDesc}`;
        }
        selectedPauseIndex = 0;
        updatePauseMenuSelectionUI();
        if (audio) audio.playMenuOpen();
    }

    function hidePauseMenu() {
        if (pauseModal) pauseModal.classList.add('hidden');
        if (audio) audio.playMenuNav();
    }

    function resumeGame() {
        hidePauseMenu();
        appState = 'game';
        updateViewMode();
    }

    function saveGameNow() {
        network.sendKey('C-s');
        if (audio) audio.playMenuSelect();
        const banner = document.getElementById('message-text');
        if (banner) {
            banner.textContent = 'Game Saved Successfully (Ctrl-S).';
        }
        resumeGame();
    }

    function updatePauseMenuSelectionUI() {
        pauseOptionBtns.forEach((btn, i) => {
            if (i === selectedPauseIndex) {
                btn.classList.add('selected');
            } else {
                btn.classList.remove('selected');
            }
        });
    }

    function navigatePauseMenu(delta = 1) {
        selectedPauseIndex = (selectedPauseIndex + delta + pauseOptionBtns.length) % pauseOptionBtns.length;
        if (audio) audio.playMenuNav();
        updatePauseMenuSelectionUI();
    }

    function selectPauseMenuItem(index) {
        if (index >= 0 && index < pauseOptionBtns.length) {
            selectedPauseIndex = index;
            updatePauseMenuSelectionUI();
        }
    }

    function activatePauseMenuItem() {
        if (audio) audio.playMenuSelect();
        switch (selectedPauseIndex) {
            case 0: // Resume Game
                resumeGame();
                break;
            case 1: // Save Game Now
                saveGameNow();
                break;
            case 2: // Load Other Character...
                showLoadMenu(true);
                break;
            case 3: // Start Over (Random)
                startNewRandomHero();
                break;
            case 4: // Start Over (Custom)
                startNewCustomHero();
                break;
            case 5: // Game Guide & Primer
                showGuide(0, 'pauseMenu');
                break;
            case 6: // Toggle Fullscreen
                if (input) input.toggleFullscreen();
                break;
            case 7: // Save & Quit to Main Menu
                returnToMainMenu();
                break;
        }
    }

    // Guide Modal Handlers
    function showGuide(tabIndex = 0, fromState = null) {
        guidePreviousState = fromState || appState;
        appState = 'guide';
        if (guideModal) guideModal.classList.remove('hidden');
        if (pauseModal) pauseModal.classList.add('hidden');
        if (mainMenuOverlay) mainMenuOverlay.classList.add('hidden');
        if (splashOverlay) splashOverlay.classList.add('hidden');
        if (loadModal) loadModal.classList.add('hidden');
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
        } else if (guidePreviousState === 'pauseMenu') {
            showPauseMenu();
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
            case 1: // Load Saved Game...
                showLoadMenu(false);
                break;
            case 2: // Start from Scratch (Random Hero)
                startNewRandomHero();
                break;
            case 3: // Start from Scratch (Custom Hero)
                startNewCustomHero();
                break;
            case 4: // Game Guide & Primer
                showGuide(0, 'mainMenu');
                break;
            case 5: // Summary & Credits
                showGuide(5, 'mainMenu');
                break;
            case 6: // Angband Online Wiki
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
        if (loadModal) loadModal.classList.add('hidden');
        if (pauseModal) pauseModal.classList.add('hidden');
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
        if (pauseModal) pauseModal.classList.add('hidden');
        if (loadModal) loadModal.classList.add('hidden');

        showMainMenu();
    }

    // Attach DOM event listeners for buttons
    const btnSplashStart = document.getElementById('btn-splash-start');
    if (btnSplashStart) btnSplashStart.addEventListener('click', () => showMainMenu());

    const btnSplashMenu = document.getElementById('btn-splash-menu');
    if (btnSplashMenu) btnSplashMenu.addEventListener('click', () => showMainMenu());

    const btnSplashGuide = document.getElementById('btn-splash-guide');
    if (btnSplashGuide) btnSplashGuide.addEventListener('click', () => showGuide(0, 'splash'));

    const btnSplashCredits = document.getElementById('btn-splash-credits');
    if (btnSplashCredits) btnSplashCredits.addEventListener('click', () => showGuide(5, 'splash'));

    if (splashOverlay) {
        splashOverlay.addEventListener('click', (e) => {
            // Clicking splash card or background advances to main menu
            if (e.target.closest('.splash-shortcut-btn') || e.target.closest('a')) return;
            showMainMenu();
        });
    }

    const btnMenu = document.getElementById('btn-menu');
    if (btnMenu) {
        btnMenu.addEventListener('click', () => {
            if (appState === 'game') {
                showPauseMenu();
            } else {
                returnToMainMenu();
            }
        });
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

    pauseOptionBtns.forEach((btn, idx) => {
        btn.addEventListener('click', () => {
            selectedPauseIndex = idx;
            updatePauseMenuSelectionUI();
            activatePauseMenuItem();
        });
        btn.addEventListener('mouseenter', () => {
            selectedPauseIndex = idx;
            updatePauseMenuSelectionUI();
        });
    });

    if (btnLoadBack) {
        btnLoadBack.addEventListener('click', () => hideLoadMenu());
    }

    // Guide Modal & Controls Toolbar Listeners
    const btnControls = document.getElementById('btn-controls');
    if (btnControls) {
        btnControls.addEventListener('click', () => {
            showGuide(1, appState === 'game' ? 'game' : null);
        });
    }

    const btnGuideHud = document.getElementById('btn-guide-hud');
    if (btnGuideHud) {
        btnGuideHud.addEventListener('click', () => {
            showGuide(2, 'game');
        });
    }

    const btnGuideClose = document.getElementById('btn-guide-close');
    if (btnGuideClose) {
        btnGuideClose.addEventListener('click', () => hideGuide());
    }

    const btnGuideBack = document.getElementById('btn-guide-back');
    if (btnGuideBack) {
        btnGuideBack.addEventListener('click', () => hideGuide());
    }

    guideTabs.forEach((tab, idx) => {
        tab.addEventListener('click', () => switchGuideTab(idx));
    });

    // Expose full coordinator interface to window.__app for input controller
    Object.assign(window.__app, {
        getAppState: () => appState,
        showSplash,
        showMainMenu,
        showLoadMenu,
        hideLoadMenu,
        navigateLoadList,
        loadSelectedSave,
        deleteSelectedSave,
        showPauseMenu,
        resumeGame,
        navigatePauseMenu,
        selectPauseMenuItem,
        activatePauseMenuItem,
        showGuide,
        hideGuide,
        switchGuideTab,
        cycleGuideTab,
        isGuideOpen: () => appState === 'guide' || (guideModal && !guideModal.classList.contains('hidden')),
        startNewRandomHero,
        startNewCustomHero,
        startCustomHeroCreation,
        rerollHero,
        returnToMainMenu,
        navigateMenu,
        selectMenuItem,
        activateMenuItem
    });

    function updateTerminalToolbar(frame) {
        const terminalTitle = document.getElementById('terminal-title');
        const quickBirthBtn = document.getElementById('btn-quick-birth');
        const termRerollBtn = document.getElementById('btn-term-reroll');
        const termCustomBtn = document.getElementById('btn-term-custom');
        const termAdvanceBtn = document.getElementById('btn-term-advance');
        const termEscapeBtn = document.getElementById('btn-term-escape');
        if (!terminalTitle || !termEscapeBtn) return;

        const screenText = (frame && frame.term && frame.term.rows)
            ? frame.term.rows.map(r => r.g || '').join('\n').toLowerCase()
            : '';
        const isReviewScreen = screenText.includes("use as is") || screenText.includes("'y': use") ||
                               screenText.includes("to start over") || screenText.includes("r to reroll") ||
                               screenText.includes("'s' to start");

        // Case 1: Final character review screen (Only during setup/birth, NEVER in active play with map)
        if (isReviewScreen && (!frame.map || !frame.player || (frame.phase && frame.phase !== 'play'))) {
            terminalTitle.textContent = '⚔ REVIEW YOUR HERO';
            if (quickBirthBtn) quickBirthBtn.style.display = 'none';
            if (termRerollBtn) termRerollBtn.style.display = 'inline-flex';
            if (termCustomBtn) termCustomBtn.style.display = 'inline-flex';
            if (termAdvanceBtn) {
                termAdvanceBtn.style.display = 'inline-flex';
                termAdvanceBtn.textContent = '⚔ Accept & Play (Enter)';
            }
            termEscapeBtn.textContent = 'Back (Esc)';
            return;
        }

        // Case 2: Character Creation / Birth before player exists or map exists
        if (!frame || !frame.player || !frame.map || (frame.phase && frame.phase !== 'play')) {
            terminalTitle.textContent = '⚔ CHARACTER CREATION';
            if (quickBirthBtn) quickBirthBtn.style.display = 'inline-flex';
            if (termRerollBtn) termRerollBtn.style.display = 'none';
            if (termCustomBtn) termCustomBtn.style.display = 'none';
            if (termAdvanceBtn) {
                termAdvanceBtn.style.display = 'inline-flex';
                termAdvanceBtn.textContent = 'Advance (Enter)';
            }
            termEscapeBtn.textContent = 'Back (Esc)';
            return;
        }

        // Case 3: In active play (player exists) — NEVER show character creation or reroll!
        if (quickBirthBtn) quickBirthBtn.style.display = 'none';
        if (termRerollBtn) termRerollBtn.style.display = 'none';
        if (termCustomBtn) termCustomBtn.style.display = 'none';

        if (frame.ui && (frame.ui.overlay || 0) > 0) {
            // In store or full-screen menu overlay
            let storeName = 'STORE';
            if (frame.term && frame.term.rows) {
                for (let r = 0; r < Math.min(4, frame.term.rows.length); r++) {
                    const line = (frame.term.rows[r].g || '').trim();
                    const m = line.match(/([\w\s]+)\s*\(\d+\)/);
                    if (m) {
                        storeName = m[1].trim();
                        break;
                    } else if (line.toLowerCase().includes('inventory')) {
                        storeName = 'INVENTORY';
                        break;
                    } else if (line.toLowerCase().includes('equipment')) {
                        storeName = 'EQUIPMENT';
                        break;
                    }
                }
            }
            terminalTitle.textContent = '⚔ ' + storeName.toUpperCase();
            if (termAdvanceBtn) {
                termAdvanceBtn.style.display = 'inline-flex';
                termAdvanceBtn.textContent = 'Advance (Enter)';
            }
            termEscapeBtn.textContent = 'Exit Store (Esc)';
        } else {
            terminalTitle.textContent = '⚔ ANGBAND CLASSIC TERMINAL';
            if (termAdvanceBtn) {
                termAdvanceBtn.style.display = 'inline-flex';
                termAdvanceBtn.textContent = 'Advance (Enter)';
            }
            termEscapeBtn.textContent = 'Close (Esc)';
        }
    }

    function needsTerminal(frame) {
        if (!frame) return true;
        // If player does not exist yet, we are in birth / character setup
        if (!frame.player) return true;
        // Non-play phases (panic save prompts, etc.) route to terminal
        if (frame.phase && frame.phase !== 'play') return true;
        // During active play, if the player is dead, the atmospheric death modal handles death presentation
        const isDead = Boolean(frame.player.dead || (frame.player.hp !== undefined && frame.player.hp <= 0 && frame.player.hp_max > 0));
        if (isDead) return false;
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
            terminal.resize();
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
        } else if (frame.phase === 'play' && frame.map && frame.player) {
            // Once in active play with map, ensure character birth review is exited and 3D world is active
            const hasOverlay = frame.ui && (frame.ui.overlay || 0) > 0;
            if (!hasOverlay && (birthReviewActive || (forceTerminal && !window.__manualTerminalOpen))) {
                confirmHeroBirth();
            }
        } else if (frame.ui && frame.ui.more) {
            // Auto-flush -more- prompts seamlessly during play and store entrance
            const screenText = (frame.term && frame.term.rows)
                ? frame.term.rows.map(r => r.g || '').join('\n').toLowerCase()
                : '';
            const isReviewScreen = screenText.includes("use as is") || screenText.includes("'y': use") ||
                                   screenText.includes("to start over") || screenText.includes("r to reroll") ||
                                   screenText.includes("'s' to start");
            if (!isReviewScreen) {
                network.sendKey('space');
            }
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

        // Update Terminal Toolbar & Context
        updateTerminalToolbar(frame);

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
