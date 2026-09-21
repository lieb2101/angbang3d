const WebSocket = require('../server/node_modules/ws');

const ws = new WebSocket('wss://angband3d-cloud-564958309282.us-central1.run.app/ws?user=DescendTest');

let seq = 0;
let lastFrame = null;

function sendKey(key) {
    console.log(`>>> Sending: key ${key}`);
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
    console.log('Connected to server');
});

ws.on('message', async (data) => {
    const msg = JSON.parse(data.toString());
    if (msg.t === 'hello') {
        console.log('Hello:', msg.build || msg.sessionId);
    } else if (msg.t === 'frame') {
        seq = msg.seq;
        lastFrame = msg;
    }
});

async function run() {
    // Wait for connect & initial frame
    while (!lastFrame) await new Promise(r => setTimeout(r, 100));
    console.log(`Initial frame seq=${seq}, phase=${lastFrame.phase}`);

    // Birth
    sendKey('enter');
    await waitForFrame();
    sendKey('@');
    await waitForFrame();
    sendKey('@');
    await waitForFrame();
    sendKey('space');
    await waitForFrame();

    console.log(`Birth complete! Phase=${lastFrame.phase}, Depth=${lastFrame.player ? lastFrame.player.depth : 'no-player'}`);

    // Find staircase in town
    let f = lastFrame;
    console.log(`Town player start at (${f.player.x}, ${f.player.y})`);
    let stairX = 23, stairY = 4;
    for (let y = 0; y < f.map.h; y++) {
        for (let x = 0; x < f.map.w; x++) {
            const featHi = parseInt(f.map.rows[y].f[x*2], 16);
            const featLo = parseInt(f.map.rows[y].f[x*2+1], 16);
            const feat = (featHi << 4) | featLo;
            if (feat === 6) { // FEAT_MORE = 6
                stairX = x;
                stairY = y;
                console.log(`Found down staircase at (${x}, ${y})!`);
                break;
            }
        }
    }

    // Walk directly to (stairX, stairY)
    while (f.player.x !== stairX || f.player.y !== stairY) {
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
            sendKey('enter');
            f = await waitForFrame();
        }
        console.log(`Walked to (${f.player.x}, ${f.player.y})`);
    }

    console.log(`Standing on stairs! Feat: ${f.map.rows[f.player.y].f.substr(f.player.x*2, 2)}. Pressing > to descend...`);
    sendKey('>');
    f = await waitForFrame();
    if (f.ui && f.ui.more) {
        sendKey('enter');
        f = await waitForFrame();
    }
    console.log(`AFTER DESCEND: Depth = ${f.player.depth}, Phase = ${f.phase}, Player at (${f.player.x}, ${f.player.y})`);
    console.log(`Map size: w=${f.map.w}, h=${f.map.h}`);

    // Inspect player light and flags at player location in Depth 1
    const px = f.player.x;
    const py = f.player.y;
    console.log('Player light:', f.player.cur_light, 'light:', f.player.light);
    console.log('Feat at player:', f.map.rows[py].f.substr(px*2, 2), 'Flag at player:', f.map.rows[py].l[px]);
    console.log('Flags around player (row py):', f.map.rows[py].l.substr(Math.max(0, px - 6), 13));
    console.log('Flags row above (py-1):', f.map.rows[py-1].l.substr(Math.max(0, px - 6), 13));
    console.log('Flags row below (py+1):', f.map.rows[py+1].l.substr(Math.max(0, px - 6), 13));

    ws.close();
    process.exit(0);
}

setTimeout(run, 1500);
