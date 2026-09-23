/**
 * Angband3D WebGL Renderer — High-Fidelity Three.js 3D Dungeon Crawler
 * Features:
 * - 1:1 Parity with Godot C# client's wire protocol and procedural textures (DungeonWorld.cs)
 * - Built-in zero-latency procedural PBR texture generation matching Godot client:
 *   - Stone wall textures with chiseled bevel lighting, dark mortar joints, block hue variation & normal maps
 *   - Flagstone floor textures with worn centers, mortar seams & normal maps
 *   - Subterranean rock ceiling textures & normal maps
 *   - Forged iron reinforced wooden door textures & normal maps
 *   - Half-timbered shop facades & 8 distinct Heraldic Shop Entrance doors with gilded rims & gold digits (1-8)
 *   - Magma fissures with pulsating incandescent veins & quartz crystal seams
 * - High-res 2K PBR texture streaming overlay from server assets
 * - Dynamic Town vs Subterranean Lighting:
 *   - Town (depth 0): Open daylight sky, Directional Sunlight (1.35), broad horizon, NO ceiling over streets!
 *   - Dungeon (depth > 0): True subterranean darkness, atmospheric depth fog, ceilings over tunnels, warm torchlight!
 * - Equipment Rule Parity with Godot ViewModel.cs:
 *   - Carrying a torch in the light slot illuminates the player without occupying their hands!
 *   - Left hand ONLY shows an equipped shield or wielded torch weapon. Empty by default!
 *   - Right hand ONLY shows an equipped melee weapon or bow, tucked cleanly in lower corner. Empty if unarmed!
 * - High-DPI 512x512 Runic Monster Medallions with health bars, glyphs, and nameplates
 * - 3D Item Pickups (Gold stacks, Potions, Scrolls, Weapons, Rings, Chests) with hovering bobbing kinematics
 * - 1:1 Camera Facing and Turning (0=North, 1=East, 2=South, 3=West) with smooth spring yaw rotation
 */

// ==============================================================================
// 1. Angband 32-Color Palette (1:1 with AngbandColors.cs)
// ==============================================================================

const ANGBAND_COLORS = [
    0x000000, // 0  dark
    0xffffff, // 1  white
    0x808080, // 2  slate
    0xff8000, // 3  orange
    0xc00000, // 4  red
    0x008c47, // 5  green
    0x0000ff, // 6  blue
    0x80590d, // 7  umber
    0x595959, // 8  light dark
    0xc0c0c0, // 9  light slate
    0x9900ff, // 10 light purple
    0xffff00, // 11 yellow
    0xff4040, // 12 light red
    0x00ff00, // 13 light green
    0x00ffff, // 14 light blue
    0xc4a161, // 15 light umber
    0x990099, // 16 purple
    0x99004d, // 17 violet
    0x009999, // 18 teal
    0x6b5933, // 19 mud
    0xffff99, // 20 light yellow
    0xe600e6, // 21 magenta
    0x33e6e6, // 22 light teal
    0xb366ff, // 23 light violet
    0xff66b3, // 24 light pink
    0xb89929, // 25 mustard
    0x708fb3, // 26 blue slate
    0x296bd1  // 27 deep light blue
];

function getAngbandHex(attr) {
    const idx = (attr !== undefined && attr >= 0 && attr < ANGBAND_COLORS.length) ? attr : 1;
    return ANGBAND_COLORS[idx];
}

function getAngbandColorString(attr) {
    const hex = getAngbandHex(attr).toString(16).padStart(6, '0');
    return '#' + hex;
}
window.getAngbandColorString = getAngbandColorString;

// ==============================================================================
// 2. Procedural Texture Engine (Exact Mathematical Parity with Godot C# DungeonWorld.cs)
// ==============================================================================

function procHash(x, y, seed = 0) {
    let n = (x + y * 57 + seed * 131) | 0;
    n = ((n << 13) ^ n) | 0;
    const inner = (Math.imul(Math.imul(n, n), 15731) + 789221) | 0;
    const val = (Math.imul(n, inner) + 1376312589) & 0x7fffffff;
    return (1.0 - val / 1073741824.0) * 0.5 + 0.5;
}

function procSmoothNoise(x, y, seed = 0) {
    const x0 = Math.floor(x) | 0;
    const y0 = Math.floor(y) | 0;
    const x1 = (x0 + 1) | 0;
    const y1 = (y0 + 1) | 0;
    const fx = x - x0;
    const fy = y - y0;
    const sx = fx * fx * (3.0 - 2.0 * fx);
    const sy = fy * fy * (3.0 - 2.0 * fy);
    const n00 = procHash(x0, y0, seed);
    const n10 = procHash(x1, y0, seed);
    const n01 = procHash(x0, y1, seed);
    const n11 = procHash(x1, y1, seed);
    const nx0 = n00 + (n10 - n00) * sx;
    const nx1 = n01 + (n11 - n01) * sx;
    return nx0 + (nx1 - nx0) * sy;
}

function procFractalNoise(x, y, octaves = 3, seed = 0) {
    let value = 0;
    let amplitude = 0.5;
    let frequency = 1.0;
    let maxVal = 0;
    for (let i = 0; i < octaves; i++) {
        value += procSmoothNoise(x * frequency, y * frequency, seed + i * 37) * amplitude;
        maxVal += amplitude;
        frequency *= 2.0;
        amplitude *= 0.5;
    }
    return value / maxVal;
}

function procCreateTexture(size, pixelFunc) {
    const canvas = document.createElement('canvas');
    canvas.width = size;
    canvas.height = size;
    const ctx = canvas.getContext('2d');
    const imgData = ctx.createImageData(size, size);
    const data = imgData.data;
    let idx = 0;
    for (let y = 0; y < size; y++) {
        for (let x = 0; x < size; x++) {
            const col = pixelFunc(x, y);
            data[idx++] = Math.min(255, Math.max(0, (col[0] * 255 + 0.5) | 0));
            data[idx++] = Math.min(255, Math.max(0, (col[1] * 255 + 0.5) | 0));
            data[idx++] = Math.min(255, Math.max(0, (col[2] * 255 + 0.5) | 0));
            data[idx++] = Math.min(255, Math.max(0, (col[3] !== undefined ? col[3] * 255 + 0.5 : 255) | 0));
        }
    }
    ctx.putImageData(imgData, 0, 0);
    const tex = new THREE.CanvasTexture(canvas);
    tex.wrapS = THREE.RepeatWrapping;
    tex.wrapT = THREE.RepeatWrapping;
    return tex;
}

const DIGIT_PATTERNS = [
    // 1
    [0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110],
    // 2
    [0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b01000, 0b11111],
    // 3
    [0b11110, 0b00001, 0b00001, 0b01110, 0b00001, 0b00001, 0b11110],
    // 4
    [0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010],
    // 5
    [0b11111, 0b10000, 0b11110, 0b00001, 0b00001, 0b10001, 0b01110],
    // 6
    [0b00110, 0b01000, 0b10000, 0b11110, 0b10001, 0b10001, 0b01110],
    // 7
    [0b11111, 0b00001, 0b00010, 0b00100, 0b00100, 0b01000, 0b01000],
    // 8
    [0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110]
];

function procStoreColor(shopNum) {
    switch (shopNum) {
        case 1: return [1.0, 0.88, 0.35]; // General Store: Warm Gold
        case 2: return [0.70, 0.90, 1.0];  // Armoury: Steel Cyan
        case 3: return [1.0, 0.60, 0.35];  // Weaponsmith: Fiery Orange
        case 4: return [0.55, 0.95, 0.65]; // Bookseller: Jade Green
        case 5: return [0.40, 1.0, 0.85];  // Alchemy: Mystic Emerald
        case 6: return [0.85, 0.65, 1.0];  // Magic: Arcane Violet
        case 7: return [1.0, 0.45, 0.65];  // Black Market: Crimson Rose
        case 8: return [1.0, 0.95, 0.70];  // Home: Cozy Amber
        default: return [1.0, 0.90, 0.50];
    }
}

function getStoreColorHex(shopNum) {
    const rgb = procStoreColor(shopNum);
    const r = Math.round(rgb[0] * 255);
    const g = Math.round(rgb[1] * 255);
    const b = Math.round(rgb[2] * 255);
    return `rgb(${r},${g},${b})`;
}

function procCreateStoneWallTexture() {
    const size = 512;
    const rowHeight = 256;
    const halfRow = 128;
    return procCreateTexture(size, (x, y) => {
        const row = Math.floor(y / rowHeight);
        const yInRow = y % rowHeight;
        const distY = Math.min(yInRow, rowHeight - 1 - yInRow);
        const xOff = (row % 2 === 1) ? halfRow : 0;
        const xInRow = (x + size - xOff) % rowHeight;
        const distX = Math.min(xInRow, rowHeight - 1 - xInRow);
        const mortarDist = Math.min(distX, distY);

        if (mortarDist <= 8) {
            const mn = procSmoothNoise(x * 0.125, y * 0.125, 101) * 0.04 - 0.02;
            return [0.12 + mn, 0.12 + mn, 0.14 + mn];
        }

        const blockId = row * 2 + Math.floor((x + size - xOff) / rowHeight);
        const blockHue = procHash(blockId, 0, 77);
        const rBase = 0.44 + (blockHue - 0.5) * 0.05;
        const gBase = 0.44 + (blockHue - 0.5) * 0.04;
        const bBase = 0.46 + (0.5 - blockHue) * 0.04;

        const bevelX = (xInRow < halfRow) ? (xInRow - 8) / 32.0 : (rowHeight - 9 - xInRow) / 32.0;
        const bevelY = (yInRow < halfRow) ? (yInRow - 8) / 32.0 : (rowHeight - 9 - yInRow) / 32.0;
        const bevel = Math.max(0, Math.min(1.0, Math.min(bevelX, bevelY)));
        const lightGradient = ((halfRow - xInRow) + (halfRow - yInRow)) * 0.0004;

        const grain = (procSmoothNoise(x * 0.06, y * 0.06, 1) - 0.5) * 0.08;
        const microGrain = (procSmoothNoise(x * 0.18, y * 0.18, 2) - 0.5) * 0.04;
        const chisel = (procSmoothNoise(x * 0.18, y * 0.04, 14) - 0.5) * 0.03;

        const r = Math.max(0, Math.min(1, rBase * (0.80 + bevel * 0.20) + lightGradient + grain + microGrain + chisel));
        const g = Math.max(0, Math.min(1, gBase * (0.80 + bevel * 0.20) + lightGradient + grain + microGrain + chisel));
        const b = Math.max(0, Math.min(1, bBase * (0.80 + bevel * 0.20) + lightGradient + grain + microGrain + chisel));
        return [r, g, b];
    });
}

function procCreateStoneWallNormal() {
    const size = 512;
    const rowHeight = 256;
    const halfRow = 128;
    return procCreateTexture(size, (x, y) => {
        const row = Math.floor(y / rowHeight);
        const yInRow = y % rowHeight;
        const distY = Math.min(yInRow, rowHeight - 1 - yInRow);
        const xOff = (row % 2 === 1) ? halfRow : 0;
        const xInRow = (x + size - xOff) % rowHeight;
        const distX = Math.min(xInRow, rowHeight - 1 - xInRow);
        const mortarDist = Math.min(distX, distY);

        if (mortarDist <= 8) return [0.5, 0.5, 1.0];

        let dx = (xInRow < halfRow) ? (1.0 - xInRow / 32.0) : -(1.0 - (rowHeight - 1 - xInRow) / 32.0);
        let dy = (yInRow < halfRow) ? (1.0 - yInRow / 32.0) : -(1.0 - (rowHeight - 1 - yInRow) / 32.0);
        dx = Math.max(-1, Math.min(1, dx));
        dy = Math.max(-1, Math.min(1, dy));

        const microBump = (procSmoothNoise(x * 0.08, y * 0.08, 50) - 0.5) * 0.15;
        const fineBump = (procSmoothNoise(x * 0.22, y * 0.22, 51) - 0.5) * 0.08;
        const nx = (dx + microBump + fineBump) * 0.55;
        const ny = -(dy + microBump + fineBump) * 0.55;
        const nz = 1.0;
        const len = Math.hypot(nx, ny, nz);
        return [nx / len * 0.5 + 0.5, ny / len * 0.5 + 0.5, nz / len * 0.5 + 0.5];
    });
}

function procCreateFloorTexture() {
    const size = 512;
    const tileSize = 256;
    return procCreateTexture(size, (x, y) => {
        const tileX = Math.floor(x / tileSize);
        const tileY = Math.floor(y / tileSize);
        const inTileX = x % tileSize;
        const inTileY = y % tileSize;
        const distX = Math.min(inTileX, tileSize - 1 - inTileX);
        const distY = Math.min(inTileY, tileSize - 1 - inTileY);
        const mortarDist = Math.min(distX, distY);

        if (mortarDist <= 8) {
            const mn = procSmoothNoise(x * 0.125, y * 0.125, 202) * 0.03 - 0.015;
            return [0.10 + mn, 0.10 + mn, 0.11 + mn];
        }

        const tileId = tileY * 2 + tileX;
        const tileHue = procHash(tileId, 0, 99);
        const rBase = 0.38 + (tileHue - 0.5) * 0.04;
        const gBase = 0.39 + (tileHue - 0.5) * 0.03;
        const bBase = 0.41 + (0.5 - tileHue) * 0.03;

        const bevel = Math.max(0.75, Math.min(1.0, (mortarDist - 8) / 32.0));
        const grain = (procSmoothNoise(x * 0.06, y * 0.06, 3) - 0.5) * 0.06;
        const microGrain = (procSmoothNoise(x * 0.18, y * 0.18, 4) - 0.5) * 0.03;

        const r = Math.max(0, Math.min(1, rBase * bevel + grain + microGrain));
        const g = Math.max(0, Math.min(1, gBase * bevel + grain + microGrain));
        const b = Math.max(0, Math.min(1, bBase * bevel + grain + microGrain));
        return [r, g, b];
    });
}

function procCreateFloorNormal() {
    const size = 512;
    const tileSize = 256;
    return procCreateTexture(size, (x, y) => {
        const inTileX = x % tileSize;
        const inTileY = y % tileSize;
        const distX = Math.min(inTileX, tileSize - 1 - inTileX);
        const distY = Math.min(inTileY, tileSize - 1 - inTileY);
        const mortarDist = Math.min(distX, distY);

        if (mortarDist <= 8) return [0.5, 0.5, 1.0];

        let dx = (inTileX < tileSize / 2) ? (1.0 - inTileX / 36.0) : -(1.0 - (tileSize - 1 - inTileX) / 36.0);
        let dy = (inTileY < tileSize / 2) ? (1.0 - inTileY / 36.0) : -(1.0 - (tileSize - 1 - inTileY) / 36.0);
        dx = Math.max(-1, Math.min(1, dx));
        dy = Math.max(-1, Math.min(1, dy));

        const microBump = (procSmoothNoise(x * 0.08, y * 0.08, 52) - 0.5) * 0.12;
        const nx = (dx + microBump) * 0.45;
        const ny = -(dy + microBump) * 0.45;
        const nz = 1.0;
        const len = Math.hypot(nx, ny, nz);
        return [nx / len * 0.5 + 0.5, ny / len * 0.5 + 0.5, nz / len * 0.5 + 0.5];
    });
}

function procCreateShopDoorTexture(shopNum, heraldicColor) {
    const size = 512;
    const plankWidth = 128;
    const pattern = (shopNum >= 1 && shopNum <= 8) ? DIGIT_PATTERNS[shopNum - 1] : null;

    return procCreateTexture(size, (x, y) => {
        const plankIdx = Math.floor(x / plankWidth);
        const inPlankX = x % plankWidth;
        const seamDist = Math.min(inPlankX, plankWidth - 1 - inPlankX);

        // Horizontal forged-iron reinforcement straps
        const isIronStrap = (y >= 88 && y <= 124) || (y >= 388 && y <= 424);
        if (isIronStrap) {
            const rivetX = inPlankX - plankWidth / 2;
            const rivetY = (y < 200) ? (y - 106) : (y - 406);
            const isRivet = rivetX * rivetX + rivetY * rivetY <= 64;
            if (isRivet) return [0.48, 0.48, 0.52];
            const ironGrain = (procSmoothNoise(x * 0.1, y * 0.1, 31) - 0.5) * 0.04;
            return [0.20 + ironGrain, 0.20 + ironGrain, 0.22 + ironGrain];
        }

        // Central Emblazoned Heraldic Shield at (256, 256)
        const dx = x - 256;
        const dy = y - 256;
        const absDx = Math.abs(dx);

        const inShield = (dy >= -92 && dy <= 0 && absDx <= 88) ||
                         (dy > 0 && dy <= 100 && absDx <= 88 - (dy * dy) / 116.0);

        if (inShield) {
            const isShieldRim = (dy <= -84 || absDx >= 80 || (dy > 0 && absDx >= 80 - (dy * dy) / 116.0));
            if (isShieldRim) {
                const bevel = (dx < 0 || dy < -76) ? 0.14 : -0.10;
                return [0.88 + bevel, 0.74 + bevel, 0.28 + bevel];
            }

            if (pattern) {
                const gx = Math.floor((x - 226) / 12);
                const gy = Math.floor((y - 214) / 12);
                if (gx >= 0 && gx < 5 && gy >= 0 && gy < 7) {
                    const isBit = (pattern[gy] & (1 << (4 - gx))) !== 0;
                    if (isBit) return [1.0, 0.90, 0.35]; // Gold digit
                }
            }

            const bgR = Math.max(0, Math.min(1, heraldicColor[0] * 0.48 + 0.10));
            const bgG = Math.max(0, Math.min(1, heraldicColor[1] * 0.48 + 0.08));
            const bgB = Math.max(0, Math.min(1, heraldicColor[2] * 0.48 + 0.06));
            return [bgR, bgG, bgB];
        }

        if (seamDist <= 4) return [0.10, 0.07, 0.04];

        const woodGrain = (procSmoothNoise(x * 0.08, y * 0.04, 55) - 0.5) * 0.04;
        const plankHue = procHash(plankIdx, 0, 88 + shopNum);
        const r = Math.max(0, Math.min(1, 0.32 + (plankHue - 0.5) * 0.03 + woodGrain));
        const g = Math.max(0, Math.min(1, 0.22 + (plankHue - 0.5) * 0.02 + woodGrain * 0.8));
        const b = Math.max(0, Math.min(1, 0.14 + (plankHue - 0.5) * 0.02 + woodGrain * 0.5));
        return [r, g, b];
    });
}

function procCreateWoodDoorTexture() {
    const size = 512;
    const plankWidth = 128;
    return procCreateTexture(size, (x, y) => {
        const plankIdx = Math.floor(x / plankWidth);
        const inPlankX = x % plankWidth;
        const seamDist = Math.min(inPlankX, plankWidth - 1 - inPlankX);

        const isIronStrap = (y >= 100 && y <= 136) || (y >= 376 && y <= 412);
        if (isIronStrap) {
            const rivetX = inPlankX - plankWidth / 2;
            const rivetY = (y < 200) ? (y - 118) : (y - 394);
            const isRivet = rivetX * rivetX + rivetY * rivetY <= 64;
            if (isRivet) return [0.44, 0.44, 0.48];
            const ironGrain = (procSmoothNoise(x * 0.1, y * 0.1, 31) - 0.5) * 0.04;
            return [0.20 + ironGrain, 0.20 + ironGrain, 0.22 + ironGrain];
        }

        if (seamDist <= 4) return [0.10, 0.07, 0.04];

        const woodGrain = (procSmoothNoise(x * 0.08, y * 0.04, 55) - 0.5) * 0.04;
        const plankHue = procHash(plankIdx, 0, 88);
        const r = Math.max(0, Math.min(1, 0.34 + (plankHue - 0.5) * 0.03 + woodGrain));
        const g = Math.max(0, Math.min(1, 0.24 + (plankHue - 0.5) * 0.02 + woodGrain * 0.8));
        const b = Math.max(0, Math.min(1, 0.15 + (plankHue - 0.5) * 0.02 + woodGrain * 0.5));
        return [r, g, b];
    });
}

function procCreateStoreTexture() {
    const size = 512;
    return procCreateTexture(size, (x, y) => {
        const isBorderBeam = x < 36 || x > 475 || y < 36 || y > 475;
        const isCrossBeam = Math.abs(x - 256) < 20 || Math.abs(y - 256) < 20;
        const isDiagonal = Math.abs((x - y) % 256) < 16 || Math.abs((x + y) % 256) < 16;

        if (isBorderBeam || isCrossBeam || isDiagonal) {
            const woodGrain = (procSmoothNoise(x * 0.08, y * 0.08, 12) - 0.5) * 0.04;
            return [0.24 + woodGrain, 0.16 + woodGrain * 0.7, 0.11 + woodGrain * 0.5];
        }

        const plasterGrain = (procSmoothNoise(x * 0.08, y * 0.08, 22) - 0.5) * 0.04;
        return [0.65 + plasterGrain, 0.62 + plasterGrain, 0.56 + plasterGrain];
    });
}

function procCreateCeilingTexture() {
    const size = 512;
    return procCreateTexture(size, (x, y) => {
        const grain = (procSmoothNoise(x * 0.06, y * 0.06, 5) - 0.5) * 0.06;
        const microGrain = (procSmoothNoise(x * 0.18, y * 0.18, 6) - 0.5) * 0.03;
        const r = Math.max(0, Math.min(1, 0.22 + grain + microGrain));
        const g = Math.max(0, Math.min(1, 0.23 + grain + microGrain));
        const b = Math.max(0, Math.min(1, 0.25 + grain + microGrain));
        return [r, g, b];
    });
}

function procCreateMagmaTexture() {
    const size = 512;
    return procCreateTexture(size, (x, y) => {
        const vein = (procFractalNoise(x * 0.02, y * 0.02, 4, 77) - 0.5) * 2.0;
        if (Math.abs(vein) < 0.24) {
            const heat = 1.0 - Math.min(1, Math.abs(vein) / 0.24);
            return [0.94 + heat * 0.06, 0.32 + heat * 0.44, 0.04 + heat * 0.12];
        }
        const rockGrain = (procSmoothNoise(x * 0.08, y * 0.08, 88) - 0.5) * 0.05;
        return [0.24 + rockGrain, 0.22 + rockGrain, 0.24 + rockGrain];
    });
}

function procCreateMagmaEmission() {
    const size = 512;
    return procCreateTexture(size, (x, y) => {
        const vein = (procFractalNoise(x * 0.02, y * 0.02, 4, 77) - 0.5) * 2.0;
        if (Math.abs(vein) < 0.24) {
            const heat = 1.0 - Math.min(1, Math.abs(vein) / 0.24);
            return [0.96, 0.34 + heat * 0.40, 0.04 + heat * 0.12];
        }
        return [0, 0, 0];
    });
}

function procCreateQuartzTexture() {
    const size = 512;
    return procCreateTexture(size, (x, y) => {
        const crystalVein = (procFractalNoise((x * 1.2 - y * 0.8) * 0.025, (x * 0.5 + y) * 0.025, 4, 44) - 0.5) * 2.0;
        if (Math.abs(crystalVein) < 0.22) {
            const glint = procSmoothNoise(x * 0.2, y * 0.2, 12) * 0.12;
            return [0.74 + glint, 0.80 + glint, 0.86 + glint];
        }
        const grain = (procSmoothNoise(x * 0.08, y * 0.08, 9) - 0.5) * 0.06;
        return [0.40 + grain, 0.41 + grain, 0.43 + grain];
    });
}

function procCreateOrganicNormal() {
    const size = 256;
    return procCreateTexture(size, (x, y) => {
        const nx = procSmoothNoise(x * 0.12, y * 0.12, 101);
        const ny = procSmoothNoise(x * 0.12, (y + 1) * 0.12, 101);
        const dx = (procSmoothNoise((x + 1) * 0.12, y * 0.12, 101) - nx) * 1.8;
        const dy = (ny - nx) * 1.8;
        return [0.5 + dx, 0.5 + dy, 1.0];
    });
}

function procCreateScaleNormal() {
    const size = 256;
    return procCreateTexture(size, (x, y) => {
        const u = (x % 32) - 16;
        const v = (y % 32) - 16;
        const dist = Math.sqrt(u * u + v * v) / 16.0;
        const h = Math.max(0, 1.0 - dist);
        const dx = (u / 16.0) * h * 0.8;
        const dy = (v / 16.0) * h * 0.8;
        return [0.5 - dx, 0.5 - dy, 1.0];
    });
}

function procCreateChitinNormal() {
    const size = 256;
    return procCreateTexture(size, (x, y) => {
        const segment = Math.sin(y * 0.15) * 0.25;
        const fineNoise = (procSmoothNoise(x * 0.25, y * 0.25, 202) - 0.5) * 0.15;
        return [0.5 + fineNoise, 0.5 + segment + fineNoise, 1.0];
    });
}

function procCreateFurNormal() {
    const size = 256;
    return procCreateTexture(size, (x, y) => {
        const strand = Math.sin((x + y * 0.3) * 0.8) * 0.3;
        const noise = (procSmoothNoise(x * 0.3, y * 0.08, 303) - 0.5) * 0.2;
        return [0.5 + strand, 0.5 + noise, 1.0];
    });
}

function createChamferedWallGeometry(cellSize, wallHeight, chamfer = 0.40) {
    const half = cellSize / 2;
    const c = chamfer;
    const shape = new THREE.Shape();
    // 8-sided polygon with 45-degree chamfers on the 4 vertical corners
    shape.moveTo(-half + c, -half);
    shape.lineTo(half - c, -half);
    shape.lineTo(half, -half + c);
    shape.lineTo(half, half - c);
    shape.lineTo(half - c, half);
    shape.lineTo(-half + c, half);
    shape.lineTo(-half, half - c);
    shape.lineTo(-half, -half + c);
    shape.closePath();

    const extrudeSettings = {
        depth: wallHeight,
        bevelEnabled: false
    };
    const geo = new THREE.ExtrudeGeometry(shape, extrudeSettings);
    geo.rotateX(Math.PI / 2);
    geo.center();
    return geo;
}

// ==============================================================================
// 2. Main 3D Dungeon Crawler Engine
// ==============================================================================

class Dungeon3D {
    constructor(canvasId) {
        this.canvas = document.getElementById(canvasId);
        this.cellSize = 2.0; // 2.0m per grid cell matching Godot DungeonWorld
        this.wallHeight = 3.0;
        this.eyeHeight = 1.62;

        // Facing: 0=North (-Z), 1=East (+X), 2=South (+Z), 3=West (-X)
        // 1:1 mathematical match with Godot DungeonWorld._facing and camera convention
        this.facing = 0;
        this.targetYaw = 0;
        this.basePitch = -9.0 * (Math.PI / 180.0);
        this.userPitchOffset = 0.0;
        this.targetPitch = this.basePitch;
        this.targetCamPos = new THREE.Vector3(0, this.eyeHeight, 0);
        this.currentCamPos = new THREE.Vector3(0, this.eyeHeight, 0);
        this.cameraBobPhase = 0;

        this.stepDuration = 0.14; // 140ms step tween
        this.stepTime = 0;
        this.isStepping = false;

        this.monsters = new Map();
        this.items = new Map();

        // Combat Juice & VFX Subsystems (Exact 1:1 Parity with Godot DungeonWorld.cs)
        this.floatingTexts = [];
        this.particles = [];
        this.trauma = 0.0;
        this.lastPlayerHp = null;
        this.processedMessages = new Set();
        this.attackAnimationTime = 0.0;
        this.hurtAnimationTime = 0.0;
        this.castAnimationTime = 0.0;
        this.lastDepth = null;
        this.audio = null;
        this.lastPlayerStatuses = new Set();

        // Zero-allocation scratch math objects for fast high-fps grid updates
        this._scratchWallColor = new THREE.Color();
        this._scratchFloorColor = new THREE.Color();
        this._scratchCeilingColor = new THREE.Color();
        this._tintDark = new THREE.Color(0.24, 0.26, 0.32);
        this._tintMemLit = new THREE.Color(0.55, 0.58, 0.65);
        this._tintMemDark = new THREE.Color(0.22, 0.24, 0.30);

        this.initThree();
        this.initTextures();
        this.initMeshes();
        this.initViewmodel();
        this.initModelResolvers();
        this.animate = this.animate.bind(this);
        requestAnimationFrame(this.animate);
    }

    initThree() {
        this.scene = new THREE.Scene();
        this.scene.background = new THREE.Color(0x2d3a50); // Initial town sky
        this.scene.fog = new THREE.FogExp2(0x2d3a50, 0.008);

        this.camera = new THREE.PerspectiveCamera(72, window.innerWidth / window.innerHeight, 0.1, 140);
        this.camera.position.set(0, this.eyeHeight, 0);
        this.camera.rotation.order = 'YXZ'; // Critical: Yaw first, then Pitch, then Roll (eliminates room skew / Dutch tilt)
        this.camera.rotation.y = 0; // Starts looking North (-Z)
        this.camera.rotation.x = 0;
        this.camera.rotation.z = 0;

        this.renderer = new THREE.WebGLRenderer({
            canvas: this.canvas,
            antialias: true,
            powerPreference: 'high-performance'
        });
        this.renderer.setSize(window.innerWidth, window.innerHeight);
        this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 1.5));
        this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
        this.renderer.toneMappingExposure = 1.05;
        this.renderer.outputEncoding = THREE.sRGBEncoding;

        // Ambient light (dynamically adjusted per depth / town)
        this.ambientLight = new THREE.AmbientLight(0x808aa8, 0.75);
        this.scene.add(this.ambientLight);

        // Directional Sunlight for outdoors (Town) matching Godot _sunLight
        this.sunLight = new THREE.DirectionalLight(0xb3ccfa, 0.95);
        this.sunLight.position.set(-20, 35, 15);
        this.scene.add(this.sunLight);

        // Player Torch PointLight - ATTACHED TO CAMERA (Matches Godot OmniAttenuation = 0.70f)
        this.torchLight = new THREE.PointLight(0xffdc99, 1.8, 16.0, 0.7);
        this.torchLight.position.set(-0.25, -0.05, -0.28);
        this.camera.add(this.torchLight);

        // Secondary warm bounce fill light near player feet - ATTACHED TO CAMERA
        this.torchFillLight = new THREE.PointLight(0xff7722, 0.35, 8.0, 0.7);
        this.torchFillLight.position.set(0.20, -0.20, -0.20);
        this.camera.add(this.torchFillLight);

        // Floating 3D Shop and Landmark Labels (Matches Godot _terrainLabels)
        this.terrainLabelsGroup = new THREE.Group();
        this.scene.add(this.terrainLabelsGroup);
        this.lastTerrainLabelsSignature = '';

        // Camera must be added to scene so camera children (torch, viewmodel) are in scene graph
        this.scene.add(this.camera);

        window.addEventListener('resize', () => {
            this.camera.aspect = window.innerWidth / window.innerHeight;
            this.camera.updateProjectionMatrix();
            this.renderer.setSize(window.innerWidth, window.innerHeight);
        });
    }

    getBiomeProfile(depth) {
        if (depth <= 0) {
            return {
                name: 'Town & Overworld',
                bgColor: 0x141e38,
                ambientColor: 0x808aa8,
                ambientEnergy: 0.75,
                sunLight: true,
                sunEnergy: 0.95,
                fogColor: 0x141e38,
                fogDensity: 0.004,
                exposure: 1.05,
                torchColor: 0xffdc99,
                torchEnergy: 1.8,
                wallColor: new THREE.Color(0.88, 0.88, 0.88),
                floorColor: new THREE.Color(0.84, 0.82, 0.80),
                ceilingColor: new THREE.Color(0.70, 0.70, 0.75),
                floorRoughness: 0.84
            };
        }
        if (depth <= 15) {
            return {
                name: 'Upper Crypts',
                bgColor: 0x040406,
                ambientColor: 0x626880,
                ambientEnergy: 0.42,
                sunLight: false,
                sunEnergy: 0.0,
                fogColor: 0x08080c,
                fogDensity: 0.010,
                exposure: 1.18,
                torchColor: 0xffdc99,
                torchEnergy: 3.5,
                wallColor: new THREE.Color(0.85, 0.85, 0.88),
                floorColor: new THREE.Color(0.78, 0.78, 0.80),
                ceilingColor: new THREE.Color(0.65, 0.65, 0.70),
                floorRoughness: 0.82
            };
        }
        if (depth <= 35) {
            return {
                name: 'Overgrown Catacombs',
                bgColor: 0x030504,
                ambientColor: 0x426147,
                ambientEnergy: 0.30,
                sunLight: false,
                sunEnergy: 0.0,
                fogColor: 0x061008,
                fogDensity: 0.014,
                exposure: 1.18,
                torchColor: 0xffe099,
                torchEnergy: 2.8,
                wallColor: new THREE.Color(0.72, 0.86, 0.70),
                floorColor: new THREE.Color(0.65, 0.78, 0.64),
                ceilingColor: new THREE.Color(0.52, 0.65, 0.50),
                floorRoughness: 0.65
            };
        }
        if (depth <= 60) {
            return {
                name: 'Crystal Caverns',
                bgColor: 0x030407,
                ambientColor: 0x425c85,
                ambientEnergy: 0.32,
                sunLight: false,
                sunEnergy: 0.0,
                fogColor: 0x060c18,
                fogDensity: 0.014,
                exposure: 1.20,
                torchColor: 0xfadc7b,
                torchEnergy: 2.8,
                wallColor: new THREE.Color(0.68, 0.78, 0.96),
                floorColor: new THREE.Color(0.60, 0.70, 0.90),
                ceilingColor: new THREE.Color(0.48, 0.56, 0.76),
                floorRoughness: 0.55
            };
        }
        if (depth <= 85) {
            return {
                name: 'Magma Underworld',
                bgColor: 0x060302,
                ambientColor: 0x7a4024,
                ambientEnergy: 0.36,
                sunLight: false,
                sunEnergy: 0.0,
                fogColor: 0x140603,
                fogDensity: 0.016,
                exposure: 1.22,
                torchColor: 0xffd18c,
                torchEnergy: 3.0,
                wallColor: new THREE.Color(0.90, 0.72, 0.65),
                floorColor: new THREE.Color(0.78, 0.62, 0.55),
                ceilingColor: new THREE.Color(0.62, 0.48, 0.40),
                floorRoughness: 0.70
            };
        }
        return {
            name: 'Abyssal Throne',
            bgColor: 0x040206,
            ambientColor: 0x613370,
            ambientEnergy: 0.30,
            sunLight: false,
            sunEnergy: 0.0,
            fogColor: 0x0d0414,
            fogDensity: 0.016,
            exposure: 1.22,
            torchColor: 0xf2d9bf,
            torchEnergy: 2.4,
            wallColor: new THREE.Color(0.75, 0.65, 0.85),
            floorColor: new THREE.Color(0.68, 0.58, 0.78),
            ceilingColor: new THREE.Color(0.50, 0.40, 0.60),
            floorRoughness: 0.60
        };
    }

    initTextures() {
        const maxAniso = this.renderer.capabilities.getMaxAnisotropy();

        // 1. Instant Procedural Textures (Exact Godot DungeonWorld Parity fallback)
        this.wallTex = procCreateStoneWallTexture();
        this.wallNormal = procCreateStoneWallNormal();
        this.floorTex = procCreateFloorTexture();
        this.floorNormal = procCreateFloorNormal();
        this.ceilingTex = procCreateCeilingTexture();
        this.doorTex = procCreateWoodDoorTexture();
        this.storeTex = procCreateStoreTexture();
        this.magmaTex = procCreateMagmaTexture();
        this.magmaEmission = procCreateMagmaEmission();
        this.quartzTex = procCreateQuartzTexture();

        // Procedural Creature Normal Maps (eliminates flat untextured models)
        this.organicNormal = procCreateOrganicNormal();
        this.scaleNormal = procCreateScaleNormal();
        this.chitinNormal = procCreateChitinNormal();
        this.furNormal = procCreateFurNormal();

        // Materials with Authentic PBR Settings matching Godot StandardMaterial3D
        this.wallMaterial = new THREE.MeshStandardMaterial({
            map: this.wallTex,
            normalMap: this.wallNormal,
            normalScale: new THREE.Vector2(0.85, 0.85),
            roughness: 0.80,
            metalness: 0.02
        });

        this.floorMaterial = new THREE.MeshStandardMaterial({
            map: this.floorTex,
            normalMap: this.floorNormal,
            normalScale: new THREE.Vector2(0.80, 0.80),
            roughness: 0.74,
            metalness: 0.04,
            side: THREE.DoubleSide
        });

        this.ceilingMaterial = new THREE.MeshStandardMaterial({
            map: this.ceilingTex,
            roughness: 0.95,
            metalness: 0.0,
            side: THREE.DoubleSide
        });

        this.doorMaterial = new THREE.MeshStandardMaterial({
            map: this.doorTex,
            normalScale: new THREE.Vector2(0.80, 0.80),
            roughness: 0.75,
            metalness: 0.10
        });

        this.storeMaterial = new THREE.MeshStandardMaterial({
            map: this.storeTex,
            roughness: 0.85,
            metalness: 0.02
        });

        // 8 Shop Entrance Doors with Heraldic Shield & Gold Number Plaque
        this.shopDoorMaterials = [];
        for (let i = 0; i < 8; i++) {
            const shopNum = i + 1;
            const shopTex = procCreateShopDoorTexture(shopNum, procStoreColor(shopNum));
            this.shopDoorMaterials.push(new THREE.MeshStandardMaterial({
                map: shopTex,
                roughness: 0.70,
                metalness: 0.15
            }));
        }

        // Magma Fissures (Incandescent emission)
        this.magmaMaterial = new THREE.MeshStandardMaterial({
            map: this.magmaTex,
            emissiveMap: this.magmaEmission,
            emissive: new THREE.Color(0xff4400),
            emissiveIntensity: 2.2,
            roughness: 0.70,
            metalness: 0.04
        });

        // Quartz Veins (Glinting crystal)
        this.quartzMaterial = new THREE.MeshStandardMaterial({
            map: this.quartzTex,
            roughness: 0.50,
            metalness: 0.10
        });

        // In-View Molten Lava
        this.lavaMaterial = new THREE.MeshStandardMaterial({
            color: 0xff3300,
            emissive: 0xff4400,
            emissiveIntensity: 2.2,
            roughness: 0.35,
            metalness: 0.10,
            side: THREE.DoubleSide
        });

        // Cooled Basalt (Memory/Fog-of-War Lava)
        this.lavaCooledMaterial = new THREE.MeshStandardMaterial({
            color: 0x141210,
            roughness: 0.95,
            metalness: 0.0,
            side: THREE.DoubleSide
        });

        this.stairsMaterial = new THREE.MeshStandardMaterial({
            map: this.wallTex,
            normalMap: this.wallNormal,
            roughness: 0.82,
            metalness: 0.04
        });

        // 2. Load Local Texture Assets (1:1 with Godot LoadTextureOrFallback in DungeonWorld.cs:1192-1209)
        const loader = new THREE.TextureLoader();
        const loadPBR = (url, targetMat, prop, isSRGB, repX = 1, repY = 1) => {
            loader.load(url, (tex) => {
                tex.wrapS = THREE.RepeatWrapping;
                tex.wrapT = THREE.RepeatWrapping;
                tex.repeat.set(repX, repY);
                if (isSRGB) tex.encoding = THREE.sRGBEncoding;
                tex.anisotropy = maxAniso;
                targetMat[prop] = tex;
                targetMat.needsUpdate = true;
            }, undefined, () => {
                // Procedural texture remains active gracefully if asset unavailable
            });
        };

        // Wall Textures
        loadPBR('/assets/textures/T_Brick_BaseColor.png', this.wallMaterial, 'map', true, 1, 1);
        loadPBR('/assets/textures/T_Brick_Normal.png', this.wallMaterial, 'normalMap', false, 1, 1);
        loadPBR('/assets/textures/T_Brick_Roughness.png', this.wallMaterial, 'roughnessMap', false, 1, 1);

        // Floor Textures
        loadPBR('/assets/textures/T_UnevenBrick_BaseColor.png', this.floorMaterial, 'map', true, 1, 1);
        loadPBR('/assets/textures/T_UnevenBrick_Normal.png', this.floorMaterial, 'normalMap', false, 1, 1);
        loadPBR('/assets/textures/T_UnevenBrick_Roughness.png', this.floorMaterial, 'roughnessMap', false, 1, 1);

        // Ceiling Textures
        loadPBR('/assets/textures/T_RockTrim_BaseColor.png', this.ceilingMaterial, 'map', true, 1, 1);
        loadPBR('/assets/textures/T_RockTrim_Normal.png', this.ceilingMaterial, 'normalMap', false, 1, 1);

        // Door Textures
        loadPBR('/assets/textures/T_WoodTrim_BaseColor.png', this.doorMaterial, 'map', true, 1, 1);
        loadPBR('/assets/textures/T_WoodTrim_Normal.png', this.doorMaterial, 'normalMap', false, 1, 1);
        loadPBR('/assets/textures/T_WoodTrim_Roughness.png', this.doorMaterial, 'roughnessMap', false, 1, 1);

        // Store Textures
        loadPBR('/assets/textures/T_Plaster_BaseColor.png', this.storeMaterial, 'map', true, 1, 1);
        loadPBR('/assets/textures/T_Plaster_Normal.png', this.storeMaterial, 'normalMap', false, 1, 1);
        loadPBR('/assets/textures/T_Plaster_ORM.png', this.storeMaterial, 'roughnessMap', false, 1, 1);

        // Stairs Textures
        loadPBR('/assets/textures/T_Brick_BaseColor.png', this.stairsMaterial, 'map', true, 1, 1);
        loadPBR('/assets/textures/T_Brick_Normal.png', this.stairsMaterial, 'normalMap', false, 1, 1);
        loadPBR('/assets/textures/T_Brick_Roughness.png', this.stairsMaterial, 'roughnessMap', false, 1, 1);
    }

    initMeshes() {
        this.maxInstances = 8192;
        // 45-degree chamfered geometry creates a visible 0.57m aperture between diagonal blocks
        const wallGeo = createChamferedWallGeometry(this.cellSize, this.wallHeight, 0.40);
        const floorGeo = new THREE.PlaneGeometry(this.cellSize, this.cellSize);
        floorGeo.rotateX(-Math.PI / 2);

        const ceilingGeo = new THREE.PlaneGeometry(this.cellSize, this.cellSize);
        ceilingGeo.rotateX(Math.PI / 2);

        // Helper to construct composite PBR geometries (1:1 with Godot SurfaceTool / BuildDoorMesh)
        const createMergedBoxGeometry = (boxes) => {
            const geometries = boxes.map(b => {
                const geo = new THREE.BoxGeometry(b.size[0], b.size[1], b.size[2]);
                if (b.rotX) geo.rotateX(b.rotX);
                if (b.rotY) geo.rotateY(b.rotY);
                if (b.rotZ) geo.rotateZ(b.rotZ);
                geo.translate(b.pos[0], b.pos[1], b.pos[2]);
                const nonIndexed = geo.toNonIndexed();
                geo.dispose();
                return nonIndexed;
            });
            let totalVerts = 0;
            for (const g of geometries) totalVerts += g.attributes.position.count;
            const posArr = new Float32Array(totalVerts * 3);
            const normArr = new Float32Array(totalVerts * 3);
            const uvArr = new Float32Array(totalVerts * 2);

            let vOffset = 0;
            for (const g of geometries) {
                posArr.set(g.attributes.position.array, vOffset * 3);
                normArr.set(g.attributes.normal.array, vOffset * 3);
                uvArr.set(g.attributes.uv.array, vOffset * 2);
                vOffset += g.attributes.position.count;
                g.dispose();
            }

            const merged = new THREE.BufferGeometry();
            merged.setAttribute('position', new THREE.BufferAttribute(posArr, 3));
            merged.setAttribute('normal', new THREE.BufferAttribute(normArr, 3));
            merged.setAttribute('uv', new THREE.BufferAttribute(uvArr, 2));
            return merged;
        };

        // 1. Stone Door Frame & Lintel for dungeon corridors (Matches Godot DungeonWorld.cs:1556-1575)
        const doorFrameGeo = createMergedBoxGeometry([
            { pos: [-0.85, 1.10, 0], size: [0.30, 2.40, 0.40] }, // Left jamb post
            { pos: [0.85, 1.10, 0], size: [0.30, 2.40, 0.40] },  // Right jamb post
            { pos: [0, 2.65, 0], size: [2.00, 0.70, 0.40] },     // Top stone lintel
            { pos: [0, -0.02, 0], size: [1.40, 0.08, 0.40] },    // Threshold step
            { pos: [-0.72, 1.75, 0.04], size: [0.10, 0.14, 0.16] }, // Top hinge bracket
            { pos: [-0.72, 0.55, 0.04], size: [0.10, 0.14, 0.16] }  // Bottom hinge bracket
        ]);

        // 1b. Authentic Shop Entrance Masonry (Matches Godot DungeonWorld.cs:1653-1678 BuildShopDoorMesh)
        // Spans full 2.0m tile depth to perfectly flush with adjacent 2.0m wall blocks and seal building interior
        const shopFrameGeo = createMergedBoxGeometry([
            { pos: [-0.85, 1.10, 0.0], size: [0.30, 2.40, 2.00] }, // Left stone pier spanning full 2.0m depth
            { pos: [0.85, 1.10, 0.0], size: [0.30, 2.40, 2.00] },  // Right stone pier spanning full 2.0m depth
            { pos: [0, 2.65, 0.0], size: [2.00, 0.70, 2.00] },     // Top stone lintel spanning full 2.0m depth to 3m ceiling
            { pos: [0, 1.10, -0.55], size: [1.40, 2.40, 0.90] },   // Back solid masonry wall sealing building interior
            { pos: [0, -0.02, 0.45], size: [1.40, 0.08, 0.90] },   // Front threshold step
            { pos: [-0.72, 1.10, 0.15], size: [0.12, 2.40, 0.30] }, // Front decorative pilaster left
            { pos: [0.72, 1.10, 0.15], size: [0.12, 2.40, 0.30] },  // Front decorative pilaster right
            { pos: [-0.64, 1.75, 0.12], size: [0.08, 0.14, 0.14] }, // Top iron hinge bracket
            { pos: [-0.64, 0.55, 0.12], size: [0.08, 0.14, 0.14] }  // Bottom iron hinge bracket
        ]);

        // 2. Closed Wooden Door Panel with Horizontal Iron Straps (DungeonWorld.cs:1581-1593)
        const doorClosedGeo = createMergedBoxGeometry([
            { pos: [0, 1.15, 0], size: [1.40, 2.20, 0.12] },       // Wooden door leaf
            { pos: [0, 1.75, 0], size: [1.36, 0.12, 0.16] },       // Top iron strap
            { pos: [0, 0.55, 0], size: [1.36, 0.12, 0.16] },       // Bottom iron strap
            { pos: [0.45, 1.10, 0.08], size: [0.10, 0.16, 0.06] },  // Front latch ring
            { pos: [0.45, 1.10, -0.08], size: [0.10, 0.16, 0.06] } // Back latch ring
        ]);

        // 2b. Shop Door Leaf with Iron Straps recessed slightly at z = 0.05 (DungeonWorld.cs:1686-1693)
        const shopDoorGeo = createMergedBoxGeometry([
            { pos: [0, 1.15, 0.05], size: [1.40, 2.20, 0.12] },       // Wooden door leaf
            { pos: [0, 1.75, 0.07], size: [1.36, 0.12, 0.14] },       // Top iron strap
            { pos: [0, 0.55, 0.07], size: [1.36, 0.12, 0.14] },       // Bottom iron strap
            { pos: [0.45, 1.10, 0.12], size: [0.10, 0.16, 0.06] }     // Front latch ring
        ]);

        // 3. Swung-Open Door Panel Angled Ajar 70° (DungeonWorld.cs:1594-1631)
        const openYaw = 70 * Math.PI / 180;
        const panelCenterX = -0.68 + 0.64 * Math.cos(openYaw);
        const panelCenterZ = 0.64 * Math.sin(openYaw);
        const doorOpenGeo = createMergedBoxGeometry([
            { pos: [panelCenterX, 1.15, panelCenterZ], size: [1.28, 2.18, 0.10], rotY: -openYaw },
            { pos: [panelCenterX, 1.75, panelCenterZ], size: [1.24, 0.12, 0.14], rotY: -openYaw },
            { pos: [panelCenterX, 0.55, panelCenterZ], size: [1.24, 0.12, 0.14], rotY: -openYaw },
            { pos: [-0.68 + 1.05 * Math.cos(openYaw), 1.10, 1.05 * Math.sin(openYaw)], size: [0.10, 0.16, 0.15], rotY: -openYaw }
        ]);

        // 4. Shattered Broken Door Planks & Splinters (DungeonWorld.cs:1632-1643)
        const doorBrokenGeo = createMergedBoxGeometry([
            { pos: [-0.35, 0.06, 0.15], size: [0.85, 0.06, 0.26] },
            { pos: [0.35, 0.05, -0.15], size: [0.75, 0.05, 0.22] },
            { pos: [0.10, 0.09, 0.30], size: [0.60, 0.06, 0.18] },
            { pos: [-0.15, 0.08, -0.25], size: [0.50, 0.05, 0.20] },
            { pos: [-0.62, 1.70, 0.08], size: [0.26, 0.48, 0.08], rotY: -0.35 }
        ]);

        // 5. Authentic 3D Stepped Staircases (Matches Godot DungeonWorld.cs:1708-1735)
        const buildStairsBoxes = (down) => {
            const boxes = [
                { pos: [-0.85, 0.40, 0], size: [0.30, 0.80, 2.0] }, // Left curb
                { pos: [0.85, 0.40, 0], size: [0.30, 0.80, 2.0] }   // Right curb
            ];
            const stepCount = 5;
            const stepWidth = 1.40;
            const stepDepth = 2.0 / stepCount;
            for (let i = 0; i < stepCount; i++) {
                const z = -1.0 + stepDepth * (i + 0.5);
                const stepHeight = down ? (0.50 - i * 0.10) : (0.10 + i * 0.10);
                const yCenter = stepHeight / 2.0;
                boxes.push({ pos: [0, yCenter, z], size: [stepWidth, stepHeight, stepDepth] });
            }
            return boxes;
        };
        const stairsDownGeo = createMergedBoxGeometry(buildStairsBoxes(true));
        const stairsUpGeo = createMergedBoxGeometry(buildStairsBoxes(false));

        // 6. Impassable Rubble Piles (Collapsed masonry boulders & stone talus)
        const rubbleGeo = createMergedBoxGeometry([
            // Central high boulder peak
            { pos: [0.05, 0.65, -0.05], size: [1.20, 1.25, 1.10], rotY: 0.35, rotX: 0.12 },
            // Left flank boulder
            { pos: [-0.52, 0.45, 0.20], size: [0.95, 0.90, 0.95], rotY: -0.45, rotZ: 0.15 },
            // Right flank boulder
            { pos: [0.55, 0.42, -0.22], size: [0.95, 0.85, 0.90], rotY: 0.55, rotZ: -0.18 },
            // Forward spilled rock
            { pos: [-0.18, 0.28, 0.55], size: [1.00, 0.55, 0.80], rotY: -0.20, rotX: -0.15 },
            // Rear spilled rock
            { pos: [0.22, 0.30, -0.55], size: [0.95, 0.60, 0.75], rotY: 0.40, rotX: 0.18 },
            // Perimeter rock skirts filling the 2.0m width
            { pos: [0.68, 0.18, 0.45], size: [0.70, 0.35, 0.65], rotY: -0.65, rotZ: 0.10 },
            { pos: [-0.68, 0.16, -0.45], size: [0.65, 0.32, 0.70], rotY: 0.75, rotZ: -0.12 },
            { pos: [-0.10, 0.85, 0.08], size: [0.70, 0.50, 0.65], rotY: 0.80, rotX: 0.22 }
        ]);

        this.wallMesh = new THREE.InstancedMesh(wallGeo, this.wallMaterial, this.maxInstances);
        this.wallMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.wallMesh);

        this.floorMesh = new THREE.InstancedMesh(floorGeo, this.floorMaterial, this.maxInstances);
        this.floorMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.floorMesh);

        this.ceilingMesh = new THREE.InstancedMesh(ceilingGeo, this.ceilingMaterial, this.maxInstances);
        this.ceilingMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.ceilingMesh);

        // Stone Doorways (Frames & Lintels for town & dungeon)
        this.doorFrameMesh = new THREE.InstancedMesh(doorFrameGeo, this.wallMaterial, 512);
        this.doorFrameMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.doorFrameMesh);

        // Full-depth Town Shop Entrance Masonry Facade (1:1 with Godot BuildShopDoorMesh)
        this.shopFrameMesh = new THREE.InstancedMesh(shopFrameGeo, this.wallMaterial, 64);
        this.shopFrameMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.shopFrameMesh);

        // Impassable Rubble Piles (feat 16)
        this.rubbleMesh = new THREE.InstancedMesh(rubbleGeo, this.wallMaterial, 256);
        this.rubbleMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.rubbleMesh);

        // Closed Dungeon Door (feat 2)
        this.doorMesh = new THREE.InstancedMesh(doorClosedGeo, this.doorMaterial, 512);
        this.doorMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.doorMesh);

        // Open Dungeon Door (feat 3)
        this.doorOpenMesh = new THREE.InstancedMesh(doorOpenGeo, this.doorMaterial, 256);
        this.doorOpenMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.doorOpenMesh);

        // Broken Dungeon Door (feat 4)
        this.doorBrokenMesh = new THREE.InstancedMesh(doorBrokenGeo, this.doorMaterial, 256);
        this.doorBrokenMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.doorBrokenMesh);

        // 8 Shop Entrance Meshes with Heraldic Bronze Plaques
        this.shopMeshes = [];
        for (let i = 0; i < 8; i++) {
            const mesh = new THREE.InstancedMesh(shopDoorGeo, this.shopDoorMaterials[i], 64);
            mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
            this.scene.add(mesh);
            this.shopMeshes.push(mesh);
        }

        this.magmaMesh = new THREE.InstancedMesh(wallGeo, this.magmaMaterial, 256);
        this.magmaMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.magmaMesh);

        this.quartzMesh = new THREE.InstancedMesh(wallGeo, this.quartzMaterial, 256);
        this.quartzMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.quartzMesh);

        this.lavaMesh = new THREE.InstancedMesh(floorGeo, this.lavaMaterial, 512);
        this.lavaMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.lavaMesh);

        this.lavaCooledMesh = new THREE.InstancedMesh(floorGeo, this.lavaCooledMaterial, 512);
        this.lavaCooledMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.lavaCooledMesh);

        // Down Staircase (feat 6)
        this.stairsMesh = new THREE.InstancedMesh(stairsDownGeo, this.stairsMaterial, 128);
        this.stairsMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.stairsMesh);

        // Up Staircase (feat 5)
        this.stairsUpMesh = new THREE.InstancedMesh(stairsUpGeo, this.stairsMaterial, 128);
        this.stairsUpMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.stairsUpMesh);

        // Pre-allocate instance color buffers with 1.0 (pure white albedo multiplier)
        const initInstancedColors = (mesh, count) => {
            const colors = new Float32Array(count * 3).fill(1.0);
            mesh.instanceColor = new THREE.InstancedBufferAttribute(colors, 3);
            mesh.count = 0; // Initialize to 0 so unpopulated instances are never drawn
        };
        initInstancedColors(this.wallMesh, this.maxInstances);
        initInstancedColors(this.floorMesh, this.maxInstances);
        initInstancedColors(this.ceilingMesh, this.maxInstances);
        initInstancedColors(this.doorFrameMesh, 512);
        initInstancedColors(this.shopFrameMesh, 64);
        initInstancedColors(this.rubbleMesh, 256);
        initInstancedColors(this.doorMesh, 512);
        initInstancedColors(this.doorOpenMesh, 256);
        initInstancedColors(this.doorBrokenMesh, 256);
        initInstancedColors(this.stairsMesh, 128);
        initInstancedColors(this.stairsUpMesh, 128);
        initInstancedColors(this.magmaMesh, 256);
        initInstancedColors(this.quartzMesh, 256);
        this.lavaMesh.count = 0;
        this.lavaCooledMesh.count = 0;
        for (let i = 0; i < 8; i++) initInstancedColors(this.shopMeshes[i], 64);

        this.dummy = new THREE.Object3D();
    }

    initViewmodel() {
        this.viewmodelGroup = new THREE.Group();
        this.camera.add(this.viewmodelGroup);
        this.scene.add(this.camera);

        this.heightRatio = 1.0;
        this.cameraBobPhase = 0;

        // Right Hand Weapon Slot: Calibrated cleanly to lower right corner (1:1 with Godot ViewModel.cs:694, 704)
        // Position: (0.24, -0.24, -0.32), Rotation: (18°, -14°, 6°)
        this.rightHandGroup = new THREE.Group();
        this.rightRestPos = new THREE.Vector3(0.24, -0.24, -0.32);
        this.rightRestRot = new THREE.Euler(18 * Math.PI / 180, -14 * Math.PI / 180, 6 * Math.PI / 180);
        this.rightHandGroup.position.copy(this.rightRestPos);
        this.rightHandGroup.rotation.copy(this.rightRestRot);
        this.rightItemSlot = new THREE.Group();
        this.rightItemSlot.name = "RightItemSlot";
        this.rightHandGroup.add(this.rightItemSlot);
        this.rightHandGroup.visible = false; // Hidden when unarmed
        this.viewmodelGroup.add(this.rightHandGroup);

        // Left Hand Shield Slot: Calibrated to lower left corner (1:1 with Godot ViewModel.cs)
        this.leftHandGroup = new THREE.Group();
        this.leftRestPos = new THREE.Vector3(-0.24, -0.24, -0.32);
        this.leftRestRot = new THREE.Euler(18 * Math.PI / 180, 14 * Math.PI / 180, -6 * Math.PI / 180);
        this.leftHandGroup.position.copy(this.leftRestPos);
        this.leftHandGroup.rotation.copy(this.leftRestRot);
        this.leftItemSlot = new THREE.Group();
        this.leftItemSlot.name = "LeftItemSlot";
        this.leftHandGroup.add(this.leftItemSlot);
        this.leftHandGroup.visible = false; // Hidden unless shield equipped
        this.viewmodelGroup.add(this.leftHandGroup);

        // Weapon models cache
        this.weaponCache = new Map();
        this.currentRightWeapon = null;
        this.currentLeftWeapon = null;

        this.attackAnimationTime = 0;
        this.hurtAnimationTime = 0;
        this.castAnimationTime = 0;
    }

    initModelResolvers() {
        this.objLoader = (typeof THREE.OBJLoader !== 'undefined') ? new THREE.OBJLoader() : null;
        this.gltfLoader = (typeof THREE.GLTFLoader !== 'undefined') ? new THREE.GLTFLoader() : null;
        this.monsterTemplates = new Map();
        this.itemTemplates = new Map();
        this.weaponCache = new Map();

        // 1a. Preload KayKit Character Palette Textures (Paints outfits, armor, faces)
        this.characterPalettes = new Map();
        const texLoader = new THREE.TextureLoader();
        const loadPalette = (key, url) => {
            texLoader.load(url, (tex) => {
                tex.encoding = THREE.sRGBEncoding;
                this.characterPalettes.set(key, tex);
            }, undefined, () => {});
        };
        loadPalette('knight', '/assets/models/characters/knight_texture.png');
        loadPalette('rogue', '/assets/models/characters/rogue_texture.png');
        loadPalette('barbarian', '/assets/models/characters/barbarian_texture.png');
        loadPalette('mage', '/assets/models/characters/mage_texture.png');
        loadPalette('skeleton', '/assets/models/characters/skeleton_texture.png');

        // 1. High-Fidelity Character GLTF Rigs (KayKit CC0 Models & Bestiary)
        if (this.gltfLoader) {
            const characterModels = [
                { key: 'adventurer', url: '/assets/models/characters/Adventurer.gltf', defaultScale: 0.95 },
                { key: 'casual', url: '/assets/models/characters/Casual.gltf', defaultScale: 0.88 },
                { key: 'farmer', url: '/assets/models/characters/Farmer.gltf', defaultScale: 0.88 },
                { key: 'formal', url: '/assets/models/characters/Formal.gltf', defaultScale: 0.90 },
                { key: 'king', url: '/assets/models/characters/King.gltf', defaultScale: 1.05 },
                { key: 'medieval', url: '/assets/models/characters/Medieval.gltf', defaultScale: 0.95 },
                { key: 'punk', url: '/assets/models/characters/Punk.gltf', defaultScale: 0.98 },
                { key: 'soldier', url: '/assets/models/characters/Soldier.gltf', defaultScale: 0.92 },
                { key: 'witch', url: '/assets/models/characters/Witch.gltf', defaultScale: 0.92 },
                { key: 'worker', url: '/assets/models/characters/Worker.gltf', defaultScale: 0.90 },
                { key: 'imp', url: '/assets/models/monsters/Imp.glb', defaultScale: 0.75 },
                { key: 'puglin', url: '/assets/models/monsters/Puglin.glb', defaultScale: 0.70 }
            ];

            characterModels.forEach(m => {
                this.gltfLoader.load(m.url, (gltf) => {
                    const scene = gltf.scene || (gltf.scenes && gltf.scenes[0]);
                    if (scene) {
                        scene.traverse((c) => {
                            if (c.isMesh) {
                                c.castShadow = true;
                                c.receiveShadow = true;
                            }
                        });
                        this.monsterTemplates.set(m.key, {
                            scene: scene,
                            defaultScale: m.defaultScale
                        });
                    }
                }, undefined, (err) => {
                    console.warn(`[3D] Failed to load character model '${m.key}':`, err);
                });
            });
        }

        if (!this.objLoader) return;

        // Shared PBR materials for loaded 3D models with organic normal maps
        const ratMat = new THREE.MeshStandardMaterial({ color: 0x5a4838, roughness: 0.85, metalness: 0.05, normalMap: this.furNormal });
        const snakeMat = new THREE.MeshStandardMaterial({ color: 0x3d6b35, roughness: 0.55, metalness: 0.15, normalMap: this.scaleNormal });
        const spiderMat = new THREE.MeshStandardMaterial({ color: 0x1c1a24, roughness: 0.40, metalness: 0.35, normalMap: this.chitinNormal });
        const frogMat = new THREE.MeshStandardMaterial({ color: 0x4f7d28, roughness: 0.45, metalness: 0.20, normalMap: this.scaleNormal });
        const waspMat = new THREE.MeshStandardMaterial({ color: 0xcaa018, roughness: 0.40, metalness: 0.30, normalMap: this.chitinNormal });

        const loadMonster = (key, url, mat, scale, yOffset = 0) => {
            this.objLoader.load(url, (obj) => {
                obj.traverse((c) => {
                    if (c.isMesh) {
                        c.material = mat.clone();
                    }
                });
                obj.scale.set(scale, scale, scale);
                obj.position.y = yOffset;
                this.monsterTemplates.set(key, obj);
            }, undefined, () => {});
        };

        loadMonster('rat', '/assets/models/monsters/Rat.obj', ratMat, 0.42, 0.0);
        loadMonster('snake', '/assets/models/monsters/Snake.obj', snakeMat, 0.38, 0.0);
        loadMonster('snake_angry', '/assets/models/monsters/Snake_angry.obj', snakeMat, 0.40, 0.0);
        loadMonster('spider', '/assets/models/monsters/Spider.obj', spiderMat, 0.48, 0.0);
        loadMonster('frog', '/assets/models/monsters/Frog.obj', frogMat, 0.38, 0.0);
        loadMonster('wasp', '/assets/models/monsters/Wasp.obj', waspMat, 0.42, 0.45);

        // Weapon models for Viewmodel (Multi-material PBR matching Blender/FBX/MTL definitions)
        const loadWeapon = (key, url, scale, rot, pos) => {
            this.objLoader.load(url, (obj) => {
                obj.traverse((c) => {
                    if (c.isMesh) {
                        const mName = ((c.material && c.material.name ? c.material.name : '') + ' ' + (c.name || '')).toLowerCase();
                        if (mName.includes('darkwood') || mName.includes('leather') || mName.includes('grip') || mName.includes('strap')) {
                            c.material = new THREE.MeshStandardMaterial({ color: 0x4a2a14, roughness: 0.85, metalness: 0.05 });
                        } else if (mName.includes('lightwood') || mName.includes('handle') || mName.includes('wood')) {
                            c.material = new THREE.MeshStandardMaterial({ color: 0x8a5528, roughness: 0.80, metalness: 0.05 });
                        } else if (mName.includes('steel') || mName.includes('lightsteel') || mName.includes('head') || mName.includes('stone') || mName.includes('blade')) {
                            c.material = new THREE.MeshStandardMaterial({ color: 0xc8d0dc, roughness: 0.35, metalness: 0.85 });
                        } else if (mName.includes('gold') || mName.includes('guard') || mName.includes('pommel')) {
                            c.material = new THREE.MeshStandardMaterial({ color: 0xe6b830, roughness: 0.25, metalness: 0.90 });
                        } else {
                            c.material = new THREE.MeshStandardMaterial({ color: 0xa8b4c4, roughness: 0.40, metalness: 0.75 });
                        }
                    }
                });
                obj.scale.set(scale, scale, scale);
                if (pos) obj.position.set(pos.x, pos.y, pos.z);
                if (rot) obj.rotation.set(rot.x, rot.y, rot.z);
                this.weaponCache.set(key, obj);

                // If this is the weapon currently held, mount it immediately in rightItemSlot
                if (this.currentRightWeapon === key && this.rightItemSlot) {
                    while (this.rightItemSlot.children.length > 0) {
                        this.rightItemSlot.remove(this.rightItemSlot.children[0]);
                    }
                    this.rightItemSlot.add(obj.clone(true));
                    this.rightHandGroup.visible = true;
                }
            }, undefined, () => {});
        };

        // Calibrated 1:1 with Godot ViewModel.cs:680-725 transforms
        loadWeapon('sword', '/assets/models/weapons/Sword.obj', 0.088, { x: 18 * Math.PI / 180, y: -14 * Math.PI / 180, z: 6 * Math.PI / 180 }, { x: 0, y: -0.03, z: 0 });
        loadWeapon('claymore', '/assets/models/weapons/Claymore.obj', 0.095, { x: 16 * Math.PI / 180, y: -12 * Math.PI / 180, z: 6 * Math.PI / 180 }, { x: 0, y: -0.03, z: 0 });
        loadWeapon('dagger', '/assets/models/weapons/Dagger.obj', 0.090, { x: 20 * Math.PI / 180, y: -12 * Math.PI / 180, z: 8 * Math.PI / 180 }, { x: 0, y: 0, z: 0 });
        loadWeapon('axe', '/assets/models/weapons/Axe_Small.obj', 0.085, { x: 18 * Math.PI / 180, y: -14 * Math.PI / 180, z: 6 * Math.PI / 180 }, { x: 0, y: -0.04, z: 0 });
        loadWeapon('hammer', '/assets/models/weapons/Hammer_Small.obj', 0.085, { x: 18 * Math.PI / 180, y: -14 * Math.PI / 180, z: 6 * Math.PI / 180 }, { x: 0, y: -0.04, z: 0 });
        loadWeapon('spear', '/assets/models/weapons/Spear.obj', 0.075, { x: 18 * Math.PI / 180, y: -12 * Math.PI / 180, z: 6 * Math.PI / 180 }, { x: 0, y: -0.08, z: 0 });
        loadWeapon('bow', '/assets/models/weapons/Bow_Wooden.obj', 0.085, { x: 0.15, y: Math.PI / 2, z: 0 }, { x: 0, y: -0.02, z: 0.04 });
        loadWeapon('shield', '/assets/models/weapons/Shield_Round.obj', 0.085, { x: 0.18, y: 0.35, z: 0 }, { x: 0, y: -0.03, z: 0 });

        // Item templates
        const goldMat = new THREE.MeshStandardMaterial({ color: 0xffd700, emissive: 0x553300, metalness: 0.95, roughness: 0.18 });
        const potionMat = new THREE.MeshStandardMaterial({ color: 0xff3355, emissive: 0xbb1133, emissiveIntensity: 1.2, roughness: 0.15, metalness: 0.2 });
        const scrollMat = new THREE.MeshStandardMaterial({ color: 0xeee0b0, roughness: 0.85 });
        const bookMat = new THREE.MeshStandardMaterial({ color: 0x8b2500, roughness: 0.70, metalness: 0.1 });
        const ringMat = new THREE.MeshStandardMaterial({ color: 0xffc400, emissive: 0x553300, metalness: 0.95, roughness: 0.15 });
        const chestMat = new THREE.MeshStandardMaterial({ color: 0x5c3a21, roughness: 0.75, metalness: 0.25 });
        const weaponMat = new THREE.MeshStandardMaterial({ color: 0xd8e4ed, roughness: 0.20, metalness: 0.90 });
        const armorMat = new THREE.MeshStandardMaterial({ color: 0xb0bcc8, roughness: 0.35, metalness: 0.85 });
        const leatherMat = new THREE.MeshStandardMaterial({ color: 0x6e482b, roughness: 0.85, metalness: 0.05 });
        const woodMat = new THREE.MeshStandardMaterial({ color: 0x82522c, roughness: 0.80, metalness: 0.05 });
        const foodMat = new THREE.MeshStandardMaterial({ color: 0xbf6c3b, roughness: 0.85, metalness: 0.05 });
        const gemMat = new THREE.MeshStandardMaterial({ color: 0x44ddff, emissive: 0x114466, emissiveIntensity: 0.8, roughness: 0.10, metalness: 0.2 });

        const loadItem = (key, url, mat, scale, yOffset = 0) => {
            this.objLoader.load(url, (obj) => {
                obj.traverse((c) => {
                    if (c.isMesh) {
                        c.material = mat.clone();
                    }
                });
                obj.scale.set(scale, scale, scale);
                obj.position.y = yOffset;
                this.itemTemplates.set(key, obj);
            }, undefined, () => {});
        };

        // Core consumables & treasures
        loadItem('$', '/assets/models/items/Gold_Ingots.obj', goldMat, 0.25, 0.05);
        loadItem('coin', '/assets/models/items/Coin.obj', goldMat, 0.22, 0.05);
        loadItem('!', '/assets/models/items/Potion1_Filled.obj', potionMat, 0.22, 0.05);
        loadItem('?', '/assets/models/items/Scroll.obj', scrollMat, 0.22, 0.05);
        loadItem('book', '/assets/models/items/Book1_Closed.obj', bookMat, 0.20, 0.05);
        loadItem('=', '/assets/models/items/Ring1.obj', ringMat, 0.18, 0.08);
        loadItem('"', '/assets/models/items/Necklace1.obj', goldMat, 0.18, 0.05);
        loadItem('*', '/assets/models/items/Crystal1.obj', gemMat, 0.18, 0.05);
        loadItem('chest', '/assets/models/items/Chest_Closed.obj', chestMat, 0.22, 0.0);
        loadItem(',', '/assets/models/items/ChickenLeg.obj', foodMat, 0.20, 0.05);

        // Armor, Footwear & Accessories
        loadItem('armor_metal', '/assets/models/items/Armor_Metal.obj', armorMat, 0.22, 0.05);
        loadItem('armor_metal2', '/assets/models/items/Armor_Metal2.obj', armorMat, 0.22, 0.05);
        loadItem('armor_leather', '/assets/models/items/Armor_Leather.obj', leatherMat, 0.22, 0.05);
        loadItem('crown', '/assets/models/items/Crown.obj', goldMat, 0.20, 0.05);
        loadItem('glove', '/assets/models/items/Glove.obj', leatherMat, 0.18, 0.05);
        loadItem('backpack', '/assets/models/items/Backpack.obj', leatherMat, 0.20, 0.05);
        loadItem('pouch', '/assets/models/items/Pouch.obj', leatherMat, 0.18, 0.05);
        loadItem('key', '/assets/models/items/Key1.obj', goldMat, 0.18, 0.05);
        loadItem('skull', '/assets/models/items/Skull.obj', scrollMat, 0.18, 0.05);

        // Weapons & Shields
        loadItem(')', '/assets/models/weapons/Sword.obj', weaponMat, 0.18, 0.05);
        loadItem('sword_big', '/assets/models/weapons/Sword_Big.obj', weaponMat, 0.20, 0.05);
        loadItem('claymore', '/assets/models/weapons/Claymore.obj', weaponMat, 0.20, 0.05);
        loadItem('dagger', '/assets/models/weapons/Dagger.obj', weaponMat, 0.18, 0.05);
        loadItem('axe', '/assets/models/weapons/Axe_Small.obj', weaponMat, 0.20, 0.05);
        loadItem('axe_double', '/assets/models/weapons/Axe_Double.obj', weaponMat, 0.22, 0.05);
        loadItem('hammer', '/assets/models/weapons/Hammer_Small.obj', weaponMat, 0.20, 0.05);
        loadItem('hammer_double', '/assets/models/weapons/Hammer_Double.obj', weaponMat, 0.20, 0.05);
        loadItem('spear', '/assets/models/weapons/Spear.obj', weaponMat, 0.22, 0.05);
        loadItem('scythe', '/assets/models/weapons/Scythe.obj', weaponMat, 0.22, 0.05);
        loadItem('}', '/assets/models/weapons/Bow_Wooden.obj', woodMat, 0.22, 0.05);
        loadItem('{', '/assets/models/weapons/Arrow.obj', woodMat, 0.20, 0.05);
        loadItem('pebble', '/assets/models/items/Mineral.obj', weaponMat, 0.16, 0.05);
        loadItem('dart', '/assets/models/items/Dart.obj', weaponMat, 0.18, 0.05);
        loadItem('shield_heater', '/assets/models/weapons/Shield_Heater.obj', armorMat, 0.22, 0.05);
        loadItem('shield_round', '/assets/models/weapons/Shield_Round.obj', armorMat, 0.20, 0.05);
    }

    updateViewmodel(player) {
        if (!player) return;

        // 1. Dynamic Character Height & World Scaling Subsystem (Exact Parity with Godot ViewModel.cs)
        const ht = player.ht || 0;
        const race = (player.race || '').toLowerCase();
        const pClass = (player.class || '').toLowerCase();

        let targetEyeHeight = 1.62;
        let heightRatio = 1.0;

        if (ht > 0) {
            targetEyeHeight = (ht / 72.0) * 1.62;
            heightRatio = ht / 72.0;
        } else if (race.includes('halfling') || race.includes('hobbit') || race.includes('kobold')) {
            targetEyeHeight = 0.85;
            heightRatio = 0.60;
        } else if (race.includes('gnome')) {
            targetEyeHeight = 1.00;
            heightRatio = 0.70;
        } else if (race.includes('dwarf')) {
            targetEyeHeight = 1.18;
            heightRatio = 0.78;
        } else if (race.includes('half-troll') || race.includes('troll')) {
            targetEyeHeight = 2.25;
            heightRatio = 1.40;
        } else if (race.includes('half-ogre') || race.includes('ogre')) {
            targetEyeHeight = 2.05;
            heightRatio = 1.28;
        } else if (race.includes('high-elf') || race.includes('elf')) {
            targetEyeHeight = 1.76;
            heightRatio = 1.08;
        }

        this.eyeHeight = Math.max(0.70, Math.min(2.35, targetEyeHeight));
        this.heightRatio = Math.max(0.55, Math.min(1.45, heightRatio));

        // Realistic perspective pitch by character height:
        // Base focal pitch: -9.0° (natural downward focus on floor/corridor/monsters ahead)
        // Height compensation: +22° per meter of height difference below 1.62m (or -22° per meter above 1.62m)
        // Tall characters (Half-Trolls, Ogres) look steep downward (-23°);
        // Short characters (Halflings, Kobolds) look upwards (+8°);
        // Dwarves look level/slight chest-height (+0.7°);
        // Humans look natural down-corridor (-9.0°).
        const heightDelta = 1.62 - this.eyeHeight;
        const basePitchDeg = -9.0 + (heightDelta * 22.0);
        this.basePitch = basePitchDeg * (Math.PI / 180.0);
        this.targetPitch = this.basePitch + (this.userPitchOffset || 0.0);

        // Subtle FOV adjustment (1:1 with Godot DungeonWorld.cs:2056)
        // Wider peripheral awareness for short races (86°), focused perspective for tall races (80°)
        if (this.camera) {
            const fov = 86.0 - (this.heightRatio - 0.60) * (6.0 / 0.85);
            this.camera.fov = Math.max(78.0, Math.min(88.0, fov));
            this.camera.updateProjectionMatrix();
        }

        this.viewmodelGroup.scale.set(this.heightRatio, this.heightRatio, this.heightRatio);

        // 4. Equipment resolution
        const weaponItem = player.weapon_item || '';
        const bowItem = player.bow_item || '';
        const shieldItem = player.shield_item || '';
        const lightRadius = player.light || 0;
        const depth = player.depth !== undefined ? player.depth : 0;

        // LEFT HAND RULE (1:1 with Godot ViewModel.cs:342-355):
        // Only show what is actually wielded in the off-hand (such as an equipped shield).
        // Carrying a torch in the light slot illuminates the dungeon without occupying hands!
        if (shieldItem) {
            this.resolveShieldModel(shieldItem);
        } else {
            this.leftHandGroup.visible = false;
        }

        // RIGHT HAND RULE (1:1 with Godot ViewModel.cs:351-353):
        // Primary equipped melee weapon or ranged bow.
        // If unarmed, remains clean & empty (no fake default weapons or mannequin arms).
        this.resolveRightWeaponModel(weaponItem, bowItem);
    }

    resolveRightWeaponModel(weaponItem, bowItem) {
        let key = '';
        const lowerW = (weaponItem || '').toLowerCase();
        const lowerB = (bowItem || '').toLowerCase();

        if (lowerW.length > 0) {
            if (lowerW.includes('dagger') || lowerW.includes('knife') || lowerW.includes('blade') || lowerW.includes('main gauche') || lowerW.includes('misericorde')) {
                key = 'dagger';
            } else if (lowerW.includes('claymore') || lowerW.includes('two-handed') || lowerW.includes('great sword') || lowerW.includes('executioner') || lowerW.includes('zweihander')) {
                key = 'claymore';
            } else if (lowerW.includes('axe') || lowerW.includes('hatchet') || lowerW.includes('cleaver')) {
                key = 'axe';
            } else if (lowerW.includes('hammer') || lowerW.includes('mace') || lowerW.includes('flail') || lowerW.includes('morning star') || lowerW.includes('club') || lowerW.includes('cudgel')) {
                key = 'hammer';
            } else if (lowerW.includes('spear') || lowerW.includes('staff') || lowerW.includes('pike') || lowerW.includes('lance') || lowerW.includes('trident') || lowerW.includes('halberd') || lowerW.includes('polearm')) {
                key = 'spear';
            } else {
                key = 'sword';
            }
        } else if (lowerB.length > 0) {
            key = 'bow';
        }

        if (this.currentRightWeapon === key) return;
        this.currentRightWeapon = key;

        if (this.rightItemSlot) {
            while (this.rightItemSlot.children.length > 0) {
                this.rightItemSlot.remove(this.rightItemSlot.children[0]);
            }

            if (!key) {
                this.rightHandGroup.visible = false;
                return;
            }

            let mesh = null;
            if (this.weaponCache.has(key)) {
                mesh = this.weaponCache.get(key).clone(true);
            }
            if (mesh) {
                this.rightItemSlot.add(mesh);
            }
            this.rightHandGroup.visible = true;
        }
    }

    resolveShieldModel(shieldItem) {
        if (!this.leftItemSlot) return;
        while (this.leftItemSlot.children.length > 0) {
            this.leftItemSlot.remove(this.leftItemSlot.children[0]);
        }
        if (!shieldItem) {
            this.leftHandGroup.visible = false;
            return;
        }
        let mesh = null;
        if (this.weaponCache.has('shield')) {
            mesh = this.weaponCache.get('shield').clone(true);
        }
        if (mesh) {
            this.leftItemSlot.add(mesh);
        }
        this.leftHandGroup.visible = true;
    }

    triggerAttackAnimation() {
        this.attackAnimationTime = 0.22; // 220ms swing
    }

    triggerHurtAnimation() {
        this.hurtAnimationTime = 0.16; // 160ms recoil
    }

    triggerCastAnimation() {
        this.castAnimationTime = 0.32; // 320ms magical raise
    }

    spawnFloatingText(text, worldPos, colorHex = '#ffcc00', scale = 1.0) {
        const canvas = document.createElement('canvas');
        canvas.width = 384;
        canvas.height = 96;
        const ctx = canvas.getContext('2d');
        ctx.font = '900 36px "Cinzel", serif';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';

        // Dark glowing outline for crisp readability against any 3D backdrop
        ctx.strokeStyle = 'rgba(0, 0, 0, 0.95)';
        ctx.lineWidth = 6;
        ctx.strokeText(text, 192, 48);

        ctx.fillStyle = colorHex;
        ctx.fillText(text, 192, 48);

        const tex = new THREE.CanvasTexture(canvas);
        tex.minFilter = THREE.LinearFilter;
        const mat = new THREE.SpriteMaterial({ map: tex, depthTest: false, depthWrite: false, transparent: true });
        const sprite = new THREE.Sprite(mat);
        sprite.scale.set(1.4 * scale, 0.35 * scale, 1.0);
        sprite.position.copy(worldPos);

        this.scene.add(sprite);
        this.floatingTexts.push({
            sprite: sprite,
            elapsed: 0,
            duration: 0.75,
            basePos: worldPos.clone(),
            material: mat
        });
    }

    spawnHitSparks(worldPos, colorHex = '#ffaa33', count = 14) {
        const pGroup = new THREE.Group();
        pGroup.position.copy(worldPos);
        const col = new THREE.Color(colorHex);
        const sparkMat = new THREE.MeshBasicMaterial({ color: col, transparent: true, opacity: 1.0 });
        const sparkGeo = new THREE.SphereGeometry(0.025, 4, 4);

        const sparks = [];
        for (let i = 0; i < count; i++) {
            const m = new THREE.Mesh(sparkGeo, sparkMat);
            const theta = Math.random() * Math.PI * 2;
            const phi = (Math.random() - 0.5) * Math.PI;
            const speed = 1.2 + Math.random() * 2.2;
            const vel = new THREE.Vector3(
                Math.cos(theta) * Math.cos(phi) * speed,
                Math.sin(phi) * speed + 1.2,
                Math.sin(theta) * Math.cos(phi) * speed
            );
            pGroup.add(m);
            sparks.push({ mesh: m, vel: vel });
        }

        this.scene.add(pGroup);
        this.particles.push({
            group: pGroup,
            sparks: sparks,
            elapsed: 0,
            duration: 0.38,
            material: sparkMat
        });
    }

    addTrauma(amount) {
        this.trauma = Math.min(1.0, this.trauma + amount);
    }

    turn(delta) {
        this.facing = ((this.facing + delta) % 4 + 4) % 4;
        this.targetYaw = -this.facing * (Math.PI / 2);
    }

    get yaw() {
        return this.camera ? this.camera.rotation.y : (-this.facing * (Math.PI / 2));
    }

    calculateStereoPan(worldX, worldZ) {
        if (!this.camera) return 0.0;
        const dx = worldX - this.currentCamPos.x;
        const dz = worldZ - this.currentCamPos.z;
        const dist = Math.sqrt(dx * dx + dz * dz);
        if (dist < 0.1) return 0.0;
        const yaw = this.camera.rotation.y;
        const rightX = Math.cos(yaw);
        const rightZ = -Math.sin(yaw);
        const lateral = (dx * rightX + dz * rightZ) / dist;
        return Math.max(-1.0, Math.min(1.0, lateral));
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

    getFlagAt(map, x, y) {
        if (!map || !map.rows || y < 0 || y >= map.h || x < 0 || x >= map.w) return 0;
        const row = map.rows[y];
        if (!row || !row.l || x >= row.l.length) return 0;
        return parseInt(row.l[x], 16) || 0;
    }

    isSolidWall(feat) {
        return feat === 15 || (feat >= 17 && feat <= 22);
    }

    isStoreKind(feat) {
        return feat >= 7 && feat <= 14;
    }

    isWalkableOrPortal(feat) {
        return feat === 1 || feat === 2 || feat === 3 || feat === 4 || feat === 5 || feat === 6 || feat === 16 || feat === 23 || feat === 24 || this.isStoreKind(feat);
    }

    /**
     * Computes final tile shade without allocating new Color objects.
     * Reuses targetColor in-place (1:1 with Godot DungeonWorld.cs:2453-2471).
     */
    computeTileShade(targetColor, baseColor, inView, outdoors, lighting) {
        targetColor.copy(baseColor);
        if (inView && (outdoors || lighting === 2 || lighting === 0 || lighting === 1)) {
            return targetColor;
        } else if (inView && lighting === 3) {
            return targetColor.multiply(this._tintDark);
        } else {
            const memTint = (lighting === 2) ? this._tintMemLit : this._tintMemDark;
            return targetColor.multiply(memTint);
        }
    }

    determineDoorOrientation(map, x, y, w, h) {
        const featN = this.getFeatAt(map, x, y - 1);
        const featS = this.getFeatAt(map, x, y + 1);
        const featW = this.getFeatAt(map, x - 1, y);
        const featE = this.getFeatAt(map, x + 1, y);

        const wallN = this.isSolidWall(featN) || y <= 0;
        const wallS = this.isSolidWall(featS) || y >= h - 1;
        const wallW = this.isSolidWall(featW) || x <= 0;
        const wallE = this.isSolidWall(featE) || x >= w - 1;

        const walkN = this.isWalkableOrPortal(featN);
        const walkS = this.isWalkableOrPortal(featS);
        const walkW = this.isWalkableOrPortal(featW);
        const walkE = this.isWalkableOrPortal(featE);

        const scoreX = (wallW ? 3 : 0) + (wallE ? 3 : 0) + (walkN ? 1 : 0) + (walkS ? 1 : 0);
        const scoreZ = (wallN ? 3 : 0) + (wallS ? 3 : 0) + (walkW ? 1 : 0) + (walkE ? 1 : 0);

        if (scoreZ > scoreX) return Math.PI / 2;
        return 0;
    }

    determineShopDoorOrientation(map, x, y, w, h) {
        const featN = this.getFeatAt(map, x, y - 1);
        const featS = this.getFeatAt(map, x, y + 1);
        const featW = this.getFeatAt(map, x - 1, y);
        const featE = this.getFeatAt(map, x + 1, y);

        const bldgN = this.isSolidWall(featN) || this.isStoreKind(featN) || y <= 0;
        const bldgS = this.isSolidWall(featS) || this.isStoreKind(featS) || y >= h - 1;
        const bldgW = this.isSolidWall(featW) || this.isStoreKind(featW) || x <= 0;
        const bldgE = this.isSolidWall(featE) || this.isStoreKind(featE) || x >= w - 1;

        const streetN = !bldgN;
        const streetS = !bldgS;
        const streetW = !bldgW;
        const streetE = !bldgE;

        // Facing exterior street (middle of building edges)
        if (bldgN && streetS && bldgW && bldgE) return 0; // South (+Z)
        if (bldgS && streetN && bldgW && bldgE) return Math.PI; // North (-Z)
        if (bldgW && streetE && bldgN && bldgS) return Math.PI / 2; // East (+X)
        if (bldgE && streetW && bldgN && bldgS) return -Math.PI / 2; // West (-X)

        // Corner cases (building on 2 adjacent sides, street on opposite sides - 1:1 with Godot DungeonWorld.cs:2261-2265)
        if (bldgN && bldgE && streetS && streetW) return 0; // SW corner -> Face South
        if (bldgN && bldgW && streetS && streetE) return 0; // SE corner -> Face South
        if (bldgS && bldgE && streetN && streetW) return Math.PI; // NW corner -> Face North
        if (bldgS && bldgW && streetN && streetE) return Math.PI; // NE corner -> Face North

        if (streetS) return 0;
        if (streetN) return Math.PI;
        if (streetE) return Math.PI / 2;
        if (streetW) return -Math.PI / 2;

        return 0;
    }

    update(frame) {
        if (!frame || !frame.map) return;
        this.updateDungeon(frame);
    }

    updateDungeon(frame) {
        const map = frame.map;
        const w = map.w || (map.rows && map.rows[0] && map.rows[0].f ? map.rows[0].f.length / 2 : 0);
        const h = map.h || (map.rows ? map.rows.length : 0);
        if (!w || !h) return;
        this.lastMap = map;

        const px = frame.player ? frame.player.x : Math.floor(w / 2);
        const py = frame.player ? frame.player.y : Math.floor(h / 2);
        const depth = (frame.player && frame.player.depth !== undefined) ? frame.player.depth : 0;
        const outdoors = depth === 0; // Town is outdoors under daylight

        // Detect stairs / depth transitions
        if (this.lastDepth !== null && this.lastDepth !== undefined && this.lastDepth !== depth) {
            if (this.audio) {
                this.audio.playStairs(depth > this.lastDepth);
                this.audio.playLevelEnter();
            }
        }
        this.lastDepth = depth;

        // Update Viewmodel Equipment rules & Dynamic character height
        this.updateViewmodel(frame.player);

        // Apply 6-Tier Depth Biomes & Atmospheric Lighting (Exact Parity with Godot BiomeProfile & GRAPHICS_HANDOVER.md)
        const biome = this.getBiomeProfile(depth);
        this.currentBiome = biome;
        this.targetTorchEnergy = biome.torchEnergy;

        this.scene.background.setHex(outdoors ? biome.bgColor : biome.fogColor);

        this.sunLight.visible = biome.sunLight;
        this.sunLight.intensity = biome.sunEnergy;

        this.ambientLight.color.setHex(biome.ambientColor);
        this.ambientLight.intensity = biome.ambientEnergy;

        this.renderer.toneMappingExposure = biome.exposure;

        // Extract torch radius from player (cur_light or light)
        const lightProp = (frame.player && (frame.player.cur_light !== undefined ? frame.player.cur_light : frame.player.light));
        this.torchRadius = Math.max(1, typeof lightProp === 'number' ? lightProp : 1);

        if (outdoors) {
            if (!(this.scene.fog instanceof THREE.FogExp2)) {
                this.scene.fog = new THREE.FogExp2(biome.fogColor, biome.fogDensity);
            } else {
                this.scene.fog.color.setHex(biome.fogColor);
                this.scene.fog.density = biome.fogDensity;
            }
        } else {
            // Subterranean dungeon: atmospheric misty blackness fading smoothly into dark void
            const dungeonFogDensity = Math.max(0.024, biome.fogDensity * 2.2);
            if (!(this.scene.fog instanceof THREE.FogExp2)) {
                this.scene.fog = new THREE.FogExp2(biome.fogColor, dungeonFogDensity);
            } else {
                this.scene.fog.color.setHex(biome.fogColor);
                this.scene.fog.density = dungeonFogDensity;
            }
        }

        // Torch distance calculation matching Godot DungeonWorld.cs:3468:
        // _torch.OmniRange = (_torchRadius + 2.5f) * Cell + 2.0f;
        const torchRange = (this.torchRadius + 2.5) * this.cellSize + 2.0;
        this.torchLight.color.setHex(biome.torchColor);
        this.torchLight.distance = torchRange;
        this.torchLight.decay = 1.0;

        // Forward vector for camera-relative VFX placement
        const fwdYaw = this.camera ? this.camera.rotation.y : 0;
        const forward = new THREE.Vector3(0, 0, -1).applyAxisAngle(new THREE.Vector3(0, 1, 0), fwdYaw);
        const spawnInFront = this.currentCamPos.clone().addScaledVector(forward, 1.1).add(new THREE.Vector3(0, 0.2, 0));

        // Player HP delta tracking for floating damage/healing & camera trauma
        if (frame.player) {
            const curHp = frame.player.hp !== undefined ? frame.player.hp : null;
            if (curHp !== null && this.lastPlayerHp !== null) {
                if (curHp < this.lastPlayerHp) {
                    const dmg = this.lastPlayerHp - curHp;
                    this.spawnFloatingText(`-${dmg}`, spawnInFront, '#ff3333', 1.25);
                    this.addTrauma(Math.min(0.65, dmg / (frame.player.hp_max || 20)));
                    this.spawnHitSparks(spawnInFront, '#ff2222', 14);
                    this.triggerHurtAnimation();
                    if (this.audio) this.audio.playPlayerHurt();
                } else if (curHp > this.lastPlayerHp) {
                    const heal = curHp - this.lastPlayerHp;
                    this.spawnFloatingText(`+${heal}`, spawnInFront, '#33ff66', 1.15);
                    if (this.audio) this.audio.playHeal();
                }
            }
            this.lastPlayerHp = curHp;
        }

        // Track Active Status Conditions from Engine Bridge
        if (frame.player && frame.player.statuses && Array.isArray(frame.player.statuses)) {
            const currentStatuses = new Set();
            for (const st of frame.player.statuses) {
                const sName = (st.name || '').toLowerCase();
                currentStatuses.add(sName);
                if (!this.lastPlayerStatuses.has(sName) && this.audio) {
                    if (sName.includes('poison')) this.audio.playPoison();
                    else if (sName.includes('confus')) this.audio.playConfused();
                    else if (sName.includes('blind')) this.audio.playBlind();
                    else if (sName.includes('paralyz')) this.audio.playParalyzed();
                    else if (sName.includes('afraid') || sName.includes('fear')) this.audio.playAfraid();
                    else if (sName.includes('starv') || sName.includes('hungr')) this.audio.playHunger();
                }
            }
            this.lastPlayerStatuses = currentStatuses;
        }

        // Parse recent game messages for kinetic combat feedback ("Juice") (1:1 with Godot DungeonWorld.cs)
        if (frame.messages && Array.isArray(frame.messages)) {
            for (const msgObj of frame.messages) {
                const msgText = typeof msgObj === 'string' ? msgObj : (msgObj && msgObj.text ? msgObj.text : '');
                if (!msgText || this.processedMessages.has(msgText)) continue;
                this.processedMessages.add(msgText);
                if (this.processedMessages.size > 200) {
                    const first = this.processedMessages.values().next().value;
                    this.processedMessages.delete(first);
                }

                const lower = msgText.toLowerCase();
                if (lower.includes('critical hit') || lower.includes('great force') || lower.includes('superb')) {
                    this.addTrauma(0.25);
                    this.spawnFloatingText('CRITICAL!', spawnInFront.clone().add(new THREE.Vector3(0, 0.25, 0)), '#ffd700', 1.35);
                    this.spawnHitSparks(spawnInFront, '#ffe066', 18);
                    this.triggerAttackAnimation();
                    if (this.audio) this.audio.playCrit();
                } else if (lower.includes('you hit') || lower.includes('you strike') || lower.includes('you slash') || lower.includes('you crush') || lower.includes('you smite')) {
                    this.spawnFloatingText('HIT', spawnInFront, '#ffaa33', 1.05);
                    this.spawnHitSparks(spawnInFront, '#ff9933', 12);
                    this.triggerAttackAnimation();
                    if (this.audio) this.audio.playHit(false);
                } else if (lower.includes('deflect') || lower.includes('blocks the blow') || lower.includes('parr') || lower.includes('bounces off')) {
                    this.spawnFloatingText('BLOCK', spawnInFront, '#55bbff', 1.05);
                    this.spawnHitSparks(spawnInFront, '#88ddff', 10);
                    if (this.audio) this.audio.playShieldBlock();
                } else if ((lower.includes('wall') || lower.includes('door') || lower.includes('rubble')) && (lower.includes('blocking your way') || lower.includes('cannot see where you are going') || lower.includes('in your way'))) {
                    this.addTrauma(0.08);
                    this.spawnFloatingText('BLOCKED', spawnInFront, '#aaaaaa', 0.95);
                    if (this.audio) this.audio.playWallBump();
                } else if (lower.includes('you miss') || lower.includes('misses you')) {
                    if (lower.includes('you miss')) this.triggerAttackAnimation();
                    this.spawnFloatingText('MISS', spawnInFront, '#a0b0c0', 0.95);
                    if (this.audio) this.audio.playWhoosh();
                } else if (lower.includes('you cast') || lower.includes('you zap') || lower.includes('you aim') || lower.includes('you recite')) {
                    this.triggerCastAnimation();
                    this.spawnFloatingText('CAST', spawnInFront, '#66ccff', 1.10);
                    let el = 'magic';
                    if (lower.includes('fire') || lower.includes('flame')) el = 'fire';
                    else if (lower.includes('frost') || lower.includes('cold') || lower.includes('ice')) el = 'cold';
                    else if (lower.includes('lightning') || lower.includes('elec') || lower.includes('spark')) el = 'lightning';
                    else if (lower.includes('poison') || lower.includes('acid')) el = 'poison';
                    if (this.audio) this.audio.playSpell(el);
                } else if (lower.includes('you shoot') || lower.includes('you fire')) {
                    this.spawnFloatingText('SHOOT', spawnInFront, '#ffdd55', 1.05);
                    this.triggerAttackAnimation();
                    if (this.audio) this.audio.playBowShoot();
                } else if (lower.includes('you have slain') || lower.includes('is destroyed') || lower.includes('dies.')) {
                    this.spawnFloatingText('SLAIN!', spawnInFront.clone().add(new THREE.Vector3(0, 0.25, 0)), '#ff44aa', 1.30);
                    this.spawnHitSparks(spawnInFront, '#ff44aa', 24);
                    if (this.audio) this.audio.playMonsterDeath();
                } else if (lower.includes('hits you') || lower.includes('bites you') || lower.includes('claws you') || lower.includes('crushes you') || lower.includes('stings you') || lower.includes('burns you')) {
                    this.addTrauma(0.35);
                    this.spawnFloatingText('OUCH!', spawnInFront, '#ff3333', 1.15);
                    this.spawnHitSparks(spawnInFront, '#ff2222', 14);
                    this.triggerHurtAnimation();
                    if (this.audio) this.audio.playPlayerHurt();
                } else if (lower.includes('you open the door') || lower.includes('the door opens')) {
                    if (this.audio) this.audio.playDoor(true);
                } else if (lower.includes('you close the door') || lower.includes('the door closes')) {
                    if (this.audio) this.audio.playDoor(false);
                } else if (lower.includes('door is broken') || lower.includes('smash open the door') || lower.includes('door is destroyed')) {
                    if (this.audio) this.audio.playDoorBreak();
                } else if (lower.includes('quaff') || lower.includes('drink')) {
                    if (this.audio) this.audio.playQuaff();
                } else if (lower.includes('read') || lower.includes('scroll')) {
                    if (this.audio) this.audio.playScroll();
                } else if (lower.includes('gold') || lower.includes('coins')) {
                    if (this.audio) this.audio.playGoldPickup();
                } else if (lower.includes('you are wielding') || lower.includes('wielding ') || lower.includes('you wear') || lower.includes('you are wearing')) {
                    if (lower.includes('sword') || lower.includes('dagger') || lower.includes('blade') || lower.includes('axe') || lower.includes('spear') || lower.includes('mace') || lower.includes('bow')) {
                        if (this.audio) this.audio.playEquipWeapon();
                    } else {
                        if (this.audio) this.audio.playEquipArmor();
                    }
                } else if (lower.includes('you drop') || lower.includes('dropped') || lower.includes('were wielding') || lower.includes('were wearing') || lower.includes('unwield')) {
                    if (this.audio) this.audio.playItemDrop();
                } else if (lower.includes('you eat') || lower.includes('delicious') || lower.includes('tastes like') || lower.includes('you devour') || lower.includes('ration') || lower.includes('hard biscuit')) {
                    this.spawnFloatingText('EAT', spawnInFront, '#ffcc66', 1.05);
                    if (this.audio) this.audio.playEat();
                } else if (lower.includes('chest') && (lower.includes('open') || lower.includes('unlock'))) {
                    if (this.audio) this.audio.playChest();
                } else if (lower.includes('disarm') && lower.includes('trap')) {
                    this.spawnFloatingText('DISARMED', spawnInFront, '#33ffaa', 1.10);
                    if (this.audio) this.audio.playTrapDisarm();
                } else if ((lower.includes('set off') || lower.includes('trigger') || lower.includes('spring')) && lower.includes('trap')) {
                    this.addTrauma(0.25);
                    this.spawnFloatingText('TRAP!', spawnInFront, '#ff4444', 1.25);
                    if (this.audio) this.audio.playTrapTrigger();
                } else if (lower.includes('teleport') || lower.includes('phase door') || lower.includes('space distortion') || lower.includes('disappears!')) {
                    this.spawnFloatingText('WARP', spawnInFront, '#cc66ff', 1.20);
                    if (this.audio) this.audio.playTeleport();
                } else if (lower.includes('poisoned') || lower.includes('feel very sick') || lower.includes('poison burns')) {
                    if (this.audio) this.audio.playPoison();
                } else if (lower.includes('confused') || lower.includes('feel dazed') || lower.includes('feel very dizzy')) {
                    if (this.audio) this.audio.playConfused();
                } else if (lower.includes('you are blind') || lower.includes('cloak of darkness falls') || lower.includes('cannot see')) {
                    if (this.audio) this.audio.playBlind();
                } else if (lower.includes('paralyzed') || lower.includes('frozen in place') || lower.includes('cannot move')) {
                    if (this.audio) this.audio.playParalyzed();
                } else if (lower.includes('terrified') || lower.includes('flee in terror') || lower.includes('afraid')) {
                    if (this.audio) this.audio.playAfraid();
                } else if (lower.includes('starving') || lower.includes('faint with hunger') || lower.includes('getting hungry')) {
                    if (this.audio) this.audio.playHunger();
                } else if (lower.includes('pick up') || lower.includes('found')) {
                    if (this.audio) this.audio.playItemPickup();
                } else if (lower.includes('level') && (lower.includes('welcome to') || lower.includes('gain') || lower.includes('advance'))) {
                    if (this.audio) this.audio.playLevelUp();
                }
            }
        }

        this.targetCamPos.set(px * this.cellSize, this.eyeHeight, py * this.cellSize);

        const dist = this.currentCamPos.distanceTo(this.targetCamPos);
        if (dist > this.cellSize * 1.5) {
            this.currentCamPos.copy(this.targetCamPos);
            this.isStepping = false;
            this.stepPlayedThisMove = false;
        } else if (dist > 0.05) {
            this.isStepping = true;
            this.stepTime = 0;
            const now = performance.now();
            if (!this.lastStepSoundTime) this.lastStepSoundTime = 0;
            if (!this.stepPlayedThisMove && (now - this.lastStepSoundTime > 200)) {
                this.stepPlayedThisMove = true;
                this.lastStepSoundTime = now;
                if (this.audio) {
                    this.audio.playFootstep(outdoors, this.heightRatio || 1.0);
                }
            }
        }

        // MultiMesh instance counts reset
        let wallCount = 0;
        let floorCount = 0;
        let ceilingCount = 0;
        let doorFrameCount = 0;
        let shopFrameCount = 0;
        let doorCount = 0;
        let doorOpenCount = 0;
        let doorBrokenCount = 0;
        let lavaCount = 0;
        let lavaCooledCount = 0;
        let stairsCount = 0;
        let stairsUpCount = 0;
        let magmaCount = 0;
        let quartzCount = 0;
        let rubbleCount = 0;
        const shopCounts = [0, 0, 0, 0, 0, 0, 0, 0];

        const sightRange = outdoors ? 45 : 28;
        const minX = Math.max(0, px - sightRange);
        const maxX = Math.min(w - 1, px + sightRange);
        const minY = Math.max(0, py - sightRange);
        const maxY = Math.min(h - 1, py + sightRange);

        for (let y = minY; y <= maxY; y++) {
            for (let x = minX; x <= maxX; x++) {
                let feat = this.getFeatAt(map, x, y);
                const flag = this.getFlagAt(map, x, y);

                let known = outdoors || (flag & 0x1) !== 0;
                let inView = outdoors || (flag & 0x2) !== 0;
                let lighting = (flag >> 2) & 0x3; // 0=LOS lit, 1=torch, 2=room lit, 3=dark

                // Unexplored dark space (not known and not in view) or feat 0 must NOT be rendered.
                // True fog-of-war (1:1 with Godot DungeonWorld.cs:2410-2415):
                // Open unexplored areas remain pure misty blackness fading into subterranean depth fog.
                if (feat === 0 || (!known && !inView)) {
                    continue;
                }

                const isWall = feat === 15 || feat === 21 || feat === 22 || feat === 17 || feat === 18 || feat === 19 || feat === 20;

                // Interior rock culling (1:1 with Godot DungeonWorld.cs:2386-2412):
                // If all 8 neighbors are solid walls/skip (no adjacent floor, door, rubble, or open space),
                // this wall is completely buried within the dungeon bedrock and has no exposed visible faces.
                if (isWall) {
                    let hasAdjacentOpen = false;
                    for (let dy = -1; dy <= 1 && !hasAdjacentOpen; dy++) {
                        for (let dx = -1; dx <= 1 && !hasAdjacentOpen; dx++) {
                            if (dx === 0 && dy === 0) continue;
                            const nx = x + dx;
                            const ny = y + dy;
                            if (nx >= 0 && nx < w && ny >= 0 && ny < h) {
                                const nFeat = this.getFeatAt(map, nx, ny);
                                if (this.isWalkableOrPortal(nFeat)) {
                                    hasAdjacentOpen = true;
                                }
                            }
                        }
                    }
                    if (!hasAdjacentOpen) {
                        continue;
                    }
                }

                // In subterranean dungeon (!outdoors), do not render memorized floors/ceilings
                // that are outside line of sight and outside the immediate torchlight vicinity.
                // This eliminates the glitch where distant memorized rooms float across the black void!
                if (!outdoors && !inView) {
                    const distSq = (x - px) * (x - px) + (y - py) * (y - py);
                    if (distSq > (this.torchRadius + 3) * (this.torchRadius + 3)) {
                        continue;
                    }
                }

                // Distance check for illumination (1:1 with Godot DungeonWorld.cs:2443-2451)
                if (lighting === 3 && inView) {
                    const distSq = (x - px) * (x - px) + (y - py) * (y - py);
                    if (distSq <= (this.torchRadius + 1) * (this.torchRadius + 1)) {
                        lighting = 1;
                    }
                }

                // Tile shade computation without heap allocations (1:1 with Godot DungeonWorld.cs:2453-2471)
                const wallShade = this.computeTileShade(this._scratchWallColor, biome.wallColor, inView, outdoors, lighting);
                const floorShade = this.computeTileShade(this._scratchFloorColor, biome.floorColor, inView, outdoors, lighting);
                const ceilingShade = this.computeTileShade(this._scratchCeilingColor, biome.ceilingColor, inView, outdoors, lighting);

                const wx = x * this.cellSize;
                const wz = y * this.cellSize;
                const wallYCenter = (this.wallHeight - 0.15) / 2; // Bottom sinks into floor at Y=-0.15, top at Y=3.00

                // Magma fissures (list-terrain.h feat 17=MAGMA, 19=MAGMA_K)
                if (feat === 17 || feat === 19) {
                    // Seal chamfered corner base with floor quad
                    this.dummy.position.set(wx, -0.05, wz);
                    this.dummy.rotation.set(0, 0, 0);
                    this.dummy.updateMatrix();
                    this.floorMesh.setMatrixAt(floorCount, this.dummy.matrix);
                    this.floorMesh.setColorAt(floorCount, floorShade);
                    floorCount++;

                    this.dummy.position.set(wx, wallYCenter, wz);
                    this.dummy.rotation.set(0, 0, 0);
                    this.dummy.updateMatrix();
                    this.magmaMesh.setMatrixAt(magmaCount, this.dummy.matrix);
                    this.magmaMesh.setColorAt(magmaCount, wallShade);
                    magmaCount++;
                    continue;
                }

                // Quartz crystal veins (list-terrain.h feat 18=QUARTZ, 20=QUARTZ_K)
                if (feat === 18 || feat === 20) {
                    // Seal chamfered corner base with floor quad
                    this.dummy.position.set(wx, -0.05, wz);
                    this.dummy.rotation.set(0, 0, 0);
                    this.dummy.updateMatrix();
                    this.floorMesh.setMatrixAt(floorCount, this.dummy.matrix);
                    this.floorMesh.setColorAt(floorCount, floorShade);
                    floorCount++;

                    this.dummy.position.set(wx, wallYCenter, wz);
                    this.dummy.rotation.set(0, 0, 0);
                    this.dummy.updateMatrix();
                    this.quartzMesh.setMatrixAt(quartzCount, this.dummy.matrix);
                    this.quartzMesh.setColorAt(quartzCount, wallShade);
                    quartzCount++;
                    continue;
                }

                // Solid Stone / Granite / Perm / Secret wall (feat 15=SECRET, 21=GRANITE, 22=PERM)
                if (feat === 15 || feat === 21 || feat === 22) {
                    let hasExposedFace = false;
                    for (let dy = -1; dy <= 1; dy++) {
                        for (let dx = -1; dx <= 1; dx++) {
                            if (dx === 0 && dy === 0) continue;
                            const nx = x + dx;
                            const ny = y + dy;
                            if (nx >= 0 && nx < w && ny >= 0 && ny < h) {
                                const nFeat = this.getFeatAt(map, nx, ny);
                                if (this.isWalkableOrPortal(nFeat)) {
                                    hasExposedFace = true;
                                    break;
                                }
                            }
                        }
                        if (hasExposedFace) break;
                    }
                    if (!hasExposedFace) continue; // Interior bedrock culling

                    // Seal chamfered corner base with floor quad
                    this.dummy.position.set(wx, -0.05, wz);
                    this.dummy.rotation.set(0, 0, 0);
                    this.dummy.updateMatrix();
                    this.floorMesh.setMatrixAt(floorCount, this.dummy.matrix);
                    this.floorMesh.setColorAt(floorCount, floorShade);
                    floorCount++;

                    this.dummy.position.set(wx, wallYCenter, wz);
                    this.dummy.rotation.set(0, 0, 0);
                    this.dummy.updateMatrix();
                    this.wallMesh.setMatrixAt(wallCount, this.dummy.matrix);
                    this.wallMesh.setColorAt(wallCount, wallShade);
                    wallCount++;
                    continue;
                }

                // Store Entrances (feat 7 to 14) -> General Store, Armoury, Weaponsmith, Bookseller, Alchemy, Magic, Black Market, Home!
                if (feat >= 7 && feat <= 14) {
                    const shopIdx = feat - 7;
                    // Floor underneath shop entrance
                    this.dummy.position.set(wx, -0.05, wz);
                    this.dummy.rotation.set(0, 0, 0);
                    this.dummy.updateMatrix();
                    this.floorMesh.setMatrixAt(floorCount, this.dummy.matrix);
                    this.floorMesh.setColorAt(floorCount, floorShade);
                    floorCount++;

                    // Align full-depth stone masonry frame and heraldic entrance door to face outward into street
                    const doorYaw = this.determineShopDoorOrientation(map, x, y, w, h);
                    this.dummy.position.set(wx, 0, wz);
                    this.dummy.rotation.set(0, doorYaw, 0);
                    this.dummy.updateMatrix();

                    this.shopFrameMesh.setMatrixAt(shopFrameCount, this.dummy.matrix);
                    this.shopFrameMesh.setColorAt(shopFrameCount, wallShade);
                    shopFrameCount++;

                    this.shopMeshes[shopIdx].setMatrixAt(shopCounts[shopIdx], this.dummy.matrix);
                    this.shopMeshes[shopIdx].setColorAt(shopCounts[shopIdx], wallShade);
                    shopCounts[shopIdx]++;

                    if (!outdoors) {
                        this.dummy.position.set(wx, this.wallHeight, wz);
                        this.dummy.rotation.set(0, 0, 0);
                        this.dummy.updateMatrix();
                        this.ceilingMesh.setMatrixAt(ceilingCount, this.dummy.matrix);
                        this.ceilingMesh.setColorAt(ceilingCount, ceilingShade);
                        ceilingCount++;
                    }
                    continue;
                }

                // Closed Dungeon Door (feat 2)
                if (feat === 2) {
                    this.dummy.position.set(wx, -0.05, wz);
                    this.dummy.rotation.set(0, 0, 0);
                    this.dummy.updateMatrix();
                    this.floorMesh.setMatrixAt(floorCount, this.dummy.matrix);
                    this.floorMesh.setColorAt(floorCount, floorShade);
                    floorCount++;

                    const doorYaw = this.determineDoorOrientation(map, x, y, w, h);
                    this.dummy.position.set(wx, 0, wz);
                    this.dummy.rotation.set(0, doorYaw, 0);
                    this.dummy.updateMatrix();

                    this.doorFrameMesh.setMatrixAt(doorFrameCount, this.dummy.matrix);
                    this.doorFrameMesh.setColorAt(doorFrameCount, wallShade);
                    doorFrameCount++;

                    this.doorMesh.setMatrixAt(doorCount, this.dummy.matrix);
                    this.doorMesh.setColorAt(doorCount, wallShade);
                    doorCount++;

                    if (!outdoors) {
                        this.dummy.position.set(wx, this.wallHeight, wz);
                        this.dummy.rotation.set(0, 0, 0);
                        this.dummy.updateMatrix();
                        this.ceilingMesh.setMatrixAt(ceilingCount, this.dummy.matrix);
                        this.ceilingMesh.setColorAt(ceilingCount, ceilingShade);
                        ceilingCount++;
                    }
                    continue;
                }

                // Open Dungeon Door (feat 3) — Swung-open door leaf angled ajar 70° (DungeonWorld.cs:1594-1631)
                if (feat === 3) {
                    this.dummy.position.set(wx, -0.05, wz);
                    this.dummy.rotation.set(0, 0, 0);
                    this.dummy.updateMatrix();
                    this.floorMesh.setMatrixAt(floorCount, this.dummy.matrix);
                    this.floorMesh.setColorAt(floorCount, floorShade);
                    floorCount++;

                    const doorYaw = this.determineDoorOrientation(map, x, y, w, h);
                    this.dummy.position.set(wx, 0, wz);
                    this.dummy.rotation.set(0, doorYaw, 0);
                    this.dummy.updateMatrix();

                    this.doorFrameMesh.setMatrixAt(doorFrameCount, this.dummy.matrix);
                    this.doorFrameMesh.setColorAt(doorFrameCount, wallShade);
                    doorFrameCount++;

                    this.doorOpenMesh.setMatrixAt(doorOpenCount, this.dummy.matrix);
                    this.doorOpenMesh.setColorAt(doorOpenCount, wallShade);
                    doorOpenCount++;

                    if (!outdoors) {
                        this.dummy.position.set(wx, this.wallHeight, wz);
                        this.dummy.rotation.set(0, 0, 0);
                        this.dummy.updateMatrix();
                        this.ceilingMesh.setMatrixAt(ceilingCount, this.dummy.matrix);
                        this.ceilingMesh.setColorAt(ceilingCount, ceilingShade);
                        ceilingCount++;
                    }
                    continue;
                }

                // Broken Dungeon Door (feat 4) — Shattered wood planks on floor & hinge remnant (DungeonWorld.cs:1632-1643)
                if (feat === 4) {
                    this.dummy.position.set(wx, -0.05, wz);
                    this.dummy.rotation.set(0, 0, 0);
                    this.dummy.updateMatrix();
                    this.floorMesh.setMatrixAt(floorCount, this.dummy.matrix);
                    this.floorMesh.setColorAt(floorCount, floorShade);
                    floorCount++;

                    const doorYaw = this.determineDoorOrientation(map, x, y, w, h);
                    this.dummy.position.set(wx, 0, wz);
                    this.dummy.rotation.set(0, doorYaw, 0);
                    this.dummy.updateMatrix();

                    this.doorFrameMesh.setMatrixAt(doorFrameCount, this.dummy.matrix);
                    this.doorFrameMesh.setColorAt(doorFrameCount, wallShade);
                    doorFrameCount++;

                    this.doorBrokenMesh.setMatrixAt(doorBrokenCount, this.dummy.matrix);
                    this.doorBrokenMesh.setColorAt(doorBrokenCount, wallShade);
                    doorBrokenCount++;

                    if (!outdoors) {
                        this.dummy.position.set(wx, this.wallHeight, wz);
                        this.dummy.rotation.set(0, 0, 0);
                        this.dummy.updateMatrix();
                        this.ceilingMesh.setMatrixAt(ceilingCount, this.dummy.matrix);
                        this.ceilingMesh.setColorAt(ceilingCount, ceilingShade);
                        ceilingCount++;
                    }
                    continue;
                }

                // Floor / Walkable corridor (feat 1, 24)
                if (feat === 1 || feat === 24) {
                    this.dummy.position.set(wx, -0.05, wz);
                    this.dummy.rotation.set(0, 0, 0);
                    this.dummy.updateMatrix();
                    this.floorMesh.setMatrixAt(floorCount, this.dummy.matrix);
                    this.floorMesh.setColorAt(floorCount, floorShade);
                    floorCount++;

                    if (!outdoors) {
                        this.dummy.position.set(wx, this.wallHeight, wz);
                        this.dummy.rotation.set(0, 0, 0);
                        this.dummy.updateMatrix();
                        this.ceilingMesh.setMatrixAt(ceilingCount, this.dummy.matrix);
                        this.ceilingMesh.setColorAt(ceilingCount, ceilingShade);
                        ceilingCount++;
                    }
                    continue;
                }

                // Molten Lava (feat 23)
                if (feat === 23) {
                    this.dummy.position.set(wx, -0.05, wz);
                    this.dummy.rotation.set(0, 0, 0);
                    this.dummy.updateMatrix();
                    if (inView) {
                        this.lavaMesh.setMatrixAt(lavaCount++, this.dummy.matrix);
                    } else {
                        this.lavaCooledMesh.setMatrixAt(lavaCooledCount++, this.dummy.matrix);
                    }
                    if (!outdoors) {
                        this.dummy.position.set(wx, this.wallHeight, wz);
                        this.dummy.rotation.set(0, 0, 0);
                        this.dummy.updateMatrix();
                        this.ceilingMesh.setMatrixAt(ceilingCount, this.dummy.matrix);
                        this.ceilingMesh.setColorAt(ceilingCount, ceilingShade);
                        ceilingCount++;
                    }
                    continue;
                }

                // Stairs Up/Down (feat 5, 6)
                if (feat === 5 || feat === 6) {
                    this.dummy.position.set(wx, -0.05, wz);
                    this.dummy.rotation.set(0, 0, 0);
                    this.dummy.updateMatrix();
                    this.floorMesh.setMatrixAt(floorCount, this.dummy.matrix);
                    this.floorMesh.setColorAt(floorCount, floorShade);
                    floorCount++;

                    this.dummy.position.set(wx, 0, wz);
                    this.dummy.rotation.set(0, 0, 0);
                    this.dummy.updateMatrix();
                    if (feat === 5) {
                        this.stairsUpMesh.setMatrixAt(stairsUpCount, this.dummy.matrix);
                        this.stairsUpMesh.setColorAt(stairsUpCount, floorShade);
                        stairsUpCount++;
                    } else {
                        this.stairsMesh.setMatrixAt(stairsCount, this.dummy.matrix);
                        this.stairsMesh.setColorAt(stairsCount, floorShade);
                        stairsCount++;
                    }

                    if (!outdoors) {
                        this.dummy.position.set(wx, this.wallHeight, wz);
                        this.dummy.rotation.set(0, 0, 0);
                        this.dummy.updateMatrix();
                        this.ceilingMesh.setMatrixAt(ceilingCount, this.dummy.matrix);
                        this.ceilingMesh.setColorAt(ceilingCount, ceilingShade);
                        ceilingCount++;
                    }
                    continue;
                }

                // Impassable Rubble Piles (feat 16) — Tumbled stone masonry & jagged boulders (DungeonWorld.cs:1738-1753, 2522-2528)
                if (feat === 16) {
                    this.dummy.position.set(wx, -0.05, wz);
                    this.dummy.rotation.set(0, 0, 0);
                    this.dummy.updateMatrix();
                    this.floorMesh.setMatrixAt(floorCount, this.dummy.matrix);
                    this.floorMesh.setColorAt(floorCount, floorShade);
                    floorCount++;

                    this.dummy.position.set(wx, 0, wz);
                    // Procedural subtle rotation variation per tile so piles don't look identical
                    const rubbleYaw = ((x * 37 + y * 59) % 4) * (Math.PI / 2);
                    this.dummy.rotation.set(0, rubbleYaw, 0);
                    this.dummy.updateMatrix();
                    this.rubbleMesh.setMatrixAt(rubbleCount, this.dummy.matrix);
                    this.rubbleMesh.setColorAt(rubbleCount, wallShade);
                    rubbleCount++;

                    if (!outdoors) {
                        this.dummy.position.set(wx, this.wallHeight, wz);
                        this.dummy.rotation.set(0, 0, 0);
                        this.dummy.updateMatrix();
                        this.ceilingMesh.setMatrixAt(ceilingCount, this.dummy.matrix);
                        this.ceilingMesh.setColorAt(ceilingCount, ceilingShade);
                        ceilingCount++;
                    }
                    continue;
                }
            }
        }

        this.wallMesh.count = wallCount;
        this.wallMesh.instanceMatrix.needsUpdate = true;
        if (this.wallMesh.instanceColor) this.wallMesh.instanceColor.needsUpdate = true;

        this.floorMesh.count = floorCount;
        this.floorMesh.instanceMatrix.needsUpdate = true;
        if (this.floorMesh.instanceColor) this.floorMesh.instanceColor.needsUpdate = true;

        this.ceilingMesh.count = ceilingCount;
        this.ceilingMesh.instanceMatrix.needsUpdate = true;
        if (this.ceilingMesh.instanceColor) this.ceilingMesh.instanceColor.needsUpdate = true;

        this.doorFrameMesh.count = doorFrameCount;
        this.doorFrameMesh.instanceMatrix.needsUpdate = true;
        if (this.doorFrameMesh.instanceColor) this.doorFrameMesh.instanceColor.needsUpdate = true;

        this.shopFrameMesh.count = shopFrameCount;
        this.shopFrameMesh.instanceMatrix.needsUpdate = true;
        if (this.shopFrameMesh.instanceColor) this.shopFrameMesh.instanceColor.needsUpdate = true;

        this.rubbleMesh.count = rubbleCount;
        this.rubbleMesh.instanceMatrix.needsUpdate = true;
        if (this.rubbleMesh.instanceColor) this.rubbleMesh.instanceColor.needsUpdate = true;

        this.doorMesh.count = doorCount;
        this.doorMesh.instanceMatrix.needsUpdate = true;
        if (this.doorMesh.instanceColor) this.doorMesh.instanceColor.needsUpdate = true;

        this.doorOpenMesh.count = doorOpenCount;
        this.doorOpenMesh.instanceMatrix.needsUpdate = true;
        if (this.doorOpenMesh.instanceColor) this.doorOpenMesh.instanceColor.needsUpdate = true;

        this.doorBrokenMesh.count = doorBrokenCount;
        this.doorBrokenMesh.instanceMatrix.needsUpdate = true;
        if (this.doorBrokenMesh.instanceColor) this.doorBrokenMesh.instanceColor.needsUpdate = true;

        for (let i = 0; i < 8; i++) {
            this.shopMeshes[i].count = shopCounts[i];
            this.shopMeshes[i].instanceMatrix.needsUpdate = true;
            if (this.shopMeshes[i].instanceColor) this.shopMeshes[i].instanceColor.needsUpdate = true;
        }

        this.magmaMesh.count = magmaCount;
        this.magmaMesh.instanceMatrix.needsUpdate = true;
        if (this.magmaMesh.instanceColor) this.magmaMesh.instanceColor.needsUpdate = true;

        this.quartzMesh.count = quartzCount;
        this.quartzMesh.instanceMatrix.needsUpdate = true;
        if (this.quartzMesh.instanceColor) this.quartzMesh.instanceColor.needsUpdate = true;

        this.lavaMesh.count = lavaCount;
        this.lavaMesh.instanceMatrix.needsUpdate = true;

        this.lavaCooledMesh.count = lavaCooledCount;
        this.lavaCooledMesh.instanceMatrix.needsUpdate = true;

        this.stairsMesh.count = stairsCount;
        this.stairsMesh.instanceMatrix.needsUpdate = true;
        if (this.stairsMesh.instanceColor) this.stairsMesh.instanceColor.needsUpdate = true;

        this.stairsUpMesh.count = stairsUpCount;
        this.stairsUpMesh.instanceMatrix.needsUpdate = true;
        if (this.stairsUpMesh.instanceColor) this.stairsUpMesh.instanceColor.needsUpdate = true;

        this.updateMonsters(frame.monsters || [], frame.player);
        this.updateItems(frame.objects || frame.items || [], frame.player);
        this.updateTerrainLabels(map, w, h, depth);
    }

    updateTerrainLabels(map, w, h, depth) {
        if (!this.terrainLabelsGroup) return;

        const outdoors = depth === 0;
        const storeNames = [
            'General Store',
            'Armoury',
            'Weapon Smiths',
            'Bookseller',
            'Alchemy Shop',
            'Magic Shop',
            'Black Market',
            'Home'
        ];

        const storePositions = new Map();
        const stairsList = [];

        for (let y = 0; y < h; y++) {
            for (let x = 0; x < w; x++) {
                const flag = this.getFlagAt(map, x, y);
                const known = outdoors || (flag & 0x1) !== 0;
                const inView = outdoors || (flag & 0x2) !== 0;
                // STRICT FOG OF WAR: Never leak undiscovered space!
                if (!known && !inView) continue;

                const feat = this.getFeatAt(map, x, y);
                if (feat >= 7 && feat <= 14) {
                    if (!storePositions.has(feat)) {
                        storePositions.set(feat, []);
                    }
                    storePositions.get(feat).push({ x: x * this.cellSize, z: y * this.cellSize });
                } else if (feat === 5) {
                    const upText = depth === 1 ? '[<] Up to Town' : `[<] Up to ${(depth - 1) * 50}ft`;
                    stairsList.push({ x: x * this.cellSize, y: 1.1, z: y * this.cellSize, name: upText });
                } else if (feat === 6) {
                    const downText = depth === 0 ? '[>] Down to Dungeon (50\')' : `[>] Down to ${(depth + 1) * 50}ft`;
                    stairsList.push({ x: x * this.cellSize, y: 1.1, z: y * this.cellSize, name: downText });
                }
            }
        }

        // Signature check to avoid recreating sprites every frame
        let sig = `d${depth};`;
        for (const [feat, pts] of storePositions.entries()) {
            sig += `${feat}:${pts.length};`;
        }
        for (const s of stairsList) {
            sig += `${s.name}:${s.x},${s.z};`;
        }

        if (sig === this.lastTerrainLabelsSignature) return;
        this.lastTerrainLabelsSignature = sig;

        while (this.terrainLabelsGroup.children.length > 0) {
            this.terrainLabelsGroup.remove(this.terrainLabelsGroup.children[0]);
        }

        const createCaption = (text, pos, colorHex, isStore = false) => {
            const canvas = document.createElement('canvas');
            canvas.width = 512;
            canvas.height = 96;
            const ctx = canvas.getContext('2d');

            ctx.clearRect(0, 0, 512, 96);

            // Clean, crisp outlined text matching Godot Label3D (OutlineSize = 8, OutlineModulate = Black)
            ctx.font = isStore ? '700 30px "Cinzel", serif' : '700 24px "Cinzel", serif';
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';

            // Bold dark outline/shadow for high contrast
            ctx.strokeStyle = 'rgba(0, 0, 0, 0.95)';
            ctx.lineWidth = 7;
            ctx.strokeText(text, 256, 48);

            ctx.fillStyle = colorHex;
            ctx.fillText(text, 256, 48);

            const tex = new THREE.CanvasTexture(canvas);
            tex.minFilter = THREE.LinearFilter;
            // depthTest = true so buildings, walls, and pillars properly occlude labels
            const mat = new THREE.SpriteMaterial({ map: tex, depthTest: true, depthWrite: false, transparent: true });
            const sprite = new THREE.Sprite(mat);
            sprite.scale.set(isStore ? 2.6 : 2.0, isStore ? 0.48 : 0.38, 1.0);
            sprite.position.set(pos.x, pos.y, pos.z);
            sprite.maxDist = isStore ? 45.0 : 25.0; // Distance culling threshold
            return sprite;
        };

        // Shop Labels at Centroid (1:1 with Godot DungeonWorld.cs:2648-2670)
        for (const [feat, points] of storePositions.entries()) {
            const storeIdx = feat - 7;
            const storeNum = storeIdx + 1;
            const storeName = storeNames[storeIdx] || `Store ${storeNum}`;
            const labelText = `[${storeNum}] ${storeName}`;
            const col = getStoreColorHex(storeNum);

            let avgX = 0, avgZ = 0;
            for (const pt of points) {
                avgX += pt.x;
                avgZ += pt.z;
            }
            avgX /= points.length;
            avgZ /= points.length;

            const sprite = createCaption(labelText, { x: avgX, y: this.wallHeight + 0.55, z: avgZ }, col, true);
            this.terrainLabelsGroup.add(sprite);
        }

        // Stairs Labels
        for (const s of stairsList) {
            const sprite = createCaption(s.name, { x: s.x, y: s.y, z: s.z }, '#88ccff', false);
            this.terrainLabelsGroup.add(sprite);
        }
    }

    createNameplateSprite(m, isTargeted) {
        const canvas = document.createElement('canvas');
        canvas.width = 384;
        canvas.height = 110;
        const ctx = canvas.getContext('2d');

        const race = m.race || m.name || 'creature';
        const glyph = m.glyph || '?';
        const colorHex = getAngbandColorString(m.attr);
        const hp = m.hp !== undefined ? m.hp : 1;
        const hpMax = m.hp_max !== undefined ? m.hp_max : 1;
        const pct = Math.max(0, Math.min(1, hpMax > 0 ? hp / hpMax : 1));

        ctx.clearRect(0, 0, 384, 110);

        let curY = 6;

        // Line 1: Status badge (e.g. 💤 Zzz... matching Godot MonsterModelResolver.cs:500-504)
        const isSensed = m.invisible || m.detected || m.unlit;
        if (m.asleep) {
            ctx.font = '700 22px "Fira Code", monospace';
            ctx.textAlign = 'center';
            ctx.strokeStyle = '#000000';
            ctx.lineWidth = 4;
            ctx.strokeText('💤 Zzz...', 192, curY + 20);
            ctx.fillStyle = '#a8d8ff';
            ctx.fillText('💤 Zzz...', 192, curY + 20);
            curY += 24;
        } else if (isTargeted) {
            ctx.font = '900 20px "Cinzel", serif';
            ctx.textAlign = 'center';
            ctx.strokeStyle = '#000000';
            ctx.lineWidth = 4;
            ctx.strokeText('⌖ TARGET ⌖', 192, curY + 18);
            ctx.fillStyle = '#ffd700';
            ctx.fillText('⌖ TARGET ⌖', 192, curY + 18);
            curY += 22;
        } else if (isSensed) {
            ctx.font = '700 18px "Cinzel", serif';
            ctx.textAlign = 'center';
            ctx.strokeStyle = '#000000';
            ctx.lineWidth = 4;
            const badgeText = m.invisible ? '👁 SENSED [INVIS]' : '👁 SENSED';
            ctx.strokeText(badgeText, 192, curY + 18);
            ctx.fillStyle = '#55ffff';
            ctx.fillText(badgeText, 192, curY + 18);
            curY += 22;
        }

        // Line 2: Monster Race Name with crisp black outline
        ctx.font = '700 24px "Cinzel", serif';
        ctx.textAlign = 'center';
        ctx.strokeStyle = '#000000';
        ctx.lineWidth = 4;
        ctx.strokeText(race, 192, curY + 22);
        ctx.fillStyle = colorHex || '#ffffff';
        ctx.fillText(race, 192, curY + 22);
        curY += 28;

        // Line 3: Compact Health Bar matching Godot: [glyph] [██████] hp/hpMax
        const barW = 160;
        const barH = 10;
        const barX = 192 - barW / 2;
        const barY = curY + 4;

        // Background
        ctx.fillStyle = 'rgba(0, 0, 0, 0.85)';
        ctx.fillRect(barX - 2, barY - 2, barW + 4, barH + 4);

        let hpCol = '#44ee44';
        if (pct < 0.3) hpCol = '#ff3b30';
        else if (pct < 0.6) hpCol = '#ffcc00';

        ctx.fillStyle = hpCol;
        ctx.fillRect(barX, barY, Math.max(2, barW * pct), barH);

        // Text labels: [glyph] on left, hp/max on right
        ctx.font = '700 16px "Fira Code", monospace';
        ctx.fillStyle = '#e0e0e0';
        ctx.strokeStyle = '#000000';
        ctx.lineWidth = 3;

        ctx.textAlign = 'right';
        ctx.strokeText(`[${glyph}]`, barX - 6, barY + 9);
        ctx.fillText(`[${glyph}]`, barX - 6, barY + 9);

        ctx.textAlign = 'left';
        ctx.strokeText(`${hp}/${hpMax}`, barX + barW + 6, barY + 9);
        ctx.fillText(`${hp}/${hpMax}`, barX + barW + 6, barY + 9);

        const tex = new THREE.CanvasTexture(canvas);
        tex.minFilter = THREE.LinearFilter;
        // Godot MonsterModelResolver.cs:388 sets NoDepthTest = false (occluded by walls)
        const mat = new THREE.SpriteMaterial({ map: tex, depthTest: true, depthWrite: false, transparent: true });
        const sprite = new THREE.Sprite(mat);
        sprite.scale.set(1.15, 0.33, 1.0);
        return sprite;
    }

    createProceduralCreatureMesh(glyph, raceName, colorHex) {
        const group = new THREE.Group();
        const lower = (raceName || '').toLowerCase();
        const mainColor = new THREE.Color(colorHex);
        const darkColor = mainColor.clone().multiplyScalar(0.4);

        if (glyph === 't' || glyph === 'h' || glyph === 'p' || glyph === 'o' || glyph === 'k') {
            // Humanoid / Orc / Townsperson / Guard
            const isGuard = lower.includes('guard') || lower.includes('knight') || lower.includes('soldier') || lower.includes('mercenary') || lower.includes('uruk');
            const skinMat = new THREE.MeshStandardMaterial({
                color: glyph === 'o' || glyph === 'k' ? 0x5a7a40 : 0xd8ad88,
                roughness: 0.75,
                normalMap: this.organicNormal,
                normalScale: new THREE.Vector2(0.65, 0.65)
            });
            const tunicMat = new THREE.MeshStandardMaterial({
                color: mainColor,
                roughness: 0.80,
                metalness: isGuard ? 0.70 : 0.05,
                normalMap: this.organicNormal,
                normalScale: new THREE.Vector2(0.85, 0.85)
            });
            const leatherMat = new THREE.MeshStandardMaterial({
                color: 0x3d2817,
                roughness: 0.85,
                normalMap: this.organicNormal
            });

            // Tapered muscular torso
            const torso = new THREE.Mesh(new THREE.CylinderGeometry(0.19, 0.14, 0.48, 10), tunicMat);
            torso.position.y = 0.58;
            group.add(torso);

            // Head
            const head = new THREE.Mesh(new THREE.SphereGeometry(0.14, 12, 10), skinMat);
            head.position.y = 0.92;
            group.add(head);

            // Helmet for guards / warriors
            if (isGuard) {
                const helm = new THREE.Mesh(new THREE.CylinderGeometry(0.15, 0.16, 0.18, 10), tunicMat);
                helm.position.y = 0.94;
                group.add(helm);
            } else {
                // Soft cap / hair for townspeople
                const hairMat = new THREE.MeshStandardMaterial({ color: 0x4a3728, roughness: 0.9 });
                const cap = new THREE.Mesh(new THREE.SphereGeometry(0.145, 10, 8, 0, Math.PI * 2, 0, Math.PI * 0.5), hairMat);
                cap.position.y = 0.94;
                group.add(cap);
            }

            // Legs
            const legL = new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.06, 0.40, 8), leatherMat);
            legL.position.set(-0.10, 0.20, 0);
            const legR = legL.clone();
            legR.position.x = 0.10;
            group.add(legL);
            group.add(legR);

            // Arms
            const armL = new THREE.Mesh(new THREE.CylinderGeometry(0.05, 0.05, 0.38, 8), tunicMat);
            armL.position.set(-0.24, 0.55, 0.02);
            const armR = armL.clone();
            armR.position.x = 0.24;
            group.add(armL);
            group.add(armR);

            // Role-Accurate Equipment Resolution (1:1 with Godot client MonsterModelResolver.cs)
            const isArcher = lower.includes('archer') || lower.includes('scout') || lower.includes('ranger') || lower.includes('sniper');
            const isMage = lower.includes('mage') || lower.includes('wizard') || lower.includes('sorcerer') || lower.includes('warlock') || lower.includes('priest') || lower.includes('shaman') || lower.includes('acolyte');
            const isRogue = lower.includes('rogue') || lower.includes('thief') || lower.includes('assassin') || lower.includes('bandit') || lower.includes('cutpurse');

            if (isGuard) {
                // Warrior / Guard: Sword + Shield
                armL.rotation.x = Math.PI / 6;
                armR.rotation.x = Math.PI / 6;

                const shield = new THREE.Mesh(new THREE.CylinderGeometry(0.14, 0.14, 0.03, 12), tunicMat);
                shield.position.set(-0.26, 0.52, 0.14);
                shield.rotation.x = Math.PI / 2;
                group.add(shield);

                const swordBlade = new THREE.Mesh(new THREE.BoxGeometry(0.04, 0.40, 0.015), new THREE.MeshStandardMaterial({ color: 0xccd5dd, metalness: 0.9, roughness: 0.2 }));
                swordBlade.position.set(0.26, 0.55, 0.18);
                swordBlade.rotation.x = Math.PI / 4;
                group.add(swordBlade);
            } else if (isArcher) {
                // Archer: Bow in left hand, drawing right arm
                armL.rotation.x = Math.PI / 3;
                armR.rotation.x = Math.PI / 6;

                const bowMesh = new THREE.Mesh(new THREE.TorusGeometry(0.18, 0.015, 6, 12, Math.PI), new THREE.MeshStandardMaterial({ color: 0x5a3d24, roughness: 0.8 }));
                bowMesh.position.set(-0.26, 0.52, 0.18);
                bowMesh.rotation.y = Math.PI / 2;
                group.add(bowMesh);
            } else if (isMage) {
                // Mage: Glowing runic staff in right hand
                armR.rotation.x = Math.PI / 4;

                const staff = new THREE.Mesh(new THREE.CylinderGeometry(0.015, 0.02, 0.70, 8), new THREE.MeshStandardMaterial({ color: 0x3d2817, roughness: 0.85 }));
                staff.position.set(0.26, 0.60, 0.15);
                const orb = new THREE.Mesh(new THREE.SphereGeometry(0.05, 8, 8), new THREE.MeshBasicMaterial({ color: mainColor }));
                orb.position.set(0.26, 0.96, 0.15);
                group.add(staff);
                group.add(orb);
            } else if (isRogue) {
                // Rogue: Serrated dagger in right hand
                armR.rotation.x = Math.PI / 3;

                const dagger = new THREE.Mesh(new THREE.BoxGeometry(0.025, 0.22, 0.01), new THREE.MeshStandardMaterial({ color: 0xa8b4c0, metalness: 0.85, roughness: 0.25 }));
                dagger.position.set(0.24, 0.50, 0.15);
                dagger.rotation.x = Math.PI / 3;
                group.add(dagger);
            }

        } else if (glyph === 'C' || glyph === 'Z') {
            // Canine: Dog / Wolf / War Dog / Hound (Quadruped with snout, ears, tail)
            const furMat = new THREE.MeshStandardMaterial({ color: mainColor, roughness: 0.85 });
            const darkFur = new THREE.MeshStandardMaterial({ color: darkColor, roughness: 0.90 });

            // Torso (horizontal body)
            const body = new THREE.Mesh(new THREE.BoxGeometry(0.24, 0.26, 0.52), furMat);
            body.position.y = 0.32;
            group.add(body);

            // Neck & Head
            const neck = new THREE.Mesh(new THREE.BoxGeometry(0.16, 0.20, 0.18), furMat);
            neck.position.set(0, 0.42, 0.24);
            neck.rotation.x = -Math.PI / 6;
            group.add(neck);

            const head = new THREE.Mesh(new THREE.BoxGeometry(0.18, 0.18, 0.20), furMat);
            head.position.set(0, 0.48, 0.34);
            group.add(head);

            // Snout with black nose
            const snout = new THREE.Mesh(new THREE.BoxGeometry(0.10, 0.10, 0.16), darkFur);
            snout.position.set(0, 0.44, 0.46);
            group.add(snout);

            // Ears (triangular pointed)
            const earGeo = new THREE.ConeGeometry(0.04, 0.10, 4);
            const earL = new THREE.Mesh(earGeo, darkFur);
            earL.position.set(-0.07, 0.58, 0.32);
            const earR = earL.clone();
            earR.position.x = 0.07;
            group.add(earL);
            group.add(earR);

            // 4 Articulated Legs
            const legGeo = new THREE.CylinderGeometry(0.04, 0.035, 0.28, 6);
            const fl = new THREE.Mesh(legGeo, furMat); fl.position.set(-0.10, 0.14, 0.18);
            const fr = fl.clone(); fr.position.x = 0.10;
            const bl = fl.clone(); bl.position.set(-0.10, 0.14, -0.18);
            const br = fl.clone(); br.position.set(0.10, 0.14, -0.18);
            group.add(fl); group.add(fr); group.add(bl); group.add(br);

            // Tail (curved upward)
            const tail = new THREE.Mesh(new THREE.CylinderGeometry(0.02, 0.03, 0.22, 6), furMat);
            tail.position.set(0, 0.38, -0.32);
            tail.rotation.x = -Math.PI / 4;
            group.add(tail);

        } else if (glyph === 'f') {
            // Feline: Cat / Panther / Tiger / Lion (Slender agile quadruped)
            const catMat = new THREE.MeshStandardMaterial({ color: mainColor, roughness: 0.80 });
            const body = new THREE.Mesh(new THREE.BoxGeometry(0.18, 0.20, 0.42), catMat);
            body.position.y = 0.26;
            group.add(body);

            const head = new THREE.Mesh(new THREE.SphereGeometry(0.11, 10, 8), catMat);
            head.position.set(0, 0.35, 0.24);
            group.add(head);

            // Pointed cat ears
            const earGeo = new THREE.ConeGeometry(0.035, 0.08, 4);
            const earL = new THREE.Mesh(earGeo, catMat);
            earL.position.set(-0.06, 0.44, 0.22);
            const earR = earL.clone();
            earR.position.x = 0.06;
            group.add(earL); group.add(earR);

            // 4 Slender legs
            const legGeo = new THREE.CylinderGeometry(0.03, 0.025, 0.22, 6);
            const fl = new THREE.Mesh(legGeo, catMat); fl.position.set(-0.08, 0.11, 0.14);
            const fr = fl.clone(); fr.position.x = 0.08;
            const bl = fl.clone(); bl.position.set(-0.08, 0.11, -0.14);
            const br = fl.clone(); br.position.set(0.08, 0.11, -0.14);
            group.add(fl); group.add(fr); group.add(bl); group.add(br);

            // Long upright curved tail
            const tail = new THREE.Mesh(new THREE.CylinderGeometry(0.015, 0.02, 0.26, 6), catMat);
            tail.position.set(0, 0.36, -0.28);
            tail.rotation.x = -Math.PI / 3;
            group.add(tail);

        } else if (glyph === 'r') {
            // Rodent: Rat / Mouse / Cave Rat (Low rounded body, snout, ears, tail)
            const ratMat = new THREE.MeshStandardMaterial({ color: mainColor, roughness: 0.90 });
            const pinkMat = new THREE.MeshStandardMaterial({ color: 0xe8a0a0, roughness: 0.70 });

            const body = new THREE.Mesh(new THREE.SphereGeometry(0.16, 12, 8), ratMat);
            body.scale.set(0.8, 0.7, 1.4);
            body.position.y = 0.16;
            group.add(body);

            const snout = new THREE.Mesh(new THREE.ConeGeometry(0.07, 0.16, 8), ratMat);
            snout.position.set(0, 0.14, 0.25);
            snout.rotation.x = Math.PI / 2;
            group.add(snout);

            const earL = new THREE.Mesh(new THREE.SphereGeometry(0.045, 6, 6), pinkMat);
            earL.position.set(-0.08, 0.24, 0.12);
            const earR = earL.clone(); earR.position.x = 0.08;
            group.add(earL); group.add(earR);

            const tail = new THREE.Mesh(new THREE.CylinderGeometry(0.012, 0.02, 0.32, 6), pinkMat);
            tail.position.set(0, 0.10, -0.30);
            tail.rotation.x = -Math.PI / 12;
            group.add(tail);

        } else if (glyph === 's') {
            // Skeleton: Bony ribcage, hollow-socket skull, articulated limbs, rusty ancient blade
            const boneMat = new THREE.MeshStandardMaterial({ color: 0xddd7c5, roughness: 0.85, metalness: 0.05 });
            const darkBone = new THREE.MeshStandardMaterial({ color: 0x4a453b, roughness: 0.90 });

            // Spine & Ribcage
            const spine = new THREE.Mesh(new THREE.CylinderGeometry(0.02, 0.025, 0.45, 6), boneMat);
            spine.position.y = 0.55;
            group.add(spine);

            for (let i = 0; i < 3; i++) {
                const rib = new THREE.Mesh(new THREE.TorusGeometry(0.12 - i * 0.015, 0.012, 4, 8, Math.PI), boneMat);
                rib.position.set(0, 0.62 - i * 0.08, 0);
                rib.rotation.x = Math.PI / 2;
                group.add(rib);
            }

            // Pelvis
            const pelvis = new THREE.Mesh(new THREE.BoxGeometry(0.22, 0.06, 0.12), boneMat);
            pelvis.position.y = 0.35;
            group.add(pelvis);

            // Skull
            const skull = new THREE.Mesh(new THREE.SphereGeometry(0.11, 10, 8), boneMat);
            skull.position.y = 0.88;
            group.add(skull);

            // Hollow eye sockets
            const eyeL = new THREE.Mesh(new THREE.SphereGeometry(0.022, 6, 6), darkBone);
            eyeL.position.set(-0.04, 0.89, 0.09);
            const eyeR = eyeL.clone(); eyeR.position.x = 0.04;
            group.add(eyeL); group.add(eyeR);

            // Bony legs
            const legL = new THREE.Mesh(new THREE.CylinderGeometry(0.02, 0.015, 0.35, 6), boneMat);
            legL.position.set(-0.08, 0.18, 0);
            const legR = legL.clone(); legR.position.x = 0.08;
            group.add(legL); group.add(legR);

            // Bony arms
            const armL = new THREE.Mesh(new THREE.CylinderGeometry(0.018, 0.015, 0.35, 6), boneMat);
            armL.position.set(-0.18, 0.52, 0.05);
            armL.rotation.x = Math.PI / 6;
            const armR = armL.clone();
            armR.position.x = 0.18;
            armR.rotation.x = Math.PI / 4;
            group.add(armL); group.add(armR);

            // Rusty ancient blade in right hand
            const blade = new THREE.Mesh(new THREE.BoxGeometry(0.03, 0.38, 0.01), new THREE.MeshStandardMaterial({ color: 0x6e584a, roughness: 0.75, metalness: 0.6 }));
            blade.position.set(0.20, 0.60, 0.18);
            blade.rotation.x = Math.PI / 3;
            group.add(blade);

        } else if (glyph === 'J' || glyph === 'n') {
            // Snake / Serpent / Naga: Multi-segmented coiled body + raised cobra head
            const scaleMat = new THREE.MeshStandardMaterial({ color: mainColor, roughness: 0.50, metalness: 0.15 });
            const numSegs = 6;
            for (let i = 0; i < numSegs; i++) {
                const segRad = 0.09 - i * 0.01;
                const seg = new THREE.Mesh(new THREE.SphereGeometry(segRad, 8, 8), scaleMat);
                const t = i / numSegs;
                seg.position.set(Math.sin(t * Math.PI * 2) * 0.18, 0.08 + (i < 2 ? (2 - i) * 0.12 : 0), (i - 2) * 0.14);
                group.add(seg);
            }
            const head = new THREE.Mesh(new THREE.ConeGeometry(0.10, 0.18, 8), scaleMat);
            head.position.set(0, 0.38, -0.16);
            head.rotation.x = -Math.PI / 4;
            group.add(head);

        } else if (glyph === 'S') {
            // Spider / Scorpion: Cephalothorax, abdomen, 8 jointed legs, glowing eyes
            const chitinMat = new THREE.MeshStandardMaterial({ color: mainColor, roughness: 0.40, metalness: 0.35 });
            const eyeMat = new THREE.MeshBasicMaterial({ color: 0xff1122 });

            const thorax = new THREE.Mesh(new THREE.SphereGeometry(0.14, 10, 8), chitinMat);
            thorax.position.y = 0.22;
            group.add(thorax);

            const abdomen = new THREE.Mesh(new THREE.SphereGeometry(0.22, 12, 10), chitinMat);
            abdomen.position.set(0, 0.26, -0.28);
            group.add(abdomen);

            // 8 Jointed Legs
            for (let i = 0; i < 4; i++) {
                const zOff = (i - 1.5) * 0.10;
                const legGeo = new THREE.CylinderGeometry(0.02, 0.015, 0.35, 6);

                const legL = new THREE.Mesh(legGeo, chitinMat);
                legL.position.set(-0.28, 0.20, zOff);
                legL.rotation.z = Math.PI / 3;
                const legR = legL.clone();
                legR.position.x = 0.28;
                legR.rotation.z = -Math.PI / 3;

                group.add(legL); group.add(legR);
            }

            // Cluster of red eyes
            for (let i = -1; i <= 1; i += 2) {
                const eye = new THREE.Mesh(new THREE.SphereGeometry(0.025, 6, 6), eyeMat);
                eye.position.set(i * 0.05, 0.26, 0.13);
                group.add(eye);
            }

        } else if (glyph === 'a' || glyph === 'K' || glyph === 'F' || glyph === 'I') {
            // Insect: Ant / Killer Beetle / Fly (Segmented body + 6 legs + antennae)
            const bugMat = new THREE.MeshStandardMaterial({ color: mainColor, roughness: 0.45, metalness: 0.3 });
            const thorax = new THREE.Mesh(new THREE.SphereGeometry(0.12, 8, 8), bugMat);
            thorax.position.y = 0.20;
            group.add(thorax);

            const abdomen = new THREE.Mesh(new THREE.SphereGeometry(0.16, 8, 8), bugMat);
            abdomen.position.set(0, 0.22, -0.20);
            group.add(abdomen);

            const head = new THREE.Mesh(new THREE.SphereGeometry(0.09, 8, 8), bugMat);
            head.position.set(0, 0.22, 0.15);
            group.add(head);

            for (let i = 0; i < 3; i++) {
                const legL = new THREE.Mesh(new THREE.CylinderGeometry(0.015, 0.012, 0.24, 6), bugMat);
                legL.position.set(-0.18, 0.14, (i - 1) * 0.10);
                legL.rotation.z = Math.PI / 3;
                const legR = legL.clone();
                legR.position.x = 0.18;
                legR.rotation.z = -Math.PI / 3;
                group.add(legL); group.add(legR);
            }

        } else if (glyph === 'd' || glyph === 'D' || glyph === 'M') {
            // Dragon / Wyrm / Hydra: Scaled quadruped, horned head, leathery wings, tail
            const dragonMat = new THREE.MeshStandardMaterial({ color: mainColor, roughness: 0.50, metalness: 0.35 });
            const bellyMat = new THREE.MeshStandardMaterial({ color: 0xecdca8, roughness: 0.60 });

            const body = new THREE.Mesh(new THREE.BoxGeometry(0.65, 0.45, 0.95), dragonMat);
            body.position.y = 0.55;
            group.add(body);

            const belly = new THREE.Mesh(new THREE.BoxGeometry(0.50, 0.08, 0.85), bellyMat);
            belly.position.y = 0.32;
            group.add(belly);

            const neck = new THREE.Mesh(new THREE.CylinderGeometry(0.16, 0.22, 0.55, 8), dragonMat);
            neck.position.set(0, 0.80, 0.40);
            neck.rotation.x = -Math.PI / 4;
            group.add(neck);

            const head = new THREE.Mesh(new THREE.ConeGeometry(0.22, 0.48, 8), dragonMat);
            head.position.set(0, 0.95, 0.55);
            head.rotation.x = Math.PI / 2;
            group.add(head);

            // Horns
            const hornGeo = new THREE.ConeGeometry(0.04, 0.22, 6);
            const hornL = new THREE.Mesh(hornGeo, dragonMat);
            hornL.position.set(-0.12, 1.10, 0.45);
            hornL.rotation.x = -Math.PI / 3;
            const hornR = hornL.clone(); hornR.position.x = 0.12;
            group.add(hornL); group.add(hornR);

            // Large spreading wings
            const wingMat = new THREE.MeshStandardMaterial({ color: darkColor, roughness: 0.65, side: THREE.DoubleSide });
            const wingL = new THREE.Mesh(new THREE.PlaneGeometry(0.95, 0.70), wingMat);
            wingL.position.set(-0.60, 0.85, 0);
            wingL.rotation.y = Math.PI / 5;
            const wingR = wingL.clone();
            wingR.position.x = 0.60;
            wingR.rotation.y = -Math.PI / 5;
            group.add(wingL); group.add(wingR);

            // Spiked tail
            const tail = new THREE.Mesh(new THREE.ConeGeometry(0.12, 0.75, 8), dragonMat);
            tail.position.set(0, 0.42, -0.75);
            tail.rotation.x = -Math.PI / 3;
            group.add(tail);

        } else if (glyph === 'j' || glyph === 'i') {
            // Slime / Ooze / Jelly: Translucent pulsing dome + glowing inner core
            const slimeMat = new THREE.MeshStandardMaterial({
                color: mainColor,
                roughness: 0.15,
                metalness: 0.1,
                transparent: true,
                opacity: 0.78
            });
            const dome = new THREE.Mesh(new THREE.SphereGeometry(0.40, 16, 12, 0, Math.PI * 2, 0, Math.PI * 0.5), slimeMat);
            dome.position.y = 0.0;
            group.add(dome);

            const coreMat = new THREE.MeshBasicMaterial({ color: 0xffffff });
            const core = new THREE.Mesh(new THREE.SphereGeometry(0.15, 10, 10), coreMat);
            core.position.y = 0.18;
            group.add(core);

        } else if (glyph === 'e') {
            // Beholder / Floating Eye: Sphere with central dilated pupil and radiating eyestalks
            const orbMat = new THREE.MeshStandardMaterial({ color: mainColor, roughness: 0.40, metalness: 0.15 });
            const eyeOrb = new THREE.Mesh(new THREE.SphereGeometry(0.35, 16, 14), orbMat);
            eyeOrb.position.y = 0.65;
            group.add(eyeOrb);

            const pupilMat = new THREE.MeshBasicMaterial({ color: 0x111111 });
            const pupil = new THREE.Mesh(new THREE.CylinderGeometry(0.12, 0.12, 0.04, 12), pupilMat);
            pupil.position.set(0, 0.65, 0.34);
            pupil.rotation.x = Math.PI / 2;
            group.add(pupil);

            for (let i = 0; i < 5; i++) {
                const angle = (i / 5) * Math.PI - Math.PI / 2;
                const stalk = new THREE.Mesh(new THREE.CylinderGeometry(0.02, 0.03, 0.28, 6), orbMat);
                stalk.position.set(Math.sin(angle) * 0.25, 0.95, Math.cos(angle) * 0.15);
                stalk.rotation.z = -angle * 0.5;
                group.add(stalk);
            }

        } else if (glyph === 'z') {
            // Zombie: Shambling rotting biped, tattered burial rags, outstretched arms
            const fleshMat = new THREE.MeshStandardMaterial({ color: 0x4a5d44, roughness: 0.88 });
            const ragMat = new THREE.MeshStandardMaterial({ color: 0x362c22, roughness: 0.95 });

            const torso = new THREE.Mesh(new THREE.BoxGeometry(0.38, 0.48, 0.22), ragMat);
            torso.position.y = 0.52;
            torso.rotation.x = 0.12; // Hunched
            group.add(torso);

            const head = new THREE.Mesh(new THREE.SphereGeometry(0.13, 10, 8), fleshMat);
            head.position.set(0, 0.82, 0.05);
            group.add(head);

            // Shambling outstretched rotting arms
            const armL = new THREE.Mesh(new THREE.CylinderGeometry(0.045, 0.040, 0.38, 6), fleshMat);
            armL.position.set(-0.24, 0.60, 0.16);
            armL.rotation.x = Math.PI / 2;
            const armR = armL.clone();
            armR.position.x = 0.24;
            group.add(armL); group.add(armR);

            const legL = new THREE.Mesh(new THREE.CylinderGeometry(0.055, 0.050, 0.36, 6), ragMat);
            legL.position.set(-0.10, 0.18, 0);
            const legR = legL.clone();
            legR.position.x = 0.10;
            group.add(legL); group.add(legR);

        } else if (glyph === 'W' || glyph === 'G' || glyph === 'L' || glyph === 'V') {
            // Undead / Ghost / Wraith / Lich: Translucent glowing phantom with cyan eyes
            const ghostMat = new THREE.MeshStandardMaterial({
                color: mainColor,
                emissive: mainColor,
                emissiveIntensity: 0.45,
                transparent: true,
                opacity: 0.82,
                roughness: 0.35
            });
            const cowl = new THREE.Mesh(new THREE.ConeGeometry(0.32, 0.95, 12), ghostMat);
            cowl.position.y = 0.65;
            group.add(cowl);

            const skull = new THREE.Mesh(new THREE.SphereGeometry(0.16, 12, 10), ghostMat);
            skull.position.y = 0.95;
            group.add(skull);

            const eyeMat = new THREE.MeshBasicMaterial({ color: 0x00ffff });
            const eyeL = new THREE.Mesh(new THREE.SphereGeometry(0.025, 6, 6), eyeMat);
            eyeL.position.set(-0.05, 0.96, 0.15);
            const eyeR = eyeL.clone(); eyeR.position.x = 0.05;
            group.add(eyeL); group.add(eyeR);

        } else if (glyph === 'b' || glyph === 'B') {
            // Bat / Bird: Small body + dual flapping wings
            const batMat = new THREE.MeshStandardMaterial({ color: 0x332822, roughness: 0.9 });
            const body = new THREE.Mesh(new THREE.SphereGeometry(0.12, 10, 8), batMat);
            body.position.y = 0.65;
            group.add(body);

            const wingMat = new THREE.MeshStandardMaterial({ color: 0x221a15, roughness: 0.8, side: THREE.DoubleSide });
            const wingL = new THREE.Mesh(new THREE.PlaneGeometry(0.40, 0.22), wingMat);
            wingL.position.set(-0.25, 0.68, 0);
            wingL.rotation.z = Math.PI / 8;
            const wingR = wingL.clone();
            wingR.position.x = 0.25;
            wingR.rotation.z = -Math.PI / 8;
            group.add(wingL); group.add(wingR);

        } else if (glyph === 'm' || glyph === ',') {
            // Mushroom / Fungi: Stalk + spotted cap
            const stalkMat = new THREE.MeshStandardMaterial({ color: 0xded4b8, roughness: 0.85 });
            const capMat = new THREE.MeshStandardMaterial({ color: mainColor, roughness: 0.45, emissive: mainColor, emissiveIntensity: 0.25 });

            const stalk = new THREE.Mesh(new THREE.CylinderGeometry(0.08, 0.12, 0.35, 8), stalkMat);
            stalk.position.y = 0.18;
            group.add(stalk);

            const cap = new THREE.Mesh(new THREE.ConeGeometry(0.32, 0.20, 12), capMat);
            cap.position.y = 0.42;
            group.add(cap);

        } else if (glyph === 'c') {
            // Centipede: Multi-segmented creeping body + tiny legs
            const centiMat = new THREE.MeshStandardMaterial({ color: mainColor, roughness: 0.50, metalness: 0.25 });
            for (let i = 0; i < 7; i++) {
                const seg = new THREE.Mesh(new THREE.SphereGeometry(0.07, 6, 6), centiMat);
                seg.position.set(0, 0.08, (i - 3) * 0.12);
                group.add(seg);
                const legL = new THREE.Mesh(new THREE.CylinderGeometry(0.01, 0.01, 0.12, 4), centiMat);
                legL.position.set(-0.10, 0.06, (i - 3) * 0.12);
                legL.rotation.z = Math.PI / 3;
                const legR = legL.clone();
                legR.position.x = 0.10;
                legR.rotation.z = -Math.PI / 3;
                group.add(legL); group.add(legR);
            }

        } else if (glyph === 'w') {
            // Worm / Worm Mass: Annulated undulating tube
            const wormMat = new THREE.MeshStandardMaterial({ color: mainColor, roughness: 0.70 });
            for (let i = 0; i < 6; i++) {
                const seg = new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.06, 0.12, 8), wormMat);
                seg.position.set(Math.sin(i * 0.8) * 0.08, 0.08, (i - 2.5) * 0.14);
                seg.rotation.x = Math.PI / 2;
                group.add(seg);
            }

        } else if (glyph === 'P' || glyph === 'T' || glyph === 'O') {
            // Giant / Troll / Ogre: Massive hulking brute
            const skinMat = new THREE.MeshStandardMaterial({ color: mainColor, roughness: 0.85 });
            const loinMat = new THREE.MeshStandardMaterial({ color: 0x3d2716, roughness: 0.90 });
            const torso = new THREE.Mesh(new THREE.BoxGeometry(0.65, 0.75, 0.45), skinMat);
            torso.position.y = 0.85;
            group.add(torso);
            const loin = new THREE.Mesh(new THREE.BoxGeometry(0.68, 0.28, 0.48), loinMat);
            loin.position.y = 0.52;
            group.add(loin);
            const head = new THREE.Mesh(new THREE.BoxGeometry(0.35, 0.35, 0.35), skinMat);
            head.position.y = 1.35;
            group.add(head);
            const armL = new THREE.Mesh(new THREE.CylinderGeometry(0.11, 0.10, 0.65, 8), skinMat);
            armL.position.set(-0.48, 0.80, 0);
            const armR = armL.clone();
            armR.position.x = 0.48;
            group.add(armL); group.add(armR);
            const legL = new THREE.Mesh(new THREE.CylinderGeometry(0.14, 0.12, 0.50, 8), skinMat);
            legL.position.set(-0.20, 0.25, 0);
            const legR = legL.clone();
            legR.position.x = 0.20;
            group.add(legL); group.add(legR);
            // Heavy club in right hand
            const club = new THREE.Mesh(new THREE.CylinderGeometry(0.08, 0.04, 0.75, 8), new THREE.MeshStandardMaterial({ color: 0x4a301a, roughness: 0.95 }));
            club.position.set(0.52, 0.75, 0.25);
            club.rotation.x = Math.PI / 4;
            group.add(club);

        } else if (glyph === 'u' || glyph === 'U') {
            // Demon / Major Demon: Horned devil with wings & tail
            const demonMat = new THREE.MeshStandardMaterial({ color: mainColor, roughness: 0.55, metalness: 0.35 });
            const torso = new THREE.Mesh(new THREE.BoxGeometry(0.48, 0.65, 0.32), demonMat);
            torso.position.y = 0.72;
            group.add(torso);
            const head = new THREE.Mesh(new THREE.BoxGeometry(0.26, 0.28, 0.26), demonMat);
            head.position.y = 1.15;
            group.add(head);
            const hornL = new THREE.Mesh(new THREE.ConeGeometry(0.04, 0.22, 6), demonMat);
            hornL.position.set(-0.10, 1.35, 0.05);
            hornL.rotation.z = -0.35;
            const hornR = hornL.clone();
            hornR.position.x = 0.10;
            hornR.rotation.z = 0.35;
            group.add(hornL); group.add(hornR);
            const wingMat = new THREE.MeshStandardMaterial({ color: darkColor, side: THREE.DoubleSide });
            const wingL = new THREE.Mesh(new THREE.PlaneGeometry(0.55, 0.50), wingMat);
            wingL.position.set(-0.45, 0.95, -0.15);
            wingL.rotation.y = Math.PI / 4;
            const wingR = wingL.clone();
            wingR.position.x = 0.45;
            wingR.rotation.y = -Math.PI / 4;
            group.add(wingL); group.add(wingR);

        } else if (glyph === 'l') {
            // Ent / Animated Tree: Trunk, branches, foliage
            const barkMat = new THREE.MeshStandardMaterial({ color: 0x3e2718, roughness: 0.95 });
            const leafMat = new THREE.MeshStandardMaterial({ color: mainColor, roughness: 0.75 });
            const trunk = new THREE.Mesh(new THREE.CylinderGeometry(0.18, 0.28, 1.10, 8), barkMat);
            trunk.position.y = 0.55;
            group.add(trunk);
            const foliage = new THREE.Mesh(new THREE.SphereGeometry(0.55, 10, 8), leafMat);
            foliage.position.y = 1.35;
            group.add(foliage);

        } else if (glyph === '?') {
            // Mimic: Treasure chest with lurking teeth
            const woodMat = new THREE.MeshStandardMaterial({ color: 0x4a2e18, roughness: 0.8 });
            const goldMat = new THREE.MeshStandardMaterial({ color: 0xd4af37, metalness: 0.9, roughness: 0.2 });
            const chest = new THREE.Mesh(new THREE.BoxGeometry(0.50, 0.35, 0.38), woodMat);
            chest.position.y = 0.18;
            group.add(chest);
            const lock = new THREE.Mesh(new THREE.BoxGeometry(0.08, 0.08, 0.04), goldMat);
            lock.position.set(0, 0.18, 0.20);
            group.add(lock);

        } else if (glyph === '$') {
            // Creeping Coins: Gilded shimmering mound of coins
            const coinMat = new THREE.MeshStandardMaterial({ color: 0xffd700, emissive: 0x664400, metalness: 0.95, roughness: 0.15 });
            const pile = new THREE.Mesh(new THREE.CylinderGeometry(0.12, 0.35, 0.22, 12), coinMat);
            pile.position.y = 0.11;
            group.add(pile);

        } else if (glyph === 'R') {
            // Reptile / Amphibian / Frog: Low squat body, bulging eyes, splayed legs
            const repMat = new THREE.MeshStandardMaterial({ color: mainColor, roughness: 0.50 });
            const body = new THREE.Mesh(new THREE.SphereGeometry(0.22, 10, 8), repMat);
            body.scale.set(1.0, 0.65, 1.2);
            body.position.y = 0.16;
            group.add(body);
            const eyeL = new THREE.Mesh(new THREE.SphereGeometry(0.05, 6, 6), new THREE.MeshBasicMaterial({ color: 0xffff00 }));
            eyeL.position.set(-0.10, 0.26, 0.16);
            const eyeR = eyeL.clone();
            eyeR.position.x = 0.10;
            group.add(eyeL); group.add(eyeR);

        } else if (glyph === 'q') {
            // Beast / Quadruped: Sturdy muscular body
            const beastMat = new THREE.MeshStandardMaterial({ color: mainColor, roughness: 0.85 });
            const body = new THREE.Mesh(new THREE.BoxGeometry(0.35, 0.35, 0.65), beastMat);
            body.position.y = 0.35;
            group.add(body);
            const head = new THREE.Mesh(new THREE.BoxGeometry(0.24, 0.24, 0.28), beastMat);
            head.position.set(0, 0.50, 0.38);
            group.add(head);
            const legGeo = new THREE.CylinderGeometry(0.05, 0.04, 0.28, 6);
            for (let i = 0; i < 4; i++) {
                const leg = new THREE.Mesh(legGeo, beastMat);
                leg.position.set(i % 2 === 0 ? -0.14 : 0.14, 0.14, i < 2 ? 0.22 : -0.22);
                group.add(leg);
            }

        } else if (glyph === 'y' || glyph === 'Y') {
            // Yeek / Yeti: Shaggy furry biped
            const furMat = new THREE.MeshStandardMaterial({ color: glyph === 'Y' ? 0xf0f4f8 : mainColor, roughness: 0.95 });
            const isYeti = (glyph === 'Y');
            const scale = isYeti ? 1.4 : 0.65;
            const body = new THREE.Mesh(new THREE.SphereGeometry(0.28 * scale, 10, 8), furMat);
            body.position.y = 0.40 * scale;
            group.add(body);
            const head = new THREE.Mesh(new THREE.SphereGeometry(0.20 * scale, 10, 8), furMat);
            head.position.y = 0.72 * scale;
            group.add(head);

        } else if (glyph === 'g') {
            // Golem: Heavy stone blocks with glowing chest rune
            const stoneMat = new THREE.MeshStandardMaterial({ color: mainColor, roughness: 0.90, metalness: 0.10 });
            const runeMat = new THREE.MeshBasicMaterial({ color: 0x00ffff });
            const torso = new THREE.Mesh(new THREE.BoxGeometry(0.55, 0.65, 0.38), stoneMat);
            torso.position.y = 0.68;
            group.add(torso);
            const rune = new THREE.Mesh(new THREE.PlaneGeometry(0.20, 0.20), runeMat);
            rune.position.set(0, 0.75, 0.20);
            group.add(rune);
            const head = new THREE.Mesh(new THREE.BoxGeometry(0.28, 0.25, 0.28), stoneMat);
            head.position.y = 1.10;
            group.add(head);

        } else if (glyph === 'E') {
            // Elemental: Swirling incandescent vortex with core
            const eleMat = new THREE.MeshStandardMaterial({
                color: mainColor,
                emissive: mainColor,
                emissiveIntensity: 0.75,
                roughness: 0.25,
                transparent: true,
                opacity: 0.85
            });
            const core = new THREE.Mesh(new THREE.DodecahedronGeometry(0.25, 0), eleMat);
            core.position.y = 0.65;
            group.add(core);
            const ring = new THREE.Mesh(new THREE.TorusGeometry(0.42, 0.05, 8, 20), eleMat);
            ring.position.y = 0.65;
            ring.rotation.x = Math.PI / 3;
            group.add(ring);

        } else {
            // Sculpted Organic Brute / Mannequin with Tactical Normal Maps (eliminates blocky untextured shapes)
            const bruteMat = new THREE.MeshStandardMaterial({
                color: mainColor,
                roughness: 0.70,
                metalness: 0.15,
                normalMap: this.organicNormal,
                normalScale: new THREE.Vector2(0.85, 0.85)
            });
            const jointMat = new THREE.MeshStandardMaterial({
                color: darkColor,
                roughness: 0.85,
                normalMap: this.chitinNormal
            });

            // Tapered muscular torso
            const chest = new THREE.Mesh(new THREE.CylinderGeometry(0.24, 0.17, 0.44, 10), bruteMat);
            chest.position.y = 0.68;
            group.add(chest);

            const waist = new THREE.Mesh(new THREE.CylinderGeometry(0.16, 0.18, 0.20, 8), jointMat);
            waist.position.y = 0.44;
            group.add(waist);

            // Sculpted rounded head with brow ridge
            const head = new THREE.Mesh(new THREE.SphereGeometry(0.15, 12, 10), bruteMat);
            head.position.y = 1.02;
            group.add(head);

            // Glowing focal eyes
            const eyeMat = new THREE.MeshBasicMaterial({ color: 0xff3322 });
            const eyeL = new THREE.Mesh(new THREE.SphereGeometry(0.025, 6, 6), eyeMat);
            eyeL.position.set(-0.05, 1.04, 0.13);
            const eyeR = eyeL.clone();
            eyeR.position.x = 0.05;
            group.add(eyeL); group.add(eyeR);

            // Articulated rounded arms
            const armGeo = new THREE.CylinderGeometry(0.05, 0.04, 0.42, 8);
            const armL = new THREE.Mesh(armGeo, bruteMat);
            armL.position.set(-0.28, 0.65, 0.04);
            armL.rotation.x = Math.PI / 8;
            armL.rotation.z = Math.PI / 16;
            const armR = armL.clone();
            armR.position.x = 0.28;
            armR.rotation.z = -Math.PI / 16;
            group.add(armL); group.add(armR);

            // Articulated legs with feet
            const legGeo = new THREE.CylinderGeometry(0.06, 0.05, 0.38, 8);
            const legL = new THREE.Mesh(legGeo, jointMat);
            legL.position.set(-0.11, 0.20, 0);
            const legR = legL.clone();
            legR.position.x = 0.11;
            group.add(legL); group.add(legR);
        }

        return group;
    }

    // -------------------------------------------------------------------------
    // High-Fidelity Monster Model & Equipment Resolvers (Matching local Godot client 1:1)
    // -------------------------------------------------------------------------
    cloneModelHierarchy(source) {
        if (!source) return null;
        const sourceLookup = new Map();
        const cloneLookup = new Map();
        const clone = source.clone(true);

        function parallelTraverse(a, b, callback) {
            callback(a, b);
            for (let i = 0; i < a.children.length; i++) {
                if (b.children[i]) {
                    parallelTraverse(a.children[i], b.children[i], callback);
                }
            }
        }

        parallelTraverse(source, clone, (sNode, cNode) => {
            sourceLookup.set(cNode, sNode);
            cloneLookup.set(sNode, cNode);
        });

        clone.traverse((node) => {
            if (node.isSkinnedMesh) {
                const sourceMesh = sourceLookup.get(node);
                if (sourceMesh && sourceMesh.skeleton) {
                    const sourceBones = sourceMesh.skeleton.bones;
                    node.skeleton = sourceMesh.skeleton.clone();
                    node.bindMatrix.copy(sourceMesh.bindMatrix);
                    node.skeleton.bones = sourceBones.map((bone) => cloneLookup.get(bone) || bone);
                    node.bind(node.skeleton, node.bindMatrix);
                }
            }
        });

        return clone;
    }

    findHandSlots(root) {
        let leftHand = null;
        let rightHand = null;
        root.traverse((node) => {
            const n = (node.name || '').toLowerCase();
            if (!leftHand && (n === 'handslot.l' || n === 'handslot_l' || n === 'hand.l' || n === 'hand_l' || n === 'wrist.l' || (n.includes('hand') && (n.includes('.l') || n.includes('_l') || n.includes('left'))))) {
                leftHand = node;
            }
            if (!rightHand && (n === 'handslot.r' || n === 'handslot_r' || n === 'hand.r' || n === 'hand_r' || n === 'wrist.r' || (n.includes('hand') && (n.includes('.r') || n.includes('_r') || n.includes('right'))))) {
                rightHand = node;
            }
        });
        return { leftHand, rightHand };
    }

    configureCreatureEquipment(mesh, role) {
        // 1. Hide any pre-existing weapon/shield child meshes embedded in model
        mesh.traverse((c) => {
            const n = (c.name || '').toLowerCase();
            if (n.includes('sword') || n.includes('shield') || n.includes('axe') || n.includes('dagger') || n.includes('bow') || n.includes('staff') || n.includes('hammer') || n.includes('spear')) {
                c.visible = false;
            }
        });

        if (role === 'unarmed') return;

        const { leftHand, rightHand } = this.findHandSlots(mesh);
        if (!rightHand && !leftHand) return;

        const attach = (handNode, weaponKey, scale = 0.05, rot = null, pos = null) => {
            if (!handNode || !this.weaponCache || !this.weaponCache.has(weaponKey)) return;
            const tmpl = this.weaponCache.get(weaponKey);
            const wClone = tmpl.clone(true);
            wClone.scale.set(scale, scale, scale);
            if (pos) wClone.position.set(pos.x, pos.y, pos.z);
            if (rot) wClone.rotation.set(rot.x, rot.y, rot.z);
            handNode.add(wClone);
        };

        if (role === 'warrior') {
            attach(rightHand, 'sword', 0.05, { x: 0, y: Math.PI / 2, z: -Math.PI / 2 }, { x: 0, y: 0, z: 0 });
            attach(leftHand, 'shield', 0.05, { x: 0, y: 0, z: 0 }, { x: 0, y: 0, z: 0 });
        } else if (role === 'barbarian') {
            attach(rightHand, 'axe', 0.05, { x: 0, y: Math.PI / 2, z: -Math.PI / 2 }, { x: 0, y: 0, z: 0 });
        } else if (role === 'rogue') {
            attach(rightHand, 'dagger', 0.05, { x: 0, y: Math.PI / 2, z: -Math.PI / 2 }, { x: 0, y: 0, z: 0 });
        } else if (role === 'archer') {
            attach(rightHand, 'bow', 0.05, { x: 0, y: Math.PI / 2, z: 0 }, { x: 0, y: 0, z: 0 });
        } else if (role === 'mage') {
            attach(rightHand, 'spear', 0.05, { x: 0, y: 0, z: -Math.PI / 2 }, { x: 0, y: 0, z: 0 });
        } else if (role === 'twohandedsword') {
            attach(rightHand, 'claymore', 0.055, { x: 0, y: Math.PI / 2, z: -Math.PI / 2 }, { x: 0, y: 0, z: 0 });
        }
    }

    resolveMonsterModelConfig(glyph, rawName) {
        const name = (rawName || '').toLowerCase();

        // 1. Townsfolk (t)
        if (glyph === 't') {
            let modelKey = 'farmer';
            if (name.includes('female') || name.includes('woman') || name.includes('lady') || name.includes('maiden') || name.includes('wench') || name.includes('maid') || name.includes('damsel')) {
                modelKey = 'casual';
            } else if (name.includes('merchant') || name.includes('shopkeeper') || name.includes('innkeeper') || name.includes('clerk') || name.includes('crier') || name.includes('scribe')) {
                modelKey = 'formal';
            } else if (name.includes('worker') || name.includes('smith') || name.includes('miner') || name.includes('artisan') || name.includes('craftsman') || name.includes('butcher') || name.includes('baker') || name.includes('cook')) {
                modelKey = 'worker';
            } else if (name.includes('mercenary') || name.includes('veteran') || name.includes('rogue') || name.includes('scoundrel') || name.includes('brawler')) {
                modelKey = 'adventurer';
            } else if (name.includes('beggar') || name.includes('leper') || name.includes('urchin') || name.includes('idiot') || name.includes('hermit') || name.includes('drunk') || name.includes('peasant') || name.includes('wretch') || name.includes('farmer')) {
                modelKey = 'farmer';
            }
            let scale = 0.88;
            if (name.includes('dwarf') || name.includes('hobbit') || name.includes('gnome') || name.includes('halfling')) {
                scale = 0.65;
            }
            return { templateKey: modelKey, scale, role: 'unarmed', isFloating: false, isEthereal: false };
        }

        // 2. Humanoids (h) and People / Adventurers (p)
        if (glyph === 'h' || glyph === 'p') {
            let scale = 0.95;
            if (name.includes('dwarf') || name.includes('hobbit') || name.includes('gnome') || name.includes('halfling') || name.includes('leprechaun')) {
                scale = 0.65;
            } else if (name.includes('elf') || name.includes('ranger') || name.includes('dunedain')) {
                scale = 0.98;
            }

            if (name.includes('knight') || name.includes('paladin') || name.includes('veteran') || name.includes('warrior') || name.includes('soldier') || name.includes('guard') || name.includes('captain') || name.includes('fighter') || name.includes('champion') || name.includes('swordsman') || name.includes('centurion') || name.includes('lord') || name.includes('templar')) {
                return { templateKey: 'medieval', scale, role: 'warrior', isFloating: false, isEthereal: false };
            }
            if (name.includes('archer') || name.includes('scout') || name.includes('sniper') || name.includes('tracker') || name.includes('marksman') || name.includes('bowman') || name.includes('hunter') || name.includes('ranger')) {
                return { templateKey: 'adventurer', scale, role: 'archer', isFloating: false, isEthereal: false };
            }
            if (name.includes('thief') || name.includes('rogue') || name.includes('burglar') || name.includes('assassin') || name.includes('cutpurse') || name.includes('bandit') || name.includes('brigand') || name.includes('ninja') || name.includes('scoundrel')) {
                return { templateKey: 'adventurer', scale, role: 'rogue', isFloating: false, isEthereal: false };
            }
            if (name.includes('peasant') || name.includes('drunkard') || name.includes('drunk') || name.includes('beggar') || name.includes('idiot') || name.includes('commoner') || name.includes('villager') || name.includes('leper') || name.includes('urchin') || name.includes('hermit') || name.includes('slave')) {
                return { templateKey: 'farmer', scale: 0.90, role: 'unarmed', isFloating: false, isEthereal: false };
            }
            if (name.includes('barbarian') || name.includes('mercenary') || name.includes('gladiator') || name.includes('berserker') || name.includes('bouncer') || name.includes('ruffian') || name.includes('brute')) {
                return { templateKey: 'punk', scale: 1.0, role: 'barbarian', isFloating: false, isEthereal: false };
            }
            if (name.includes('mage') || name.includes('wizard') || name.includes('warlock') || name.includes('sorcerer') || name.includes('alchemist') || name.includes('scholar') || name.includes('priest') || name.includes('cleric') || name.includes('sage') || name.includes('cultist') || name.includes('druid') || name.includes('seer') || name.includes('shaman') || name.includes('necromancer')) {
                return { templateKey: 'witch', scale: 0.92, role: 'mage', isFloating: false, isEthereal: false };
            }
            return { templateKey: 'adventurer', scale, role: 'warrior', isFloating: false, isEthereal: false };
        }

        // 3. Orcs, Goblins, Snagas, Uruks (o)
        if (glyph === 'o') {
            if (name.includes('goblin') || name.includes('snaga')) {
                return { templateKey: 'punk', scale: 0.72, role: 'rogue', isFloating: false, isEthereal: false };
            }
            if (name.includes('archer') || name.includes('scout') || name.includes('sniper')) {
                return { templateKey: 'adventurer', scale: 0.85, role: 'archer', isFloating: false, isEthereal: false };
            }
            if (name.includes('shaman') || name.includes('mage') || name.includes('curse')) {
                return { templateKey: 'witch', scale: 0.85, role: 'mage', isFloating: false, isEthereal: false };
            }
            if (name.includes('uruk') || name.includes('captain') || name.includes('chieftain') || name.includes('leader') || name.includes('black orc')) {
                return { templateKey: 'punk', scale: 0.98, role: 'barbarian', isFloating: false, isEthereal: false };
            }
            return { templateKey: 'adventurer', scale: 0.88, role: 'barbarian', isFloating: false, isEthereal: false };
        }

        // 4. Skeletons (s)
        if (glyph === 's') {
            if (name.includes('archer') || name.includes('scout') || name.includes('sniper')) {
                return { templateKey: 'soldier', scale: 0.88, role: 'archer', isFloating: false, isEthereal: false };
            }
            if (name.includes('mage') || name.includes('sorcerer') || name.includes('druj') || name.includes('necromancer')) {
                return { templateKey: 'witch', scale: 0.88, role: 'mage', isFloating: false, isEthereal: false };
            }
            return { templateKey: 'medieval', scale: 0.92, role: 'warrior', isFloating: false, isEthereal: false };
        }

        // 5. Zombies, Mummies, Ghouls (z)
        if (glyph === 'z') {
            if (name.includes('worker') || name.includes('ghoul')) {
                return { templateKey: 'worker', scale: 0.90, role: 'unarmed', isFloating: false, isEthereal: false };
            }
            if (name.includes('mummy')) {
                return { templateKey: 'farmer', scale: 0.90, role: 'unarmed', isFloating: false, isEthereal: false };
            }
            return { templateKey: 'adventurer', scale: 0.90, role: 'unarmed', isFloating: false, isEthereal: false };
        }

        // 6. Liches (L)
        if (glyph === 'L') {
            return { templateKey: 'witch', scale: 1.05, role: 'mage', isFloating: false, isEthereal: true };
        }

        // 7. Wights, Wraiths, Nazgul (W)
        if (glyph === 'W') {
            if (name.includes('nazgul') || name.includes('ringwraith') || name.includes('witch-king')) {
                return { templateKey: 'medieval', scale: 1.10, role: 'twohandedsword', isFloating: true, isEthereal: true };
            }
            if (name.includes('wraith')) {
                return { templateKey: 'witch', scale: 1.00, role: 'mage', isFloating: true, isEthereal: true };
            }
            return { templateKey: 'medieval', scale: 0.98, role: 'warrior', isFloating: true, isEthereal: true };
        }

        // 8. Ghosts, Spectres (G)
        if (glyph === 'G') {
            return { templateKey: 'witch', scale: 0.90, role: 'unarmed', isFloating: true, isEthereal: true };
        }

        // 9. Vampires (V)
        if (glyph === 'V') {
            return { templateKey: 'king', scale: 1.00, role: 'warrior', isFloating: false, isEthereal: false };
        }

        // 10. Ainur, Maiar (A)
        if (glyph === 'A') {
            return { templateKey: 'king', scale: 1.15, role: 'twohandedsword', isFloating: false, isEthereal: false };
        }

        // 11. Giants, Morgoth (P)
        if (glyph === 'P') {
            const scale = name.includes('morgoth') ? 1.55 : 1.35;
            return { templateKey: 'king', scale, role: 'barbarian', isFloating: false, isEthereal: false };
        }

        // 12. Trolls (T), Ogres (O)
        if (glyph === 'T' || glyph === 'O') {
            return { templateKey: 'punk', scale: glyph === 'T' ? 1.25 : 1.18, role: 'barbarian', isFloating: false, isEthereal: false };
        }

        // 13. Kobolds (k) -> Puglin.glb
        if (glyph === 'k' || name.includes('kobold')) {
            return { templateKey: 'puglin', scale: 0.65, role: 'rogue', isFloating: false, isEthereal: false };
        }

        // 14. Demons / Imps (u / U) -> Imp.glb
        if (glyph === 'u' || (glyph === 'U' && (name.includes('imp') || name.includes('minor') || name.includes('demon')))) {
            return { templateKey: 'imp', scale: glyph === 'U' ? 1.10 : 0.75, role: 'unarmed', isFloating: true, isEthereal: false };
        }

        // 15. Rodents (r) -> Rat.obj
        if (glyph === 'r' || name.includes('rat')) {
            return { templateKey: 'rat', scale: 0.42, role: 'unarmed', isFloating: false, isEthereal: false };
        }

        // 16. Snakes & Serpents (J / n) -> Snake.obj / Snake_angry.obj
        if (glyph === 'J' || glyph === 'n' || name.includes('snake') || name.includes('serpent')) {
            const key = (name.includes('viper') || name.includes('cobra') || name.includes('rattle') || name.includes('black')) ? 'snake_angry' : 'snake';
            return { templateKey: key, scale: 0.38, role: 'unarmed', isFloating: false, isEthereal: false };
        }

        // 17. Spiders & Scorpions (S) -> Spider.obj
        if (glyph === 'S' || name.includes('spider') || name.includes('scorpion')) {
            return { templateKey: 'spider', scale: 0.48, role: 'unarmed', isFloating: false, isEthereal: false };
        }

        // 18. Frogs & Amphibians (R) -> Frog.obj
        if (glyph === 'R' || name.includes('frog') || name.includes('toad')) {
            return { templateKey: 'frog', scale: 0.38, role: 'unarmed', isFloating: false, isEthereal: false };
        }

        // 19. Wasps & Flying Insects (F) -> Wasp.obj
        if (glyph === 'F' || name.includes('wasp') || name.includes('fly')) {
            return { templateKey: 'wasp', scale: 0.42, role: 'unarmed', isFloating: true, isEthereal: false };
        }

        return null; // Fallback to anatomical procedural creature tokens
    }

    applyCharacterSkinAndTint(mesh, glyph, rawName, colorHex, templateKey, isEthereal = false) {
        if (!mesh) return;
        if (isEthereal) {
            const spectralColor = new THREE.Color(colorHex);
            mesh.traverse((c) => {
                if (c.isMesh && c.material) {
                    c.material = c.material.clone();
                    c.material.transparent = true;
                    c.material.opacity = 0.60;
                    c.material.emissive = spectralColor;
                    c.material.emissiveIntensity = 0.85;
                }
            });
            return;
        }

        const lowerName = (rawName || '').toLowerCase();
        const tint = new THREE.Color(colorHex);
        // Is tint neutral white / light gray? (If so, preserve original KayKit textures without white bleaching)
        const isWhiteTint = (tint.r > 0.90 && tint.g > 0.90 && tint.b > 0.90);

        const isDemon = glyph === 'U' || (glyph === 'u' && !lowerName.includes('imp'));
        const isAinu = glyph === 'A';
        const isUndead = glyph === 's' || glyph === 'z' || glyph === 'L' || glyph === 'V';
        const isDragon = glyph === 'D' || glyph === 'd' || glyph === 'M';
        const isElemental = glyph === 'E' || glyph === 'v';

        let paletteTex = null;
        if (this.characterPalettes) {
            if (templateKey === 'soldier') paletteTex = this.characterPalettes.get('knight');
            else if (templateKey === 'witch') paletteTex = this.characterPalettes.get('mage');
            else if (templateKey === 'punk' || templateKey === 'casual') paletteTex = this.characterPalettes.get('rogue');
            else if (templateKey === 'adventurer' || templateKey === 'farmer' || templateKey === 'worker' || templateKey === 'medieval' || templateKey === 'king') paletteTex = this.characterPalettes.get('barbarian');
            else if (templateKey === 'skeleton') paletteTex = this.characterPalettes.get('skeleton');
        }

        mesh.traverse((c) => {
            if (c.isMesh && c.material) {
                const origMat = c.material;
                const mat = origMat.clone();
                const matName = (mat.name || c.name || '').toLowerCase();

                if (paletteTex && !mat.map) {
                    mat.map = paletteTex;
                    mat.needsUpdate = true;
                }

                // Specialized handling for Rat
                if (templateKey === 'rat') {
                    if (matName.includes('pink') || matName.includes('nose') || matName.includes('ear') || matName.includes('tail')) {
                        mat.color.setHex(0xd9a6ad);
                    } else {
                        if (lowerName.includes('white') || isWhiteTint) {
                            mat.color.setHex(0xf2f2f2);
                        } else if (lowerName.includes('black') || lowerName.includes('shadow') || lowerName.includes('dark')) {
                            mat.color.setHex(0x1f1f1f);
                        } else if (!isWhiteTint) {
                            mat.color.copy(tint).multiplyScalar(0.75);
                        } else {
                            mat.color.setHex(0x594738);
                        }
                    }
                } else if (templateKey === 'spider' || templateKey === 'snake' || templateKey === 'snake_angry' || templateKey === 'frog' || templateKey === 'wasp') {
                    if (!isWhiteTint) {
                        mat.color.lerp(tint, 0.55);
                    }
                } else if (isDemon) {
                    mat.emissive = new THREE.Color(0xf24714);
                    mat.emissiveIntensity = 0.85;
                } else if (isAinu) {
                    mat.emissive = new THREE.Color(0xf2e066);
                    mat.emissiveIntensity = 0.65;
                } else if (isDragon) {
                    mat.metalness = 0.35;
                    mat.roughness = 0.45;
                    if (!isWhiteTint) {
                        mat.emissive = tint.clone().multiplyScalar(0.5);
                        mat.emissiveIntensity = 0.8;
                    }
                } else if (isElemental) {
                    mat.emissive = !isWhiteTint ? tint.clone() : new THREE.Color(0xb3ccff);
                    mat.emissiveIntensity = 1.2;
                } else if (isUndead) {
                    mat.roughness = Math.min(1.0, (mat.roughness || 0.5) + 0.15);
                    if (matName.includes('skin') || matName.includes('head') || matName.includes('face')) {
                        mat.color.lerp(new THREE.Color(0xb3b8ae), 0.50);
                    }
                } else if (glyph === 'o' || glyph === 'k' || glyph === 'y' || glyph === 'T' || glyph === 'O') {
                    // Orcs, goblins, kobolds, trolls - customize skin tone while leaving clothes and gear intact (1:1 with Godot MonsterModelResolver.cs:3372-3393)
                    if (matName.includes('skin') || matName.includes('head') || matName.includes('face') || matName.includes('body')) {
                        if (glyph === 'o') {
                            mat.color.setHex(0x527a38); // Orc green skin
                        } else if (glyph === 'k') {
                            mat.color.setHex(0x946138); // Kobold reptilian skin
                        } else if (glyph === 'y') {
                            mat.color.setHex(0x7a8561); // Yeek mottled skin
                        } else {
                            mat.color.setHex(0x73856b); // Troll/ogre hide
                        }
                    } else if (!isWhiteTint) {
                        mat.color.lerp(tint, 0.25);
                    }
                } else if (!isWhiteTint && !matName.includes('skin') && !matName.includes('face') && !matName.includes('head') && !matName.includes('hair') && !matName.includes('eye')) {
                    // Accent tinting on garments/armor for distinct monster variants (1:1 with Godot MonsterModelResolver.cs:3399-3403)
                    mat.color.lerp(tint, 0.28);
                }

                c.material = mat;
            }
        });
    }

    createFoggyMonsterAura(glyph, rawName, colorHex, modelHeight, isFloating) {
        const group = new THREE.Group();
        const mats = [];
        const baseColor = new THREE.Color(colorHex || 0x66ccff);
        const etherealColor = baseColor.clone().lerp(new THREE.Color(0x77eeff), 0.65);

        // 1. Ethereal Shroud / Misty Apparition Envelope
        const radius = Math.max(0.35, modelHeight * 0.40);
        const shroudGeo = new THREE.SphereGeometry(radius, 16, 12);
        shroudGeo.scale(1.0, 1.35, 1.0);
        const shroudMat = new THREE.MeshBasicMaterial({
            color: etherealColor,
            transparent: true,
            opacity: 0.38,
            blending: THREE.AdditiveBlending,
            depthWrite: false,
            side: THREE.DoubleSide
        });
        mats.push(shroudMat);
        const shroudMesh = new THREE.Mesh(shroudGeo, shroudMat);
        shroudMesh.position.y = isFloating ? 0.35 + modelHeight * 0.45 : modelHeight * 0.45;
        group.add(shroudMesh);

        // 2. Inner Glowing Core / Silhouette
        const coreGeo = new THREE.CylinderGeometry(radius * 0.35, radius * 0.70, Math.max(0.45, modelHeight * 0.75), 12);
        const coreMat = new THREE.MeshBasicMaterial({
            color: 0xddffff,
            transparent: true,
            opacity: 0.52,
            blending: THREE.AdditiveBlending,
            depthWrite: false
        });
        mats.push(coreMat);
        const coreMesh = new THREE.Mesh(coreGeo, coreMat);
        coreMesh.position.y = shroudMesh.position.y;
        group.add(coreMesh);

        // 3. Floating Eye special ethereal pupil / eye ring
        if (glyph === 'e') {
            const eyeGeo = new THREE.TorusGeometry(radius * 0.65, 0.04, 8, 20);
            const eyeMat = new THREE.MeshBasicMaterial({
                color: 0xffd700,
                transparent: true,
                opacity: 0.85,
                blending: THREE.AdditiveBlending,
                depthWrite: false
            });
            mats.push(eyeMat);
            const eyeMesh = new THREE.Mesh(eyeGeo, eyeMat);
            eyeMesh.position.y = shroudMesh.position.y;
            group.add(eyeMesh);
        }

        // 4. Ground Sensed Ripple / Detection Beacon
        const rippleGeo = new THREE.RingGeometry(0.12, 0.52, 24);
        const rippleMat = new THREE.MeshBasicMaterial({
            color: etherealColor,
            transparent: true,
            opacity: 0.45,
            side: THREE.DoubleSide,
            blending: THREE.AdditiveBlending,
            depthWrite: false
        });
        mats.push(rippleMat);
        const rippleMesh = new THREE.Mesh(rippleGeo, rippleMat);
        rippleMesh.rotation.x = -Math.PI / 2;
        rippleMesh.position.y = 0.03;
        group.add(rippleMesh);

        group.foggyMaterials = mats;
        group.groundRipples = rippleMesh;
        group.visible = false;

        return group;
    }

    createMonster3DEntity(m, isTargeted) {
        const root = new THREE.Group();
        const glyph = m.glyph || '?';
        const race = (m.race || m.name || '').toLowerCase();
        const colorHex = getAngbandColorString(m.attr);

        const config = this.resolveMonsterModelConfig(glyph, m.race || m.name);
        let mesh = null;
        let modelHeight = 1.25;
        let isFloating = false;

        if (config && this.monsterTemplates && this.monsterTemplates.has(config.templateKey)) {
            const tmplData = this.monsterTemplates.get(config.templateKey);
            const srcObj = tmplData.scene || tmplData;
            mesh = this.cloneModelHierarchy(srcObj);

            const s = config.scale || (tmplData.defaultScale || 1.0);
            mesh.scale.set(s, s, s);

            isFloating = config.isFloating;
            if (isFloating) mesh.position.y = 0.35;

            // Compute true physical height from geometry bounding box
            mesh.updateMatrixWorld(true);
            const bbox = new THREE.Box3().setFromObject(mesh);
            if (isFinite(bbox.max.y) && bbox.max.y > 0) {
                modelHeight = bbox.max.y;
            } else if (config.templateKey === 'rat') modelHeight = 0.95;
            else if (config.templateKey === 'snake' || config.templateKey === 'snake_angry') modelHeight = 1.20;
            else if (config.templateKey === 'spider') modelHeight = 1.00;
            else if (config.templateKey === 'frog') modelHeight = 0.65;
            else if (config.templateKey === 'wasp') modelHeight = 1.35;
            else if (config.templateKey === 'imp' || config.templateKey === 'puglin') modelHeight = 1.35;
            else if (config.templateKey === 'king' || config.templateKey === 'punk') modelHeight = 2.05;
            else modelHeight = 1.85;

            // Materials & tinting (Exact 1:1 Parity with Godot MonsterModelResolver.cs:3189-3406)
            this.applyCharacterSkinAndTint(mesh, glyph, m.race || m.name, colorHex, config.templateKey, config.isEthereal);

            // Role-accurate equipment slots
            this.configureCreatureEquipment(mesh, config.role);
        }

        // Procedural anatomical creature fallback for unmapped non-humanoids (eyes, worms, slimes, mushrooms, etc.)
        if (!mesh) {
            mesh = this.createProceduralCreatureMesh(glyph, m.race || m.name, colorHex);
            if (glyph === 'b' || glyph === 'B' || glyph === 'e' || glyph === 'W' || glyph === 'G' || glyph === 'I' || race.includes('wasp') || race.includes('eye') || race.includes('bat') || race.includes('ghost') || race.includes('wraith')) {
                isFloating = true;
                modelHeight = 1.10;
            } else if (glyph === 'm' || glyph === ',' || glyph === 'r' || glyph === 's' || glyph === 'c' || glyph === 'w') {
                modelHeight = 0.55;
            } else if (glyph === 'd' || glyph === 'D' || glyph === 'P' || glyph === 'T' || glyph === 'O' || glyph === 'u' || glyph === 'U') {
                modelHeight = 1.65;
            } else if (glyph === 'S' || glyph === 'f' || glyph === 'C' || glyph === 'Z' || glyph === 'R') {
                modelHeight = 0.75;
            }
        }

        root.add(mesh);
        root.creatureMesh = mesh;
        root.modelHeight = modelHeight;
        root.isFloating = isFloating;
        root.config = config;
        root.isFallback = !config || !this.monsterTemplates || !this.monsterTemplates.has(config.templateKey);
        root.colorHex = colorHex;
        root.glyph = glyph;
        root.race = m.race || m.name || '';

        // Sensed / Invisible Foggy Misty Aura (depicts invisible or dark unlit sensed creatures)
        const foggyAura = this.createFoggyMonsterAura(glyph, m.race || m.name, colorHex, modelHeight, isFloating);
        root.add(foggyAura);
        root.foggyAura = foggyAura;
        root.foggyMaterials = foggyAura.foggyMaterials;
        root.groundRipples = foggyAura.groundRipples;

        // Overhead Billboarding Nameplate & Health Bar (always clear of creature model with generous margin)
        const nameplate = this.createNameplateSprite(m, isTargeted);
        nameplate.position.set(0, modelHeight + 0.42, 0);
        root.add(nameplate);
        root.nameplate = nameplate;

        return root;
    }

    updateMonsters(monsters, player) {
        const activeIds = new Set();
        const targetId = (player && player.target && player.target.id !== undefined) ? player.target.id.toString() : null;

        const px = player ? player.x : 0;
        const py = player ? player.y : 0;
        const depth = player && player.depth !== undefined ? player.depth : 0;
        const outdoors = depth === 0;

        monsters.forEach(m => {
            const id = m.id !== undefined ? m.id.toString() : `${m.x}_${m.y}_${m.glyph}`;
            activeIds.add(id);
            const isTargeted = (targetId && targetId === id);
            let entity = this.monsters.get(id);

            const wx = m.x * this.cellSize;
            const wz = m.y * this.cellSize;
            const baseY = 0.0;

            // Sensed / Invisible / Darkness Visibility Logic:
            let isTileLit = outdoors;
            if (!isTileLit && this.lastMap) {
                const flag = this.getFlagAt(this.lastMap, m.x, m.y);
                const inView = (flag & 0x2) !== 0;
                const lighting = (flag >> 2) & 0x3;
                const distSq = (m.x - px) ** 2 + (m.y - py) ** 2;
                isTileLit = inView && (lighting < 3 || distSq <= (this.torchRadius + 1) ** 2);
            }
            const isSensed = !!(m.invisible || m.detected || m.unlit || !isTileLit);

            if (!entity) {
                entity = this.createMonster3DEntity(m, isTargeted);
                entity.position.set(wx, baseY, wz);
                entity.targetPos = new THREE.Vector3(wx, baseY, wz);
                entity.lastHp = m.hp;
                entity.lastSensed = isSensed;
                this.scene.add(entity);
                this.monsters.set(id, entity);
            } else {
                entity.targetPos.set(wx, baseY, wz);

                // Auto-upgrade fallback mannequin entity to authentic GLTF/OBJ model once template has loaded!
                if (entity.isFallback && entity.config && this.monsterTemplates && this.monsterTemplates.has(entity.config.templateKey)) {
                    const tmplData = this.monsterTemplates.get(entity.config.templateKey);
                    const srcObj = tmplData.scene || tmplData;
                    const upgradedMesh = this.cloneModelHierarchy(srcObj);
                    if (upgradedMesh) {
                        const s = entity.config.scale || (tmplData.defaultScale || 1.0);
                        upgradedMesh.scale.set(s, s, s);
                        if (entity.config.isFloating) upgradedMesh.position.y = 0.35;

                        upgradedMesh.updateMatrixWorld(true);
                        const bbox = new THREE.Box3().setFromObject(upgradedMesh);
                        if (isFinite(bbox.max.y) && bbox.max.y > 0) {
                            entity.modelHeight = bbox.max.y;
                        } else {
                            entity.modelHeight = 1.85;
                        }

                        this.applyCharacterSkinAndTint(upgradedMesh, entity.glyph, entity.race, entity.colorHex, entity.config.templateKey, entity.config.isEthereal);

                        this.configureCreatureEquipment(upgradedMesh, entity.config.role);

                        entity.remove(entity.creatureMesh);
                        entity.creatureMesh = upgradedMesh;
                        entity.add(upgradedMesh);
                        entity.isFallback = false;

                        if (entity.nameplate) {
                            entity.nameplate.position.set(0, entity.modelHeight + 0.42, 0);
                        }
                    }
                }

                // Monster HP delta tracking: Spawn floating damage numbers above entity head
                if (entity.lastHp !== undefined && m.hp !== undefined && m.hp < entity.lastHp) {
                    const dmg = entity.lastHp - m.hp;
                    const textPos = new THREE.Vector3(wx, entity.modelHeight + 0.55, wz);
                    this.spawnFloatingText(`-${dmg}`, textPos, '#ff9900', 1.20);
                    this.spawnHitSparks(new THREE.Vector3(wx, entity.modelHeight * 0.5, wz), getAngbandColorString(m.attr), 14);
                    if (this.audio) {
                        const pan = this.calculateStereoPan(wx, wz);
                        this.audio.playMonsterVocal(m.glyph || entity.glyph, 'grunt', pan);
                    }
                }

                // Refresh nameplate if HP, status, target state, or sensed state changed
                if (entity.lastHp !== m.hp || entity.lastTargeted !== isTargeted || entity.lastAsleep !== m.asleep || entity.lastAfraid !== m.afraid || entity.lastSensed !== isSensed) {
                    entity.remove(entity.nameplate);
                    entity.nameplate = this.createNameplateSprite(m, isTargeted);
                    entity.nameplate.position.set(0, entity.modelHeight + 0.42, 0);
                    entity.add(entity.nameplate);
                    entity.lastHp = m.hp;
                    entity.lastTargeted = isTargeted;
                    entity.lastAsleep = m.asleep;
                    entity.lastAfraid = m.afraid;
                    entity.lastSensed = isSensed;
                }
            }

            // In Angband, all monsters emitted in frame.monsters are detected / sensed by the player
            entity.visible = true;
            if (isSensed) {
                if (entity.creatureMesh) entity.creatureMesh.visible = false;
                if (entity.foggyAura) entity.foggyAura.visible = true;
            } else {
                if (entity.creatureMesh) entity.creatureMesh.visible = true;
                if (entity.foggyAura) entity.foggyAura.visible = false;
            }
        });

        for (const [id, entity] of this.monsters.entries()) {
            if (!activeIds.has(id)) {
                if (entity.visible && this.audio) {
                    const pan = this.calculateStereoPan(entity.position.x, entity.position.z);
                    this.audio.playMonsterDeath(entity.glyph, pan);
                }
                this.scene.remove(entity);
                this.monsters.delete(id);
            }
        }
    }

    createItem3DEntity(it) {
        const group = new THREE.Group();
        const g = it.glyph || '?';
        const lowerName = (it.name || '').toLowerCase();
        const colorHex = getAngbandColorString(it.attr);

        let mesh = null;

        // 1. Comprehensive Model & Keyword Resolver (Matches Godot ItemModelResolver.cs:158-350)
        let tmpl = null;

        // Gold & Coins
        if (g === '$' || lowerName.includes('gold') || lowerName.includes('coin') || lowerName.includes('copper') || lowerName.includes('silver')) {
            tmpl = this.itemTemplates.get('$') || this.itemTemplates.get('coin');
        }
        // Potions & Flasks
        else if (g === '!' || lowerName.includes('potion') || lowerName.includes('flask') || lowerName.includes('draught') || lowerName.includes('elixir')) {
            tmpl = this.itemTemplates.get('!');
        }
        // Books & Spellbooks
        else if (lowerName.includes('book') || lowerName.includes('tome') || lowerName.includes('grimoire') || lowerName.includes('prayer') || lowerName.includes('sorcery') || lowerName.includes('spellbook')) {
            tmpl = this.itemTemplates.get('book');
        }
        // Scrolls & Parchment
        else if (g === '?' || lowerName.includes('scroll') || lowerName.includes('parchment')) {
            tmpl = this.itemTemplates.get('?');
        }
        // Rings
        else if (g === '=' || lowerName.includes('ring') || lowerName.includes('band')) {
            tmpl = this.itemTemplates.get('=');
        }
        // Amulets & Necklaces
        else if (g === '"' || lowerName.includes('amulet') || lowerName.includes('necklace') || lowerName.includes('pendant') || lowerName.includes('medallion') || lowerName.includes('periapt')) {
            tmpl = this.itemTemplates.get('"') || this.itemTemplates.get('=');
        }
        // Gems & Crystals
        else if (g === '*' || lowerName.includes('gem') || lowerName.includes('crystal') || lowerName.includes('diamond') || lowerName.includes('ruby') || lowerName.includes('emerald') || lowerName.includes('sapphire') || lowerName.includes('phial') || lowerName.includes('star of') || lowerName.includes('arkenstone')) {
            tmpl = this.itemTemplates.get('*');
        }
        // Chests & Boxes
        else if (lowerName.includes('chest') || lowerName.includes('coffer') || lowerName.includes('box')) {
            tmpl = this.itemTemplates.get('chest');
        }
        // Food & Rations
        else if (g === ',' || lowerName.includes('ration') || lowerName.includes('food') || lowerName.includes('meat') || lowerName.includes('bread') || lowerName.includes('mushroom') || lowerName.includes('apple') || lowerName.includes('slime mold')) {
            tmpl = this.itemTemplates.get(',');
        }
        // Skulls & Remains
        else if (lowerName.includes('skull') || lowerName.includes('bone') || lowerName.includes('skeleton')) {
            tmpl = this.itemTemplates.get('skull');
        }
        // Keys & Lockpicks
        else if (lowerName.includes('key') || lowerName.includes('lockpick')) {
            tmpl = this.itemTemplates.get('key');
        }
        // Bags & Pouches
        else if (lowerName.includes('backpack') || lowerName.includes('sack')) {
            tmpl = this.itemTemplates.get('backpack');
        } else if (lowerName.includes('bag') || lowerName.includes('pouch')) {
            tmpl = this.itemTemplates.get('pouch');
        }
        // Shields
        else if (g === '(' || lowerName.includes('shield') || lowerName.includes('buckler') || lowerName.includes('targe')) {
            if (lowerName.includes('round') || lowerName.includes('small')) {
                tmpl = this.itemTemplates.get('shield_round');
            } else {
                tmpl = this.itemTemplates.get('shield_heater') || this.itemTemplates.get('shield_round');
            }
        }
        // Helms & Crowns
        else if (lowerName.includes('crown') || lowerName.includes('coronet') || lowerName.includes('helm') || lowerName.includes('cap') || lowerName.includes('hat')) {
            tmpl = this.itemTemplates.get('crown');
        }
        // Gloves & Gauntlets
        else if (lowerName.includes('glove') || lowerName.includes('gauntlet') || lowerName.includes('cesta') || lowerName.includes('bracer')) {
            tmpl = this.itemTemplates.get('glove');
        }
        // Body Armor & Cloaks (Glyph '[')
        else if (g === '[' || lowerName.includes('plate') || lowerName.includes('chain') || lowerName.includes('mail') || lowerName.includes('armor') || lowerName.includes('cuirass') || lowerName.includes('corselet') || lowerName.includes('robe') || lowerName.includes('cloak')) {
            if (lowerName.includes('leather') || lowerName.includes('soft') || lowerName.includes('robe') || lowerName.includes('cloak')) {
                tmpl = this.itemTemplates.get('armor_leather');
            } else {
                tmpl = this.itemTemplates.get('armor_metal') || this.itemTemplates.get('armor_metal2');
            }
        }
        // Footwear: Boots, Shoes, Sandals (Glyph ']')
        else if (g === ']' || lowerName.includes('sandal') || lowerName.includes('boot') || lowerName.includes('shoe') || lowerName.includes('greave')) {
            // Footwear will be built with dedicated procedural 3D shoe pair below if no OBJ
            tmpl = null;
        }
        // Weapons: Axes
        else if (lowerName.includes('battle axe') || lowerName.includes('great axe') || lowerName.includes('broad axe') || lowerName.includes('halberd') || lowerName.includes('poleaxe')) {
            tmpl = this.itemTemplates.get('axe_double') || this.itemTemplates.get('axe');
        } else if (lowerName.includes('axe') || lowerName.includes('cleaver') || lowerName.includes('hatchet')) {
            tmpl = this.itemTemplates.get('axe');
        }
        // Weapons: Hammers & Maces
        else if (lowerName.includes('war hammer') || lowerName.includes('great hammer') || lowerName.includes('mattock')) {
            tmpl = this.itemTemplates.get('hammer_double') || this.itemTemplates.get('hammer');
        } else if (lowerName.includes('hammer') || lowerName.includes('mace') || lowerName.includes('flail') || lowerName.includes('star') || lowerName.includes('club') || lowerName.includes('cudgel') || lowerName.includes('whip')) {
            tmpl = this.itemTemplates.get('hammer');
        }
        // Weapons: Staves, Spears & Polearms
        else if (g === '/' || g === '_' || g === '|' || lowerName.includes('spear') || lowerName.includes('lance') || lowerName.includes('pike') || lowerName.includes('trident') || lowerName.includes('staff')) {
            tmpl = this.itemTemplates.get('spear');
        }
        // Weapons: Scythes
        else if (lowerName.includes('scythe')) {
            tmpl = this.itemTemplates.get('scythe') || this.itemTemplates.get('spear');
        }
        // Weapons: Daggers & Knives
        else if (lowerName.includes('dagger') || lowerName.includes('knife') || lowerName.includes('rapier') || lowerName.includes('stiletto') || lowerName.includes('main gauche') || lowerName.includes('misericorde') || lowerName.includes('athame')) {
            tmpl = this.itemTemplates.get('dagger');
        }
        // Weapons: Two-Handed Swords & Greatswords
        else if (lowerName.includes('two-handed') || lowerName.includes('great sword') || lowerName.includes('claymore') || lowerName.includes('zweihander') || lowerName.includes('flamberge')) {
            tmpl = this.itemTemplates.get('claymore');
        } else if (lowerName.includes('bastard') || lowerName.includes('broad sword') || lowerName.includes('broadsword')) {
            tmpl = this.itemTemplates.get('sword_big') || this.itemTemplates.get('claymore');
        }
        // Weapons: Swords & Blades (Glyph ')')
        else if (g === ')' || lowerName.includes('sword') || lowerName.includes('blade') || lowerName.includes('sabre') || lowerName.includes('scimitar') || lowerName.includes('cutlass') || lowerName.includes('katana') || lowerName.includes('foil')) {
            tmpl = this.itemTemplates.get(')');
        }
        // Bows & Crossbows (Glyph '}')
        else if (g === '}' || lowerName.includes('bow') || lowerName.includes('crossbow') || lowerName.includes('arbalest') || lowerName.includes('sling')) {
            tmpl = this.itemTemplates.get('}');
        }
        // Slings Ammo: Pebbles, Stones, Rocks (Glyph '{')
        else if (lowerName.includes('pebble') || lowerName.includes('stone') || lowerName.includes('rock')) {
            tmpl = this.itemTemplates.get('pebble') || this.itemTemplates.get('*');
        }
        // Darts
        else if (lowerName.includes('dart')) {
            tmpl = this.itemTemplates.get('dart');
        }
        // Shots & Bullets (Iron shot, sling bullet)
        else if (lowerName.includes('shot') || lowerName.includes('bullet')) {
            tmpl = this.itemTemplates.get('pebble') || this.itemTemplates.get('*');
        }
        // Arrows & Bolts (Glyph '{')
        else if (g === '{' || lowerName.includes('arrow') || lowerName.includes('bolt')) {
            tmpl = this.itemTemplates.get('{');
        }

        if (tmpl) {
            mesh = tmpl.clone(true);
        }

        // 2. High-Quality Stylized Procedural 3D Item Pickups
        // Ensures NO item appears as a raw generic white octahedron (Exact 1:1 Parity with Godot GetPickupMesh)
        if (!mesh) {
            const isMetal = (g === ')' || g === '[' || g === '(' || g === '=' || lowerName.includes('iron') || lowerName.includes('steel') || lowerName.includes('metal') || lowerName.includes('chain') || lowerName.includes('plate'));
            const isGold = (g === '$' || lowerName.includes('gold') || lowerName.includes('crown'));
            const mat = new THREE.MeshStandardMaterial({
                color: new THREE.Color(colorHex),
                emissive: new THREE.Color(colorHex),
                emissiveIntensity: 0.35,
                metalness: isMetal ? 0.88 : (isGold ? 0.95 : 0.15),
                roughness: isMetal ? 0.30 : (isGold ? 0.20 : 0.65)
            });

            // Footwear / Boots / Sandals / Shoes (Glyph ']' or footwear keywords)
            if (g === ']' || lowerName.includes('sandal') || lowerName.includes('boot') || lowerName.includes('shoe')) {
                const bootsGroup = new THREE.Group();
                const soleMat = new THREE.MeshStandardMaterial({ color: 0x3d2716, roughness: 0.85, metalness: 0.05 });
                const strapMat = new THREE.MeshStandardMaterial({ color: new THREE.Color(colorHex), roughness: 0.70, metalness: 0.10 });

                [-0.07, 0.07].forEach(xOffset => {
                    // Sole base
                    const sole = new THREE.Mesh(new THREE.BoxGeometry(0.09, 0.035, 0.20), soleMat);
                    sole.position.set(xOffset, 0.018, 0);
                    bootsGroup.add(sole);

                    // Upper foot body / strap
                    const isBoot = lowerName.includes('boot') || lowerName.includes('greave');
                    const upperHeight = isBoot ? 0.14 : 0.06;
                    const upper = new THREE.Mesh(new THREE.BoxGeometry(0.08, upperHeight, isBoot ? 0.14 : 0.10), strapMat);
                    upper.position.set(xOffset, 0.035 + upperHeight / 2, isBoot ? -0.02 : 0.01);
                    bootsGroup.add(upper);
                });
                mesh = bootsGroup;
            }
            // Torches & Light Sources (Glyph '~')
            else if (g === '~' || lowerName.includes('torch') || lowerName.includes('lantern')) {
                const torchGroup = new THREE.Group();
                const woodShaft = new THREE.Mesh(new THREE.CylinderGeometry(0.025, 0.025, 0.45, 8), new THREE.MeshStandardMaterial({ color: 0x5c3a21, roughness: 0.85 }));
                torchGroup.add(woodShaft);
                const flame = new THREE.Mesh(new THREE.ConeGeometry(0.05, 0.12, 8), new THREE.MeshStandardMaterial({ color: 0xffaa00, emissive: 0xff5500, emissiveIntensity: 1.5 }));
                flame.position.y = 0.24;
                torchGroup.add(flame);
                mesh = torchGroup;
            }
            // Rings (Glyph '=')
            else if (g === '=' || lowerName.includes('ring')) {
                mesh = new THREE.Mesh(new THREE.TorusGeometry(0.10, 0.035, 12, 24), mat);
                mesh.rotation.x = Math.PI / 4;
            }
            // Amulets & Necklaces (Glyph '"')
            else if (g === '"' || lowerName.includes('amulet')) {
                const amuletGroup = new THREE.Group();
                const pendant = new THREE.Mesh(new THREE.CylinderGeometry(0.09, 0.09, 0.025, 16), mat);
                pendant.rotation.x = Math.PI / 2;
                amuletGroup.add(pendant);
                const gem = new THREE.Mesh(new THREE.SphereGeometry(0.045, 12, 8), new THREE.MeshStandardMaterial({ color: 0x00ffff, emissive: 0x0088cc, emissiveIntensity: 1.2 }));
                gem.position.z = 0.015;
                amuletGroup.add(gem);
                mesh = amuletGroup;
            }
            // Potions (Glyph '!')
            else if (g === '!') {
                const flaskGroup = new THREE.Group();
                const body = new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.10, 0.24, 12), mat);
                const neck = new THREE.Mesh(new THREE.CylinderGeometry(0.03, 0.03, 0.08, 12), mat);
                neck.position.y = 0.15;
                flaskGroup.add(body);
                flaskGroup.add(neck);
                mesh = flaskGroup;
            }
            // Scrolls (Glyph '?')
            else if (g === '?') {
                mesh = new THREE.Mesh(new THREE.CylinderGeometry(0.04, 0.04, 0.35, 12), mat);
                mesh.rotation.z = Math.PI / 2;
            }
            // Weapons (Glyph ')')
            else if (g === ')') {
                mesh = new THREE.Mesh(new THREE.BoxGeometry(0.05, 0.50, 0.10), mat);
            }
            // Armor & Shields (Glyphs '[', '(')
            else if (g === '[' || g === '(') {
                mesh = new THREE.Mesh(new THREE.BoxGeometry(0.28, 0.36, 0.08), mat);
            }
            // Staves, Wands, Polearms (Glyphs '/', '_', '|')
            else if (g === '/' || g === '_' || g === '|') {
                mesh = new THREE.Mesh(new THREE.CylinderGeometry(0.02, 0.02, 0.55, 8), mat);
            }
            // Sling ammo / Pebbles / Stones / Rocks / Shots
            else if (lowerName.includes('pebble') || lowerName.includes('stone') || lowerName.includes('rock') || lowerName.includes('shot') || lowerName.includes('bullet')) {
                mesh = new THREE.Mesh(new THREE.DodecahedronGeometry(0.08, 1), mat);
            }
            // Food (Glyph ',')
            else if (g === ',') {
                mesh = new THREE.Mesh(new THREE.SphereGeometry(0.12, 12, 8), mat);
            }
            // Default Stylized Relic
            else {
                mesh = new THREE.Mesh(new THREE.BoxGeometry(0.18, 0.18, 0.18), mat);
            }
        }

        group.add(mesh);
        group.itemMesh = mesh;

        // Overhead Item Caption Sprite
        if (it.name) {
            const canvas = document.createElement('canvas');
            canvas.width = 384;
            canvas.height = 64;
            const ctx = canvas.getContext('2d');

            ctx.fillStyle = 'rgba(6, 8, 12, 0.88)';
            ctx.strokeStyle = colorHex;
            ctx.lineWidth = 2;
            if (ctx.roundRect) ctx.roundRect(12, 8, 360, 48, 8);
            else ctx.rect(12, 8, 360, 48);
            ctx.fill();
            ctx.stroke();

            ctx.font = '600 22px "Fira Code", monospace';
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.fillStyle = '#f0f4f8';
            const shortName = it.name.length > 22 ? it.name.substring(0, 21) + '…' : it.name;
            ctx.fillText(shortName, 192, 32);

            const tex = new THREE.CanvasTexture(canvas);
            tex.minFilter = THREE.LinearFilter;
            const sprite = new THREE.Sprite(new THREE.SpriteMaterial({ map: tex, depthTest: false, depthWrite: false }));
            sprite.scale.set(1.2, 0.20, 1.0);
            sprite.position.y = 0.48;
            group.add(sprite);
            group.caption = sprite;
        }

        return group;
    }

    updateItems(items, player) {
        const activeIds = new Set();
        const px = player ? player.x : 0;
        const py = player ? player.y : 0;
        const depth = player && player.depth !== undefined ? player.depth : 0;
        const outdoors = depth === 0;

        items.forEach(it => {
            const id = `${it.x}_${it.y}_${it.name || it.glyph}`;
            activeIds.add(id);
            let entity = this.items.get(id);

            const wx = it.x * this.cellSize;
            const wz = it.y * this.cellSize;

            if (!entity) {
                entity = this.createItem3DEntity(it);
                entity.position.set(wx, 0.18, wz);
                this.scene.add(entity);
                this.items.set(id, entity);
            }

            // Strict Subterranean Visibility Invariant:
            // Items are only visible in 3D if outdoors, or if within player's lit field of view or torchlight
            let isVisible = outdoors;
            if (!isVisible && this.lastMap) {
                const flag = this.getFlagAt(this.lastMap, it.x, it.y);
                const inView = (flag & 0x2) !== 0;
                const lighting = (flag >> 2) & 0x3;
                const distSq = (it.x - px) ** 2 + (it.y - py) ** 2;
                isVisible = inView && (lighting < 3 || distSq <= (this.torchRadius + 1) ** 2);
            }
            entity.visible = isVisible;

            // Only show caption when player is within 6 cells and item is visible
            if (entity.caption) {
                const dist = Math.hypot(it.x - px, it.y - py);
                entity.caption.visible = isVisible && (dist <= 6);
            }
        });

        for (const [id, entity] of this.items.entries()) {
            if (!activeIds.has(id)) {
                this.scene.remove(entity);
                this.items.delete(id);
            }
        }
    }

    animate() {
        requestAnimationFrame(this.animate);

        const delta = 0.016; // ~60fps step
        const tNow = performance.now();

        // Smooth camera position tweening
        if (this.isStepping) {
            this.stepTime += delta;
            const progress = Math.min(1.0, this.stepTime / this.stepDuration);
            const t = progress * (2 - progress); // Ease out quad

            this.camera.position.lerpVectors(this.currentCamPos, this.targetCamPos, t);

            // Reactive Walking Head-bob (subtle vertical sinusoidal breathing)
            this.cameraBobPhase += delta * 18;
            this.camera.position.y = this.eyeHeight + Math.sin(this.cameraBobPhase) * 0.045;

            if (progress >= 1.0) {
                this.currentCamPos.copy(this.targetCamPos);
                this.camera.position.copy(this.targetCamPos);
                this.isStepping = false;
                this.stepPlayedThisMove = false;
            }
        } else {
            this.camera.position.copy(this.targetCamPos);
            this.isStepping = false;
            this.stepPlayedThisMove = false;
            // Idle breathing bob
            this.camera.position.y = this.eyeHeight + Math.sin(tNow * 0.002) * 0.012;
        }

        // Multi-frequency organic torchlight flicker (Godot subtleFlicker equation)
        const subtleFlicker = 1.0 +
            Math.sin(tNow * 0.011) * 0.05 +
            Math.cos(tNow * 0.024) * 0.035 +
            Math.sin(tNow * 0.037) * 0.02;
        const currentTargetEnergy = this.targetTorchEnergy !== undefined ? this.targetTorchEnergy : 2.8;
        this.torchLight.intensity = currentTargetEnergy * subtleFlicker;
        this.torchFillLight.intensity = (currentTargetEnergy * 0.22) * subtleFlicker;

        if (this.flameMesh) {
            this.flameMesh.scale.set(
                1.0 + Math.sin(tNow * 0.015) * 0.14,
                1.0 + Math.cos(tNow * 0.022) * 0.22,
                1.0 + Math.sin(tNow * 0.015) * 0.14
            );
        }

        // Ensure rotation order is always YXZ (Yaw first, then Pitch, then Roll)
        this.camera.rotation.order = 'YXZ';

        // Smooth spring camera turning (1:1 with Godot client)
        const targetYaw = this.targetYaw;
        const currentYaw = this.camera.rotation.y;
        const diff = targetYaw - currentYaw;
        const wrappedDiff = Math.atan2(Math.sin(diff), Math.cos(diff));

        if (Math.abs(wrappedDiff) > 0.002) {
            this.camera.rotation.y += wrappedDiff * Math.min(1.0, delta * 22);
        } else {
            this.camera.rotation.y = targetYaw;
        }

        // Smooth camera pitch based on character height & manual head tilt
        const activeTargetPitch = (this.basePitch !== undefined ? this.basePitch : -0.157) + (this.userPitchOffset || 0.0);
        this.targetPitch = activeTargetPitch;
        const pitchDiff = this.targetPitch - this.camera.rotation.x;
        if (Math.abs(pitchDiff) > 0.001) {
            this.camera.rotation.x += pitchDiff * Math.min(1.0, delta * 12);
        } else {
            this.camera.rotation.x = this.targetPitch;
        }

        // Camera roll: lock strictly to 0 when not turning or experiencing trauma (prevents any left/right room skew)
        let roll = 0;
        if (Math.abs(wrappedDiff) > 0.005) {
            // Very subtle turning roll inertia (1:1 with Godot DungeonWorld.cs:3405)
            roll = Math.max(-0.02, Math.min(0.02, -wrappedDiff * 0.04));
        }
        this.camera.rotation.z = roll;

        // Live real-time broadcast of camera movement & yaw to HUD for smooth needle, vision cone & minimap
        if (window.__app && window.__app.hud) {
            const curYaw = -this.camera.rotation.y;
            const contX = this.cellSize > 0 ? (this.camera.position.x / this.cellSize) : 0;
            const contY = this.cellSize > 0 ? (this.camera.position.z / this.cellSize) : 0;
            const moved = Math.abs(contX - (this._lastBroadcastX || 0)) > 0.015 || Math.abs(contY - (this._lastBroadcastY || 0)) > 0.015;
            const turned = Math.abs(curYaw - (this._lastBroadcastYaw || 0)) > 0.003;
            if (moved || turned) {
                this._lastBroadcastX = contX;
                this._lastBroadcastY = contY;
                this._lastBroadcastYaw = curYaw;
                if (typeof window.__app.hud.onCameraMove === 'function') {
                    window.__app.hud.onCameraMove(contX, contY, curYaw);
                } else if (typeof window.__app.hud.onCameraTurn === 'function') {
                    window.__app.hud.onCameraTurn(curYaw);
                }
            }
        }

        // Camera Screen Trauma / Shake (Decaying trauma scalar matching Godot DungeonWorld.cs)
        if (this.trauma > 0) {
            this.trauma = Math.max(0, this.trauma - delta * 1.5);
            const shake = this.trauma * this.trauma;
            const yawShake = (Math.random() - 0.5) * 0.04 * shake;
            const pitchShake = (Math.random() - 0.5) * 0.04 * shake;
            const rollShake = (Math.random() - 0.5) * 0.02 * shake;
            this.camera.rotation.y += yawShake;
            this.camera.rotation.x += pitchShake;
            this.camera.rotation.z += rollShake;
        }

        // Natural Walk Bobbing & Turn Sway on First-Person Hands (1:1 with Godot ViewModel.cs)
        let bobY = 0;
        let bobX = 0;
        if (this.isStepping) {
            this.cameraBobPhase += delta * 9.5;
            bobY = Math.sin(this.cameraBobPhase) * 0.014;
            bobX = Math.cos(this.cameraBobPhase * 0.5) * 0.010;
        } else {
            // Subtle idle breathing
            bobY = Math.sin(tNow * 0.0025) * 0.003;
        }

        // Viewmodel Action Dynamics: Attack thrust, Hurt recoil, Spellcast surge
        let actZ = 0;
        let actY = 0;
        let actRotZ = 0;
        let actRotX = 0;

        if (this.attackAnimationTime > 0) {
            this.attackAnimationTime -= delta;
            const prog = (0.22 - this.attackAnimationTime) / 0.22;
            const sinPulse = Math.sin(prog * Math.PI);
            actZ += sinPulse * 0.16; // Thrust forward
            actRotZ -= sinPulse * 0.32; // Slash rotation
            actRotX += sinPulse * 0.12;
        } else if (this.hurtAnimationTime > 0) {
            this.hurtAnimationTime -= delta;
            const prog = (0.16 - this.hurtAnimationTime) / 0.16;
            const sinPulse = Math.sin(prog * Math.PI);
            actZ -= sinPulse * 0.10; // Recoil back toward camera
            actRotX -= sinPulse * 0.18;
            actY -= sinPulse * 0.04;
        } else if (this.castAnimationTime > 0) {
            this.castAnimationTime -= delta;
            const prog = (0.32 - this.castAnimationTime) / 0.32;
            const sinPulse = Math.sin(prog * Math.PI);
            actY += sinPulse * 0.09; // Raise hands/wand
            actRotX -= sinPulse * 0.22;
        }

        // Apply to Right Hand
        if (this.rightHandGroup && this.rightRestPos) {
            this.rightHandGroup.position.x = this.rightRestPos.x + bobX;
            this.rightHandGroup.position.y = this.rightRestPos.y + bobY + actY;
            this.rightHandGroup.position.z = this.rightRestPos.z + actZ;
            this.rightHandGroup.rotation.x = this.rightRestRot.x + actRotX;
            this.rightHandGroup.rotation.y = this.rightRestRot.y;
            this.rightHandGroup.rotation.z = this.rightRestRot.z + actRotZ;
        }

        // Apply to Left Hand
        if (this.leftHandGroup && this.leftRestPos) {
            this.leftHandGroup.position.x = this.leftRestPos.x - bobX;
            this.leftHandGroup.position.y = this.leftRestPos.y + bobY + (this.hurtAnimationTime > 0 ? actY : 0);
            this.leftHandGroup.position.z = this.leftRestPos.z + (this.hurtAnimationTime > 0 ? actZ : 0);
            this.leftHandGroup.rotation.x = this.leftRestRot.x + (this.hurtAnimationTime > 0 ? actRotX : 0);
            this.leftHandGroup.rotation.y = this.leftRestRot.y;
            this.leftHandGroup.rotation.z = this.leftRestRot.z;
        }

        // Animated flicker on handheld torch flame
        if (this.torchMesh && this.torchMesh.visible && this.flameMesh && this.innerFlameMesh) {
            const flicker = 0.92 + Math.sin(tNow * 0.018) * 0.08 + (Math.random() - 0.5) * 0.06;
            this.flameMesh.scale.set(flicker, flicker * 1.1, flicker);
            this.innerFlameMesh.scale.set(flicker, flicker * 1.05, flicker);
        }

        // Floating combat texts (damage numbers, crits, misses drifting upward)
        for (let i = this.floatingTexts.length - 1; i >= 0; i--) {
            const ft = this.floatingTexts[i];
            ft.elapsed += delta;
            ft.sprite.position.y = ft.basePos.y + ft.elapsed * 0.85;
            const alpha = Math.max(0, 1.0 - (ft.elapsed / ft.duration));
            ft.material.opacity = alpha;
            if (ft.elapsed >= ft.duration) {
                this.scene.remove(ft.sprite);
                ft.material.dispose();
                if (ft.material.map) ft.material.map.dispose();
                this.floatingTexts.splice(i, 1);
            }
        }

        // Kinetic impact sparks & combat particles
        for (let i = this.particles.length - 1; i >= 0; i--) {
            const p = this.particles[i];
            p.elapsed += delta;
            for (const sp of p.sparks) {
                sp.mesh.position.addScaledVector(sp.vel, delta);
                sp.vel.y -= delta * 6.0; // Gravity
            }
            const alpha = Math.max(0, 1.0 - (p.elapsed / p.duration));
            p.material.opacity = alpha;
            if (p.elapsed >= p.duration) {
                this.scene.remove(p.group);
                p.material.dispose();
                this.particles.splice(i, 1);
            }
        }

        // Distance culling for floating terrain labels (Godot VisibilityRangeEnd: 45m stores, 25m stairs)
        if (this.terrainLabelsGroup && this.terrainLabelsGroup.children) {
            for (let i = 0; i < this.terrainLabelsGroup.children.length; i++) {
                const sprite = this.terrainLabelsGroup.children[i];
                const d = this.camera.position.distanceTo(sprite.position);
                sprite.visible = (d <= (sprite.maxDist || 45.0));
            }
        }

        // Monsters gentle hover, walking lerp & facing
        const monTime = tNow * 0.003;
        for (const entity of this.monsters.values()) {
            if (entity.nameplate) {
                const d = this.camera.position.distanceTo(entity.position);
                entity.nameplate.visible = (d <= 24.0); // Godot VisibilityRangeEnd = 24.0f
            }
            if (entity.targetPos) {
                const dist = entity.position.distanceTo(entity.targetPos);
                if (dist > this.cellSize * 2.2) {
                    // Teleport snap
                    entity.position.copy(entity.targetPos);
                } else if (dist > 0.01) {
                    // Smooth 1-tile walking lerp
                    entity.position.lerp(entity.targetPos, 0.22);
                    // Face movement direction
                    const dx = entity.targetPos.x - entity.position.x;
                    const dz = entity.targetPos.z - entity.position.z;
                    if (dx * dx + dz * dz > 0.001) {
                        entity.rotation.y = Math.atan2(dx, dz);
                    }
                } else {
                    // Idle: Smoothly turn to face the player's camera (matching Godot client MonsterModelResolver.cs)
                    const dx = this.camera.position.x - entity.position.x;
                    const dz = this.camera.position.z - entity.position.z;
                    if (dx * dx + dz * dz > 0.001) {
                        const targetYaw = Math.atan2(dx, dz);
                        const diff = targetYaw - entity.rotation.y;
                        const wrapped = Math.atan2(Math.sin(diff), Math.cos(diff));
                        entity.rotation.y += wrapped * 0.12;
                    }
                }
            }
            // Vertical hover/float
            const baseY = entity.isFloating ? 0.45 : 0.0;
            entity.position.y = baseY + Math.sin(monTime + entity.position.x * 2.0) * (entity.isFloating ? 0.08 : 0.02);

            // Ethereal pulse & ripple rotation for sensed / invisible foggy aura
            if (entity.foggyAura && entity.foggyAura.visible) {
                const pulse = 0.38 + Math.sin(monTime * 2.2 + entity.position.x) * 0.16;
                if (entity.foggyMaterials) {
                    for (let mIdx = 0; mIdx < entity.foggyMaterials.length; mIdx++) {
                        entity.foggyMaterials[mIdx].opacity = pulse;
                    }
                }
                if (entity.groundRipples) {
                    entity.groundRipples.rotation.z += delta * 0.9;
                    const rScale = 1.0 + Math.sin(monTime * 1.8) * 0.12;
                    entity.groundRipples.scale.set(rScale, rScale, 1.0);
                }
            }
        }

        // 3D Items continuous rotation & hover bob
        for (const entity of this.items.values()) {
            if (entity.itemMesh) {
                entity.itemMesh.rotation.y += delta * 1.5;
            }
            entity.position.y = 0.18 + Math.sin(tNow * 0.0035 + entity.position.x) * 0.04;
        }

        this.renderer.render(this.scene, this.camera);
    }
}

window.Dungeon3D = Dungeon3D;
