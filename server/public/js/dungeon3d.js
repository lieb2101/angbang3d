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
// 1. Procedural Texture Engine (Exact Mathematical Parity with Godot C# DungeonWorld.cs)
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
        this.targetCamPos = new THREE.Vector3(0, this.eyeHeight, 0);
        this.currentCamPos = new THREE.Vector3(0, this.eyeHeight, 0);
        this.cameraBobPhase = 0;

        this.stepDuration = 0.14; // 140ms step tween
        this.stepTime = 0;
        this.isStepping = false;

        this.monsters = new Map();
        this.items = new Map();

        this.initThree();
        this.initTextures();
        this.initMeshes();
        this.initViewmodel();
        this.animate = this.animate.bind(this);
        requestAnimationFrame(this.animate);
    }

    initThree() {
        this.scene = new THREE.Scene();
        this.scene.background = new THREE.Color(0x2d3a50); // Initial town sky
        this.scene.fog = new THREE.FogExp2(0x2d3a50, 0.008);

        this.camera = new THREE.PerspectiveCamera(72, window.innerWidth / window.innerHeight, 0.1, 140);
        this.camera.position.set(0, this.eyeHeight, 0);
        this.camera.rotation.y = 0; // Starts looking North (-Z)

        this.renderer = new THREE.WebGLRenderer({
            canvas: this.canvas,
            antialias: true,
            powerPreference: 'high-performance'
        });
        this.renderer.setSize(window.innerWidth, window.innerHeight);
        this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
        this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
        this.renderer.toneMappingExposure = 1.25;
        this.renderer.outputEncoding = THREE.sRGBEncoding;

        // Ambient light (dynamically adjusted per depth / town)
        this.ambientLight = new THREE.AmbientLight(0xb0c4de, 0.85);
        this.scene.add(this.ambientLight);

        // Directional Sunlight for outdoors (Town) matching Godot _sunLight
        this.sunLight = new THREE.DirectionalLight(0xfffaed, 1.35);
        this.sunLight.position.set(-20, 35, 15);
        this.scene.add(this.sunLight);

        // Player Torch PointLight: warm amber glow with realistic falloff
        this.torchLight = new THREE.PointLight(0xff9e42, 2.8, 22, 1.8);
        this.torchLight.position.set(0, this.eyeHeight + 0.2, 0);
        this.scene.add(this.torchLight);

        // Secondary warm bounce fill light near player feet
        this.torchFillLight = new THREE.PointLight(0xff5511, 0.8, 8, 2.0);
        this.torchFillLight.position.set(0, this.eyeHeight - 0.2, 0);
        this.scene.add(this.torchFillLight);

        window.addEventListener('resize', () => {
            this.camera.aspect = window.innerWidth / window.innerHeight;
            this.camera.updateProjectionMatrix();
            this.renderer.setSize(window.innerWidth, window.innerHeight);
        });
    }

    initTextures() {
        const maxAniso = this.renderer.capabilities.getMaxAnisotropy();

        // 1. Instant Procedural Textures (Exact Godot DungeonWorld Parity)
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
            metalness: 0.04
        });

        this.ceilingMaterial = new THREE.MeshStandardMaterial({
            map: this.ceilingTex,
            roughness: 0.95,
            metalness: 0.0
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
            metalness: 0.10
        });

        // Cooled Basalt (Memory/Fog-of-War Lava)
        this.lavaCooledMaterial = new THREE.MeshStandardMaterial({
            color: 0x141210,
            roughness: 0.95,
            metalness: 0.0
        });

        this.stairsMaterial = new THREE.MeshStandardMaterial({
            map: this.wallTex,
            normalMap: this.wallNormal,
            roughness: 0.82,
            metalness: 0.04
        });

        // 2. Asynchronous 2K PBR Texture Streaming from Cloud Assets
        const loader = new THREE.TextureLoader();
        const upgradeTexture = (url, targetMat, prop, isSRGB, repX = 1, repY = 1) => {
            loader.load(url, (tex) => {
                tex.wrapS = THREE.RepeatWrapping;
                tex.wrapT = THREE.RepeatWrapping;
                tex.repeat.set(repX, repY);
                if (isSRGB) tex.encoding = THREE.sRGBEncoding;
                tex.anisotropy = maxAniso;
                targetMat[prop] = tex;
                targetMat.needsUpdate = true;
            }, undefined, () => {
                // Procedural texture remains active seamlessly!
            });
        };

        upgradeTexture('/assets/textures/T_Brick_BaseColor.png', this.wallMaterial, 'map', true, 1, 1.5);
        upgradeTexture('/assets/textures/T_Brick_Normal.png', this.wallMaterial, 'normalMap', false, 1, 1.5);
        upgradeTexture('/assets/textures/T_UnevenBrick_BaseColor.png', this.floorMaterial, 'map', true, 1, 1);
        upgradeTexture('/assets/textures/T_UnevenBrick_Normal.png', this.floorMaterial, 'normalMap', false, 1, 1);
        upgradeTexture('/assets/textures/T_RockTrim_BaseColor.png', this.ceilingMaterial, 'map', true, 1, 1);
        upgradeTexture('/assets/textures/T_WoodTrim_BaseColor.png', this.doorMaterial, 'map', true, 1, 1.5);
        upgradeTexture('/assets/textures/T_Plaster_BaseColor.png', this.storeMaterial, 'map', true, 1, 1);
    }

    initMeshes() {
        this.maxInstances = 4096;
        const boxGeo = new THREE.BoxGeometry(this.cellSize, this.wallHeight, this.cellSize);
        const floorGeo = new THREE.PlaneGeometry(this.cellSize, this.cellSize);
        floorGeo.rotateX(-Math.PI / 2);

        const ceilingGeo = new THREE.PlaneGeometry(this.cellSize, this.cellSize);
        ceilingGeo.rotateX(Math.PI / 2);

        // Door Panel Geometry matching Godot Door (1.6m wide, 2.7m tall, 0.16m deep)
        const doorGeo = new THREE.BoxGeometry(1.6, 2.7, 0.16);

        this.wallMesh = new THREE.InstancedMesh(boxGeo, this.wallMaterial, this.maxInstances);
        this.wallMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.wallMesh);

        this.floorMesh = new THREE.InstancedMesh(floorGeo, this.floorMaterial, this.maxInstances);
        this.floorMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.floorMesh);

        this.ceilingMesh = new THREE.InstancedMesh(ceilingGeo, this.ceilingMaterial, this.maxInstances);
        this.ceilingMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.ceilingMesh);

        this.doorMesh = new THREE.InstancedMesh(doorGeo, this.doorMaterial, 512);
        this.doorMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.doorMesh);

        // 8 Shop Entrance Meshes
        this.shopMeshes = [];
        for (let i = 0; i < 8; i++) {
            const mesh = new THREE.InstancedMesh(doorGeo, this.shopDoorMaterials[i], 64);
            mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
            this.scene.add(mesh);
            this.shopMeshes.push(mesh);
        }

        this.magmaMesh = new THREE.InstancedMesh(boxGeo, this.magmaMaterial, 256);
        this.magmaMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.magmaMesh);

        this.quartzMesh = new THREE.InstancedMesh(boxGeo, this.quartzMaterial, 256);
        this.quartzMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.quartzMesh);

        this.lavaMesh = new THREE.InstancedMesh(floorGeo, this.lavaMaterial, 512);
        this.lavaMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.lavaMesh);

        this.lavaCooledMesh = new THREE.InstancedMesh(floorGeo, this.lavaCooledMaterial, 512);
        this.lavaCooledMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.lavaCooledMesh);

        this.stairsMesh = new THREE.InstancedMesh(boxGeo, this.stairsMaterial, 128);
        this.stairsMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.stairsMesh);

        // Pre-allocate instance color buffers
        const white = new THREE.Color(1, 1, 1);
        this.wallMesh.setColorAt(0, white);
        this.floorMesh.setColorAt(0, white);
        this.ceilingMesh.setColorAt(0, white);
        this.doorMesh.setColorAt(0, white);
        this.stairsMesh.setColorAt(0, white);
        for (let i = 0; i < 8; i++) this.shopMeshes[i].setColorAt(0, white);

        this.dummy = new THREE.Object3D();
    }

    initViewmodel() {
        this.viewmodelGroup = new THREE.Group();
        this.camera.add(this.viewmodelGroup);
        this.scene.add(this.camera);

        // Right Hand Weapon: Primary melee weapon / bow.
        // Hidden by default when unarmed, matching Godot client!
        this.rightHandGroup = new THREE.Group();
        this.rightHandGroup.position.set(0.25, -0.24, -0.36);
        this.rightHandGroup.rotation.set(0.18, -0.22, 0.15);
        this.rightHandGroup.visible = false;
        this.viewmodelGroup.add(this.rightHandGroup);

        // Left Hand: Shield or wielded torch weapon.
        // Hidden by default when off-hand is empty, matching Godot client!
        this.leftHandGroup = new THREE.Group();
        this.leftHandGroup.position.set(-0.25, -0.24, -0.34);
        this.leftHandGroup.rotation.set(0.12, 0.18, -0.10);
        this.leftHandGroup.visible = false;
        this.viewmodelGroup.add(this.leftHandGroup);

        const objLoader = (typeof THREE.OBJLoader !== 'undefined') ? new THREE.OBJLoader() : null;

        const bladeMat = new THREE.MeshStandardMaterial({ color: 0xd8e4ed, roughness: 0.16, metalness: 0.95 });
        const goldMat = new THREE.MeshStandardMaterial({ color: 0xc99c3a, roughness: 0.30, metalness: 0.88 });
        const gripMat = new THREE.MeshStandardMaterial({ color: 0x3d2618, roughness: 0.88 });

        if (objLoader) {
            // Load 3D Sword for right hand
            objLoader.load('/assets/models/weapons/Sword.obj', (obj) => {
                obj.traverse((child) => {
                    if (child.isMesh) {
                        const n = (child.name || '').toLowerCase();
                        if (n.includes('guard') || n.includes('pommel') || n.includes('gold')) {
                            child.material = goldMat;
                        } else if (n.includes('grip') || n.includes('handle') || n.includes('wood')) {
                            child.material = gripMat;
                        } else {
                            child.material = bladeMat;
                        }
                    }
                });
                obj.scale.set(0.14, 0.14, 0.14);
                obj.position.set(0, -0.02, 0);
                obj.rotation.set(0, Math.PI / 2, 0);
                this.rightHandGroup.add(obj);
                this.swordMesh = obj;
            }, undefined, () => {});

            // Load 3D Shield for left hand
            objLoader.load('/assets/models/weapons/Shield_Round.obj', (obj) => {
                obj.traverse((child) => {
                    if (child.isMesh) {
                        child.material = goldMat;
                    }
                });
                obj.scale.set(0.11, 0.11, 0.11);
                obj.position.set(0, 0, 0);
                this.leftHandGroup.add(obj);
                this.shieldMesh = obj;
                this.shieldMesh.visible = false;
            }, undefined, () => {});
        }

        // Compact handheld torch (ONLY visible if specifically wielding a torch weapon)
        const torchStaffGeo = new THREE.CylinderGeometry(0.015, 0.020, 0.38, 8);
        const torchStaffMat = new THREE.MeshStandardMaterial({ map: this.doorTex, roughness: 0.85 });
        const torchStaff = new THREE.Mesh(torchStaffGeo, torchStaffMat);
        torchStaff.position.y = -0.10;

        const sconceGeo = new THREE.CylinderGeometry(0.030, 0.025, 0.06, 8);
        const sconceMat = new THREE.MeshStandardMaterial({ color: 0x1e1e1e, metalness: 0.85, roughness: 0.35 });
        const sconce = new THREE.Mesh(sconceGeo, sconceMat);
        sconce.position.y = 0.10;

        const flameGeo = new THREE.ConeGeometry(0.035, 0.12, 8);
        const flameMat = new THREE.MeshBasicMaterial({ color: 0xff8811 });
        this.flameMesh = new THREE.Mesh(flameGeo, flameMat);
        this.flameMesh.position.y = 0.18;

        const innerFlameGeo = new THREE.ConeGeometry(0.018, 0.08, 8);
        const innerFlameMat = new THREE.MeshBasicMaterial({ color: 0xffffaa });
        this.innerFlameMesh = new THREE.Mesh(innerFlameGeo, innerFlameMat);
        this.innerFlameMesh.position.y = 0.16;

        this.torchMesh = new THREE.Group();
        this.torchMesh.add(torchStaff);
        this.torchMesh.add(sconce);
        this.torchMesh.add(this.flameMesh);
        this.torchMesh.add(this.innerFlameMesh);
        this.torchMesh.visible = false;
        this.leftHandGroup.add(this.torchMesh);

        this.attackAnimationTime = 0;
    }

    updateViewmodel(player) {
        if (!player) return;

        const weaponItem = player.weapon_item || '';
        const bowItem = player.bow_item || '';
        const shieldItem = player.shield_item || '';

        // LEFT HAND RULE:
        // Carrying a torch/lantern in the equipment light slot illuminates without occupying hands!
        // The left hand only shows an item if wielding a shield or using a torch as a weapon.
        const isWieldingTorch = weaponItem.toLowerCase().includes('torch');
        const hasShield = Boolean(shieldItem);

        if (hasShield && this.shieldMesh) {
            this.leftHandGroup.visible = true;
            this.shieldMesh.visible = true;
            if (this.torchMesh) this.torchMesh.visible = false;
        } else if (isWieldingTorch && this.torchMesh) {
            this.leftHandGroup.visible = true;
            if (this.shieldMesh) this.shieldMesh.visible = false;
            this.torchMesh.visible = true;
        } else {
            this.leftHandGroup.visible = false;
        }

        // RIGHT HAND RULE:
        // Shows primary equipped weapon or bow. If unarmed, hands are clean & empty!
        const hasWeapon = Boolean(weaponItem || bowItem);
        this.rightHandGroup.visible = hasWeapon;
    }

    triggerAttackAnimation() {
        this.attackAnimationTime = 0.22; // 220ms swing
    }

    turn(delta) {
        this.facing = ((this.facing + delta) % 4 + 4) % 4;
        this.targetYaw = -this.facing * (Math.PI / 2);
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
        return feat === 1 || feat === 2 || feat === 3 || feat === 4 || feat === 5 || feat === 6 || feat === 23 || feat === 24 || this.isStoreKind(feat);
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

        // Facing exterior street
        if (bldgN && streetS && bldgW && bldgE) return 0; // South (+Z)
        if (bldgS && streetN && bldgW && bldgE) return Math.PI; // North (-Z)
        if (bldgW && streetE && bldgN && bldgS) return Math.PI / 2; // East (+X)
        if (bldgE && streetW && bldgN && bldgS) return -Math.PI / 2; // West (-X)

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

        const px = frame.player ? frame.player.x : Math.floor(w / 2);
        const py = frame.player ? frame.player.y : Math.floor(h / 2);
        const depth = (frame.player && frame.player.depth !== undefined) ? frame.player.depth : 0;
        const outdoors = depth === 0; // Town is outdoors under daylight

        // Update Viewmodel Equipment rules
        this.updateViewmodel(frame.player);

        // Apply Town vs Dungeon Lighting & Atmosphere
        if (outdoors) {
            this.scene.background.setHex(0x2d3a50);
            this.scene.fog.color.setHex(0x2d3a50);
            this.scene.fog.density = 0.008;
            this.sunLight.visible = true;
            this.sunLight.intensity = 1.35;
            this.ambientLight.color.setHex(0xb0c4de);
            this.ambientLight.intensity = 0.65;
            this.torchLight.intensity = 0.8;
            this.torchFillLight.intensity = 0.3;
        } else {
            this.scene.background.setHex(0x020305);
            this.scene.fog.color.setHex(0x020305);
            this.scene.fog.density = 0.038;
            this.sunLight.visible = false;
            this.sunLight.intensity = 0;
            this.ambientLight.color.setHex(0x1a202c);
            this.ambientLight.intensity = 0.12;
            this.torchLight.intensity = 2.8;
            this.torchFillLight.intensity = 0.8;
        }

        this.targetCamPos.set(px * this.cellSize, this.eyeHeight, py * this.cellSize);

        const dist = this.currentCamPos.distanceTo(this.targetCamPos);
        if (dist > this.cellSize * 1.5) {
            this.currentCamPos.copy(this.targetCamPos);
            this.isStepping = false;
        } else if (dist > 0.01) {
            this.isStepping = true;
            this.stepTime = 0;
        }

        let wallCount = 0;
        let floorCount = 0;
        let ceilingCount = 0;
        let doorCount = 0;
        let lavaCount = 0;
        let lavaCooledCount = 0;
        let stairsCount = 0;
        let magmaCount = 0;
        let quartzCount = 0;
        const shopCounts = [0, 0, 0, 0, 0, 0, 0, 0];

        const sightRange = outdoors ? 45 : 28;
        const minX = Math.max(0, px - sightRange);
        const maxX = Math.min(w - 1, px + sightRange);
        const minY = Math.max(0, py - sightRange);
        const maxY = Math.min(h - 1, py + sightRange);

        const colInView = new THREE.Color(1.0, 1.0, 1.0);
        const colMemory = new THREE.Color(0.28, 0.30, 0.38); // Cool dark slate memory tint

        for (let y = minY; y <= maxY; y++) {
            for (let x = minX; x <= maxX; x++) {
                const feat = this.getFeatAt(map, x, y);
                const flag = this.getFlagAt(map, x, y);

                const known = outdoors || (flag & 0x1) !== 0;
                const inView = outdoors || (flag & 0x2) !== 0;

                // Strict Fog-of-War Invariant: Unexplored tiles remain dark void
                if (!known && !inView) continue;

                if (!outdoors) {
                    const dx = x - px;
                    const dy = y - py;
                    if (dx * dx + dy * dy > 28 * 28) continue;
                }

                const wx = x * this.cellSize;
                const wz = y * this.cellSize;
                const tileCol = inView ? colInView : colMemory;

                // Magma fissures (feat 19, 20)
                if (feat === 19 || feat === 20) {
                    this.dummy.position.set(wx, this.wallHeight / 2, wz);
                    this.dummy.rotation.set(0, 0, 0);
                    this.dummy.updateMatrix();
                    this.magmaMesh.setMatrixAt(magmaCount, this.dummy.matrix);
                    this.magmaMesh.setColorAt(magmaCount, tileCol);
                    magmaCount++;
                    continue;
                }

                // Quartz crystal veins (feat 21, 22)
                if (feat === 21 || feat === 22) {
                    this.dummy.position.set(wx, this.wallHeight / 2, wz);
                    this.dummy.rotation.set(0, 0, 0);
                    this.dummy.updateMatrix();
                    this.quartzMesh.setMatrixAt(quartzCount, this.dummy.matrix);
                    this.quartzMesh.setColorAt(quartzCount, tileCol);
                    quartzCount++;
                    continue;
                }

                // Solid Stone / Granite / Perm wall
                const isWall = feat === 15 || (feat >= 17 && feat <= 22);
                if (isWall) {
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

                    this.dummy.position.set(wx, this.wallHeight / 2, wz);
                    this.dummy.rotation.set(0, 0, 0);
                    this.dummy.updateMatrix();
                    this.wallMesh.setMatrixAt(wallCount, this.dummy.matrix);
                    this.wallMesh.setColorAt(wallCount, tileCol);
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
                    this.floorMesh.setColorAt(floorCount, tileCol);
                    floorCount++;

                    // Align heraldic entrance door to face outward into street
                    const doorYaw = this.determineShopDoorOrientation(map, x, y, w, h);
                    this.dummy.position.set(wx, this.wallHeight / 2, wz);
                    this.dummy.rotation.set(0, doorYaw, 0);
                    this.dummy.updateMatrix();
                    this.shopMeshes[shopIdx].setMatrixAt(shopCounts[shopIdx], this.dummy.matrix);
                    this.shopMeshes[shopIdx].setColorAt(shopCounts[shopIdx], tileCol);
                    shopCounts[shopIdx]++;

                    if (!outdoors) {
                        this.dummy.position.set(wx, this.wallHeight, wz);
                        this.dummy.rotation.set(0, 0, 0);
                        this.dummy.updateMatrix();
                        this.ceilingMesh.setMatrixAt(ceilingCount, this.dummy.matrix);
                        this.ceilingMesh.setColorAt(ceilingCount, tileCol);
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
                    this.floorMesh.setColorAt(floorCount, tileCol);
                    floorCount++;

                    const doorYaw = this.determineDoorOrientation(map, x, y, w, h);
                    this.dummy.position.set(wx, this.wallHeight / 2, wz);
                    this.dummy.rotation.set(0, doorYaw, 0);
                    this.dummy.updateMatrix();
                    this.doorMesh.setMatrixAt(doorCount, this.dummy.matrix);
                    this.doorMesh.setColorAt(doorCount, tileCol);
                    doorCount++;

                    if (!outdoors) {
                        this.dummy.position.set(wx, this.wallHeight, wz);
                        this.dummy.rotation.set(0, 0, 0);
                        this.dummy.updateMatrix();
                        this.ceilingMesh.setMatrixAt(ceilingCount, this.dummy.matrix);
                        this.ceilingMesh.setColorAt(ceilingCount, tileCol);
                        ceilingCount++;
                    }
                    continue;
                }

                // Floor / Walkable corridor (feat 1, 3, 4, 24)
                if (feat === 1 || feat === 3 || feat === 4 || feat === 24) {
                    this.dummy.position.set(wx, -0.05, wz);
                    this.dummy.rotation.set(0, 0, 0);
                    this.dummy.updateMatrix();
                    this.floorMesh.setMatrixAt(floorCount, this.dummy.matrix);
                    this.floorMesh.setColorAt(floorCount, tileCol);
                    floorCount++;

                    if (!outdoors) {
                        this.dummy.position.set(wx, this.wallHeight, wz);
                        this.dummy.rotation.set(0, 0, 0);
                        this.dummy.updateMatrix();
                        this.ceilingMesh.setMatrixAt(ceilingCount, this.dummy.matrix);
                        this.ceilingMesh.setColorAt(ceilingCount, tileCol);
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
                    continue;
                }

                // Stairs Up/Down (feat 5, 6)
                if (feat === 5 || feat === 6) {
                    this.dummy.position.set(wx, -0.05, wz);
                    this.dummy.rotation.set(0, 0, 0);
                    this.dummy.updateMatrix();
                    this.floorMesh.setMatrixAt(floorCount, this.dummy.matrix);
                    this.floorMesh.setColorAt(floorCount, tileCol);
                    floorCount++;

                    this.dummy.position.set(wx, 0.4, wz);
                    this.dummy.updateMatrix();
                    this.stairsMesh.setMatrixAt(stairsCount, this.dummy.matrix);
                    this.stairsMesh.setColorAt(stairsCount, tileCol);
                    stairsCount++;
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

        this.doorMesh.count = doorCount;
        this.doorMesh.instanceMatrix.needsUpdate = true;
        if (this.doorMesh.instanceColor) this.doorMesh.instanceColor.needsUpdate = true;

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

        this.updateMonsters(frame.monsters || []);
        this.updateItems(frame.objects || frame.items || []);
    }

    createMonsterToken(m) {
        const canvas = document.createElement('canvas');
        canvas.width = 512;
        canvas.height = 512;
        const ctx = canvas.getContext('2d');

        const cx = 256;
        const cy = 230;
        const r = 160;

        // Outer ambient shadow
        const glow = ctx.createRadialGradient(cx, cy, r * 0.8, cx, cy, r * 1.3);
        glow.addColorStop(0, 'rgba(0, 0, 0, 0.6)');
        glow.addColorStop(1, 'rgba(0, 0, 0, 0)');
        ctx.fillStyle = glow;
        ctx.beginPath();
        ctx.arc(cx, cy, r * 1.3, 0, Math.PI * 2);
        ctx.fill();

        // Metallic Rim
        const isUnique = (m.name || '').includes('the ') || (m.glyph && m.glyph === m.glyph.toUpperCase());
        const rimGrad = ctx.createLinearGradient(cx - r, cy - r, cx + r, cy + r);
        if (isUnique) {
            rimGrad.addColorStop(0, '#ffe57f');
            rimGrad.addColorStop(0.5, '#b8860b');
            rimGrad.addColorStop(1, '#ffd700');
        } else {
            rimGrad.addColorStop(0, '#9eabb8');
            rimGrad.addColorStop(0.5, '#3a424e');
            rimGrad.addColorStop(1, '#7a8594');
        }

        ctx.fillStyle = rimGrad;
        ctx.beginPath();
        ctx.arc(cx, cy, r, 0, Math.PI * 2);
        ctx.fill();

        // Inner Stone Medallion
        const innerGrad = ctx.createRadialGradient(cx, cy, 10, cx, cy, r - 14);
        innerGrad.addColorStop(0, '#222834');
        innerGrad.addColorStop(0.7, '#131720');
        innerGrad.addColorStop(1, '#0b0d13');
        ctx.fillStyle = innerGrad;
        ctx.beginPath();
        ctx.arc(cx, cy, r - 14, 0, Math.PI * 2);
        ctx.fill();

        // Inner engraved circle
        ctx.strokeStyle = isUnique ? 'rgba(255, 215, 0, 0.45)' : 'rgba(160, 180, 200, 0.28)';
        ctx.lineWidth = 3;
        ctx.beginPath();
        ctx.arc(cx, cy, r - 24, 0, Math.PI * 2);
        ctx.stroke();

        // Creature Glyph in glowing Cinzel/Roguelike font
        ctx.font = '900 160px "Cinzel", "Fira Code", monospace';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.shadowColor = m.color || '#ff4444';
        ctx.shadowBlur = 24;
        ctx.fillStyle = m.color || '#ffffff';
        ctx.fillText(m.glyph || 'M', cx, cy - 4);
        ctx.shadowBlur = 0;

        // Health Bar overhead
        const barW = 240;
        const barH = 22;
        const barX = cx - barW / 2;
        const barY = 32;

        ctx.fillStyle = 'rgba(0, 0, 0, 0.85)';
        ctx.strokeStyle = '#333e4c';
        ctx.lineWidth = 3;
        ctx.beginPath();
        if (ctx.roundRect) ctx.roundRect(barX, barY, barW, barH, 6);
        else ctx.rect(barX, barY, barW, barH);
        ctx.fill();
        ctx.stroke();

        const hp = m.hp !== undefined ? m.hp : 100;
        const maxHp = m.maxhp !== undefined ? m.maxhp : 100;
        const pct = Math.max(0, Math.min(1, maxHp > 0 ? hp / maxHp : 1));
        const hpFillW = Math.max(4, (barW - 4) * pct);

        let hpColor = '#38e078';
        if (pct < 0.3) hpColor = '#ff3b30';
        else if (pct < 0.6) hpColor = '#ffcc00';

        ctx.fillStyle = hpColor;
        ctx.beginPath();
        if (ctx.roundRect) ctx.roundRect(barX + 2, barY + 2, hpFillW, barH - 4, 4);
        else ctx.rect(barX + 2, barY + 2, hpFillW, barH - 4);
        ctx.fill();

        // Name Banner at the bottom
        if (m.name) {
            const name = m.name.length > 20 ? m.name.substring(0, 19) + '…' : m.name;
            ctx.font = '700 32px "Cinzel", serif';
            const textMetrics = ctx.measureText(name);
            const bannerW = Math.min(460, textMetrics.width + 44);
            const bannerH = 44;
            const bannerX = cx - bannerW / 2;
            const bannerY = cy + r - 10;

            ctx.fillStyle = 'rgba(10, 12, 18, 0.92)';
            ctx.strokeStyle = isUnique ? '#d4af37' : '#556272';
            ctx.lineWidth = 2;
            ctx.beginPath();
            if (ctx.roundRect) ctx.roundRect(bannerX, bannerY, bannerW, bannerH, 8);
            else ctx.rect(bannerX, bannerY, bannerW, bannerH);
            ctx.fill();
            ctx.stroke();

            ctx.fillStyle = isUnique ? '#ffd700' : '#e6edf5';
            ctx.fillText(name, cx, bannerY + bannerH / 2);
        }

        const tex = new THREE.CanvasTexture(canvas);
        tex.minFilter = THREE.LinearFilter;
        return tex;
    }

    updateMonsters(monsters) {
        const activeIds = new Set();

        monsters.forEach(m => {
            const id = m.id || `${m.x}_${m.y}_${m.glyph}`;
            activeIds.add(id);
            let sprite = this.monsters.get(id);

            if (!sprite) {
                const tex = this.createMonsterToken(m);
                const mat = new THREE.SpriteMaterial({ map: tex, depthWrite: false });
                sprite = new THREE.Sprite(mat);
                sprite.scale.set(1.5, 1.5, 1.5);
                sprite.position.set(m.x * this.cellSize, 0.9, m.y * this.cellSize);
                sprite.targetPos = new THREE.Vector3(m.x * this.cellSize, 0.9, m.y * this.cellSize);
                this.scene.add(sprite);
                this.monsters.set(id, sprite);
            } else {
                sprite.targetPos = new THREE.Vector3(m.x * this.cellSize, 0.9, m.y * this.cellSize);
            }
        });

        for (const [id, sprite] of this.monsters.entries()) {
            if (!activeIds.has(id)) {
                this.scene.remove(sprite);
                this.monsters.delete(id);
            }
        }
    }

    createItemMesh(it) {
        const g = it.glyph || '?';
        const group = new THREE.Group();

        if (g === '$') {
            // Gold Ingot Stacks
            const goldMat = new THREE.MeshStandardMaterial({
                color: 0xffd700,
                emissive: 0x553300,
                metalness: 0.95,
                roughness: 0.18
            });
            for (let i = 0; i < 3; i++) {
                const bar = new THREE.Mesh(new THREE.BoxGeometry(0.24, 0.08, 0.12), goldMat);
                bar.position.set((i - 1) * 0.04, i * 0.08, 0);
                group.add(bar);
            }
        } else if (g === '!') {
            // Potion flask with glowing elixir
            const glassMat = new THREE.MeshStandardMaterial({
                color: 0xff3355,
                emissive: 0xbb1133,
                emissiveIntensity: 1.2,
                roughness: 0.12,
                metalness: 0.20
            });
            const body = new THREE.Mesh(new THREE.CylinderGeometry(0.08, 0.12, 0.24, 12), glassMat);
            const neck = new THREE.Mesh(new THREE.CylinderGeometry(0.04, 0.04, 0.08, 8), glassMat);
            neck.position.y = 0.15;
            group.add(body);
            group.add(neck);
        } else if (g === '?') {
            // Scroll with seal
            const scrollMat = new THREE.MeshStandardMaterial({ color: 0xeee0b0, roughness: 0.82 });
            const roll = new THREE.Mesh(new THREE.CylinderGeometry(0.05, 0.05, 0.32, 10), scrollMat);
            roll.rotation.z = Math.PI / 4;
            group.add(roll);
        } else if (g === '=' || g === '"') {
            // Gold Ring / Amulet
            const ringMat = new THREE.MeshStandardMaterial({
                color: 0xffc400,
                emissive: 0x664400,
                metalness: 0.95,
                roughness: 0.15
            });
            const ring = new THREE.Mesh(new THREE.TorusGeometry(0.12, 0.035, 8, 16), ringMat);
            group.add(ring);
        } else {
            // Weapons, Armor, Relics
            const gemMat = new THREE.MeshStandardMaterial({
                color: 0x44bbff,
                emissive: 0x114488,
                metalness: 0.85,
                roughness: 0.25
            });
            const gem = new THREE.Mesh(new THREE.OctahedronGeometry(0.20, 0), gemMat);
            group.add(gem);
        }

        return group;
    }

    updateItems(items) {
        const activeIds = new Set();

        items.forEach(it => {
            const id = `${it.x}_${it.y}_${it.name || it.glyph}`;
            activeIds.add(id);
            let mesh = this.items.get(id);

            if (!mesh) {
                mesh = this.createItemMesh(it);
                this.scene.add(mesh);
                this.items.set(id, mesh);
            }

            mesh.position.set(it.x * this.cellSize, 0.35, it.y * this.cellSize);
        });

        for (const [id, mesh] of this.items.entries()) {
            if (!activeIds.has(id)) {
                this.scene.remove(mesh);
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
            }
        } else {
            this.camera.position.copy(this.targetCamPos);
            // Idle breathing bob
            this.camera.position.y = this.eyeHeight + Math.sin(tNow * 0.002) * 0.012;
        }

        // Multi-frequency organic torchlight flicker
        const flicker = 2.8 +
            Math.sin(tNow * 0.011) * 0.22 +
            Math.cos(tNow * 0.024) * 0.15 +
            Math.sin(tNow * 0.037) * 0.08;
        this.torchLight.intensity = flicker;

        if (this.flameMesh) {
            this.flameMesh.scale.set(
                1.0 + Math.sin(tNow * 0.015) * 0.14,
                1.0 + Math.cos(tNow * 0.022) * 0.22,
                1.0 + Math.sin(tNow * 0.015) * 0.14
            );
        }

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

        // Attack animation on right hand
        if (this.attackAnimationTime > 0) {
            this.attackAnimationTime -= delta;
            this.rightHandGroup.position.z = -0.36 - Math.sin((0.22 - this.attackAnimationTime) * Math.PI * 4.5) * 0.18;
            this.rightHandGroup.rotation.z = 0.15 - Math.sin((0.22 - this.attackAnimationTime) * Math.PI * 4.5) * 0.35;
        } else {
            this.rightHandGroup.position.z = -0.36;
            this.rightHandGroup.rotation.z = 0.15;
        }

        // Monsters gentle hover & movement interpolation
        const monTime = tNow * 0.003;
        for (const sprite of this.monsters.values()) {
            if (sprite.targetPos) {
                sprite.position.lerp(sprite.targetPos, 0.18);
            }
            sprite.position.y = 0.9 + Math.sin(monTime + sprite.position.x) * 0.06;
        }

        // 3D Items hover & continuous rotation
        for (const mesh of this.items.values()) {
            mesh.rotation.y += delta * 1.5;
            mesh.position.y = 0.35 + Math.sin(tNow * 0.003 + mesh.position.x) * 0.06;
        }

        this.renderer.render(this.scene, this.camera);
    }
}

window.Dungeon3D = Dungeon3D;
