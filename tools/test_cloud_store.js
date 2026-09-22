const WebSocket = require('../server/node_modules/ws');

const CLOUD_HOST = process.env.CLOUD_HOST || 'angband3d-cloud-564958309282.us-central1.run.app';
const ws = new WebSocket(`wss://${CLOUD_HOST}/ws?user=StoreTest_` + Math.random().toString(36).substring(2, 6));

let seq = 0;
let lastFrame = null;

function sendKey(key) {
    ws.send(`key ${key}`);
}

function waitForFrame(timeout = 3000) {
    const startSeq = seq;
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => resolve(lastFrame), timeout);
        const check = () => {
            if (seq > startSeq) {
                clearTimeout(timer);
                resolve(lastFrame);
            } else {
                setTimeout(check, 50);
            }
        };
        check();
    });
}

ws.on('open', async () => {
    console.log('[Test] Connected to us-east1 WebSocket');
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
    console.log(`[Test] Connected, phase: ${lastFrame.phase}`);

    // Quick birth
    sendKey('enter');
    await waitForFrame();
    sendKey('@');
    await waitForFrame();
    sendKey('@');
    await waitForFrame();
    sendKey('space');
    await waitForFrame();

    console.log(`[Test] In game! Depth: ${lastFrame.player ? lastFrame.player.depth : 'none'}`);

    // Path to nearest store
    const m = lastFrame.map;
    const stores = [];
    for (let y = 0; y < m.h; y++) {
        for (let x = 0; x < m.w; x++) {
            const feat = parseInt(m.rows[y].f.substring(x * 2, x * 2 + 2), 16);
            if (feat >= 7 && feat <= 14) stores.push({ x, y, feat });
        }
    }

    const px = lastFrame.player.x;
    const py = lastFrame.player.y;
    stores.sort((a, b) => (Math.abs(a.x - px) + Math.abs(a.y - py)) - (Math.abs(b.x - px) + Math.abs(b.y - py)));
    const target = stores[0];
    console.log(`[Test] Nearest store at (${target.x}, ${target.y}), feat ${target.feat}`);

    // BFS path
    const queue = [{ x: px, y: py, path: [] }];
    const visited = new Set([`${px},${py}`]);
    let bestPath = [];
    while (queue.length > 0) {
        const cur = queue.shift();
        if (cur.x === target.x && cur.y === target.y) {
            bestPath = cur.path;
            break;
        }
        for (const [dx, dy, k] of [[0, -1, 'up'], [0, 1, 'down'], [-1, 0, 'left'], [1, 0, 'right']]) {
            const nx = cur.x + dx;
            const ny = cur.y + dy;
            const key = `${nx},${ny}`;
            if (nx >= 0 && nx < m.w && ny >= 0 && ny < m.h && !visited.has(key)) {
                const nfeat = parseInt(m.rows[ny].f.substring(nx * 2, nx * 2 + 2), 16);
                if ((nfeat >= 1 && nfeat <= 6) || (nx === target.x && ny === target.y)) {
                    visited.add(key);
                    queue.push({ x: nx, y: ny, path: [...cur.path, k] });
                }
            }
        }
    }

    console.log(`[Test] Walking ${bestPath.length} steps to store...`);
    for (const step of bestPath) {
        sendKey(step);
        await waitForFrame();
        if (lastFrame.ui && lastFrame.ui.overlay > 0) {
            console.log(`[Test] SUCCESS: Stepped into store! Overlay: ${lastFrame.ui.overlay}`);
            const screen = (lastFrame.term && lastFrame.term.rows) ? lastFrame.term.rows.map(r => r.g || '').join('\n') : '';
            console.log(`[Test] Store Screen Content:\n${screen.substring(0, 300)}...`);
            ws.close();
            process.exit(0);
            return;
        }
    }

    console.error('[Test] Finished path without overlay triggering!');
    ws.close();
    process.exit(1);
}

run().catch(err => {
    console.error(err);
    process.exit(1);
});
