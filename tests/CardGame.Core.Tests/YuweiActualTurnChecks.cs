using System.Text.Json;
using CardGame.Core;
using CardGame.Core.Campaigns;
using CardGame.Content.Standard;

internal static class YuweiActualTurnChecks
{
    private const string Shiyuan = StandardShanheS5HanEmperorsPackage.ShiyuanId;
    private const string Yuwei = StandardShanheS5HanEmperorsPackage.YuweiId;
    private const string Source = "fixture:yuwei-source", Driver = "fixture:yuwei-prepare";
    private const string Peer = "fixture:yuwei-peer", Quiet = "fixture:yuwei-quiet", Watch = "fixture:yuwei-watch";
    private const string Deck = "fixture:yuwei-deck";
    private const string DrawReason = "skill-program." + Shiyuan + ".target-draw";

    public static void PrepareNativeUsesDoubleQualifiedShiyuanAndColdChildren()
    {
        var s = Scene.Start(qualified: true); s.Target();
        s.Until(() => s.P?.SkillPrompt?.SkillId == Watch);
        var first = s.E<ProgramShiYuanTargetDrawEvent>().Single();
        var producer = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Driver);
        var receiver = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Shiyuan);
        var native = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Action?.ActionId == first.CardActionId);
        Need(s.G.CreateSnapshot(0).TurnNumber == 1 && producer.WindowContext?.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
            first is { OwnerSeat: 1, SourceSeat: 0, DrawCount: 3 } && native is { CardKind: CardKind.Slash, PhysicalCardIds.Count: 0 } &&
            s.V(1).Hp == 3 && s.V().Hp == 4 && s.Draws() == 3 && s.V(1).HandCount == 4 &&
            !s.E<ProgramYuWeiActiveTurnEvent>().Any() && receiver.WindowContext?.CardUse?.CardActionId == native.Action!.ActionId,
            "Before any Play-phase YuWei notification, a real Qun preparation Slash targets the qualified emperor and the mandatory ShiYuan pays its full Draw3 before a true gain child.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); s.Play();
        var draws = s.E<ProgramShiYuanTargetDrawEvent>();
        var outcomeEvidence = JsonSerializer.Serialize(new
        {
            Slashes = s.Slashes(),
            Dodges = s.E<CardRespondedEvent>().Where(e => e.ResponderSeat == 1 && e.EffectiveCardKind == CardKind.Dodge),
            Damage = s.E<DamageAppliedEvent>(), DrawFacts = draws,
            DistinctDrawActions = draws.Select(e => e.CardActionId).Distinct().Count(),
            DrawMovementCount = s.Draws(), TargetHandCount = s.V(1).HandCount,
            Started = s.E<ProgramBindingStartedEvent>().Where(e => e.SkillId == Shiyuan),
            Resolved = s.E<ProgramBindingResolvedEvent>().Where(e => e.SkillId == Shiyuan),
            PlayNotifications = s.E<ProgramYuWeiActiveTurnEvent>(),
            CurrentPhase = s.G.CreateSnapshot(0).Phase, Pending = s.P?.Kind
        });
        var playNotifications = s.G.Events.Where(e => e.Payload is ProgramYuWeiActiveTurnEvent
            { OwnerSeat: 1, TurnNumber: 1 }).ToArray();
        var preparationBeforePlayNotification = playNotifications is [var notification] && s.G.Events
            .Where(e => e.Payload is CardUseDeclaredEvent { SourceSeat: 0, CardKind: CardKind.Slash } ||
                e.Payload is ProgramShiYuanTargetDrawEvent { OwnerSeat: 1, SourceSeat: 0, TurnNumber: 1 })
            .All(e => e.Sequence < notification.Sequence);
        Need(s.Slashes().Length == 3 && s.E<CardRespondedEvent>().Count(e => e.ResponderSeat == 1 && e.EffectiveCardKind == CardKind.Dodge) == 3 &&
            !s.E<DamageAppliedEvent>().Any() && draws.Length == 2 && draws.All(e => e.OwnerSeat == 1 && e.SourceSeat == 0 && e.DrawCount == 3 && e.TurnNumber == 1) &&
            draws.Select(e => e.CardActionId).Distinct().Count() == 2 && s.Draws() == 6 && s.V(1).HandCount == 4 &&
            s.E<ProgramBindingStartedEvent>().Count(e => e.SkillId == Shiyuan) == 2 &&
            s.E<ProgramBindingResolvedEvent>().Count(e => e.SkillId == Shiyuan && e.Activated && e.Completed) == 2 &&
            preparationBeforePlayNotification,
            "Three independent preparation Slashes are genuinely Dodged without changing HP: the same option draws exactly twice, the third is exhausted, and cold replay never pays the first draw again. All preparation uses and draws precede the retained native Play notification. Actual: " + outcomeEvidence);
        s.Cold();

        var ordinary = Scene.Start(qualified: false); ordinary.Target();
        ordinary.Until(() => ordinary.P?.SkillPrompt?.SkillId == Watch);
        ordinary.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); ordinary.Play();
        Need(ordinary.Slashes().Length == 3 && ordinary.E<CardRespondedEvent>().Count(e => e.ResponderSeat == 1 && e.EffectiveCardKind == CardKind.Dodge) == 3 &&
            ordinary.E<ProgramShiYuanTargetDrawEvent>() is [{ DrawCount: 3, OwnerSeat: 1 }] && ordinary.Draws() == 3 &&
            ordinary.V(1).HandCount == 1 && !ordinary.E<ProgramYuWeiActiveTurnEvent>().Any(),
            "An enemy emperor with the same printed Lord skill but no encounter qualification still gets only one ShiYuan use; neither enemy identity nor the Qun turn borrows Lord qualification.");
        ordinary.Cold();
    }

    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(seat => G.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int Draws() => G.CardMovements.Count(move => move.To == CardLocation.Hand(1) && move.Reason.Value == DrawReason);
        public CardUseDeclaredEvent[] Slashes() => E<CardUseDeclaredEvent>().Where(e => e.CardKind == CardKind.Slash && e.SourceSeat == 0).ToArray();
        public void Accept(GameCommand command)
        {
            var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
            Need(result.Accepted, result.Error?.ToString() ?? "The actual pre-Play YuWei command was rejected.");
        }
        public void Answer(Func<PromptChoice, bool> select)
        {
            var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(select).Id, G.Revision));
        }
        public void Until(Func<bool> done)
        {
            for (var i = 0; i < 180; i++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "The bounded YuWei fixture remains live.");
                if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected YuWei prompt: " + JsonSerializer.Serialize(P));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The actual pre-Play YuWei boundary was not reached: " + JsonSerializer.Serialize(P));
        }
        public void Target()
        {
            Until(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target") == true);
            Answer(c => c.Targets.SequenceEqual([1]));
        }
        public void Play() => Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            G.CardMovements, Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint())
        });
        public void Cold()
        {
            var state = State();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == state, "The actual preparation use, qualification, per-option quota, once-paid Draw3 and every private view cold-restore identically.");
        }
        public static Scene Start(bool qualified)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
                new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new StandardShanheS5HanEmperorsPackage(), new Fixture());
            var setup = new ShanheBattleSetup(81, 1, "fixture-yuwei-prepare", Source, 4, 0, 0, 0, 0, [], [], [],
                [new(StandardShanheS5HanEmperorsPackage.LiubianId, ExtraSkillIds: [Watch])
                    { StartingHand = [new(CardKind.Dodge, Suit.Spade, 2)], QualifiedPrintedLordSkillIds = qualified ? [Yuwei] : null },
                 new(Peer) { StartingHand = [] }, new(Peer) { StartingHand = [] }], [], new string('f', 64));
            var game = GameEngine.CreateStandard(new()
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 3
            }, registry);
            var scene = new Scene(game, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-yuwei-real-turn", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"triggers":[{"id":"actual-prepare-slashes","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[
                {"op":"selectTarget","target":"owner","targetKind":"otherLivingVirtualSlashTarget"},
                {"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true},
                {"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true},
                {"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true}]}]},
              {"id":"{{{Watch}}}","revision":1,"triggers":[{"id":"first-real-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{{DrawReason}}}"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}]}]},
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var names = new Dictionary<string, object>
            {
                [Driver] = new { name = "真实准备发杀", description = "同一准备阶段的三次独立原生用牌" },
                [Watch] = new { name = "真实诗怨获牌孩子", description = "已摸牌后继续", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Quiet] = new { name = "静默同桌", description = "跳过出牌阶段" }
            };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Source, "真实群势力回合持有者", "supporter", Driver, "qun", 4));
            builder.AddGeneral(new(Peer, "静默其他角色", "supporter", Quiet, "wei", 4));
            builder.AddDeck(new(Deck, "零起手真实闪牌堆", 0, 0, [])
            { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 7)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "余威全回合原生小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Source, StandardShanheS5HanEmperorsPackage.LiubianId, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
