const WebSocket = require('../server/node_modules/ws');

// Use a unique user so it starts freshly in town (Depth 0)
const testUser = 'WalkStair_' + Date.now();
const ws = new WebSocket(`wss://angband3d-cloud-564958309282.us-central1.run.app/ws?user=${testUser}`);

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
                setTimeout(check, 30);
            }
        };
        check();
    });
}

ws.on('open', async () => {
    console.log('Connected as', testUser);
});

ws.on('message', async (data) => {
    const msg = JSON.parse(data.toString());
    if (msg.t === 'frame') {
        seq = msg.seq;
        lastFrame = msg;
    }
});

async function run() {
    while (!lastFrame) await new Promise(r => setTimeout(r, 100));

    // Birth
    sendKey('enter');
    await waitForFrame();
    sendKey('@');
    await waitForFrame();
    sendKey('@');
    await waitForFrame();
    sendKey('space');
    await waitForFrame();

    console.log(`Birth complete! Phase=${lastFrame.phase}, Depth=${lastFrame.player.depth}, Pos=(${lastFrame.player.x}, ${lastFrame.player.y})`);

    // Find '>' staircase in map
    let stairX = -1, stairY = -1;
    const rows = lastFrame.map.rows;
    for (let y = 0; y < rows.length; y++) {
        const x = rows[y].g.indexOf('>');
        if (x !== -1) {
            stairX = x;
            stairY = y;
            break;
        }
    }
    console.log(`Down staircase found at (${stairX}, ${stairY})`);

    // Walk towards staircase
    while (lastFrame.player.x !== stairX || lastFrame.player.y !== stairY) {
        const px = lastFrame.player.x;
        const py = lastFrame.player.y;
        let moveKey = null;

        if (stairY < py) moveKey = '8';      // North
        else if (stairY > py) moveKey = '2'; // South
        else if (stairX < px) moveKey = '4'; // West
        else if (stairX > px) moveKey = '6'; // East

        sendKey(moveKey);
        await waitForFrame();
        console.log(`Step towards stair: now at (${lastFrame.player.x}, ${lastFrame.player.y})`);
    }

    console.log('Standing on staircase! Descending with >...');
    sendKey('>');
    let f = await waitForFrame();
    if (f.ui && f.ui.more) {
        sendKey('enter');
        f = await waitForFrame();
    }

    console.log(`Descent complete! New Depth = ${f.player ? f.player.depth : 'null'}`);
    console.log('Dungeon level feeling / message:', (f.messages || []).map(m => m.text).slice(-2));

    ws.close();
    process.exit(0);
}

setTimeout(run, 1500);
