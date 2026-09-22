const WebSocket = require('../server/node_modules/ws');

const ws = new WebSocket('wss://angband3d-cloud-564958309282.us-east1.run.app/ws?user=StairTest_' + Date.now());

let seq = 0;
let lastFrame = null;

function sendKey(key) {
    ws.send(`key ${key}`);
}

function waitForFrame(timeout = 4000) {
    const startSeq = seq;
    return new Promise((resolve) => {
        const timer = setTimeout(() => resolve(lastFrame), timeout);
        const check = () => {
            if (seq > startSeq) {
                clearTimeout(timer);
                resolve(lastFrame);
            } else {
                setTimeout(check, 40);
            }
        };
        check();
    });
}

ws.on('open', () => {
    console.log('[StairTest] Connected');
});

ws.on('message', (data) => {
    const msg = JSON.parse(data.toString());
    if (msg.t === 'frame') {
        seq = msg.seq;
        lastFrame = msg;
    }
});

async function run() {
    while (!lastFrame) await new Promise(r => setTimeout(r, 100));
    console.log(`[StairTest] Connected, phase: ${lastFrame.phase}`);

    // Quick birth
    sendKey('enter');
    await waitForFrame();
    sendKey('@');
    await waitForFrame();
    sendKey('@');
    await waitForFrame();
    sendKey('space');
    await waitForFrame();

    console.log(`[StairTest] In play: Depth=${lastFrame.player ? lastFrame.player.depth : 'none'}`);

    let f = lastFrame;
    let stairX = 23, stairY = 4;
    for (let y = 0; y < f.map.h; y++) {
        for (let x = 0; x < f.map.w; x++) {
            const featHi = parseInt(f.map.rows[y].f[x*2], 16);
            const featLo = parseInt(f.map.rows[y].f[x*2+1], 16);
            const feat = (featHi << 4) | featLo;
            if (feat === 6) {
                stairX = x;
                stairY = y;
                break;
            }
        }
    }
    console.log(`[StairTest] Staircase located at (${stairX}, ${stairY})`);

    // Walk to staircase
    let steps = 0;
    while ((f.player.x !== stairX || f.player.y !== stairY) && steps++ < 50) {
        let dx = stairX - f.player.x;
        let dy = stairY - f.player.y;
        let key = '';
        if (dx > 0) key = 'right';
        else if (dx < 0) key = 'left';
        else if (dy > 0) key = 'down';
        else if (dy < 0) key = 'up';

        sendKey(key);
        f = await waitForFrame();
        if (f.ui && f.ui.more) {
            sendKey('space');
            f = await waitForFrame();
        }
    }
    console.log(`[StairTest] Standing on stairs at (${f.player.x}, ${f.player.y})`);

    // Descend
    console.log('[StairTest] Descending with ">"...');
    sendKey('>');
    f = await waitForFrame();

    console.log(`[StairTest] Arrived at Depth=${f.player.depth}, pos=(${f.player.x}, ${f.player.y}), more=${f.ui && f.ui.more}`);

    // If ui.more is active on stairs (level feeling), flush with space
    if (f.ui && f.ui.more) {
        console.log('[StairTest] ui.more is active on stairs! Flushing with space...');
        sendKey('space');
        f = await waitForFrame();
    }

    const startX = f.player.x;
    const startY = f.player.y;
    console.log(`[StairTest] Starting position on DL ${f.player.depth}: (${startX}, ${startY})`);

    // Try moving in all 4 cardinal directions until player moves to an adjacent walkable tile
    const testDirs = ['8', '2', '4', '6'];
    let moved = false;
    for (const dir of testDirs) {
        sendKey(dir);
        f = await waitForFrame();
        if (f.ui && f.ui.more) {
            sendKey('space');
            f = await waitForFrame();
        }
        if (f.player.x !== startX || f.player.y !== startY) {
            console.log(`[StairTest] SUCCESS: Player moved off stairs to (${f.player.x}, ${f.player.y}) via dir ${dir}!`);
            moved = true;
            break;
        }
    }

    if (!moved) {
        console.error('[StairTest] FAILED: Player remained stuck on stairs!');
        process.exit(1);
    }

    console.log('[StairTest] ALL STAIR DESCENT CHECKS PASSED!');
    ws.close();
    process.exit(0);
}

setTimeout(run, 1000);
