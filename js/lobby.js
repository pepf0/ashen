// Waiting room: members, host settings, start/leave/kick.
import { el, data, kitCard } from "./data.js";
import { nameEl } from "./profile.js";
import { toast } from "./ui.js";

const FIELDS = [
    { key: "public", label: "Public lobby", type: "checkbox" },
    { key: "lives", label: "Starting lives", min: 1, max: 20 },
    { key: "shopSeconds", label: "Shop timer (s)", min: 10, max: 600 },
    { key: "startMoney", label: "Starting money", min: 0, max: 10000 },
    { key: "income", label: "Income per round", min: 0, max: 1000 },
    { key: "winBonus", label: "Win bonus", min: 0, max: 1000 },
    { key: "specialMode", label: "Special items unique", type: "select", options: [["perPlayer", "Per player"], ["perLobby", "Per lobby"]] },
    { key: "startHp", label: "Starting HP", min: 1, max: 1000000 },
    { key: "hpUpgradeMult", label: "HP upgrade multiplier", min: 1, max: 10, step: 0.05 },
    { key: "hpUpgradeCost", label: "HP upgrade cost", min: 0, max: 10000 },
    { key: "hpUpgradeCostIncrease", label: "HP cost increase", min: 0, max: 10000 }
];

export function createLobbyView(app) {
    const fieldsRoot = document.getElementById("settingsFields");
    const inputs = {};
    // Edits not yet confirmed by the server, so a lobby update can't overwrite what the host just typed.
    let pending = {};
    let current = {};

    for (const f of FIELDS) {
        let input;
        if (f.type === "checkbox") input = el("input", { type: "checkbox" });
        else if (f.type === "select") input = el("select", {}, f.options.map(([v, l]) => el("option", { value: v, text: l })));
        else input = el("input", { type: "number", min: f.min, max: f.max, step: f.step ?? 1 });
        input.addEventListener("change", () => {
            pending[f.key] = { value: readField(f), at: performance.now() };
            const edits = Object.fromEntries(Object.entries(pending).map(([k, p]) => [k, p.value]));
            app.socket.send("settings", { settings: { ...current, ...edits } });
        });
        inputs[f.key] = input;
        fieldsRoot.append(el("label", { class: f.type === "checkbox" ? "check" : "field" },
            f.type === "checkbox" ? input : null, el("span", { text: f.label }), f.type === "checkbox" ? null : input));
    }

    function readField(f) {
        const input = inputs[f.key];
        if (f.type === "checkbox") return input.checked;
        if (f.type === "select") return input.value;
        return Number(input.value);
    }

    document.getElementById("lobbyCode").addEventListener("click", async () => {
        try {
            await navigator.clipboard.writeText(app.lobby.code);
            toast("Lobby code copied", "info");
        } catch { /* clipboard blocked */ }
    });
    document.getElementById("startMatch").addEventListener("click", () => app.socket.send("start"));
    document.getElementById("addBot").addEventListener("click", () =>
        app.socket.send("addBot", { difficulty: document.getElementById("botDifficulty").value }));
    document.getElementById("leaveLobby").addEventListener("click", () => app.socket.send("leave"));

    return {
        render(lobby) {
            const isHost = lobby.hostId === app.user.id;
            document.getElementById("lobbyCode").textContent = lobby.code;
            document.getElementById("playerCount").textContent = `${lobby.members.length}/8`;
            document.getElementById("memberList").replaceChildren(...lobby.members.map(m =>
                el("li", { class: m.connected ? "" : "offline" },
                    el("span", { class: "dot" + (m.connected ? " on" : "") }),
                    el("span", { class: "grow" }, nameEl(m.name, m.appearance, m.id), m.mastery ? el("span", { class: "mastery", text: ` ${m.mastery}` }) : null),
                    m.bot ? el("span", { class: "tag bot", text: `Bot · ${m.bot}` }) : null,
                    el("span", { class: "tag", text: data.kits[m.kit]?.name ?? m.kit }),
                    m.id === lobby.hostId ? el("span", { class: "tag", text: "Host" }) : null,
                    isHost && m.id !== app.user.id
                        ? el("button", { class: "btn btn-small btn-ghost", text: "Kick", onclick: () => app.socket.send("kick", { userId: m.id }) })
                        : null)));
            const start = document.getElementById("startMatch");
            start.hidden = !isHost;
            start.disabled = lobby.members.filter(m => m.connected).length < 2;
            document.getElementById("addBotRow").hidden = !isHost || lobby.members.length >= 8;
            document.getElementById("lobbyHint").textContent = isHost
                ? (start.disabled ? "Waiting for at least one more player (or add a bot)." : "Start whenever everyone is here.")
                : "Waiting for the host to start the match.";
            const mine = lobby.members.find(m => m.id === app.user.id)?.kit;
            document.getElementById("kitGrid").replaceChildren(...Object.keys(data.kits).map(id =>
                kitCard(id, { selected: id === mine, onclick: () => app.socket.send("kit", { kit: id }) })));
            current = lobby.settings;
            for (const [key, p] of Object.entries(pending)) {
                // Confirmed, or old enough that the server must have clamped it.
                if (lobby.settings[key] === p.value || performance.now() - p.at > 1500) delete pending[key];
            }
            for (const f of FIELDS) {
                const input = inputs[f.key];
                input.disabled = !isHost;
                if (document.activeElement === input && isHost) continue;
                const value = pending[f.key]?.value ?? lobby.settings[f.key];
                if (f.type === "checkbox") input.checked = !!value;
                else input.value = value;
            }
        }
    };
}
