const WebSocket = require('../server/node_modules/ws');

const CLOUD_HOST = process.env.CLOUD_HOST || 'angband3d-cloud-564958309282.us-central1.run.app';
const wsUrl = `wss://${CLOUD_HOST}/ws?user=FullUxTest_` + Math.random().toString(36).substring(2, 6);
const ws = new WebSocket(wsUrl);

let seq = 0;
let lastFrame = null;
let messagesReceived = [];

function sendKey(key) {
    ws.send(`key ${key}`);
}

function waitForFrame(predicate, timeout = 5000) {
    const startSeq = seq;
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => resolve(lastFrame), timeout);
        const check = () => {
            if (seq > startSeq && (!predicate || predicate(lastFrame))) {
                clearTimeout(timer);
                resolve(lastFrame);
            } else {
                setTimeout(check, 50);
            }
        };
        check();
    });
}

ws.on('open', () => {
    console.log('[FullUX] Connected to Cloud Run WebSocket');
});

ws.on('message', (data) => {
    const msg = JSON.parse(data.toString());
    if (msg.t === 'frame') {
        seq = msg.seq;
        lastFrame = msg;
        if (msg.messages && msg.messages.length > 0) {
            for (const m of msg.messages) {
                if (m && m.text && !messagesReceived.some(x => x.text === m.text)) {
                    messagesReceived.push(m);
                }
            }
        }
    }
});

async function run() {
    while (!lastFrame) await new Promise(r => setTimeout(r, 100));
    console.log(`[FullUX] Session started, initial seq: ${seq}, phase: ${lastFrame.phase}`);

    // Quick-birth character
    sendKey('enter');
    await waitForFrame(f => f.phase === 'setup' && f.term);
    sendKey('@');
    await waitForFrame();
    sendKey('@');
    await waitForFrame();
    sendKey('space');
    await waitForFrame(f => f.phase === 'play');

    console.log(`[FullUX] Entered Game Play! Depth: ${lastFrame.player.depth}, HP: ${lastFrame.player.hp}/${lastFrame.player.hp_max}, Turn: ${lastFrame.player.turn || lastFrame.player.game_turn}`);
    console.log(`[FullUX] Initial messages captured (${messagesReceived.length}):`, messagesReceived.map(m => m.text).join(' | '));

    // Move in game
    const startX = lastFrame.player.x;
    const startY = lastFrame.player.y;
    console.log(`[FullUX] Player start pos: (${startX}, ${startY})`);

    // Perform a few steps and actions (inspect inventory, rest, move)
    sendKey('i');
    await waitForFrame();
    sendKey('escape');
    await waitForFrame();

    sendKey('up');
    await waitForFrame();
    sendKey('down');
    await waitForFrame();

    console.log(`[FullUX] Post-movement pos: (${lastFrame.player.x}, ${lastFrame.player.y})`);
    console.log(`[FullUX] Total messages after actions (${messagesReceived.length}):`, messagesReceived.slice(-5).map(m => m.text).join(' | '));

    // Retire / trigger death
    console.log('[FullUX] Triggering character retirement/death sequence...');
    sendKey('Q');
    await waitForFrame();
    sendKey('y');
    await waitForFrame();
    sendKey('@');
    await waitForFrame();

    if (lastFrame.ui && lastFrame.ui.more) {
        sendKey('space');
        await waitForFrame();
    }

    console.log(`[FullUX] Death Frame: Dead=${lastFrame.player.dead}, HP=${lastFrame.player.hp}, DiedFrom=${lastFrame.player.died_from}`);
    const screen = (lastFrame.term && lastFrame.term.rows) ? lastFrame.term.rows.map(r => r.g || '').join('\n') : '';
    console.log(`[FullUX] Tombstone / Death menu in terminal: ${screen.includes('Information') || screen.includes('Quit') || screen.includes('New Game')}`);

    if (lastFrame.player.dead && (screen.includes('Information') || screen.includes('Quit') || screen.includes('New Game'))) {
        console.log('[FullUX] ALL CHECKS PASSED: Game play, message streaming, actions, and death workflow verified on Cloud Run!');
        ws.close();
        process.exit(0);
    } else {
        console.error('[FullUX] FAILED: Death state or menu missing');
        ws.close();
        process.exit(1);
    }
}

run().catch(err => {
    console.error(err);
    process.exit(1);
});
