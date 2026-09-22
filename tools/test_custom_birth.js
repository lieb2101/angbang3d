const WebSocket = require('../server/node_modules/ws');

const ws = new WebSocket('wss://angband3d-cloud-564958309282.us-east1.run.app/ws?user=CustomHero_' + Date.now());

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
    console.log('[CustomHeroTest] Connected to Cloud Run');
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
    console.log(`[CustomHeroTest] Connected, phase: ${lastFrame.phase}`);

    // Advance past title screen
    sendKey('enter');
    let f = await waitForFrame();

    // Step 1: Race selection -> Human ('a')
    console.log('[CustomHeroTest] Step 1: Selecting Race: Human (a)...');
    sendKey('a');
    f = await waitForFrame();

    // Step 2: Class selection -> Warrior ('a')
    console.log('[CustomHeroTest] Step 2: Selecting Class: Warrior (a)...');
    sendKey('a');
    f = await waitForFrame();

    // Step 3: Roller selection -> Point-based (Enter)
    console.log('[CustomHeroTest] Step 3: Selecting Roller: Point-based (Enter)...');
    sendKey('enter');
    f = await waitForFrame();

    // Step 4: Accept stats (Enter)
    console.log('[CustomHeroTest] Step 4: Accepting Stats (Enter)...');
    sendKey('enter');
    f = await waitForFrame();

    // Step 5: Keep name (Enter)
    console.log('[CustomHeroTest] Step 5: Confirming Name (Enter)...');
    sendKey('enter');
    f = await waitForFrame();

    // Step 6: Biography advance (space)
    console.log('[CustomHeroTest] Step 6: Advancing biography (space)...');
    sendKey('space');
    f = await waitForFrame();

    // Step 7: Final confirmation to enter 3D play (enter)
    console.log('[CustomHeroTest] Step 7: Final Review Confirmation (enter)...');
    sendKey('enter');
    f = await waitForFrame();

    if (f.ui && f.ui.more) {
        sendKey('space');
        f = await waitForFrame();
    }

    console.log(`[CustomHeroTest] Final state: Phase=${f.phase}, Depth=${f.player ? f.player.depth : 'none'}, Race=${f.player ? f.player.race : 'none'}, Class=${f.player ? f.player.class : 'none'}, HP=${f.player ? f.player.hp : 'none'}`);

    if (f.phase === 'play' && f.player && f.player.depth === 0) {
        console.log(`[CustomHeroTest] SUCCESS: Custom Hero (${f.player.race} ${f.player.class}) successfully created and entered 3D Town!`);
    } else {
        console.error('[CustomHeroTest] FAILED: Did not reach play phase in town!');
        process.exit(1);
    }

    ws.close();
    process.exit(0);
}

setTimeout(run, 1000);
