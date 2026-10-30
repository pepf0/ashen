// Animated pixel arena for live fights, replays and the menu demo. Draws at a low internal resolution
// (scaled up crisply by CSS) so it stays cheap. Damage numbers and labels are DOM floats on top.
import { KIT_COLORS, image } from "./sprites.js";
import { data } from "./data.js";
import { effectsLevel, sfx as playSfx } from "./fx.js";
import { SKINS, skinApplies, skinnedCanvas } from "./skins.js";
import { isPrime, primeIntro, primeCutIn, primeVictory } from "./prime.js";

export const W = 320, H = 144, GROUND = 118;
const RES = 3; // canvas pixels per arena pixel: backgrounds stay chunky, item art stays crisp
const BASE_X = [100, 220];
const CY = GROUND - 30; // height the item clusters hover at

const RANGED = { "generic.bow": "arrow", "pepf.guitar": "note" };
const LASH = { "z7fox.whip": true };

/** Friends' signature sets: a lighter aura in their pack color and their own victory effect. */
const SIG = {
    z7fox: { pal: ["#e0407a", "#ff9ac0", "#ff4e6a", "#ffffff"], word: "RED CARD!" },
    ely7: { pal: ["#a8ff40", "#ffd166", "#fff2a0", "#ffffff"], word: "JACKPOT!" },
    xxz: { pal: ["#8a6aff", "#b26bff", "#d8c8ff", "#ffffff"], word: "OVERLOAD!" }
};
const sigOf = appearance => appearance && Object.keys(SIG).find(k => Object.values(appearance).some(v => typeof v === "string" && v.endsWith(`_${k}`)));

/** Pepf Prime palette (the admin set's aura, trails and cinematics). */
const PRIME = ["#40e0d0", "#7af0e0", "#b8fff6", "#ffffff"];

const PACK_GLOW = { pepf: "#40e0d0", z7fox: "#e0407a", ely7: "#a8ff40", xxzweiradxx: "#8a6aff", generic: "#e8e4ff" };

const MOD_COLORS = {
    rage: "#ff4e3e", bloodlust: "#b0182a", rampage: "#ff9a3d", brace: "#ffd166", smoke: "#a7a3c8", retreat: "#40e0d0",
    hex: "#b26bff", mark: "#ff5a6e", double_down: "#4fd67f", mirror: "#ff6dff", flurry: "#5b8cff"
};

function mulberry(seed) {
    return () => {
        seed |= 0; seed = (seed + 0x6D2B79F5) | 0;
        let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
        t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

const lerp = (a, b, t) => a + (b - a) * t;
const clamp01 = t => Math.max(0, Math.min(1, t));

export class Arena {
    /**
     * fighters: [{ name, kit, weaponId, appearance }] in server order. left: index drawn on the left.
     */
    constructor(root, { fighters, left = 0, arenaId = null, seed = 1, silent = false }) {
        this.silent = silent;
        this.clock = 0;
        this.freezeUntil = 0;
        this.root = root;
        this.canvas = document.createElement("canvas");
        this.canvas.width = W * RES;
        this.canvas.height = H * RES;
        this.canvas.className = "arena-canvas";
        this.floats = document.createElement("div");
        this.floats.className = "arena-floats";
        this.flashEl = document.createElement("div");
        this.flashEl.className = "arena-flash";
        root.replaceChildren(this.canvas, this.flashEl, this.floats);
        this.ctx = this.canvas.getContext("2d");
        this.ctx.imageSmoothingEnabled = false;
        this.arenaId = arenaId ?? "plain";
        this.rng = mulberry(seed);
        this.bg = drawBackground(this.arenaId, mulberry(seed * 7 + 3));
        this.particles = [];
        this.projectiles = [];
        this.rings = [];
        this.stars = [];
        this.bolts = [];
        this.timers = [];
        this.shake = 0;
        this.skyFlash = 0;
        this.overtime = 0;
        this.sudden = false;
        this.ended = false;
        this.f = fighters.map((info, i) => {
            const side = i === left ? 0 : 1;
            const items = (info.items ?? []).map(it => ({ ...it, img: image(`img/item/${data.items[it.id]?.sprite}`), pulse: -9 }));
            const weaponId = items.find(it => it.slot === "weapon")?.id ?? info.weaponId ?? null;
            return {
                items,
                weaponItem: items.find(it => it.slot === "weapon") ?? null,
                shirtItem: items.find(it => it.slot === "shirt") ?? null,
                dyn: items.filter(it => it.slot !== "weapon" && it.slot !== "shirt"),
                heavy: weaponId === "generic.hammer" || weaponId === "xxzweiradxx.staff",
                i, side, dir: side === 0 ? 1 : -1, kit: info.kit ?? "none", info,
                color: KIT_COLORS[info.kit] ?? KIT_COLORS.none,
                weaponId, weapon: weaponId ? image(`img/item/${data.items[weaponId]?.sprite}`) : null,
                skin: skinApplies(weaponId, info.appearance?.weaponSkin) ? info.appearance.weaponSkin : null,
                ranged: RANGED[weaponId] ?? null, lash: !!LASH[weaponId],
                atk: -9, hit: -9, dodge: -9, cast: -9, castColor: "#fff", hop: -9, dead: -9, win: -9,
                hp: 1, maxHp: 1, ghost: 1, shield: 0, stun: 0, invuln: 0, parry: false, windup: null, ult: 0, mods: [],
                ashDone: false, prime: isPrime(info.appearance), primeIntro: false, primeNote: 0,
                sig: isPrime(info.appearance) ? null : SIG[sigOf(info.appearance)] ?? null
            };
        });
        // Pepf Prime entrance card (live fights and replays, not the menu demo).
        const star = this.f.find(f => f.prime);
        this.primeSky = !!star;
        root.classList.toggle("prime-stage", !!star && !silent);
        this.meteors = [];
        if (star && !silent) {
            primeIntro(root, { name: star.info.name ?? "Pepf", kit: star.kit, title: star.info.appearance?.title }, star.side);
            this.sfx("primeIntro");
        }
        this.start = performance.now();
        this.last = this.start;
        this.running = true;
        this.frame = this.frame.bind(this);
        this.raf = requestAnimationFrame(this.frame);
    }

    /** Fight-animation clock: stops briefly on big hits (hit-stop) so they land with weight. */
    get now() { return this.clock; }

    sfx(name, gap) { if (!this.silent) playSfx(name, gap); }

    hitStop(seconds) { if (effectsLevel() > 0) this.freezeUntil = Math.max(this.freezeUntil, performance.now() + seconds * 1000); }

    destroy() {
        this.running = false;
        cancelAnimationFrame(this.raf);
        this.timers.forEach(clearTimeout);
    }

    later(seconds, fn) {
        const id = setTimeout(() => { if (this.running) fn(); }, seconds * 1000);
        this.timers.push(id);
        if (this.timers.length > 64) this.timers.splice(0, this.timers.length - 64);
    }

    // ---------------- positions ----------------

    x(f) { return BASE_X[f.side] + this.offset(f).x; }
    /** Top of the floating item cluster (HP bar, floats). */
    headY(f) { return CY - 28 + this.offset(f).y; }

    /** Offset of the whole cluster: knockback, dodge, ult hop, victory bounce, idle hover. */
    offset(f) {
        const t = this.now;
        let x = 0, y = 0;
        const pa = (t - f.atk) / 0.3;
        if (pa >= 0 && pa < 1 && f.ranged) x -= f.dir * 3 * Math.sin(pa * Math.PI);
        const ph = (t - f.hit) / 0.22;
        if (ph >= 0 && ph < 1) x -= f.dir * 6 * (1 - ph);
        const pd = (t - f.dodge) / 0.4;
        if (pd >= 0 && pd < 1) { x -= f.dir * 18 * Math.sin(pd * Math.PI); y -= 10 * Math.sin(pd * Math.PI); }
        const pj = (t - f.hop) / 0.45;
        if (pj >= 0 && pj < 1) y -= 14 * Math.sin(pj * Math.PI);
        if (f.win > 0 && t > f.win) y -= Math.abs(Math.sin((t - f.win) * 5)) * 12;
        if (f.dead < 0 && !this.ended) y += f.stun > 0 ? 5 : Math.sin(t * 2.2 + f.side * 1.7) * 2.5;
        return { x, y };
    }

    /** Where the weapon is right now (it flies over to strike, then returns). */
    weaponPose(f, cx, cy, t) {
        const home = { x: cx + f.dir * 18, y: cy + 4, a: f.dir * 0.25 + Math.sin(t * 2.6 + f.side) * 0.08 };
        if (f.stun > 0) return { ...home, y: home.y + 6, a: f.dir * 1.3 };
        const pa = (t - f.atk) / (f.heavy ? 0.42 : 0.32);
        if (f.ranged || f.lash || pa < 0 || pa >= 1) {
            if (f.ranged && pa >= 0 && pa < 1) return { ...home, a: home.a - f.dir * 0.4 * Math.sin(pa * Math.PI) };
            if (f.lash && pa >= 0 && pa < 1) return { ...home, x: home.x + f.dir * 6 * Math.sin(pa * Math.PI), a: home.a + f.dir * 1.2 * Math.sin(pa * Math.PI) };
            return home;
        }
        const tgt = this.f[1 - f.i];
        const tx = BASE_X[tgt.side] - f.dir * 14, ty = CY - (f.heavy ? 16 : 2);
        const out = 0.35, strike = 0.5;
        if (pa < out) {
            const k = 1 - Math.pow(1 - pa / out, 3);
            return { x: lerp(home.x, tx, k), y: lerp(home.y, ty, k) - Math.sin(k * Math.PI) * 10, a: home.a - f.dir * 1.0 * k };
        }
        if (pa < strike) {
            const k = (pa - out) / (strike - out);
            return { x: tx, y: ty + (f.heavy ? 18 * k : 4 * k), a: home.a + f.dir * lerp(-1.0, 1.6, k) };
        }
        const k = (pa - strike) / (1 - strike), e = k * k * (3 - 2 * k);
        return { x: lerp(tx, home.x, e), y: lerp(ty, home.y, e), a: lerp(home.a + f.dir * 1.6, home.a, e) };
    }

    /** Positions of every item of a fighter this frame (weapon, shirt, orbiting dynamics). */
    layout(f, t) {
        const off = this.offset(f);
        const cx = BASE_X[f.side] + off.x, cy = CY + off.y;
        const out = [];
        const n = f.dyn.length;
        f.dyn.forEach((it, k) => {
            if (f.prime) {
                // Pepf Prime: a faster, precessing orbit, so the charms swing over and under like a gyroscope.
                const a = t * 1.25 * f.dir + (k / Math.max(1, n)) * Math.PI * 2 + f.side, tilt = t * 0.6;
                const y = Math.sin(a) * 9 * Math.cos(tilt) + Math.cos(a) * 7 * Math.sin(tilt);
                out.push({ it, x: cx - f.dir * 8 + Math.cos(a) * 32, y: cy - 6 + y, size: 20 + Math.sin(a) * 4, a: Math.sin(t * 2.2 + k) * 0.25, depth: Math.sin(a) });
                return;
            }
            const a = t * 0.8 * f.dir + (k / Math.max(1, n)) * Math.PI * 2 + f.side;
            out.push({ it, x: cx - f.dir * 8 + Math.cos(a) * 30, y: cy - 6 + Math.sin(a) * 9, size: 20 + Math.sin(a) * 3, a: Math.sin(t * 1.5 + k) * 0.12, depth: Math.sin(a) });
        });
        if (f.shirtItem) out.push({ it: f.shirtItem, x: cx - f.dir * 8, y: cy - 8 + Math.sin(t * 2 + 1) * (f.prime ? 2.5 : 1), size: 32, a: f.prime ? Math.sin(t * 1.7) * 0.07 : 0, depth: 0 });
        if (f.weaponItem) {
            const w = this.weaponPose(f, cx, cy, t);
            out.push({ it: f.weaponItem, x: w.x, y: w.y, size: 40, a: w.a, depth: 0.5, weapon: true });
        }
        return out.sort((p, q) => p.depth - q.depth);
    }

    /** Briefly enlarges and lights up the item that triggered an effect. */
    pulse(f, itemId, color) {
        const it = f?.items.find(x => x.id === itemId);
        if (!it) return;
        it.pulse = this.now;
        it.pulseColor = color ?? PACK_GLOW[data.items[itemId]?.pack] ?? "#ffffff";
    }

    itemOfEffect(f, effectId) {
        return f?.items.find(x => data.items[x.id]?.effects?.includes(effectId))?.id ?? null;
    }

    // ---------------- input from the fight ----------------

    snap(snap) {
        if (!snap) return;
        this.overtime = snap.overtime ?? 0;
        this.sudden = !!snap.suddenDeath;
        snap.f.forEach((s, i) => {
            const f = this.f[i];
            if (!f) return;
            const hp = Math.max(0, s.hp / s.maxHp);
            if (hp < f.hp - 0.001 && f.ghost < f.hp) f.ghost = f.hp;
            f.hp = hp;
            f.maxHp = s.maxHp;
            f.shield = Math.min(1, s.shield / s.maxHp);
            f.stun = s.stun;
            f.invuln = s.invuln;
            f.parry = s.parry;
            f.bulwark = s.bulwark ?? 0;
            f.windup = s.windup;
            f.ult = s.ult ?? 0;
            f.mods = s.mods ?? [];
        });
    }

    event(ev) {
        const src = this.f[ev.source], tgt = this.f[ev.target];
        switch (ev.kind) {
            case "damage": return this.damage(ev, src, tgt);
            case "stun":
                if (ev.blocked) { this.bubble(tgt, "#ffd166"); this.float(tgt, "Stun blocked", "blocked"); }
                else {
                    this.pulse(src, this.itemOfEffect(src, ev.effectSource), "#ffd166");
                    this.sfx("stun");
                    this.float(tgt, ev.crit ? `Stunned ${ev.duration}s!` : `Stunned ${ev.duration}s`, "stun");
                    this.burst(this.x(tgt), this.headY(tgt), "#ffd166", 10, 1.2);
                }
                break;
            case "buff":
                if (ev.stacks > 0 && ev.itemSource) this.pulse(src, ev.itemSource);
                break;
            case "invulnerability":
                this.pulse(src, this.itemOfEffect(src, ev.effectSource), "#ffd166");
                this.float(src, "Invulnerable", "invuln");
                this.sfx("block");
                this.ring(this.x(src), GROUND - 18, "#40e0d0", 26);
                break;
            case "money":
                if (ev.payout != null) this.float(src, ev.won ? `Chip ×2 (${Math.round(ev.payout)}$)` : `Chip ×0.5 (${Math.round(ev.payout)}$)`, "money");
                else if (ev.gain > 0) this.float(src, `+${Math.round(ev.gain * 10) / 10}$`, "money");
                this.coins(src);
                this.pulse(src, this.itemOfEffect(src, ev.effectSource), "#ffd166");
                this.sfx("coin", 0.15);
                break;
            case "heal":
                if (ev.amount >= 1) this.sfx("heal", 0.3);
                if (ev.amount >= 1) this.float(src, `+${Math.round(ev.amount * 10) / 10}`, "heal");
                for (let k = 0; k < 4; k++) this.particle(this.x(src) + (this.rng() - 0.5) * 20, GROUND - this.rng() * 30, 0, -14, 0.8, "#6be08a", 2, 0);
                break;
            case "ability": {
                if (ev.ability === "second_wind") { this.float(src, "Second Wind!", "ult"); this.ultFx(src, "#6be08a"); break; }
                const a = abilityById(ev.ability);
                this.float(src, a?.name ?? ev.ability, a?.ultimate ? "ult" : "ability");
                this.sfx(a?.ultimate ? "ult" : "ability");
                if (a?.ultimate) this.hitStop(0.12);
                src.cast = this.now;
                src.castColor = MOD_COLORS[ev.ability] ?? src.color;
                if (a?.ultimate) this.ultFx(src, src.prime ? "#40e0d0" : src.color);
                if (a?.ultimate && src.prime && !this.silent) {
                    primeCutIn(this.root, { name: src.info.name ?? "Pepf", kit: src.kit }, a.name, src.side);
                    this.hitStop(0.4);
                    this.sfx("cutin");
                    this.primeNova(src);
                }
                else this.ring(this.x(src), GROUND - 2, src.castColor, 22, true);
                if (ev.ability === "shout") this.ring(this.x(src) + src.dir * 10, this.headY(src) + 10, "#ffffff", 40);
                if (ev.ability === "hex" || ev.ability === "mark") { const t = this.f[1 - src.i]; this.ring(this.x(t), this.headY(t) + 8, src.castColor, 16); }
                if (ev.ability === "smoke") for (let k = 0; k < 14; k++) this.particle(this.x(src) + (this.rng() - 0.5) * 30, GROUND - this.rng() * 34, (this.rng() - 0.5) * 10, -6, 1.4, "#8a86a8", 4, 0);
                if (ev.ability === "jackpot") this.coins(src);
                break;
            }
        }
    }

    damage(ev, src, tgt) {
        const num = (text, kind) => this.float(tgt, text, src?.prime ? `${kind} prime-dmg` : kind);
        const attack = ev.weaponAttack || ev.abilitySource === "lunge" || ev.abilitySource === "flurry";
        let delay = 0;
        if (attack && src) delay = this.attack(src, tgt, ev);
        const show = () => {
            if (ev.suddenDeath) { this.flashHit(tgt, 0.5); this.sfx("hit", 0.25); return; }
            if (ev.abilitySource === "lightning") {
                this.bolt(this.x(tgt));
                this.sfx("lightning");
                if (ev.blocked) { this.bubble(tgt, "#40e0d0"); this.float(tgt, "Blocked", "blocked"); return; }
                this.float(tgt, `Lightning ${Math.round(ev.damage)}`, "stun");
                this.flashHit(tgt, 4);
                return;
            }
            if (ev.blocked) { this.bubble(tgt, "#40e0d0"); this.float(tgt, "Blocked", "blocked"); this.sfx("block"); return; }
            if (ev.parried) {
                this.sfx("parry");
                this.hitStop(0.08);
                this.sparks(this.x(tgt) + tgt.dir * 8, this.headY(tgt) + 18, "#ffffff", 14);
                this.float(tgt, "Parried!", "blocked");
                return;
            }
            if (ev.missed) { this.float(tgt, "Miss", "dodge"); this.sfx("dodge"); return; }
            if (ev.dodged && tgt?.prime) {
                // Pepf Prime dodge: a blink of light, an echo left behind.
                this.afterimage(tgt, "#40e0d0", 0.6);
                this.ring(this.x(tgt), CY - 4, "#b8fff6", 30);
                for (let k = 0; k < 16; k++) this.particle(this.x(tgt) + (this.rng() - 0.5) * 30, CY - 4 + (this.rng() - 0.5) * 26, (this.rng() - 0.5) * 40, -10 - this.rng() * 20, 0.5, PRIME[k % 4], 1, 0, true);
            }
            if (ev.dodged) { tgt.dodge = this.now; this.float(tgt, "Dodge", "dodge"); this.afterimage(tgt); this.sfx("dodge"); return; }
            const dmg = Math.round(ev.damage * 10) / 10;
            if (ev.reflected) {
                this.float(tgt, `${ev.abilitySource === "thorns" ? "Thorns" : "Reflect"} ${dmg}`, "smash");
                this.flashHit(tgt, 1);
                this.sfx("hit");
            } else if (ev.abilitySource === "headshot") {
                this.sfx("crit"); this.sfx("smash");
                this.hitStop(0.16);
                this.beam(src, tgt);
                this.float(tgt, `Headshot ${Math.round(ev.damage)}`, "smash");
                this.flashHit(tgt, 6);
                this.sparks(this.x(tgt), this.headY(tgt) + 6, "#ff5a6e", 24);
            } else if (ev.effectSource) {
                this.pulse(src, this.itemOfEffect(src, ev.effectSource), "#b48cff");
                if (src) { src.atk = this.now; src.heavy = true; }
                this.float(tgt, `Smash ${Math.round(ev.damage)}`, "smash");
                this.sfx("smash");
                this.hitStop(0.14);
                this.ring(this.x(tgt), GROUND - 2, "#b48cff", 46, true);
                this.debris(this.x(tgt));
                this.flashHit(tgt, 5);
            } else if (ev.critMultiplier) {
                num(`${dmg}!`, "crit");
                this.sfx("crit");
                this.hitStop(0.07);
                this.sparks(this.x(tgt), this.headY(tgt) + 16, "#ff9f43", 18);
                this.flashHit(tgt, 3);
            } else {
                num(`${dmg}`, "hit");
                this.sfx("hit");
                this.sparks(this.x(tgt), this.headY(tgt) + 18, src?.color ?? "#ffffff", 6);
                this.flashHit(tgt, 0.8);
            }
            if (src?.prime && !ev.reflected) this.primeImpact(tgt, !!ev.critMultiplier || ev.abilitySource === "headshot");
            if (ev.bulwark > 0) { this.sfx("block"); this.bubble(tgt, "#ffdc78"); this.float(tgt, `-${Math.round(ev.bulwark)} bulwark`, "invuln"); }
            if (ev.absorbed > 0) { this.sfx("block"); this.bubble(tgt, "#cfe8ff"); this.float(tgt, `-${Math.round(ev.absorbed)} shield`, "invuln"); }
        };
        if (delay > 0) this.later(delay, show); else show();
    }

    /** Starts the attack animation; returns how long until it lands (projectiles). */
    attack(f, tgt) {
        f.atk = this.now;
        this.sfx(f.ranged ?? "swing");
        if (f.ranged) {
            const dur = 0.13;
            this.projectiles.push({ kind: f.ranged, x0: this.x(f) + f.dir * 14, y0: this.headY(f) + 22, x1: this.x(tgt), y1: this.headY(tgt) + 18, t0: this.now, dur, dir: f.dir, color: f.color });
            return dur;
        }
        if (f.lash) { this.projectiles.push({ kind: "lash", f, tgt, t0: this.now, dur: 0.22 }); return 0.08; }
        if (!f.weaponItem) return 0;
        return f.heavy ? 0.16 : 0.12;
    }

    flashHit(f, shake) {
        f.hit = this.now;
        if (effectsLevel() > 0) this.shake = Math.max(this.shake, shake);
    }

    afterimage(f, tint = null, life = 0.35) {
        this.particles.push({ ghost: this.layout(f, this.now), life, max: life, tint, dir: f.dir });
    }

    // ---------------- effects ----------------

    float(f, text, kind) {
        if (!f) return;
        const node = document.createElement("div");
        node.className = `afloat ${kind}`;
        node.textContent = text;
        if (kind === "sig" && f.sig) node.style.setProperty("--sig", f.sig.pal[0]);
        const x = (this.x(f) + (this.rng() - 0.5) * 22) / W * 100;
        const lift = { "hit prime-dmg": 4, prime: 30, sig: 26, ability: 16, ult: 18, money: -30, heal: -18, invuln: -10 }[kind] ?? 2;
        const y = (this.headY(f) - lift) / H * 100;
        node.style.left = `${x}%`;
        node.style.top = `${y}%`;
        this.floats.append(node);
        while (this.floats.childElementCount > 18) this.floats.firstElementChild.remove();
        setTimeout(() => node.remove(), 1200);
    }

    particle(x, y, vx, vy, life, color, size = 1, g = 30, glow = false) {
        const cap = effectsLevel() > 0 ? 420 : 120;
        if (this.particles.length >= cap) this.particles.shift();
        this.particles.push({ x, y, vx, vy, life, max: life, color, size, g, glow });
    }

    sparks(x, y, color, n) {
        n = effectsLevel() > 0 ? n : Math.ceil(n / 3);
        for (let k = 0; k < n; k++) {
            const a = this.rng() * Math.PI * 2, sp = 30 + this.rng() * 70;
            this.particle(x, y, Math.cos(a) * sp, Math.sin(a) * sp - 20, 0.25 + this.rng() * 0.35, k % 3 ? color : "#ffffff", 1, 160);
        }
    }

    burst(x, y, color, n, life = 0.8) {
        for (let k = 0; k < n; k++) {
            const a = (k / n) * Math.PI * 2;
            this.particle(x, y, Math.cos(a) * 30, Math.sin(a) * 30, life, color, 1, 0);
        }
    }

    debris(x) {
        for (let k = 0; k < 16; k++)
            this.particle(x + (this.rng() - 0.5) * 30, GROUND - 1, (this.rng() - 0.5) * 80, -40 - this.rng() * 70, 0.7, k % 2 ? "#6a5a7a" : "#3b3350", 2, 220);
    }

    coins(f) {
        for (let k = 0; k < 5; k++)
            this.particle(this.x(f) + (this.rng() - 0.5) * 10, this.headY(f), (this.rng() - 0.5) * 50, -60 - this.rng() * 40, 0.9, "#ffd166", 2, 200);
    }

    ring(x, y, color, radius, flat = false) {
        this.rings.push({ x, y, color, radius, flat, t0: this.now, dur: 0.45 });
    }

    bubble(f, color) { this.rings.push({ x: this.x(f), y: CY - 4, color, radius: 34, bubble: true, t0: this.now, dur: 0.35 }); }

    bolt(x, color = "#fff7a8") {
        const pts = [];
        let bx = x + (this.rng() - 0.5) * 20;
        for (let y = 0; y <= GROUND - 20; y += 8) { pts.push([bx, y]); bx += (this.rng() - 0.5) * 12; }
        pts.push([x, GROUND - 20]);
        this.bolts.push({ pts, t0: this.now, dur: 0.35, color });
        this.skyFlash = 1;
        this.skyRgb = color === "#fff7a8" ? null : "190, 170, 255";
        if (effectsLevel() > 0) this.shake = Math.max(this.shake, 4);
    }

    beam(src, tgt) {
        if (!src) return;
        this.bolts.push({ pts: [[this.x(src) + src.dir * 12, this.headY(src) + 12], [this.x(tgt), this.headY(tgt) + 8]], t0: this.now, dur: 0.3, color: "#ff5a6e", width: 2 });
    }

    ultFx(f, color) {
        f.hop = this.now;
        this.ring(this.x(f), GROUND - 18, color, 60);
        this.ring(this.x(f), GROUND - 2, color, 40, true);
        for (let k = 0; k < 24; k++) this.particle(this.x(f) + (this.rng() - 0.5) * 30, GROUND - this.rng() * 10, (this.rng() - 0.5) * 20, -40 - this.rng() * 50, 1, color, 1, 0);
        this.flashEl.style.setProperty("--flash", color);
        this.flashEl.classList.remove("on");
        void this.flashEl.offsetWidth;
        this.flashEl.classList.add("on");
    }

    /** End of the fight: the loser crumbles to ash, the winner celebrates. winner -1 = draw. */
    finish(winner) {
        if (this.ended) return;
        this.ended = true;
        this.f.forEach(f => {
            if (winner === -1 || f.i !== winner) f.dead = this.now;
            else f.win = this.now + 0.4;
        });
        if (winner >= 0 && this.f[winner].prime && !this.silent && effectsLevel() > 0) {
            // Finisher: slow motion, the camera punches in on the fallen side with a chromatic split.
            const loser = this.f[1 - winner];
            this.root.style.setProperty("--fx", `${(this.x(loser) / W) * 100}%`);
            this.root.classList.remove("pc-finisher");
            void this.root.offsetWidth;
            this.root.classList.add("pc-finisher");
            setTimeout(() => this.root.classList.remove("pc-finisher"), 1300);
            this.hitStop(0.5);
            this.stars.push({ x: this.x(loser), y: CY - 4, t0: this.now, dur: 0.8, r: 50 });
            this.sfx("cutin");
        }
        if (winner >= 0) {
            const w = this.f[winner];
            this.later(0.6, () => this.victoryFx(w));
        }
    }

    victoryFx(f) {
        const effect = f.info.appearance?.victory;
        const x = this.x(f);
        const palettes = {
            victory_confetti: ["#40e0d0", "#e0407a", "#ffd166", "#a8ff40"],
            victory_fireworks: ["#ffd166", "#ff9f43", "#fff7a8"],
            victory_lightning: ["#fff7a8"],
            victory_ghosts: ["#ffffff"],
            victory_pixelrain: ["#c0c0ff", "#8783a8"],
            victory_dragon: ["#ff5a3d", "#ffd166"],
            victory_crown: ["#ffd166"]
        };
        if (effect === "victory_pepf") {
            // Encore: three chord rings, a tidal wave across the whole arena, a fountain of notes from both
            // edges, a firework crown and a turquoise flash.
            this.sfx("encore");
            if (this.silent) this.float(f, "ENCORE!", "prime");
            else primeVictory(this.root, { name: f.info.name ?? "Pepf", title: f.info.appearance?.title });
            for (let r = 0; r < 3; r++) this.later(r * 0.18, () => { this.ring(x, CY, PRIME[r], 40 + r * 25); this.ring(x, GROUND - 2, PRIME[r + 1], 50 + r * 20, true); });
            for (let k = 0; k < 24; k++) this.later(k * 0.05, () => {
                const fromLeft = k % 2 === 0, ex = fromLeft ? 4 : W - 4;
                this.projectiles.push({ kind: "note", x0: ex, y0: GROUND - 4, x1: x + (this.rng() - 0.5) * 120, y1: -10, t0: this.now, dur: 1.3, dir: 1, color: PRIME[k % 4] });
            });
            for (let dx = 0; dx < W; dx += 3) this.later(Math.abs(dx - x) / W * 0.6, () =>
                this.particle(dx, GROUND, 0, -40 - this.rng() * 30, 0.8, PRIME[Math.floor(this.rng() * 4)], 1, 60));
            this.later(0.5, () => {
                const bx = x, by = 26;
                for (let k = 0; k < 36; k++) {
                    const a = (k / 36) * Math.PI * 2;
                    this.particle(bx, by, Math.cos(a) * 60, Math.sin(a) * 40, 1.2, PRIME[k % 4], 1, 30);
                }
                this.sparks(bx, by, "#40e0d0", 20);
            });
            for (let k = 0; k < 50; k++) this.particle(x + (this.rng() - 0.5) * 100, CY + (this.rng() - 0.5) * 40, (this.rng() - 0.5) * 30, -20 - this.rng() * 40, 1.8, PRIME[k % 4], 1, 0);
            this.flashEl.style.setProperty("--flash", "#40e0d0");
            this.flashEl.classList.remove("on");
            void this.flashEl.offsetWidth;
            this.flashEl.classList.add("on");
            return;
        }
        if (effect === "victory_z7fox") {
            // Red Card: a cross of crimson light, a falling red card and magenta embers.
            const pal = SIG.z7fox.pal;
            this.float(f, SIG.z7fox.word, "sig");
            this.ring(x, CY, "#ff4e6a", 60); this.ring(x, GROUND - 2, "#e0407a", 50, true);
            for (let k = 0; k < 40; k++) {
                const arm = k % 4, d = 10 + (k >> 2) * 6;
                const [vx, vy] = [[1, 0], [-1, 0], [0, 1], [0, -1]][arm];
                this.particle(x, CY - 6, vx * d * 2.2, vy * d * 1.6, 0.9, pal[k % 4], 1, 0, true);
            }
            this.projectiles.push({ kind: "card", x0: x + 30, y0: -12, x1: x - 6, y1: CY - 20, t0: this.now, dur: 0.7, dir: 1, color: "#ff2a3a" });
            for (let k = 0; k < 30; k++) this.particle(x + (this.rng() - 0.5) * 110, GROUND, (this.rng() - 0.5) * 10, -30 - this.rng() * 40, 1.6, pal[k % 4], 1, 0, true);
            this.sfx("crit");
            return;
        }
        if (effect === "victory_ely7") {
            // Jackpot: coins fountain out and lucky clovers rain down.
            const pal = SIG.ely7.pal;
            this.float(f, SIG.ely7.word, "sig");
            for (let k = 0; k < 4; k++) this.later(k * 0.15, () => { this.coins(f); this.coins(f); this.sfx("coin"); });
            for (let k = 0; k < 36; k++) this.particle(x + (this.rng() - 0.5) * 140, -this.rng() * 30, (this.rng() - 0.5) * 12, 30 + this.rng() * 30, 2.4, k % 3 ? "#a8ff40" : "#2e6a0a", 2, 0);
            for (let k = 0; k < 24; k++) { const a = (k / 24) * Math.PI * 2; this.particle(x, CY, Math.cos(a) * 55, Math.sin(a) * 40, 0.8, pal[k % 4], 1, 0, true); }
            this.ring(x, CY, "#ffd166", 56);
            return;
        }
        if (effect === "victory_xxz") {
            // Overload: violet lightning strikes around the winner and runes spiral up.
            const pal = SIG.xxz.pal;
            this.float(f, SIG.xxz.word, "sig");
            this.bolt(x - 24, "#d8c8ff"); this.later(0.18, () => this.bolt(x + 22, "#b26bff")); this.later(0.36, () => this.bolt(x, "#ffffff"));
            this.sfx("lightning");
            for (let r = 0; r < 3; r++) this.later(r * 0.15, () => this.ring(x, GROUND - 2, pal[r], 40 + r * 18, true));
            for (let k = 0; k < 40; k++) { const a = (k / 40) * Math.PI * 6; this.later(k * 0.02, () => this.particle(x + Math.cos(a) * 26, GROUND - k * 1.6, -Math.sin(a) * 20, -20, 1, pal[k % 4], 1, 0, true)); }
            return;
        }
        if (effect === "victory_meteor") {
            const tx = x + f.dir * 40;
            this.projectiles.push({ kind: "meteor", x0: tx - 60, y0: -10, x1: tx, y1: GROUND - 4, t0: this.now, dur: 0.5, dir: 1, color: "#ff7a1a" });
            this.later(0.5, () => { this.ring(tx, GROUND - 2, "#ff9a3d", 70, true); this.debris(tx); this.sparks(tx, GROUND - 6, "#ffd166", 30); this.shake = 6; this.sfx("smash"); });
            return;
        }
        if (effect === "victory_souls") {
            for (let k = 0; k < 12; k++) this.later(k * 0.08, () => this.particle(x + (this.rng() - 0.5) * 90, GROUND, (this.rng() - 0.5) * 6, -28, 2.4, k % 2 ? "#7af0e0" : "#c0fff8", 3, -4));
            return;
        }
        if (effect === "victory_ashstorm") {
            for (let k = 0; k < 90; k++) { const a = this.rng() * Math.PI * 2, r = 10 + this.rng() * 50; this.particle(x + Math.cos(a) * r, GROUND - 20 + Math.sin(a) * r * 0.5, -Math.sin(a) * 60, Math.cos(a) * 20 - 10, 1.6, k % 3 ? "#8a8498" : "#ff8a3d", 1, 0); }
            return;
        }
        if (effect === "victory_lightning") { this.bolt(x - 20); this.later(0.25, () => this.bolt(x + 18)); return; }
        if (effect === "victory_fireworks" || effect === "victory_dragon") {
            for (let b = 0; b < 3; b++) this.later(b * 0.35, () => {
                const bx = x + (this.rng() - 0.5) * 80, by = 20 + this.rng() * 30;
                const pal = palettes[effect];
                for (let k = 0; k < 26; k++) {
                    const a = (k / 26) * Math.PI * 2;
                    this.particle(bx, by, Math.cos(a) * 50, Math.sin(a) * 50, 1, pal[k % pal.length], 1, 40);
                }
            });
            return;
        }
        const pal = palettes[effect] ?? [f.color, "#ffffff", "#ffd166"];
        for (let k = 0; k < 40; k++) {
            const fromTop = effect !== "victory_ghosts";
            this.particle(x + (this.rng() - 0.5) * 120, fromTop ? -this.rng() * 30 : GROUND,
                (this.rng() - 0.5) * 20, fromTop ? 30 + this.rng() * 40 : -30 - this.rng() * 30, 2.2, pal[k % pal.length], effect === "victory_ghosts" ? 3 : 2, 0);
        }
    }

    /** Turns the fallen items into drifting ash, pixel by pixel. */
    crumble(f) {
        f.ashDone = true;
        this.sfx("crumble");
        const dense = effectsLevel() > 0;
        for (const p of f.fallen ?? []) {
            const px = pixelsOf(p.it.img, 12);
            if (!px) continue;
            for (let y = 0; y < 12; y++) for (let x = 0; x < 12; x++) {
                const o = (y * 12 + x) * 4;
                if (px[o + 3] < 80 || (!dense && (x + y) % 2)) continue;
                const c = `rgb(${px[o]},${px[o + 1]},${px[o + 2]})`;
                const gx = p.x + (x - 6) * p.size / 12, gy = p.y + (y - 6) * p.size / 12;
                this.particle(gx, gy, (this.rng() - 0.5) * 14, -8 - this.rng() * 26, 1.2 + this.rng() * 1.6, this.rng() < 0.55 ? c : "#6a6478", 1, -6);
            }
        }
    }

    // ---------------- drawing ----------------

    frame(ts) {
        if (!this.running) return;
        const real = Math.min(0.05, (ts - this.last) / 1000);
        this.last = ts;
        const dt = ts < this.freezeUntil ? real * 0.06 : real;
        this.clock += dt;
        if (!this.root.isConnected || this.root.closest("[hidden]")) { this.raf = requestAnimationFrame(this.frame); return; }
        const ctx = this.ctx, t = this.now;
        ctx.save();
        ctx.setTransform(RES, 0, 0, RES, 0, 0);
        ctx.imageSmoothingEnabled = false;
        if (this.shake > 0.1) {
            ctx.translate(Math.round((this.rng() - 0.5) * this.shake), Math.round((this.rng() - 0.5) * this.shake));
            this.shake *= Math.pow(0.002, dt);
        } else this.shake = 0;
        ctx.drawImage(this.bg.canvas, 0, 0);
        this.bg.animate?.(ctx, t, dt, this);
        if (this.skyFlash > 0) {
            ctx.fillStyle = `rgba(${this.skyRgb ?? "255, 250, 220"}, ${this.skyFlash * 0.45})`;
            ctx.fillRect(0, 0, W, H);
            this.skyFlash = Math.max(0, this.skyFlash - dt * 3);
        }

        if (this.primeSky && effectsLevel() > 0) this.drawAurora(ctx, t, dt);
        for (const f of this.f) this.drawUnder(ctx, f, t);
        this.drawEchoes(ctx);
        for (const f of this.f) this.drawFighter(ctx, f, t);
        this.drawProjectiles(ctx, t);
        this.drawRings(ctx, t);
        this.drawStars(ctx, t);
        this.drawBolts(ctx, t);
        this.drawParticles(ctx, dt);
        for (const f of this.f) this.drawOver(ctx, f, t);

        // Overtime / sudden death: a pulsing red edge.
        if (this.overtime || this.sudden) {
            const a = (this.sudden ? 0.35 : 0.15) + Math.sin(t * (this.sudden ? 8 : 3)) * 0.08;
            const g = ctx.createRadialGradient(W / 2, H / 2, H * 0.35, W / 2, H / 2, W * 0.7);
            g.addColorStop(0, "rgba(255, 40, 60, 0)");
            g.addColorStop(1, `rgba(255, 40, 60, ${a})`);
            ctx.fillStyle = g;
            ctx.fillRect(0, 0, W, H);
        }
        ctx.restore();
        this.raf = requestAnimationFrame(this.frame);
    }

    drawUnder(ctx, f, t) {
        if (f.dead >= 0 && t - f.dead > 1.1) return;
        const x = Math.round(this.x(f));
        // Shadow
        ctx.fillStyle = "rgba(0, 0, 0, 0.35)";
        ctx.beginPath();
        ctx.ellipse(x, GROUND + 1, 30, 4, 0, 0, Math.PI * 2);
        ctx.fill();
        if (f.dead >= 0) return;
        if (f.prime) this.drawPrime(ctx, f, t, x);
        else if (f.sig) this.drawSig(ctx, f, t, x);
        // Kit rune circle: slowly turning ticks in the kit's color, brighter with ult charge.
        const glow = 0.25 + Math.min(1, f.ult) * 0.35 + (f.ult >= 1 ? Math.sin(t * 6) * 0.2 : 0);
        ctx.strokeStyle = f.color;
        ctx.globalAlpha = glow;
        ctx.beginPath();
        ctx.ellipse(x, GROUND, 32, 6, 0, 0, Math.PI * 2);
        ctx.stroke();
        ctx.fillStyle = f.color;
        for (let k = 0; k < 10; k++) {
            const a = t * 0.6 * f.dir + (k / 10) * Math.PI * 2;
            ctx.fillRect(Math.round(x + Math.cos(a) * 32), Math.round(GROUND + Math.sin(a) * 6), 1, 1);
        }
        ctx.globalAlpha = 1;
        if (f.ult >= 1 && this.rng() < 0.3) this.particle(x + (this.rng() - 0.5) * 40, GROUND - 2, 0, -24, 0.7, f.color, 1, 0);
        // Active ability buffs: colored motes rising through the cluster.
        for (const m of f.mods) {
            const c = MOD_COLORS[m.id];
            if (!c || m.left <= 0) continue;
            if (this.rng() < (effectsLevel() > 0 ? 0.35 : 0.1))
                this.particle(x + (this.rng() - 0.5) * 40, CY + 10 - this.rng() * 30, 0, -18, 0.6, c, 1, 0);
        }
    }

    /** Pepf Prime aura: swaying light shafts, a turning magic circle, a rolling tide, orbiting stars and notes. */
    drawPrime(ctx, f, t, x) {
        const rich = effectsLevel() > 0;
        const oy = this.offset(f).y;
        if (!f.primeIntro) {
            f.primeIntro = true;
            this.ring(x, CY - 4, "#40e0d0", 46);
            this.ring(x, GROUND, "#b8fff6", 44, true);
            for (let k = 0; k < (rich ? 36 : 12); k++) {
                const a = (k / 36) * Math.PI * 2;
                this.particle(x + Math.cos(a) * 6, CY - 4 + Math.sin(a) * 6, Math.cos(a) * 50, Math.sin(a) * 34 - 10, 1, PRIME[k % 4], 1, 0, true);
            }
        }
        const pulse = 0.5 + Math.sin(t * 2.2) * 0.5;
        ctx.save();
        ctx.globalCompositeOperation = "lighter";
        // Light shafts from above, swaying and fanning out toward the ground.
        for (let k = 0; k < 3; k++) {
            const sway = Math.sin(t * 0.7 + k * 2.1) * 10;
            const top = x + sway + (k - 1) * 8, w0 = 4 + k * 2, w1 = 20 + k * 8;
            const g = ctx.createLinearGradient(0, 0, 0, GROUND);
            g.addColorStop(0, "rgba(64, 224, 208, 0)");
            g.addColorStop(0.55, `rgba(64, 224, 208, ${0.05 + pulse * 0.03})`);
            g.addColorStop(1, `rgba(184, 255, 246, ${0.1 + pulse * 0.06})`);
            ctx.fillStyle = g;
            ctx.beginPath();
            ctx.moveTo(top - w0 / 2, 0);
            ctx.lineTo(top + w0 / 2, 0);
            ctx.lineTo(x + (k - 1) * 12 + w1 / 2, GROUND);
            ctx.lineTo(x + (k - 1) * 12 - w1 / 2, GROUND);
            ctx.closePath();
            ctx.fill();
        }
        // Glow pool on the floor.
        const pool = ctx.createRadialGradient(x, GROUND, 2, x, GROUND, 46);
        pool.addColorStop(0, `rgba(64, 224, 208, ${0.3 + pulse * 0.15})`);
        pool.addColorStop(1, "rgba(64, 224, 208, 0)");
        ctx.fillStyle = pool;
        ctx.save();
        ctx.translate(x, GROUND);
        ctx.scale(1, 0.22);
        ctx.translate(-x, -GROUND);
        ctx.fillRect(x - 46, GROUND - 46, 92, 92);
        ctx.restore();
        // Magic circle on the ground: two counter-turning rings, a star and rune ticks, seen in perspective.
        const R = 36, ry = 0.2;
        const pt = (a, r) => [x + Math.cos(a) * r, GROUND + Math.sin(a) * r * ry];
        ctx.lineWidth = 0.6;
        ctx.strokeStyle = `rgba(122, 240, 224, ${0.55 + pulse * 0.3})`;
        ctx.beginPath(); ctx.ellipse(x, GROUND, R, R * ry, 0, 0, Math.PI * 2); ctx.stroke();
        ctx.strokeStyle = `rgba(184, 255, 246, ${0.35 + pulse * 0.2})`;
        ctx.beginPath(); ctx.ellipse(x, GROUND, R * 0.72, R * 0.72 * ry, 0, 0, Math.PI * 2); ctx.stroke();
        const spin = t * 0.5;
        ctx.strokeStyle = `rgba(64, 224, 208, ${0.4 + pulse * 0.25})`;
        // Hexagram: two triangles.
        ctx.beginPath();
        for (let k = 0; k <= 3; k++) { const [px, py] = pt(spin + (k * 2 * Math.PI) / 3, R * 0.72); if (k === 0) ctx.moveTo(px, py); else ctx.lineTo(px, py); }
        for (let k = 0; k <= 3; k++) { const [px, py] = pt(spin + Math.PI / 3 + (k * 2 * Math.PI) / 3, R * 0.72); if (k === 0) ctx.moveTo(px, py); else ctx.lineTo(px, py); }
        ctx.stroke();
        ctx.fillStyle = "#b8fff6";
        for (let k = 0; k < 16; k++) {
            const [px, py] = pt(-t * 0.8 + (k / 16) * Math.PI * 2, R * 0.86);
            ctx.globalAlpha = 0.5 + 0.5 * Math.sin(t * 4 + k);
            ctx.fillRect(px - 0.5, py - 0.5, k % 4 ? 1 : 2, 1);
        }
        // Equalizer ring: bars dancing around the circle like a speaker, jumping on every hit.
        f.beat = Math.max(0, (f.beat ?? 0) - 0.04);
        for (let k = 0; k < 30; k++) {
            const a = (k / 30) * Math.PI * 2;
            const [px, py] = pt(a, R * 1.12);
            const h = 1.5 + Math.abs(Math.sin(t * 5 + k * 0.9) * Math.sin(t * 2.1 + k * 0.4)) * (5 + f.beat * 10);
            ctx.globalAlpha = 0.35 + 0.4 * (Math.sin(a) > 0 ? 1 : 0.5);
            ctx.fillStyle = PRIME[k % 4];
            ctx.fillRect(px - 0.5, py - h, 1, h);
        }
        ctx.globalAlpha = 1;
        ctx.lineWidth = 1;
        ctx.restore();
        // Rolling tide along the ground.
        for (let dx = -44; dx <= 44; dx++) {
            const fade = 1 - Math.abs(dx) / 45;
            const y1 = GROUND + 3 + Math.sin(dx * 0.25 + t * 3) * 1.5;
            ctx.globalAlpha = fade * 0.9;
            ctx.fillStyle = PRIME[(Math.floor(dx / 3 + t * 6) % 4 + 4) % 4];
            ctx.fillRect(x + dx, Math.round(y1), 1, 1);
        }
        ctx.globalAlpha = 1;
        // Three stars orbiting the cluster with glowing wakes.
        for (let k = 0; k < 3; k++) {
            const a = t * 1.6 + (k / 3) * Math.PI * 2;
            const sx = x + Math.cos(a) * 34, sy = CY - 4 + oy + Math.sin(a) * 12;
            const near = Math.sin(a) > 0;
            ctx.fillStyle = k === 0 ? "#ffffff" : PRIME[k];
            ctx.fillRect(Math.round(sx), Math.round(sy), 1, 1);
            if (near) { ctx.fillRect(Math.round(sx) - 1, Math.round(sy), 3, 1); ctx.fillRect(Math.round(sx), Math.round(sy) - 1, 1, 3); }
            if (rich && this.rng() < 0.5) this.particle(sx, sy, 0, 0, 0.4, PRIME[(k + 1) % 4], 1, 0, true);
        }
        // Echoes: a turquoise afterimage peels off the cluster every half second.
        if (rich && t > (f.primeEcho ?? 0)) { f.primeEcho = t + 0.5; this.afterimage(f, "#40e0d0", 0.45); }
        // Electric arcs flicker from the cluster out to the orbiting stars.
        if (rich && this.rng() < 0.12) {
            const a = t * 1.6 + Math.floor(this.rng() * 3) * (Math.PI * 2 / 3);
            const ex = x + Math.cos(a) * 34, ey = CY - 4 + oy + Math.sin(a) * 12;
            ctx.save();
            ctx.globalCompositeOperation = "lighter";
            ctx.strokeStyle = "rgba(184, 255, 246, 0.85)";
            ctx.lineWidth = 0.7;
            ctx.beginPath();
            ctx.moveTo(x, CY - 4 + oy);
            for (let s = 1; s < 5; s++) ctx.lineTo(lerp(x, ex, s / 5) + (this.rng() - 0.5) * 6, lerp(CY - 4 + oy, ey, s / 5) + (this.rng() - 0.5) * 6);
            ctx.lineTo(ex, ey);
            ctx.stroke();
            ctx.restore();
        }
        // Awakening: below 35% HP the aura flares with a heartbeat.
        if (f.hp < 0.35 && t > (f.primeBeat ?? 0)) {
            f.primeBeat = t + 0.85;
            this.ring(x, CY - 4, "#ffffff", 44);
            this.ring(x, GROUND, "#40e0d0", 50, true);
            f.beat = 1.4;
            for (let k = 0; k < (rich ? 14 : 5); k++) this.particle(x + (this.rng() - 0.5) * 40, GROUND - 2, (this.rng() - 0.5) * 8, -50 - this.rng() * 40, 0.8, PRIME[k % 4], 1, 0, true);
        }
        // Notes drift up out of the cluster.
        if (t > f.primeNote) {
            f.primeNote = t + (rich ? 0.9 : 2) + this.rng() * 0.8;
            const nx = x + (this.rng() - 0.5) * 30;
            this.projectiles.push({ kind: "note", x0: nx, y0: CY + oy, x1: nx + (this.rng() - 0.5) * 30, y1: CY - 50, t0: t, dur: 1.4, dir: 1, color: PRIME[Math.floor(this.rng() * 4)] });
        }
        // Motes rising through the light.
        if (this.rng() < (rich ? 0.45 : 0.1))
            this.particle(x + (this.rng() - 0.5) * 50, GROUND - 1, (this.rng() - 0.5) * 4, -12 - this.rng() * 14, 1.6, PRIME[Math.floor(this.rng() * 4)], 1, 0, true);
    }

    /** Pepf Prime sky: aurora curtains across the top of the arena and the occasional shooting star. */
    drawAurora(ctx, t, dt) {
        ctx.save();
        ctx.globalCompositeOperation = "lighter";
        const bands = [["64, 224, 208", 18, 0], ["122, 160, 255", 30, 1.7], ["184, 255, 246", 10, 3.1]];
        for (const [rgb, base, ph] of bands) {
            for (let x = 0; x < W; x += 2) {
                const y = base + Math.sin(x * 0.018 + t * 0.45 + ph) * 7 + Math.sin(x * 0.051 - t * 0.8 + ph) * 3;
                const a = (0.06 + 0.05 * Math.sin(x * 0.03 + t * 1.3 + ph)) * (0.6 + 0.4 * Math.sin(t * 0.7 + ph));
                if (a <= 0) continue;
                // Stepped falloff instead of a gradient per strip (cheap enough for every frame).
                ctx.fillStyle = `rgba(${rgb}, ${a * 0.5})`; ctx.fillRect(x, y - 3, 2, 3);
                ctx.fillStyle = `rgba(${rgb}, ${a})`; ctx.fillRect(x, y, 2, 6);
                ctx.fillStyle = `rgba(${rgb}, ${a * 0.55})`; ctx.fillRect(x, y + 6, 2, 8);
                ctx.fillStyle = `rgba(${rgb}, ${a * 0.25})`; ctx.fillRect(x, y + 14, 2, 10);
            }
        }
        if (this.rng() < dt * 0.35) this.meteors.push({ x: 40 + this.rng() * (W - 80), y: -4, vx: -60 - this.rng() * 40, vy: 40 + this.rng() * 20, life: 0.7 });
        this.meteors = this.meteors.filter(m => (m.life -= dt) > 0);
        for (const m of this.meteors) {
            m.x += m.vx * dt; m.y += m.vy * dt;
            const g = ctx.createLinearGradient(m.x, m.y, m.x - m.vx * 0.18, m.y - m.vy * 0.18);
            g.addColorStop(0, `rgba(255, 255, 255, ${m.life})`);
            g.addColorStop(1, "rgba(64, 224, 208, 0)");
            ctx.strokeStyle = g;
            ctx.lineWidth = 1;
            ctx.beginPath(); ctx.moveTo(m.x, m.y); ctx.lineTo(m.x - m.vx * 0.18, m.y - m.vy * 0.18); ctx.stroke();
        }
        ctx.restore();
    }

    /** Tinted echoes (Pepf Prime): drift back and up, fading in then out, behind the real items. */
    drawEchoes(ctx) {
        for (const p of this.particles) {
            if (!p.ghost || !p.tint) continue;
            const k = 1 - p.life / p.max;
            for (const g of p.ghost)
                this.drawItem(ctx, { ...g, x: g.x - p.dir * (4 + k * 14), y: g.y - k * 4 }, this.now,
                    { alpha: Math.sin(k * Math.PI) * 0.45, f: g.weapon ? this.f.find(f => f.weaponItem === g.it) : null, tint: p.tint });
        }
    }

    /** Signature aura: a glowing ring in the friend's color and motes rising from it. */
    drawSig(ctx, f, t, x) {
        const [c0, c1] = f.sig.pal;
        const pulse = 0.5 + Math.sin(t * 2.4) * 0.5;
        ctx.save();
        ctx.globalCompositeOperation = "lighter";
        const pool = ctx.createRadialGradient(x, GROUND, 1, x, GROUND, 36);
        pool.addColorStop(0, c0 + "55");
        pool.addColorStop(1, c0 + "00");
        ctx.fillStyle = pool;
        ctx.translate(x, GROUND); ctx.scale(1, 0.22); ctx.translate(-x, -GROUND);
        ctx.fillRect(x - 36, GROUND - 36, 72, 72);
        ctx.restore();
        ctx.strokeStyle = c1;
        ctx.globalAlpha = 0.45 + pulse * 0.35;
        ctx.beginPath(); ctx.ellipse(x, GROUND, 28 + pulse * 2, 5 + pulse * 0.5, 0, 0, Math.PI * 2); ctx.stroke();
        ctx.globalAlpha = 1;
        if (this.rng() < (effectsLevel() > 0 ? 0.25 : 0.06))
            this.particle(x + (this.rng() - 0.5) * 44, GROUND - 1, 0, -14 - this.rng() * 10, 1.3, f.sig.pal[Math.floor(this.rng() * 4)], 1, 0, true);
    }

    /** Ribbon of light behind a Pepf Prime weapon (additive, fades along its length). */
    drawPrimeTrail(ctx, f, w, t) {
        f.trail ??= [];
        f.trail.push({ x: w.x, y: w.y, t });
        while (f.trail.length && t - f.trail[0].t > 0.22) f.trail.shift();
        if (f.trail.length < 3) return;
        ctx.save();
        ctx.globalCompositeOperation = "lighter";
        ctx.lineCap = "round";
        for (let i = 1; i < f.trail.length; i++) {
            const a = f.trail[i - 1], b = f.trail[i], k = i / f.trail.length;
            const speed = Math.hypot(b.x - a.x, b.y - a.y);
            if (speed < 0.3) continue;
            ctx.strokeStyle = `rgba(64, 224, 208, ${0.35 * k})`;
            ctx.lineWidth = 4 * k;
            ctx.beginPath(); ctx.moveTo(a.x, a.y); ctx.lineTo(b.x, b.y); ctx.stroke();
            ctx.strokeStyle = `rgba(255, 255, 255, ${0.6 * k})`;
            ctx.lineWidth = 1.2 * k;
            ctx.beginPath(); ctx.moveTo(a.x, a.y); ctx.lineTo(b.x, b.y); ctx.stroke();
        }
        ctx.restore();
    }

    /** A Pepf Prime hit: a shockwave, a star flash and glowing shards (bigger on crits). */
    primeImpact(tgt, big) {
        if (!tgt) return;
        const me = this.f[1 - tgt.i];
        if (me) me.beat = Math.min(1.6, (me.beat ?? 0) + (big ? 1 : 0.5));
        const x = this.x(tgt), y = this.headY(tgt) + 16;
        this.ring(x, y, big ? "#ffffff" : "#7af0e0", big ? 34 : 20);
        if (big) { this.ring(x, GROUND - 2, "#40e0d0", 40, true); this.shake = Math.max(this.shake, 3); }
        this.stars.push({ x, y, t0: this.now, dur: big ? 0.35 : 0.2, r: big ? 14 : 8 });
        const n = effectsLevel() > 0 ? (big ? 18 : 8) : 4;
        for (let k = 0; k < n; k++) {
            const a = this.rng() * Math.PI * 2, sp = 30 + this.rng() * (big ? 90 : 50);
            this.particle(x, y, Math.cos(a) * sp, Math.sin(a) * sp - 10, 0.3 + this.rng() * 0.3, PRIME[k % 4], 1, 80, true);
        }
    }

    /** Ultimate nova: a dome of light bursting out of the Prime cluster. */
    primeNova(f) {
        const x = this.x(f);
        for (let r = 0; r < 4; r++) this.later(r * 0.08, () => this.ring(x, CY - 4, PRIME[r], 50 + r * 30));
        this.stars.push({ x, y: CY - 4, t0: this.now, dur: 0.6, r: 40 });
        for (let k = 0; k < (effectsLevel() > 0 ? 60 : 20); k++) {
            const a = (k / 60) * Math.PI * 2;
            this.particle(x, CY - 4, Math.cos(a) * 90, Math.sin(a) * 50, 0.9, PRIME[k % 4], 1, 0, true);
        }
        this.shake = Math.max(this.shake, 5);
    }

    /** Four-pointed lens flares (additive), used by Prime hits and the nova. */
    drawStars(ctx, t) {
        this.stars = this.stars.filter(s => t - s.t0 < s.dur);
        if (!this.stars.length) return;
        ctx.save();
        ctx.globalCompositeOperation = "lighter";
        for (const s of this.stars) {
            const k = (t - s.t0) / s.dur, r = s.r * (0.4 + k * 0.8), a = 1 - k;
            const g = ctx.createRadialGradient(s.x, s.y, 0, s.x, s.y, r);
            g.addColorStop(0, `rgba(255, 255, 255, ${0.9 * a})`);
            g.addColorStop(0.3, `rgba(122, 240, 224, ${0.5 * a})`);
            g.addColorStop(1, "rgba(64, 224, 208, 0)");
            ctx.fillStyle = g;
            ctx.beginPath(); ctx.arc(s.x, s.y, r, 0, Math.PI * 2); ctx.fill();
            ctx.fillStyle = `rgba(255, 255, 255, ${0.8 * a})`;
            ctx.fillRect(s.x - r * 1.6, s.y - 0.4, r * 3.2, 0.8);
            ctx.fillRect(s.x - 0.4, s.y - r * 1.1, 0.8, r * 2.2);
        }
        ctx.restore();
    }

    /** A small floating pixel crown over the HP bar of a Pepf Prime cluster. */
    drawPrimeCrown(ctx, x, top, t) {
        const y = Math.round(top - 14 + Math.sin(t * 2.5) * 1.5);
        const shine = Math.floor(t * 4) % 7;
        const rows = ["1.1.1", "11111", "11111"];
        rows.forEach((row, ry) => [...row].forEach((c, rx) => {
            if (c !== "1") return;
            ctx.fillStyle = rx === shine ? "#ffffff" : ry === 0 ? "#b8fff6" : "#40e0d0";
            ctx.fillRect(x - 2 + rx, y + ry, 1, 1);
        }));
        ctx.fillStyle = "#ffffff";
        ctx.fillRect(x, y + 1, 1, 1);
    }

    drawFighter(ctx, f, t) {
        if (f.dead >= 0) return this.drawFalling(ctx, f, t);
        const smoke = f.mods.some(m => (m.id === "smoke" || m.id === "retreat") && m.left > 0);
        const flash = t - f.hit < 0.09;
        const placed = this.layout(f, t);
        f.lastLayout = placed;
        // Motion trail behind a flying weapon.
        const w = placed.find(p => p.weapon);
        const pa = (t - f.atk) / (f.heavy ? 0.42 : 0.32);
        if (w && !f.ranged && !f.lash && pa > 0 && pa < 0.6) {
            for (let k = 3; k >= 1; k--) {
                const back = this.weaponPose(f, BASE_X[f.side] + this.offset(f).x, CY + this.offset(f).y, t - k * 0.025);
                this.drawItem(ctx, { ...w, x: back.x, y: back.y, a: back.a }, t, { alpha: 0.18 * (4 - k) / 3, f });
            }
        }
        if (w && f.prime) this.drawPrimeTrail(ctx, f, w, t);
        for (const p of placed) this.drawItem(ctx, p, t, { alpha: smoke ? 0.5 : f.invuln > 0 ? 0.8 + Math.sin(t * 20) * 0.2 : 1, flash, f });
        if (w && f.skin && SKINS[f.skin] && this.rng() < 0.25)
            this.particle(w.x + (this.rng() - 0.5) * 14, w.y + (this.rng() - 0.5) * 14, 0, -10, 0.6, SKINS[f.skin].glow, 1, 0);
        if (w && f.parry && Math.sin(t * 18) > 0) { ctx.fillStyle = "#ffffff"; ctx.fillRect(Math.round(w.x + f.dir * 8), Math.round(w.y - 10), 2, 2); }
    }

    /** One floating item: soft glow in its pack color, a pop when its effect fires, white on hits. */
    drawItem(ctx, p, t, { alpha = 1, flash = false, f, tint = null }) {
        const it = p.it;
        const img = p.weapon && f?.skin ? (skinnedCanvas(it.id, f.skin) ?? it.img) : it.img;
        if (!img || (img instanceof HTMLImageElement && (!img.complete || !img.naturalWidth))) return;
        const pk = (t - it.pulse) / 0.45;
        const pop = pk >= 0 && pk < 1 ? Math.sin(pk * Math.PI) : 0;
        const size = p.size * (1 + pop * 0.35);
        ctx.save();
        ctx.globalAlpha = alpha;
        ctx.translate(p.x, p.y);
        ctx.rotate(p.a ?? 0);
        if (p.weapon && f && f.dir < 0) ctx.scale(-1, 1);
        if (tint) {
            // Echo: a flat silhouette in one color (Pepf Prime afterimages).
            ctx.globalCompositeOperation = "lighter";
            ctx.imageSmoothingEnabled = true;
            ctx.drawImage(tintOf(img, tint), -size / 2, -size / 2, size, size);
            ctx.imageSmoothingEnabled = false;
            ctx.restore();
            return;
        }
        // Pack-colored aura behind the item.
        const aura = PACK_GLOW[data.items[it.id]?.pack] ?? "#e8e4ff";
        ctx.globalAlpha = alpha * (0.16 + Math.sin(t * 3 + p.size) * 0.05);
        ctx.fillStyle = aura;
        ctx.beginPath();
        ctx.arc(0, 0, size * 0.5, 0, Math.PI * 2);
        ctx.fill();
        ctx.globalAlpha = alpha;
        if (pop > 0) {
            ctx.globalAlpha = alpha * pop * 0.7;
            ctx.fillStyle = it.pulseColor ?? "#fff";
            ctx.beginPath();
            ctx.arc(0, 0, size * 0.62, 0, Math.PI * 2);
            ctx.fill();
            ctx.globalAlpha = alpha;
        }
        ctx.imageSmoothingEnabled = true;
        // Dark outline so items read on any background.
        const dark = tintOf(img, "#05020c"), o = 0.7;
        ctx.globalAlpha = alpha * 0.85;
        for (const [dx, dy] of [[-o, 0], [o, 0], [0, -o], [0, o]]) ctx.drawImage(dark, -size / 2 + dx, -size / 2 + dy, size, size);
        ctx.globalAlpha = alpha;
        ctx.drawImage(img, -size / 2, -size / 2, size, size);
        if (flash) {
            ctx.globalAlpha = alpha * 0.85;
            ctx.drawImage(tintOf(img, "#ffffff"), -size / 2, -size / 2, size, size);
        }
        ctx.imageSmoothingEnabled = false;
        ctx.restore();
    }

    /** Defeat: the items drop to the ground, bounce once, then crumble to ash. */
    drawFalling(ctx, f, t) {
        const age = t - f.dead;
        if (!f.fallen) f.fallen = (f.lastLayout ?? this.layout(f, t)).map(p => ({ ...p, vx: (this.rng() - 0.5) * 30 - f.dir * 10, vy: -30 - this.rng() * 30, va: (this.rng() - 0.5) * 8 }));
        if (age > 1.1) { if (!f.ashDone) this.crumble(f); return; }
        const dt = Math.min(0.05, age - (f.lastFall ?? age));
        f.lastFall = age;
        for (const p of f.fallen) {
            p.vy += 260 * dt;
            p.x += p.vx * dt;
            p.y += p.vy * dt;
            p.a += p.va * dt;
            const floor = GROUND - p.size * 0.3;
            if (p.y > floor) { p.y = floor; p.vy *= -0.35; p.vx *= 0.6; p.va *= 0.5; }
            this.drawItem(ctx, p, t, { alpha: age > 0.8 ? 1 - (age - 0.8) / 0.3 : 1, f });
        }
    }

    drawOver(ctx, f, t) {
        if (f.dead >= 0) return;
        const x = Math.round(this.x(f)), top = Math.round(this.headY(f));
        // Mini HP bar above the head.
        const bw = 30, bx = x - bw / 2, by = top - 7;
        ctx.fillStyle = "#0b0618";
        ctx.fillRect(bx - 1, by - 1, bw + 2, 5);
        ctx.fillStyle = "#5a1a2a";
        ctx.fillRect(bx, by, bw, 3);
        f.ghost = Math.max(f.hp, f.ghost - 0.012);
        ctx.fillStyle = "#ff8a6e";
        ctx.fillRect(bx, by, Math.round(bw * f.ghost), 3);
        ctx.fillStyle = f.hp < 0.3 ? "#ffb347" : f.side === 0 ? "#4fdf8f" : "#ff5a6e";
        ctx.fillRect(bx, by, Math.round(bw * f.hp), 3);
        if (f.shield > 0) { ctx.fillStyle = "#dff2ff"; ctx.fillRect(bx, by - 2, Math.max(1, Math.round(bw * f.shield)), 1); }
        if (f.prime) this.drawPrimeCrown(ctx, x, top, t);
        // Ult charge pips under the bar.
        ctx.fillStyle = f.ult >= 1 ? f.color : "rgba(255,255,255,0.25)";
        ctx.fillRect(bx, by + 4, Math.round(bw * Math.min(1, f.ult)), 1);

        // Stun stars
        if (f.stun > 0) for (let k = 0; k < 3; k++) {
            const a = t * 5 + k * 2.1;
            ctx.fillStyle = k % 2 ? "#ffd166" : "#fff7a8";
            ctx.fillRect(Math.round(x + Math.cos(a) * 11), Math.round(top + 2 + Math.sin(a) * 3), 2, 2);
        }
        // Invulnerability / shield bubble
        if (f.invuln > 0 || f.bulwark > 0 || f.shield > 0) {
            ctx.strokeStyle = f.invuln > 0 || f.bulwark > 0 ? `rgba(255, 220, 120, ${0.6 + Math.sin(t * 10) * 0.25})` : `rgba(190, 230, 255, ${0.25 + f.shield})`;
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.ellipse(x, CY - 4 + this.offset(f).y, 38, 30, 0, 0, Math.PI * 2);
            ctx.stroke();
        }
        // Headshot wind-up: a red laser sight on the enemy that blinks faster as it charges.
        if (f.windup != null && f.windup > 0) {
            const tgt = this.f[1 - f.i];
            if (Math.sin(t * (10 + (2 - f.windup) * 18)) > -0.2) {
                ctx.strokeStyle = "rgba(255, 60, 80, 0.7)";
                ctx.beginPath();
                ctx.moveTo(x + f.dir * 12, top + 12);
                ctx.lineTo(this.x(tgt), this.headY(tgt) + 8);
                ctx.stroke();
                ctx.fillStyle = "#ff3c50";
                ctx.fillRect(Math.round(this.x(tgt)) - 1, Math.round(this.headY(tgt)) + 7, 3, 3);
            }
        }
        // Cast rune under the fighter right after an ability.
        const pc = (t - f.cast) / 0.5;
        if (pc >= 0 && pc < 1) {
            ctx.strokeStyle = f.castColor;
            ctx.globalAlpha = 1 - pc;
            ctx.beginPath();
            ctx.ellipse(x, GROUND, 8 + pc * 16, 2 + pc * 4, 0, 0, Math.PI * 2);
            ctx.stroke();
            ctx.globalAlpha = 1;
        }
    }

    drawProjectiles(ctx, t) {
        this.projectiles = this.projectiles.filter(p => t - p.t0 < p.dur);
        for (const p of this.projectiles) {
            const k = clamp01((t - p.t0) / p.dur);
            if (p.kind === "lash") {
                const x0 = this.x(p.f) + p.f.dir * 12, y0 = this.headY(p.f) + 22;
                const x1 = this.x(p.tgt), y1 = this.headY(p.tgt) + 18;
                const reach = k < 0.5 ? k * 2 : 1;
                ctx.strokeStyle = "#c97a3a";
                ctx.lineWidth = 1;
                ctx.beginPath();
                ctx.moveTo(x0, y0);
                ctx.quadraticCurveTo((x0 + x1) / 2, y0 - 22 * (1 - k), lerp(x0, x1, reach), lerp(y0, y1, reach));
                ctx.stroke();
                continue;
            }
            const x = lerp(p.x0, p.x1, k), y = lerp(p.y0, p.y1, k) - Math.sin(k * Math.PI) * (p.kind === "note" ? 10 : 4);
            if (p.kind === "card") {
                ctx.save();
                ctx.translate(Math.round(x), Math.round(y));
                ctx.rotate(Math.sin(k * 9) * 0.4);
                ctx.fillStyle = "#05020c"; ctx.fillRect(-5, -7, 10, 14);
                ctx.fillStyle = p.color; ctx.fillRect(-4, -6, 8, 12);
                ctx.fillStyle = "#ff8a9a"; ctx.fillRect(-4, -6, 8, 1);
                ctx.restore();
                continue;
            }
            if (p.kind === "meteor") {
                ctx.fillStyle = "#ffd166"; ctx.fillRect(Math.round(x) - 2, Math.round(y) - 2, 5, 5);
                ctx.fillStyle = "#ff5a1a"; ctx.fillRect(Math.round(x) - 3, Math.round(y) - 1, 7, 3);
                this.particle(x, y, -20, -10, 0.4, this.rng() < 0.5 ? "#ff7a1a" : "#ffd166", 2, 0);
                continue;
            }
            if (p.kind === "arrow") {
                ctx.fillStyle = "#e8dcc8";
                ctx.fillRect(Math.round(x - 5 * p.dir), Math.round(y), 8, 1);
                ctx.fillStyle = "#9aa0ad";
                ctx.fillRect(Math.round(x + 3 * p.dir), Math.round(y) - 1, 2, 3);
            } else {
                // Music note
                ctx.fillStyle = p.color;
                ctx.fillRect(Math.round(x), Math.round(y), 3, 2);
                ctx.fillRect(Math.round(x) + 2, Math.round(y) - 6, 1, 7);
                ctx.fillRect(Math.round(x) + 3, Math.round(y) - 6, 2, 1);
                if (this.rng() < 0.5) this.particle(x, y, 0, 0, 0.25, p.color, 1, 0);
            }
        }
    }

    drawRings(ctx, t) {
        this.rings = this.rings.filter(r => t - r.t0 < r.dur);
        for (const r of this.rings) {
            const k = (t - r.t0) / r.dur;
            ctx.globalAlpha = 1 - k;
            ctx.strokeStyle = r.color;
            ctx.lineWidth = r.bubble ? 2 : 1;
            ctx.beginPath();
            if (r.bubble) ctx.ellipse(r.x, r.y, r.radius, r.radius + 3, 0, 0, Math.PI * 2);
            else if (r.flat) ctx.ellipse(r.x, r.y, r.radius * k + 4, (r.radius * k + 4) * 0.25, 0, 0, Math.PI * 2);
            else ctx.arc(r.x, r.y, r.radius * (0.2 + k * 0.8), 0, Math.PI * 2);
            ctx.stroke();
        }
        ctx.globalAlpha = 1;
        ctx.lineWidth = 1;
    }

    drawBolts(ctx, t) {
        this.bolts = this.bolts.filter(b => t - b.t0 < b.dur);
        for (const b of this.bolts) {
            const k = (t - b.t0) / b.dur;
            ctx.globalAlpha = 1 - k;
            ctx.strokeStyle = b.color;
            ctx.lineWidth = (b.width ?? 2) * (1 - k * 0.5);
            ctx.beginPath();
            b.pts.forEach(([x, y], i) => (i ? ctx.lineTo(x, y) : ctx.moveTo(x, y)));
            ctx.stroke();
        }
        ctx.globalAlpha = 1;
        ctx.lineWidth = 1;
    }

    drawParticles(ctx, dt) {
        const keep = [];
        for (const p of this.particles) {
            p.life -= dt;
            if (p.life <= 0) continue;
            keep.push(p);
            if (p.ghost) {
                if (p.tint) continue; // drawn behind the clusters by drawEchoes
                for (const g of p.ghost) this.drawItem(ctx, g, this.now, { alpha: (p.life / p.max) * 0.35, f: g.weapon ? this.f.find(f => f.weaponItem === g.it) : null });
                continue;
            }
            p.vy += p.g * dt;
            p.x += p.vx * dt;
            p.y += p.vy * dt;
            ctx.globalAlpha = Math.min(1, (p.life / p.max) * 1.6);
            ctx.fillStyle = p.color;
            if (p.glow) {
                // Additive halo: reads as bloom on the dark arenas.
                ctx.globalCompositeOperation = "lighter";
                ctx.globalAlpha *= 0.16;
                ctx.beginPath();
                ctx.arc(p.x + p.size / 2, p.y + p.size / 2, p.size * 1.8, 0, Math.PI * 2);
                ctx.fill();
                ctx.globalAlpha = Math.min(1, (p.life / p.max) * 1.6);
                ctx.fillRect(p.x, p.y, p.size, p.size);
                ctx.globalCompositeOperation = "source-over";
                continue;
            }
            ctx.fillRect(Math.round(p.x), Math.round(p.y), p.size, p.size);
        }
        ctx.globalAlpha = 1;
        this.particles = keep;
    }
}

const tints = new Map();
/** Single-color silhouette of an image (hit flash, outline). */
function tintOf(img, color) {
    let byColor = tints.get(color);
    if (!byColor) tints.set(color, byColor = new WeakMap());
    if (byColor.has(img)) return byColor.get(img);
    const c = document.createElement("canvas");
    c.width = img.naturalWidth ?? img.width;
    c.height = img.naturalHeight ?? img.height;
    const x = c.getContext("2d");
    x.drawImage(img, 0, 0);
    x.globalCompositeOperation = "source-atop";
    x.fillStyle = color;
    x.fillRect(0, 0, c.width, c.height);
    byColor.set(img, c);
    return c;
}

const pixelCache = new WeakMap();
/** An image shrunk to n x n, as RGBA bytes (for the ash effect). */
function pixelsOf(img, n) {
    if (!img?.complete || !img.naturalWidth) return null;
    if (pixelCache.has(img)) return pixelCache.get(img);
    const c = document.createElement("canvas");
    c.width = c.height = n;
    const x = c.getContext("2d");
    x.drawImage(img, 0, 0, n, n);
    const d = x.getImageData(0, 0, n, n).data;
    pixelCache.set(img, d);
    return d;
}

function abilityById(id) {
    for (const kit of Object.values(data.kits)) for (const a of kit.abilities) if (a.id === id) return a;
    return null;
}

// ======================= backgrounds =======================

function bands(ctx, colors, y0 = 0, y1 = GROUND) {
    const h = (y1 - y0) / colors.length;
    colors.forEach((c, i) => { ctx.fillStyle = c; ctx.fillRect(0, Math.round(y0 + i * h), W, Math.ceil(h) + 1); });
}

function ridge(ctx, rng, color, base, amp, step = 6) {
    ctx.fillStyle = color;
    let y = base;
    for (let x = 0; x < W; x += step) {
        y = Math.max(base - amp, Math.min(base, y + (rng() - 0.5) * amp * 0.7));
        ctx.fillRect(x, Math.round(y), step, GROUND - Math.round(y));
    }
}

function ground(ctx, rng, top, fill, speck) {
    ctx.fillStyle = top;
    ctx.fillRect(0, GROUND, W, 2);
    ctx.fillStyle = fill;
    ctx.fillRect(0, GROUND + 2, W, H - GROUND - 2);
    ctx.fillStyle = speck;
    for (let k = 0; k < 90; k++) ctx.fillRect(Math.floor(rng() * W), GROUND + 3 + Math.floor(rng() * (H - GROUND - 3)), 1 + Math.floor(rng() * 3), 1);
}

function stars(ctx, rng, n, maxY) {
    for (let k = 0; k < n; k++) {
        ctx.fillStyle = rng() < 0.2 ? "#ffffff" : "rgba(220, 220, 255, 0.55)";
        ctx.fillRect(Math.floor(rng() * W), Math.floor(rng() * maxY), 1, 1);
    }
}

function vignette(ctx) {
    const g = ctx.createRadialGradient(W / 2, H * 0.55, H * 0.4, W / 2, H * 0.55, W * 0.62);
    g.addColorStop(0, "rgba(0,0,0,0)");
    g.addColorStop(1, "rgba(3,0,21,0.55)");
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, W, H);
}

/** Drifting flakes shared by several arenas. */
function drift(list, rng, n, make) {
    while (list.length < n) list.push(make(rng, true));
    return list;
}

function drawBackground(id, rng) {
    const canvas = document.createElement("canvas");
    canvas.width = W;
    canvas.height = H;
    const ctx = canvas.getContext("2d");
    const flakes = [];
    let animate = null;

    const flakeLayer = (n, color, vx, vy, size = 1) => (c, t, dt) => {
        const count = effectsLevel() > 0 ? n : Math.ceil(n / 3);
        drift(flakes, rng, count, (r, first) => ({ x: r() * W, y: first ? r() * H : -2, s: 0.5 + r(), p: r() * 6 }));
        c.fillStyle = color;
        for (const f of flakes) {
            f.x += (vx + Math.sin(t + f.p) * 4) * f.s * dt;
            f.y += vy * f.s * dt;
            if (f.y > H || f.x < -4 || f.x > W + 4) { f.x = rng() * W; f.y = vy > 0 ? -2 : H + 2; }
            c.fillRect(Math.round(f.x), Math.round(f.y), size, size);
        }
    };

    switch (id) {
        case "lava": {
            bands(ctx, ["#1a0505", "#2a0808", "#3d0c0a", "#561410", "#6e1d10", "#8a2a12"]);
            ctx.fillStyle = "#1a0a0a";
            ctx.beginPath(); ctx.moveTo(150, GROUND); ctx.lineTo(205, 34); ctx.lineTo(222, 34); ctx.lineTo(290, GROUND); ctx.fill();
            ctx.fillStyle = "#ff6a1a"; ctx.fillRect(206, 32, 15, 3);
            ridge(ctx, rng, "#140808", GROUND - 8, 18);
            ground(ctx, rng, "#2a1a16", "#1a1210", "#2e201a");
            const cracks = [];
            for (let k = 0; k < 9; k++) {
                let x = rng() * W, y = GROUND + 4 + rng() * 18;
                for (let s = 0; s < 6; s++) { cracks.push([Math.round(x), Math.round(y)]); x += 2 + rng() * 3; y += (rng() - 0.5) * 3; }
            }
            vignette(ctx);
            const embers = flakeLayer(26, "#ff9a3d", 3, -14);
            animate = (c, t, dt) => {
                const glow = 0.6 + Math.sin(t * 2) * 0.3;
                c.fillStyle = `rgba(255, 106, 26, ${glow})`;
                for (const [x, y] of cracks) c.fillRect(x, y, 2, 1);
                c.fillStyle = `rgba(255, 140, 40, ${0.25 + Math.sin(t * 1.3) * 0.1})`;
                c.fillRect(0, H - 6, W, 6);
                embers(c, t, dt);
            };
            break;
        }
        case "ice": {
            bands(ctx, ["#040b1d", "#06142e", "#0b1f40", "#122b52", "#1a3866", "#24477a"]);
            stars(ctx, rng, 50, 60);
            ctx.fillStyle = "#5a8fc7";
            for (let k = 0; k < 14; k++) { const x = rng() * W, h = 20 + rng() * 40; ctx.beginPath(); ctx.moveTo(x - 7, GROUND); ctx.lineTo(x, GROUND - h); ctx.lineTo(x + 7, GROUND); ctx.fill(); }
            ctx.fillStyle = "#9fd3ff";
            for (let k = 0; k < 8; k++) { const x = rng() * W, h = 12 + rng() * 26; ctx.beginPath(); ctx.moveTo(x - 4, GROUND); ctx.lineTo(x, GROUND - h); ctx.lineTo(x + 3, GROUND); ctx.fill(); }
            ground(ctx, rng, "#ffffff", "#cfe4f7", "#a9c8e8");
            vignette(ctx);
            const snow = flakeLayer(40, "#ffffff", -6, 16);
            animate = (c, t, dt) => {
                for (let band = 0; band < 2; band++) {
                    c.fillStyle = band ? "rgba(64, 224, 208, 0.12)" : "rgba(168, 255, 64, 0.1)";
                    for (let x = 0; x < W; x += 2) {
                        const y = 18 + band * 10 + Math.sin(x / 30 + t * 0.6 + band) * 7;
                        c.fillRect(x, Math.round(y), 2, 8);
                    }
                }
                snow(c, t, dt);
            };
            break;
        }
        case "sanctuary": {
            bands(ctx, ["#1d1430", "#2b1d3d", "#45294a", "#6a3d4e", "#94584e", "#b8784e"]);
            ctx.fillStyle = "#e9d8a6"; ctx.fillRect(150, 20, 20, 20);
            for (const cx of [26, 60, 260, 294]) {
                ctx.fillStyle = "#cfc3a3"; ctx.fillRect(cx - 6, 34, 12, GROUND - 34);
                ctx.fillStyle = "#a89c80"; ctx.fillRect(cx + 3, 34, 3, GROUND - 34);
                ctx.fillStyle = "#e6dcc0"; ctx.fillRect(cx - 9, 30, 18, 5); ctx.fillRect(cx - 8, GROUND - 4, 16, 4);
            }
            ctx.fillStyle = "#e6dcc0"; ctx.fillRect(14, 24, 58, 6); ctx.fillRect(248, 24, 58, 6);
            ground(ctx, rng, "#e8dcc0", "#c9b994", "#b3a37e");
            ctx.fillStyle = "rgba(0,0,0,0.08)";
            for (let x = 0; x < W; x += 16) ctx.fillRect(x, GROUND + 2, 1, H);
            vignette(ctx);
            const motes = flakeLayer(24, "#fff2a8", 2, -8);
            animate = (c, t, dt) => {
                c.fillStyle = `rgba(255, 240, 180, ${0.06 + Math.sin(t * 0.8) * 0.03})`;
                for (let k = 0; k < 4; k++) {
                    const x = 90 + k * 40 + Math.sin(t * 0.3 + k) * 6;
                    c.beginPath(); c.moveTo(x, 0); c.lineTo(x + 14, 0); c.lineTo(x + 40, GROUND); c.lineTo(x + 18, GROUND); c.fill();
                }
                motes(c, t, dt);
            };
            break;
        }
        case "storm": {
            bands(ctx, ["#07090f", "#0c0f1a", "#121726", "#191f31", "#20283c", "#283046"]);
            ctx.fillStyle = "#11141f";
            for (let k = 0; k < 18; k++) { const x = rng() * W, y = rng() * 30; ctx.fillRect(Math.round(x), Math.round(y), 30 + rng() * 40, 6 + rng() * 6); }
            ctx.fillStyle = "#151926";
            ctx.beginPath(); ctx.moveTo(40, GROUND); ctx.lineTo(120, 40); ctx.lineTo(140, 52); ctx.lineTo(170, 30); ctx.lineTo(260, GROUND); ctx.fill();
            ground(ctx, rng, "#4a4a58", "#33333e", "#26262f");
            vignette(ctx);
            const drops = [];
            animate = (c, t, dt, arena) => {
                const n = effectsLevel() > 0 ? 70 : 25;
                while (drops.length < n) drops.push({ x: rng() * (W + 60), y: rng() * H, s: 0.7 + rng() * 0.6 });
                c.fillStyle = "rgba(170, 190, 230, 0.45)";
                for (const d of drops) {
                    d.x -= 60 * d.s * dt; d.y += 220 * d.s * dt;
                    if (d.y > H) { d.y = -4; d.x = rng() * (W + 60); }
                    c.fillRect(Math.round(d.x), Math.round(d.y), 1, 4);
                }
                if (rng() < dt * 0.15) arena.skyFlash = 0.5;
            };
            break;
        }
        case "gale": {
            bands(ctx, ["#1b2a4a", "#2b3a5c", "#4a4a6a", "#7a5a6e", "#b06a62", "#d88a62"]);
            ctx.fillStyle = "#ffd9a0"; ctx.fillRect(40, 60, 18, 18);
            ctx.fillStyle = "#3a5a7a"; ctx.fillRect(0, GROUND - 14, W, 14);
            ridge(ctx, rng, "#2c3a3a", GROUND - 2, 10, 4);
            ground(ctx, rng, "#5a8a4a", "#3c6b3a", "#2e5530");
            const tufts = Array.from({ length: 30 }, () => [Math.floor(rng() * W), GROUND - 1]);
            vignette(ctx);
            const leaves = flakeLayer(14, "#a8ff40", 80, 6, 2);
            const streaks = [];
            animate = (c, t, dt) => {
                c.fillStyle = "#7ab05a";
                for (const [x, y] of tufts) { const sway = Math.round(Math.sin(t * 4 + x) * 1.5); c.fillRect(x + sway, y - 3, 1, 3); c.fillRect(x + 2 + sway, y - 2, 1, 2); }
                while (streaks.length < 8) streaks.push({ x: rng() * W, y: 10 + rng() * (GROUND - 20), l: 10 + rng() * 30, s: 120 + rng() * 120 });
                c.fillStyle = "rgba(255, 255, 255, 0.28)";
                for (const s of streaks) { s.x += s.s * dt; if (s.x > W + 40) { s.x = -s.l; s.y = 10 + rng() * (GROUND - 20); } c.fillRect(Math.round(s.x), Math.round(s.y), Math.round(s.l), 1); }
                leaves(c, t, dt);
            };
            break;
        }
        case "forge": {
            bands(ctx, ["#1a0d0a", "#22110c", "#2a150e", "#311a10"]);
            for (let y = 0; y < GROUND; y += 8) for (let x = (y / 8) % 2 ? -8 : 0; x < W; x += 16) {
                ctx.fillStyle = rng() < 0.5 ? "#3a1f1a" : "#33201a";
                ctx.fillRect(x + 1, y + 1, 14, 6);
            }
            ctx.fillStyle = "#120806"; ctx.fillRect(130, 40, 60, 50);
            ctx.fillStyle = "#2a1a14"; ctx.fillRect(126, 36, 68, 6);
            ctx.fillStyle = "#4a4a54"; ctx.fillRect(40, GROUND - 12, 24, 6); ctx.fillRect(46, GROUND - 6, 12, 6); ctx.fillRect(36, GROUND - 14, 10, 3);
            ctx.fillStyle = "#6a6a74"; for (let y = 0; y < 50; y += 4) { ctx.fillRect(280, y, 2, 3); ctx.fillRect(20, y, 2, 3); }
            ground(ctx, rng, "#4a3a34", "#2e2420", "#3a2e28");
            vignette(ctx);
            animate = (c, t, dt, arena) => {
                const f = 0.6 + Math.sin(t * 9) * 0.1 + Math.sin(t * 23) * 0.08;
                c.fillStyle = `rgba(255, 120, 30, ${f})`; c.fillRect(134, 56, 52, 34);
                c.fillStyle = `rgba(255, 210, 90, ${f * 0.8})`; c.fillRect(144, 68, 32, 22);
                const g = c.createRadialGradient(160, 80, 4, 160, 80, 120);
                g.addColorStop(0, `rgba(255, 120, 30, ${0.18 * f})`); g.addColorStop(1, "rgba(255,120,30,0)");
                c.fillStyle = g; c.fillRect(0, 0, W, H);
                if (rng() < dt * 8) arena.particle(150 + rng() * 20, 64, (rng() - 0.5) * 40, -30 - rng() * 30, 0.8, "#ffb347", 1, 60);
            };
            break;
        }
        case "colosseum": {
            bands(ctx, ["#1a2a5a", "#24376a", "#2f4a80", "#3d5c94", "#4a6aa0"], 0, 40);
            const crowd = [];
            for (let tier = 0; tier < 5; tier++) {
                const y = 36 + tier * 13;
                ctx.fillStyle = tier % 2 ? "#6a5a4a" : "#7a6a56"; ctx.fillRect(0, y, W, 13);
                ctx.fillStyle = "#4a3e32"; ctx.fillRect(0, y + 11, W, 2);
                for (let x = 2; x < W; x += 5) if (rng() < 0.85) crowd.push({ x, y: y + 4, c: ["#e0407a", "#40e0d0", "#ffd166", "#a8ff40", "#c0c0ff", "#f0c49a"][Math.floor(rng() * 6)], p: rng() * 6 });
            }
            ctx.fillStyle = "#8a2a2a"; for (const bx of [30, 110, 200, 280]) ctx.fillRect(bx, 36, 10, 22);
            ground(ctx, rng, "#e0c080", "#c8a060", "#b08850");
            vignette(ctx);
            animate = (c, t) => {
                c.globalAlpha = 0.55;
                for (const p of crowd) {
                    c.fillStyle = p.c;
                    c.fillRect(p.x, p.y - Math.round(Math.max(0, Math.sin(t * 5 + p.p)) * 2), 3, 3);
                }
                c.globalAlpha = 1;
                c.fillStyle = "rgba(10, 6, 24, 0.25)";
                c.fillRect(0, 36, W, GROUND - 36);
            };
            break;
        }
        case "void": {
            bands(ctx, ["#030008", "#06010f", "#0a0218", "#100322", "#16042c"]);
            stars(ctx, rng, 120, GROUND);
            ctx.fillStyle = "#2a0f4a"; ctx.fillRect(60, GROUND, 200, 3);
            ctx.fillStyle = "#1a0830"; ctx.fillRect(70, GROUND + 3, 180, 8); ctx.fillRect(100, GROUND + 11, 120, 6); ctx.fillRect(140, GROUND + 17, 40, 6);
            ctx.fillStyle = "#b26bff"; for (let x = 64; x < 256; x += 12) ctx.fillRect(x, GROUND, 4, 1);
            const rocks = Array.from({ length: 5 }, () => ({ x: rng() * W, y: 20 + rng() * 60, w: 6 + rng() * 10, p: rng() * 6 }));
            animate = (c, t) => {
                for (let k = 0; k < 40; k++) {
                    const a = t * 0.4 + k * 0.6, r = 8 + k * 1.6;
                    c.fillStyle = k % 3 ? "rgba(178, 107, 255, 0.5)" : "rgba(64, 224, 208, 0.5)";
                    c.fillRect(Math.round(160 + Math.cos(a) * r * 1.6), Math.round(48 + Math.sin(a) * r * 0.6), 1, 1);
                }
                for (const r of rocks) {
                    const y = Math.round(r.y + Math.sin(t + r.p) * 3);
                    c.fillStyle = "#24103c"; c.fillRect(Math.round(r.x), y, Math.round(r.w), 4);
                    c.fillStyle = "#3a1a5e"; c.fillRect(Math.round(r.x), y, Math.round(r.w), 1);
                }
            };
            break;
        }
        default: {
            // The Ashen Wastes: night sky, a pale moon, ruined towers and falling ash.
            bands(ctx, ["#0a0618", "#120a26", "#1b0f33", "#26143c", "#331a44", "#43224b"]);
            stars(ctx, rng, 60, 70);
            ctx.fillStyle = "#e8dcc8"; ctx.beginPath(); ctx.arc(250, 30, 11, 0, Math.PI * 2); ctx.fill();
            ctx.fillStyle = "#c9bca6"; ctx.fillRect(244, 26, 3, 3); ctx.fillRect(252, 33, 4, 3);
            ridge(ctx, rng, "#1e1430", GROUND - 22, 22, 8);
            ctx.fillStyle = "#170f26";
            for (let k = 0; k < 6; k++) {
                const x = Math.floor(rng() * W), h = 26 + rng() * 34, w = 8 + rng() * 10;
                ctx.fillRect(x, GROUND - h, w, h);
                for (let b = 0; b < w; b += 3) ctx.fillRect(x + b, GROUND - h - 2, 2, 2);
                if (rng() < 0.6) { ctx.fillStyle = "rgba(255, 150, 60, 0.6)"; ctx.fillRect(x + 3, GROUND - h + 8, 2, 3); ctx.fillStyle = "#170f26"; }
            }
            ground(ctx, rng, "#4a4060", "#2a2338", "#3b3350");
            ctx.fillStyle = "#544a6a"; for (let k = 0; k < 14; k++) ctx.fillRect(Math.floor(rng() * W), GROUND - 2, 1 + Math.floor(rng() * 2), 2);
            vignette(ctx);
            animate = flakeLayer(34, "rgba(200, 190, 220, 0.75)", -8, 10);
        }
    }
    return { canvas, animate };
}
