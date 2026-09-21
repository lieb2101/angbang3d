const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');
const WebSocket = require('../server/node_modules/ws');

async function main() {
    const chromePath = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
    const port = 9223;
    const targetUrl = 'https://angband3d-cloud-564958309282.us-central1.run.app/?char=Hero_' + Date.now();

    console.log('[Test] Launching Chrome on port ' + port + '...');
    const chrome = spawn(chromePath, [
        '--headless=new',
        `--remote-debugging-port=${port}`,
        '--window-size=1280,800',
        '--no-first-run',
        '--no-default-browser-check',
        '--user-data-dir=' + path.join(__dirname, 'temp_rubble_profile_' + Date.now())
    ], { stdio: 'ignore' });

    await new Promise(r => setTimeout(r, 1500));

    try {
        const res = await fetch(`http://127.0.0.1:${port}/json/new?${encodeURIComponent(targetUrl)}`, { method: 'PUT' });
        const target = await res.json();
        const ws = new WebSocket(target.webSocketDebuggerUrl);
        let id = 1;
        const callbacks = new Map();

        ws.on('open', async () => {
            function send(method, params = {}) {
                return new Promise((resolve) => {
                    const reqId = id++;
                    callbacks.set(reqId, resolve);
                    ws.send(JSON.stringify({ id: reqId, method, params }));
                });
            }

            ws.on('message', (data) => {
                const msg = JSON.parse(data.toString());
                if (msg.id && callbacks.has(msg.id)) {
                    const cb = callbacks.get(msg.id);
                    callbacks.delete(msg.id);
                    cb(msg.result);
                }
            });

            await send('Runtime.enable');
            await send('Page.enable');

            // Wait for connect
            await send('Runtime.evaluate', {
                expression: 'new Promise(resolve => { const check = () => { if (window.__app && window.__app.network && window.__app.network.connected) resolve(); else setTimeout(check, 100); }; check(); })',
                awaitPromise: true
            });

            // Quick birth
            await send('Runtime.evaluate', { expression: 'document.getElementById("btn-quick-birth").click()' });

            // Wait for 3D town world
            await send('Runtime.evaluate', {
                expression: `new Promise(resolve => {
                    const check = () => {
                        const term = document.getElementById("terminal-container");
                        if (term && term.classList.contains("hidden")) resolve(true);
                        else setTimeout(check, 300);
                    };
                    check();
                })`,
                awaitPromise: true
            });
            await new Promise(r => setTimeout(r, 1000));

            // Move to (31, 3) facing South towards Rubble at (31, 4)
            // Player starts around (30, 3)
            console.log('[Test] Navigating near rubble in town...');
            await send('Runtime.evaluate', {
                expression: `(async () => {
                    // Turn East to walk right
                    window.__app.network.sendKey('right'); // Walk east
                    await new Promise(r => setTimeout(r, 350));
                    // Turn to face South towards rubble (facing 2)
                    window.__app.dungeon.facing = 2;
                    window.__app.dungeon.targetYaw = Math.PI;
                    if (window.__app.hud) window.__app.hud.onCameraTurn(Math.PI);
                })()`,
                awaitPromise: true
            });
            await new Promise(r => setTimeout(r, 1200));

            // Capture Town Rubble View
            const snap1 = await send('Page.captureScreenshot', { format: 'png' });
            fs.writeFileSync(path.join(__dirname, 'town_rubble_view.png'), Buffer.from(snap1.data, 'base64'));
            console.log('[Test] Saved town_rubble_view.png');

            // Descend to Depth 1
            console.log('[Test] Descending to Depth 1...');
            await send('Runtime.evaluate', {
                expression: `(async () => {
                    window.__app.network.sendKey('ctrl_a');
                    await new Promise(r => setTimeout(r, 200));
                    window.__app.network.sendKey('enter');
                    await new Promise(r => setTimeout(r, 200));
                    window.__app.network.sendKeys('1');
                    await new Promise(r => setTimeout(r, 200));
                    window.__app.network.sendKey('enter');
                    await new Promise(r => setTimeout(r, 800));
                    // Turn to face along the hallway
                    window.__app.dungeon.facing = 1;
                    window.__app.dungeon.targetYaw = -Math.PI / 2;
                    if (window.__app.hud) window.__app.hud.onCameraTurn(-Math.PI / 2);
                })()`,
                awaitPromise: true
            });
            await new Promise(r => setTimeout(r, 1500));

            const snap2 = await send('Page.captureScreenshot', { format: 'png' });
            fs.writeFileSync(path.join(__dirname, 'dungeon_fog_view.png'), Buffer.from(snap2.data, 'base64'));
            console.log('[Test] Saved dungeon_fog_view.png');

            console.log('[Test] Complete!');
            chrome.kill();
            process.exit(0);
        });
    } catch (err) {
        console.error('[Test Error]', err);
        chrome.kill();
        process.exit(1);
    }
}

main();
