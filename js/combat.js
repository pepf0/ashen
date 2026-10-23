// Live fight view. The server runs the fight in real time and streams ticks; Q/W/E/R send abilities.
import { data, el, item, itemCard, abilityById, abilityTooltip, attachTooltip, abilityPretty } from "./data.js";
import { formatTime } from "./ui.js";
import { label as keyLabelOf, actionFor, SKILL_ACTIONS } from "./keybinds.js";
import { nameEl, portraitEl } from "./profile.js";
import { Arena } from "./arena.js";
import { sfx } from "./fx.js";
import { skinApplies, skinnedUrl } from "./skins.js";

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
            "aria-label": `${a.name}: ${abilityPretty(a)}`,
            onclick: mine ? () => app.socket.send("ability", { slot }) : undefined
        }, sweep, el("span", { class: "ab-key", text: mine ? keyLabelOf(SKILL_ACTIONS[slot]) : a.key.toUpperCase() }), el("span", { class: "ab-name", text: a.name }), label);
        attachTooltip(btn, () => abilityTooltip(a, mine ? keyLabelOf(SKILL_ACTIONS[slot]) : a.key.toUpperCase()));
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
        const weaponCard = weapon ? itemCard(weapon.id, { level: weapon.level, size: "large", skin: info.appearance?.weaponSkin }) : el("div", { class: "slot-empty big", text: "No weapon" });
        const others = info.items.filter(i => i.slot !== "weapon");
        const buffs = el("ul", { class: "buff-list" });
        const kit = info.kit ? data.kits[info.kit] : null;
        const abilities = kit ? kit.abilities.map((a, i) => abilityButton(a, i, isYou && fight?.you != null)) : [];

        root.replaceChildren(
            el("div", { class: "fighter-head" },
                portraitEl(info.kit, info.appearance, "md"),
                el("h3", {}, nameEl(info.name, info.appearance, info.playerId ?? 0),
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
            buffs);
        root.classList.toggle("you", isYou);
        root.classList.remove("stunned", "invulnerable", "sudden", "dead");
        root.className = root.className.replace(/\bframe-\S+/g, "").trim();
        if (info.appearance?.frame) root.classList.add(`frame-${info.appearance.frame}`);
        return { root, hpFill, hpGhost, hpShield, hpText, status, cdFill, weaponCard, buffs, info, abilities };
    }

    function hurt(panel) {
        panel.root.classList.remove("hurt");
        void panel.root.offsetWidth;
        panel.root.classList.add("hurt");
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
        fight.arena = new Arena(document.getElementById("arenaStage"), {
            fighters: msg.fighters.map(f => ({ name: f.name, kit: f.kit, appearance: f.appearance, items: f.items })),
            left, arenaId: msg.arena?.id, seed: typeof msg.fightId === "number" ? msg.fightId : 7
        });
        document.getElementById("arenaName").textContent = msg.arena?.name ?? "The Ashen Wastes";
        fight.lastCount = null;
        document.getElementById("cRound").textContent = msg.you == null ? `Round ${msg.round} · spectating` : `Round ${msg.round}`;
        const mod = document.getElementById("cModifier");
        mod.textContent = [msg.modifier ? `${msg.modifier.name}: ${msg.modifier.description}` : null,
            msg.arena ? msg.arena.description : null].filter(Boolean).join("  ·  ");
        mod.className = "arena-modifier " + (msg.modifier ? (msg.modifier.positive ? "good" : "bad") : "");
        document.getElementById("resultScreen").hidden = true;
        document.getElementById("spectatorBar").hidden = msg.you != null;
        render();
        if (msg.ended && msg.result) end({ fightId: msg.fightId, result: msg.result });
        loop();
    }

    function stop() {
        if (fight?.raf) cancelAnimationFrame(fight.raf);
        fight?.arena?.destroy();
        if (replay?.raf) cancelAnimationFrame(replay.raf);
        replay = null;
        fight = null;
        document.getElementById("countdown").hidden = true;
        document.getElementById("replayControls").hidden = true;
        document.getElementById("spectatorText").textContent = "You are watching this fight.";
    }

    // ---------------- replays ----------------
    let replay = null;
    let replayBack = null;

    /** Plays a stored fight (from /api/replays/{id}) in this view. back() is called by the Back button. */
    function playReplay(data, back) {
        stop();
        replayBack = back;
        start({
            fightId: "replay", round: data.round, you: null, countdown: 0, modifier: data.modifier, arena: data.arena,
            fighters: data.fighters, buffs: data.fighters.map(f => f.startBuffs), snap: data.snaps[0]
        });
        document.getElementById("cRound").textContent = `Replay · Round ${data.round}`;
        document.getElementById("spectatorText").textContent = `${data.fighters[0].name} vs ${data.fighters[1].name}`;
        document.getElementById("replayControls").hidden = false;
        replay = { data, time: 0, speed: 1, paused: false, e: 0, s: 0, last: performance.now(), raf: 0, done: false };
        replayFrame();
    }

    function replayFrame() {
        if (!replay || !fight) return;
        const now = performance.now();
        if (!replay.paused) replay.time += ((now - replay.last) / 1000) * replay.speed;
        replay.last = now;
        const { events, snaps, result } = replay.data;
        while (replay.e < events.length && events[replay.e].t <= replay.time) apply(events[replay.e++]);
        while (replay.s + 1 < snaps.length && snaps[replay.s + 1].t <= replay.time) replay.s++;
        fight.snap = snaps[replay.s];
        render();
        if (!replay.done && replay.time >= result.duration + 0.2) {
            replay.done = true;
            end({ fightId: "replay", result });
        }
        replay.raf = requestAnimationFrame(replayFrame);
    }

    document.getElementById("replayPause").addEventListener("click", e => {
        if (!replay) return;
        replay.paused = !replay.paused;
        e.target.textContent = replay.paused ? "Play" : "Pause";
    });
    document.querySelectorAll("#replayControls [data-speed]").forEach(b => b.addEventListener("click", () => {
        if (replay) replay.speed = Number(b.dataset.speed);
    }));
    document.getElementById("replayRestart").addEventListener("click", () => {
        if (replay) playReplay(replay.data, replayBack);
    });

    function loop() {
        if (!fight) return;
        const left = (fight.countdownEnd - performance.now()) / 1000;
        const cd = document.getElementById("countdown");
        if (left > -0.6) {
            const text = left > 0 ? `${Math.ceil(left)}` : "Fight!";
            if (text !== fight.lastCount) {
                fight.lastCount = text;
                cd.hidden = false;
                cd.textContent = text;
                cd.classList.toggle("go", left <= 0);
                cd.classList.remove("pop");
                void cd.offsetWidth;
                cd.classList.add("pop");
                sfx(left > 0 ? "tick" : "go");
            }
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
        fight.arena?.event(ev);
        const target = fight.panel(ev.target);
        const source = fight.panel(ev.source);
        switch (ev.kind) {
            case "damage":
                if (ev.weaponAttack || ev.abilitySource === "lunge" || ev.abilitySource === "flurry") {
                    source.weaponCard.classList.remove("swing");
                    void source.weaponCard.offsetWidth;
                    source.weaponCard.classList.add("swing");
                }
                if (!ev.dodged && !ev.blocked && !ev.missed && !ev.parried && ev.damage > 0 && !ev.suddenDeath) hurt(target);
                break;
            case "buff":
                if (ev.stacks > 0) fight.buffs[ev.source].set(ev.effectSource, { ...ev });
                else fight.buffs[ev.source].delete(ev.effectSource);
                break;
        }
    }

    function render() {
        const snap = fight.snap;
        if (!snap) return;
        fight.arena?.snap(snap);
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
            const statusKey = `${s.stun.toFixed(1)}|${s.invuln.toFixed(1)}|${s.parry}|${s.windup == null ? "" : Math.max(0, s.windup).toFixed(1)}`;
            if (p.statusKey !== statusKey) p.status.replaceChildren(...[
                s.stun > 0 ? el("span", { class: "badge stun", text: `Stunned ${s.stun.toFixed(1)}s` }) : null,
                s.invuln > 0 ? el("span", { class: "badge invuln", text: `Invulnerable ${s.invuln.toFixed(1)}s` }) : null,
                s.parry ? el("span", { class: "badge invuln", text: "Parry ready" }) : null,
                s.windup != null ? el("span", { class: "badge stun", text: `Headshot in ${Math.max(0, s.windup).toFixed(1)}s` }) : null
            ].filter(Boolean));
            p.statusKey = statusKey;

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

            // Only rebuild the buff list when something visible changed (stacks, values, timers at 0.1s).
            const buffKey = [...fight.buffs[i].values()].map(b => `${b.effectSource}:${b.stacks}:${b.mult}:${b.add}:${b.duration != null ? Math.max(0, b.t + b.duration - snap.t).toFixed(1) : ""}`).join(",")
                + "|" + s.mods.map(m => `${m.id}:${Math.max(0, m.left).toFixed(1)}`).join(",");
            if (p.buffKey === buffKey) continue;
            p.buffKey = buffKey;
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
                    el("span", { class: "key small-key" + (a?.ultimate ? " ult" : ""), text: a ? a.key.toUpperCase() : "?" }),
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
        fight.arena?.finish(r.winner);
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
        setTimeout(() => sfx(won ? "victory" : "defeat"), 500);
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
        setTimeout(() => { if (fight?.ended && fight.you != null) document.getElementById("resultScreen").hidden = false; }, 2100);
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
        const action = actionFor(e, SKILL_ACTIONS);
        if (!action) return;
        const slot = SKILL_ACTIONS.indexOf(action);
        e.preventDefault();
        app.socket.send("ability", { slot });
        const ab = fight.panel(fight.you).abilities[slot];
        ab?.btn.classList.remove("pressed");
        void ab?.btn.offsetWidth;
        ab?.btn.classList.add("pressed");
    });

    document.getElementById("leaveSpectate").addEventListener("click", () => {
        const back = replay ? replayBack : null;
        stop();
        if (back) back(); else app.showGame();
    });

    return {
        playReplay,
        start,
        tick,
        end,
        stop,
        get active() { return !!fight; },
        get spectating() { return fight?.you == null; },
        onState(state) { if (fight?.ended || fight?.you == null) updateWait(state); }
    };
}
