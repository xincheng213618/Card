using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class OlMoSimaYiChecks
{
    private const string Gui = "ol:mo-guifu", Mou = "ol:mo-moubian", Zhou = "ol:mo-zhouxi", Lie = "ol:mo-lieshi";
    private const string Owner = "fixture:msy-owner", Ally = "fixture:msy-ally", Enemy = "fixture:msy-enemy", Far = "fixture:msy-far", Quiet = "fixture:msy-quiet";

    public static void ActualDamageRecordsMarkedAliasesLearningAndRoundSlashReturn()
    {
        var s = Scene.Start(); s.Play();
        var mark = s.E<MoSimaYiDodgeMarkedEvent>().Single();
        s.Need(s.V().Hand.Any(c => c.Id == mark.CardId && c.Kind == CardKind.Dodge && c.SkillMarkLabel?.Contains("诡伏", StringComparison.Ordinal) == true) &&
            s.G.CardMovements.Count(m => m.CardId == mark.CardId && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) &&
                m.Reason.Value == "skill-program.ol:mo-guifu.obtain") == 1,
            "Round start obtains one actual marked stock Dodge, without manufacturing a draw or physical card.");
        var slashes = s.V().Hand.Where(c => c.Kind == CardKind.Slash).Select(c => c.Id).ToArray();
        var duel = s.V().Hand.Single(c => c.Kind == CardKind.Duel).Id;
        s.Card(s.Actions().First(a => a.CardId == slashes[0] && a.ConversionSource is null && a.TargetSeats.SequenceEqual([3]))); s.Play();
        s.Card(s.Actions().First(a => a.CardId == duel && a.ConversionSource is null && a.TargetSeats.SequenceEqual([3]))); s.Play();
        s.Accept(new UseProgramSkillCommand(0, Lie, "two-different-oaths", [], [1], s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Lie && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("option") == "0"));
        s.Answer(c => c.Parameters.GetValueOrDefault("option") == "0"); s.Play();
        var lieshi = s.E<MoSimaYiStartedEvent>().Single(e => e.Operation == SkillProgramEffectOp.ResolveMoSimaYiLieshi);
        s.Need(s.V().IsJudgmentAreaAbolished && s.V().Hp == 11 &&
            s.E<MoSimaYiLieshiChosenEvent>().Where(e => e.FrameId == lieshi.FrameId).Select(e => (e.ActorSeat, e.Option)).SequenceEqual([(0, 0), (1, 1)]) &&
            s.E<MoSimaYiCompletedEvent>().Count(e => e.FrameId == lieshi.FrameId) == 1 &&
            s.G.CardMovements.Any(m => m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile && m.CardKind == CardKind.Dodge && m.Reason.Value == "skill-program.ol:mo-lieshi.discard") &&
            s.E<MoSimaYiDodgeMarkedEvent>().Length == 2,
            "The complete gainable Lieshi abolishes the first participant's judgment area, deals real Fire damage, drains Guifu's HP-change obtain, then lets the actual other participant discard all Dodge as a different option.");
        var records = s.E<MoSimaYiRecordAddedEvent>().Where(e => e.OwnerSeat == 0).ToArray();
        s.Need(records.Length == 3 && records.Count(e => e.CardKind == CardKind.Slash) == 1 && records.Count(e => e.CardKind == CardKind.Duel) == 1 &&
            records.Count(e => e.SkillId == Lie && e.CardKind is null) == 1 && !records.Any(e => e.SkillId == Gui),
            "Actual native Slash and Duel record their effective names; the direct Lieshi Fire damage records its real skill, never Guifu or a conversion ancestor.");

        s.End(); s.Reach(s.MoubianOffer); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); s.Play();
        var roundAcquires = s.E<MoSimaYiStartedEvent>().Where(e => e.Source.SkillId == Gui && e.Source.BindingId == "acquire-round").ToArray();
        var healthAcquires = s.E<MoSimaYiStartedEvent>().Where(e => e.Source.SkillId == Gui && e.Source.BindingId == "acquire-health").ToArray();
        s.Need(roundAcquires.Select(e => e.Turn).SequenceEqual([1, 5]) && roundAcquires.Select(e => e.Round).Distinct().Count() == 2 &&
            roundAcquires.All(e => s.E<MoSimaYiDodgeMarkedEvent>().Count(m => m.FrameId == e.FrameId) == 1) &&
            healthAcquires.Length == 1 && s.E<MoSimaYiDodgeMarkedEvent>().Length == 3,
            "Crossing all three NPC turns obtains no extra round Dodge: only actual round starts at turns 1 and 5 obtain once each, independently of the one real HP-change obtain.");
        var learned = s.E<MoSimaYiStartedEvent>().Single(e => e.Operation == SkillProgramEffectOp.ResolveMoSimaYiMoubian);
        s.Need(s.E<MoSimaYiSkillFrozenEvent>().Where(e => e.FrameId == learned.FrameId).Select(e => e.SkillId).ToHashSet().SetEquals([Lie, Zhou]) &&
            s.E<MoSimaYiSkillGrantedEvent>().Count(e => e.FrameId == learned.FrameId && !e.Temporary) == 2 &&
            s.E<OlMoRumoEnteredEvent>() is [{ Origin.SkillId: Mou }],
            "Moubian freezes exactly the then-recorded skill identities plus derived Zhouxi, and enters the single shared Rumo ledger without granting card names as skills.");
        s.Card(s.Actions().First(a => a.CardId == slashes[1] && a.ConversionSource is null && a.TargetSeats.SequenceEqual([3]))); s.Play();
        var alias = s.Actions().First(a => a.ConversionSource?.SkillId == Gui && a.PlayedCardKind == CardKind.Slash && a.TargetSeats.SequenceEqual([3]));
        var id = alias.CardId!.Value;
        s.Card(alias); s.Play();
        var used = s.E<MoSimaYiNameUsedEvent>().Single(e => e.Kind == CardKind.Slash);
        s.Need(s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 &&
            !s.E<CardUseDebitRecordedEvent>().Any(e => e.Debit.CardActionId == used.ActionId) &&
            !s.Actions().Any(a => a.ConversionSource?.SkillId == Gui && a.PlayedCardKind == CardKind.Slash),
            "A genuine marked Dodge is paid once as the recorded Slash after ordinary quota was spent; this Use consumes the actual-turn name slot and no ordinary Slash debit.");
        s.End(); s.Play();
        var retaliations = s.E<MoSimaYiSlashIssuedEvent>().Where(e => e.Origin.Target == 0 && e.Origin.Actor == 3).ToArray();
        s.Need(retaliations.Length == 1 && retaliations.All(e =>
            s.E<CardUseDeclaredEvent>().Count(d => d.ResolutionId == e.Origin.UseId && d.CardId == 0 && d.SourceSeat == e.Origin.Actor) == 1 &&
            s.E<CardUseFinishedEvent>().Count(d => d.ResolutionId == e.Origin.UseId) == 1 &&
            s.E<MoSimaYiSlashReturnedEvent>().Count(d => d.UseId == e.Origin.UseId && d.ActionId == e.Origin.ActionId) == 1 &&
            !s.E<CardUseDebitRecordedEvent>().Any(d => d.Debit.CardActionId == e.Origin.ActionId)),
            "Repeated damage to one participant yields one real round-end forced neutral Slash, a full native defense and exactly one parent return, without ordinary quota.");
        s.Need(s.ColdDone && s.E<MoSimaYiSampledEvent>().Any(e => e.Count == 3), "The real three-skill private pending has survived representative Cold recovery.");
        var lease = s.E<MoSimaYiSkillGrantedEvent>().First(e => e.Temporary);
        while (!s.E<ProgramNextActualTurnStartSkillGrantExpiredEvent>().Any(e => e.Grant.SourceId == lease.GrantSourceId))
        { s.End(); s.Play(); }
        s.Need(s.E<ProgramNextActualTurnStartSkillGrantExpiredEvent>().Count(e => e.Grant.SourceId == lease.GrantSourceId) == 1 &&
            s.E<OlMoRumoEnteredEvent>().Length == 1 && s.E<MoSimaYiRecordAddedEvent>().Count(e => e.OwnerSeat == 0 && e.SkillId == Lie) == 1,
            "The chosen temporary skill expires only at its recipient's next real start; permanent records and the one shared Rumo entry remain.");
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G = game;
        public bool ColdDone;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public IReadOnlyList<LegalAction> Actions() => G.GetHumanLegalActions();
        public bool MoubianOffer() => P?.SkillPrompt?.SkillId == Mou && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Card(LegalAction a) => Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId)
            { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P!; var choice = p.Choices.FirstOrDefault(predicate);
            Need(choice is not null, "A real published human choice must exist.");
            Accept(new AnswerPromptCommand(0, p.PromptId, choice!.Id, G.Revision));
        }
        public void Accept(GameCommand command) { var result = G.Submit(command); Need(result.Accepted, "Native command rejected: " + result.Error?.Message); }
        public void Reach(Func<bool> done)
        {
            for (var n = 0; n < 260; n++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "Unexpected bounded native cutoff.");
                if (P is not { PlayerSeat: 0 } p) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mo-sima-yi") &&
                    G.ResolutionStack.LastOrDefault() is ProgramSkillFrame { MoSimaYi.Stage: MoSimaYiStage.ChoosingSkill })
                {
                    if (!ColdDone)
                    {
                        var view = SnapshotJson.Serialize(G.CreateSnapshot(0));
                        G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
                        Need(view == SnapshotJson.Serialize(G.CreateSnapshot(0)), "The exact sampled skill names and source survive Cold without resampling or double granting.");
                        ColdDone = true;
                    }
                    Answer(_ => true);
                }
                else if (MoubianOffer()) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.SkillPrompt?.SkillId == "boundary:juece-current" &&
                    p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
                    Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)
                    Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage");
                else throw new InvalidOperationException("Unexpected real Magic Sima Yi prompt: " + JsonSerializer.Serialize(p));
            }
            throw new InvalidOperationException("The bounded Magic Sima Yi flow did not reach its native checkpoint.");
        }
        public void Need(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.Phase, P,
                Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
                Facts = G.Events.Where(e => e.Payload.GetType().Name.StartsWith("MoSimaYi", StringComparison.Ordinal)).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) }));
        }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
                new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
            static ShanheStartingCard C(CardKind k, int rank) => new(k, Suit.Spade, rank);
            var setup = new ShanheBattleSetup(31, 1, "mo-sima-yi-native", Owner, 12, 0, 0, 0, 0, [],
                [C(CardKind.Slash, 7), C(CardKind.Slash, 8), C(CardKind.Duel, 9), C(CardKind.Dodge, 10)], [],
                [new(Enemy) { StartingHand = [] }, new(Far) { StartingHand = [] }], [new(Ally) { StartingHand = [C(CardKind.Dodge, 11)] }], new string('d', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 17 }, registry);
            var s = new Scene(game, registry); s.Accept(new StartGameCommand()); return s;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ol-mo-sima-yi", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Quiet}}","revision":1,
                "triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,
                "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """;
            var programs = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Quiet] = new { name = "静默同行", description = "跳过无关出牌" } } }));
            b.AddSkill(new(Quiet, "静默同行", "原生跳过无关出牌") { Program = programs.Programs[Quiet], ProgramPresentation = programs.Presentations[Quiet] });
            foreach (var kind in new[] { CardKind.Slash, CardKind.Dodge, CardKind.Duel })
            { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:msy-card-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            b.AddGeneral(new(Owner, "魔司马懿真实主链", "", "standard:none", "wei", 12, [Gui, Mou, Lie]));
            foreach (var peer in new[] { Ally, Enemy, Far }) b.AddGeneral(new(peer, "静默同行", "", Quiet, "wei", 12));
            b.AddDeck(new("fixture:msy-deck", "固定六十四张真实闪", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64)
                .Select(i => new ContentDeckPhysicalCard("fixture:msy-card-Dodge", i % 2 == 0 ? Suit.Heart : Suit.Club, i % 13 + 1)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "魔司马懿真实小局", 4, 4, new Dictionary<string, int>(), "fixture:msy-deck",
                GeneralPoolIds: [Owner, Ally, Enemy, Far], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
