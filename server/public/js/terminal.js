/**
 * Angband3D Web Terminal — High-DPI Vector Canvas Terminal
 * Renders Angband's 80x24 character grid with 100% pixel-perfect crispness at any resolution.
 * Supports exact 2-hex attribute color parsing and mouse-click selection.
 */

class WebTerminal {
    constructor(canvasId, onSelectKey) {
        this.canvas = document.getElementById(canvasId);
        this.ctx = this.canvas.getContext('2d');
        this.onSelectKey = onSelectKey || (() => {});

        this.cols = 80;
        this.rows = 24;

        // Accurate Angband 16-color palette
        this.palette = [
            '#1a1d24', // 0: Dark Black/Background
            '#ffffff', // 1: White
            '#9ea3ad', // 2: Grey / Slate
            '#ff9900', // 3: Orange (peaks / flames)
            '#e63946', // 4: Red
            '#2a9d8f', // 5: Green
            '#457b9d', // 6: Blue
            '#a3704c', // 7: Umber / Brown
            '#555a64', // 8: Dark Grey
            '#c8d1dc', // 9: Light Slate
            '#b5179e', // 10: Violet
            '#ffd166', // 11: Yellow
            '#ff6b6b', // 12: Light Red
            '#06d6a0', // 13: Light Green
            '#4cc9f0', // 14: Light Blue
            '#e0a96d'  // 15: Light Umber
        ];

        this.lastRows = [];
        this.resize();
        window.addEventListener('resize', () => this.resize());
        this.setupMouseEvents();
    }

    resize() {
        // Compute crisp font size based on available viewport width/height
        const maxW = Math.min(window.innerWidth * 0.95, 1200);
        const maxH = Math.min(window.innerHeight * 0.88, 750);

        // Optimal cell dimensions maintaining 80x24 aspect ratio
        const cellW = Math.floor(maxW / this.cols);
        const cellH = Math.floor(maxH / this.rows);
        this.cellSize = Math.max(12, Math.min(cellW, Math.floor(cellH * 0.58)));

        this.charWidth = this.cellSize;
        this.charHeight = Math.floor(this.cellSize * 1.75);
        this.fontSize = Math.floor(this.charHeight * 0.82);

        const dpr = Math.min(window.devicePixelRatio || 1, 2);
        this.canvas.width = this.cols * this.charWidth * dpr;
        this.canvas.height = this.rows * this.charHeight * dpr;

        this.canvas.style.width = `${this.cols * this.charWidth}px`;
        this.canvas.style.height = `${this.rows * this.charHeight}px`;

        this.ctx.scale(dpr, dpr);

        if (this.lastTermData) {
            this.render(this.lastTermData);
        }
    }

    setupMouseEvents() {
        this.canvas.addEventListener('click', (e) => {
            const rect = this.canvas.getBoundingClientRect();
            const clickX = e.clientX - rect.left;
            const clickY = e.clientY - rect.top;

            const col = Math.floor(clickX / this.charWidth);
            const row = Math.floor(clickY / this.charHeight);

            this.handleRowClick(row, col);
        });
    }

    handleRowClick(row, col) {
        if (!this.lastRows || !this.lastRows[row]) {
            // Clicking anywhere advances if on a prompt screen
            this.onSelectKey('enter');
            return;
        }

        const line = this.lastRows[row].g || '';

        // If screen says "Press any key to continue" or "-more-", advance
        if (line.toLowerCase().includes('press any key') || line.toLowerCase().includes('-more-')) {
            this.onSelectKey('enter');
            return;
        }

        // Check if row has a letter or symbol menu item e.g. "a) Human", "@) Random", "*) All"
        const match = line.match(/^\s*([a-zA-Z0-9@*?])[\)\.\:]/);
        if (match) {
            this.onSelectKey(match[1]);
            return;
        }

        // Check if row indicates random selection with @
        if (line.match(/^\s*@\b/) || line.includes('@ to generate') || line.includes('@ for random') || line.includes('@) Random')) {
            this.onSelectKey('@');
            return;
        }

        if (line.includes('[y/n]') || line.toLowerCase().includes('are you sure')) {
            this.onSelectKey('y');
            return;
        }

        // Default click advances
        this.onSelectKey('enter');
    }

    parseAttr(aStr, cellIdx) {
        if (!aStr) return 1;
        const idx = cellIdx * 2;
        if (idx + 1 >= aStr.length) return 1;
        const hi = parseInt(aStr[idx], 16) || 0;
        const lo = parseInt(aStr[idx + 1], 16) || 0;
        const val = (hi << 4) | lo;
        return val & 0x0f; // Low 4 bits is palette index 0-15
    }

    render(termData) {
        if (!termData || !termData.rows) return;
        this.lastTermData = termData;
        this.lastRows = termData.rows;

        const w = this.cols * this.charWidth;
        const h = this.rows * this.charHeight;

        // Clear canvas with deep dark background
        this.ctx.fillStyle = '#080a0f';
        this.ctx.fillRect(0, 0, w, h);

        // Ornate border
        this.ctx.strokeStyle = 'rgba(212, 175, 55, 0.4)';
        this.ctx.lineWidth = 2;
        this.ctx.strokeRect(1, 1, w - 2, h - 2);

        this.ctx.font = `${this.fontSize}px 'Fira Code', 'Courier New', monospace`;
        this.ctx.textBaseline = 'top';

        for (let r = 0; r < termData.rows.length && r < this.rows; r++) {
            const rowObj = termData.rows[r];
            const text = rowObj.g || '';
            const attrs = rowObj.a || '';
            const y = (rowObj.y !== undefined ? rowObj.y : r) * this.charHeight;

            for (let c = 0; c < text.length && c < this.cols; c++) {
                const ch = text[c];
                if (ch === ' ' || ch === '\0') continue;

                const colorIdx = this.parseAttr(attrs, c);
                this.ctx.fillStyle = this.palette[colorIdx] || '#ffffff';
                this.ctx.fillText(ch, c * this.charWidth + 1, y + 2);
            }
        }
    }
}

window.WebTerminal = WebTerminal;
