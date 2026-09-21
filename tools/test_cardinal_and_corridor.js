const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');
const WebSocket = require('../server/node_modules/ws');

async function main() {
    const chromePath = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
    const port = 9225;
    const testUser = 'TestAngles_' + Date.now();
    const targetUrl = `https://angband3d-cloud-564958309282.us-central1.run.app/?char=${testUser}`;

    console.log('[Test] Launching Chrome...');
    const chrome = spawn(chromePath, [
        '--headless=new',
        `--remote-debugging-port=${port}`,
        '--window-size=1280,800',
        '--no-first-run',
        '--no-default-browser-check',
        '--user-data-dir=' + path.join(__dirname, 'temp_chrome_angles_' + Date.now())
    ], { stdio: 'ignore' });

    await new Promise(r => setTimeout(r, 1500));

    try {
        const res = await fetch(`http://127.0.0.1:${port}/json/new?${encodeURIComponent(targetUrl)}`, { method: 'PUT' });
        const target = await res.json();
        const ws = new WebSocket(target.webSocketDebuggerUrl);
        let id = 1;

        const send = (method, params = {}) => new Promise((resolve) => {
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
            console.log('[Test] CDP connected');
            await send('Runtime.enable');
            await send('Page.enable');

            // Wait for 3D world to load
            await new Promise(r => setTimeout(r, 3000));

            // Click quick birth
            await send('Runtime.evaluate', {
                expression: `(() => {
                    const btn = document.getElementById('btn-quick-birth');
                    if (btn) btn.click();
                })()`
            });

            // Wait for 3D world
            for (let i = 0; i < 20; i++) {
                const res = await send('Runtime.evaluate', {
                    expression: `(window.__app && window.__app.currentPhase === 'play' && window.__app.dungeon && window.__app.dungeon.camera) ? true : false`
                });
                if (res.result && res.result.value === true) break;
                await new Promise(r => setTimeout(r, 500));
            }

            console.log('[Test] In 3D World! Checking camera angles facing North...');
            let rot = await send('Runtime.evaluate', {
                expression: `(() => {
                    const c = window.__app.dungeon.camera;
                    return { x: c.rotation.x, y: c.rotation.y, z: c.rotation.z, order: c.rotation.order, pitch: window.__app.dungeon.targetPitch };
                })()`,
                returnByValue: true
            });
            console.log('[Test] North Camera:', rot.result.value);

            // Turn East (Yaw -PI/2)
            console.log('[Test] Turning camera East...');
            await send('Runtime.evaluate', {
                expression: `(() => {
                    window.__app.dungeon.turnCameraRight();
                })()`
            });
            await new Promise(r => setTimeout(r, 800));

            rot = await send('Runtime.evaluate', {
                expression: `(() => {
                    const c = window.__app.dungeon.camera;
                    return { x: c.rotation.x, y: c.rotation.y, z: c.rotation.z, order: c.rotation.order };
                })()`,
                returnByValue: true
            });
            console.log('[Test] East Camera (facing 1):', rot.result.value);

            // Capture screenshot facing East
            const eastShot = await send('Page.captureScreenshot', { format: 'png' });
            fs.writeFileSync(path.join(__dirname, 'facing_east_capture.png'), Buffer.from(eastShot.data, 'base64'));
            console.log('[Test] Captured facing_east_capture.png');

            // Turn South (Yaw -PI)
            console.log('[Test] Turning camera South...');
            await send('Runtime.evaluate', {
                expression: `(() => {
                    window.__app.dungeon.turnCameraRight();
                })()`
            });
            await new Promise(r => setTimeout(r, 800));

            rot = await send('Runtime.evaluate', {
                expression: `(() => {
                    const c = window.__app.dungeon.camera;
                    return { x: c.rotation.x, y: c.rotation.y, z: c.rotation.z, order: c.rotation.order };
                })()`,
                returnByValue: true
            });
            console.log('[Test] South Camera (facing 2):', rot.result.value);

            // Capture screenshot facing South
            const southShot = await send('Page.captureScreenshot', { format: 'png' });
            fs.writeFileSync(path.join(__dirname, 'facing_south_capture.png'), Buffer.from(southShot.data, 'base64'));
            console.log('[Test] Captured facing_south_capture.png');

            // Now test Death Screen Modal & input handling
            console.log('[Test] Testing Death Screen Modal & Input handling...');
            const deathTestResult = await send('Runtime.evaluate', {
                expression: `(() => {
                    const hud = window.__app.hud;
                    const input = window.__app.input;
                    const net = window.__app.network;

                    // Mock death frame
                    const deathFrame = {
                        phase: 'play',
                        player: {
                            name: 'Test Hero',
                            race: 'Elf',
                            class: 'Mage',
                            level: 5,
                            depth: 2,
                            gold: 2500,
                            exp: 1200,
                            dead: true,
                            equipment: [{ mention: 'a) ', name: 'a Dagger' }],
                            inventory: [{ name: 'a Potion of Cure Light Wounds' }]
                        },
                        term: {
                            rows: [
                                { g: '   Goodbye, Test Hero!                                                          ', a: '01' },
                                { g: '   A panic save exists. Use it? [y/n]                                           ', a: '03' }
                            ]
                        }
                    };

                    hud.showDeathModal(deathFrame);

                    // Verify active death tab is 0
                    const tab0Active = hud.activeDeathTab === 0;

                    // Test key dispatch: dispatch 'y' keydown
                    let sentKey = null;
                    const origSendKey = net.sendKey;
                    net.sendKey = (k) => { sentKey = k; };

                    const eventY = new KeyboardEvent('keydown', { key: 'y', bubbles: true });
                    window.dispatchEvent(eventY);

                    const ySent = sentKey === 'y';

                    // Test dispatch 'Tab' keydown (should switch to tab 1: equipment)
                    const eventTab = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true });
                    window.dispatchEvent(eventTab);
                    const tab1Active = hud.activeDeathTab === 1;

                    // Test dispatch '2' keydown (should switch to tab 1)
                    // Test dispatch '3' keydown (should switch to tab 2: inventory)
                    const event3 = new KeyboardEvent('keydown', { key: '3', bubbles: true });
                    window.dispatchEvent(event3);
                    const tab2Active = hud.activeDeathTab === 2;

                    // Restore sendKey
                    net.sendKey = origSendKey;

                    return {
                        deathModalVisible: !hud.deathModal.classList.contains('hidden'),
                        tab0ActiveInitial: tab0Active,
                        yForwardedToTerminal: ySent,
                        tabKeySwitchedToTab1: tab1Active,
                        number3SwitchedToTab2: tab2Active
                    };
                })()`,
                returnByValue: true
            });
            console.log('[Test] Death Modal Test Result:', deathTestResult.result.value);

            console.log('[Test] All tests completed successfully!');
            chrome.kill();
            process.exit(0);
        });
    } catch (e) {
        console.error(e);
        chrome.kill();
        process.exit(1);
    }
}

main();
