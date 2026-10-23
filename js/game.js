// Match view during the shop phase: top bar, players, events, shop, build and stats.
import { el, item, itemCard, statPretty, kitCard, augmentCard, data, abilityPretty, abilityTooltip, attachTooltip } from "./data.js";
import { openDialog, resolveDialog, formatTime } from "./ui.js";
import { label as keyLabelOf, actionFor, onKeybindsChanged, SKILL_ACTIONS } from "./keybinds.js";
import { nameEl, portraitEl } from "./profile.js";
import { sfx } from "./fx.js";

const SHOP_ACTIONS = ["reroll", "ready", "hp", "buy1", "buy2", "buy3", "buy4", "buy5", "buy6"];
const hint = action => el("span", { class: "kb-hint", text: keyLabelOf(action) });

export function createGameView(app) {
    let state = null;
    let timerEnd = 0;
    let rerender = () => {};
    // For "juice": what changed since the last render (new roll, bought slot, level up, money).
    let lastRoll = "", lastBought = new Set(), lastLevels = new Map(), shownMoney = null, moneyAnim = 0;
    const mySkin = () => state?.players?.find(p => p.id === app.user?.id)?.appearance?.weaponSkin;

    function animateMoney(target) {
        const node = document.getElementById("gMoney");
        if (shownMoney === null || shownMoney === target) { shownMoney = target; node.textContent = `${target}$`; return; }
        const from = shownMoney, start = performance.now();
        node.parentElement.classList.remove("gain", "spend");
        void node.offsetWidth;
        node.parentElement.classList.add(target > from ? "gain" : "spend");
        cancelAnimationFrame(moneyAnim);
        const step = now => {
            const k = Math.min(1, (now - start) / 450);
            shownMoney = Math.round(from + (target - from) * (1 - Math.pow(1 - k, 3)));
            node.textContent = `${shownMoney}$`;
            if (k < 1) moneyAnim = requestAnimationFrame(step);
        };
        moneyAnim = requestAnimationFrame(step);
    }

    document.getElementById("rerollButton").addEventListener("click", () => { sfx("reroll"); app.socket.send("reroll"); });
    document.getElementById("hpButton").addEventListener("click", () => app.socket.send("buyHp"));
    document.getElementById("readyButton").addEventListener("click", () => {
        if (state?.you) { if (!state.you.ready) sfx("ready"); app.socket.send("ready", { ready: !state.you.ready }); }
    });
    document.getElementById("forfeit").addEventListener("click", async () => {
        const eliminated = state?.you?.eliminated;
        const ok = await openDialog(eliminated ? "Leave the match?" : "Forfeit the match?",
            el("p", { text: eliminated
                ? "You'll go back to the menu and miss the final results."
                : "You lose all your lives and are eliminated right away." }),
            [{ label: "Stay", value: false }, { label: eliminated ? "Leave" : "Forfeit", value: true, kind: "btn-danger" }]);
        if (ok) app.socket.send("leave");
    });

    document.getElementById("changeKit").addEventListener("click", async () => {
        const you = state?.you;
        if (!you) return;
        const choice = await openDialog(`Change kit (${state.kitSwapCost}$)`,
            el("div", { class: "kit-grid" }, Object.keys(data.kits).map(id =>
                kitCard(id, { selected: id === you.kit, onclick: id === you.kit ? undefined : () => resolveDialog(id) }))),
            [{ label: "Cancel", value: null }]);
        if (choice) app.socket.send("kit", { kit: choice });
    });

    // Shop hotkeys (only while the shop view is open and you're not typing).
    document.addEventListener("keydown", e => {
        if (document.body.dataset.view !== "Game" || !state?.you || state.phase !== "shop") return;
        if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement || e.repeat || e.ctrlKey || e.metaKey || e.altKey) return;
        if (!document.getElementById("dialog").hidden) return;
        const action = actionFor(e, SHOP_ACTIONS);
        if (!action) return;
        e.preventDefault();
        if (action === "reroll") document.getElementById("rerollButton").click();
        else if (action === "ready") document.getElementById("readyButton").click();
        else if (action === "hp") document.getElementById("hpButton").click();
        else document.querySelectorAll("#shopGrid .btn-buy")[Number(action.slice(3)) - 1]?.click();
    });
    onKeybindsChanged(() => { if (state) rerender(state); });

    setInterval(() => {
        if (!state || state.phaseRemaining == null) return;
        document.getElementById("gTimer").textContent = formatTime((timerEnd - performance.now()) / 1000);
    }, 200);

    function owned(you, id) {
        return [you.weapon, you.shirt, ...you.dynamics].find(x => x && x.id === id);
    }

    /** action is "buy" for the shop roll or "buyPity" for the weaponless offer. */
    async function buy(index, action = "buy") {
        const you = state.you;
        const slot = (action === "buyPity" ? you.shop.pity : you.shop.items)[index];
        const it = item(slot.itemId);
        const mine = owned(you, it.id);
        if (mine) return app.socket.send(action, { slot: index });

        if (it.type === "weapon" || it.type === "shirt") {
            const current = it.type === "weapon" ? you.weapon : you.shirt;
            if (current) {
                const ok = await openDialog(`Replace your ${it.type}?`,
                    el("p", { text: `${item(current.id).name}${current.level > 1 ? ` (Lv ${current.level})` : ""} will be replaced by ${it.name}. There is no refund.` }),
                    [{ label: "Cancel", value: false }, { label: "Replace", value: true, kind: "btn-primary" }]);
                if (!ok) return;
            }
            return app.socket.send(action, { slot: index });
        }

        if (you.dynamics.every(d => d)) {
            const choice = await openDialog(`Replace which item with ${it.name}?`,
                el("div", { class: "pick-row" }, you.dynamics.map((d, i) =>
                    el("button", { class: "pick", onclick: () => resolveDialog(i) },
                        itemCard(d.id, { level: d.level }), el("span", { text: item(d.id).name })))),
                [{ label: "Cancel", value: null }]);
            if (choice === null || choice === undefined) return;
            return app.socket.send(action, { slot: index, replace: choice });
        }
        app.socket.send(action, { slot: index });
    }

    const SOURCE_TEXT = {
        weaponless: "Empty Hands: you fought rounds 1 and 2 without a weapon, so grab a special weapon and its buff for a third of the price.",
        chest: "From the chest: free.",
        shrine: "From the shrine: free."
    };

    function renderPity(you, open) {
        const panel = document.getElementById("pityPanel");
        const offer = you.shop.pity ?? [];
        panel.hidden = offer.length === 0;
        if (!offer.length) return;
        document.getElementById("pityText").textContent = [...new Set(offer.map(o => SOURCE_TEXT[o.source] ?? ""))].join(" ")
            + " Buying these doesn't lock your rerolls. They're gone after this shop.";
        document.getElementById("pityGrid").replaceChildren(...offer.map((slot, i) => {
            const it = item(slot.itemId);
            const canBuy = open && !slot.bought && you.money >= slot.price;
            return el("div", { class: "shop-item special" + (slot.bought ? " bought" : "") },
                itemCard(it.id, { size: "large" }),
                el("div", { class: "shop-name", text: it.name }),
                el("div", { class: "shop-type muted small" }, el("s", { text: `${it.price}$` }), ` Special ${it.type}`),
                el("button", { class: "btn btn-buy", disabled: !canBuy, onclick: () => buy(i, "buyPity") },
                    el("span", { text: slot.bought ? "Bought" : "Buy" }), slot.bought ? null : el("b", { text: `${slot.price}$` })));
        }));
    }

    /** Why a shop item fits the current build (pack synergy, buffs an owned item, buffed by one). */
    function synergies(you, it) {
        const ownedItems = [you.weapon, you.shirt, ...you.dynamics].filter(Boolean).map(o => item(o.id));
        const reasons = [];
        if (it.pack !== "generic" && ownedItems.some(o => o.pack === it.pack && o.id !== it.id)) reasons.push("Pack synergy");
        const targets = (it.effects ?? []).map(id => data.effects[id]?.target);
        for (const o of ownedItems) {
            if (o.id === it.id) continue;
            if (targets.includes(o.id)) reasons.push(`Buffs ${o.name}`);
            if ((o.effects ?? []).some(id => data.effects[id]?.target === it.id)) reasons.push(`Boosted by ${o.name}`);
        }
        return [...new Set(reasons)];
    }

    function renderShop(you) {
        const grid = document.getElementById("shopGrid");
        const open = state.phase === "shop" && !you.eliminated;
        renderPity(you, open);
        const rollKey = `${state.round}|${you.shop.items.map(x => x.itemId).join(",")}`;
        const newRoll = rollKey !== lastRoll;
        const bought = new Set(you.shop.items.map((x, i) => x.bought ? i : -1).filter(i => i >= 0));
        const justBought = newRoll ? new Set() : new Set([...bought].filter(i => !lastBought.has(i)));
        if (justBought.size) sfx("buy");
        lastRoll = rollKey;
        lastBought = bought;
        grid.replaceChildren(...you.shop.items.map((slot, i) => {
            const it = item(slot.itemId);
            const mine = owned(you, it.id);
            const maxed = mine && mine.level >= (state.maxLevel ?? 5);
            const price = slot.price ?? it.price;
            const canBuy = open && !slot.bought && !maxed && you.money >= price;
            const label = slot.bought ? "Bought" : maxed ? "Max level" : mine ? `Lv ${mine.level + 1}` : `Buy`;
            const syn = slot.bought ? [] : synergies(you, it);
            return el("div", { class: "shop-item" + (slot.bought ? " bought" : "") + (it.pack !== "generic" ? ` special pack-${it.pack}` : "") + (syn.length ? " synergy" : "")
                + (newRoll ? " deal" : "") + (justBought.has(i) ? " just-bought" : ""), style: `--i:${i}`, dataset: { pack: it.pack } },
                syn.length ? el("span", { class: "synergy-tag", title: syn.join(", "), text: syn[0] + (syn.length > 1 ? ` +${syn.length - 1}` : "") }) : null,
                itemCard(it.id, { level: mine && !slot.bought ? mine.level + 1 : 1, size: "large", skin: mySkin() }),
                el("div", { class: "shop-name", text: it.name }),
                el("div", { class: "shop-type muted small", text: it.pack !== "generic" ? `Special ${it.type}` : it.type }),
                i < 6 ? el("span", { class: "kb-hint slot-hint", text: keyLabelOf(`buy${i + 1}`) }) : null,
                el("button", { class: "btn btn-buy", disabled: !canBuy, onclick: () => buy(i) },
                    el("span", { text: label }), slot.bought ? null : el("b", {},
                        price !== it.price ? el("s", { class: "muted", text: `${it.price}$` }) : null, ` ${price}$`)));
        }));

        const reroll = document.getElementById("rerollButton");
        reroll.replaceChildren(`Reroll (${you.shop.rerollsLeft})`, hint("reroll"));
        reroll.disabled = !open || you.shop.locked || you.shop.rerollsLeft <= 0;
        const hp = document.getElementById("hpButton");
        hp.replaceChildren(`+HP ×${state.settings.hpUpgradeMult} · ${you.hpUpgradeCost}$`, hint("hp"));
        hp.disabled = !open || you.money < you.hpUpgradeCost;
        const ready = document.getElementById("readyButton");
        ready.replaceChildren(you.ready ? "Not ready" : "Ready", hint("ready"));
        ready.classList.toggle("is-ready", you.ready);
        ready.disabled = !open;
        const income = you.income ?? {};
        const labels = { interest: "interest", income: "income", lossStreak: "loss streak bonus", tax: "tax" };
        document.getElementById("incomeNote").textContent = Object.keys(income).length
            ? "This shop: " + Object.entries(income).map(([k, v]) => `${v >= 0 ? "+" : ""}${v}$ ${labels[k] ?? k}`).join(", ")
            : "";
        document.getElementById("shopNote").textContent = you.shop.locked
            ? "You bought from this roll, so rerolls are locked until the next shop. You can still buy the rest."
            : `Special item chance per slot: ${statPretty("special_chance", you.specialChance)[1]}`;
    }

    function renderBuild(you) {
        const levels = new Map([you.weapon, you.shirt, ...you.dynamics].filter(Boolean).map(x => [x.id, x.level]));
        const grown = [...levels].filter(([id, lv]) => lastLevels.has(id) ? lv > lastLevels.get(id) : lastLevels.size > 0).map(([id]) => id);
        if (grown.some(id => lastLevels.has(id))) sfx("levelup");
        lastLevels = levels;
        const slot = (target, entry, label) => {
            const card = entry ? itemCard(entry.id, { level: entry.level, skin: mySkin() }) : null;
            if (card && grown.includes(entry.id)) card.classList.add("level-up");
            target.replaceChildren(card ?? el("div", { class: "slot-empty", text: label }));
        };
        slot(document.getElementById("slotWeapon"), you.weapon, "No weapon");
        slot(document.getElementById("slotShirt"), you.shirt, "No shirt");
        document.getElementById("slotDynamics").replaceChildren(...you.dynamics.map((d, i) => {
            const box = el("div", { class: "slot" });
            slot(box, d, `Slot ${i + 1}`);
            return box;
        }));

        const order = ["max_hp", "damage", "cooldown", "crit_chance", "crit_damage", "damage_reduction", "dodge_chance"];
        const stats = { ...you.stats, special_chance: you.specialChance };
        document.getElementById("statList").replaceChildren(...[...order, "special_chance"]
            .filter(k => stats[k] !== undefined)
            .map(k => {
                const [name, value] = statPretty(k, stats[k]);
                return el("li", {}, el("span", { class: "muted", text: name }), el("b", { text: value }));
            }));
    }

    function renderModifier() {
        const banner = document.getElementById("modifierBanner");
        const m = state.modifier;
        banner.hidden = !m;
        if (!m) return;
        banner.className = "modifier-banner " + (m.positive ? "good" : "bad");
        banner.replaceChildren(el("b", { text: m.name }), el("span", { text: m.description }));
    }

    function renderFights(you) {
        const panel = document.getElementById("spectatePanel");
        const fights = state.fights ?? [];
        panel.hidden = !(you?.canSpectate && fights.length);
        if (panel.hidden) return;
        document.getElementById("fightList").replaceChildren(...fights.map(f =>
            el("li", {},
                el("span", { class: "grow", text: `${f.names[0]} vs ${f.names[1]}` }),
                el("span", { class: "muted small", text: f.ended ? (f.winner === -1 ? "Draw" : `${f.names[f.winner]} won`) : "Live" }),
                el("button", { class: "btn btn-small", text: "Watch", onclick: () => app.socket.send("spectate", { fightId: f.id }) }))));
    }

    function renderAugments(you) {
        const panel = document.getElementById("augmentPanel");
        const offer = you.augmentOffer;
        panel.hidden = !offer || state.phase !== "shop";
        if (!panel.hidden)
            document.getElementById("augmentOffer").replaceChildren(...offer.map((id, i) =>
                augmentCard(id, { onclick: () => app.socket.send("augment", { index: i }) })));
        document.getElementById("myAugments").replaceChildren(...(you.augments?.length
            ? [el("span", { class: "muted small", text: "Augments: " }), ...you.augments.map(id => augmentCard(id, { small: true }))]
            : []));
    }

    function renderMystery(you) {
        const panel = document.getElementById("mysteryPanel");
        const m = you.mystery;
        panel.hidden = !m || state.phase !== "shop";
        if (panel.hidden) return;
        // Don't wipe the bet the player is typing when another player's state update arrives.
        if (panel.contains(document.activeElement) && document.activeElement.tagName === "INPUT" && !m.used) return;
        const send = arg => app.socket.send("mystery", arg === undefined ? {} : { arg: String(arg) });
        const title = { chest: "Mystery: Chest", shrine: "Mystery: Shrine", gamble: "Mystery: Gamble Table", blacksmith: "Mystery: Blacksmith", fountain: "Mystery: Fountain", library: "Mystery: Library" }[m.kind];
        const text = {
            chest: "Pay 25$ to open it: a random item (10% chance for a special), yours for free.",
            shrine: "Sacrifice 1 life to take one of these special items for free.",
            gamble: "Bet up to half your money. 50%: double it, 50%: lose it.",
            blacksmith: "Pay 15$ to level up one of your items by 1 (specials up to level 3).",
            fountain: `Pay 40$ for +1 life (up to ${you.startingLives}).`,
            library: "Get 10$ and your next kit change is free."
        }[m.kind];
        let body;
        if (m.used) body = el("p", { class: "mystery-result", text: m.result ?? "Done." });
        else if (m.kind === "shrine") body = el("div", { class: "pick-row" }, m.choices.map((id, i) =>
            el("button", { class: "pick", onclick: () => send(i) }, itemCard(id), el("span", { text: item(id).name }))));
        else if (m.kind === "gamble") {
            const input = el("input", { type: "number", min: "1", max: String(Math.floor(you.money / 2)), value: String(Math.floor(you.money / 4)) });
            body = el("div", { class: "row" }, input, el("button", { class: "btn", text: "Bet", onclick: () => send(Math.floor(Number(input.value))) }));
        } else if (m.kind === "blacksmith") {
            const owned = [you.weapon, you.shirt, ...you.dynamics].filter(Boolean);
            body = el("div", { class: "pick-row" }, owned.map(o => el("button", { class: "pick", onclick: () => send(o.slot) },
                itemCard(o.id, { level: o.level }), el("span", { text: `${item(o.id).name} → Lv ${o.level + 1}` }))));
        } else {
            const cost = { chest: 25, fountain: 40, library: 0 }[m.kind];
            body = el("button", { class: "btn btn-primary", disabled: you.money < cost, onclick: () => send() },
                m.kind === "chest" ? "Open (25$)" : m.kind === "fountain" ? "Drink (40$)" : "Read");
        }
        panel.replaceChildren(el("h3", { text: title }), el("p", { class: "muted small", text }), body);
    }

    function renderMutators() {
        document.getElementById("mutatorRow").replaceChildren(...(state.mutators ?? []).map(m =>
            el("span", { class: "mutator-chip", title: m.description }, el("b", { text: m.name }), ` ${m.description}`)));
    }

    function renderSide() {
        document.getElementById("gPlayers").replaceChildren(...state.players.map(p =>
            el("li", { class: (p.alive ? "" : "dead ") + (p.connected ? "" : "offline") },
                portraitEl(p.kit, p.appearance, "xs"),
                el("span", { class: "grow" }, nameEl(p.name, p.appearance, p.id, { showTitle: false }), p.id === app.user.id ? el("span", { class: "muted", text: " (you)" }) : null,
                    p.mastery ? el("div", { class: "mastery", text: p.mastery }) : null),
                p.bounty > 0 ? el("span", { class: "tag bounty", title: "Beat them to collect it", text: `Bounty ${p.bounty}$` }) : null,
                !p.alive ? el("span", { class: "tag dead", text: "Out" })
                    : state.phase === "shop" ? el("span", { class: "tag" + (p.ready ? " ready" : ""), text: p.ready ? "Ready" : "Shopping" }) : null)));
        document.getElementById("gEvents").replaceChildren(...state.events.map(e =>
            el("li", { class: (e.claimedBy ? "claimed" : "") + (e.special ? " special" : "") },
                el("div", { class: "row spread" }, el("b", { text: e.name }), el("span", { class: "small muted", text: e.reward })),
                el("div", { class: "small muted", text: e.claimedBy ? `${e.special ? "Taken" : "Claimed"} by ${e.claimedBy}` : e.description }))));
    }

    const view = {
        get state() { return state; },
        render(next) {
            state = next;
            const you = state.you;
            timerEnd = performance.now() + (state.phaseRemaining ?? 0) * 1000;
            document.getElementById("gRound").textContent = state.round;
            document.getElementById("gPhase").textContent = { shop: "Shop", combat: "Fighting", results: "Results" }[state.phase] ?? state.phase;
            document.getElementById("gTimer").textContent = state.phaseRemaining == null ? "–" : formatTime(state.phaseRemaining);
            renderSide();
            renderModifier();
            renderMutators();
            renderFights(you);
            if (!you) return;
            renderAugments(you);
            renderMystery(you);
            const kit = data.kits[you.kit];
            document.getElementById("myKit").replaceChildren(el("span", { class: "muted small", text: "Kit: " }), el("b", { text: kit?.name ?? you.kit }),
                el("span", { class: "kit-keys" }, (kit?.abilities ?? []).map((a, i) => {
                    const k = el("span", { class: "key" + (a.ultimate ? " ult" : ""), text: keyLabelOf(SKILL_ACTIONS[i]) });
                    attachTooltip(k, () => abilityTooltip(a, keyLabelOf(SKILL_ACTIONS[i])));
                    return k;
                })));
            // Full ability list so you always know what your kit does while shopping.
            document.getElementById("myKitAbilities").replaceChildren(...(kit?.abilities ?? []).map((a, i) => el("li", {},
                el("span", { class: "key" + (a.ultimate ? " ult" : ""), text: keyLabelOf(SKILL_ACTIONS[i]) }),
                el("div", {},
                    el("b", { text: a.name }),
                    el("span", { class: "muted small", text: a.ultimate ? "  ultimate" : `  ${a.cooldown}s` }),
                    el("div", { class: "small", text: abilityPretty(a) })))));
            const changeKit = document.getElementById("changeKit");
            changeKit.textContent = `Change kit (${state.kitSwapCost}$)`;
            changeKit.disabled = state.phase !== "shop" || you.eliminated || you.money < state.kitSwapCost;
            animateMoney(you.money);
            document.getElementById("gLives").textContent = you.eliminated ? "0" : `${you.lives}`;
            document.getElementById("gMaxHp").textContent = Math.round(you.maxHp);
            document.getElementById("forfeit").textContent = you.eliminated ? "Leave" : "Forfeit";
            document.getElementById("shopPanel").hidden = you.eliminated;
            document.getElementById("eliminatedPanel").hidden = !you.eliminated;
            if (you.eliminated) {
                document.getElementById("eliminatedText").textContent = you.placement
                    ? `You finished in place ${you.placement}. Final record: ${you.wins} wins, ${you.losses} losses.`
                    : "Better luck next time.";
            } else {
                renderShop(you);
            }
            renderBuild(you);
        }
    };
    rerender = next => view.render(next);
    return view;
}
