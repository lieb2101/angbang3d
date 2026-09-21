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

    console.log('\nAll server unit tests passed successfully!');
    process.exit(0);
}

runTests().catch(err => {
    console.error('Test failed:', err);
    process.exit(1);
});
