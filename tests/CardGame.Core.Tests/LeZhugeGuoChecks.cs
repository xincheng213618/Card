using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class LeZhugeGuoChecks
{
    public static void NativeReorderAndSameUseEffects()
    {
        Run(CardKind.Slash, CardKind.IronChain, [1, 2], restore: true);
        Run(CardKind.ArrowBarrage, CardKind.IronChain, [1, 2], restore: false);
        Run(CardKind.BorrowedSword, CardKind.IronChain, [0, 1], restore: false);
        Run(CardKind.Dodge, CardKind.Slash, [3], restore: false);
        Run(CardKind.Nullification, CardKind.Slash, [3], restore: false);
    }
    private static void Run(CardKind shown, CardKind original, int[] targets, bool restore)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(shown));
        var setup = new ShanheBattleSetup(7, 1, "le-zhuge-guo", Owner, 3, 0, 0, 0, 0, [],
            [new(original, Suit.Club, 7), new(CardKind.Peach, Suit.Heart, 3), new(CardKind.Dodge, Suit.Spade, 4)],
            shown == CardKind.BorrowedSword ? [new(CardKind.Crossbow, Suit.Club, 1)] : [],
            [new(EnemyA, Hp: 8) { StartingHand = [] }, new(EnemyB, Hp: 8) { StartingHand = [] }],
            [new(Ally, Hp: 8) { StartingHand = [] }], new string('a', 64));
        var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
            UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        PendingDecision? P() => Enumerable.Range(0, 4).Select(i => g.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        T[] E<T>() => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
        void Need(bool c, string m) { if (!c) throw new InvalidOperationException(m); }
        void Accept(GameCommand c) { var r = g.Submit(c); Need(r.Accepted, r.Error?.Message ?? "Native command rejected."); }
        void Reach(Func<bool> done)
        {
            for (var i = 0; i < 200; i++)
            {
                if (done()) return;
                if (P() is { PlayerSeat: 0 } p) throw new InvalidOperationException("Unexpected human boundary: " + p.Prompt);
                Need(g.State.Winner == Winner.None, "The native fixture ended before its representative effect.");
                Accept(new AdvanceOneStepCommand(g.Revision));
            }
            throw new InvalidOperationException("Le Zhuge Guo did not reach its bounded native continuation.");
        }
        void Answer(Func<PromptChoice, bool> match)
        { var p = P()!; Need(p.PlayerSeat == 0, "Only actual human choices are answered."); Accept(new AnswerPromptCommand(0, p.PromptId, p.Choices.Single(match).Id, g.Revision)); }
        void Reorder()
        {
            while (P()?.Kind == DecisionKind.ProgramTopReorder)
            {
                var p = P()!;
                Answer(c => p.Choices.Any(x => x.Parameters.GetValueOrDefault("action") == "finish-top")
                    ? c.Parameters.GetValueOrDefault("action") == "finish-top" : c.Id == p.Choices.First().Id);
            }
        }
        Accept(new StartGameCommand()); Reach(() => P()?.Kind == DecisionKind.ProgramTopReorder);
        var opening = E<LeXidiInitializedEvent>().Single();
        var openingCount = g.CreateSnapshot(0).Players[0].HandCount;
        Need(opening.Count == openingCount && E<LeXidiEntityMarkedEvent>().Count(e => e.Initial) == openingCount &&
            E<LeXidiReorderStartedEvent>().Single().RequestedCount == Math.Max(1, openingCount) && g.CreateSnapshot(0).Players[0].Hand.All(c => c.SkillMarkLabel?.Contains("笛") == true),
            "Xidi marks the real opening entities and preparation watches their current Hand count.");
        Reorder(); Reach(() => P()?.Kind == DecisionKind.PlayCard);
        var handBefore = g.CreateSnapshot(0).Players[0].HandCount;
        var action = g.GetHumanLegalActions().First(a => a.Kind == (original == CardKind.IronChain ? LegalActionKind.IronChain : LegalActionKind.Slash) && a.TargetSeats.SequenceEqual(targets));
        var cardId = action.CardId!.Value;
        Accept(new PlayCardCommand(0, cardId, action.TargetSeats, g.Revision, P()!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
        Reach(() => P()?.SkillPrompt?.SkillId == "le:chengyan");
        Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(() => P()?.SkillPrompt?.SkillId == Watch);
        if (restore)
        {
            var before = JsonSerializer.Serialize(g.CreateSnapshot(0));
            g = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
            Need(before == JsonSerializer.Serialize(g.CreateSnapshot(0)), "One representative real draw child restores the original paid Use and pending reveal.");
        }
        Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        if (shown == CardKind.BorrowedSword)
        {
            Reach(() => P()?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "le-chengyan-borrowed-victim") == true);
            Answer(c => c.Targets.SequenceEqual([1]));
        }
        Reach(() => P()?.Kind == DecisionKind.PlayCard);
        var revealed = E<LeChengyanRevealedEvent>().Single();
        Need(E<CardUseDeclaredEvent>().Count(e => e.CardId == cardId) == 1 && E<CardUseFinishedEvent>().Count(e => e.CardId == cardId && e.CardKind == original) == 1 &&
            g.CreateCardZoneDiagnostics().Single(c => c.CardId == cardId).Location == CardLocation.DiscardPile,
            "Chengyan retains exactly one native declaration, payment, original card kind and completion.");
        Need(revealed.EffectiveKind == shown, "The actual drawn entity supplies the frozen shown kind.");
        if (shown is CardKind.Slash or CardKind.ArrowBarrage)
        {
            Need(targets.All(t => g.CreateSnapshot(0).Players[t].Hp == 7), "All original targets receive the native response/damage effect on the same Use.");
            Need(g.CreateSnapshot(0).Players[0].HandCount == handBefore && E<LeChengyanEffectIssuedEvent>().Single().UseId == revealed.UseId &&
                targets.All(t => !g.CreateSnapshot(0).Players[t].IsChained), "A displayed Slash replaces both Iron Chain effects and remains a hand entity.");
        }
        else if (shown == CardKind.BorrowedSword)
            Need(E<BorrowedSwordResolvedEvent>().Single() is { WeaponOwnerSeat: 0, SlashTargetSeat: 1, UsedSlash: false, TransferredWeaponKind: CardKind.Crossbow } &&
                g.CreateSnapshot(0).Players[0].HandCount == handBefore + 1 && E<LeChengyanEffectStartedEvent>().Single().TargetCount == 2 &&
                targets.All(t => !g.CreateSnapshot(0).Players[t].IsChained), "The user supplies a legal auxiliary victim on the same Use; the real weapon is transferred once and the other weaponless original target has no effect.");
        else if (shown == CardKind.Nullification)
            Need(targets.All(t => g.CreateSnapshot(0).Players[t].Hp == 7) && g.CreateSnapshot(0).Players[0].HandCount == handBefore &&
                E<LeChengyanDrawReturnedEvent>() is [{ Second: false, ActualCount: 1 }] && E<LeChengyanEffectIssuedEvent>().Length == 0 &&
                E<LeXidiEntityMarkedEvent>().All(e => e.Initial) && E<LeChengyanCompletedEvent>().Single().Replaced == false &&
                g.CreateCardZoneDiagnostics().Single(c => c.CardId == revealed.CardId).Location == CardLocation.Hand(0),
                "A genuinely drawn and shown Nullification remains in Hand; the original Slash still damages once, without a replacement plan, second draw or extra flute mark.");
        else
            Need(targets.All(t => g.CreateSnapshot(0).Players[t].Hp == 7) && g.CreateSnapshot(0).Players[0].HandCount == handBefore + 1 && E<LeChengyanDrawReturnedEvent>().Length == 2 &&
                E<LeXidiEntityMarkedEvent>().Count(e => !e.Initial) == 1 && E<LeXidiEntityMarkedEvent>().All(e => e.Initial || e.CardId != revealed.CardId),
                "A displayed nonmatching Dodge preserves the Slash and only the second actual draw gains the physical flute mark.");
        Accept(new EndPlayPhaseCommand(0, g.Revision, P()!.PromptId)); Reach(() => P()?.Kind == DecisionKind.ProgramTopReorder);
        Need(E<LeXidiReorderStartedEvent>().Last().FluteCount == openingCount - 1 + (shown == CardKind.Dodge ? 1 : 0), "Ending recalculates current marked Hand entities.");
        Reorder();
    }
    private const string Owner = "fixture:lzg-owner", Ally = "fixture:lzg-ally", EnemyA = "fixture:lzg-enemy-a", EnemyB = "fixture:lzg-enemy-b",
        Watch = "fixture:lzg-watch", Quiet = "fixture:lzg-quiet", Deck = "fixture:lzg-deck";
    private sealed class Fixture(CardKind shown) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-le-zhuge-guo", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, ["le:zhuge-guo"]);
            foreach (var k in new[] { CardKind.Slash, CardKind.Dodge, CardKind.Peach, CardKind.IronChain, CardKind.ArrowBarrage, CardKind.BorrowedSword, CardKind.Crossbow, CardKind.Nullification })
            { var d = CardCatalog.Get(k); b.AddCard(new("fixture:lzg-card-" + k, d.DisplayName, d.CategoryName, d.Description, k)); }
            var rules = "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[" +
                "{\"id\":\"" + Watch + "\",\"revision\":1,\"triggers\":[{\"id\":\"gain-child\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program.le:chengyan.draw\"],\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"pause\",\"options\":[{\"id\":\"continue\"}]}]}]}," +
                "{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}]}";
            var ids = new[] { Watch, Quiet };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = ids.ToDictionary(id => id, id => new { name = "真实停点", description = "原生摸牌子结算或静候", optionLabels = id == Watch ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, "真实停点", "原生结算") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Owner, "乐诸葛果原生检查", "", "standard:none", "shu", 3, ["le:xidi", "le:chengyan", Watch]));
            foreach (var id in new[] { Ally, EnemyA, EnemyB }) b.AddGeneral(new(id, "静候角色", "", "standard:none", "shu", 8, [Quiet]));
            // Normal draws are deliberately zero; the only draws under test are genuine Chengyan requests.
            b.AddDeck(new(Deck, "效果小牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 16).Select(_ => new ContentDeckPhysicalCard("fixture:lzg-card-" + shown, Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "乐诸葛果小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
