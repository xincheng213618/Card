using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class MouHuangYueyingMobileChecks
{
    private const string Jizhi = "mou-mobile:jizhi", Qicai = "mou-mobile:qicai", Driver = "fixture:mhym-driver", Watch = "fixture:mhym-watch",
        Quiet = "fixture:mhym-quiet", Owner = "fixture:mhym-owner", Ally = "fixture:mhym-ally", Enemy = "fixture:mhym-enemy", Far = "fixture:mhym-far", Deck = "fixture:mhym-deck";

    public static void NativeJizhiExactExemptionAndQicaiThreeReceivedEntities()
    {
        var s = Scene.Start(); var drawTwo = s.V().Hand.Single(c => c.Kind == CardKind.DrawTwo).Id;
        var a = s.G.GetHumanLegalActions().First(a => a.CardId == drawTwo && a.ConversionSource is null);
        s.Accept(new PlayCardCommand(0, drawTwo, a.TargetSeats, s.G.Revision, s.P!.PromptId, a.PlayedCardKind, a.TargetCardId)); s.Play();
        var exempt = s.E<MouHuangYueyingJizhiExemptEvent>().Single(); var use = s.E<CardUseDeclaredEvent>().Single(e => e.SourceSeat == 0 && e.CardKind == CardKind.DrawTwo);
        s.Need(s.E<MouHuangYueyingJizhiDrawReturnedEvent>() is [{ ActualCount: 1, ReplacementCount: 0 }] && s.V().Hand.Any(c => c.Id == exempt.CardId) &&
            s.G.CardMovements.Count(m => m.CardId == exempt.CardId && m.Sequence == exempt.MovementSequence && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.ResolutionId) == 1,
            "Jizhi returns its exact native Draw1 inside a real ordinary-trick Use, grants the original drawn entity's exemption, and returns the original Use once.");

        var equipment = s.V().Hand.Single(c => c.Kind == CardKind.Crossbow).Id;
        s.Use(Driver, "discard-one", [equipment], []); s.Play();
        s.Need(s.G.CardMovements.Count(m => m.CardId == equipment && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) == 1,
            "The chosen equipment is truly in the public DiscardPile before Qicai.");
        s.Use(Qicai, "place-equipment", [], [1]);
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-huang-yueying-qicai") == true);
        s.Answer(c => c.Cards.SequenceEqual([equipment]) && c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.DiscardPile)); s.Play();
        var grant = s.E<MouHuangYueyingQiGrantedEvent>().Single();
        s.Need(grant.RecipientSeat == 1 && s.V(1).Equipment.Any(c => c.Id == equipment) &&
            s.G.CardMovements.Count(m => m.CardId == equipment && m.From == CardLocation.DiscardPile && m.To == CardLocation.Equipment(1)) == 1 &&
            Enumerable.Range(0, 4).All(viewer => s.G.CreateSnapshot(viewer).Players[1].Markers?.Single(m => m.Kind == PlayerMarkerKind.MouHuangYueyingQi).Count == 3) &&
            !s.G.GetHumanLegalActions().Any(a => a.SkillId == Qicai),
            "Qicai pays one actual discard-stock equipment placement, publicly issues three remaining tricks to that recipient, and closes the actual-Play once entry.");

        s.Use(Driver, "draw-four", [], [1]);
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch);
        var claims = s.E<MouHuangYueyingQiGainClaimedEvent>().Where(e => e.GrantId == grant.GrantId).OrderBy(e => e.GainSequence).ToArray();
        var drawFour = s.G.CardMovements.Where(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(1) &&
            m.Reason.Value == $"skill-program.{Driver}.Draw").OrderBy(m => m.Sequence).ToArray();
        s.Need(drawFour.Length == 4 && claims.Length == 3 && claims.Select(e => e.Remaining).SequenceEqual([2, 1, 0]) &&
            claims.Select(e => e.CardId).SequenceEqual(drawFour.Take(3).Select(m => m.CardId)) &&
            claims.Select(e => e.GainSequence).SequenceEqual(drawFour.Take(3).Select(m => (long)m.Sequence)) &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.MouHuangYueyingMobile is { Operation: SkillProgramEffectOp.TransferMouHuangYueyingQicaiGains, Paid: true }),
            "One real native Draw4 reserves only its first three ordinary-trick entities before any transfer child, preserving each actual movement batch and the exact owning paid frame. " +
            JsonSerializer.Serialize(new { Draw = drawFour, Claims = claims, Transfers = s.E<MouHuangYueyingQiTransferredEvent>(),
                Parents = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Select(f => new { f.Id, f.SkillId, f.MouHuangYueyingMobile }) }));
        s.Cold(); s.Answer(_ => true); s.Play();
        var transfers = s.E<MouHuangYueyingQiTransferredEvent>().Where(e => e.GrantId == grant.GrantId).ToArray();
        s.Need(transfers.Length == 3 && s.E<MouHuangYueyingQiClaimSettledEvent>().Count(e => e.GrantId == grant.GrantId) == 3 &&
            claims.All(c => s.V().Hand.Any(h => h.Id == c.CardId) && transfers.Count(t => t.CardId == c.CardId && t.GainSequence == c.GainSequence) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == c.CardId && m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.mou-mobile:qicai.transfer") == 1) &&
            s.V(1).HandCount == 1 && s.V(1).Hand.Single().Id == drawFour[3].CardId &&
            (s.G.CreateSnapshot(0).Players[1].Markers ?? []).All(m => m.Kind != PlayerMarkerKind.MouHuangYueyingQi) &&
            s.E<HpConditionalSkillGrantChangedEvent>().Any(e => e.SkillId == Qicai && !e.Added),
            "All three signed transfers settle once after a real first-transfer gain child removes Qicai; the fourth actual ordinary trick stays with its recipient and no debt is stacked or repaid.");

        s.Accept(new EndPlayPhaseCommand(0, s.G.Revision, s.P!.PromptId)); s.Reach(() => s.P?.Kind == DecisionKind.DiscardCards);
        var discard = s.P!; var sameKindOther = s.V().Hand.First(h => h.Kind == CardKind.Dismantlement && h.Id != exempt.CardId).Id;
        s.Need(!discard.ValidCardIds.Contains(exempt.CardId) && discard.ValidCardIds.Contains(sameKindOther) && discard.RequiredCardCount > 0,
            "The actual native hand-limit prompt exempts only Jizhi's drawn entity, while an unrelated same-kind entity remains eligible.");
        s.Accept(new DiscardCardsCommand(0, discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray(), discard.PromptId, s.G.Revision));
        s.Need(s.V().Hand.Any(c => c.Id == exempt.CardId), "The real discard payment retains the issued exempt card without altering the native hand limit or consuming the wrong entity.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-huang-yueying-mobile", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "mou:huang-yueying-mobile");
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{Driver}}","revision":1,"hpSkillGrants":[{"id":"healthy-qicai","skillId":"{{Qicai}}","minimumHp":3}],"activations":[
                  {"id":"discard-one","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]},
                  {"id":"draw-four","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":4}]}]},
                {"id":"{{Watch}}","revision":1,"triggers":[{"id":"first-real-transfer-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementReasons":["skill-program.mou-mobile:qicai.transfer"],"movementOccurrence":"perBatch","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"held","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":1}]}]},
                {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """;
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实弃牌与摸牌", description = "原生小局" },
                [Watch] = new { name = "真实取得子窗", description = "暂停后移除来源", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Quiet] = new { name = "静默", description = "跳过无关常规出牌" } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "真实原生小局") { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Owner, "移动谋黄月英本人", "", "standard:none", "shu", 3, [Jizhi, Driver, Watch]));
            foreach (var id in new[] { Ally, Enemy, Far }) b.AddGeneral(new(id, "真实其他角色", "", "standard:none", "qun", 8, [Quiet]));
            b.AddDeck(new(Deck, "固定小牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:dismantlement", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "移动谋黄月英原生团队小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Ally, Enemy, Far], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class Scene(GameEngine g, ContentRegistry registry)
    {
        internal GameEngine G { get; private set; } = g;
        internal PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        internal PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        internal T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        internal void Use(string skill, string activation, int[] cards, int[] targets) => Accept(new UseProgramSkillCommand(0, skill, activation, cards, targets, G.Revision, P!.PromptId));
        internal void Answer(Func<PromptChoice, bool> predicate) { var p = P!; Accept(new AnswerPromptCommand(0, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        internal void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        internal void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 160; i++)
            {
                if (ready()) return;
                Need(G.State.Winner == Winner.None, "Unexpected terminal cutoff.");
                if (P is null) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (P.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage");
                else throw new InvalidOperationException("Unexpected native Yueying boundary: " + JsonSerializer.Serialize(P));
            }
            throw new InvalidOperationException("Missing bounded native Yueying boundary: " + JsonSerializer.Serialize(P));
        }
        internal void Cold()
        {
            var view = SnapshotJson.Serialize(G.CreateSnapshot(0)); var moves = JsonSerializer.Serialize(G.CardMovements);
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(view == SnapshotJson.Serialize(G.CreateSnapshot(0)) && moves == JsonSerializer.Serialize(G.CardMovements), "The real first transfer's paid child restores without changing its frozen original claim or repeating payment.");
        }
        internal void Need(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason + " " + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.Phase, G.State.Winner, P, Hands = G.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.HandCount, p.Hp, p.Markers }) })); }
        internal void Accept(GameCommand command) { var r = G.Submit(command); Need(r.Accepted, "Rejected native " + command.GetType().Name + ": " + r.Error?.Message); }
        internal static Scene Start()
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            static ShanheStartingCard C(CardKind kind) => new(kind, Suit.Club, 7);
            var setup = new ShanheBattleSetup(83, 1, "mhym-main", Owner, 3, 0, 0, 0, 0, [], [C(CardKind.DrawTwo), C(CardKind.Crossbow), C(CardKind.Indulgence), C(CardKind.Peach)], [],
                [new(Enemy), new(Far)], [new(Ally)], new string('b', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false,
                MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new Scene(g, r); s.Accept(new StartGameCommand()); s.Play(); return s;
        }
    }
}
