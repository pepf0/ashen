// REST + WebSocket client. All URLs are relative, so the game also works under a sub-path like /ashen/.

const TOKEN_KEY = "ashen.token";

export function getToken() {
    try { return localStorage.getItem(TOKEN_KEY); } catch { return null; }
}

export function setToken(token) {
    try {
        if (token) localStorage.setItem(TOKEN_KEY, token);
        else localStorage.removeItem(TOKEN_KEY);
    } catch { /* storage blocked: the session just won't survive a reload */ }
}

export async function api(path, { method = "GET", body } = {}) {
    const headers = { "Content-Type": "application/json" };
    const token = getToken();
    if (token) headers.Authorization = `Bearer ${token}`;
    const res = await fetch(`api/${path}`, { method, headers, body: body ? JSON.stringify(body) : undefined });
    let json = null;
    try { json = await res.json(); } catch { /* empty body */ }
    if (!res.ok) {
        const err = new Error(json?.error ?? `Request failed (${res.status})`);
        err.status = res.status;
        throw err;
    }
    return json;
}

/** Game socket with automatic reconnects. Handlers are registered per message type. */
export class GameSocket {
    constructor() {
        this.handlers = {};
        this.retry = 0;
        this.wanted = false;
        this.ws = null;
    }

    on(type, fn) { this.handlers[type] = fn; return this; }

    connect() {
        this.wanted = true;
        if (this.ws && this.ws.readyState <= 1) return;
        const url = new URL("ws", location.href);
        url.protocol = location.protocol === "https:" ? "wss:" : "ws:";
        const ws = new WebSocket(url);
        this.ws = ws;
        ws.onopen = () => {
            this.retry = 0;
            ws.send(JSON.stringify({ type: "auth", token: getToken() }));
            this.handlers.open?.();
        };
        ws.onmessage = ev => {
            const msg = JSON.parse(ev.data);
            if (msg.type === "replaced") { this.wanted = false; }
            this.handlers[msg.type]?.(msg);
        };
        ws.onclose = () => {
            if (this.ws !== ws) return;
            this.handlers.close?.();
            if (!this.wanted) return;
            const delay = Math.min(10000, 500 * 2 ** this.retry++);
            setTimeout(() => this.wanted && this.connect(), delay);
        };
    }

    disconnect() {
        this.wanted = false;
        this.ws?.close();
        this.ws = null;
    }

    send(type, payload = {}) {
        if (this.ws?.readyState === 1) {
            this.ws.send(JSON.stringify({ type, ...payload }));
            return true;
        }
        return false;
    }
}
