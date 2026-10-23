// Admin panel (only shown to admins): live lobbies, users and bans, data editor with hot reload, stats.
import { api, getToken } from "./net.js";
import { el, data } from "./data.js";
import { toast, openDialog } from "./ui.js";

const SIM_FIELDS = [
    { key: "bots", label: "Bots", value: 6, min: 2, max: 8 },
    { key: "lives", label: "Starting lives", value: 5, min: 1, max: 20, setting: true },
    { key: "startMoney", label: "Starting money", value: 100, min: 0, max: 10000, setting: true },
    { key: "income", label: "Income per round", value: 20, min: 0, max: 1000, setting: true },
    { key: "winBonus", label: "Win bonus", value: 10, min: 0, max: 1000, setting: true },
    { key: "startHp", label: "Starting HP", value: 100, min: 1, max: 1000000, setting: true },
    { key: "hpUpgradeMult", label: "HP upgrade multiplier", value: 1.2, min: 1, max: 10, step: 0.05, setting: true },
    { key: "hpUpgradeCost", label: "HP upgrade cost", value: 10, min: 0, max: 10000, setting: true },
    { key: "hpUpgradeCostIncrease", label: "HP cost increase", value: 5, min: 0, max: 10000, setting: true },
    { key: "specialChanceMult", label: "Special chance ×", value: 1, min: 0, max: 100, step: 0.25, setting: true }
];

function loadSimPrefs() { try { return JSON.parse(localStorage.getItem("ashen.sim") ?? "{}"); } catch { return {}; } }
function saveSimPrefs(p) { try { localStorage.setItem("ashen.sim", JSON.stringify(p)); } catch { /* ignore */ } }

export function createAdmin(app) {
    const root = document.getElementById("adminRoot");
    let tab = "lobbies";

    const tabs = ["lobbies", "simulate", "users", "data", "stats"];
    function header() {
        return el("div", { class: "admin-tabs" }, tabs.map(t => el("button", {
            class: "btn btn-small" + (t === tab ? " btn-primary" : ""), text: t[0].toUpperCase() + t.slice(1),
            onclick: () => { tab = t; render(); }
        })), el("button", { class: "btn btn-small btn-ghost", text: "Refresh", onclick: () => render() }));
    }

    async function render() {
        root.replaceChildren(header(), el("p", { class: "muted", text: "Loading…" }));
        try {
            const body = await ({ lobbies, simulate, users, data: dataEditor, stats }[tab])();
            root.replaceChildren(header(), body);
        } catch (err) {
            root.replaceChildren(header(), el("p", { class: "form-error", text: err.message }));
        }
    }

    async function lobbies() {
        const list = await api("admin/lobbies");
        if (!list.length) return el("p", { class: "muted", text: "No lobbies right now." });
        return el("div", { class: "admin-list" }, list.map(l => el("div", { class: "panel admin-card" },
            el("div", { class: "row spread" },
                el("b", { text: `${l.code} · ${l.state}${l.round ? ` · round ${l.round}` : ""}` }),
                el("button", { class: "btn btn-small btn-danger", text: "Close lobby", onclick: async () => {
                    await api(`admin/lobbies/${l.code}/close`, { method: "POST" }); render();
                } })),
            l.mutators.length ? el("div", { class: "muted small", text: `Mutators: ${l.mutators.join(", ")}` }) : null,
            el("ul", { class: "member-list" }, l.members.map(m => el("li", {},
                el("span", { class: "dot" + (m.connected || m.bot ? " on" : "") }),
                el("span", { class: "grow", text: `${m.name}${m.bot ? ` (bot, ${m.bot})` : ""}${m.id === l.hostId ? " · host" : ""}` }),
                m.lives != null ? el("span", { class: "muted small", text: m.eliminated ? "out" : `${m.lives} lives` }) : null,
                el("button", { class: "btn btn-small btn-ghost", text: "Kick", onclick: async () => {
                    await api(`admin/lobbies/${l.code}/kick`, { method: "POST", body: { userId: m.id } }); render();
                } })))))));
    }

    /** Bot-only match with tweakable settings, watched live at adjustable speed. */
    async function simulate() {
        const prefs = loadSimPrefs();
        const inputs = {};
        const fields = SIM_FIELDS.map(f => {
            const input = el("input", { type: "number", min: f.min, max: f.max, step: f.step ?? 1, value: String(prefs[f.key] ?? f.value) });
            inputs[f.key] = input;
            return el("label", { class: "field" }, el("span", { text: f.label }), input);
        });
        const diffs = ["easy", "normal", "hard"].map(d => {
            const box = el("input", { type: "checkbox" });
            box.checked = (prefs.difficulties ?? ["normal", "hard"]).includes(d);
            return { d, box };
        });
        const special = el("select", {}, el("option", { value: "perPlayer", text: "Per player" }), el("option", { value: "perLobby", text: "Per lobby" }));
        special.value = prefs.specialMode ?? "perPlayer";
        const mutators = el("input", { type: "checkbox" });
        mutators.checked = prefs.mutators ?? true;
        const speed = el("select", {}, [0.5, 1, 2, 4, 8, 16].map(v => el("option", { value: String(v), text: `${v}×` })));
        speed.value = String(prefs.speed ?? 4);
        const start = () => {
            const settings = { public: false, specialMode: special.value, shopSeconds: 60 };
            const p = { difficulties: diffs.filter(x => x.box.checked).map(x => x.d), specialMode: special.value, mutators: mutators.checked, speed: Number(speed.value) };
            for (const f of SIM_FIELDS) {
                p[f.key] = Number(inputs[f.key].value);
                if (f.setting) settings[f.key] = p[f.key];
            }
            if (!p.difficulties.length) p.difficulties = ["normal"];
            saveSimPrefs(p);
            app.socket.send("simStart", { settings, bots: p.bots, difficulties: p.difficulties, mutators: p.mutators, speed: p.speed });
        };
        return el("div", { class: "panel sim-setup" },
            el("h3", { text: "Bot simulation" }),
            el("p", { class: "muted small", text: "Starts a match with only bots. You watch as an observer: every bot's build and stats each round, and its fights at the speed you pick (change it or pause any time). Simulations are not saved to the history, but they are written to the balancing logs." }),
            el("div", { class: "settings-fields sim-fields" }, fields,
                el("label", { class: "field" }, el("span", { text: "Special items unique" }), special),
                el("label", { class: "field" }, el("span", { text: "Starting speed" }), speed)),
            el("div", { class: "row" }, el("span", { class: "muted small", text: "Bot difficulties:" }),
                diffs.map(x => el("label", { class: "check small" }, x.box, x.d)),
                el("label", { class: "check small" }, mutators, "Random mutators")),
            el("div", { class: "row end" },
                el("button", { class: "btn", text: "Reset", onclick: () => { saveSimPrefs({}); render(); } }),
                el("button", { class: "btn btn-primary", text: "Start simulation", onclick: start })));
    }

    async function users() {
        const input = el("input", { type: "text", placeholder: "Search username" });
        const list = el("div");
        const load = async () => {
            const rows = await api(`admin/users?q=${encodeURIComponent(input.value)}`);
            list.replaceChildren(el("table", { class: "table" },
                el("thead", {}, el("tr", {}, ["Id", "Username", "Name", "Joined", "Status", ""].map(h => el("th", { text: h })))),
                el("tbody", {}, rows.map(u => el("tr", {},
                    el("td", { text: `${u.id}` }), el("td", { text: u.username }), el("td", { text: u.name }),
                    el("td", { class: "small", text: new Date(u.createdAt).toLocaleDateString() }),
                    el("td", { class: u.banned ? "form-error" : "", text: u.banned ? `Banned${u.reason ? `: ${u.reason}` : ""}` : "OK" }),
                    el("td", {}, u.banned
                        ? el("button", { class: "btn btn-small", text: "Unban", onclick: async () => { await api(`admin/users/${u.id}/unban`, { method: "POST" }); load(); } })
                        : el("button", { class: "btn btn-small btn-danger", text: "Ban", onclick: async () => {
                            const reason = el("input", { type: "text", placeholder: "Reason (optional)" });
                            const ok = await openDialog(`Ban ${u.username}?`, reason, [{ label: "Cancel", value: false }, { label: "Ban", value: true, kind: "btn-danger" }]);
                            if (ok) { await api(`admin/users/${u.id}/ban`, { method: "POST", body: { reason: reason.value || null } }); load(); }
                        } })))))));
        };
        input.addEventListener("input", () => { clearTimeout(load.t); load.t = setTimeout(load, 250); });
        await load();
        return el("div", {}, input, list);
    }

    async function dataEditor() {
        const files = await api("admin/data");
        const select = el("select", {}, files.map(f => el("option", { value: f.file, text: f.file + (f.overridden ? " (edited)" : "") })));
        const area = el("textarea", { class: "data-editor", spellcheck: "false" });
        const note = el("p", { class: "form-note" });
        const load = async () => {
            const res = await fetch(`api/admin/data/${select.value}`, { headers: { Authorization: `Bearer ${getToken()}` } });
            area.value = await res.text();
            note.textContent = "";
        };
        const save = async () => {
            try { JSON.parse(area.value); } catch (e) { note.textContent = `Not valid JSON: ${e.message}`; return; }
            const res = await fetch(`api/admin/data/${select.value}`, {
                method: "PUT", headers: { Authorization: `Bearer ${getToken()}`, "Content-Type": "application/json" }, body: area.value
            });
            const json = await res.json();
            note.textContent = res.ok
                ? (json.applied ? "Saved and reloaded." : "Saved. It will be applied when no match is running.")
                : json.error;
            if (res.ok) toast("Data saved", "info");
        };
        const reset = async () => {
            const ok = await openDialog(`Reset ${select.value}?`, el("p", { text: "This removes your edits and goes back to the repository's version (a backup is kept)." }),
                [{ label: "Cancel", value: false }, { label: "Reset", value: true, kind: "btn-danger" }]);
            if (!ok) return;
            const json = await api(`admin/data/${select.value}`, { method: "DELETE" });
            note.textContent = json.applied ? "Reset and reloaded." : "Reset. It will be applied when no match is running.";
            await load();
        };
        select.addEventListener("change", load);
        await load();
        return el("div", { class: "data-editor-wrap" },
            el("div", { class: "row" }, select,
                el("button", { class: "btn btn-primary", text: "Validate & save", onclick: save }),
                el("button", { class: "btn btn-ghost", text: "Reset to repository version", onclick: reset })),
            el("p", { class: "muted small", text: "The whole data set is validated before saving. Edits are stored in json-overrides on the server and survive updates. Players get the new data after a page reload." }),
            area, note);
    }

    async function stats() {
        const s = await api("admin/stats");
        const pct = (w, p) => `${Math.round((w / Math.max(1, p)) * 100)}%`;
        const name = id => data.items[id]?.name ?? data.augments[id]?.name ?? data.kits[id]?.name ?? id;
        const table = (title, rows, cols) => el("div", { class: "panel" }, el("h3", { text: title }),
            el("table", { class: "table" }, el("thead", {}, el("tr", {}, cols.map(c => el("th", { text: c[0] })))),
                el("tbody", {}, rows.slice(0, 15).map(r => el("tr", {}, cols.map(c => el("td", { text: String(c[1](r)) })))))));
        return el("div", { class: "admin-stats" },
            el("div", { class: "profile-stats" }, [
                ["Online", s.online], ["Lobbies", s.lobbies], ["In match", s.inMatch], ["Users", s.data.users],
                ["Matches", s.data.matches], ["Today", s.data.matchesToday]
            ].map(([k, v]) => el("div", {}, el("b", { text: String(v) }), el("span", { class: "muted small", text: k })))),
            s.pendingReload ? el("p", { class: "form-note", text: "Data edits are waiting for running matches to end." }) : null,
            el("div", { class: "board-grid" },
                table("Kits", s.data.kits, [["Kit", r => name(r.kit)], ["Picks", r => r.picks], ["Wins", r => r.wins], ["Win %", r => pct(r.wins, r.picks)], ["Avg place", r => r.avgPlacement]]),
                table("Items in final builds", s.data.items, [["Item", r => name(r.id)], ["Picks", r => r.picks], ["Win %", r => pct(r.wins, r.picks)]]),
                table("Augments", s.data.augments, [["Augment", r => name(r.id)], ["Picks", r => r.picks], ["Win %", r => pct(r.wins, r.picks)]])));
    }

    return { render };
}
