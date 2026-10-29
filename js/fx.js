// Visual and audio "juice": the drifting ember background, the effects level setting and synthesized sound effects.
// Settings are per device (localStorage), everything works without it.

const store = {
    get(key, fallback) { try { return localStorage.getItem(key) ?? fallback; } catch { return fallback; } },
    set(key, value) { try { localStorage.setItem(key, value); } catch { /* private mode */ } }
};

let level = Number(store.get("ashen.effects", "2"));
let volume = Number(store.get("ashen.volume", "0.5"));
const listeners = new Set();

/** 0 = reduced, 1 = normal, 2 = full. */
export function effectsLevel() { return level; }
export function setEffectsLevel(v) { level = v; store.set("ashen.effects", String(v)); document.body.dataset.fx = String(v); listeners.forEach(f => f()); }
export function getVolume() { return volume; }
export function setVolume(v) { volume = v; store.set("ashen.volume", String(v)); if (master) master.gain.value = v * 0.5; }
export function onFxChange(f) { listeners.add(f); }

// ======================= ember background =======================

export function startEmbers() {
    document.body.dataset.fx = String(level);
    const canvas = document.createElement("canvas");
    canvas.className = "embers";
    canvas.setAttribute("aria-hidden", "true");
    document.body.prepend(canvas);
    const ctx = canvas.getContext("2d");
    let w = 0, h = 0, last = 0, acc = 0;
    const embers = [];
    const resize = () => {
        // Low resolution on purpose: chunky pixels and cheap to draw.
        w = canvas.width = Math.ceil(innerWidth / 3);
        h = canvas.height = Math.ceil(innerHeight / 3);
    };
    resize();
    addEventListener("resize", resize);
    const spawn = first => ({
        x: Math.random() * w, y: first ? Math.random() * h : h + 2,
        vy: 4 + Math.random() * 10, vx: (Math.random() - 0.5) * 3, p: Math.random() * 6,
        life: 6 + Math.random() * 8, hot: Math.random() < 0.35
    });
    const step = ts => {
        requestAnimationFrame(step);
        const dt = Math.min(0.1, (ts - last) / 1000);
        last = ts;
        acc += dt;
        if (acc < 1 / 30 || document.hidden) return; // 30 fps is plenty
        const frame = acc;
        acc = 0;
        const target = level === 0 ? 0 : level === 1 ? 26 : 60;
        while (embers.length < target) embers.push(spawn(embers.length < target / 2));
        if (embers.length > target) embers.length = target;
        ctx.clearRect(0, 0, w, h);
        for (const e of embers) {
            e.life -= frame;
            e.y -= e.vy * frame;
            e.x += (e.vx + Math.sin(ts / 900 + e.p) * 2) * frame;
            if (e.y < -2 || e.life <= 0) Object.assign(e, spawn(false));
            const a = Math.min(1, e.life / 3) * (0.35 + Math.sin(ts / 300 + e.p) * 0.2);
            ctx.fillStyle = e.hot ? `rgba(255, 140, 60, ${a})` : `rgba(190, 180, 220, ${a * 0.7})`;
            ctx.fillRect(Math.round(e.x), Math.round(e.y), 1, 1);
        }
    };
    requestAnimationFrame(step);
}

// ======================= sound =======================

let audio = null, master = null, noiseBuffer = null;
const lastPlayed = new Map();

function ac() {
    if (audio) return audio;
    const Ctx = window.AudioContext || window.webkitAudioContext;
    if (!Ctx) return null;
    audio = new Ctx();
    master = audio.createGain();
    master.gain.value = volume * 0.5;
    const comp = audio.createDynamicsCompressor();
    master.connect(comp).connect(audio.destination);
    noiseBuffer = audio.createBuffer(1, audio.sampleRate * 0.5, audio.sampleRate);
    const d = noiseBuffer.getChannelData(0);
    for (let i = 0; i < d.length; i++) d[i] = Math.random() * 2 - 1;
    return audio;
}

// Browsers only allow audio after a user gesture.
addEventListener("pointerdown", () => { ac()?.resume(); }, { once: false, passive: true });
addEventListener("keydown", () => { ac()?.resume(); }, { passive: true });

function tone({ type = "square", f0, f1 = f0, dur = 0.1, vol = 0.3, delay = 0 }) {
    const t = audio.currentTime + delay;
    const o = audio.createOscillator();
    const g = audio.createGain();
    o.type = type;
    o.frequency.setValueAtTime(f0, t);
    o.frequency.exponentialRampToValueAtTime(Math.max(20, f1), t + dur);
    g.gain.setValueAtTime(vol, t);
    g.gain.exponentialRampToValueAtTime(0.001, t + dur);
    o.connect(g).connect(master);
    o.start(t);
    o.stop(t + dur + 0.02);
}

function noise({ dur = 0.1, vol = 0.3, freq = 2000, q = 1, type = "bandpass", delay = 0, f1 }) {
    const t = audio.currentTime + delay;
    const src = audio.createBufferSource();
    src.buffer = noiseBuffer;
    const filter = audio.createBiquadFilter();
    filter.type = type;
    filter.frequency.setValueAtTime(freq, t);
    if (f1) filter.frequency.exponentialRampToValueAtTime(f1, t + dur);
    filter.Q.value = q;
    const g = audio.createGain();
    g.gain.setValueAtTime(vol, t);
    g.gain.exponentialRampToValueAtTime(0.001, t + dur);
    src.connect(filter).connect(g).connect(master);
    src.start(t);
    src.stop(t + dur + 0.02);
}

const SOUNDS = {
    hit: () => { noise({ dur: 0.08, vol: 0.35, freq: 1400, q: 0.8 }); tone({ type: "square", f0: 180, f1: 70, dur: 0.07, vol: 0.12 }); },
    crit: () => { noise({ dur: 0.14, vol: 0.45, freq: 2600, q: 0.7 }); tone({ type: "sawtooth", f0: 520, f1: 90, dur: 0.16, vol: 0.18 }); },
    smash: () => { noise({ dur: 0.35, vol: 0.6, freq: 500, f1: 80, type: "lowpass" }); tone({ type: "sine", f0: 110, f1: 35, dur: 0.35, vol: 0.5 }); },
    dodge: () => noise({ dur: 0.12, vol: 0.18, freq: 3000, f1: 800, q: 2 }),
    block: () => { tone({ type: "triangle", f0: 1200, f1: 900, dur: 0.18, vol: 0.18 }); tone({ type: "sine", f0: 2400, dur: 0.12, vol: 0.06 }); },
    parry: () => { tone({ type: "square", f0: 1800, f1: 1500, dur: 0.12, vol: 0.12 }); noise({ dur: 0.06, vol: 0.25, freq: 5000 }); },
    stun: () => { for (let k = 0; k < 3; k++) tone({ type: "triangle", f0: 1400 - k * 200, dur: 0.08, vol: 0.1, delay: k * 0.06 }); },
    heal: () => tone({ type: "sine", f0: 520, f1: 880, dur: 0.18, vol: 0.08 }),
    coin: () => { tone({ type: "square", f0: 988, dur: 0.06, vol: 0.08 }); tone({ type: "square", f0: 1319, dur: 0.12, vol: 0.08, delay: 0.06 }); },
    ability: () => { tone({ type: "triangle", f0: 300, f1: 700, dur: 0.15, vol: 0.12 }); noise({ dur: 0.15, vol: 0.08, freq: 1800, f1: 4000 }); },
    ult: () => { tone({ type: "sawtooth", f0: 120, f1: 480, dur: 0.45, vol: 0.18 }); noise({ dur: 0.5, vol: 0.2, freq: 600, f1: 3000 }); tone({ type: "square", f0: 240, f1: 960, dur: 0.4, vol: 0.06, delay: 0.05 }); },
    lightning: () => { noise({ dur: 0.6, vol: 0.6, freq: 3000, f1: 200, type: "lowpass" }); tone({ type: "sawtooth", f0: 90, f1: 40, dur: 0.5, vol: 0.2 }); },
    arrow: () => noise({ dur: 0.07, vol: 0.12, freq: 4000, f1: 1500, q: 3 }),
    note: () => tone({ type: "square", f0: [523, 659, 784, 880][Math.floor(Math.random() * 4)], dur: 0.12, vol: 0.07 }),
    swing: () => noise({ dur: 0.07, vol: 0.08, freq: 1200, f1: 2600, q: 1.5 }),
    buy: () => { tone({ type: "square", f0: 660, dur: 0.05, vol: 0.09 }); tone({ type: "square", f0: 990, dur: 0.1, vol: 0.09, delay: 0.05 }); noise({ dur: 0.05, vol: 0.1, freq: 6000, delay: 0.02 }); },
    levelup: () => [523, 659, 784, 1047].forEach((f, k) => tone({ type: "square", f0: f, dur: 0.1, vol: 0.08, delay: k * 0.06 })),
    reroll: () => { for (let k = 0; k < 5; k++) noise({ dur: 0.03, vol: 0.12, freq: 2500 + k * 300, delay: k * 0.03 }); },
    ready: () => { tone({ type: "triangle", f0: 440, dur: 0.07, vol: 0.12 }); tone({ type: "triangle", f0: 660, dur: 0.12, vol: 0.12, delay: 0.07 }); },
    click: () => tone({ type: "square", f0: 900, dur: 0.025, vol: 0.05 }),
    tick: () => tone({ type: "square", f0: 880, dur: 0.05, vol: 0.1 }),
    go: () => { tone({ type: "square", f0: 523, dur: 0.1, vol: 0.12 }); tone({ type: "square", f0: 1047, dur: 0.25, vol: 0.12, delay: 0.08 }); noise({ dur: 0.3, vol: 0.12, freq: 1500 }); },
    victory: () => [392, 523, 659, 784, 1047].forEach((f, k) => tone({ type: "square", f0: f, dur: k === 4 ? 0.4 : 0.11, vol: 0.1, delay: k * 0.1 })),
    defeat: () => [392, 349, 311, 262].forEach((f, k) => tone({ type: "triangle", f0: f, f1: f * 0.97, dur: k === 3 ? 0.6 : 0.18, vol: 0.13, delay: k * 0.18 })),
    crumble: () => noise({ dur: 0.9, vol: 0.25, freq: 900, f1: 120, type: "lowpass" }),
    announce: () => [784, 988, 1175].forEach((f, k) => tone({ type: "triangle", f0: f, dur: 0.14, vol: 0.09, delay: k * 0.08 })),
    encore: () => {
        [523, 659, 784, 1047, 1319, 1568].forEach((f, k) => tone({ type: "square", f0: f, dur: k === 5 ? 0.5 : 0.1, vol: 0.08, delay: k * 0.07 }));
        [1047, 1319, 1568].forEach(f => tone({ type: "triangle", f0: f, dur: 0.7, vol: 0.06, delay: 0.45 }));
        noise({ dur: 0.6, vol: 0.06, freq: 6000, f1: 9000, q: 1, delay: 0.42 });
    },
    whoosh: () => noise({ dur: 0.25, vol: 0.12, freq: 400, f1: 2400, q: 0.8 })
};

/** Plays a sound effect. Rate-limited per sound so a flurry of hits doesn't turn into noise. */
export function sfx(name, minGap = 0.045) {
    if (volume <= 0 || !SOUNDS[name]) return;
    if (!ac() || audio.state !== "running") return;
    const now = audio.currentTime;
    if (now - (lastPlayed.get(name) ?? -1) < minGap) return;
    lastPlayed.set(name, now);
    try { SOUNDS[name](); } catch { /* ignore audio errors */ }
}
