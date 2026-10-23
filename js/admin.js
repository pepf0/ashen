// Admin panel (only shown to admins): live lobbies, users and bans, data editor with hot reload, stats.
import { api, getToken } from "./net.js";
import { el, data } from "./data.js";
import { toast, openDialog } from "./ui.js";

export function createAdmin() {
    const root = document.getElementById("adminRoot");
    let tab = "lobbies";

    const tabs = ["lobbies", "users", "data", "stats"];
    function header() {
        return el("div", { class: "admin-tabs" }, tabs.map(t => el("button", {
            class: "btn btn-small" + (t === tab ? " btn-primary" : ""), text: t[0].toUpperCase() + t.slice(1),
            onclick: () => { tab = t; render(); }
        })), el("button", { class: "btn btn-small btn-ghost", text: "Refresh", onclick: () => render() }));
    }

    async function render() {
        root.replaceChildren(header(), el("p", { class: "muted", text: "Loading…" }));
        try {
            const body = await ({ lobbies, users, data: dataEditor, stats }[tab])();
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
