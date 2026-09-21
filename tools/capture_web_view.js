const { spawn } = require('child_process');
const http = require('http');
const fs = require('fs');
const path = require('path');
const WebSocket = require('../server/node_modules/ws');

async function main() {
    const chromePath = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
    const port = 9222;
    const targetUrl = 'https://angband3d-cloud-564958309282.us-central1.run.app/?char=Hero_' + Date.now();

    console.log('[Capture] Launching Chrome headless on port ' + port + '...');
    const chrome = spawn(chromePath, [
        '--headless=new',
        `--remote-debugging-port=${port}`,
        '--disable-gpu-shader-disk-cache',
        '--window-size=1280,800',
        '--no-first-run',
        '--no-default-browser-check',
        '--user-data-dir=' + path.join(__dirname, 'temp_chrome_profile_' + Date.now())
    ], { stdio: 'ignore' });

    // Give Chrome 1.5s to start listening
    await new Promise(r => setTimeout(r, 1500));

    try {
        // Query /json/new to create a new target
        const res = await fetch(`http://127.0.0.1:${port}/json/new?${encodeURIComponent(targetUrl)}`, { method: 'PUT' });
        const target = await res.json();
        console.log('[Capture] Target created:', target.webSocketDebuggerUrl);

        const ws = new WebSocket(target.webSocketDebuggerUrl);
        let id = 1;
        const callbacks = new Map();

        ws.on('open', async () => {
            console.log('[Capture] CDP Connected');

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
                } else if (msg.method === 'Runtime.consoleAPICalled') {
                    const args = msg.params.args.map(a => a.value || JSON.stringify(a)).join(' ');
                    console.log(`[Browser Console ${msg.params.type}]`, args);
                } else if (msg.method === 'Runtime.exceptionThrown') {
                    console.error(`[Browser Uncaught Exception]`, msg.params.exceptionDetails.text, msg.params.exceptionDetails.exception);
                } else if (msg.method === 'Log.entryAdded') {
                    console.log(`[Browser Log ${msg.params.entry.level}]`, msg.params.entry.text);
                }
            });

            await send('Runtime.enable');
            await send('Log.enable');
            await send('Page.enable');
            await send('Network.enable');

            ws.on('message', (data) => {
                const msg = JSON.parse(data.toString());
                if (msg.method === 'Network.responseReceived') {
                    if (msg.params.response.status >= 400) {
                        console.log(`[404/Error Resource] ${msg.params.response.status} ${msg.params.response.url}`);
                    }
                }
            });

            console.log('[Capture] Waiting 2 seconds for page load...');
            await new Promise(r => setTimeout(r, 2000));

            async function pressKey(key, text) {
                console.log(`[Capture] Pressing key: ${key}`);
                await send('Input.dispatchKeyEvent', { type: 'keyDown', key, text });
                await new Promise(r => setTimeout(r, 50));
                await send('Input.dispatchKeyEvent', { type: 'keyUp', key });
            }

            const sendAppKey = async (k) => {
                await send('Runtime.evaluate', {
                    expression: `if (window.__app) window.__app.network.sendKey('${k}');`
                });
            };

            console.log('[Capture] Waiting for WebSocket game connection to establish...');
            await send('Runtime.evaluate', {
                expression: 'new Promise(resolve => { const check = () => { if (window.__app && window.__app.network && window.__app.network.connected) resolve(); else setTimeout(check, 100); }; check(); })',
                awaitPromise: true
            });
            console.log('[Capture] Connected! Waiting 1s for splash frame...');
            await new Promise(r => setTimeout(r, 1000));

            console.log('[Capture] Clicking #btn-quick-birth to start 4-step hero birth...');
            await send('Runtime.evaluate', {
                expression: 'document.getElementById("btn-quick-birth").click()'
            });

            console.log('[Capture] Waiting for terminal to hide and 3D town world to load...');
            const entered = await send('Runtime.evaluate', {
                expression: `new Promise(resolve => {
                    const start = Date.now();
                    const check = () => {
                        const term = document.getElementById("terminal-container");
                        if (term && term.classList.contains("hidden")) {
                            resolve(true);
                            return;
                        }
                        if (Date.now() - start > 18000) {
                            resolve(false);
                            return;
                        }
                        setTimeout(check, 500);
                    };
                    check();
                })`,
                awaitPromise: true
            });
            console.log('[Capture] Entered 3D World:', entered && entered.result ? entered.result.value : false);

            console.log('[Capture] Waiting 2 seconds for town 3D world to render...');
            await new Promise(r => setTimeout(r, 2000));

            console.log('[Capture] Turning camera to face town square (South)...');
            await send('Runtime.evaluate', {
                expression: 'if (window.__app) { window.__app.dungeon.turn(1); window.__app.dungeon.turn(1); }'
            });
            await new Promise(r => setTimeout(r, 600));

            console.log('[Capture] Walking forward toward townspeople in the plaza...');
            for (let s = 0; s < 4; s++) {
                await send('Runtime.evaluate', {
                    expression: 'if (window.__app) { const k = window.__app.input.getRelativeDirectionKey(8); if (k) window.__app.network.sendKey(k); }'
                });
                await new Promise(r => setTimeout(r, 500));
            }
            await new Promise(r => setTimeout(r, 600));
            const itemSlotInfo = await send('Runtime.evaluate', {
                expression: `(() => {
                    const vm = window.__app ? window.__app.dungeon : null;
                    if (!vm) return "no vm";
                    const rSlot = vm.rightArm && vm.rightArm.itemSlot ? vm.rightArm.itemSlot : null;
                    const rChildren = rSlot ? rSlot.children.map(c => ({ name: c.name, type: c.type, visible: c.visible, count: c.children ? c.children.length : 0 })) : [];
                    return JSON.stringify({
                        currentRightWeapon: vm.currentRightWeapon,
                        slotChildren: rChildren,
                        weaponCacheKeys: Array.from(vm.weaponCache.keys()),
                        heightRatio: vm.heightRatio,
                        rightHandPos: vm.rightHandGroup.position,
                        rightHandRot: vm.rightHandGroup.rotation
                    });
                })()`,
                returnByValue: true
            });
            console.log('[Capture ItemSlot Debug]', itemSlotInfo && itemSlotInfo.result ? itemSlotInfo.result.value : 'none');

            // Inspect loaded monster templates & weapon cache
            const tmplDebug = await send('Runtime.evaluate', {
                expression: `(() => {
                    const d = window.__app ? window.__app.dungeon : null;
                    if (!d) return "no dungeon";
                    return JSON.stringify({
                        monsterTemplateKeys: Array.from(d.monsterTemplates.keys()),
                        weaponCacheKeys: Array.from(d.weaponCache.keys())
                    });
                })()`,
                returnByValue: true
            });
            console.log('[Capture Templates Loaded]', tmplDebug && tmplDebug.result ? tmplDebug.result.value : 'none');

            // Spawn townspeople (including Blubbering idiot with white attribute) right in the camera view frustum
            await send('Runtime.evaluate', {
                expression: `(() => {
                    const p = window.__app.player;
                    const d = window.__app.dungeon;
                    if (p && d) {
                        const yaw = d.camera.rotation.y;
                        const fwdX = -Math.sin(yaw);
                        const fwdZ = -Math.cos(yaw);
                        const rightX = Math.cos(yaw);
                        const rightZ = -Math.sin(yaw);

                        d.updateMonsters([
                            {
                                id: 901,
                                x: p.x + fwdX * 3.0 - rightX * 0.9,
                                y: p.y + fwdZ * 3.0 - rightZ * 0.9,
                                glyph: 't',
                                race: 'Blubbering idiot',
                                attr: 1, // White attribute
                                hp: 2,
                                hp_max: 2
                            },
                            {
                                id: 902,
                                x: p.x + fwdX * 3.0 + rightX * 0.9,
                                y: p.y + fwdZ * 3.0 + rightZ * 0.9,
                                glyph: 't',
                                race: 'Townsperson',
                                attr: 4, // Red attribute
                                hp: 15,
                                hp_max: 15
                            }
                        ], p);
                    }
                })()`
            });
            await new Promise(r => setTimeout(r, 2000));

            console.log('[Capture] Taking close-up screenshot of Town...');
            const screenshotResult = await send('Page.captureScreenshot', { format: 'png' });
            if (screenshotResult && screenshotResult.data) {
                const buffer = Buffer.from(screenshotResult.data, 'base64');
                const outPath = path.join(__dirname, 'web_capture.png');
                fs.writeFileSync(outPath, buffer);
                console.log(`[Capture] Town screenshot written to ${outPath} (${buffer.length} bytes)`);
            }

            // Now navigate to Depth 1 (Upper Crypts / Dungeon)
            console.log('[Capture] Enabling Wizard Mode and descending to Depth 1 (synchronized)...');
            const descendResult = await send('Runtime.evaluate', {
                expression: `(async () => {
                    const sendKeyAndWait = (key) => {
                        return new Promise(resolve => {
                            const startSeq = window.__app.lastFrame ? window.__app.lastFrame.seq : 0;
                            window.__app.network.sendKey(key);
                            const check = () => {
                                if (window.__app.lastFrame && window.__app.lastFrame.seq > startSeq) {
                                    resolve(window.__app.lastFrame);
                                } else {
                                    setTimeout(check, 30);
                                }
                            };
                            check();
                        });
                    };

                    // Find down staircase in map (FEAT_MORE = 6)
                    const map = window.__app.lastFrame.map;
                    let stairX = 23, stairY = 4;
                    for (let y = 0; y < map.h; y++) {
                        for (let x = 0; x < map.w; x++) {
                            const featHi = parseInt(map.rows[y].f[x*2], 16);
                            const featLo = parseInt(map.rows[y].f[x*2+1], 16);
                            if (((featHi << 4) | featLo) === 6) {
                                stairX = x; stairY = y; break;
                            }
                        }
                    }

                    // Walk to the down staircase
                    let f = window.__app.lastFrame;
                    for (let step = 0; step < 40 && (f.player.x !== stairX || f.player.y !== stairY); step++) {
                        let dx = stairX - f.player.x;
                        let dy = stairY - f.player.y;
                        let k = '';
                        if (dx > 0) k = 'right';
                        else if (dx < 0) k = 'left';
                        else if (dy > 0) k = 'down';
                        else if (dy < 0) k = 'up';
                        f = await sendKeyAndWait(k);
                        if (f.ui && f.ui.more) f = await sendKeyAndWait('enter');
                    }

                    // Descend to Depth 1 with '>'
                    f = await sendKeyAndWait('>');
                    if (f.ui && f.ui.more) {
                        f = await sendKeyAndWait('enter');
                    }

                    return {
                        depth: f.player ? f.player.depth : null,
                        seq: f.seq,
                        playerPos: f.player ? { x: f.player.x, y: f.player.y } : null,
                        awaiting: f.ui ? f.ui.awaiting_command : null
                    };
                })()`,
                awaitPromise: true,
                returnByValue: true
            });
            console.log('[Capture Descend Result]', descendResult && descendResult.result ? descendResult.result.value : 'none');
            await new Promise(r => setTimeout(r, 2000));

            const depthCheck = await send('Runtime.evaluate', {
                expression: `(() => {
                    const p = (window.__app && window.__app.lastFrame) ? window.__app.lastFrame.player : null;
                    const d = window.__app ? window.__app.dungeon : null;
                    return JSON.stringify({
                        depth: p ? p.depth : null,
                        torchIntensity: d && d.torchLight ? d.torchLight.intensity : null,
                        torchRange: d && d.torchLight ? d.torchLight.distance : null,
                        torchPos: d && d.torchLight ? d.torchLight.position : null,
                        cameraPos: d && d.camera ? d.camera.position : null,
                        ambientColor: d && d.ambientLight ? d.ambientLight.color.getHexString() : null,
                        ambientIntensity: d && d.ambientLight ? d.ambientLight.intensity : null,
                        sunVisible: d && d.sunLight ? d.sunLight.visible : null,
                        wallCount: d && d.wallMesh ? d.wallMesh.count : 0,
                        floorCount: d && d.floorMesh ? d.floorMesh.count : 0,
                        ceilingCount: d && d.ceilingMesh ? d.ceilingMesh.count : 0
                    });
                })()`,
                returnByValue: true
            });
            console.log('[Capture Depth 1 State]', depthCheck && depthCheck.result ? depthCheck.result.value : 'none');

            console.log('[Capture] Taking screenshot of Depth 1 (Upper Crypts)...');
            const depth1Result = await send('Page.captureScreenshot', { format: 'png' });
            if (depth1Result && depth1Result.data) {
                const buffer = Buffer.from(depth1Result.data, 'base64');
                const outPath = path.join(__dirname, 'depth1_capture.png');
                fs.writeFileSync(outPath, buffer);
                console.log(`[Capture] SUCCESS! Depth 1 screenshot written to ${outPath} (${buffer.length} bytes)`);
            }

            ws.close();
            chrome.kill();
        });

        // Timeout guard
        setTimeout(() => {
            console.log('[Capture] Timeout guard triggered');
            try { ws.close(); } catch(e) {}
            try { chrome.kill(); } catch(e) {}
            process.exit(0);
        }, 45000);

    } catch (err) {
        console.error('[Capture] Error:', err);
        chrome.kill();
    }
}

main();
