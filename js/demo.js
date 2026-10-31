// Title screen: two random fighters spar forever in a random arena (fake events, no server, no sound).
import { Arena } from "./arena.js";
import { data } from "./data.js";

const ARENAS = [null, "lava", "ice", "sanctuary", "storm", "gale", "forge", "colosseum", "void"];
const WEAPONS = ["generic.sword", "generic.hammer", "generic.bow", "generic.frying_pan", "pepf.guitar", "z7fox.whip", "ely7.blade", "xxzweiradxx.staff"];
const SKINS = [null, null, "ws_ember", "ws_frost", "ws_void", "ws_gilded", "ws_toxic", "ws_prismatic"];

export function startDemo(root) {
    let arena = null, timer = 0, round = 0;
    const pick = list => list[Math.floor(Math.random() * list.length)];

    function newDuel() {
        arena?.destroy();
        clearInterval(timer);
        const kits = Object.keys(data.kits);
        const dynamics = Object.values(data.items).filter(i => !["weapon", "shirt"].includes(i.type)).map(i => i.id);
        const shirts = Object.values(data.items).filter(i => i.type === "shirt").map(i => i.id);
        const fighters = [0, 1].map(() => ({
            kit: pick(kits),
            items: [{ id: pick(WEAPONS), slot: "weapon" }, { id: pick(shirts), slot: "shirt" },
                ...[...new Set([pick(dynamics), pick(dynamics), pick(dynamics)])].map((id, k) => ({ id, slot: `dynamic${k}` }))],
            appearance: { weaponSkin: pick(SKINS), victory: pick(["victory_fireworks", "victory_confetti", "victory_souls", "victory_ashstorm", "victory_meteor"]) }
        }));
        // Every few rounds one side shows off the admin set "Pepf Prime".
        if (round % 4 === 3) fighters[round % 2].appearance = { weaponSkin: "ws_pepf", victory: "victory_pepf", frame: "frame_pepf" };
        arena = new Arena(root, { fighters, arenaId: ARENAS[round++ % ARENAS.length], seed: Date.now() & 0xffff, silent: true });
        const hp = [1, 1], t = { now: 0 }, next = [0.6, 1.1];
        const kitAbilities = fighters.map(f => data.kits[f.kit]?.abilities ?? []);
        timer = setInterval(() => {
            if (root.closest("[hidden]")) return;
            t.now += 0.1;
            for (let i = 0; i < 2; i++) {
                if (t.now < next[i] || hp[0] <= 0 || hp[1] <= 0) continue;
                next[i] = t.now + 0.7 + Math.random() * 0.9;
                const roll = Math.random(), j = 1 - i;
                const ev = { kind: "damage", source: i, target: j, weaponAttack: true, damage: 0 };
                if (roll < 0.15) ev.dodged = true;
                else if (roll < 0.22) { const a = pick(kitAbilities[i]); if (a) arena.event({ kind: "ability", source: i, target: i, ability: a.id }); continue; }
                else if (roll < 0.3) { const it = pick(fighters[i].items.slice(1)); arena.event({ kind: "buff", source: i, target: i, stacks: 1, itemSource: it.id }); continue; }
                else {
                    const crit = roll > 0.82;
                    ev.damage = Math.round((6 + Math.random() * 8) * (crit ? 2.2 : 1));
                    if (crit) ev.critMultiplier = 2.2;
                    hp[j] = Math.max(0, hp[j] - ev.damage / 110);
                }
                arena.event(ev);
            }
            arena.snap({ t: t.now, f: hp.map(h => ({ hp: h * 100, maxHp: 100, shield: 0, stun: 0, invuln: 0, parry: false, windup: null, ult: 0.3, mods: [] })) });
            if (hp[0] <= 0 || hp[1] <= 0) {
                clearInterval(timer);
                arena.finish(hp[0] <= 0 ? 1 : 0);
                setTimeout(newDuel, 4200);
            }
        }, 100);
    }
    newDuel();
}
