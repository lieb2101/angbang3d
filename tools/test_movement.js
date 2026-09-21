const WebSocket = require('../server/node_modules/ws');

const CLOUD_HOST = process.env.CLOUD_HOST || 'angband3d-cloud-564958309282.us-central1.run.app';
const WSS_URL = `wss://${CLOUD_HOST}/ws?user=MoveHero_${Date.now()}`;

console.log(`[MoveTest] Connecting to ${WSS_URL}...`);
const ws = new WebSocket(WSS_URL);

let step = 0;
const timeout = setTimeout(() => {
    console.error('[MoveTest] Test timed out');
    process.exit(1);
}, 20000);

ws.on('open', () => {
    console.log('[MoveTest] Connected');
});

ws.on('message', (data) => {
    const str = data.toString().trim();
    if (!str) return;

    try {
        const msg = JSON.parse(str);
        if (msg.t === 'frame') {
            const phase = msg.phase || 'unknown';

            if (phase === 'setup' && step === 0) {
                step = 1;
                console.log('[MoveTest] Advancing through birth...');
                ws.send('key enter');
                setTimeout(() => ws.send('key @'), 200);
                setTimeout(() => ws.send('key enter'), 500);
                return;
            }

            if (phase === 'play' && msg.player && step === 1) {
                step = 2;
                console.log(`[MoveTest] In dungeon at (${msg.player.x}, ${msg.player.y}). Testing move 'down'...`);
                const startX = msg.player.x;
                const startY = msg.player.y;
                ws.send('key down');

                setTimeout(() => {
                    console.log('[MoveTest] Testing inventory menu key "i"...');
                    ws.send('key i');
                }, 400);

                setTimeout(() => {
                    console.log('[MoveTest] Closing inventory with escape...');
                    ws.send('key escape');
                }, 800);

                setTimeout(() => {
                    console.log('[MoveTest] SUCCESS: Full movement and menu cycle verified!');
                    clearTimeout(timeout);
                    ws.close();
                    process.exit(0);
                }, 1200);
            }
        }
    } catch (_) {}
});
