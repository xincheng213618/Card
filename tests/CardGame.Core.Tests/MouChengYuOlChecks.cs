using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class MouChengYuOlChecks
{
    private const string Hero = "mou:cheng-yu-ol", Lidan = "mou-ol:lidan", Danchi = "mou-ol:danchi";
    private const string Owner = "fixture:mcy-owner", Ally = "fixture:mcy-ally", Enemy = "fixture:mcy-enemy", Far = "fixture:mcy-far", Quiet = "fixture:mcy-quiet";

    public static void StackedTargetsAndActualNextUseNativeRewardsCold()
    {
        var s = Scene.Start(); s.Play();
        var drawCards = s.V().Hand.Where(c => c.Kind == CardKind.DrawTwo).Select(c => c.Id).Order().ToArray();
        var chain = s.V().Hand.Single(c => c.Kind == CardKind.IronChain).Id;
        var slash = s.V().Hand.Single(c => c.Kind == CardKind.Slash).Id;
        var duel = s.V().Hand.Single(c => c.Kind == CardKind.Duel).Id;
        s.Card(drawCards[0], []); s.Play();
        s.Card(chain, [0]); s.Play();
        s.Need(s.E<MouLidanPermitEvent>().Count(e => e.ForSlash) == 2,
            "Two completed real tricks independently issue two permanent next-Slash target permissions.");

        s.Card(slash, [1]);
        s.Reach(() => s.Verb("mou-lidan", "add"));
        var stack = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == slash).MouLidan!;
        s.Need(stack.Maximum == 2 && s.E<MouLidanConsumedEvent>().Count(e => e.UseId == stack.UseId) == 2,
            "The next matching actual Slash consumes the whole two-permission stack exactly once.");
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "mou-lidan" && c.Targets.SequenceEqual([2]));
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "mou-lidan" && c.Targets.SequenceEqual([3]));
        s.Reach(s.DanchiOffer);
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Reach(() => s.G.CreateSnapshot(1).PendingDecision?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-danchi") == true);
        s.Play();
        var promise = s.E<MouDanchiPromiseEvent>().Single();
        s.Need(promise.Damage.SourceSeat == 0 && promise.Damage.VictimSeat == 1 && promise.Category == SkillProgramCardCategory.Basic &&
            s.E<MouDanchiAcceptedEvent>().Length == 1 && s.E<MouDanchiNextUseEvent>().Length == 0 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == stack.UseId) == 1 &&
            s.E<DamageAppliedEvent>().Where(e => e.SourceSeat == 0).Select(e => e.TargetSeat).Order().SequenceEqual([1, 2, 3]),
            "The actual nearby victim privately chooses Basic through native AI; the ongoing three-target damage card is excluded from its own next-Use promise.");
        s.PaidOnce(slash);

        s.Card(duel, [3]);
        s.Reach(() => s.Verb("mou-lidan", "add"));
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "mou-lidan" && c.Targets.SequenceEqual([2]));
        s.Reach(() => s.Verb("mou-danchi", "slash"));
        s.Cold();
        var next = s.E<MouDanchiNextUseEvent>().Single();
        var physicalNext = s.E<CardUseDeclaredEvent>().Single(e => e.CardId == duel);
        var reward = s.E<MouDanchiNativeIssuedEvent>().Single(e => e.Origin.Kind == CardKind.DrawTwo).Origin;
        var suspension = s.E<MouDanchiCursorSuspendedEvent>().Single();
        var mother = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == physicalNext.ResolutionId);
        s.Need(next.PromiseId == promise.FrameId && next.UseId == physicalNext.ResolutionId && next.Kind == CardKind.Duel &&
            next.Category == SkillProgramCardCategory.Trick && next.ActionId != promise.Damage.OriginalActionId &&
            s.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == reward.UseId && e.CardId == 0 && e.CardKind == CardKind.DrawTwo && e.SourceSeat == 0) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == reward.UseId) == 1 &&
            s.E<MouDanchiNativeReturnedEvent>().Count(e => e.UseId == reward.UseId) == 1 &&
            s.G.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)) == 4 &&
            suspension.Suspension.UseId == mother.Id && suspension.Suspension.ActionId == mother.Action!.ActionId &&
            suspension.Suspension.Kind == CardKind.Duel && suspension.Suspension.DuelActive && suspension.Suspension.AttackActive &&
            mother.Step == ResolutionFrameStep.Completed && mother.Continuations.Duel is { Active: false } &&
            mother.CardAttack is { Active: false },
            "The damage source's next true Use is the complete real two-target Duel; its different type issues and returns a genuine zero-material DrawTwo while the exact original Duel/attack cursors remain on their suspended mother.");
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "mou-danchi" && c.Targets.SequenceEqual([3]));
        s.Reach(() => s.Verb("mou-lidan", "add"));
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "mou-lidan" && c.Targets.SequenceEqual([2]));
        s.Play();
        var extraSlash = s.E<MouDanchiNativeIssuedEvent>().Single(e => e.Origin.Kind == CardKind.Slash).Origin;
        s.Need(s.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == extraSlash.UseId && e.CardId == 0 && e.SourceSeat == 0) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == extraSlash.UseId) == 1 &&
            s.E<MouDanchiNativeReturnedEvent>().Count(e => e.UseId == extraSlash.UseId) == 1 &&
            s.E<MouDanchiCompletedEvent>().Count(e => e.PromiseId == promise.FrameId && e.Reward) == 1 &&
            s.E<MouDanchiCursorRestoredEvent>().Count(e => e.FrameId == suspension.FrameId && e.UseId == mother.Id && e.ActionId == mother.Action!.ActionId) == 1 &&
            !s.E<CardUseDebitRecordedEvent>().Any(e => e.Debit.CardActionId == extraSlash.ActionId) &&
            s.E<CardUseDebitRecordedEvent>().Count(e => e.Debit.CardActionId == stack.ActionId) == 1,
            "The optional extra Slash preserves real defenses and target expansion, completes once, and returns to the original paid parent without consuming an ordinary Play Slash debit.");
        s.PaidOnce(drawCards[0]); s.PaidOnce(duel); s.PaidOnce(chain);

        s.Card(drawCards[1], []);
        s.Reach(() => s.Verb("mou-lidan", "add"));
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "mou-lidan" && c.Targets.SequenceEqual([1]));
        s.Play();
        var realDuel = s.E<CardUseDeclaredEvent>().Single(e => e.CardId == duel);
        var applied = s.E<MouLidanTargetsAppliedEvent>().Single(e => e.UseId == realDuel.ResolutionId);
        s.Need(s.E<MouLidanTargetSlotEvent>().Where(e => e.FrameId == applied.FrameId && e.Added).Select(e => e.Seat).SequenceEqual([2]) &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == realDuel.ResolutionId) == 1 &&
            s.E<MouDanchiAcceptedEvent>().Length == 1 && s.E<MouDanchiNextUseEvent>().Length == 1 &&
            s.E<MouDanchiNativeIssuedEvent>().Length == 2 && s.G.ResolutionStack.Count == 0 &&
            s.G.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)) == 6 &&
            s.G.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(1)) == 2,
            "The genuine two-target Duel fully returns after its reward subtree, then a later extra-target DrawTwo retains all real draws; Danchi remains once for the actual turn and never reissues the settled promise.");
        s.PaidOnce(drawCards[1]);
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool DanchiOffer() => P?.SkillPrompt?.SkillId == Danchi && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public bool Verb(string action, string verb) => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action && c.Parameters.GetValueOrDefault("verb") == verb) == true;
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void Card(int id, IReadOnlyList<int> targets)
        {
            var action = G.GetHumanLegalActions().Single(a => a.CardId == id && a.ConversionSource is null && a.TargetSeats.SequenceEqual(targets));
            Accept(new PlayCardCommand(0, id, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId));
        }
        public void Answer(Func<PromptChoice, bool> choose)
        {
            var p = P!; var choice = p.Choices.FirstOrDefault(choose);
            Need(choice is not null, "The actual native choice must be published.");
            Accept(new AnswerPromptCommand(0, p.PromptId, choice!.Id, G.Revision));
        }
        public void Accept(GameCommand command) { var result = G.Submit(command); Need(result.Accepted, "Native command rejected: " + result.Error?.Message); }
        public void Reach(Func<bool> done)
        {
            for (var n = 0; n < 180; n++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "Unexpected bounded native cutoff.");
                if (P is not { PlayerSeat: 0 } p) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (Verb("mou-lidan", "finish")) Answer(c => c.Parameters.GetValueOrDefault("verb") == "finish");
                else if (DanchiOffer()) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification)
                    Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage");
                else throw new InvalidOperationException("Unexpected native Mou Cheng Yu choice: " + JsonSerializer.Serialize(p));
            }
            throw new InvalidOperationException("The bounded Mou Cheng Yu flow did not reach its native checkpoint.");
        }
        public void Cold()
        {
            var before = new[] { SnapshotJson.Serialize(G.CreateSnapshot(0)), SnapshotJson.Serialize(G.CreateSnapshot(1)) };
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before.SequenceEqual(new[] { SnapshotJson.Serialize(G.CreateSnapshot(0)), SnapshotJson.Serialize(G.CreateSnapshot(1)) }),
                "The exact completed mother Duel, its suspended attack cursor and returned DrawTwo survive Cold at the optional native Slash choice without repeating payment or reward.");
        }
        public void PaidOnce(int id) => Need(G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
            "Each original physical Use material is paid and cleaned up exactly once.");
        public void Need(bool condition, string text)
        {
            if (!condition) throw new InvalidOperationException(text + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.Phase, P,
                Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
                Facts = G.Events.Where(e => e.Payload.GetType().Name.StartsWith("MouLidan", StringComparison.Ordinal) || e.Payload.GetType().Name.StartsWith("MouDanchi", StringComparison.Ordinal)).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) }));
        }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
                new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
            static ShanheStartingCard C(CardKind kind, int rank) => new(kind, Suit.Spade, rank);
            var setup = new ShanheBattleSetup(31, 1, "mou-cheng-yu-ol-native", Owner, 12, 0, 0, 0, 0, [],
                [C(CardKind.DrawTwo, 2), C(CardKind.DrawTwo, 3), C(CardKind.IronChain, 4), C(CardKind.Slash, 7), C(CardKind.Duel, 9)], [],
                [new(Enemy) { StartingHand = [] }, new(Far) { StartingHand = [] }], [new(Ally) { StartingHand = [] }], new string('e', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 5 }, registry);
            var s = new Scene(game, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-cheng-yu-ol", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Quiet}}","revision":1,
                "triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,
                "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """;
            var programs = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Quiet] = new { name = "静默同行", description = "跳过无关出牌阶段" } } }));
            b.AddSkill(new(Quiet, "静默同行", "原生跳过无关出牌阶段") { Program = programs.Programs[Quiet], ProgramPresentation = programs.Presentations[Quiet] });
            foreach (var kind in new[] { CardKind.Slash, CardKind.Dodge, CardKind.DrawTwo, CardKind.IronChain, CardKind.Duel })
            { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:mcy-card-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            b.AddGeneral(new(Owner, "谋程昱真实主链", "", "standard:none", "wei", 12, [Lidan, Danchi]));
            foreach (var id in new[] { Ally, Enemy, Far }) b.AddGeneral(new(id, "静默同行", "", Quiet, "wei", 12));
            b.AddDeck(new("fixture:mcy-deck", "固定六十四张真实闪", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64)
                .Select(i => new ContentDeckPhysicalCard("fixture:mcy-card-Dodge", i % 2 == 0 ? Suit.Heart : Suit.Club, i % 13 + 1)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋程昱OL原生主链", 4, 4, new Dictionary<string, int>(), "fixture:mcy-deck",
                GeneralPoolIds: [Owner, Ally, Enemy, Far], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
