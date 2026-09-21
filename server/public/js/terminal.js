/**
 * Angband3D Web Terminal — Canvas 80x24 ANSI/Angband Terminal
 * Renders character creation, store inventories, help manuals, and prompt screens.
 * Supports mouse-click menu selection and crisp pixel-perfect scaling.
 */

class WebTerminal {
    constructor(canvasId, onSelectKey) {
        this.canvas = document.getElementById(canvasId);
        this.ctx = this.canvas.getContext('2d');
        this.onSelectKey = onSelectKey || (() => {});

        this.cols = 80;
        this.rows = 24;
        this.charWidth = 10;
        this.charHeight = 18;

        this.canvas.width = this.cols * this.charWidth;
        this.canvas.height = this.rows * this.charHeight;

        // Angband 16-color palette
        this.palette = [
            '#000000', // 0: Black
            '#ffffff', // 1: White
            '#8a8a8a', // 2: Grey
            '#ff8800', // 3: Orange
            '#cc0000', // 4: Red
            '#009944', // 5: Green
            '#0055ff', // 6: Blue
            '#995500', // 7: Umber / Brown
            '#444444', // 8: Dark Grey
            '#bbbbbb', // 9: Light Grey
            '#cc00cc', // 10: Violet
            '#ffff00', // 11: Yellow
            '#ff4444', // 12: Light Red
            '#00ff00', // 13: Light Green
            '#00ccff', // 14: Light Blue
            '#cc8844'  // 15: Light Umber
        ];

        this.lastRows = [];
        this.setupMouseEvents();
    }

    setupMouseEvents() {
        this.canvas.addEventListener('click', (e) => {
            const rect = this.canvas.getBoundingClientRect();
            const scaleX = this.canvas.width / rect.width;
            const scaleY = this.canvas.height / rect.height;

            const clickX = (e.clientX - rect.left) * scaleX;
            const clickY = (e.clientY - rect.top) * scaleY;

            const col = Math.floor(clickX / this.charWidth);
            const row = Math.floor(clickY / this.charHeight);

            this.handleRowClick(row, col);
        });
    }

    handleRowClick(row, col) {
        if (!this.lastRows || !this.lastRows[row]) return;
        const line = this.lastRows[row].g || '';
        if (!line) return;

        // Check if row has a letter menu item like "a) ..." or "1) ..."
        const match = line.match(/^\s*([a-zA-Z0-9])[\)\.\:]/);
        if (match) {
            const key = match[1];
            this.onSelectKey(key);
            return;
        }

        // Check if there's a bracketed prompt e.g. [y/n]
        if (line.includes('[y/n]')) {
            this.onSelectKey('y');
            return;
        }

        // Otherwise default to Enter/confirm if clicked on prompt
        if (line.includes('-more-') || line.includes('press any key')) {
            this.onSelectKey('enter');
        }
    }

    render(termData) {
        if (!termData || !termData.rows) return;
        this.lastRows = termData.rows;

        // Clear canvas
        this.ctx.fillStyle = '#06070a';
        this.ctx.fillRect(0, 0, this.canvas.width, this.canvas.height);

        this.ctx.font = '15px "Fira Code", monospace';
        this.ctx.textBaseline = 'top';

        for (let r = 0; r < termData.rows.length; r++) {
            const rowObj = termData.rows[r];
            const text = rowObj.g || '';
            const attrs = rowObj.a || '';
            const y = (rowObj.y !== undefined ? rowObj.y : r) * this.charHeight;

            for (let c = 0; c < text.length; c++) {
                const ch = text[c];
                if (ch === ' ' || ch === '\0') continue;

                let colorIdx = 1; // Default white
                if (typeof attrs === 'string' && c < attrs.length) {
                    const code = attrs.charCodeAt(c);
                    // Standard Angband bridge: attribute byte is index or char
                    colorIdx = code < 16 ? code : (code - 48);
                    if (colorIdx < 0 || colorIdx >= 16) colorIdx = 1;
                } else if (Array.isArray(attrs) && c < attrs.length) {
                    colorIdx = attrs[c] % 16;
                }

                this.ctx.fillStyle = this.palette[colorIdx];
                this.ctx.fillText(ch, c * this.charWidth, y + 2);
            }
        }
    }
}

window.WebTerminal = WebTerminal;
