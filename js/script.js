let tabMenu = document.getElementById("tabMenu");
let tabCompendium = document.getElementById("tabCompendium");
let tabSettings = document.getElementById("tabSettings");
let tabs = [tabMenu, tabCompendium, tabSettings];

let join = document.getElementById("join");
let compendium = document.getElementById("compendium");
let itemInfo = document.getElementById("itemInfo");

let backButton = document.getElementById("backButton")
let backContainer = document.getElementById("backContainer")

tabs.forEach((tab) => tab.addEventListener("click", () => {

    tabs.forEach((t) => t.classList.remove("active"));
    tab.classList.add("active");
     if (tab === tabMenu) {
        join.style.display = "block";
        compendium.style.display = "none";
    } else if (tab === tabCompendium) {
        compendium.style.display = "block";
        join.style.display = "none";
    } else if (tab === tabSettings) {
        join.style.display = "none";
        compendium.style.display = "none"
    }
}));
backButton.addEventListener("click", () => {
    compendium.style.display="block";
    backContainer.style.display="none";
    tabs.forEach(tab => { tab.style.display = "block"; tabCompendium.classList.remove("active"); })
    tabCompendium.classList.add("active");
    itemInfo.style.display = "none";
});

tabMenu.classList.add("active");

let itemsEffects = {};

Promise.all([
    fetch("json/items.json").then(r => r.json()),
    fetch("json/effects.json").then(r => r.json())
]).then(([itemsJson, effectsJson]) => {
    itemsEffects = loadItemsAndEffects(itemsJson, effectsJson);
    renderCompendium();
});

function loadItemsAndEffects(itemsJson, effectsJson) {
    let effects = Object.fromEntries(effectsJson.map(e => [e.id, e]))
    let items = Object.fromEntries(itemsJson.map(i => [i.id, i]))

    return { items, effects };
}

function statPretty(key, value) {
    if (key === "damage") {
        return ["Damage", value];
    } else if (key === "cooldown") {
        return ["Cooldown", `${value}s`];
    } else if (key === "crit_chance") {
        return ["Crit Chance", `${value * 100}%`];
    } else if (key === "crit_damage") {
        return ["Crit Damage", `${value}x`];
    } else if (key === "damage_reduction") {
        return ["Damage Reduction", `${value * 100}%`];
    } else if (key === "dodge_chance") {
        return ["Dodge Chance", `${value * 100}%`];
    }
    return [key, value];
}

function effectPretty(effect) {
    let keys = Object.keys(effect);
    let text = effect.description.replace(/\{([^}]+)\}/g, (_, expr) => {
        let value = new Function(...keys, `return ${expr};`)(...keys.map(k => effect[k]));
        return Math.round(value * 100) / 100;
    });
    text = text.replace(/\b1 seconds\b/g, "1 second");
    if (effect.mult !== undefined) {
        text = text.replace(/\bbuff\b/g, "multiplier");
        if (effect.stat !== "cooldown") {
            text = text.replace(/\bby ([\d.]+)%/g, "by a $1% multiplier");
        }
    } else if (effect.add !== undefined) {
        text = text.replace(/\bby ([\d.]+)%/g, "by a flat $1%");
    }
    return text;
}

function renderCompendium() {
    let types = ["Weapon", "Buff", "Shirt", "Charm", "Passive"];
    for (let type of types) {
        let categoryTitle = document.createElement("h2");
        categoryTitle.textContent = type;
        let grid = document.createElement("div");
        grid.classList.add("item-grid");
        grid.id = `${type}-grid`;
        compendium.appendChild(categoryTitle);
        for (let item of Object.values(itemsEffects.items)) {
            if (item.type !== type.toLowerCase()) {
                continue;
            }
            let itemCard = document.createElement("div");
            itemCard.classList.add("item-card");
            itemCard.dataset.pack = item.pack
            let itemImg = document.createElement("img");
            itemImg.src = `./img/item/${item.sprite}`;
            itemImg.alt = item.name;
            itemCard.append(itemImg);
            grid.append(itemCard);

            itemCard.addEventListener("click", () => {
                tabs.forEach(tab => tab.style.display = "none");
                compendium.style.display = "none";
                backContainer.style.display = "flex";
                itemInfo.style.display = "flex";
                document.getElementById("itemInfoName").textContent = item.name;
                document.getElementById("itemInfoImg").src = itemImg.src;
                document.getElementById("itemInfoStats").replaceChildren();
                for (let [key,value] of Object.entries(item.base_stats ?? {})) {
                    let [key_pretty, value_pretty] = statPretty(key, value);
                    let li = document.createElement("li");
                    li.textContent = `${key_pretty}: ${value_pretty}`;
                    document.getElementById("itemInfoStats").appendChild(li);
                }
                let levelStats = Object.entries(item.level_stats ?? {});
                document.getElementById("itemInfoLevelStatsTitle").style.display = levelStats.length ? "block" : "none";
                document.getElementById("itemInfoLevelStats").replaceChildren();
                for (let [key, value] of levelStats) {
                    let [key_pretty, value_pretty] = statPretty(key.replace(/_add$/, ""), Math.abs(value));
                    let sign = value < 0 ? "-" : "+";
                    let li = document.createElement("li");
                    li.textContent = `${key_pretty}: ${sign}${value_pretty}`;
                    document.getElementById("itemInfoLevelStats").appendChild(li);
                }
                document.getElementById("itemInfoEffects").replaceChildren();
                if (item.effects !== undefined) {
                    for (let effectId of item.effects) {
                        let li = document.createElement("li");
                        li.textContent = effectPretty(itemsEffects.effects[effectId]);
                        document.getElementById("itemInfoEffects").appendChild(li);
                    }
                }
                document.getElementById("itemInfoDescription").textContent = `\"${item.description}\"`;
            });
        }
        compendium.appendChild(grid);
    }
}