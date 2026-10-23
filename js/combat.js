// Real-time fight replay. The server simulates the whole fight up front; this plays its events back.
import { data, el, item, itemCard, statPretty } from "./data.js";
import { formatTime } from "./ui.js";

const OVERTIME_START = 30;
const SUDDEN_DEATH_START = 60;
const END_PAUSE = 1.2;

const STAT_NAMES = {
    damage: "Damage", cooldown: "Cooldown", crit_chance: "Crit chance", crit_damage: "Crit damage",
    damage_reduction: "Damage reduction", dodge_chance: "Dodge", hit_condition: "Hits needed"
};

function targetName(id) {
    if (data.items[id]) return data.items[id].name;
    const owner = Object.values(data.items).find(i => i.effects?.includes(id));
    const kind = data.effects[id]?.type ?? "effect";
    return owner ? `${owner.name} ${kind}` : id;
}

function buffValue(b) {
    if (b.mult != null) return `×${Math.round(b.mult * 100) / 100}`;
    if (b.add != null) {
        const percent = ["crit_chance", "dodge_chance", "damage_reduction"].includes(b.stat);
        const v = percent ? `${Math.round(b.add * 1000) / 10}%` : Math.round(b.add * 100) / 100;
        const unit = b.stat === "cooldown" ? "s" : "";
        return `${b.add >= 0 ? "+" : ""}${v}${unit}`;
    }
    return "";
}

export function createCombatView(app) {
    let replay = null;

    function fighterPanel(root, info, isYou) {
        const hpFill = el("div", { class: "hp-fill" });
        const hpGhost = el("div", { class: "hp-ghost" });
        const hpText = el("span", { class: "hp-text" });
        const status = el("div", { class: "status-row" });
        const cdFill = el("div", { class: "cd-fill" });
        const weapon = info.items.find(i => i.slot === "weapon");
        const weaponCard = weapon ? itemCard(weapon.id, { level: weapon.level, size: "large" }) : el("div", { class: "slot-empty big", text: "No weapon" });
        const others = info.items.filter(i => i.slot !== "weapon");
        const buffs = el("ul", { class: "buff-list" });
        const floats = el("div", { class: "floats" });

        root.replaceChildren(
            el("div", { class: "fighter-head" },
                el("h3", {}, info.name, isYou ? el("span", { class: "tag", text: "You" }) : null, info.ghost ? el("span", { class: "tag", text: "Ghost" }) : null),
                el("div", { class: "hp-bar" }, hpGhost, hpFill, hpText)),
            status,
            el("div", { class: "fighter-body" },
                el("div", { class: "weapon-box" }, weaponCard, el("div", { class: "cd-bar" }, cdFill)),
                el("div", { class: "fighter-items" }, others.map(o => itemCard(o.id, { level: o.level })))),
            el("div", { class: "fighter-meta muted small", text:
                `DR ${Math.round(info.damageReduction * 100)}% · Dodge ${Math.round(info.dodgeChance * 100)}%` }),
            el("h4", { text: "Buffs" }),
            buffs,
            floats);
        root.classList.toggle("you", isYou);
        return { root, hpFill, hpGhost, hpText, status, cdFill, weaponCard, buffs, floats, info };
    }

    function float(panel, text, cls) {
        const node = el("div", { class: `float ${cls}`, text });
        node.style.left = `${20 + Math.random() * 60}%`;
        panel.floats.append(node);
        setTimeout(() => node.remove(), 1100);
    }

    function shake(panel) {
        panel.root.classList.remove("shake");
        void panel.root.offsetWidth;
        panel.root.classList.add("shake");
    }

    function play(msg) {
        stop();
        const { result, you } = msg;
        const sideOf = i => (i === you ? 0 : 1);
        const panels = [];
        panels[sideOf(0)] = fighterPanel(document.getElementById(`fighter${sideOf(0)}`), result.fighters[0], you === 0);
        panels[sideOf(1)] = fighterPanel(document.getElementById(`fighter${sideOf(1)}`), result.fighters[1], you === 1);
        const byIndex = i => panels[sideOf(i)];

        const attacks = [[], []];
        for (const ev of result.events) if (ev.kind === "damage" && ev.weaponAttack) attacks[ev.source].push(ev.t);

        const buffs = [new Map(), new Map()];
        result.fighters.forEach((f, i) => f.startBuffs.forEach(b => buffs[i].set(b.effectSource, { ...b, t: 0 })));

        replay = {
            msg, result, you, byIndex,
            start: performance.now() - Math.max(0, msg.elapsed ?? 0) * 1000,
            next: 0,
            attacks, buffs,
            hp: result.fighters.map(f => f.maxHp),
            stunnedUntil: [0, 0],
            invulnerableUntil: [0, 0],
            finished: false,
            raf: 0
        };
        document.getElementById("cRound").textContent = `Round ${msg.round}`;
        document.getElementById("resultScreen").hidden = true;
        for (let i = 0; i < 2; i++) renderBuffs(i, 0);
        frame();
    }

    function stop() {
        if (replay?.raf) cancelAnimationFrame(replay.raf);
        replay = null;
    }

    function renderBuffs(i, t) {
        const panel = replay.byIndex(i);
        const list = [...replay.buffs[i].values()];
        panel.buffs.replaceChildren(...(list.length ? list.map(b => {
            const left = b.duration != null ? Math.max(0, b.t + b.duration - t) : null;
            return el("li", {},
                b.itemSource ? el("img", { src: `img/item/${item(b.itemSource)?.sprite}`, alt: "" }) : el("span", { class: "buff-dot" }),
                el("span", { class: "grow", text: `${targetName(b.buffTarget)} ${STAT_NAMES[b.stat] ?? b.stat}` }),
                b.stacks > 1 ? el("span", { class: "stacks", text: `${b.stacks}` }) : null,
                el("b", { text: buffValue(b) }),
                left != null ? el("span", { class: "buff-time", text: `${left.toFixed(1)}s` }) : null);
        }) : [el("li", { class: "muted small", text: "None" })]));
    }

    function apply(ev) {
        const r = replay;
        r.hp = ev.hp;
        const target = r.byIndex(ev.target);
        const source = r.byIndex(ev.source);
        switch (ev.kind) {
            case "damage":
                if (ev.weaponAttack) {
                    source.weaponCard.classList.remove("swing");
                    void source.weaponCard.offsetWidth;
                    source.weaponCard.classList.add("swing");
                }
                if (ev.suddenDeath) {
                    target.root.classList.add("sudden");
                } else if (ev.blocked) {
                    float(target, "Blocked", "blocked");
                } else if (ev.dodged) {
                    float(target, "Dodge", "dodge");
                } else if (ev.effectSource) {
                    float(target, `Smash ${Math.round(ev.damage)}`, "smash");
                    shake(target);
                } else if (ev.critMultiplier) {
                    float(target, `${Math.round(ev.damage * 10) / 10}!`, "crit");
                    shake(target);
                } else {
                    float(target, `${Math.round(ev.damage * 10) / 10}`, "hit");
                }
                break;
            case "stun":
                if (ev.blocked) float(target, "Stun blocked", "blocked");
                else {
                    r.stunnedUntil[ev.target] = Math.max(r.stunnedUntil[ev.target], ev.t) + ev.duration;
                    float(target, ev.crit ? `Stunned ${ev.duration}s!` : `Stunned ${ev.duration}s`, "stun");
                }
                break;
            case "invulnerability":
                r.invulnerableUntil[ev.target] = Math.max(r.invulnerableUntil[ev.target], ev.t + ev.duration);
                float(target, "Invulnerable", "invuln");
                break;
            case "money":
                if (ev.payout != null) float(source, ev.won ? `Chip ×2 (${Math.round(ev.payout)}$)` : `Chip ×0.5 (${Math.round(ev.payout)}$)`, "money");
                else if (ev.gain > 0) float(source, `+${Math.round(ev.gain * 10) / 10}$`, "money");
                break;
            case "buff":
                if (ev.stacks > 0) r.buffs[ev.source].set(ev.effectSource, { ...ev });
                else r.buffs[ev.source].delete(ev.effectSource);
                break;
        }
    }

    function frame() {
        const r = replay;
        if (!r) return;
        const t = (performance.now() - r.start) / 1000;
        let buffsChanged = false;
        while (r.next < r.result.events.length && r.result.events[r.next].t <= t) {
            const ev = r.result.events[r.next++];
            if (ev.kind === "buff") buffsChanged = true;
            apply(ev);
        }
        const shown = Math.min(t, r.result.duration);

        for (let i = 0; i < 2; i++) {
            const p = r.byIndex(i);
            const max = r.result.fighters[i].maxHp;
            const pct = Math.max(0, Math.min(1, r.hp[i] / max));
            p.hpFill.style.width = `${pct * 100}%`;
            p.hpGhost.style.width = `${pct * 100}%`;
            p.hpFill.classList.toggle("low", pct < 0.3);
            p.hpText.textContent = `${Math.ceil(Math.max(0, r.hp[i]))} / ${Math.round(max)}`;

            const times = r.attacks[i];
            let prev = 0, nextT = null;
            for (const at of times) { if (at <= shown) prev = at; else { nextT = at; break; } }
            const fill = nextT == null ? (times.length ? 1 : 0) : (shown - prev) / Math.max(0.05, nextT - prev);
            p.cdFill.style.width = `${Math.max(0, Math.min(1, fill)) * 100}%`;

            const stunned = shown < r.stunnedUntil[i];
            const invuln = shown < r.invulnerableUntil[i];
            p.root.classList.toggle("stunned", stunned);
            p.root.classList.toggle("invulnerable", invuln);
            p.status.replaceChildren(...[
                stunned ? el("span", { class: "badge stun", text: `Stunned ${(r.stunnedUntil[i] - shown).toFixed(1)}s` }) : null,
                invuln ? el("span", { class: "badge invuln", text: `Invulnerable ${(r.invulnerableUntil[i] - shown).toFixed(1)}s` }) : null
            ].filter(Boolean));
            if (buffsChanged || [...r.buffs[i].values()].some(b => b.duration != null)) renderBuffs(i, shown);
        }

        document.getElementById("cTime").textContent = `${shown.toFixed(1)}s`;
        const ot = document.getElementById("cOvertime");
        if (shown >= SUDDEN_DEATH_START) {
            ot.textContent = "Sudden death";
            ot.className = "overtime sudden";
        } else if (shown > OVERTIME_START) {
            ot.textContent = `Overtime · damage ×${(1 + 0.1 * (shown - OVERTIME_START)).toFixed(1)}`;
            ot.className = "overtime on";
        } else {
            ot.textContent = "";
            ot.className = "overtime";
        }

        if (!r.finished && t >= r.result.duration + END_PAUSE) {
            r.finished = true;
            showResult();
            app.socket.send("replayDone");
            return;
        }
        r.raf = requestAnimationFrame(frame);
    }

    function showResult() {
        const { result, you } = replay;
        const won = result.winner === you, draw = result.winner === -1;
        const title = document.getElementById("resultTitle");
        title.textContent = draw ? "Draw" : won ? "Victory" : "Defeat";
        title.className = draw ? "draw" : won ? "win" : "lose";
        const opp = 1 - you;
        const rows = [
            ["Opponent", result.fighters[opp].name],
            ["Damage dealt", Math.round(result.damageDealt[you])],
            ["Damage taken", Math.round(result.damageTaken[you])],
            ["Fight length", `${result.duration.toFixed(1)}s`],
            ["Money earned in fight", `${result.earnings[you]}$`]
        ];
        if (won && result.damageTaken[you] <= 0) rows.push(["Flawless victory", "Yes"]);
        document.getElementById("resultStats").replaceChildren(...rows.map(([k, v]) =>
            el("li", {}, el("span", { class: "muted", text: k }), el("b", { text: String(v) }))));
        document.getElementById("resultScreen").hidden = false;
        updateWait(app.game.state);
    }

    function updateWait(state) {
        const wait = document.getElementById("resultWait");
        if (!state) return;
        if (state.phase === "results") {
            wait.textContent = `Lives left: ${state.you?.lives ?? 0}. Next shop in ${formatTime(state.phaseRemaining ?? 0)}s`;
            let left = state.phaseRemaining ?? 0;
            clearInterval(updateWait.timer);
            updateWait.timer = setInterval(() => {
                left -= 0.25;
                if (left <= 0) return clearInterval(updateWait.timer);
                wait.textContent = `Lives left: ${state.you?.lives ?? 0}. Next shop in ${formatTime(left)}s`;
            }, 250);
        } else {
            clearInterval(updateWait.timer);
            wait.textContent = "Waiting for the other fights to finish…";
        }
    }

    return {
        play,
        stop,
        get finished() { return !replay || replay.finished; },
        get active() { return !!replay; },
        onState(state) { if (replay?.finished) updateWait(state); }
    };
}
