using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class AnniversaryZhangWenChecks
{
    private const string Hero = "anniversary:zhang-wen", Songshu = "anniversary:songshu", Sibian = "anniversary:sibian",
        Peer = "fixture:zhang-wen-peer", Prime = "fixture:zhang-wen-prime", NoDraw = "fixture:zhang-wen-no-draw",
        Hold = "fixture:zhang-wen-gain-hold", Deck = "fixture:zhang-wen-deck";

    public static void SongshuRefundAndSibianWholeExtremaGiftResumeCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(83, 1, "zhang-wen-current-rules", Hero, 3, 0, 0, 0, 0, [Prime, Hold],
            [new(CardKind.Dodge, Suit.Heart, 1), new(CardKind.Slash, Suit.Spade, 1), new(CardKind.Peach, Suit.Heart, 7), new(CardKind.Slash, Suit.Diamond, 13)], [],
            [new(Peer) { StartingHand = [new(CardKind.Slash, Suit.Club, 12), new(CardKind.Slash, Suit.Club, 11)] }, new(Peer) { StartingHand = [] }],
            [new(Peer) { StartingHand = [] }], new string('c', 64));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
            UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(new StartGameCommand());
        Reach(() => P()?.SkillPrompt?.SkillId == Sibian && P()!.Choices.Any(c => Key(c) == "activate"));
        Answer(c => Key(c) == "activate"); Reach(() => P()?.SkillPrompt?.SkillId == Hold);
        var root = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.ZhangWenSibian is not null); var r = root.ZhangWenSibian!;
        var all = r.Entities.Select(e => e.CardId).ToArray(); var extrema = r.Extrema.ToArray(); var remainder = all.Except(extrema).Single();
        Need(r.Stage == ZhangWenSibianStage.GainChildren && r.Entities.Count == 4 && r.Entities.Select(e => e.Rank).Order().SequenceEqual([1, 1, 7, 13]) &&
            extrema.Length == 3 && V(0).Hand.Select(c => c.Id).Order().SequenceEqual(extrema.Order()) && V(1).HandCount == 0 && V(2).HandCount == 2 &&
            !E<ZhangWenSibianRecipientEvent>().Any(e => e.FrameId == root.Id),
            "Sibian actually reveals four physical entities, obtains all tied extrema and waits for the real three-card gain child before choosing a recipient.");
        var views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(game.CreateSnapshot(i))).ToArray();
        var frames = game.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
        game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(views.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(game.CreateSnapshot(i)))) &&
            frames.SequenceEqual(game.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))),
            "Cold retains the original four entities, paid extrema gain, native movement child and deferred whole-remainder choice.");
        Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(() => P()?.Choices.Any(c => Key(c) == "anniversary-zhang-wen") == true);
        Need(P()!.Choices.Where(c => c.Parameters.GetValueOrDefault("mode") == "give").SelectMany(c => c.Targets).Order().SequenceEqual([1, 3]),
            "Only the actual current tied minimum-Hand participants can receive the complete remainder, after the gain has settled.");
        Answer(c => c.Parameters.GetValueOrDefault("mode") == "give" && c.Targets.SequenceEqual([1])); Play();
        Need(V(0).HandCount == 3 && V(1).Hand.Select(c => c.Id).SequenceEqual([remainder]) &&
            E<ZhangWenSibianRecipientEvent>().Count(e => e.FrameId == root.Id && e.Recipient == 1) == 1 &&
            game.CardMovements.Count(m => extrema.Contains(m.CardId) && m.From == CardLocation.Processing && m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.anniversary:sibian.extrema") == 3 &&
            game.CardMovements.Count(m => m.CardId == remainder && m.From == CardLocation.Processing && m.To == CardLocation.Hand(1) && m.Reason.Value == "skill-program.anniversary:sibian.gift") == 1 &&
            !game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == root.Id),
            "Having obtained three cards with rank difference twelve still permits the current unrestricted gift; cold neither repays gains nor recreates entities.");

        var king = V(0).Hand.Single(c => c.Rank == 13).Id;
        Contest(king); Play();
        var first = E<ProgramSkillStartedEvent>().Last(e => e.SkillId == Songshu);
        Need(E<PindianResultDeterminedEvent>().Where(e => e.SkillId == Songshu).Select(e => e.Result).ToArray() is [{ SourceWon: true, SourceRank: 13, OpponentRank: 12 }] &&
            E<ZhangWenSongshuRefundedEvent>().Count(e => e.FrameId == first.FrameId && e.PhaseBefore == 1 && e.PhaseAfter == 0 && e.TurnBefore == 1 && e.TurnAfter == 0) == 1 &&
            CanContest() && V(0).HandCount == 2 && V(2).HandCount == 1,
            "A real winning Pindian discards its submitted cards, draws no reward and refunds exactly its accepted actual-Play debit.");
        var low = V(0).Hand.First(c => c.Rank == 1).Id; var ownerBefore = V(0).HandCount; var peerBefore = V(2).HandCount; var before = game.CardMovements.Max(m => m.Sequence);
        Contest(low); Play();
        var second = E<ProgramSkillStartedEvent>().Last(e => e.SkillId == Songshu);
        Need(second.FrameId != first.FrameId && E<PindianResultDeterminedEvent>().Where(e => e.SkillId == Songshu).Last().Result is { SourceWon: false, SourceRank: 1, OpponentRank: 11 } &&
            E<ZhangWenSongshuRefundedEvent>().Count() == 1 && V(0).HandCount == ownerBefore - 1 + 2 && V(2).HandCount == peerBefore - 1 + 2 &&
            game.CardMovements.Count(m => m.Sequence > before && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)) == 2 &&
            game.CardMovements.Count(m => m.Sequence > before && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(2)) == 2 && !CanContest(),
            "The second actual Pindian really grants two draws to both participants after not winning, and consumes this Play's unreimbursed limit.");

        string Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action") ?? "";
        PendingDecision? P() => Enumerable.Range(0, 4).Select(i => game.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        PlayerSnapshot V(int seat) => game.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        T[] E<T>() => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
        bool CanContest() => game.GetHumanLegalActions().Any(a => a.ProgramSkillId == Songshu && a.ProgramActivationId == "hand-contest");
        void Contest(int card) => Accept(new UseProgramSkillCommand(0, Songshu, "hand-contest", [card], [2], game.Revision, P()!.PromptId));
        void Play() => Reach(() => P() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        void Accept(GameCommand command) { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Zhang Wen command."); }
        void Answer(Func<PromptChoice, bool> predicate) { var p = P()!; var c = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && c is not null, "The original human must answer the exact current native choice."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, game.Revision)); }
        void Reach(Func<bool> ready) { for (var i = 0; i < 110; i++) { if (ready()) return; if (P() is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected Zhang Wen boundary " + Dump()); Accept(new AdvanceOneStepCommand(game.Revision)); } Need(false, "The bounded native Zhang Wen continuation was not reached."); }
        string Dump() => JsonSerializer.Serialize(new { game.State.TurnNumber, game.State.CurrentSeat, P = P(), Hands = Enumerable.Range(0, 4).Select(i => new { Seat = i, Cards = V(i).Hand, V(i).HandCount }),
            Frames = game.ResolutionStack, Started = E<ProgramSkillStartedEvent>(), Sibian = E<ZhangWenSibianStepEvent>(), Refunds = E<ZhangWenSongshuRefundedEvent>(), Pindian = E<PindianResultDeterminedEvent>() });
        void Need(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason + " " + Dump()); }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-zhang-wen", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var catalog = SkillProgramCatalog.Load(Rules, Presentation);
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "固定拼点角色", "", NoDraw, "wei", 4));
            b.AddDeck(new(Deck, "固定短牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "张温现行主流程", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
        private static string Presentation => JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
        { [Prime] = new { name = "固定顶四", description = "开局真实实体置顶" }, [NoDraw] = new { name = "不摸牌", description = "固定短局" },
          [Hold] = new { name = "获取子窗", description = "真实获取后暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } });
        private static string Rules => $$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
          {"id":"{{Prime}}","revision":1,"triggers":[{"id":"prime-top","window":"gameStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"selectOwnedCards","target":"owner","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"top-four"},{"op":"moveBoundCards","target":"owner","sourceBind":"top-four","destination":"drawPileTop","awaitMovementTriggers":true}]}]},
          {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"quiet","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
          {"id":"{{Hold}}","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.anniversary:sibian.extrema"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}]}]}
        ]}
        """;
    }
}
