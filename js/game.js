// Match view during the shop phase: top bar, players, events, shop, build and stats.
import { el, item, itemCard, statPretty, kitCard, data, MAX_LEVEL } from "./data.js";
import { openDialog, resolveDialog, formatTime } from "./ui.js";

export function createGameView(app) {
    let state = null;
    let timerEnd = 0;

    document.getElementById("rerollButton").addEventListener("click", () => app.socket.send("reroll"));
    document.getElementById("hpButton").addEventListener("click", () => app.socket.send("buyHp"));
    document.getElementById("readyButton").addEventListener("click", () => {
        if (state?.you) app.socket.send("ready", { ready: !state.you.ready });
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

    function renderPity(you, open) {
        const panel = document.getElementById("pityPanel");
        const offer = you.shop.pity ?? [];
        panel.hidden = offer.length === 0;
        if (!offer.length) return;
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

    function renderShop(you) {
        const grid = document.getElementById("shopGrid");
        const open = state.phase === "shop" && !you.eliminated;
        renderPity(you, open);
        grid.replaceChildren(...you.shop.items.map((slot, i) => {
            const it = item(slot.itemId);
            const mine = owned(you, it.id);
            const maxed = mine && mine.level >= MAX_LEVEL;
            const price = slot.price ?? it.price;
            const canBuy = open && !slot.bought && !maxed && you.money >= price;
            const label = slot.bought ? "Bought" : maxed ? "Max level" : mine ? `Level up → ${mine.level + 1}` : `Buy`;
            return el("div", { class: "shop-item" + (slot.bought ? " bought" : "") + (it.pack !== "generic" ? " special" : "") },
                itemCard(it.id, { level: mine && !slot.bought ? mine.level + 1 : 1, size: "large" }),
                el("div", { class: "shop-name", text: it.name }),
                el("div", { class: "shop-type muted small", text: it.pack !== "generic" ? `Special ${it.type}` : it.type }),
                el("button", { class: "btn btn-buy", disabled: !canBuy, onclick: () => buy(i) },
                    el("span", { text: label }), slot.bought ? null : el("b", {},
                        price !== it.price ? el("s", { class: "muted", text: `${it.price}$` }) : null, ` ${price}$`)));
        }));

        const reroll = document.getElementById("rerollButton");
        reroll.textContent = `Reroll (${you.shop.rerollsLeft})`;
        reroll.disabled = !open || you.shop.locked || you.shop.rerollsLeft <= 0;
        const hp = document.getElementById("hpButton");
        hp.textContent = `+HP ×${state.settings.hpUpgradeMult} · ${you.hpUpgradeCost}$`;
        hp.disabled = !open || you.money < you.hpUpgradeCost;
        const ready = document.getElementById("readyButton");
        ready.textContent = you.ready ? "Not ready" : "Ready";
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
        const slot = (target, entry, label) => {
            target.replaceChildren(entry ? itemCard(entry.id, { level: entry.level }) : el("div", { class: "slot-empty", text: label }));
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

    function renderSide() {
        document.getElementById("gPlayers").replaceChildren(...state.players.map(p =>
            el("li", { class: (p.alive ? "" : "dead ") + (p.connected ? "" : "offline") },
                el("span", { class: "dot" + (p.connected ? " on" : "") }),
                el("span", { class: "grow" }, p.name, p.id === app.user.id ? el("span", { class: "muted", text: " (you)" }) : null),
                !p.alive ? el("span", { class: "tag dead", text: "Out" })
                    : state.phase === "shop" ? el("span", { class: "tag" + (p.ready ? " ready" : ""), text: p.ready ? "Ready" : "Shopping" }) : null)));
        document.getElementById("gEvents").replaceChildren(...state.events.map(e =>
            el("li", { class: e.claimedBy ? "claimed" : "" },
                el("div", { class: "row spread" }, el("b", { text: e.name }), el("span", { class: "small muted", text: e.reward })),
                el("div", { class: "small muted", text: e.claimedBy ? `Claimed by ${e.claimedBy}` : e.description }))));
    }

    return {
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
            renderFights(you);
            if (!you) return;
            const kit = data.kits[you.kit];
            document.getElementById("myKit").replaceChildren(el("span", { class: "muted small", text: "Kit: " }), el("b", { text: kit?.name ?? you.kit }),
                el("span", { class: "kit-keys" }, (kit?.abilities ?? []).map(a => el("span", { class: "key" + (a.ultimate ? " ult" : ""), title: a.name, text: a.key.toUpperCase() }))));
            const changeKit = document.getElementById("changeKit");
            changeKit.textContent = `Change kit (${state.kitSwapCost}$)`;
            changeKit.disabled = state.phase !== "shop" || you.eliminated || you.money < state.kitSwapCost;
            document.getElementById("gMoney").textContent = `${you.money}$`;
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
}
