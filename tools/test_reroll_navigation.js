const { spawn } = require('child_process');
const path = require('path');
const WebSocket = require('../server/node_modules/ws');

async function testReroll() {
    const port = 9225;
    const chrome = spawn('C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe', [
        '--headless=new',
        '--remote-debugging-port=' + port,
        '--window-size=1280,800',
        '--no-first-run',
        '--no-default-browser-check',
        '--user-data-dir=' + path.join(__dirname, 'temp_reroll_test_' + Date.now())
    ], { stdio: 'ignore' });

    await new Promise(r => setTimeout(r, 1500));
    const targetUrl = 'https://angband3d-cloud-564958309282.us-central1.run.app/?char=TestReroll_' + Date.now();
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
            } else if (msg.method === 'Runtime.consoleAPICalled') {
                const args = msg.params.args.map(a => a.value || JSON.stringify(a)).join(' ');
                console.log(`[Browser Console ${msg.params.type}]`, args);
            } else if (msg.method === 'Runtime.exceptionThrown') {
                console.error(`[Browser Exception]`, msg.params.exceptionDetails);
            }
        };
        ws.on('message', onMsg);
        ws.send(JSON.stringify({ id: reqId, method, params }));
    });

    ws.on('open', async () => {
        await send('Runtime.enable');
        await send('Page.enable');
        await new Promise(r => setTimeout(r, 2500));

        // 1. Show death modal
        const evalRes = await send('Runtime.evaluate', {
            expression: `(() => {
                if (!window.__app || !window.__app.hud) return 'no app';
                const mockFrame = {
                    phase: 'play',
                    player: { name: 'DeadGuy', dead: true, depth: 0 }
                };
                window.__app.hud.showDeathModal(mockFrame);
                return 'death modal shown: ' + !document.getElementById('death-modal').classList.contains('hidden');
            })()`
        });
        console.log('[Test Eval]', evalRes);

        // 2. Dispatch 'N' keydown event to window
        console.log('[Test] Dispatching N keydown event to trigger reroll...');
        const navPromise = new Promise(resolve => {
            ws.on('message', (data) => {
                const msg = JSON.parse(data.toString());
                if (msg.method === 'Page.frameNavigated') {
                    resolve(msg.params.frame.url);
                }
            });
        });

        await send('Input.dispatchKeyEvent', {
            type: 'keyDown',
            key: 'N',
            code: 'KeyN',
            windowsVirtualKeyCode: 78
        });

        const navUrl = await Promise.race([
            navPromise,
            new Promise(r => setTimeout(() => r('timeout'), 4000))
        ]);

        console.log('[Test] Navigation result:', navUrl);
        if (navUrl.includes('?char=Hero_')) {
            console.log('[Test] SUCCESS: Navigated to fresh character URL:', navUrl);
        } else {
            console.error('[Test] FAILED: Did not navigate to Hero_* URL:', navUrl);
            process.exit(1);
        }

        try { chrome.kill(); } catch (_) {}
        process.exit(0);
    });
}

testReroll().catch(err => {
    console.error(err);
    process.exit(1);
});
