const fs = require('fs');
const path = require('path');
const assert = require('assert');

console.log('Testing AC and Gold HUD updates and DOM structure:');

// 1. Verify index.html structure
const html = fs.readFileSync(path.join(__dirname, '../server/public/index.html'), 'utf8');

assert.ok(html.includes('id="stats-strip"'), 'index.html must have #stats-strip');
assert.ok(html.includes('id="val-ac"'), 'index.html must have #val-ac');
assert.ok(html.includes('id="val-gold"'), 'index.html must have #val-gold');

// Verify val-ac and val-gold are inside stats-strip
const statsStripStart = html.indexOf('<div id="stats-strip">');
const statsStripEnd = html.indexOf('</div>\n            </div>\n\n            <!-- Center Column');
assert.ok(statsStripStart !== -1 && statsStripEnd !== -1, 'stats-strip bounds found');
const statsStripContent = html.substring(statsStripStart, statsStripEnd);
assert.ok(statsStripContent.includes('id="val-ac"'), '#val-ac must be inside #stats-strip');
assert.ok(statsStripContent.includes('id="val-gold"'), '#val-gold must be inside #stats-strip');
assert.ok(statsStripContent.includes('class="stat-divider"'), 'stat-divider must be inside #stats-strip');

// Verify old Economy & Defense panel is removed
assert.ok(!html.includes('Economy & Defense Panel'), 'Old Economy & Defense Panel should be removed');
console.log('  -> Step 1: index.html DOM structure verified OK');

// 2. Mock DOM for WebHUD
class ElementMock {
    constructor(tagName, id = '') {
        this.tagName = tagName;
        this.id = id;
        this.className = '';
        this.classList = {
            add: () => {},
            remove: () => {},
            contains: () => false,
            toggle: () => false
        };
        this.children = [];
        this.lastElementChild = null;
        this.textContent = '';
        this.style = {};
        this.scrollTop = 0;
        this.scrollHeight = 100;
        this.clientHeight = 100;
        this.title = '';
    }
    getContext() {
        return {
            fillRect: () => {},
            clearRect: () => {},
            beginPath: () => {},
            arc: () => {},
            fill: () => {},
            stroke: () => {},
            moveTo: () => {},
            lineTo: () => {},
            closePath: () => {},
            save: () => {},
            restore: () => {},
            translate: () => {},
            rotate: () => {}
        };
    }
    appendChild(child) {
        this.children.push(child);
        this.lastElementChild = child;
    }
    removeChild() {}
    addEventListener() {}
}

const elements = {
    'val-ac': new ElementMock('span', 'val-ac'),
    'val-gold': new ElementMock('span', 'val-gold'),
    'val-depth': new ElementMock('span', 'val-depth'),
    'hp-fill': new ElementMock('div', 'hp-fill'),
    'hp-text': new ElementMock('span', 'hp-text'),
    'sp-fill': new ElementMock('div', 'sp-fill'),
    'sp-text': new ElementMock('span', 'sp-text'),
    'char-title': new ElementMock('div', 'char-title'),
    'stat-str': new ElementMock('span', 'stat-str'),
    'stat-int': new ElementMock('span', 'stat-int'),
    'stat-wis': new ElementMock('span', 'stat-wis'),
    'stat-dex': new ElementMock('span', 'stat-dex'),
    'stat-con': new ElementMock('span', 'stat-con'),
    'compass-canvas': new ElementMock('canvas', 'compass-canvas'),
    'compass-label': new ElementMock('span', 'compass-label'),
    'message-feed-window': new ElementMock('div', 'message-feed-window'),
    'message-feed-list': new ElementMock('div', 'message-feed-list'),
    'message-feed-scroll': new ElementMock('div', 'message-feed-scroll')
};

global.document = {
    getElementById: (id) => elements[id] || new ElementMock('div', id),
    querySelectorAll: () => [],
    createElement: (tag) => new ElementMock(tag)
};
global.window = {
    getAngbandColorString: () => '#ffd700'
};

const hudCode = fs.readFileSync(path.join(__dirname, '../server/public/js/hud.js'), 'utf8');
const fn = new Function('document', 'window', `${hudCode}; return WebHUD;`);
const WebHUD = fn(global.document, global.window);

const hud = new WebHUD();

// Test standard AC without bonus (e.g. Gauntlets [3,+0] + Iron Helm [7,+0] = AC 10)
hud.update({
    phase: 'play',
    player: {
        name: 'Breth',
        race: 'Half-Troll',
        class: 'Paladin',
        hp: 12,
        hp_max: 19,
        sp: 1,
        sp_max: 1,
        ac: 10,
        ac_base: 10,
        ac_to_a: 0,
        gold: 101,
        stats: {
            str: { use: '58' },
            int: { use: '3' },
            wis: { use: '12' },
            dex: { use: '11' },
            con: { use: '18' }
        }
    }
});

assert.strictEqual(elements['val-ac'].textContent, '10');
assert.strictEqual(elements['val-gold'].textContent, '101');
console.log('  -> Step 2: Standard AC (10) and Gold (101) update correctly OK');

// Test AC with magical bonus (e.g. AC 14 with +4 bonus, and 12500 gold)
hud.update({
    phase: 'play',
    player: {
        name: 'Breth',
        race: 'Half-Troll',
        class: 'Paladin',
        hp: 19,
        hp_max: 19,
        sp: 1,
        sp_max: 1,
        ac: 14,
        ac_base: 10,
        ac_to_a: 4,
        gold: 12500
    }
});

assert.strictEqual(elements['val-ac'].textContent, '14 (+4)');
assert.strictEqual(elements['val-gold'].textContent, '12,500');
console.log('  -> Step 3: Magical bonus AC 14 (+4) and formatted Gold 12,500 update correctly OK');

console.log('\nAll AC and Gold HUD tests passed successfully!');
