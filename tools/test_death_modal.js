const { spawn } = require('child_process');
const path = require('path');
const fs = require('fs');
const WebSocket = require('../server/node_modules/ws');

async function testDeath() {
    const port = 9224;
    const chrome = spawn('C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe', [
        '--headless=new',
        '--remote-debugging-port=' + port,
        '--window-size=1280,800',
        '--no-first-run',
        '--no-default-browser-check',
        '--user-data-dir=' + path.join(__dirname, 'temp_death_test_' + Date.now())
    ], { stdio: 'ignore' });

    await new Promise(r => setTimeout(r, 1500));
    const targetUrl = 'https://angband3d-cloud-564958309282.us-central1.run.app/?char=TestDeath_' + Date.now();
    const res = await fetch('http://127.0.0.1:' + port + '/json/new?' + encodeURIComponent(targetUrl), { method: 'PUT' });
    const target = await res.json();
    const ws = new WebSocket(target.webSocketDebuggerUrl);

    let id = 1;
    const send = (method, params = {}) => new Promise(resolve => {
        const reqId = id++;
        const onMsg = (data) => {
            const msg = JSON.parse(data.toString());
            if (msg.id === reqId) {
                ws.off('message', onMsg);
                resolve(msg.result);
            }
        };
        ws.on('message', onMsg);
        ws.send(JSON.stringify({ id: reqId, method, params }));
    });

    ws.on('open', async () => {
        await send('Runtime.enable');
        await send('Page.enable');
        await new Promise(r => setTimeout(r, 2500));

        // Evaluate showDeathModal with authentic player frame and select Tab 1 (Equipment)
        await send('Runtime.evaluate', {
            expression: `(() => {
                if (!window.__app || !window.__app.hud) return 'no hud';
                const mockFrame = {
                    phase: 'play',
                    player: {
                        name: 'Brave Warrior',
                        race: 'Dwarf',
                        class: 'Warrior',
                        level: 12,
                        depth: 4,
                        max_depth: 6,
                        gold: 14250,
                        exp: 8420,
                        died_from: 'a Fire Drake breath',
                        dead: true,
                        stats: { str: 18, int: 11, wis: 13, dex: 16, con: 18 },
                        ac: 38,
                        speed: 112,
                        equipment: [
                            { slot_name: 'weapon', mention: 'a) Wielding', name: 'a Broad Sword (2d5) (+4,+6)', weight: 150 },
                            { slot_name: 'shield', mention: 'b) On arm', name: 'a Large Metal Shield (+2)', weight: 120 },
                            { slot_name: 'head', mention: 'c) On head', name: 'an Iron Helm (+1)', weight: 75 }
                        ],
                        inventory: [
                            { name: '3 Rations of Food', weight: 30 },
                            { name: 'a Potion of Cure Serious Wounds', weight: 4 },
                            { name: 'a Scroll of Phase Door', weight: 2 }
                        ],
                        quiver: []
                    }
                };
                window.__app.hud.showDeathModal(mockFrame);
                window.__app.hud.switchDeathTab(1);
                return 'ok';
            })()`
        });

        await new Promise(r => setTimeout(r, 800));
        const shot = await send('Page.captureScreenshot', { format: 'png' });
        const outPath = path.join(__dirname, 'death_modal_capture.png');
        fs.writeFileSync(outPath, Buffer.from(shot.data, 'base64'));
        console.log('Death screenshot captured to', outPath);
        process.exit(0);
    });
}
testDeath();
