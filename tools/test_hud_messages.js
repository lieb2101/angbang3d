const fs = require('fs');
const path = require('path');
const assert = require('assert');

// Simple DOM Mock for WebHUD message feed test
class ElementMock {
    constructor(tagName, id = '') {
        this.tagName = tagName;
        this.id = id;
        this.className = '';
        this.classList = {
            add: (c) => {},
            remove: (c) => {},
            contains: (c) => false,
            toggle: (c) => false
        };
        this.children = [];
        this.lastElementChild = null;
        this.textContent = '';
        this.style = {};
        this.scrollTop = 0;
        this.scrollHeight = 100;
        this.clientHeight = 100;
    }
    getContext(type) {
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
        child.parentNode = this;
    }
    removeChild(child) {
        this.children = this.children.filter(c => c !== child);
        if (this.lastElementChild === child) {
            this.lastElementChild = this.children.length > 0 ? this.children[this.children.length - 1] : null;
        }
    }
    addEventListener(evt, fn) {}
}

const elements = {
    'message-feed-window': new ElementMock('div', 'message-feed-window'),
    'message-feed-list': new ElementMock('div', 'message-feed-list'),
    'message-feed-scroll': new ElementMock('div', 'message-feed-scroll'),
    'msg-more-indicator': new ElementMock('span', 'msg-more-indicator'),
    'btn-msg-size-toggle': new ElementMock('button', 'btn-msg-size-toggle'),
    'btn-msg-scroll-top': new ElementMock('button', 'btn-msg-scroll-top'),
    'btn-msg-scroll-bottom': new ElementMock('button', 'btn-msg-scroll-bottom'),
    'btn-msg-clear': new ElementMock('button', 'btn-msg-clear')
};

global.document = {
    getElementById: (id) => elements[id] || new ElementMock('div', id),
    querySelectorAll: (sel) => [],
    createElement: (tag) => new ElementMock(tag)
};
global.window = {
    getAngbandColorString: (attr) => '#ffd700'
};

// Load WebHUD from hud.js
const hudCode = fs.readFileSync(path.join(__dirname, '../server/public/js/hud.js'), 'utf8');
const fn = new Function('document', 'window', `${hudCode}; return WebHUD;`);
const WebHUD = fn(global.document, global.window);

const hud = new WebHUD();
hud.setupMessageFeed();

console.log('Testing WebHUD message feed:');

// 1. Initial play entry
const frame1 = {
    phase: 'play',
    player: { name: 'Hero', depth: 0 },
    messages: [
        { text: '====================', count: 1, attr: 1 },
        { text: 'You can learn 1 more prayer.', count: 1, attr: 1 }
    ],
    term: { rows: [{ g: '                                        ' }] }
};
hud.update(frame1);

assert.strictEqual(hud.messageHistory.length, 1);
assert.strictEqual(hud.messageHistory[0].text, 'Welcome to the Town of Angband! Visit the General Store and Armory to equip your journey.');
console.log('  -> Step 1: Clean welcome message seeded on play entry OK');

// 2. Bump into a wall
const frame2 = {
    phase: 'play',
    player: { name: 'Hero', depth: 0 },
    messages: [
        { text: 'You can learn 1 more prayer.', count: 1, attr: 1 },
        { text: 'There is a wall in the way!', count: 1, attr: 1 }
    ],
    term: { rows: [{ g: 'There is a wall in the way!             ' }] }
};
hud.update(frame2);

assert.strictEqual(hud.messageHistory.length, 2);
assert.strictEqual(hud.messageHistory[1].text, 'There is a wall in the way!');
console.log('  -> Step 2: New message appended on wall bump OK');

// 3. Bump into wall again (count increments in Angband)
const frame3 = {
    phase: 'play',
    player: { name: 'Hero', depth: 0 },
    messages: [
        { text: 'You can learn 1 more prayer.', count: 1, attr: 1 },
        { text: 'There is a wall in the way!', count: 2, attr: 1 }
    ],
    term: { rows: [{ g: 'There is a wall in the way!             ' }] }
};
hud.update(frame3);

assert.strictEqual(hud.messageHistory.length, 2);
assert.strictEqual(hud.messageHistory[1].repeat, 2);
assert.strictEqual(hud.messageHistory[1].el.textContent, 'There is a wall in the way! (x2)');
console.log('  -> Step 3: Message repeat count updated in-place without duplicating lines OK');

// 4. Combat message (attacking monster)
const frame4 = {
    phase: 'play',
    player: { name: 'Hero', depth: 0 },
    messages: [
        { text: 'There is a wall in the way!', count: 2, attr: 1 },
        { text: 'You strike the giant rat.', count: 1, attr: 1 }
    ],
    term: { rows: [{ g: 'You strike the giant rat.               ' }] }
};
hud.update(frame4);

assert.strictEqual(hud.messageHistory.length, 3);
assert.strictEqual(hud.messageHistory[2].text, 'You strike the giant rat.');
console.log('  -> Step 4: Combat message added cleanly OK');

// 5. Monster counter-attacks on row 0
const frame5 = {
    phase: 'play',
    player: { name: 'Hero', depth: 0 },
    messages: [
        { text: 'There is a wall in the way!', count: 2, attr: 1 },
        { text: 'You strike the giant rat.', count: 1, attr: 1 }
    ],
    term: { rows: [{ g: 'The giant rat bites you! -more-        ' }] }
};
hud.update(frame5);

assert.strictEqual(hud.messageHistory.length, 4);
assert.strictEqual(hud.messageHistory[3].text, 'The giant rat bites you!');
console.log('  -> Step 5: Term row 0 combat alert captured with prompt stripped OK');

console.log('\nAll WebHUD message feed tests passed with 100% success!');
