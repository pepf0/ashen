// Player profiles, styled names (cosmetics) and the cosmetics locker in Settings.
import { portraitUrl } from "./sprites.js";
import { api } from "./net.js";
import { el, data, itemCard } from "./data.js";
import { openDialog, toast } from "./ui.js";

let achievementCache = null;

export async function loadAchievements() {
    if (!achievementCache) {
        const [defs, extra] = await Promise.all([
            fetch("json/achievements.json").then(r => r.json()),
            api("achievements").catch(() => ({ rarity: {} }))
        ]);
        achievementCache = { list: defs, rarity: extra.rarity ?? {} };
    }
    return achievementCache;
}

/**
 * A player name with their name color, level and title. Clicking opens their profile (not for bots).
 * appearance: { nameColor, frame, victory, title, level } from the server, or null.
 */
export function nameEl(name, appearance, userId, { showTitle = true } = {}) {
    const node = nameNode(name, appearance);
    const wrap = el("span", { class: "name-wrap" + (userId > 0 ? " clickable" : ""), onclick: userId > 0 ? e => { e.stopPropagation(); openProfile(userId); } : undefined },
        appearance?.level ? el("span", { class: "level-badge-sm" + (isPrime(appearance) ? " crown" : ""), text: `${appearance.level}` }) : null,
        node,
        showTitle && appearance?.title ? titleEl(appearance.title) : null);
    return wrap;
}

/** Whether the player wears any piece of the admin set "Pepf Prime" (rows, podiums and profiles get extra flair). */
export { isPrime } from "./prime.js";
import { isPrime, holo, primeLetters } from "./prime.js";

/** A title; title cosmetics get their own style class (e.g. the admin set). */
export function titleEl(title) {
    const c = data.cosmetics.find(x => x.kind === "title" && x.name === title);
    return el("span", { class: "player-title" + (c ? ` title-${c.id}` : ""), text: title });
}

/** The name in its color, wrapped in its name border (if any). The border is a separate element so its
 * background can't hide gradient (text-clipped) name colors. */
export function nameNode(name, appearance, cls = "") {
    const lettered = appearance?.nameColor === "name_pepf";
    const text = el("span", { class: "player-name" + (appearance?.nameColor ? ` nc-${appearance.nameColor}` : "") + (lettered ? " lettered" : "") + (cls ? ` ${cls}` : ""),
        "aria-label": lettered ? name : undefined }, lettered ? primeLetters(name) : name);
    const border = appearance?.nameBorder;
    return border && border !== "nb_none" ? el("span", { class: `nb ${border}` }, text) : text;
}

/** Pixel portrait of the player's kit inside their portrait border cosmetic. size: "sm" | "md" | "lg". */
export function portraitEl(kit, appearance, size = "sm") {
    const border = appearance?.border && appearance.border !== "pb_none" ? appearance.border : "pb_default";
    // Pepf Prime has their own character instead of the kit's.
    const who = isPrime(appearance) ? "pepf" : kit ?? "none";
    return el("span", { class: `portrait ${size} ${border}`, dataset: { kit: kit ?? "none" } },
        el("img", { src: portraitUrl(who, 4), alt: "", draggable: "false" }));
}

function bar(into, needed) {
    const pct = needed ? Math.min(100, (into / needed) * 100) : 100;
    return el("div", { class: "xp-bar" }, el("div", { class: "xp-fill", style: `width:${pct}%` }),
        el("span", { text: needed ? `${into} / ${needed} XP` : "Max level" }));
}

export async function openProfile(userId) {
    try {
        const [p, ach] = await Promise.all([api(`profile/${userId}`), loadAchievements()]);
        const unlocked = new Map(p.achievements.map(a => [a.id, a.at]));
        const body = el("div", { class: "profile" },
            el("div", { class: "profile-head" + (isPrime(p.appearance) ? " prime" : "") },
                portraitEl(p.kits?.[0]?.kit ?? "none", p.appearance, "lg"),
                el("div", { class: "profile-level" + (isPrime(p.appearance) ? " crown" : ""), text: `${p.level}` }),
                el("div", { class: "grow" },
                    el("div", { class: "profile-name" }, nameNode(p.name, p.appearance)),
                    el("div", { class: "muted small" }, `@${p.username}`, p.appearance?.title ? [" · ", titleEl(p.appearance.title)] : "", p.mastery ? ` · ${p.mastery}` : ""),
                    bar(p.levelXp, p.nextLevelXp))),
            el("div", { class: "profile-stats" }, [
                ["Games", p.stats.games], ["Wins", p.stats.wins], ["Win rate", `${Math.round(p.stats.winRate * 100)}%`],
                ["Top 3", p.stats.top3], ["Avg place", p.stats.avgPlacement || "–"], ["Fights won", p.stats.fightsWon]
            ].map(([k, v]) => el("div", {}, el("b", { text: String(v) }), el("span", { class: "muted small", text: k })))),
            el("h4", { text: "Kits" }),
            p.kits.length ? el("ul", { class: "kit-stats" }, p.kits.map(k => el("li", {},
                el("span", { class: "grow", text: data.kits[k.kit]?.name ?? k.kit }),
                el("span", { class: "muted small", text: `${k.games} games · ${k.wins} wins (${Math.round((k.wins / Math.max(1, k.games)) * 100)}%)` }))))
                : el("p", { class: "muted small", text: "No matches yet." }),
            el("h4", { text: "Favourite items" }),
            el("div", { class: "mini-build" }, p.favouriteItems.map(i => itemCard(i.id, { size: "small" }))),
            el("h4", { text: `Achievements (${unlocked.size}/${ach.list.length})` }),
            el("div", { class: "achievement-grid" }, ach.list.map(a => el("div", {
                class: "achievement" + (unlocked.has(a.id) ? " unlocked" : ""),
                title: `${a.description}${unlocked.has(a.id) ? ` · unlocked ${new Date(unlocked.get(a.id)).toLocaleDateString()}` : ""}`
            },
                el("b", { text: a.name }),
                el("span", { class: "small", text: a.description }),
                el("span", { class: "rarity", text: `${Math.round((ach.rarity[a.id] ?? 0) * 100)}% of players` })))),
            el("h4", { text: "Recent matches" }),
            el("ul", { class: "match-list compact" }, p.recentMatches.map(m => el("li", {},
                el("span", { class: "grow", text: m.winners ? `${m.winners} won` : "Match" }),
                el("span", { class: "muted small", text: `${m.players} players · ${m.rounds} rounds · ${new Date(m.endedAt).toLocaleDateString()}` })))));
        holo(body.querySelector(".profile-head.prime"), 5);
        await openDialog(p.name, body, [{ label: "Close", value: true }]);
    } catch (err) {
        toast(err.message);
    }
}

// ---------------- locker ----------------

const KINDS = [
    { kind: "name_color", label: "Name color" },
    { kind: "name_border", label: "Name border" },
    { kind: "title", label: "Title" },
    { kind: "border", label: "Portrait border" },
    { kind: "frame", label: "Fighter frame" },
    { kind: "weapon_skin", label: "Generic weapon skin" },
    { kind: "victory", label: "Victory effect" }
];

export async function renderLocker(root, onChange) {
    let locker;
    try { locker = await api("me/locker"); } catch { root.replaceChildren(el("p", { class: "muted", text: "Couldn't load cosmetics." })); return; }
    const ach = await loadAchievements();
    const achName = id => ach.list.find(a => a.id === id)?.name ?? id;

    const equip = async (kind, id) => {
        try {
            locker = await api("me/locker", { method: "PUT", body: { ...locker.equipped, [kind]: id } });
            draw();
            onChange?.();
        } catch (err) { toast(err.message); }
    };

    const draw = () => {
        root.replaceChildren(
            el("div", { class: "locker-level" }, el("b", { text: `Level ${locker.level}` }), bar(locker.levelXp, locker.nextLevelXp)),
            ...KINDS.map(({ kind, label }) => {
                const options = kind === "title"
                    ? [{ id: null, name: "No title", unlocked: true }, ...locker.titles.map(t => ({ id: t, name: t, unlocked: true })),
                        ...locker.cosmetics.filter(c => c.kind === "title" && !c.unlocked).map(c => ({ ...c, id: c.name }))]
                    : locker.cosmetics.filter(c => c.kind === kind);
                return el("div", { class: "locker-group" },
                    el("h4", { text: label }),
                    el("div", { class: "locker-options" }, options.map(o => {
                        const selected = (locker.equipped[kind] ?? null) === o.id || (!locker.equipped[kind] && (o.id === null || o.id.endsWith("_none") || o.id === "name_default"));
                        const hint = o.unlocked ? "" : o.unlock?.level ? `Level ${o.unlock.level}` : `Achievement: ${achName(o.unlock?.achievement)}`;
                        return el("button", {
                            class: `locker-option ${kind}` + (selected ? " selected" : "") + (o.unlocked ? "" : " locked"),
                            disabled: !o.unlocked,
                            title: hint,
                            onclick: () => equip(kind, o.id)
                        },
                            kind === "name_color" ? el("span", { class: `player-name nc-${o.id}`, text: o.name })
                                : kind === "name_border" ? nameNode(o.name, { nameBorder: o.id })
                                : kind === "border" ? el("span", { class: "row" }, portraitEl(locker.kit ?? "duelist", { border: o.id }, "sm"), o.name)
                                : kind === "weapon_skin" ? el("span", { class: "row" }, itemCard("generic.sword", { size: "small", skin: o.id }), o.name)
                                : kind === "frame" ? el("span", { class: `frame-swatch frame-${o.id}` }, o.name)
                                : kind === "title" ? titleEl(o.name)
                                : el("span", { text: o.name }),
                            o.unlocked ? null : el("span", { class: "muted small", text: hint }));
                    })));
            }));
    };
    draw();
}
