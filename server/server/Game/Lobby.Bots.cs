namespace Ashen;

public enum BotDifficulty
{
    Easy,
    Normal,
    Hard
}

/// <summary>
/// Bot players: they sit in lobby seats with negative ids, shop when a shop opens and fight with the
/// fight bot (<see cref="Fight"/>). Bots act through the same methods as humans, using an identity-only connection.
/// </summary>
public partial class Lobby
{
    private static readonly string[] BotNames = { "Nova", "Rook", "Vex", "Ember", "Grim", "Pip", "Sable", "Juno", "Quill", "Moth" };
    private int botCounter;

    public string? AddBot(Connection conn, BotDifficulty difficulty)
    {
        lock (sync)
        {
            if (conn.User.Id != HostId) return "Only the host can add bots.";
            if (InMatch) return "Bots can only be added before the match.";
            if (Members.Count >= MaxPlayers) return "This lobby is full.";
            AddBotUnlocked(difficulty);
            BroadcastLobby();
            return null;
        }
    }

    private void AddBotUnlocked(BotDifficulty difficulty)
    {
        long id = -(++botCounter);
        var used = Members.Select(m => m.Name).ToHashSet();
        var name = BotNames.Where(n => !used.Contains($"Bot {n}")).OrderBy(_ => rng.Next()).FirstOrDefault() ?? $"#{botCounter}";
        var member = new LobbyMember
        {
            UserId = id,
            Name = $"Bot {name}",
            Difficulty = difficulty,
            Kit = GameData.Kits.Keys.ElementAt(rng.Next(GameData.Kits.Count))
        };
        member.BotIdentity = new Connection(null!, new User(id, member.Name, member.Name));
        Members.Add(member);
        AddSystemChat($"{member.Name} ({difficulty}) joined.");
    }

    /// <summary>Solo game: fills the lobby with bots and starts right away.</summary>
    public string? StartSolo(Connection conn, int bots, BotDifficulty difficulty)
    {
        lock (sync)
        {
            bots = Math.Clamp(bots, 1, MaxPlayers - 1);
            Settings = new LobbySettings { Public = false }.Sanitized();
            for (int i = 0; i < bots && Members.Count < MaxPlayers; i++) AddBotUnlocked(difficulty);
            BroadcastLobby();
            return Start(conn);
        }
    }

    private int BotSkill(Player p) => MemberOf(p.Id)?.Difficulty switch
    {
        BotDifficulty.Easy => 0,
        BotDifficulty.Normal => 1,
        _ => 2
    };

    /// <summary>Every bot does its whole shop turn when the shop opens.</summary>
    private void BotsTakeShop()
    {
        foreach (var m in Members.Where(m => m.IsBot).ToList())
        {
            var p = FindPlayer(m.UserId);
            if (p is null || p.Eliminated || m.BotIdentity is null) continue;
            try
            {
                BotShop(p, m.BotIdentity, m.Difficulty!.Value);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"[lobby {Code}] bot {m.Name} failed to shop: {e.Message}");
            }
            p.Ready = true;
        }
    }

    private void BotShop(Player p, Connection me, BotDifficulty diff)
    {
        // Augment
        if (p.AugmentOffer is { Count: > 0 } offer)
            PickAugment(me, diff == BotDifficulty.Easy ? rng.Next(offer.Count) : BestAugment(offer));

        // Mystery events (Normal and Hard only)
        if (diff != BotDifficulty.Easy && p.Mystery is { Used: false } mystery)
        {
            switch (mystery.Kind)
            {
                case "library":
                    UseMystery(me, null);
                    break;
                case "fountain" when p.Money >= MysteryOffer.FountainCost + 30:
                    UseMystery(me, null);
                    break;
                case "chest" when p.Money >= 70:
                    UseMystery(me, null);
                    break;
                case "blacksmith" when p.Money >= 45 && p.Weapon is not null:
                    UseMystery(me, "weapon");
                    break;
            }
        }

        // Bonus offers first (cheap or free)
        for (int i = 0; i < p.Shop.Pity.Count; i++)
        {
            var slot = p.Shop.Pity[i];
            if (slot.Bought || p.Money < (slot.Price ?? 0)) continue;
            if (Score(p, GameData.Items[slot.ItemId], diff) > 0) Buy(p, me, i, pity: true);
        }

        // Hard bots reroll a bit looking for good items
        if (diff == BotDifficulty.Hard)
        {
            int rerolls = 0;
            while (rerolls < 3 && p.Shop.RerollsLeft > 0 && !p.Shop.Locked && p.Money >= 30
                   && !p.Shop.Items.Any(s => Score(p, GameData.Items[s.ItemId], diff) >= 40))
            {
                Reroll(me);
                rerolls++;
            }
        }

        int reserve = diff switch
        {
            BotDifficulty.Easy => 0,
            BotDifficulty.Normal => 10,
            _ => round >= 3 ? 40 : 10 // keep money for interest
        };
        for (int guard = 0; guard < 10; guard++)
        {
            var candidates = p.Shop.Items
                .Select((s, i) => (Slot: s, Index: i, Item: GameData.Items[s.ItemId]))
                .Where(c => !c.Slot.Bought)
                .Select(c => (c.Slot, c.Index, c.Item, Price: c.Slot.Price ?? c.Item.Price, Score: Score(p, c.Item, diff)))
                .Where(c => c.Price <= p.Money && (p.Money - c.Price >= reserve || (p.Weapon is null && c.Item.Type == ItemType.Weapon)))
                .Where(c => c.Score > 0)
                .ToList();
            if (candidates.Count == 0) break;
            var pick = diff == BotDifficulty.Easy
                ? candidates[rng.Next(candidates.Count)]
                : candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Price).First();
            if (Buy(p, me, pick.Index, pity: false) is not null) break;
            if (diff == BotDifficulty.Easy && rng.NextDouble() < 0.4) break;
        }

        // Spare money into HP
        if (diff != BotDifficulty.Easy && round >= 2)
        {
            while (p.Money - HpUpgradeCost(p) >= reserve + 20 && p.HpUpgrades < round) BuyHp(me);
        }
    }

    /// <summary>Buys a shop/bonus slot, replacing the weakest dynamic item when the slots are full.</summary>
    private string? Buy(Player p, Connection me, int index, bool pity)
    {
        var slot = (pity ? p.Shop.Pity : p.Shop.Items)[index];
        var item = GameData.Items[slot.ItemId];
        int? replace = null;
        if (item.IsDynamic && p.FindOwned(item.Id) is null && Array.IndexOf(p.Dynamics, null) < 0)
        {
            var weakest = p.Dynamics.Select((d, i) => (d, i))
                .OrderBy(x => x.d!.Item.IsSpecial ? 1 : 0).ThenBy(x => x.d!.Level).ThenBy(x => x.d!.Item.Price).First();
            if (weakest.d!.Item.IsSpecial && !item.IsSpecial) return "skip";
            replace = weakest.i;
        }
        return pity ? BuyPity(me, index, replace) : Buy(me, index, replace);
    }

    /// <summary>How much a bot wants an item. 0 or less = don't buy.</summary>
    private int Score(Player p, Item item, BotDifficulty diff)
    {
        var owned = p.FindOwned(item.Id);
        if (owned is not null && owned.Level >= MaxLevel) return -100;
        if (item.IsSpecial && p.ObtainedSpecials.Contains(item.Id)) return -100;
        int score = 0;
        var myPacks = p.AllItems().Where(i => i.Item.IsSpecial).Select(i => i.Item.Pack).ToHashSet();
        if (item.IsSpecial) score += 40 + (myPacks.Contains(item.Pack) ? 30 : 0);
        // Items that buff the weapon we hold (e.g. Pick for the Guitar)
        if (diff == BotDifficulty.Hard && p.Weapon is not null && item.Effects.Any(e => e.Target == p.Weapon.Id)) score += 25;

        switch (item.Type)
        {
            case ItemType.Weapon:
                if (p.Weapon is null) score += 60;
                else if (owned is not null) score += 15;
                else if (item.IsSpecial && !p.Weapon.Item.IsSpecial) score += 10;
                else score -= 60;
                break;
            case ItemType.Shirt:
                if (p.Shirt is null) score += 30;
                else if (owned is not null) score += 12;
                else if (item.IsSpecial && !p.Shirt.Item.IsSpecial) score += 5;
                else score -= 40;
                break;
            default:
                if (owned is not null) score += 12;
                else if (Array.IndexOf(p.Dynamics, null) >= 0) score += 18;
                else score += item.IsSpecial ? 0 : -30;
                break;
        }
        return score;
    }

    /// <summary>Normal/Hard: prefer augments with fight mods over pure economy later in the match.</summary>
    private int BestAugment(List<string> offer)
    {
        string[] fight = { "weapon_damage_mult", "lifesteal", "double_strike", "second_wind", "max_hp_mult", "crit_chance_add", "execute", "regen" };
        int best = 0, bestScore = int.MinValue;
        for (int i = 0; i < offer.Count; i++)
        {
            var aug = GameData.Augments.First(a => a.Id == offer[i]);
            int score = aug.Mods.Keys.Count(k => fight.Contains(k)) * (round >= 6 ? 3 : 1) + aug.Mods.Count + rng.Next(2);
            if (score > bestScore) (best, bestScore) = (i, score);
        }
        return best;
    }
}
