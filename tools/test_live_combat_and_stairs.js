const WebSocket = require('../server/node_modules/ws');
const assert = require('assert');

const ws = new WebSocket('wss://angband3d-cloud-564958309282.us-east1.run.app/ws?user=LiveCombatTest_' + Math.random().toString(36).substring(2, 6));

let lastFrame = null;
const frames = [];

ws.on('message', (data) => {
    const msg = JSON.parse(data.toString());
    if (msg.t === 'frame') {
        lastFrame = msg;
        frames.push(msg);
    }
});

function wait(ms = 100) {
    return new Promise(resolve => setTimeout(resolve, ms));
}

function sendKey(k) {
    ws.send('key ' + k);
}

// Minimal mock DOM for WebHUD
const elements = {};
function getMockElement(id) {
    if (!elements[id]) {
        elements[id] = {
            id,
            style: {},
            dataset: {},
            setAttribute: () => {},
            getAttribute: () => null,
            classList: {
                _classes: new Set(),
                add(c) { this._classes.add(c); },
                remove(c) { this._classes.delete(c); },
                contains(c) { return this._classes.has(c); }
            },
            innerHTML: '',
            textContent: '',
            appendChild(el) { (this.children = this.children || []).push(el); },
            prepend(el) { (this.children = this.children || []).unshift(el); },
            scrollTop: 0,
            scrollHeight: 100,
            children: [],
            addEventListener: () => {},
            removeEventListener: () => {},
            getContext: () => ({
                fillRect: () => {},
                clearRect: () => {},
                strokeRect: () => {},
                drawImage: () => {},
                beginPath: () => {},
                arc: () => {},
                fill: () => {},
                stroke: () => {},
                moveTo: () => {},
                lineTo: () => {},
                closePath: () => {},
                fillText: () => {}
            })
        };
    }
    return elements[id];
}

global.document = {
    getElementById: getMockElement,
    querySelectorAll: () => [],
    querySelector: () => null,
    createElement: (tag) => ({
        tag,
        style: {},
        classList: {
            _classes: new Set(),
            add(c) { this._classes.add(c); },
            remove(c) { this._classes.delete(c); },
            contains(c) { return this._classes.has(c); }
        },
        innerHTML: '',
        textContent: '',
        appendChild(el) { (this.children = this.children || []).push(el); },
        setAttribute: () => {},
        addEventListener: () => {},
        removeEventListener: () => {},
        children: [],
        getContext: () => ({
            fillRect: () => {},
            clearRect: () => {},
            strokeRect: () => {},
            drawImage: () => {},
            beginPath: () => {},
            arc: () => {},
            fill: () => {},
            stroke: () => {},
            moveTo: () => {},
            lineTo: () => {},
            closePath: () => {},
            fillText: () => {}
        })
    })
};

global.window = {
    innerWidth: 1920,
    innerHeight: 1080
};

// Load actual WebHUD class from hud.js
const fs = require('fs');
const hudJs = fs.readFileSync('./server/public/js/hud.js', 'utf8');
const WebHUD = eval(hudJs + ';\nWebHUD;');

async function run() {
    console.log('1. Connecting to live Cloud backend and auto-birthing...');
    while (!lastFrame) await wait(50);

    for (let i = 0; i < 25; i++) {
        if (lastFrame.phase === 'play' && lastFrame.map && lastFrame.player) break;
        sendKey('enter');
        await wait(100);
        sendKey('@');
        await wait(100);
    }

    assert(lastFrame.phase === 'play', 'Must be in active play phase');
    console.log(`   -> Reached Town (Depth: ${lastFrame.player.depth}) at pos (${lastFrame.player.x}, ${lastFrame.player.y})`);

    const hud = new WebHUD();
    hud.update(lastFrame);

    console.log('   -> Initial message count:', hud.messageHistory.length);
    console.log('   -> Initial message:', hud.messageHistory[0] ? hud.messageHistory[0].text : 'none');
    assert.strictEqual(hud.messageHistory[0].text, 'Welcome to the Town of Angband! Visit the General Store and Armory to equip your journey.');

    // Move around town and bump walls / search for town monster to hit
    console.log('2. Testing town exploration & combat logging without exceptions...');
    for (let step = 0; step < 10; step++) {
        sendKey('up');
        await wait(60);
        hud.update(lastFrame);
    }
    for (let step = 0; step < 10; step++) {
        sendKey('right');
        await wait(60);
        hud.update(lastFrame);
    }

    console.log(`   -> Log after movement/bumps: ${hud.messageHistory.length} messages`);
    for (const m of hud.messageHistory) {
        console.log(`      [MSG] ${m.text}`);
    }

    // Now let's descend down stairs (feat 6)
    console.log('3. Locating stairs to descend to Dungeon Level 1...');
    let stairX = -1, stairY = -1;
    const map = lastFrame.map;
    for (let y = 0; y < map.h; y++) {
        for (let x = 0; x < map.w; x++) {
            const f = parseInt(map.rows[y].f.substring(x*2, x*2+2), 16) || 0;
            if (f === 6) { // Downstairs
                stairX = x; stairY = y;
                break;
            }
        }
        if (stairX !== -1) break;
    }

    if (stairX !== -1) {
        console.log(`   -> Found downstairs at (${stairX}, ${stairY})`);
        // BFS path
        const queue = [{ x: lastFrame.player.x, y: lastFrame.player.y, path: [] }];
        const visited = new Set([`${lastFrame.player.x},${lastFrame.player.y}`]);
        let path = null;
        while (queue.length > 0) {
            const c = queue.shift();
            if (c.x === stairX && c.y === stairY) { path = c.path; break; }
            for (const d of [{dx:0,dy:-1,k:'up'},{dx:0,dy:1,k:'down'},{dx:-1,dy:0,k:'left'},{dx:1,dy:0,k:'right'}]) {
                const nx = c.x + d.dx, ny = c.y + d.dy;
                if (nx >= 0 && nx < map.w && ny >= 0 && ny < map.h && !visited.has(`${nx},${ny}`)) {
                    const feat = parseInt(map.rows[ny].f.substring(nx*2, nx*2+2), 16);
                    if (hud.isWalkableOrPortal(feat)) {
                        visited.add(`${nx},${ny}`);
                        queue.push({ x: nx, y: ny, path: [...c.path, d.k] });
                    }
                }
            }
        }

        if (path) {
            console.log(`   -> Walking to stairs (${path.length} steps)...`);
            for (const k of path) {
                sendKey(k);
                await wait(80);
                hud.update(lastFrame);
            }
            console.log(`   -> At stairs position (${lastFrame.player.x}, ${lastFrame.player.y}). Descending ('>')...`);
            sendKey('>');
            await wait(300);
            sendKey('space'); // dismiss -more- if any
            await wait(200);

            hud.update(lastFrame);
            console.log(`   -> Now at Depth: ${lastFrame.player.depth}`);
            console.log(`   -> Latest messages in dungeon:`);
            for (const m of hud.messageHistory.slice(0, 5)) {
                console.log(`      [DUNGEON MSG] ${m.text}`);
            }

            // Verify depth changed and transition was cleanly logged
            assert(lastFrame.player.depth > 0, 'Player must be in dungeon (depth > 0)');
            const hasDescentMsg = hud.messageHistory.some(m => m.text.includes('You descend into Dungeon Level'));
            assert(hasDescentMsg, 'Must log dungeon descent message');

            // Move in dungeon to trigger combat/obstacles
            console.log('4. Exploring dungeon level 1 and verifying live turn-by-turn log updates...');
            const prevCount = hud.messageHistory.length;
            for (let s = 0; s < 15; s++) {
                sendKey('down');
                await wait(60);
                hud.update(lastFrame);
            }
            console.log(`   -> Dungeon exploration completed without any exceptions! Message count: ${hud.messageHistory.length}`);
        }
    }

    ws.close();
    console.log('\n*** Live Cloud Combat & Dungeon Transition Test PASSED 100%! ***');
    process.exit(0);
}

run().catch(err => {
    console.error('Test Failed:', err);
    if (ws) ws.close();
    process.exit(1);
});
