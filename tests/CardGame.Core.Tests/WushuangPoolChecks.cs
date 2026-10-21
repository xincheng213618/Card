using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static partial class WushuangModeChecks
{
    public static void OfficialPoolsPricesVersionsAndAcquisitionRecovery()
    {
        var registry = StandardWushuangContentRegistry.Create();
        Require(WushuangCatalogue.Skills.Count == 218 && WushuangTactics.All.Count == 194 &&
            WushuangTactics.All.All(t => t.IsImplemented), "All source records and every tactic have an executable binding.");
        var missing = WushuangCatalogue.Skills.Where(s => s.Id != 3337).Where(s => s.SkillId is not { } id ||
            !registry.Skills.TryGetValue(id, out var d) || d.ImplementationStatus != SkillImplementationStatus.Complete).Select(s => s.Id + ":" + s.SkillId).ToArray();
        Require(WushuangCatalogue.Skills.Single(s => s.Id == 3337) is { SourceSpellId: 3596, SkillId: null } &&
            WushuangCatalogue.Skills.Single(s => s.Id == 3001) is { Cost: 3, Quality: 3, SkillId: "wushuang:skill-3001" } &&
            WushuangCatalogue.Skills.Single(s => s.Id == 3029) is { SourceSpellId: 66, Cost: 2, Quality: 2, SkillId: "classic:wushuang" } &&
            WushuangCatalogue.Skills.Single(s => s.Id == 3260) is { SourceSpellId: 296, Cost: 2, SkillId: "wushuang:skill-3260" },
            "Official single-duel prices and spell identities distinguish older Qimou from its draw-added version.");
        var (g, r) = Start(false, 63, gain: true);
        Drive(g, () => Shop(g) is { CanShop: true, Phase: WushuangShopPhase.OpeningTactic });
        var offer = Shop(g).Offers.Single(o => o.TacticId == 2294);
        var before = g.CardMovements.Count;
        Buy(g, offer.Id);
        Drive(g, () => P(g)?.SkillPrompt?.SkillId == "fixture:wushuang-gain" &&
            g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.WushuangDeckGain is not null));
        var paid = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.WushuangDeckGain is not null);
        Require(paid.WushuangDeckGain!.CardIds.Count == 3 && paid.WushuangDeckGain.CardIds is IList<int> { IsReadOnly: true } &&
            E<WushuangOfferBoughtEvent>(g).Count(e => e.OfferId == offer.Id) == 1,
            "The acquired tactic records three physical trick cards and its purchase before its real gain child.");
        Cold(g, r);
        Play(g);
        Require(E<WushuangDeckCardsObtainedEvent>(g).Count(e => e.ProgramFrameId == paid.Id) == 1 &&
            E<WushuangOfferBoughtEvent>(g).Count(e => e.OfferId == offer.Id) == 1,
            "Resuming the purchased tactic returns to the shop once without another payment or duplicate pile gain.");
        Require(g.CardMovements.Skip(before).Any(m => m.Reason.Value == "wushuang.obtain-deck-cards"),
            "The acquisition uses committed physical card movements.");
        Cold(g, r);
        Require(missing.Length == 0, "Every named official skill maps to a complete version; missing: " + string.Join(",", missing));
    }

    public static void FreeRefreshCapacityAndOriginalReplacement()
    {
        var (free, fr) = Start(false, 148);
        Drive(free, () => Shop(free) is { CanShop: true, Phase: WushuangShopPhase.OpeningTactic });
        Buy(free, Shop(free).Offers.Single(o => o.TacticId == 2291).Id);
        Shopping(free);
        var tokens = Shop(free).Tokens;
        Require(Shop(free) is { FreeRerolls: 2, RerollCost: 0 }, "Refresh I grants exactly two accumulated free refreshes.");
        Accept(free, new RerollWushuangShopCommand(0, free.Revision));
        Accept(free, new RerollWushuangShopCommand(0, free.Revision));
        Require(Shop(free) is { FreeRerolls: 0, RerollCost: 1 } && Shop(free).Tokens == tokens,
            "Both free refreshes are consumed before any token cost.");
        Cold(free, fr);
        var (slots, sr) = Start(false, 57);
        Drive(slots, () => Shop(slots) is { CanShop: true, Phase: WushuangShopPhase.OpeningTactic });
        Buy(slots, Shop(slots).Offers.Single(o => o.TacticId == 2293).Id);
        Play(slots);
        Require(Shop(slots).ExtraSkillCapacity == 3 && Shop(slots).ExtraSkillIds.Count == 0,
            "Tactics increase the real shop capacity without occupying that capacity themselves.");
        Cold(slots, sr);
        var (original, or) = Start(false, 54);
        Drive(original, () => Shop(original) is { CanShop: true, Phase: WushuangShopPhase.OpeningTactic });
        Buy(original, Shop(original).Offers.Single(o => o.TacticId == 2221).Id);
        Shopping(original);
        AffordableSkillPage(original);
        var skill = Shop(original).Offers.First(o => o.SkillId is not null && o.Cost <= Shop(original).Tokens);
        Require(Shop(original).ReplaceableOriginalSkillIds.Contains(Driver), "One Forgetting exposes exact printed source grants for replacement.");
        Buy(original, skill.Id, Driver);
        Require(Shop(original).ExtraSkillIds.Count == 0 && original.CreateSnapshot(0).Players[0].Skills!.All(s => s.ContentId != Driver) &&
            Shop(original).ReplaceableOriginalSkillIds.Contains(skill.SkillId!), "Replacing a printed slot preserves its slot identity and can be replaced again.");
        Cold(original, or);
    }

    public static void OriginalQimouAndHunZiVersions()
    {
        var (g, r) = Start(false, skills: ["wushuang:skill-3260"]);
        Play(g);
        var hand = g.CreateSnapshot(0).Players[0].HandCount;
        var hp = g.State.Players[0].Hp;
        Accept(g, new UseProgramSkillCommand(0, "wushuang:skill-3260", "lose-hp-for-turn-benefits", [], [], g.Revision, P(g)!.PromptId));
        Cold(g, r);
        var prompt = P(g)!;
        Accept(g, new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("quantity") == "2").Id, g.Revision));
        Play(g);
        Require(g.State.Players[0].Hp == hp - 2 && g.State.Players[0].HandCount == hand &&
            E<TurnRuleModifierGrantedEvent>(g).Where(e => e.Modifier.Source.SkillId == "wushuang:skill-3260")
                .Select(e => e.Modifier.Amount).Order().SequenceEqual([-2, 2]),
            "The official 296 Qimou loses HP and grants distance/slash benefits without the newer two-card Draw.");
        Cold(g, r);
        var (awaken, ar) = Start(false, skills: ["wushuang:skill-3226"], initialHp: 1);
        Drive(awaken, () => Shop(awaken) is { CanShop: true, Phase: WushuangShopPhase.OpeningTactic });
        Buy(awaken, Shop(awaken).Offers.Single(o => o.TacticId == 2151).Id);
        Play(awaken);
        Require(awaken.State.Players[0].MaxHp == 29 && awaken.State.Players[0].Hp == 2 &&
            awaken.CreateSnapshot(0).Players[0].Skills!.Any(s => s.ContentId == "boundary:yingzi") &&
            awaken.CreateSnapshot(0).Players[0].Skills!.Any(s => s.ContentId == "classic:yinghun"),
            "HunZi 896 lowers MaxHP, recovers one HP, then grants the exact official Yingzi and Yinghun versions.");
        Cold(awaken, ar);
    }

    public static void TacticQuotaFloorsAndEmptySlotsUseActualRules()
    {
        foreach (var (seed, tactic, query, original, expected) in new[]
        {
            (7, 2298, "EvaluateDrawCount", 4, 3), (7, 2298, "EvaluateDrawCount", 0, 0),
            (40, 2299, "EvaluateSlashUseLimit", 3, 2), (40, 2299, "EvaluateSlashUseLimit", 0, 0)
        })
        {
            var (g, r) = Start(false, seed, aiQuota: original);
            Drive(g, () => Shop(g) is { CanShop: true, Phase: WushuangShopPhase.OpeningTactic });
            Buy(g, Shop(g).Offers.Single(o => o.TacticId == tactic).Id); Play(g);
            var players = (IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(g)!;
            var evaluation = (RuleQueryEvaluation)typeof(GameEngine).GetMethod(query,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(g,
                    query == "EvaluateDrawCount" ? [players[1]] : [players[1], null, null])!;
            Require(evaluation.Value is FiniteRuleQueryValue value && value.Value == expected,
                "The enemy quota reduction applies after ordinary modifiers and never raises a quota already below its floor.");
            Cold(g, r);
        }
        var (empty, er) = Start(true, 22);
        Drive(empty, () => Shop(empty) is { CanShop: true, Phase: WushuangShopPhase.OpeningTactic });
        Buy(empty, Shop(empty).Offers.Single(o => o.TacticId == 2226).Id); Play(empty);
        for (var i = 0; i < 5; i++)
        {
            var slash = empty.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]));
            Accept(empty, new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, empty.Revision, P(empty)!.PromptId));
            Play(empty);
        }
        Require(!empty.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "Two unlocked empty skill slots grant exactly four extra native Slash uses.");
        Cold(empty, er);
    }
}
