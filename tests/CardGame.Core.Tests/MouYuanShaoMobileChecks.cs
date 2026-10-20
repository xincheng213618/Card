using System.Text.Json;
using CardGame.Content.Standard;

namespace CardGame.Core.Tests;

internal static class MouYuanShaoMobileChecks
{
    private const string Luanji = "mou-mobile:luanji", Xueyi = "mou-mobile:xueyi", Pair = "any-hand-pair-as-arrow-barrage";
    private const string Owner = "fixture:mys-owner", Mode = "identity:classic-mys", Deck = "fixture:mys-deck", Quiet = "fixture:mys-quiet";
    public static void AnyPairNativeArrowResponsesIndependentTurnCapsAndLordTargets()
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 5, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g)?.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g);
        var initialView = g.CreateSnapshot(0); var start = initialView.Players[0];
        // FactionId is an optional effective-faction override in the public view.
        // Ordinary visible identity generals retain faction on their content definition.
        var otherQun = initialView.Players.Count(p => p.Seat != 0 && p.IsAlive && (p.FactionId ?? r.Generals[p.GeneralId].FactionId) == "qun");
        Need(start.Role == Role.Lord && otherQun == 3 && start.HandLimit == start.Hp + 2 * otherQun,
            $"The complete native Lord population policy is active alongside the new designation reward: role={start.Role}, hp={start.Hp}, max={start.MaxHp}, handLimit={start.HandLimit}, otherQun={otherQun}.");
        for (var round = 0; round < 2; round++)
        {
            var beforeMoves = g.CardMovements.Count; var beforeHand = g.CreateSnapshot(0).Players[0].HandCount;
            var beforeReserves = g.Events.Select(e => e.Payload).OfType<MouYuanShaoRewardReservedEvent>().ToArray();
            var hand = g.CreateSnapshot(0).Players[0].Hand;
            var pair = hand.SelectMany((a, i) => hand.Skip(i + 1).Select(b => new[] { a, b }))
                .First(cards => cards[0].Suit != cards[1].Suit).Select(c => c.Id).ToArray();
            Accept(g, new UseProgramSkillCommand(0, Luanji, Pair, pair, [], g.Revision, P(g)!.PromptId)); Play(g);
            var declarations = g.Events.Select(e => e.Payload).OfType<CardUseDeclaredEvent>().Where(e => e.CardKind == CardKind.ArrowBarrage && e.SourceSeat == 0).ToArray();
            var use = declarations.Last(); var action = g.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>().Select(e => e.Action).Last(a => a.Type == CardActionType.Use && a.ActorSeat == 0 && a.EffectiveKind == CardKind.ArrowBarrage);
            Need(action.PhysicalCards.Select(c => c.CardId).Order().SequenceEqual(pair.Order()) && action.ConversionChain.Any(c => c.SkillId == Luanji) &&
                g.Events.Select(e => e.Payload).OfType<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.ResolutionId) == 1,
                "Two different-suit original Hand entities pay for one real native Arrow Barrage and its single complete use.");
            foreach (var id in pair)
                Need(g.CardMovements.Skip(beforeMoves).Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                    g.CardMovements.Skip(beforeMoves).Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
                    "Each selected material enters processing and finishes once through the original global-card payment.");
            var responses = g.Events.Select(e => e.Payload).OfType<GroupResponseEvent>().Where(e => e.ResolutionId == use.ResolutionId && e.UsedResponse).ToArray();
            var reservations = g.Events.Select(e => e.Payload).OfType<MouYuanShaoRewardReservedEvent>().Except(beforeReserves).ToArray();
            Need(responses.Length == 4 && responses.All(e => e.ResponseCardKind == CardKind.Dodge) &&
                reservations.Count(e => e.Source.SkillId == Luanji) == 3 && reservations.Count(e => e.Source.SkillId == Xueyi) == 2,
                "Four genuinely paid native Dodge responses stop at three Luanji rewards; initial other-Qun designations independently stop at two Xueyi rewards.");
            foreach (var (skill, count) in new[] { (Luanji, 3), (Xueyi, 2) })
                Need(g.CardMovements.Skip(beforeMoves).Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) &&
                    m.Reason.Value == $"skill-program.{skill}.{SkillProgramEffectOp.Draw}") == count,
                    "Reward limits count actual native drawn entities, with separate named-skill budgets.");
            Need(g.CreateSnapshot(0).Players[0].HandCount == beforeHand - 2 + 5 &&
                !g.GetHumanLegalActions().Any(a => a.SkillId == Luanji && a.ProgramActivationId == Pair),
                "The conversion is once in this actual Play while the two independent rewards keep their complete benefits.");
            var cp = GameCheckpointJson.Serialize(g.CreateCheckpoint()); var view = SnapshotJson.Serialize(g.CreateSnapshot(0));
            g = GameReplay.Restore(GameCheckpointJson.Deserialize(cp), r);
            Need(SnapshotJson.Serialize(g.CreateSnapshot(0)) == view, "A representative restored completed native reward sequence preserves the player's actual view.");
            if (round == 0) { Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Play(g); }
        }
        var nativeTurns = g.Events.Select(e => e.Payload).OfType<MouYuanShaoRewardReservedEvent>().Where(e => e.Source.SkillId == Luanji)
            .Select(e => e.Turn).Distinct().ToArray();
        Need(nativeTurns.Length == 2, "A later real turn refreshes both caps without conflating it with the prior accepted uses.");
    }
    private static PendingDecision? P(GameEngine g) => g.CreateSnapshot(0).PendingDecision;
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<bool> ready)
    {
        for (var i = 0; i < 240; i++)
        {
            if (ready()) return;
            if (g.State.Winner != Winner.None) throw new InvalidOperationException("Unexpected native terminal cutoff.");
            var p = P(g);
            if (p is null) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p.Kind == DecisionKind.PlayCard) Accept(g, new EndPlayPhaseCommand(0, g.Revision, p.PromptId));
            else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
            {
                var choice = p.Choices.First(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                Accept(g, new AnswerPromptCommand(0, p.PromptId, choice.Id, g.Revision));
            }
            else throw new InvalidOperationException("Unexpected native boundary: " + p.Prompt);
        }
        throw new InvalidOperationException("Small native Yuan Shao fixture did not return in its bounded cursor budget.");
    }
    private static void Accept(GameEngine g, GameCommand c)
    { var result = g.Submit(c); if (!result.Accepted) throw new InvalidOperationException(result.Error?.Message ?? "Rejected real native command."); }
    private static void Need(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-yuan-shao-mobile", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "mou:yuan-shao-mobile");
            var rules = "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}]}";
            var presentation = "{\"schemaVersion\":" + SkillProgramCatalog.PresentationSchemaVersion + ",\"skills\":{\"" + Quiet + "\":{\"name\":\"quiet\",\"description\":\"Small native peer turns\"}}}";
            var catalog = SkillProgramCatalog.Load(rules, presentation);
            b.AddSkill(new(Quiet, "quiet", "Small native peer turns") { Program = catalog.Programs[Quiet], ProgramPresentation = catalog.Presentations[Quiet] });
            b.AddGeneral(new(Owner, "原生袁绍", "", Luanji, "qun", 8, [Xueyi]));
            for (var i = 1; i < 5; i++) b.AddGeneral(new("fixture:mys-peer-" + i, "其他角色", "", Quiet, i == 4 ? "wei" : "qun", 8));
            b.AddDeck(new(Deck, "Fixed small real stock", 8, 0, []) { PhysicalCards = Enumerable.Range(0, 96)
                .Select(i => new ContentDeckPhysicalCard("standard:dodge", i % 2 == 0 ? Suit.Spade : Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "Native small Yuan Shao", 5, 5, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 3 },
                Deck, GeneralCandidateCount: 5, GeneralPoolIds: new[] { Owner }.Concat(Enumerable.Range(1, 4).Select(i => "fixture:mys-peer-" + i)).ToArray()));
        }
    }
}
