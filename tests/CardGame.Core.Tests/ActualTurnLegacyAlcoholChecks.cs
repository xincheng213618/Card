using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ActualTurnLegacyAlcoholChecks
{
    private const string Driver = "fixture:legacy-alcohol-driver";
    private const string Rescue = "fixture:legacy-alcohol-rescue";
    private const string Hp = "fixture:legacy-alcohol-hp";
    private const string Completed = "fixture:legacy-alcohol-completed";
    private const string Peer = "fixture:legacy-alcohol-peer";
    private const string Owner = "fixture:legacy-alcohol-owner";
    private const string Mode = "identity:classic-legacy-alcohol-fixture";

    public static void SynchronousLegacyWineWaitsForHpAndCompletionChildren()
    {
        foreach (var recoveryPolicy in new[] { false, true }) Exercise(recoveryPolicy);
    }

    private static void Exercise(bool recoveryPolicy)
    {
        var (game, registry) = Start(recoveryPolicy);
        Require(game.State.CurrentSeat == 0 && game.State.Players[0].Hp == 1,
            "The human's actual turn starts with a naturally wounded one-HP fixture owner.");
        PlayFirstRealUse(game);
        ReachPlay(game);
        var turn = game.State.TurnNumber;
        var starts = Facts<TurnStartedEvent>(game).Count();
        Require(Facts<CurrentTurnCardUseKindsRecordedEvent>(game).Where(e => e.ActorSeat == 0 &&
                e.TurnNumber == turn).Select(e => e.ActualTurnActorUseOrdinal).SequenceEqual([1]),
            "One real converted hand card occupies the first actual-use slot before the dying Wine.");

        var lord = game.CreateSnapshot(0, revealAll: true).Players.Single(p => p.Role == Role.Lord).Seat;
        Require(lord != 0 && game.State.Players[lord].Hp == 2 &&
                game.State.Players.Where(p => p.Seat != 0).All(p => p is { IsAlive: true, Hp: > 0 }) &&
                registry.GetSkill(Peer).Program!.CardPolicies.Any(p =>
                    p.Kind == SkillProgramCardPolicyKind.RedirectOwnTurnFactionRecovery) == recoveryPolicy,
            "Both small catalogs retain naturally positive-HP peers; the second supplies a true lord recovery policy without a qualifying zero-HP candidate.");
        Use(game, "hurt-self", []);

        ReachOption(game, Rescue, "flip-choice");
        var victimDying = game.ResolutionStack.OfType<DyingFrame>().Single(f => f.VictimSeat == 0);
        var rescue = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Rescue);
        var driver = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Driver);
        var rescueId = rescue.Id;
        var driverId = driver.Id;
        var dyingId = victimDying.Id;
        Require(game.State.Players[0] is { IsAlive: true, Hp: 0, IsFaceDown: false } &&
                rescue.WindowContext?.Window == SkillProgramTriggerWindow.SelfDyingResponse &&
                rescue.WindowContext.ParentFrameId == victimDying.Id && victimDying.ResponderSeat == 0 &&
                victimDying.ParentFrameId == driver.Id && game.ResolutionStack.OfType<DyingFrame>().Count() == 1 &&
                game.State.Players.Where(p => p.Seat != 0).All(p => p is { IsAlive: true, Hp: > 0 }),
            "The real zero-HP owner receives its own exact native self-dying choice under the HP-loss activation, with every potential recovery provider at positive HP.");
        game = Cold(game, registry);
        RejectWrongActor(game, lord);
        Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "flip");

        ReachOption(game, Hp, "hp-return");
        var wine = Wine(game);
        var wineId = wine.Id;
        var hpChild = game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
        var declared = Facts<CurrentTurnCardUseKindsRecordedEvent>(game).Single(e => e.OriginFrameId == wineId);
        Require(game.State.Players[0] is { Hp: 1, IsFaceDown: true } &&
                wine.CardId == 0 && wine.PhysicalCardIds is { Count: 0 } &&
                declared.CardActionId is null && declared.HandSuitMask == 0 && declared.ActualTurnActorUseOrdinal == 2 &&
                hpChild.Change is { TargetSeat: 0, Kind: HpChangeKind.Recovery, Amount: 1, HpBefore: 0, HpAfter: 1 } &&
                hpChild.Continuation == PostEventContinuation.CardUse && hpChild.ResumeFrameId == wineId &&
                !Facts<CardUseFinishedEvent>(game).Any(e => e.ResolutionId == wineId) &&
                !Facts<ProgramOptionChosenEvent>(game).Any(e => e.FrameId == rescueId && e.ResultBind == "rescue-tail") &&
                wine.LegacyDyingAlcoholReturn?.ProgramFrameId == rescueId && wine.Action is not null &&
                P(game) is { PlayerSeat: 0, IsPrivate: true } && game.CreateSnapshot(lord).PendingDecision is null &&
                !game.ResolutionStack.OfType<RecoveryReplacementFrame>().Any() &&
                !Facts<RecoveryReplacementChosenEvent>(game).Any(),
            "The exact HP child pauses after one real recovery and before Wine completion or the producer tail, with its declared second ordinal intact.");
        game = Cold(game, registry);
        RejectWrongActor(game, lord);
        Continue(game);

        ReachOption(game, Completed, "completion-return");
        wine = Wine(game);
        var window = game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(f => f.Action.EffectiveKind == CardKind.Alcohol);
        var action = window.Action;
        var normalized = Facts<LegacyActualUseCompletionCapturedEvent>(game).Single(e => e.CardUseFrameId == wineId);
        Require(window.ParentFrameId == wineId && wine.Action?.ActionId == action.ActionId &&
                wine.LegacyDyingAlcoholReturn?.ProgramFrameId == rescueId &&
                normalized.ProgramParentFrameId == rescueId && normalized.Action.ActionId == action.ActionId &&
                action is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 0, EffectiveKind: CardKind.Alcohol,
                    EffectiveSuit: Suit.None, EffectiveRank: 0, PhysicalCards.Count: 0 } &&
                action.TargetSeats.SequenceEqual([0]) && action.ConversionChain is [var source] &&
                source == normalized.ProducerSource && source.SkillId == Rescue && source.OwnerSeat == 0 &&
                window.Candidates is [var completion] && completion.SkillId == Completed &&
                completion.FrozenContext?.Facts is { ActualTurnActorUseOrdinal: 2,
                    CardActionActorIsCurrentTurn: true, CardActionActorIsOwner: true } &&
                Facts<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == wineId && e.CardId == 0 && e.CardKind == CardKind.Alcohol) == 1 &&
                !Facts<ProgramOptionChosenEvent>(game).Any(e => e.FrameId == rescueId && e.ResultBind == "rescue-tail"),
            "Completion binds the immutable zero-entity action to its exact producer and original second declaration, while its typed program return waits.");
        game = Cold(game, registry);
        Continue(game);
        ReachOption(game, Rescue, "rescue-tail");
        Require(!game.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == wineId) &&
                game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == rescueId).InstructionIndex == 4 &&
                game.ResolutionStack.OfType<DyingFrame>().Any(f => f.Id == dyingId),
            "The completed Wine returns once to its exact paused rescue instruction before that rescue's own dying return.");
        game = Cold(game, registry);
        Continue(game);
        ReachOption(game, Driver, "driver-tail");
        game = Cold(game, registry);
        Continue(game);
        ReachPlay(game);
        Require(game.State.TurnNumber == turn && Facts<TurnStartedEvent>(game).Count() == starts &&
                game.State.Players[0] is { IsAlive: true, Hp: 1 } &&
                !game.ResolutionStack.Any(f => f is DyingFrame or RecoveryReplacementFrame or DamageFrame or DamageTriggerWindowFrame) &&
                Facts<CardUseDeclaredEvent>(game).Count(e => e.ResolutionId == wineId) == 1 &&
                Facts<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == wineId) == 1 &&
                Facts<LegacyActualUseCompletionCapturedEvent>(game).Count(e => e.CardUseFrameId == wineId) == 1 &&
                Facts<CardActionAcceptedEvent>(game).Count(e => e.Action.ActionId == action.ActionId) == 1 &&
                Facts<CurrentTurnCardUseKindsRecordedEvent>(game).Count(e => e.OriginFrameId == wineId) == 1 &&
                Facts<ProgramDyingRescueEvent>(game).Count(e => e.DyingFrameId == dyingId && e.SkillId == Rescue &&
                    e.CardId == 0 && e.RecoveredHp == 1 && e.VictimHp == 1) == 1 &&
                Facts<RecoveryAppliedEvent>(game).Count(e => e.TargetSeat == 0) == 1 &&
                Facts<ProgramOptionChosenEvent>(game).Count(e => e.FrameId == rescueId && e.ResultBind == "rescue-tail") == 1 &&
                Facts<ProgramOptionChosenEvent>(game).Count(e => e.FrameId == driverId && e.ResultBind == "driver-tail") == 1 &&
                Facts<DyingResolvedEvent>(game).Count(e => e.ResolutionId == dyingId && e.Survived) == 1 &&
                Facts<DyingResolvedEvent>(game).Count() == 1 &&
                !Facts<PlayerDyingEvent>(game).Any(e => e.VictimSeat != 0) &&
                !Facts<RecoveryReplacementChosenEvent>(game).Any() &&
                !game.CardMovements.Any(m => m.CardId == 0),
            "All cold children return to one recovery, one Wine/action/ordinal, one rescue tail and one driver tail, without a manufactured physical entity or repeated payment.");
        _ = Cold(game, registry);
    }

    private static CardUseFrame Wine(GameEngine game) => game.ResolutionStack.OfType<CardUseFrame>()
        .Single(f => f.CardId == 0 && f.CardKind == CardKind.Alcohol);
    private static IEnumerable<T> Facts<T>(GameEngine game) => game.Events.Select(e => e.Payload).OfType<T>();
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4)
        .Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
    private static void ReachOption(GameEngine game, string skill, string binding) => Reach(game, () =>
        P(game)?.SkillPrompt?.SkillId == skill && P(game)!.Choices.Any(c => c.Parameters.GetValueOrDefault("result-bind") == binding));
    private static void ReachPlay(GameEngine game) => Reach(game, () => P(game) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine game, Func<bool> reached)
    {
        for (var step = 0; step < 180; step++)
        {
            if (reached()) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The small legacy-Wine fixture did not reach its exact boundary: " + JsonSerializer.Serialize(P(game)));
    }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> selected)
    {
        var prompt = P(game) ?? throw new InvalidOperationException("The exact legacy-Wine choice is absent.");
        Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(selected).Id, game.Revision));
    }
    private static void Continue(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void RejectWrongActor(GameEngine game, int seat)
    {
        var prompt = P(game)!;
        var before = State(game);
        Require(prompt.PlayerSeat != seat && !game.Submit(new AnswerPromptCommand(seat, prompt.PromptId,
                prompt.Choices.First().Id, game.Revision)).Accepted && State(game) == before,
            "A foreign actor cannot answer the exact owner's dying, recovery or HP child, including after cold restoration.");
    }
    private static void Use(GameEngine game, string activation, IReadOnlyList<int> targets) => Accept(game,
        new UseProgramSkillCommand(0, Driver, activation, [], targets, game.Revision, P(game)!.PromptId));
    private static void PlayFirstRealUse(GameEngine game)
    {
        var card = game.GetHumanLegalActions().First(a => a.PlayedCardKind == CardKind.DrawTwo && a.ConversionSource?.SkillId == Driver);
        Accept(game, new PlayCardCommand(0, card.CardId!.Value, card.TargetSeats, game.Revision,
            P(game)!.PromptId, card.PlayedCardKind, card.TargetCardId)
        { ConversionSource = card.ConversionSource, AdditionalConversionSources = card.AdditionalConversionSources });
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "Rejected actual legacy-Wine command.");
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack), game.CardMovements,
        Events = game.Events.Select(e => $"{e.Sequence}|{JsonSerializer.Serialize(e.Payload, e.Payload.GetType())}").ToArray(),
        Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics()
    });
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(restored) == State(game), "All private views, exact suspended parents, ordinal facts, movements and accepted commands cold-restore identically.");
        return restored;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Start(bool recoveryPolicy)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(recoveryPolicy));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Renegade, ModeId = Mode,
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false, MaxTurns = 8
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, () => P(game) is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, P(game)!.PromptId));
        ReachPlay(game);
        return (game, registry);
    }

    private sealed class Fixture(bool recoveryPolicy) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("legacy-actual-alcohol-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var peerMembers = recoveryPolicy ? """
                "cardPolicies":[{"id":"real-lord-replacement","kind":"redirectOwnTurnFactionRecovery","factionId":"wu","ownerRole":"lord","value":1,"providerDrawCount":1}]
                """ : "\"modifiers\":[{\"id\":\"quiet\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":1,\"priority\":0}]";
            var catalog = SkillProgramCatalog.Load($$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
                  {"id":"{{{Driver}}}","revision":1,"viewAs":[{"id":"first-real-use","inputKinds":["crossbow"],"inputSuits":[],"outputKind":"drawTwo","forPlay":true,"forResponse":false,"singleCardTrickUse":true}],
                    "activations":[
                      {"id":"hurt-self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1},{"op":"chooseOption","target":"owner","resultBind":"driver-tail","options":[{"id":"continue"}]}]}]},
                  {"id":"{{{Rescue}}}","revision":1,"triggers":[{"id":"virtual-wine","window":"selfDyingResponse","subject":"owner","optional":false,"effects":[
                    {"op":"chooseOption","target":"owner","resultBind":"flip-choice","options":[{"id":"flip","condition":{"kind":"not","children":[{"kind":"faceDown"}]}}]},
                    {"op":"turnOver","target":"owner"},{"op":"useVirtualDyingAlcohol","target":"owner"},
                    {"op":"chooseOption","target":"owner","resultBind":"rescue-tail","options":[{"id":"continue"}]}]}]},
                  {"id":"{{{Hp}}}","revision":1,"triggers":[{"id":"after-real-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-return","options":[{"id":"continue"}]}]}]},
                  {"id":"{{{Completed}}}","revision":1,"triggers":[{"id":"exact-second-wine","window":"cardUseCompleted","cardKinds":["alcohol"],"ownerRelation":"observer","singleActionInstance":true,"optional":false,
                    "condition":{"kind":"all","children":[{"kind":"cardActionActorIsCurrentTurn"},{"kind":"cardActionActorIsOwner"},{"kind":"cardActionActualTurnUseOrdinalIs","value":2}]},
                    "effects":[{"op":"chooseOption","target":"owner","resultBind":"completion-return","options":[{"id":"continue"}]}]}]},
                  {"id":"{{{Peer}}}","revision":1,{{{peerMembers}}}}]}
                """, JsonSerializer.Serialize(new
                {
                    schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new[] { Driver, Rescue, Hp, Completed, Peer }.ToDictionary(id => id, id => new
                    {
                        name = id, description = "真实虚拟酒完成及回复子窗",
                        optionLabels = id == Rescue ? new Dictionary<string, string> { ["flip"] = "翻面", ["continue"] = "继续" }
                            : id == Peer ? new Dictionary<string, string>()
                            : new Dictionary<string, string> { ["continue"] = "继续" }
                    })
                }));
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, id, "真实虚拟酒完成及回复子窗")
                {
                    Program = program, ProgramPresentation = catalog.Presentations[id],
                    SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => id == Peer ? 1000d : -100d)
                });
            builder.AddGeneral(new(Owner, "虚拟酒当事人", "supporter", Driver, "wu", 4, [Rescue, Hp, Completed]) { InitialHp = 1 });
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:legacy-alcohol-peer-{i}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "真实回复候选", "supporter", Peer, "wu", 4) { InitialHp = 1 });
            builder.AddDeck(new("fixture:legacy-alcohol-deck", "同质实体牌", 4, 2, [new("standard:crossbow", 40)]));
            builder.AddMode(new(Mode, "实际回合虚拟酒检查", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:legacy-alcohol-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
