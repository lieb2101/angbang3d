const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');
const WebSocket = require('../server/node_modules/ws');

async function main() {
    const chromePath = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
    const port = 9224;
    const targetUrl = 'https://angband3d-cloud-564958309282.us-central1.run.app/?char=Hero_' + Date.now();

    console.log('[Verify] Launching Chrome on port ' + port + '...');
    const chrome = spawn(chromePath, [
        '--headless=new',
        `--remote-debugging-port=${port}`,
        '--window-size=1280,800',
        '--no-first-run',
        '--no-default-browser-check',
        '--user-data-dir=' + path.join(__dirname, 'temp_tilt_profile_' + Date.now())
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

            // Descend to Depth 1
            console.log('[Verify] Descending to Depth 1...');
            await send('Runtime.evaluate', {
                expression: `(async () => {
                    window.__app.network.sendKey('ctrl_a');
                    await new Promise(r => setTimeout(r, 200));
                    window.__app.network.sendKey('enter');
                    await new Promise(r => setTimeout(r, 200));
                    window.__app.network.sendKeys('1');
                    await new Promise(r => setTimeout(r, 200));
                    window.__app.network.sendKey('enter');
                })()`,
                awaitPromise: true
            });
            await new Promise(r => setTimeout(r, 1500));

            // Read camera and creature state
            const state = await send('Runtime.evaluate', {
                expression: `(() => {
                    const d = window.__app.dungeon;
                    const monsters = [];
                    for (const [id, m] of d.monsters.entries()) {
                        monsters.push({
                            id,
                            name: m.race,
                            modelHeight: m.modelHeight,
                            nameplateY: m.nameplate ? m.nameplate.position.y : null,
                            pos: { x: m.position.x, y: m.position.y, z: m.position.z }
                        });
                    }
                    return {
                        eyeHeight: d.eyeHeight,
                        basePitch: d.basePitch,
                        basePitchDeg: (d.basePitch * 180 / Math.PI),
                        cameraPitch: d.camera.rotation.x,
                        cameraPitchDeg: (d.camera.rotation.x * 180 / Math.PI),
                        cameraFov: d.camera.fov,
                        monsters
                    };
                })()`,
                returnByValue: true
            });
            console.log('[Verify Depth 1 State]', JSON.stringify(state.result.value, null, 2));

            // Capture Depth 1 Screenshot
            const snap = await send('Page.captureScreenshot', { format: 'png' });
            fs.writeFileSync(path.join(__dirname, 'verified_depth1_tilt.png'), Buffer.from(snap.data, 'base64'));
            console.log('[Verify] Saved verified_depth1_tilt.png');

            chrome.kill();
            process.exit(0);
        });
    } catch (err) {
        console.error('[Verify Error]', err);
        chrome.kill();
        process.exit(1);
    }
}

main();
