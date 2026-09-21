/**
 * Angband3D WebSocket Client — Real-Time Game Stream Relay
 * Connects directly to the cloud C engine daemon, measures round-trip latency,
 * and queues user key commands.
 */

class GameNetwork {
    constructor() {
        this.ws = null;
        this.connected = false;
        this.pingMs = 0;
        this.lastPingSent = 0;

        this.onFrame = null;
        this.onHello = null;
        this.onBye = null;
        this.onPing = null;
        this.onStatus = null;
    }

    connect(charName = 'Adventurer', isNew = false) {
        const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
        const host = window.location.host;
        let wsUrl = `${protocol}//${host}/ws?user=${encodeURIComponent(charName)}`;
        if (isNew) {
            wsUrl += '&new=1';
        }

        if (this.onStatus) this.onStatus('Connecting to Cloud Realm...');

        this.ws = new WebSocket(wsUrl);

        this.ws.onopen = () => {
            this.connected = true;
            if (this.onStatus) this.onStatus('Connected to Cloud Realm');
            this.startPingHeartbeat();
        };

        this.ws.onmessage = (event) => {
            const str = event.data.trim();
            if (!str) return;

            try {
                const msg = JSON.parse(str);

                if (msg.t === 'hello') {
                    if (this.onHello) this.onHello(msg);
                } else if (msg.t === 'pong') {
                    if (this.lastPingSent > 0) {
                        this.pingMs = Math.round(performance.now() - this.lastPingSent);
                        if (this.onPing) this.onPing(this.pingMs);
                    }
                } else if (msg.t === 'frame') {
                    if (this.onFrame) this.onFrame(msg);
                } else if (msg.t === 'bye') {
                    if (this.onBye) this.onBye(msg.detail || 'Game session terminated');
                }
            } catch (err) {
                // Ignore raw non-JSON text frames
            }
        };

        this.ws.onerror = (err) => {
            console.error('[WebSocket Error]', err);
            if (this.onStatus) this.onStatus('Network connection error');
        };

        this.ws.onclose = () => {
            this.connected = false;
            if (this.onStatus) this.onStatus('Disconnected from server. Reconnecting in 3s...');
            setTimeout(() => this.connect(charName), 3000);
        };
    }

    startPingHeartbeat() {
        setInterval(() => {
            if (this.connected && this.ws.readyState === WebSocket.OPEN) {
                this.lastPingSent = performance.now();
                this.ws.send(JSON.stringify({ t: 'ping', time: Date.now() }));
            }
        }, 2000);
    }

    sendKey(spec) {
        if (!this.connected || this.ws.readyState !== WebSocket.OPEN) return;
        this.ws.send(`key ${spec}`);
    }

    sendCommand(cmd) {
        if (!this.connected || this.ws.readyState !== WebSocket.OPEN) return;
        this.ws.send(cmd);
    }
}

window.GameNetwork = GameNetwork;
