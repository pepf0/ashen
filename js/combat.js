// Live fight view. The server runs the fight in real time and streams ticks; Q/W/E/R send abilities.
import { data, el, item, itemCard, abilityById } from "./data.js";
import { formatTime } from "./ui.js";

const STAT_NAMES = {
    damage: "Damage", cooldown: "Cooldown", crit_chance: "Crit chance", crit_damage: "Crit damage",
    damage_reduction: "Damage reduction", dodge_chance: "Dodge", hit_condition: "Hits needed"
};
const KEYS = ["q", "w", "e", "r"];

function targetName(id) {
    if (data.items[id]) return data.items[id].name;
    const owner = Object.values(data.items).find(i => i.effects?.includes(id));
    const kind = data.effects[id]?.type ?? "effect";
    return owner ? `${owner.name} ${kind}` : id;
}

function buffLabel(b) {
    if (b.effectSource?.startsWith("synergy.")) return "Pack synergy";
    return `${targetName(b.buffTarget)} ${STAT_NAMES[b.stat] ?? b.stat}`;
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
    let fight = null;

    function abilityButton(a, slot, mine) {
        const sweep = el("div", { class: "ab-sweep" });
        const label = el("span", { class: "ab-cd" });
        const btn = el(mine ? "button" : "div", {
            class: "ab" + (a.ultimate ? " ult" : "") + (mine ? "" : " small"),
            title: `${a.name}: ${a.description}`,
            onclick: mine ? () => app.socket.send("ability", { slot }) : undefined
        }, sweep, el("span", { class: "ab-key", text: a.key.toUpperCase() }), el("span", { class: "ab-name", text: a.name }), label);
        return { btn, sweep, label, ability: a };
    }

    function fighterPanel(root, info, isYou) {
        const hpFill = el("div", { class: "hp-fill" });
        const hpGhost = el("div", { class: "hp-ghost" });
        const hpShield = el("div", { class: "hp-shield" });
        const hpText = el("span", { class: "hp-text" });
        const status = el("div", { class: "status-row" });
        const cdFill = el("div", { class: "cd-fill" });
        const weapon = info.items.find(i => i.slot === "weapon");
        const weaponCard = weapon ? itemCard(weapon.id, { level: weapon.level, size: "large" }) : el("div", { class: "slot-empty big", text: "No weapon" });
        const others = info.items.filter(i => i.slot !== "weapon");
        const buffs = el("ul", { class: "buff-list" });
        const floats = el("div", { class: "floats" });
        const kit = info.kit ? data.kits[info.kit] : null;
        const abilities = kit ? kit.abilities.map((a, i) => abilityButton(a, i, isYou && fight?.you != null)) : [];

        root.replaceChildren(
            el("div", { class: "fighter-head" },
                el("h3", {}, info.name,
                    isYou ? el("span", { class: "tag", text: "You" }) : null,
                    info.ghost ? el("span", { class: "tag", text: "Ghost" }) : null,
                    kit ? el("span", { class: "tag", text: kit.name }) : null),
                el("div", { class: "hp-bar" }, hpGhost, hpFill, hpShield, hpText)),
            status,
            el("div", { class: "fighter-body" },
                el("div", { class: "weapon-box" }, weaponCard, el("div", { class: "cd-bar" }, cdFill)),
                el("div", { class: "fighter-items" }, others.map(o => itemCard(o.id, { level: o.level })))),
            el("div", { class: "fighter-meta muted small", text:
                `DR ${Math.round(info.damageReduction * 100)}% · Dodge ${Math.round(info.dodgeChance * 100)}%` }),
            abilities.length ? el("div", { class: "ability-bar" + (isYou && fight?.you != null ? " mine" : "") }, abilities.map(a => a.btn)) : null,
            el("h4", { text: "Buffs" }),
            buffs,
            floats);
        root.classList.toggle("you", isYou);
        root.classList.remove("stunned", "invulnerable", "sudden", "dead");
        return { root, hpFill, hpGhost, hpShield, hpText, status, cdFill, weaponCard, buffs, floats, info, abilities };
    }

    function float(panel, text, cls) {
        const node = el("div", { class: `float ${cls}`, text });
        node.style.left = `${15 + Math.random() * 65}%`;
        panel.floats.append(node);
        setTimeout(() => node.remove(), 1100);
    }

    function shake(panel) {
        panel.root.classList.remove("shake");
        void panel.root.offsetWidth;
        panel.root.classList.add("shake");
    }

    /** fightStart: build both panels. `you` is null when spectating. */
    function start(msg) {
        stop();
        fight = {
            id: msg.fightId,
            you: msg.you,
            fighters: msg.fighters,
            buffs: [new Map(), new Map()],
            snap: msg.snap,
            ended: false,
            countdownEnd: performance.now() + (msg.countdown ?? 0) * 1000,
            raf: 0
        };
        const left = msg.you ?? 0;
        fight.sideOf = i => (i === left ? 0 : 1);
        fight.panels = [];
        for (let i = 0; i < 2; i++) {
            fight.panels[fight.sideOf(i)] = fighterPanel(document.getElementById(`fighter${fight.sideOf(i)}`), msg.fighters[i], msg.you === i);
            (msg.buffs?.[i] ?? msg.fighters[i].startBuffs).forEach(b => fight.buffs[i].set(b.effectSource, { ...b, t: msg.snap?.t ?? 0 }));
        }
        fight.panel = i => fight.panels[fight.sideOf(i)];
        document.getElementById("cRound").textContent = msg.you == null ? `Round ${msg.round} · spectating` : `Round ${msg.round}`;
        const mod = document.getElementById("cModifier");
        mod.textContent = msg.modifier ? `${msg.modifier.name}: ${msg.modifier.description}` : "";
        mod.className = "arena-modifier " + (msg.modifier ? (msg.modifier.positive ? "good" : "bad") : "");
        document.getElementById("resultScreen").hidden = true;
        document.getElementById("spectatorBar").hidden = msg.you != null;
        render();
        if (msg.ended && msg.result) end({ fightId: msg.fightId, result: msg.result });
        loop();
    }

    function stop() {
        if (fight?.raf) cancelAnimationFrame(fight.raf);
        fight = null;
        document.getElementById("countdown").hidden = true;
    }

    function loop() {
        if (!fight) return;
        const left = (fight.countdownEnd - performance.now()) / 1000;
        const cd = document.getElementById("countdown");
        if (left > -0.6) {
            cd.hidden = false;
            cd.textContent = left > 0 ? `${Math.ceil(left)}` : "Fight!";
        } else cd.hidden = true;
        fight.raf = requestAnimationFrame(loop);
    }

    function tick(msg) {
        if (!fight || msg.fightId !== fight.id) return;
        for (const ev of msg.events) apply(ev);
        fight.snap = msg.snap;
        render();
    }

    function apply(ev) {
        const target = fight.panel(ev.target);
        const source = fight.panel(ev.source);
        switch (ev.kind) {
            case "damage":
                if (ev.weaponAttack || ev.abilitySource === "lunge" || ev.abilitySource === "flurry") {
                    source.weaponCard.classList.remove("swing");
                    void source.weaponCard.offsetWidth;
                    source.weaponCard.classList.add("swing");
                }
                if (ev.suddenDeath) target.root.classList.add("sudden");
                else if (ev.blocked) float(target, "Blocked", "blocked");
                else if (ev.parried) float(target, "Parried!", "blocked");
                else if (ev.missed) float(target, "Miss", "dodge");
                else if (ev.dodged) float(target, "Dodge", "dodge");
                else if (ev.reflected) float(target, `Reflect ${Math.round(ev.damage * 10) / 10}`, "smash");
                else if (ev.abilitySource === "headshot") { float(target, `Headshot ${Math.round(ev.damage)}`, "smash"); shake(target); }
                else if (ev.effectSource) { float(target, `Smash ${Math.round(ev.damage)}`, "smash"); shake(target); }
                else if (ev.critMultiplier) { float(target, `${Math.round(ev.damage * 10) / 10}!`, "crit"); shake(target); }
                else float(target, `${Math.round(ev.damage * 10) / 10}`, "hit");
                if (ev.absorbed > 0) float(target, `-${Math.round(ev.absorbed)} shield`, "invuln");
                break;
            case "stun":
                if (ev.blocked) float(target, "Stun blocked", "blocked");
                else float(target, ev.crit ? `Stunned ${ev.duration}s!` : `Stunned ${ev.duration}s`, "stun");
                break;
            case "invulnerability":
                float(target, "Invulnerable", "invuln");
                break;
            case "money":
                if (ev.payout != null) float(source, ev.won ? `Chip ×2 (${Math.round(ev.payout)}$)` : `Chip ×0.5 (${Math.round(ev.payout)}$)`, "money");
                else if (ev.gain > 0) float(source, `+${Math.round(ev.gain * 10) / 10}$`, "money");
                break;
            case "heal":
                float(source, `+${Math.round(ev.amount * 10) / 10}`, "heal");
                break;
            case "ability": {
                const a = abilityById(ev.ability);
                float(source, a?.name ?? ev.ability, a?.ultimate ? "ult" : "ability");
                break;
            }
            case "buff":
                if (ev.stacks > 0) fight.buffs[ev.source].set(ev.effectSource, { ...ev });
                else fight.buffs[ev.source].delete(ev.effectSource);
                break;
        }
    }

    function render() {
        const snap = fight.snap;
        if (!snap) return;
        for (let i = 0; i < 2; i++) {
            const p = fight.panel(i);
            const s = snap.f[i];
            const pct = Math.max(0, Math.min(1, s.hp / s.maxHp));
            p.hpFill.style.width = `${pct * 100}%`;
            p.hpGhost.style.width = `${pct * 100}%`;
            p.hpShield.style.width = `${Math.min(1, s.shield / s.maxHp) * 100}%`;
            p.hpShield.style.left = `${pct * 100}%`;
            p.hpFill.classList.toggle("low", pct < 0.3);
            p.hpText.textContent = `${Math.ceil(s.hp)} / ${Math.round(s.maxHp)}${s.shield > 0 ? ` (+${Math.ceil(s.shield)})` : ""}`;
            p.cdFill.style.width = `${s.weapon * 100}%`;
            p.root.classList.toggle("stunned", s.stun > 0);
            p.root.classList.toggle("invulnerable", s.invuln > 0);
            p.root.classList.toggle("sudden", !!snap.suddenDeath);
            p.status.replaceChildren(...[
                s.stun > 0 ? el("span", { class: "badge stun", text: `Stunned ${s.stun.toFixed(1)}s` }) : null,
                s.invuln > 0 ? el("span", { class: "badge invuln", text: `Invulnerable ${s.invuln.toFixed(1)}s` }) : null,
                s.parry ? el("span", { class: "badge invuln", text: "Parry ready" }) : null,
                s.windup != null ? el("span", { class: "badge stun", text: `Headshot in ${Math.max(0, s.windup).toFixed(1)}s` }) : null
            ].filter(Boolean));

            p.abilities.forEach((ab, slot) => {
                if (slot === 3) {
                    const charge = s.ult ?? 0;
                    ab.sweep.style.background = `conic-gradient(rgba(255, 209, 102, 0.28) ${charge * 360}deg, transparent 0)`;
                    ab.label.textContent = charge >= 1 ? "READY" : `${Math.floor(charge * 100)}%`;
                    ab.btn.classList.toggle("ready", charge >= 1);
                } else {
                    const left = s.cds[slot];
                    const total = ab.ability.cooldown || 1;
                    ab.sweep.style.background = left > 0 ? `conic-gradient(rgba(3, 0, 21, 0.72) ${(left / total) * 360}deg, transparent 0)` : "none";
                    ab.label.textContent = left > 0 ? left.toFixed(1) : "";
                    ab.btn.classList.toggle("ready", left <= 0);
                }
            });

            const items = [...fight.buffs[i].values()].map(b => {
                const left = b.duration != null ? Math.max(0, b.t + b.duration - snap.t) : null;
                return el("li", {},
                    b.itemSource ? el("img", { src: `img/item/${item(b.itemSource)?.sprite}`, alt: "" }) : el("span", { class: "buff-dot" }),
                    el("span", { class: "grow", text: buffLabel(b) }),
                    b.stacks > 1 ? el("span", { class: "stacks", text: `${b.stacks}` }) : null,
                    el("b", { text: buffValue(b) }),
                    left != null ? el("span", { class: "buff-time", text: `${left.toFixed(1)}s` }) : null);
            });
            for (const m of s.mods) {
                const a = abilityById(m.id);
                items.push(el("li", { class: m.debuff ? "debuff" : "ability-buff" },
                    el("span", { class: "key small-key" + (a?.ultimate ? " ult" : ""), text: a?.key.toUpperCase() ?? "?" }),
                    el("span", { class: "grow", text: a?.name ?? m.id }),
                    el("span", { class: "buff-time", text: `${Math.max(0, m.left).toFixed(1)}s` })));
            }
            p.buffs.replaceChildren(...(items.length ? items : [el("li", { class: "muted small", text: "None" })]));
        }
        document.getElementById("cTime").textContent = `${snap.t.toFixed(1)}s`;
        const ot = document.getElementById("cOvertime");
        if (snap.suddenDeath) { ot.textContent = "Sudden death"; ot.className = "overtime sudden"; }
        else if (snap.overtime) { ot.textContent = `Overtime · damage ×${snap.overtime.toFixed(1)}`; ot.className = "overtime on"; }
        else { ot.textContent = ""; ot.className = "overtime"; }
    }

    function end(msg) {
        if (!fight || msg.fightId !== fight.id) return;
        fight.ended = true;
        const r = msg.result;
        if (fight.you == null) {
            const title = document.getElementById("cOvertime");
            title.textContent = r.winner === -1 ? "Draw" : `${r.fighters[r.winner].name} wins`;
            title.className = "overtime on";
            return;
        }
        const you = fight.you;
        const won = r.winner === you, draw = r.winner === -1;
        const title = document.getElementById("resultTitle");
        title.textContent = draw ? "Draw" : won ? "Victory" : "Defeat";
        title.className = draw ? "draw" : won ? "win" : "lose";
        const opp = 1 - you;
        const rows = [
            ["Opponent", r.fighters[opp].name],
            ["Damage dealt", Math.round(r.damageDealt[you])],
            ["Damage taken", Math.round(r.damageTaken[you])],
            ["Fight length", `${r.duration.toFixed(1)}s`],
            ["Money earned in fight", `${r.earnings[you]}$`]
        ];
        if (won && r.damageTaken[you] <= 0) rows.push(["Flawless victory", "Yes"]);
        document.getElementById("resultStats").replaceChildren(...rows.map(([k, v]) =>
            el("li", {}, el("span", { class: "muted", text: k }), el("b", { text: String(v) }))));
        setTimeout(() => { if (fight?.ended && fight.you != null) document.getElementById("resultScreen").hidden = false; }, 900);
        updateWait(app.game.state);
    }

    function updateWait(state) {
        const wait = document.getElementById("resultWait");
        const list = document.getElementById("resultFights");
        if (!state) return;
        clearInterval(updateWait.timer);
        if (state.phase === "results") {
            let left = state.phaseRemaining ?? 0;
            const show = () => { wait.textContent = `Lives left: ${state.you?.lives ?? 0}. Next shop in ${formatTime(left)}s`; };
            show();
            updateWait.timer = setInterval(() => { left -= 0.25; if (left <= 0) clearInterval(updateWait.timer); else show(); }, 250);
        } else {
            wait.textContent = "Waiting for the other fights to finish…";
        }
        const others = (state.fights ?? []).filter(f => f.id !== fight?.id);
        list.replaceChildren(...others.map(f => el("li", {},
            el("span", { class: "grow", text: `${f.names[0]} vs ${f.names[1]}` }),
            el("span", { class: "muted small", text: f.ended ? (f.winner === -1 ? "Draw" : `${f.names[f.winner]} won`) : "Live" }),
            f.ended ? null : el("button", { class: "btn btn-small", text: "Watch", onclick: () => app.socket.send("spectate", { fightId: f.id }) }))));
        list.hidden = others.length === 0;
    }

    document.addEventListener("keydown", e => {
        if (!fight || fight.ended || fight.you == null || document.body.dataset.view !== "Combat") return;
        if (e.target instanceof HTMLInputElement || e.repeat || e.ctrlKey || e.metaKey || e.altKey) return;
        const slot = KEYS.indexOf(e.key.toLowerCase());
        if (slot < 0) return;
        e.preventDefault();
        app.socket.send("ability", { slot });
        const ab = fight.panel(fight.you).abilities[slot];
        ab?.btn.classList.remove("pressed");
        void ab?.btn.offsetWidth;
        ab?.btn.classList.add("pressed");
    });

    document.getElementById("leaveSpectate").addEventListener("click", () => {
        stop();
        app.showGame();
    });

    return {
        start,
        tick,
        end,
        stop,
        get active() { return !!fight; },
        get spectating() { return fight?.you == null; },
        onState(state) { if (fight?.ended || fight?.you == null) updateWait(state); }
    };
}
