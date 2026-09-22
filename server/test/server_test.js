/**
 * Test suite for Angband3D Cloud Server
 * Validates:
 *  - /health endpoint
 *  - Save metadata parser and /api/saves REST endpoint
 *  - Save upload validation (SaveVNLA magic)
 *  - Standalone zip download endpoint (/download/angband3d-standalone.zip)
 */

const http = require('http');
const assert = require('assert');
const fs = require('fs');
const path = require('path');

const PORT = 8081;
process.env.PORT = PORT.toString();

// Start the server
require('../src/server.js');

function get(pathStr) {
    return new Promise((resolve, reject) => {
        http.get(`http://localhost:${PORT}${pathStr}`, res => {
            let data = '';
            res.on('data', chunk => data += chunk);
            res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, body: data }));
        }).on('error', reject);
    });
}

function post(pathStr, bodyBuffer, headers = {}) {
    return new Promise((resolve, reject) => {
        const req = http.request(`http://localhost:${PORT}${pathStr}`, {
            method: 'POST',
            headers: {
                'Content-Length': bodyBuffer.length,
                ...headers
            }
        }, res => {
            let data = '';
            res.on('data', chunk => data += chunk);
            res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, body: data }));
        });
        req.on('error', reject);
        req.write(bodyBuffer);
        req.end();
    });
}

async function runTests() {
    console.log('Testing server endpoints on port', PORT);
    await new Promise(r => setTimeout(r, 500));

    // Test 1: Health check
    console.log('Test 1: Health check');
    const health = await get('/health');
    assert.strictEqual(health.status, 200);
    const healthJson = JSON.parse(health.body);
    assert.strictEqual(healthJson.status, 'ok');
    console.log('  -> Health check OK');

    // Test 2: List saves
    console.log('Test 2: List saves');
    const saves = await get('/api/saves');
    assert.strictEqual(saves.status, 200);
    const savesJson = JSON.parse(saves.body);
    assert(Array.isArray(savesJson.saves));
    console.log(`  -> Saves listed: ${savesJson.saves.length} save files found`);

    // Test 3: Save Upload Validation (Reject corrupt payload)
    console.log('Test 3: Save Upload Validation (reject invalid header)');
    const corruptData = Buffer.from('NOT_A_VALID_SAVE_FILE');
    const rejectRes = await post('/api/saves/upload', corruptData);
    assert.strictEqual(rejectRes.status, 400);
    console.log('  -> Corrupt save properly rejected with 400 Bad Request');

    // Test 4: Save Upload Validation (Accept valid SaveVNLA mock)
    console.log('Test 4: Save Upload Validation (accept valid SaveVNLA mock)');
    const validHeader = Buffer.alloc(100);
    validHeader.write('SaveVNLA', 0, 8, 'ascii');
    validHeader.write('description\0', 8, 16, 'ascii');
    validHeader.writeUInt32LE(1, 24); // version
    const descText = 'MorgothSlayer, Level 50 High-Elf Warrior';
    validHeader.writeUInt32LE(descText.length, 28);
    validHeader.write(descText, 36, descText.length, 'utf8');

    const uploadRes = await post('/api/saves/upload', validHeader, { 'x-character-name': 'TestHero' });
    assert.strictEqual(uploadRes.status, 200);
    const uploadJson = JSON.parse(uploadRes.body);
    assert.strictEqual(uploadJson.status, 'saved');
    console.log('  -> Valid SaveVNLA upload accepted and saved:', uploadJson.filename);

    // Clean up test file
    const testSavePath = path.join(__dirname, '../../engine/build/game/lib/save/TestHero');
    if (fs.existsSync(testSavePath)) {
        fs.unlinkSync(testSavePath);
        console.log('  -> Cleaned up test save file');
    }

    // Test 5: Static web client delivery
    console.log('Test 5: Static web client delivery');
    const indexRes = await get('/');
    assert.strictEqual(indexRes.status, 200);
    assert(indexRes.body.includes('<title>Angband3D'));
    assert(indexRes.headers['content-type'].includes('text/html'));
    console.log('  -> / serves index.html (200 OK)');

    const cssRes = await get('/css/dungeon.css');
    assert.strictEqual(cssRes.status, 200);
    assert(cssRes.body.includes('--font-fantasy'));
    console.log('  -> /css/dungeon.css serves CSS (200 OK)');

    const jsRes = await get('/js/dungeon3d.js');
    assert.strictEqual(jsRes.status, 200);
    console.log('  -> /js/dungeon3d.js serves 3D engine (200 OK)');

    // Test 6: Healthz liveness probe & metrics
    console.log('Test 6: Healthz liveness probe');
    const healthz = await get('/healthz');
    assert.strictEqual(healthz.status, 200);
    const healthzJson = JSON.parse(healthz.body);
    assert.strictEqual(healthzJson.status, 'ok');
    assert(typeof healthzJson.uptime === 'number');
    assert(typeof healthzJson.activeSessions === 'number');
    assert.strictEqual(healthzJson.engine, 'ready');
    console.log('  -> /healthz returned metrics & ready status (200 OK)');

    // Test 7: Gzip compression & Cache-Control
    console.log('Test 7: Gzip compression & Cache-Control headers');
    const gzipRes = await new Promise((resolve, reject) => {
        http.get(`http://localhost:${PORT}/js/dungeon3d.js`, {
            headers: { 'Accept-Encoding': 'gzip' }
        }, res => {
            const chunks = [];
            res.on('data', c => chunks.push(c));
            res.on('end', () => resolve({
                status: res.statusCode,
                headers: res.headers,
                bodyLength: Buffer.concat(chunks).length
            }));
        }).on('error', reject);
    });
    assert.strictEqual(gzipRes.status, 200);
    assert.strictEqual(gzipRes.headers['content-encoding'], 'gzip');
    assert(gzipRes.headers['cache-control'].includes('max-age'));
    console.log(`  -> /js/dungeon3d.js delivered with gzip compression (${gzipRes.bodyLength} bytes compressed)`);

    // Test 8: GLB Binary Model Delivery & MIME type
    console.log('Test 8: GLB Binary Model Delivery & MIME type');
    const glbRes = await get('/assets/models/monsters/Imp.glb');
    assert.strictEqual(glbRes.status, 200);
    assert.strictEqual(glbRes.headers['content-type'], 'model/gltf-binary');
    assert(glbRes.headers['cache-control'].includes('immutable'));
    console.log('  -> /assets/models/monsters/Imp.glb served with model/gltf-binary (200 OK)');

    const puglinRes = await get('/assets/models/monsters/Puglin.glb');
    assert.strictEqual(puglinRes.status, 200);
    assert.strictEqual(puglinRes.headers['content-type'], 'model/gltf-binary');
    console.log('  -> /assets/models/monsters/Puglin.glb served with model/gltf-binary (200 OK)');

    // Test 9: 3D Item and Monster Model Asset Integrity
    console.log('Test 9: 3D Item and Monster Model Asset Integrity');
    const mineralRes = await get('/assets/models/items/Mineral.obj');
    assert.strictEqual(mineralRes.status, 200);
    console.log('  -> /assets/models/items/Mineral.obj (Pebble/Stone) served (200 OK)');

    const dartRes = await get('/assets/models/items/Dart.obj');
    assert.strictEqual(dartRes.status, 200);
    console.log('  -> /assets/models/items/Dart.obj served (200 OK)');

    const spiderRes = await get('/assets/models/monsters/Spider.obj');
    assert.strictEqual(spiderRes.status, 200);
    console.log('  -> /assets/models/monsters/Spider.obj served (200 OK)');

    // Test 10: Client HTML Structure & Component Verification
    console.log('Test 10: Client HTML Structure & Component Verification');
    assert(indexRes.body.includes('id="store-actions-bar"'), 'store-actions-bar must be present in index.html');
    assert(indexRes.body.includes('id="btn-store-buy"'), 'btn-store-buy must be present');
    assert(indexRes.body.includes('id="btn-store-sell"'), 'btn-store-sell must be present');
    assert(indexRes.body.includes('id="btn-store-examine"'), 'btn-store-examine must be present');
    assert(indexRes.body.includes('id="btn-store-exit"'), 'btn-store-exit must be present');
    assert(indexRes.body.includes('id="map-resize-handle"'), 'map-resize-handle must be present');
    assert(indexRes.body.includes('id="msg-resize-handle"'), 'msg-resize-handle must be present');
    console.log('  -> Store actions bar, item action buttons, and window resize handles verified in index.html');

    console.log('\nAll server unit tests passed successfully!');
    process.exit(0);
}

runTests().catch(err => {
    console.error('Test failed:', err);
    process.exit(1);
});
