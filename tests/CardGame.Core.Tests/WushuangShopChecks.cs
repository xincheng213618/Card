using CardGame.Content.Standard;
using CardGame.Core;

internal static partial class WushuangModeChecks
{
    public static void MixedShopNoMountsAndEphemeralEquipment()
    {
        var standard = StandardWushuangContentRegistry.Create();
        var duel = standard.Decks[standard.Modes[WushuangMode.ModeId].DeckId!].PhysicalCards!;
        var military = standard.Decks["classic:standard-deck"].PhysicalCards!;
        var expected = military.Where(card => standard.Cards[card.CardDefinitionId].LegacyKind is { } kind &&
            WushuangCatalogue.IsCardAllowed(kind)).ToArray();
        Require(duel.Count < military.Count && duel.SequenceEqual(expected),
            "The duel recipe removes mounts and BorrowedSword while preserving the order and printed identity of every eligible basic equipment and card.");
        Require(WushuangCatalogue.Cards.Count == 121 && WushuangCatalogue.Cards.Count(c => c.IsEquipment) == 41 &&
            WushuangTactics.ById[2045].Description.Contains("+2") && !WushuangTactics.ById.ContainsKey(2046),
            "Source equipment and hand-card offers are preserved; same-name tactics retain their single-duel numeric identity.");
        var allowedCards = WushuangCatalogue.Cards.Where(c => c.IsAvailableInShop).ToArray();
        Require(allowedCards.Length == 89 && allowedCards.Count(c => c.IsEquipment) == 13 &&
            allowedCards.All(c => c.Kind is { } kind && WushuangCatalogue.IsCardAllowed(kind)) &&
            !WushuangCatalogue.IsCardAllowed(CardKind.BorrowedSword) &&
            !WushuangCatalogue.IsCardAllowed(CardKind.ShuraInfernoHalberd) &&
            !WushuangCatalogue.IsCardAllowed(CardKind.DelusionCrown) &&
            WushuangCatalogue.IsCardAllowed(CardKind.BaguaFormation),
            "The actual shop catalogue contains only basic weapons/armor and no mounts, treasures, special equipment or BorrowedSword.");
        Require(new[] { 3099, 3103, 3153, 3157, 3275, 3276 }.All(id => !WushuangCatalogue.Skills.Single(s => s.Id == id).IsAvailableInShop) &&
            new[] { 3027, 3048, 3100, 3282, 3163 }.All(id => WushuangCatalogue.Skills.Single(s => s.Id == id).IsAvailableInShop) &&
            WushuangTactics.All.Where(t => t.Description.Contains("护甲") || t.Id is 2010 or 2129 or 2238 or 2244).All(t => !t.IsAvailableInShop),
            "Third-player-only skills, armor, distance-only and BorrowedSword tactics are unavailable; useful close-range skills, Guanxu and Gongxin remain.");
        var pool = standard.Modes[WushuangMode.ModeId].GeneralPoolIds!;
        Require(pool.Contains("classic:sun-quan") && pool.Contains("classic:lu-meng") && pool.Contains("boundary:xu-sheng") &&
            pool.Contains("boundary:zhang-jiao") &&
            !pool.Contains("classic:liu-bei") && !pool.Contains("classic:hua-tuo") &&
            !pool.Contains("mou:huang-gai-mobile") && standard.Generals.ContainsKey("classic:liu-bei"),
            "The single-duel pool excludes specific support/armor editions while retaining standard duel generals and the global collection.");
        var (noArmor, noArmorRegistry) = Start(false, skills: ["wushuang:tactic-2069", "wushuang:tactic-2074"]);
        Play(noArmor);
        NextHumanTurn(noArmor);
        Require(noArmor.CreateSnapshot(0).Players.All(p => p.Armor == 0) &&
            !E<ArmorGrantedEvent>(noArmor).Any(),
            "Armor remains zero even when an armor source arrives as a printed or acquired skill, without publishing fake armor receipts.");
        Cold(noArmor, noArmorRegistry);

        var (noDistance, noDistanceRegistry) = Start(false, skills: ["classic:yicong"], initialHp: 2);
        Play(noDistance);
        Require(noDistance.GetCombatDistance(1, 0) == 1 && noDistance.GetCombatDistance(0, 1) == 1,
            "Single-duel distance stays one even when a wounded general's Yicong would increase incoming distance.");
        Cold(noDistance, noDistanceRegistry);

        var (game, registry) = Start(false, 119, skills: ["boundary:zhiheng"]);
        Drive(game, () => Shop(game) is { CanShop: true, Phase: WushuangShopPhase.OpeningTactic });
        Buy(game, Shop(game).Offers[0].Id);
        Shopping(game);
        var page = Shop(game).Offers;
        Require(page.Count == 4 && page.Count(o => o.Kind == WushuangOfferKind.Card) == 2,
            "The verified native page contains two independently priced physical-card offers.");
        foreach (var offer in page.Where(o => o.CardOfferId is not null))
        {
            var source = WushuangCatalogue.Cards.Single(c => c.Id == offer.CardOfferId);
            Require(offer.CardKind == source.Kind && offer.Cost == source.Cost && offer.Quality == source.Quality,
                "Every sampled physical-card offer uses the single-duel price, quality and exact version binding.");
        }
        var equipment = page.Single(o => o.CardOfferId == 4186);
        var sourceCard = WushuangCatalogue.Cards.Single(c => c.Id == equipment.CardOfferId);
        Buy(game, equipment.Id);
        var created = E<WushuangShopEquipmentCreatedEvent>(game).Single(e => e.OfferId == equipment.Id);
        var card = game.CreateSnapshot(0).Players[0].Equipment.Single(c => c.Id == created.CardId);
        Require(card.Kind == sourceCard.Kind && card.Suit == sourceCard.Suit && card.Rank == sourceCard.Rank &&
            E<WushuangOfferBoughtEvent>(game).Count(e => e.OfferId == equipment.Id) == 1,
            "Buying equipment equips one physical entity with the original suit/rank and a single paid receipt.");
        Cold(game, registry);

        var (converted, convertedRegistry) = Start(false, 7, skills: ["boundary:wusheng"]);
        Drive(converted, () => Shop(converted) is { CanShop: true, Phase: WushuangShopPhase.OpeningTactic });
        Buy(converted, Shop(converted).Offers[0].Id);
        Shopping(converted);
        var redEquipment = Shop(converted).Offers.Single(o => o.CardOfferId == 4187);
        Buy(converted, redEquipment.Id);
        var redCreated = E<WushuangShopEquipmentCreatedEvent>(converted).Single(e => e.OfferId == redEquipment.Id);
        Accept(converted, new FinishWushuangShopCommand(0, converted.Revision));
        Play(converted);
        var action = converted.GetHumanLegalActions().First(a => a.CardId == redCreated.CardId &&
            a.ConversionSource?.SkillId == "boundary:wusheng");
        Accept(converted, new PlayCardCommand(0, redCreated.CardId, action.TargetSeats, converted.Revision,
            P(converted)!.PromptId, action.PlayedCardKind) { ConversionSource = action.ConversionSource });
        Cold(converted, convertedRegistry);
        Play(converted);
        Require(!converted.IsFaulted && converted.CardMovements.Count(m => m.CardId == redCreated.CardId &&
            m.From == CardLocation.Equipment(0) && m.To == CardLocation.OutsideGame) == 1 &&
            E<CardActionAcceptedEvent>(converted).Any(e => e.Action.EffectiveKind == CardKind.Slash &&
                e.Action.PhysicalCards.Any(c => c.CardId == redCreated.CardId && c.From == CardLocation.Equipment(0))),
            "Wusheng consumes a purchased red weapon once and keeps its real native Slash action valid after destruction and cold replay.");
        Cold(converted, convertedRegistry);
        var handOffer = Shop(game).Offers.First(o => o.CardOfferId is not null && o.CardKind is { } kind &&
            !EquipmentCatalog.IsEquipment(kind) && o.Cost == 0);
        var handSource = WushuangCatalogue.Cards.Single(c => c.Id == handOffer.CardOfferId);
        Buy(game, handOffer.Id);
        Require(game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Kind == handSource.Kind &&
            c.Suit == handSource.Suit && c.Rank == handSource.Rank), "Hand-card purchases preserve exact printed identity.");
        Accept(game, new FinishWushuangShopCommand(0, game.Revision));
        Play(game);
        Accept(game, new UseProgramSkillCommand(0, "boundary:zhiheng", "exchange-owned-cards", [created.CardId], [], game.Revision, P(game)!.PromptId));
        Cold(game, registry);
        Play(game);
        Require(game.CardMovements.Count(m => m.CardId == created.CardId && m.From.Zone == CardZoneKind.Equipment && m.To == CardLocation.OutsideGame) == 1 &&
            !game.CardMovements.Any(m => m.CardId == created.CardId && m.To == CardLocation.DiscardPile) &&
            E<WushuangShopEquipmentDestroyedEvent>(game).Count(e => e.CardId == created.CardId) == 1,
            "A native equipment cost destroys the shop entity once, pays the skill normally and never puts the equipment into the reshuffle pool.");
        Cold(game, registry);
    }
}
