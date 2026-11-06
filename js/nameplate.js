// Nameplates: a banner with portrait, level crest, name, title and lives. Styled by the player's signature set
// (Pepf Prime and the friend sets), otherwise by their level tier.
import { el } from "./data.js";
import { nameNode, portraitEl } from "./profile.js";
import { isPrime, sigKey } from "./prime.js";

export const TIERS = [[50, "mythic", "Mythic"], [40, "diamond", "Diamond"], [30, "platinum", "Platinum"], [20, "gold", "Gold"], [10, "silver", "Silver"], [1, "bronze", "Bronze"]];

/** CSS class for a player's plate: their set if they wear one, else their level tier. */
export function plateClass(appearance) {
    if (isPrime(appearance)) return "np-set-pepf";
    const key = sigKey(appearance);
    if (key) return `np-set-${key}`;
    const lvl = appearance?.level ?? 1;
    return `np-tier-${TIERS.find(([min]) => lvl >= min)[1]}`;
}

export function tierName(level) { return TIERS.find(([min]) => (level ?? 1) >= min)[2]; }

/** Hearts: filled for lives left, hollow for lives lost. */
export function hearts(lives, max) {
    if (lives == null) return null;
    const total = Math.max(max ?? lives, lives);
    return el("span", { class: "np-hearts", title: `${lives} ${lives === 1 ? "life" : "lives"} left` },
        Array.from({ length: total }, (_, i) => el("i", { class: i < lives ? "full" : "lost", style: `--h:${i}` })));
}

/**
 * A nameplate. opts: { kit, lives, maxLives, you, out, compact, sub } (sub: extra small text under the name).
 */
export function nameplate(name, appearance, { kit, lives, maxLives, you = false, out = false, compact = false, sub } = {}) {
    const level = appearance?.level;
    return el("div", { class: `nameplate ${plateClass(appearance)}` + (compact ? " compact" : "") + (you ? " you" : "") + (out ? " out" : "") },
        el("span", { class: "np-shine", "aria-hidden": "true" }),
        portraitEl(kit, appearance, compact ? "sm" : "md"),
        el("div", { class: "np-body" },
            el("div", { class: "np-line" },
                level ? el("span", { class: "np-crest", title: `Level ${level} · ${tierName(level)}` }, el("b", { text: `${level}` })) : null,
                nameNode(name, appearance)),
            appearance?.title ? el("div", { class: "np-title", text: appearance.title }) : null,
            sub ? el("div", { class: "np-sub", text: sub }) : null),
        hearts(lives, maxLives));
}
