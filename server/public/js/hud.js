/**
 * Angband3D HUD — Glassmorphic Fantasy User Interface & 2D Minimap Radar
 */

class WebHUD {
    constructor(minimapId) {
        this.minimapCanvas = document.getElementById(minimapId);
        this.minimapCtx = this.minimapCanvas.getContext('2d');
        this.minimapCanvas.width = 160;
        this.minimapCanvas.height = 160;

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

        this.messageText = document.getElementById('message-text');
        this.promptBar = document.getElementById('prompt-bar');
        this.promptText = document.getElementById('prompt-text');

        this.messages = [];
    }

    update(frame) {
        if (!frame) return;

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
        const curHp = player.hp !== undefined ? player.hp : 100;
        const maxHp = player.max_hp !== undefined ? player.max_hp : 100;
        const hpPct = Math.max(0, Math.min(100, (curHp / Math.max(1, maxHp)) * 100));
        this.hpFill.style.width = `${hpPct}%`;
        this.hpText.textContent = `${curHp} / ${maxHp}`;

        // SP Bar
        const curSp = player.sp !== undefined ? player.sp : 0;
        const maxSp = player.max_sp !== undefined ? player.max_sp : 0;
        const spPct = maxSp > 0 ? Math.max(0, Math.min(100, (curSp / maxSp) * 100)) : 0;
        this.spFill.style.width = `${spPct}%`;
        this.spText.textContent = `${curSp} / ${maxSp}`;

        // Stats
        if (player.stats) {
            this.statStr.textContent = player.stats.str || '10';
            this.statInt.textContent = player.stats.int || '10';
            this.statWis.textContent = player.stats.wis || '10';
            this.statDex.textContent = player.stats.dex || '10';
            this.statCon.textContent = player.stats.con || '10';
        }

        // Depth, Gold, AC
        const depth = player.depth !== undefined ? player.depth : 0;
        this.depthVal.textContent = depth === 0 ? 'Town' : `${depth * 50}' (L${depth})`;
        this.goldVal.textContent = (player.gold || 0).toLocaleString();
        this.acVal.textContent = player.ac !== undefined ? player.ac : '0';

        // Messages
        if (frame.messages && frame.messages.length > 0) {
            const last = frame.messages[frame.messages.length - 1];
            if (last && last.text) {
                this.messageText.textContent = last.text;
            }
        }

        // Context Prompts (e.g. -more-, [y/n])
        if (ui.more) {
            this.promptBar.style.display = 'block';
            this.promptText.textContent = '[-more-] Press Space or Enter';
        } else {
            this.promptBar.style.display = 'none';
        }

        // Minimap Radar
        if (frame.map && frame.player) {
            this.renderMinimap(frame.map, frame.player, frame.monsters || []);
        }
    }

    renderMinimap(map, player, monsters) {
        const ctx = this.minimapCtx;
        const w = this.minimapCanvas.width;
        const h = this.minimapCanvas.height;

        ctx.fillStyle = '#0a0d14';
        ctx.fillRect(0, 0, w, h);

        const radius = 18;
        const cellSize = w / (radius * 2);

        const px = player.x;
        const py = player.y;

        for (let dy = -radius; dy <= radius; dy++) {
            for (let dx = -radius; dx <= radius; dx++) {
                const mx = px + dx;
                const my = py + dy;
                if (mx < 0 || mx >= map.w || my < 0 || my >= map.h) continue;

                const cell = map.cells[my * map.w + mx];
                if (!cell || (!cell.k && !cell.v)) continue;

                const sx = (dx + radius) * cellSize;
                const sy = (dy + radius) * cellSize;

                const feat = cell.f;
                if (feat >= 17 && feat <= 22) {
                    ctx.fillStyle = '#444c5c'; // Wall
                } else if (feat === 1) {
                    ctx.fillStyle = '#1e2533'; // Floor
                } else if (feat === 2) {
                    ctx.fillStyle = '#a06020'; // Closed door
                } else if (feat === 3 || feat === 4) {
                    ctx.fillStyle = '#604010'; // Open door
                } else if (feat === 5 || feat === 6) {
                    ctx.fillStyle = '#40a0ff'; // Stairs
                } else if (feat === 23) {
                    ctx.fillStyle = '#ff4400'; // Lava
                } else {
                    ctx.fillStyle = '#222';
                }

                ctx.fillRect(sx, sy, cellSize, cellSize);
            }
        }

        // Monsters
        monsters.forEach(m => {
            const mdx = m.x - px;
            const mdy = m.y - py;
            if (Math.abs(mdx) <= radius && Math.abs(mdy) <= radius) {
                const sx = (mdx + radius) * cellSize;
                const sy = (mdy + radius) * cellSize;
                ctx.fillStyle = '#ff3333';
                ctx.fillRect(sx + 1, sy + 1, cellSize - 2, cellSize - 2);
            }
        });

        // Player Center Glyph
        const cx = radius * cellSize;
        const cy = radius * cellSize;
        ctx.fillStyle = '#ffd700';
        ctx.beginPath();
        ctx.arc(cx + cellSize / 2, cy + cellSize / 2, cellSize * 0.75, 0, Math.PI * 2);
        ctx.fill();
    }

    setPing(ms) {
        if (this.pingBadge) {
            this.pingBadge.textContent = `Cloud [${ms}ms]`;
        }
    }
}

window.WebHUD = WebHUD;
