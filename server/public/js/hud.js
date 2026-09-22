/**
 * Angband3D HUD — Glassmorphic Fantasy User Interface, 2D Minimap Radar, Compass, and Death System
 * 1:1 Parity with the Godot C# desktop client (Overlay.cs).
 */

class WebHUD {
    constructor(minimapId) {
        this.minimapCanvas = document.getElementById(minimapId);
        this.minimapCtx = this.minimapCanvas.getContext('2d');
        this.minimapContainer = document.getElementById('minimap-container');
        this.minimapHeader = document.getElementById('minimap-header');

        // Minimap sizing and zoom presets (Unified and responsive)
        this.minimapSizes = [
            { cls: 'standard', name: 'Standard', w: 260, h: 260 },
            { cls: 'expanded', name: 'Expanded', w: 360, h: 360 },
            { cls: 'tactical', name: 'Tactical', w: 460, h: 460 },
            { cls: 'compact', name: 'Compact', w: 220, h: 220 }
        ];
        this.minimapSizeIndex = 0;
        this.minimapZoom = 1.0;
        this.currentCameraYaw = 0;
        this.lastFrame = null;
        this.audio = null;

        this.applyMinimapSize();

        // Left Panel (Health, Mana, Attributes)
        this.hpFill = document.getElementById('hp-fill');
        this.hpText = document.getElementById('hp-text');
        this.spFill = document.getElementById('sp-fill');
        this.spText = document.getElementById('sp-text');
        this.charTitle = document.getElementById('char-title');

        this.statStr = document.getElementById('stat-str');
        this.statInt = document.getElementById('stat-int');
        this.statWis = document.getElementById('stat-wis');
        this.statDex = document.getElementById('stat-dex');
        this.statCon = document.getElementById('stat-con');

        this.depthVal = document.getElementById('val-depth');
        this.goldVal = document.getElementById('val-gold');
        this.acVal = document.getElementById('val-ac');
        this.pingBadge = document.getElementById('ping-badge');

        // Compass (1:1 with Godot Overlay.cs:1750-1791)
        this.compassCanvas = document.getElementById('compass-canvas');
        this.compassCtx = this.compassCanvas ? this.compassCanvas.getContext('2d') : null;
        this.compassLabel = document.getElementById('compass-label');

        // Top Landscape Message Feed Window (Full message history with scrollback)
        this.messageFeedWindow = document.getElementById('message-feed-window');
        this.messageFeedList = document.getElementById('message-feed-list');
        this.messageFeedScroll = document.getElementById('message-feed-scroll');
        this.messageHistory = [];
        this.userScrolledUp = false;
        this.prevMessages = null;
        this.lastTermRow0 = '';
        this.currentTurn = 0;
        this.isDeadInPlay = false;

        this.promptBar = document.getElementById('prompt-bar');
        this.promptText = document.getElementById('prompt-text');

        // Detailed 3-Row Telemetry Footer (Matches Overlay.cs:1620-1740)
        this.footerDiagonalHint = document.getElementById('footer-diagonal-hint');
        this.footerStairsHint = document.getElementById('footer-stairs-hint');
        this.footerKeyHints = document.getElementById('footer-key-hints');
        this.footerCharIdentity = document.getElementById('footer-char-identity');
        this.footerLevel = document.getElementById('footer-level');
        this.footerHp = document.getElementById('footer-hp');
        this.footerSp = document.getElementById('footer-sp');
        this.footerAc = document.getElementById('footer-ac');
        this.footerGold = document.getElementById('footer-gold');
        this.footerExp = document.getElementById('footer-exp');
        this.footerPlace = document.getElementById('footer-place');
        this.footerSpeed = document.getElementById('footer-speed');
        this.footerLight = document.getElementById('footer-light');
        this.footerFacing = document.getElementById('footer-facing');

        this.footerTarget = document.getElementById('footer-target');
        this.footerBadges = document.getElementById('footer-badges');
        this.footerGear = document.getElementById('footer-gear');

        // Death Screen Modal Elements (Matches Overlay.cs:840-935)
        this.deathModal = document.getElementById('death-modal');
        this.deathEpitaph = document.getElementById('death-epitaph');
        this.deathCause = document.getElementById('death-cause');
        this.deathScore = document.getElementById('death-score');
        this.deathTabs = document.querySelectorAll('.death-tab');
        this.deathContents = document.querySelectorAll('.death-tab-content');
        this.activeDeathTab = 0;
        this.deathTerminal = null;

        this.setupMinimapControls();
        this.setupDeathModal();
        this.setupMessageFeed();

        if (this.promptBar) {
            this.promptBar.addEventListener('click', () => {
                if (window.__app && window.__app.network) {
                    window.__app.network.sendKey('space');
                }
            });
        }

        // Offscreen canvas cache for static minimap tiles (eliminates hundreds of fillText calls per frame on turn)
        this.minimapTileCanvas = document.createElement('canvas');
        this.minimapTileCtx = this.minimapTileCanvas.getContext('2d');
        this.minimapTileCacheKey = '';
    }

    /* -------------------------------------------------------------
     * Minimap Scale and Zoom Controls
     * ------------------------------------------------------------- */
    applyMinimapSize() {
        const size = this.minimapSizes[this.minimapSizeIndex];
        if (!size || !this.minimapCanvas) return;

        this.minimapCanvas.width = size.w;
        this.minimapCanvas.height = size.h;

        if (this.minimapContainer) {
            this.minimapContainer.classList.remove('compact', 'standard', 'expanded', 'tactical', 'command');
            this.minimapContainer.classList.add(size.cls);
            this.minimapContainer.style.width = `${size.w}px`;
            this.minimapContainer.style.height = 'auto';
        }

        this.updateMinimapHeader();

        if (this.lastFrame && this.lastFrame.map && this.lastFrame.player) {
            this.renderMinimap(this.lastFrame.map, this.lastFrame.player, this.lastFrame.monsters || [], this.currentCameraYaw);
        }
    }

    setPing(ms) {
        if (this.pingBadge) {
            this.pingBadge.textContent = `Cloud [${ms}ms]`;
            this.pingBadge.style.color = ms < 100 ? '#62e062' : (ms < 250 ? '#ffd700' : '#ff7777');
        }
    }

    setStatus(status) {
        if (this.pingBadge) {
            this.pingBadge.textContent = status;
            if (status.includes('Connecting')) {
                this.pingBadge.style.color = '#ffd700';
            } else if (status.includes('Connected')) {
                this.pingBadge.style.color = '#62e062';
            } else if (status.includes('Disconnected') || status.includes('Error')) {
                this.pingBadge.style.color = '#ff5555';
            }
        }
    }

    setupMessageFeed() {
        this.messageFeedWindow = document.getElementById('message-feed-window');
        this.messageFeedList = document.getElementById('message-feed-list');
        this.messageFeedScroll = document.getElementById('message-feed-scroll');
        if (!this.messageHistory) this.messageHistory = [];
        this.userScrolledUp = false;

        const btnScrollTop = document.getElementById('btn-msg-scroll-top');
        if (btnScrollTop) {
            btnScrollTop.addEventListener('click', (e) => {
                e.stopPropagation();
                if (this.messageFeedScroll) {
                    this.messageFeedScroll.scrollTop = 0;
                    this.userScrolledUp = true;
                }
            });
        }

        const btnScrollBottom = document.getElementById('btn-msg-scroll-bottom');
        if (btnScrollBottom) {
            btnScrollBottom.addEventListener('click', (e) => {
                e.stopPropagation();
                if (this.messageFeedScroll) {
                    this.messageFeedScroll.scrollTop = this.messageFeedScroll.scrollHeight;
                    this.userScrolledUp = false;
                }
            });
        }

        const btnClear = document.getElementById('btn-msg-clear');
        if (btnClear) {
            btnClear.addEventListener('click', (e) => {
                e.stopPropagation();
                if (this.messageFeedList) {
                    this.messageFeedList.innerHTML = '';
                }
                this.messageHistory = [];
                this.userScrolledUp = false;
            });
        }

        const btnToggleSize = document.getElementById('btn-msg-size-toggle');
        if (btnToggleSize) {
            btnToggleSize.addEventListener('click', (e) => {
                e.stopPropagation();
                if (this.messageFeedWindow) {
                    this.messageFeedWindow.classList.toggle('expanded');
                    const isExpanded = this.messageFeedWindow.classList.contains('expanded');
                    btnToggleSize.textContent = isExpanded ? '▼ Compact' : '▲ Expand';
                    btnToggleSize.title = isExpanded ? 'Collapse Message Log to 2 lines' : 'Expand Message Log to show full history';
                    if (this.messageFeedScroll) {
                        this.messageFeedScroll.scrollTop = this.messageFeedScroll.scrollHeight;
                    }
                }
            });
        }

        const moreBadge = document.getElementById('msg-more-indicator');
        if (moreBadge) {
            moreBadge.addEventListener('click', (e) => {
                e.stopPropagation();
                if (window.__app && window.__app.network) {
                    window.__app.network.sendKey('space');
                }
            });
        }

        if (this.messageFeedScroll) {
            this.messageFeedScroll.addEventListener('scroll', () => {
                const maxScroll = this.messageFeedScroll.scrollHeight - this.messageFeedScroll.clientHeight;
                // If within 20px of bottom, stick to bottom on new messages
                this.userScrolledUp = (maxScroll - this.messageFeedScroll.scrollTop) > 20;
            });
            // Stop wheel / key navigation from leaking to game movement when interacting with feed
            this.messageFeedScroll.addEventListener('keydown', (e) => {
                if (['ArrowUp', 'ArrowDown', 'PageUp', 'PageDown', 'Home', 'End'].includes(e.key)) {
                    e.stopPropagation();
                }
            });
        }
    }

    resetMessages(welcomeText = null) {
        if (!this.messageFeedList) {
            this.messageFeedList = document.getElementById('message-feed-list');
            this.messageFeedScroll = document.getElementById('message-feed-scroll');
        }
        if (this.messageFeedList) {
            this.messageFeedList.innerHTML = '';
        }
        this.messageHistory = [];
        this.prevMessages = [];
        this.lastTermRow0 = null;
        if (welcomeText) {
            this.addMessage(welcomeText, '#ffd700');
        }
    }

    getMessageColor(text, attr) {
        if (!text) return '#c8d1dc';
        const t = text.toLowerCase();
        if (t.includes('you hit') || t.includes('you slash') || t.includes('you crush') || 
            t.includes('you smite') || t.includes('you shoot') || t.includes('you destroy') || 
            t.includes('dies.') || t.includes('destroyed.') || t.includes('killed.')) {
            return '#ffd166'; // Player offensive success (Gold)
        }
        if (t.includes('hit') || t.includes('slash') || t.includes('crush') || 
            t.includes('bite') || t.includes('claw') || t.includes('sting') || 
            t.includes('touch') || t.includes('spit') || t.includes('breath') || 
            t.includes('burn') || t.includes('wounds') || t.includes('damage') ||
            t.includes('attacks')) {
            return '#ff6b6b'; // Monster attacks player (Light Red)
        }
        if (t.includes('heal') || t.includes('feel very good') || t.includes('cure') || 
            t.includes('restore') || t.includes('regenerate')) {
            return '#06d6a0'; // Healing (Emerald Green)
        }
        if (t.includes('spell') || t.includes('prayer') || t.includes('magic') || 
            t.includes('scroll') || t.includes('glows') || t.includes('shines') || 
            t.includes('teleport') || t.includes('detect')) {
            return '#4cc9f0'; // Magic (Sky Blue)
        }
        if (t.includes('cannot') || t.includes('no potions') || t.includes('no scrolls') || 
            t.includes('nothing to fire') || t.includes('no spells') || t.includes('wall in the way') || 
            t.includes('nothing there') || t.includes('misses you') || t.includes('you miss')) {
            return '#ff9900'; // Warning / Miss / Blocked (Orange)
        }
        if (window.getAngbandColorString && attr !== undefined && attr > 1) {
            return window.getAngbandColorString(attr);
        }
        return '#c8d1dc'; // Default clear text (Light Slate)
    }

    addMessage(text, color = '#ffd700', rawText = null) {
        if (!text || !text.trim()) return;
        let trimmed = text.trim();
        if (trimmed.startsWith('---') || trimmed.startsWith('===')) return;

        // Clean out trailing prompt suffixes like " -more-" or " [more]"
        trimmed = trimmed.replace(/\s*[-–—]more[-–—]\s*$/i, '');
        trimmed = trimmed.replace(/\s*\[more\]\s*$/i, '');
        trimmed = trimmed.trim();
        if (!trimmed) return;

        // Strictly reject noisy setup fragments
        const lower = trimmed.toLowerCase();
        if (lower.includes('accept character history') ||
            lower.includes('to start over') ||
            lower.includes('r to reroll') ||
            lower.includes('point-based') ||
            lower.includes('standard roller') ||
            lower.includes('quick roller') ||
            lower.includes('character sheet') ||
            lower.includes('choose race') ||
            lower.includes('choose class') ||
            lower.includes('select an option') ||
            lower.includes('press any key') ||
            trimmed === "' . ." ||
            trimmed === "'.'") {
            return;
        }

        if (!this.messageFeedList) {
            this.messageFeedList = document.getElementById('message-feed-list');
            this.messageFeedScroll = document.getElementById('message-feed-scroll');
        }
        if (!this.messageHistory) {
            this.messageHistory = [];
        }

        const now = Date.now();
        const baseText = rawText || trimmed;

        // Deduplicate adjacent identical message by incrementing count on existing line
        const lastMsg = this.messageHistory.length > 0 ? this.messageHistory[this.messageHistory.length - 1] : null;
        if (lastMsg && (lastMsg.rawText === baseText || lastMsg.text === trimmed)) {
            lastMsg.repeat = (lastMsg.repeat || 1) + 1;
            if (lastMsg.el) {
                lastMsg.el.textContent = `${lastMsg.rawText || trimmed} (x${lastMsg.repeat})`;
            }
            if (this.messageFeedScroll && !this.userScrolledUp) {
                this.messageFeedScroll.scrollTop = this.messageFeedScroll.scrollHeight;
            }
            return;
        }

        // Remove .latest highlight from previously newest entry
        if (this.messageFeedList && this.messageFeedList.lastElementChild) {
            this.messageFeedList.lastElementChild.classList.remove('latest');
        }

        const el = document.createElement('div');
        el.className = 'feed-line latest';
        el.textContent = trimmed;
        if (color) el.style.color = color;

        if (this.messageFeedList) {
            this.messageFeedList.appendChild(el);
        }

        const msgObj = {
            id: now + '_' + Math.random(),
            text: trimmed,
            rawText: baseText,
            repeat: 1,
            color,
            turn: this.currentTurn,
            timestamp: now,
            el
        };
        this.messageHistory.push(msgObj);

        // Keep last 500 game messages in DOM history
        if (this.messageHistory.length > 500) {
            const evicted = this.messageHistory.shift();
            if (evicted.el && evicted.el.parentNode) {
                evicted.el.parentNode.removeChild(evicted.el);
            }
        }

        // Auto-scroll to bottom unless player explicitly scrolled up to read history
        if (this.messageFeedScroll && !this.userScrolledUp) {
            this.messageFeedScroll.scrollTop = this.messageFeedScroll.scrollHeight;
        }
    }

    updateLastMessageCount(rawText, count) {
        if (!this.messageHistory || this.messageHistory.length === 0) return;
        const lastMsg = this.messageHistory[this.messageHistory.length - 1];
        if (lastMsg && (lastMsg.rawText === rawText || lastMsg.text === rawText || lastMsg.text.startsWith(rawText))) {
            lastMsg.repeat = count;
            if (lastMsg.el) {
                lastMsg.el.textContent = `${lastMsg.rawText || rawText} (x${count})`;
            }
            if (this.messageFeedScroll && !this.userScrolledUp) {
                this.messageFeedScroll.scrollTop = this.messageFeedScroll.scrollHeight;
            }
        }
    }

    tickMessageQueue() {
        // With persistent landscape window, messages do not disappear from view, but retain history!
    }

    extractNewMessages(prevList, currentList) {
        if (!prevList || prevList.length === 0) return currentList || [];
        if (!currentList || currentList.length === 0) return [];

        const maxK = Math.min(prevList.length, currentList.length);
        for (let k = maxK; k > 0; k--) {
            let match = true;
            for (let j = 0; j < k; j++) {
                const p = prevList[prevList.length - k + j];
                const c = currentList[j];
                if (p.text !== c.text || p.attr !== c.attr) {
                    match = false;
                    break;
                }
            }
            if (match) {
                return currentList.slice(k);
            }
        }
        return currentList;
    }

    cycleMinimapSize(delta = 1) {
        this.minimapSizeIndex = (this.minimapSizeIndex + delta + this.minimapSizes.length) % this.minimapSizes.length;
        if (this.audio) this.audio.playMenuNav();
        this.applyMinimapSize();
    }

    adjustMinimapZoom(delta) {
        this.minimapZoom = Math.max(0.5, Math.min(3.0, Math.round((this.minimapZoom + delta) * 100) / 100));
        if (this.audio) this.audio.playMenuNav();
        this.updateMinimapHeader();
        if (this.lastFrame && this.lastFrame.map && this.lastFrame.player) {
            this.renderMinimap(this.lastFrame.map, this.lastFrame.player, this.lastFrame.monsters || [], this.currentCameraYaw);
        }
    }

    resetMinimapZoom() {
        this.minimapZoom = 1.0;
        if (this.audio) this.audio.playMenuNav();
        this.updateMinimapHeader();
        if (this.lastFrame && this.lastFrame.map && this.lastFrame.player) {
            this.renderMinimap(this.lastFrame.map, this.lastFrame.player, this.lastFrame.monsters || [], this.currentCameraYaw);
        }
    }

    updateMinimapHeader() {
        const size = this.minimapSizes[this.minimapSizeIndex];
        const hintEl = document.getElementById('minimap-hint-text');
        if (hintEl && size) {
            hintEl.textContent = `[⛶] ${size.name} • ${this.minimapZoom.toFixed(1)}x`;
        }
    }

    setCustomMinimapSize(w, h) {
        if (!this.minimapCanvas) return;
        const validW = Math.max(200, Math.min(800, Math.round(w)));
        const validH = Math.max(200, Math.min(800, Math.round(h)));
        if (this.minimapContainer) {
            this.minimapContainer.style.width = `${validW}px`;
            this.minimapContainer.style.height = `${validH}px`;
        }
        // Deduct ~76px for header, controls bar, and tilt bar so canvas never clips
        const canvasH = Math.max(120, validH - 76);
        this.minimapCanvas.width = validW;
        this.minimapCanvas.height = canvasH;
        this.updateMinimapHeader();
        if (this.lastFrame && this.lastFrame.map && this.lastFrame.player) {
            this.renderMinimap(this.lastFrame.map, this.lastFrame.player, this.lastFrame.monsters || [], this.currentCameraYaw);
        }
    }

    setupMinimapControls() {
        if (this.minimapHeader) {
            this.minimapHeader.style.cursor = 'pointer';
            this.minimapHeader.addEventListener('click', () => {
                this.cycleMinimapSize(1);
            });
        }

        if (this.minimapContainer) {
            this.minimapContainer.addEventListener('wheel', (e) => {
                e.preventDefault();
                this.adjustMinimapZoom(e.deltaY < 0 ? 0.15 : -0.15);
            }, { passive: false });
        }

        const btnZoomOut = document.getElementById('btn-map-zoom-out');
        if (btnZoomOut) {
            btnZoomOut.addEventListener('click', (e) => {
                e.stopPropagation();
                this.adjustMinimapZoom(-0.2);
            });
        }

        const btnZoomIn = document.getElementById('btn-map-zoom-in');
        if (btnZoomIn) {
            btnZoomIn.addEventListener('click', (e) => {
                e.stopPropagation();
                this.adjustMinimapZoom(0.2);
            });
        }

        const btnToggleSize = document.getElementById('btn-map-toggle-size');
        if (btnToggleSize) {
            btnToggleSize.addEventListener('click', (e) => {
                e.stopPropagation();
                this.cycleMinimapSize(1);
            });
        }

        const btnRecenter = document.getElementById('btn-map-recenter');
        if (btnRecenter) {
            btnRecenter.addEventListener('click', (e) => {
                e.stopPropagation();
                this.resetMinimapZoom();
            });
        }

        const btnSizeDec = document.getElementById('btn-map-size-dec');
        if (btnSizeDec) {
            btnSizeDec.addEventListener('click', (e) => {
                e.stopPropagation();
                this.cycleMinimapSize(-1);
            });
        }

        const btnSizeInc = document.getElementById('btn-map-size-inc');
        if (btnSizeInc) {
            btnSizeInc.addEventListener('click', (e) => {
                e.stopPropagation();
                this.cycleMinimapSize(1);
            });
        }
    }

    /* -------------------------------------------------------------
     * Continuous 60 FPS Camera Yaw Integration (Compass & Minimap)
     * ------------------------------------------------------------- */
    onCameraTurn(yaw) {
        this.currentCameraYaw = yaw;
        this.updateFooterFacing(yaw);
        this.renderCompass(yaw);
        if (this.lastFrame && this.lastFrame.map && this.lastFrame.player) {
            this.renderMinimap(this.lastFrame.map, this.lastFrame.player, this.lastFrame.monsters || [], yaw);
        }
    }

    updateFooterFacing(yaw) {
        if (!this.footerFacing) return;
        const normYaw = (yaw % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI);
        const sector = Math.round(normYaw / (Math.PI / 4)) % 8;
        const sectorNames = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'];
        const sectorArrows = ['▲', '↗', '►', '↘', '▼', '↙', '◄', '↖'];
        const dirName = sectorNames[sector] || 'N';
        const dirArrow = sectorArrows[sector] || '▲';
        this.footerFacing.textContent = `🧭 ${dirName}`;
        this.footerFacing.title = `Camera Facing: ${dirName} (${dirArrow})`;
    }

    renderCompass(yaw) {
        if (!this.compassCtx || !this.compassCanvas) return;
        const ctx = this.compassCtx;
        const w = this.compassCanvas.width;
        const h = this.compassCanvas.height;
        const cx = w / 2;
        const cy = h / 2;
        const radius = Math.min(cx, cy) - 2.5;

        ctx.clearRect(0, 0, w, h);

        // Compass background circular bezel (Overlay.cs:1757-1758)
        ctx.fillStyle = 'rgba(12, 14, 20, 0.90)';
        ctx.beginPath();
        ctx.arc(cx, cy, radius, 0, Math.PI * 2);
        ctx.fill();

        ctx.strokeStyle = 'rgba(180, 150, 60, 0.75)';
        ctx.lineWidth = 1.5;
        ctx.beginPath();
        ctx.arc(cx, cy, radius, 0, Math.PI * 2);
        ctx.stroke();

        // Continuous needle direction vector
        // In 2D: X is right, Y is down; North is (0, -1).
        const dirVec = { x: Math.sin(yaw), y: -Math.cos(yaw) };
        const sideVec = { x: -dirVec.y * 3.5, y: dirVec.x * 3.5 };

        const tip = { x: cx + dirVec.x * (radius - 2.5), y: cy + dirVec.y * (radius - 2.5) };
        const baseCenter = { x: cx - dirVec.x * (radius - 4.5), y: cy - dirVec.y * (radius - 4.5) };
        const leftCorner = { x: cx + sideVec.x, y: cy + sideVec.y };
        const rightCorner = { x: cx - sideVec.x, y: cy - sideVec.y };

        // North / Forward pointer (red/ruby, Overlay.cs:1779-1780)
        ctx.fillStyle = '#f03838';
        ctx.beginPath();
        ctx.moveTo(tip.x, tip.y);
        ctx.lineTo(leftCorner.x, leftCorner.y);
        ctx.lineTo(cx, cy);
        ctx.closePath();
        ctx.fill();

        ctx.fillStyle = '#b81c1c';
        ctx.beginPath();
        ctx.moveTo(tip.x, tip.y);
        ctx.lineTo(cx, cy);
        ctx.lineTo(rightCorner.x, rightCorner.y);
        ctx.closePath();
        ctx.fill();

        // South / Backward pointer (silver/steel, Overlay.cs:1783-1784)
        ctx.fillStyle = '#9aa6c0';
        ctx.beginPath();
        ctx.moveTo(baseCenter.x, baseCenter.y);
        ctx.lineTo(leftCorner.x, leftCorner.y);
        ctx.lineTo(cx, cy);
        ctx.closePath();
        ctx.fill();

        ctx.fillStyle = '#738099';
        ctx.beginPath();
        ctx.moveTo(baseCenter.x, baseCenter.y);
        ctx.lineTo(cx, cy);
        ctx.lineTo(rightCorner.x, rightCorner.y);
        ctx.closePath();
        ctx.fill();

        // Fixed north marker notch on compass bezel
        ctx.fillStyle = '#ffd700';
        ctx.beginPath();
        ctx.moveTo(cx, cy - radius + 1);
        ctx.lineTo(cx - 3, cy - radius + 6);
        ctx.lineTo(cx + 3, cy - radius + 6);
        ctx.closePath();
        ctx.fill();

        // Center golden jewel rivet
        ctx.fillStyle = '#ffd700';
        ctx.beginPath();
        ctx.arc(cx, cy, 2.2, 0, Math.PI * 2);
        ctx.fill();

        // Update compass facing label if present
        if (this.compassLabel) {
            const normYaw = (yaw % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI);
            const sector = Math.round(normYaw / (Math.PI / 4)) % 8;
            const names = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'];
            this.compassLabel.textContent = names[sector] || 'N';
        }
    }

    /* -------------------------------------------------------------
     * Minimap Radar Rendering (Zoom, Direction Cone & Tiles)
     * ------------------------------------------------------------- */
    renderMinimap(map, player, monsters, continuousYaw) {
        if (!this.minimapCtx || !this.minimapCanvas || !map || !player) return;
        const ctx = this.minimapCtx;
        const w = this.minimapCanvas.width;
        const h = this.minimapCanvas.height;

        const yaw = continuousYaw !== undefined ? continuousYaw : this.currentCameraYaw;

        // Dynamic cell size with minimap zoom (Matches Overlay.cs:1434)
        const baseCellSize = 12;
        const cellSize = Math.max(4, Math.min(48, Math.round(baseCellSize * this.minimapZoom)));

        const cols = Math.floor(w / cellSize);
        const lines = Math.floor(h / cellSize);

        const px = player.x;
        const py = player.y;

        // Use continuous player coordinates when stepping/animating for butter-smooth 60fps tracking
        const drawPx = (typeof this.continuousX === 'number' && !isNaN(this.continuousX)) ? this.continuousX : px;
        const drawPy = (typeof this.continuousY === 'number' && !isNaN(this.continuousY)) ? this.continuousY : py;

        const ox = Math.max(0, Math.min(map.w - cols, Math.floor(drawPx) - Math.floor(cols / 2)));
        const oy = Math.max(0, Math.min(map.h - lines, Math.floor(drawPy) - Math.floor(lines / 2)));

        const cx = (drawPx - ox) * cellSize + cellSize / 2;
        const cy = (drawPy - oy) * cellSize + cellSize / 2;

        // Direction vector from continuous camera yaw
        const dir = { x: Math.sin(yaw), y: -Math.cos(yaw) };
        const side = { x: -dir.y, y: dir.x };

        // 1. Offscreen Tile Cache Generation: Re-rasterize ASCII glyphs when map sequence, viewport scroll, or zoom changes
        const frameSeq = (this.lastFrame && this.lastFrame.seq !== undefined) ? this.lastFrame.seq : 0;
        const cacheKey = `${frameSeq}_${ox}_${oy}_${this.minimapZoom}_${w}_${h}`;
        if (cacheKey !== this.minimapTileCacheKey || this.minimapTileCanvas.width !== w || this.minimapTileCanvas.height !== h) {
            this.minimapTileCanvas.width = w;
            this.minimapTileCanvas.height = h;
            this.minimapTileCacheKey = cacheKey;

            const tCtx = this.minimapTileCtx;
            tCtx.fillStyle = '#060910';
            tCtx.fillRect(0, 0, w, h);

            function darkenColor(hex, factor = 0.45) {
                if (!hex || hex[0] !== '#') return hex;
                const r = Math.round(parseInt(hex.slice(1, 3), 16) * factor);
                const g = Math.round(parseInt(hex.slice(3, 5), 16) * factor);
                const b = Math.round(parseInt(hex.slice(5, 7), 16) * factor);
                return `rgba(${r}, ${g}, ${b}, 0.70)`;
            }

            if (cellSize > 6) {
                tCtx.font = `bold ${Math.max(8, Math.floor(cellSize * 1.02))}px monospace`;
                tCtx.textAlign = 'center';
                tCtx.textBaseline = 'middle';
            }

            for (let rowIdx = 0; rowIdx < lines && (oy + rowIdx) < map.h; rowIdx++) {
                const my = oy + rowIdx;
                const row = map.rows[my];
                if (!row || !row.g) continue;

                for (let colIdx = 0; colIdx < cols && (ox + colIdx) < map.w; colIdx++) {
                    const mx = ox + colIdx;
                    if (mx >= row.g.length) continue;

                    const ch = row.g[mx];
                    if (ch === ' ') continue;

                    const flag = (row.l && mx < row.l.length) ? (parseInt(row.l[mx], 16) || 0) : 0;
                    if ((flag & 0x3) === 0) continue; // Fog of war: not known and not in view

                    const inView = (flag & 0x2) !== 0;
                    const sx = colIdx * cellSize;
                    const sy = rowIdx * cellSize;

                    if (inView) {
                        const isFloor = ch === '.' || ch === '+' || ch === '\'';
                        tCtx.fillStyle = isFloor ? 'rgba(36, 41, 56, 0.45)' : 'rgba(26, 30, 42, 0.35)';
                    } else {
                        tCtx.fillStyle = 'rgba(8, 10, 15, 0.65)';
                    }
                    tCtx.fillRect(sx, sy, cellSize, cellSize);

                    const attrIdx = mx * 2;
                    const attrVal = (row.a && attrIdx + 1 < row.a.length) ? parseInt(row.a.substring(attrIdx, attrIdx + 2), 16) : 1;
                    let colour = window.getAngbandColorString ? window.getAngbandColorString(attrVal) : '#ffffff';
                    if (!inView) {
                        colour = darkenColor(colour, 0.45);
                    }

                    tCtx.fillStyle = colour;
                    if (cellSize <= 6) {
                        tCtx.fillRect(sx, sy, cellSize, cellSize);
                    } else {
                        tCtx.fillText(ch, sx + cellSize / 2, sy + cellSize / 2);
                    }
                }
            }
        }

        // Fast GPU blit of cached minimap tiles
        ctx.clearRect(0, 0, w, h);
        ctx.drawImage(this.minimapTileCanvas, 0, 0);

        // 2. Draw Translucent Golden Vision Cone (Matches Godot Overlay.cs:1148-1165, 1449-1459)
        const coneDist = Math.max(16, Math.min(85, cellSize * 4.4));
        const coneSpread = coneDist * 0.65;
        const leftTip = { x: cx + dir.x * coneDist + side.x * coneSpread, y: cy + dir.y * coneDist + side.y * coneSpread };
        const rightTip = { x: cx + dir.x * coneDist - side.x * coneSpread, y: cy + dir.y * coneDist - side.y * coneSpread };

        ctx.fillStyle = 'rgba(255, 215, 0, 0.18)';
        ctx.beginPath();
        ctx.moveTo(cx, cy);
        ctx.lineTo(leftTip.x, leftTip.y);
        ctx.lineTo(rightTip.x, rightTip.y);
        ctx.closePath();
        ctx.fill();

        ctx.strokeStyle = 'rgba(255, 220, 100, 0.50)';
        ctx.lineWidth = 1.0;
        ctx.beginPath();
        ctx.moveTo(cx, cy);
        ctx.lineTo(leftTip.x, leftTip.y);
        ctx.stroke();
        ctx.beginPath();
        ctx.moveTo(cx, cy);
        ctx.lineTo(rightTip.x, rightTip.y);
        ctx.stroke();

        const baseAngle = Math.atan2(dir.y, dir.x);
        ctx.beginPath();
        ctx.arc(cx, cy, coneDist, baseAngle - 0.58, baseAngle + 0.58);
        ctx.stroke();

        // 3. Monsters on Minimap
        monsters.forEach(m => {
            const colIdx = m.x - ox;
            const rowIdx = m.y - oy;
            if (colIdx >= 0 && colIdx < cols && rowIdx >= 0 && rowIdx < lines) {
                const sx = colIdx * cellSize;
                const sy = rowIdx * cellSize;
                ctx.fillStyle = 'rgba(230, 40, 40, 0.45)';
                ctx.fillRect(sx, sy, cellSize, cellSize);
                const mCol = (window.getAngbandColorString && m.attr !== undefined) ? window.getAngbandColorString(m.attr) : '#ff4444';
                ctx.fillStyle = mCol;
                if (cellSize <= 6) {
                    ctx.fillRect(sx + 1, sy + 1, Math.max(2, cellSize - 2), Math.max(2, cellSize - 2));
                } else {
                    ctx.font = `bold ${Math.max(8, Math.floor(cellSize * 1.02))}px monospace`;
                    ctx.textAlign = 'center';
                    ctx.textBaseline = 'middle';
                    ctx.fillText(m.glyph || 'm', sx + cellSize / 2, sy + cellSize / 2);
                }
            }
        });

        // 4. Player Directional Pointer Polygon (Matches Godot Overlay.cs:1050-1067, 1520-1528)
        ctx.fillStyle = 'rgba(255, 215, 50, 0.30)';
        ctx.beginPath();
        ctx.arc(cx, cy, Math.max(cellSize * 0.55, 4.5), 0, Math.PI * 2);
        ctx.fill();

        const ptrSize = Math.max(cellSize * 0.70, 6.0);
        const pTip = { x: cx + dir.x * ptrSize, y: cy + dir.y * ptrSize };
        const pBaseCenter = { x: cx - dir.x * (ptrSize * 0.55), y: cy - dir.y * (ptrSize * 0.55) };
        const pLeftCorner = { x: pBaseCenter.x + side.x * (ptrSize * 0.65), y: pBaseCenter.y + side.y * (ptrSize * 0.65) };
        const pRightCorner = { x: pBaseCenter.x - side.x * (ptrSize * 0.65), y: pBaseCenter.y - side.y * (ptrSize * 0.65) };
        const pNotch = { x: cx - dir.x * (ptrSize * 0.20), y: cy - dir.y * (ptrSize * 0.20) };

        // Left triangle (bright yellow)
        ctx.fillStyle = '#fff266';
        ctx.beginPath();
        ctx.moveTo(pTip.x, pTip.y);
        ctx.lineTo(pLeftCorner.x, pLeftCorner.y);
        ctx.lineTo(pNotch.x, pNotch.y);
        ctx.closePath();
        ctx.fill();

        // Right triangle (darkened gold)
        ctx.fillStyle = '#bfb54d';
        ctx.beginPath();
        ctx.moveTo(pTip.x, pTip.y);
        ctx.lineTo(pNotch.x, pNotch.y);
        ctx.lineTo(pRightCorner.x, pRightCorner.y);
        ctx.closePath();
        ctx.fill();

        // Outline
        ctx.strokeStyle = '#261a05';
        ctx.lineWidth = 1.2;
        ctx.beginPath();
        ctx.moveTo(pLeftCorner.x, pLeftCorner.y);
        ctx.lineTo(pTip.x, pTip.y);
        ctx.lineTo(pRightCorner.x, pRightCorner.y);
        ctx.lineTo(pNotch.x, pNotch.y);
        ctx.closePath();
        ctx.stroke();
    }

    /* -------------------------------------------------------------
     * Frame Update: Gauges, 3-Row Telemetry Footer, Minimap Header
     * ------------------------------------------------------------- */
    update(frame) {
        if (!frame) return;
        this.lastFrame = frame;

        const player = frame.player || {};
        const ui = frame.ui || {};

        // Character Name, Race, Class
        if (player.name) {
            const r = player.race || '';
            const c = player.class || '';
            this.charTitle.textContent = `${player.name} (${r} ${c})`;
        } else {
            this.charTitle.textContent = 'Hero of Angband';
        }

        // HP Bar
        const curHp = player.hp !== undefined ? player.hp : (player.chp !== undefined ? player.chp : 100);
        const maxHp = player.hp_max !== undefined ? player.hp_max : (player.mhp !== undefined ? player.mhp : 100);
        const hpPct = Math.max(0, Math.min(100, (curHp / Math.max(1, maxHp)) * 100));
        this.hpFill.style.width = `${hpPct}%`;
        this.hpText.textContent = `${curHp} / ${maxHp}`;

        // SP Bar
        const curSp = player.sp !== undefined ? player.sp : (player.csp !== undefined ? player.csp : 0);
        const maxSp = player.sp_max !== undefined ? player.sp_max : (player.msp !== undefined ? player.msp : 0);
        const spPct = maxSp > 0 ? Math.max(0, Math.min(100, (curSp / maxSp) * 100)) : 0;
        this.spFill.style.width = `${spPct}%`;
        this.spText.textContent = `${curSp} / ${maxSp}`;

        // Left Panel Stats
        if (player.stats) {
            const getStat = s => (s && typeof s === 'object') ? (s.use || s.top || '10') : (s || '10');
            this.statStr.textContent = getStat(player.stats.str);
            this.statInt.textContent = getStat(player.stats.int);
            this.statWis.textContent = getStat(player.stats.wis);
            this.statDex.textContent = getStat(player.stats.dex);
            this.statCon.textContent = getStat(player.stats.con);
        }

        // Depth, Gold, AC
        const depth = player.depth !== undefined ? player.depth : 0;
        const locStr = depth === 0 ? 'Town' : `${depth * 50}ft`;
        this.depthVal.textContent = locStr;
        this.goldVal.textContent = (player.gold || 0).toLocaleString();
        const yaw = (window.__app && window.__app.dungeon) ? window.__app.dungeon.yaw : (this.currentCameraYaw || 0);
        const normYaw = (yaw % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI);
        const sector = Math.round(normYaw / (Math.PI / 4)) % 8;
        const sectorNames = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'];
        const sectorArrows = ['▲', '↗', '►', '↘', '▼', '↙', '◄', '↖'];
        const arrowChar = sectorArrows[sector] || '▲';
        const facingName = sectorNames[sector] || 'N';

        this.updateFooterFacing(yaw);

        // Minimap Header (Overlay.cs:1404)
        if (player.x !== undefined && player.y !== undefined) {
            const titleEl = document.getElementById('minimap-title-text');
            if (titleEl) {
                titleEl.innerHTML = `MINIMAP (${player.x},${player.y}) <span style="color:#ffd700;">[${arrowChar} ${facingName}]</span>`;
            }
        }

        // Contextual Diagonal Movement Hint (Row 1)
        if (this.footerDiagonalHint && frame.map && player.x !== undefined && player.y !== undefined) {
            const px = player.x;
            const py = player.y;
            const yaw = window.__app && window.__app.dungeon ? window.__app.dungeon.yaw : 0;
            const normYaw = (yaw % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI);
            const sector = Math.round(normYaw / (Math.PI / 4)) % 8;

            const forwardOffsets = {
                0: { dx: 0, dy: -1 }, // N
                1: { dx: 1, dy: -1 }, // NE
                2: { dx: 1, dy: 0 },  // E
                3: { dx: 1, dy: 1 },  // SE
                4: { dx: 0, dy: 1 },  // S
                5: { dx: -1, dy: 1 }, // SW
                6: { dx: -1, dy: 0 }, // W
                7: { dx: -1, dy: -1 } // NW
            };
            const fwd = forwardOffsets[sector] || { dx: 0, dy: -1 };
            const frontFeat = this.getFeatAt(frame.map, px + fwd.dx, py + fwd.dy);
            const isFrontBlocked = !this.isWalkableOrPortal(frontFeat);

            let diagText = '';
            if (isFrontBlocked) {
                // Check relative diagonal directions: 7 = Forward-Left, 9 = Forward-Right
                const leftSector = (sector + 7) % 8;
                const rightSector = (sector + 1) % 8;
                const leftOff = forwardOffsets[leftSector];
                const rightOff = forwardOffsets[rightSector];
                const leftWalkable = leftOff && this.isWalkableOrPortal(this.getFeatAt(frame.map, px + leftOff.dx, py + leftOff.dy));
                const rightWalkable = rightOff && this.isWalkableOrPortal(this.getFeatAt(frame.map, px + rightOff.dx, py + rightOff.dy));

                if (leftWalkable && rightWalkable) {
                    diagText = '⤢ Diag: [7] ↖ / [9] ↗';
                } else if (leftWalkable) {
                    diagText = '↖ Diag: [7] Step Left';
                } else if (rightWalkable) {
                    diagText = '↗ Diag: [9] Step Right';
                }
            }

            this.footerDiagonalHint.textContent = diagText ? `${diagText} • ` : '';
            this.footerDiagonalHint.style.display = diagText ? 'inline' : 'none';
        }

        // Contextual Stairs Hint (Row 1) & Dynamic Action Button
        const btnStair = document.getElementById('btn-stair');
        if (frame.map && player.x !== undefined && player.y !== undefined) {
            const currentFeat = this.getFeatAt(frame.map, player.x, player.y);
            let stairsText = '';
            if (currentFeat === 6) { // Downstairs
                stairsText = depth === 0 ? `[>] Down to Dungeon (50')` : `[>] Down to ${(depth + 1) * 50}ft`;
                if (btnStair) {
                    btnStair.style.display = 'inline-flex';
                    btnStair.innerHTML = depth === 0 ? '⬇ Enter Dungeon <kbd>&gt;</kbd>' : '⬇ Descend <kbd>&gt;</kbd>';
                    btnStair.dataset.key = '>';
                }
            } else if (currentFeat === 5) { // Upstairs
                stairsText = depth === 1 ? `[<] Up to Town` : `[<] Up to ${(depth - 1) * 50}ft`;
                if (btnStair) {
                    btnStair.style.display = 'inline-flex';
                    btnStair.innerHTML = depth === 1 ? '⬆ Return to Town <kbd>&lt;</kbd>' : '⬆ Ascend <kbd>&lt;</kbd>';
                    btnStair.dataset.key = '<';
                }
            } else {
                if (btnStair) {
                    btnStair.style.display = 'none';
                }
            }
            if (this.footerStairsHint) {
                this.footerStairsHint.textContent = stairsText;
                this.footerStairsHint.style.display = stairsText ? 'inline-flex' : 'none';
            }
        }

        // Detailed 3-Row Telemetry Footer / Streamlined Status Bar
        if (this.footerCharIdentity) {
            this.footerCharIdentity.textContent = `${player.name || 'Hero'} the ${player.race || 'Human'} ${player.class || 'Warrior'}`;
        }
        if (this.footerLevel) {
            this.footerLevel.textContent = `Lvl ${player.level || 1}`;
        }
        if (this.footerHp) {
            this.footerHp.textContent = `HP ${curHp}/${maxHp}`;
            const hpRatio = curHp / Math.max(1, maxHp);
            this.footerHp.style.color = hpRatio > 0.6 ? '#62e062' : (hpRatio > 0.25 ? '#ffd700' : '#ff4444');
        }
        if (this.footerSp) {
            if (maxSp > 0) {
                this.footerSp.style.display = 'inline';
                this.footerSp.textContent = `SP ${curSp}/${maxSp}`;
            } else {
                this.footerSp.style.display = 'none';
            }
        }
        if (this.footerAc) {
            this.footerAc.textContent = `AC ${player.ac !== undefined ? player.ac : '0'}`;
        }
        if (this.footerGold) {
            this.footerGold.textContent = `AU ${(player.gold || 0).toLocaleString()}`;
        }
        if (this.footerExp) {
            const expNext = player.exp_next;
            this.footerExp.textContent = `EXP ${player.exp || 0}${expNext ? `/${expNext}` : ''}`;
        }
        if (this.footerPlace) {
            this.footerPlace.textContent = depth === 0 ? 'Town' : `DL ${depth} (${depth * 50}ft)`;
        }
        if (this.footerSpeed) {
            const spd = player.speed !== undefined ? player.speed : 110;
            const spdStr = spd > 110 ? `Fast (+${spd - 110})` : (spd < 110 ? `Slow (-${110 - spd})` : 'Normal (0)');
            this.footerSpeed.textContent = `⚡ Spd ${spdStr}`;
        }
        if (this.footerLight) {
            const lightRadius = player.light !== undefined ? player.light : 0;
            const hasLightItem = !!player.light_item;
            const lightItemName = player.light_item || 'None';
            const lightFuel = player.light_fuel || 0;
            const lightStr = depth === 0
                ? (hasLightItem ? `☀️ ${lightItemName}` : '☀️ Daylight')
                : (hasLightItem ? `🏮 ${lightItemName} (${lightFuel > 0 ? `${lightFuel}t` : `R:${lightRadius}`})` : (lightRadius > 0 ? `🏮 Light ${lightRadius}` : '🌑 Dark'));
            this.footerLight.textContent = lightStr;
        }
        if (this.footerFacing) {
            this.footerFacing.textContent = `🧭 ${facingName}`;
        }

        // Target tracking
        if (this.footerTarget) {
            if (player.target && player.target.name) {
                this.footerTarget.style.display = 'inline-flex';
                this.footerTarget.textContent = `⚔ Target: ${player.target.name} (${player.target.pct || 100}%)`;
            } else {
                this.footerTarget.style.display = 'none';
            }
        }

        // Status badges
        if (this.footerBadges) {
            this.footerBadges.innerHTML = '';
            if (player.statuses && Array.isArray(player.statuses) && player.statuses.length > 0) {
                player.statuses.forEach(st => {
                    const span = document.createElement('span');
                    span.className = 'footer-badge';
                    span.textContent = `[${st.name}]`;
                    if (window.getAngbandColorString && st.attr !== undefined) {
                        span.style.color = window.getAngbandColorString(st.attr);
                    }
                    this.footerBadges.appendChild(span);
                });
            }

            if (player.study && player.study > 0) {
                const span = document.createElement('span');
                span.className = 'footer-badge badge-study';
                span.textContent = `[STUDY: ${player.study}]`;
                this.footerBadges.appendChild(span);
            }

            if (player.resting && player.resting !== 0) {
                const span = document.createElement('span');
                span.className = 'footer-badge badge-resting';
                span.textContent = `[RESTING${player.resting > 0 ? `: ${player.resting}` : ''}]`;
                this.footerBadges.appendChild(span);
            }

            if (player.word_recall && player.word_recall > 0) {
                const span = document.createElement('span');
                span.className = 'footer-badge badge-recall';
                span.textContent = `[RECALL: ${player.word_recall}t]`;
                this.footerBadges.appendChild(span);
            }
        }

        // Gear summary on status bar
        if (this.footerGear) {
            const weap = player.weapon_item || 'Bare Hands';
            this.footerGear.style.display = 'inline-flex';
            this.footerGear.textContent = `⚔ ${weap}`;
            this.footerGear.title = `Wielding: ${weap} • Shield: ${player.shield_item || 'None'} • Ranged: ${player.bow_item || 'None'}`;
        }

        // Turn number tracking
        if (player.turn !== undefined) {
            this.currentTurn = player.turn;
        } else if (player.game_turn !== undefined) {
            this.currentTurn = player.game_turn;
        }

        if (!this.messageFeedWindow) {
            this.messageFeedWindow = document.getElementById('message-feed-window');
        }
        if (!this.messageFeedList) {
            this.messageFeedList = document.getElementById('message-feed-list');
            this.messageFeedScroll = document.getElementById('message-feed-scroll');
        }

        const inPlay = Boolean(frame && (frame.phase === 'play' || (frame.player && frame.player.name)));

        // Message Feed Window Visibility: Always visible in town and dungeon when in play
        if (this.messageFeedWindow) {
            if (inPlay) {
                this.messageFeedWindow.style.display = 'flex';
                this.messageFeedWindow.style.visibility = 'visible';
                this.messageFeedWindow.style.opacity = '1';
            } else {
                this.messageFeedWindow.style.display = 'none';
            }
        }

        const moreBadge = document.getElementById('msg-more-indicator');
        if (moreBadge) {
            moreBadge.style.display = 'none'; // Auto-advanced, no pulsing chip required
        }

        // Clean message log start upon entering active play (Town or Dungeon)
        if (inPlay && (!this.wasInPlay || !this.messageHistory || this.messageHistory.length === 0)) {
            const depth = player.depth !== undefined ? player.depth : 0;
            const welcomeMsg = depth === 0
                ? "Welcome to the Town of Angband! Visit the General Store and Armory to equip your journey."
                : `You descend into Dungeon Level ${depth} (${depth * 50}ft).`;
            this.resetMessages(welcomeMsg);
            this.lastSeenMessages = (frame.messages && Array.isArray(frame.messages))
                ? frame.messages.map(m => ({ text: m.text, count: m.count, attr: m.attr }))
                : [];
            this.lastTermRow0 = null;
        }

        // Multi-Line Message Log Queue: Capture ALL in-game messages during active play
        if (inPlay) {
            let newItems = [];
            // 1. Process structured engine messages
            if (frame.messages && Array.isArray(frame.messages)) {
                const curList = frame.messages.filter(m => m && m.text && m.text.trim().length > 0 &&
                    !m.text.trim().startsWith('===') && !m.text.trim().startsWith('---'));
                const prevList = this.lastSeenMessages || [];

                if (curList.length > 0) {
                    const prevLast = prevList.length > 0 ? prevList[prevList.length - 1] : null;
                    const curLast = curList[curList.length - 1];

                    // Suffix matching to find newly appended messages
                    let matchedOverlap = 0;
                    for (let k = Math.min(prevList.length, curList.length); k > 0; k--) {
                        let match = true;
                        for (let j = 0; j < k; j++) {
                            if (prevList[prevList.length - k + j].text !== curList[j].text) {
                                match = false;
                                break;
                            }
                        }
                        if (match) {
                            matchedOverlap = k;
                            break;
                        }
                    }

                    newItems = curList.slice(matchedOverlap);
                    for (const item of newItems) {
                        let text = item.text.trim();
                        if (item.count && item.count > 1) {
                            text += ` (x${item.count})`;
                        }
                        const col = this.getMessageColor(item.text.trim(), item.attr);
                        this.addMessage(text, col, item.text.trim());
                    }

                    // If no new items were appended, did the last message increment its count?
                    if (newItems.length === 0 && prevLast && curLast && prevLast.text === curLast.text && curLast.count > (prevLast.count || 1)) {
                        this.updateLastMessageCount(curLast.text.trim(), curLast.count);
                    }

                    this.lastSeenMessages = curList.map(m => ({ text: m.text, count: m.count, attr: m.attr }));
                }
            }

            // 2. Also capture dynamic action lines on term row 0 during play (combat, spell, status, prompts)
            if (frame.term && frame.term.rows && frame.term.rows[0] && frame.term.rows[0].g) {
                let line0 = frame.term.rows[0].g.trim();
                line0 = line0.replace(/\s*[-–—]more[-–—]\s*$/i, '');
                line0 = line0.replace(/\s*\[more\]\s*$/i, '');
                line0 = line0.replace(/\s*\[?\s*press\s+space\s*\]?\s*$/i, '');
                line0 = line0.trim();
                const isMenuPrompt = line0.includes('Select Item:') ||
                                     line0.includes('(Inven:') ||
                                     line0.includes('(Equip:') ||
                                     line0.includes('which item?') ||
                                     line0.includes('which potion?') ||
                                     line0.includes('which scroll?') ||
                                     line0.includes('which spell?') ||
                                     line0.includes('which prayer?') ||
                                     line0.includes('Direction or <click>') ||
                                     line0.includes('Select an option') ||
                                     line0.includes('Press any key') ||
                                     line0.startsWith('---') ||
                                     line0.startsWith('===');

                if (line0 && line0.length > 0 && !isMenuPrompt) {
                    const alreadyInNewItems = newItems.some(it => it.text && it.text.trim() === line0);
                    const isNewAction = frame.seq !== this.lastTermRow0Seq || line0 !== this.lastTermRow0;
                    if (!alreadyInNewItems && isNewAction) {
                        this.lastTermRow0 = line0;
                        this.lastTermRow0Seq = frame.seq;
                        const col = this.getMessageColor(line0);
                        this.addMessage(line0, col, line0);
                    }
                }
            }
        }

        this.wasInPlay = inPlay;

        // Process message queue turn/time expiration tick
        this.tickMessageQueue();

        // Minimap Radar & Compass
        if (frame.map && frame.player) {
            this.renderMinimap(frame.map, frame.player, frame.monsters || [], this.currentCameraYaw);
        }
        this.renderCompass(this.currentCameraYaw);

        // Check if player died: ALWAYS display death modal whenever player is dead or HP <= 0
        const isDead = Boolean(player.dead || (player.hp !== undefined && player.hp <= 0 && player.hp_max > 0));
        if (isDead) {
            this.isDeadInPlay = true;
            this.showDeathModal(frame);
        } else if (!this.isDeadInPlay) {
            this.hideDeathModal();
        }
    }

    getFeatAt(map, x, y) {
        if (!map || !map.rows || y < 0 || y >= map.h || x < 0 || x >= map.w) return 0;
        const row = map.rows[y];
        if (!row || !row.f) return 0;
        const idx = x * 2;
        if (idx + 1 >= row.f.length) return 0;
        const hi = parseInt(row.f[idx], 16) || 0;
        const lo = parseInt(row.f[idx + 1], 16) || 0;
        return (hi << 4) | lo;
    }

    /* -------------------------------------------------------------
     * Atmospheric Death Screen Modal (Matches Godot Overlay.cs:840-1022)
     * ------------------------------------------------------------- */
    hideDeathModal() {
        if (!this.deathModal) return;
        this.deathModal.classList.add('hidden');
        this.isDeadInPlay = false;
    }

    rerollCharacter() {
        this.hideDeathModal();
        this.isDeadInPlay = false;
        const randomId = Math.random().toString(36).substring(2, 6).toUpperCase();
        const newChar = 'Hero_' + randomId;
        console.log('[Angband3D] Reroll requested. Starting fresh character:', newChar);
        window.location.href = `/?char=${encodeURIComponent(newChar)}&new=1`;
    }

    setupDeathModal() {
        if (!this.deathModal) return;

        this.deathTabs.forEach(tab => {
            tab.addEventListener('click', (e) => {
                const tabIdx = parseInt(tab.dataset.tab, 10);
                this.switchDeathTab(tabIdx);
            });
        });

        const btnReload = document.getElementById('btn-death-reload');
        if (btnReload) {
            btnReload.addEventListener('click', () => {
                this.hideDeathModal();
                if (window.__app && window.__app.startNewRandomHero) {
                    window.__app.startNewRandomHero();
                } else {
                    window.location.reload();
                }
            });
        }

        const btnReroll = document.getElementById('btn-death-reroll');
        if (btnReroll) {
            btnReroll.addEventListener('click', () => {
                this.hideDeathModal();
                if (window.__app && window.__app.startNewRandomHero) {
                    window.__app.startNewRandomHero();
                } else {
                    this.rerollCharacter();
                }
            });
        }

        const btnMenu = document.getElementById('btn-death-menu');
        if (btnMenu) {
            btnMenu.addEventListener('click', () => {
                this.hideDeathModal();
                if (window.__app && window.__app.returnToMainMenu) {
                    window.__app.returnToMainMenu();
                } else {
                    window.location.href = '/';
                }
            });
        }
    }

    switchDeathTab(idx) {
        this.activeDeathTab = idx;
        this.deathTabs.forEach(t => {
            t.classList.toggle('active', parseInt(t.dataset.tab, 10) === idx);
        });
        this.deathContents.forEach((c, i) => {
            c.classList.toggle('active', i === idx);
        });

        const frameToUse = this.lastDeathFrame || this.lastFrame;
        if (frameToUse) {
            this.renderDeathTabContent(idx, frameToUse);
        }
    }

    showDeathModal(frame) {
        if (!this.deathModal) return;
        this.deathModal.classList.remove('hidden');
        this.lastDeathFrame = frame;

        const player = frame.player || {};
        const name = player.name || 'Hero';
        const race = player.race || 'Human';
        const pClass = player.class || 'Warrior';
        const level = player.level || 1;
        const depth = player.depth || 0;
        const maxDepth = player.max_depth || depth;
        const gold = player.gold || 0;
        const exp = player.exp || 0;
        const diedFrom = player.died_from || 'mortal wounds';

        if (this.deathEpitaph) {
            this.deathEpitaph.textContent = `${name} the ${race} ${pClass} (Level ${level})`;
        }
        if (this.deathCause) {
            const depthStr = depth === 0 ? 'in the Town' : `on Dungeon Level ${depth} (${depth * 50} ft)`;
            this.deathCause.textContent = `Killed by ${diedFrom} ${depthStr}.`;
        }
        if (this.deathScore) {
            this.deathScore.textContent = `Gold: ${gold.toLocaleString()} AU  •  Experience: ${exp.toLocaleString()}  •  Deepest Level: ${maxDepth} (${maxDepth * 50} ft)`;
        }

        // Update tab header badge counts
        const eqCount = (player.equipment && Array.isArray(player.equipment)) ? player.equipment.length : 0;
        const invCount = (player.inventory && Array.isArray(player.inventory)) ? player.inventory.length : 0;
        const quivCount = (player.quiver && Array.isArray(player.quiver)) ? player.quiver.length : 0;

        if (this.deathTabs[1]) this.deathTabs[1].textContent = `2. Equipment (${eqCount})`;
        if (this.deathTabs[2]) this.deathTabs[2].textContent = `3. Inventory (${invCount})`;
        if (this.deathTabs[3]) this.deathTabs[3].textContent = `4. Quiver (${quivCount})`;

        this.renderDeathTabContent(this.activeDeathTab, frame);
    }

    renderDeathTabContent(tabIdx, frame) {
        const player = frame.player || {};

        if (tabIdx === 0) {
            // Tab 0: Tombstone Terminal Canvas
            const termCanvas = document.getElementById('death-terminal-canvas');
            if (termCanvas && frame.term) {
                if (!this.deathTerminal) {
                    this.deathTerminal = new WebTerminal('death-terminal-canvas', (key) => {
                        if (window.__app && window.__app.network) {
                            window.__app.network.sendKey(key);
                        }
                    });
                }
                this.deathTerminal.render(frame.term);
            }
        } else if (tabIdx === 1) {
            // Tab 1: Equipment List
            const list = document.getElementById('death-equipment-list');
            if (list) {
                list.innerHTML = '';
                const eq = player.equipment || [];
                if (eq.length === 0) {
                    list.innerHTML = '<li class="death-list-empty">(No equipment worn at death)</li>';
                } else {
                    eq.forEach(item => {
                        const li = document.createElement('li');
                        li.className = 'death-list-item';
                        const prefix = item.mention || item.slot_name || '';
                        li.innerHTML = `<span class="item-slot">${prefix}</span><span class="item-name">${item.name || ''}</span><span class="item-weight">${item.weight ? (item.weight / 10).toFixed(1) + ' lbs' : ''}</span>`;
                        list.appendChild(li);
                    });
                }
            }
        } else if (tabIdx === 2) {
            // Tab 2: Inventory List
            const list = document.getElementById('death-inventory-list');
            if (list) {
                list.innerHTML = '';
                const inv = player.inventory || [];
                if (inv.length === 0) {
                    list.innerHTML = '<li class="death-list-empty">(Inventory was empty)</li>';
                } else {
                    inv.forEach((item, idx) => {
                        const li = document.createElement('li');
                        li.className = 'death-list-item';
                        const letter = String.fromCharCode(97 + idx); // a, b, c...
                        li.innerHTML = `<span class="item-slot">${letter})</span><span class="item-name">${item.name || ''}</span><span class="item-weight">${item.weight ? (item.weight / 10).toFixed(1) + ' lbs' : ''}</span>`;
                        list.appendChild(li);
                    });
                }
            }
        } else if (tabIdx === 3) {
            // Tab 3: Quiver List
            const list = document.getElementById('death-quiver-list');
            if (list) {
                list.innerHTML = '';
                const quiv = player.quiver || [];
                if (quiv.length === 0) {
                    list.innerHTML = '<li class="death-list-empty">(Quiver was empty)</li>';
                } else {
                    quiv.forEach((item, idx) => {
                        const li = document.createElement('li');
                        li.className = 'death-list-item';
                        li.innerHTML = `<span class="item-slot">${idx})</span><span class="item-name">${item.name || ''}</span><span class="item-weight">${item.weight ? (item.weight / 10).toFixed(1) + ' lbs' : ''}</span>`;
                        list.appendChild(li);
                    });
                }
            }
        } else if (tabIdx === 4) {
            // Tab 4: Character Attributes & Statistics Grid
            const container = document.getElementById('death-stats-container');
            if (container) {
                const getStat = s => (s && typeof s === 'object') ? (s.use || s.top || '10') : (s || '10');
                const stats = player.stats || {};
                container.innerHTML = `
                    <div class="death-stat-card"><div class="stat-name">STRENGTH</div><div class="stat-num">${getStat(stats.str)}</div></div>
                    <div class="death-stat-card"><div class="stat-name">INTELLIGENCE</div><div class="stat-num">${getStat(stats.int)}</div></div>
                    <div class="death-stat-card"><div class="stat-name">WISDOM</div><div class="stat-num">${getStat(stats.wis)}</div></div>
                    <div class="death-stat-card"><div class="stat-name">DEXTERITY</div><div class="stat-num">${getStat(stats.dex)}</div></div>
                    <div class="death-stat-card"><div class="stat-name">CONSTITUTION</div><div class="stat-num">${getStat(stats.con)}</div></div>
                    <div class="death-stat-card"><div class="stat-name">ARMOR CLASS</div><div class="stat-num">${player.ac || 0}</div></div>
                    <div class="death-stat-card"><div class="stat-name">SPEED</div><div class="stat-num">${player.speed || 110}</div></div>
                    <div class="death-stat-card"><div class="stat-name">DEEP LEVEL</div><div class="stat-num">${player.max_depth || player.depth || 0} (${(player.max_depth || player.depth || 0) * 50}ft)</div></div>
                `;
            }
        }
    }

    setPing(ms) {
        if (this.pingBadge) {
            this.pingBadge.textContent = `Cloud [${ms}ms]`;
        }
    }
}

window.WebHUD = WebHUD;
