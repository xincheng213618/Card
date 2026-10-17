using System.IO;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class WenheRoundRuleChecks
{
    private const string Mode = "fixture:wenhe-rules", Owner = "fixture:wenhe-rules-owner",
        Driver = "fixture:wenhe-round-driver", Quiet = "fixture:wenhe-round-quiet",
        DrawWatch = "fixture:wenhe-round-draw", MoveWatch = "fixture:wenhe-round-replacement";

    public static void FamineIssuesOnceWhenItsHostActuallyDiesAtOldRoundEnd()
    {
        var (g, r) = Start(5);
        PlayInRound(g, 2, WenheRuleKind.Famine);
        SpendHumanNirvana(g);
        Need(V(g, 0) is { Hp: 3, IsAlive: true } && Nirvanas(g, 0) == 1,
            "The actual five-tier host must first consume its real limited dying rescue, without stripping the mode grant.");
        Use(g, "self-two"); Play(g);
        Need(V(g, 0).Hp == 1, "Real HP-loss commands must prepare a one-health famine host.");
        var others = g.CreateSnapshot(0).Players.Skip(1).ToDictionary(p => p.Seat, p => p.Hp);
        g = Cold(g, r);
        End(g);
        Drive(g, () => E<RoundEndedEvent>(g).Any(e => e.RoundNumber == 2));
        var issue = E<WenheRuleFlowIssuedEvent>(g).Where(e => e.Operation == SkillProgramEffectOp.ResolveWenheRoundEnd && e.RoundNumber == 2).ToArray();
        Need(issue is [{ OwnerSeat: 0, Amount: 2, Count: 8 }] && !V(g, 0).IsAlive && V(g, 0).SurvivalRank == 8 &&
            E<WenheRuleParticipantEvent>(g).Where(e => e.ProgramFrameId == issue[0].ProgramFrameId).Select(e => e.Seat).SequenceEqual(Enumerable.Range(0, 8)) &&
            others.All(pair => V(g, pair.Key).Hp == pair.Value - 2) && g.CreateSnapshot(0).Winner == Winner.None,
            "Famine must freeze the true minimum hand after real hand-limit discard and issue once; the dead minimum-seat host cannot cause a second full famine under its successor.");
        Cold(g, r);
    }

    public static void EquipmentReplacementCompletesOneParticipantChildBeforeTheNextTie()
    {
        var (g, r) = Start(2, armorPile: true);
        Drive(g, () => P(g)?.SkillPrompt?.SkillId == MoveWatch, endPlay: true);
        Need(g.CreateSnapshot(0).WenheRule is { RoundNumber: 2, Rule: WenheRuleKind.Equipment },
            "The fixed two-round physical armor fixture must reach the real equipment rule without seed searching in a test.");
        var frame = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.WenheRuleFlow is not null);
        var flow = frame.WenheRuleFlow!;
        var oldArmor = g.CardMovements.Last(m => m.From == CardLocation.Equipment(0) && m.Reason == CardMoveReasons.EquipmentReplace).CardId;
        Need(flow is { Operation: SkillProgramEffectOp.ResolveWenheTurnEnd, Stage: 2, Index: 0 } &&
            flow.Seats.SequenceEqual(Enumerable.Range(0, 8)) &&
            E<ProgramSkillHpLostEvent>(g).Count(e => e.FrameId == frame.Id) == 1 &&
            E<ProgramSkillHpLostEvent>(g).Single(e => e.FrameId == frame.Id).TargetSeat == 0 &&
            E<SilverLionRemovedRecoveryEvent>(g).Any(e => e.PlayerSeat == 0 && e.Reason == CardMoveReasons.EquipmentReplace) &&
            V(g, 0).Equipment is [{ Kind: CardKind.SilverLion } armor] && armor.Id != oldArmor &&
            g.CreateCardZoneDiagnostics().Single(c => c.CardId == oldArmor).Location.Zone == CardZoneKind.DiscardPile,
            "A real armor replacement must pause in the first tied participant's movement child after its native Silver Lion recovery, before charging the next participant.");
        var frameId = frame.Id;
        var actualTurn = flow.TurnNumber;
        g = Cold(g, r);
        Play(g);
        Need(E<ProgramSkillHpLostEvent>(g).Where(e => e.FrameId == frameId).Select(e => e.TargetSeat).SequenceEqual(Enumerable.Range(0, 8)) &&
            E<WenheRuleFlowIssuedEvent>(g).Count(e => e.Operation == SkillProgramEffectOp.ResolveWenheTurnEnd && e.TurnNumber == actualTurn) == 1 &&
            g.CreateSnapshot(0).Players.All(p => p.Equipment is [{ Kind: CardKind.SilverLion }]),
            "Resuming the exact replacement child must finish the original frozen tie list once and retain the actual new equipment entities.");
        Cold(g, r);
    }

    public static void DeadActualTurnOwnerStillRunsOnlyTheModeEquipmentEnding()
    {
        var (g, r) = Start(2);
        PlayInRound(g, 2, WenheRuleKind.Equipment);
        SpendHumanNirvana(g);
        Need(Nirvanas(g, 0) == 1 && V(g, 0).Hp == 3, "The real human must consume the one mode Nirvana before the actual death test.");
        var turn = g.CreateSnapshot(0).TurnNumber;
        var before = g.Events.Count;
        var aliveHp = g.CreateSnapshot(0).Players.Skip(1).ToDictionary(p => p.Seat, p => p.Hp);
        Use(g, "self-twenty");
        Drive(g, () => E<TurnEndedEvent>(g).Any(e => e.TurnNumber == turn && e.ActorSeat == 0));
        var issued = E<WenheRuleFlowIssuedEvent>(g).Where(e => e.Operation == SkillProgramEffectOp.ResolveWenheTurnEnd && e.TurnNumber == turn).ToArray();
        Need(!V(g, 0).IsAlive && issued is [{ OwnerSeat: 1, Count: 7 }] &&
            E<WenheRuleParticipantEvent>(g).Where(e => e.ProgramFrameId == issued[0].ProgramFrameId).Select(e => e.Seat).SequenceEqual(Enumerable.Range(1, 7)) &&
            aliveHp.All(pair => V(g, pair.Key).Hp == pair.Value - 1) &&
            !g.Events.Skip(before).Any(e => e.Payload is ProgramBindingStartedEvent { SkillId: Driver, BindingId: "ordinary-ending" }),
            "A genuinely dead actual turn owner must retain one mode-only equipment ending under a living host, without opening its ordinary ending-stage skill.");
        Cold(g, r);
    }

    public static void BountyKillUsesDoubleRealRewardWithoutHealing()
    {
        var (g, r) = Start(0);
        PlayInRound(g, 2, WenheRuleKind.Bounty);
        Use(g, "weaken-two", 1); Play(g);
        Use(g, "defeat", 1); Play(g);
        Need(V(g, 1).IsAlive && Nirvanas(g, 1) == 1, "The first actual lethal hit must consume the opponent's mode rescue rather than award a kill.");
        var before = V(g, 0);
        g = Cold(g, r);
        Use(g, "defeat", 1); Play(g);
        var reward = E<WenheRuleFlowIssuedEvent>(g).Single(e => e.Operation == SkillProgramEffectOp.RewardWenheKiller);
        Need(!V(g, 1).IsAlive && V(g, 0).MaxHp == before.MaxHp + 2 && V(g, 0).Hp == before.Hp &&
            V(g, 0).HandCount == before.HandCount + 6 && reward is { Amount: 2, Count: 1 } &&
            E<NativeDrawAttemptStartedEvent>(g).Any(e => e.Context.ParentFrameId == reward.ProgramFrameId && e.Context.RequestedCount == 6),
            "The fixed real Bounty round must grant exactly two maximum health and six native cards, never healing the wounded killer.");
        Cold(g, r);
    }

    public static void LastStandRescueDrawAndExtraActualTurnArePaidOnceEach()
    {
        var (g, r) = Start(15);
        Play(g);
        Use(g, "leave-one"); Play(g);
        Need(V(g, 0).Hp == 1 && Nirvanas(g, 0) == 0, "A real loss command must leave the human at one HP without pre-consuming Nirvana.");
        End(g);
        PlayInRound(g, 2, WenheRuleKind.LastStand);
        Need(V(g, 0).Hp == 3 && Nirvanas(g, 0) == 1, "The actual LastStand loss must enter dying and finish the mode rescue before its following draw.");
        var first = E<WenheRuleFlowIssuedEvent>(g).Single(e => e.Operation == SkillProgramEffectOp.ResolveWenheTurnStart);
        Need(E<ProgramSkillHpLostEvent>(g).Count(e => e.FrameId == first.ProgramFrameId && e.TargetSeat == 0 && e.Amount == 1) == 1 &&
            E<NativeDrawAttemptStartedEvent>(g).Any(e => e.Context.ParentFrameId == first.ProgramFrameId && e.Context.RequestedCount == 3),
            "The rescued actual turn must pay one HP loss and then one true native three-card draw without re-entering the paid stage.");
        g = Cold(g, r);
        Use(g, "extra"); Play(g); End(g);
        Drive(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && g.CreateSnapshot(0).TurnNumber > first.TurnNumber);
        var starts = E<WenheRuleFlowIssuedEvent>(g).Where(e => e.Operation == SkillProgramEffectOp.ResolveWenheTurnStart).ToArray();
        Need(starts.Length == 2 && starts.All(e => e.RoundNumber == 2 && e.OwnerSeat == 0) &&
            starts[1].TurnNumber == starts[0].TurnNumber + 1 && V(g, 0).Hp == 2 && Nirvanas(g, 0) == 1 &&
            E<RoundStartedEvent>(g).Select(e => e.RoundNumber).SequenceEqual([1, 2]),
            "A real pending extra turn must apply LastStand again under a new actual turn, without drawing a new round rule or resetting the one-game rescue.");
        Cold(g, r);
    }

    public static void FamineAllDeadCompletesAsDrawAfterItsFrozenDeathChildren()
    {
        var (g, r) = Start(5);
        Play(g);
        Need(g.CreateSnapshot(0).WenheRule is { RoundNumber: 1, Rule: WenheRuleKind.Chaos },
            "The bounded all-dead fixture must prepare its real survivors during the fixed first round.");
        SpendHumanNirvana(g);
        Use(g, "self-two"); Play(g);
        for (var seat = 1; seat < 8; seat++)
        {
            Use(g, "weaken-two", seat); Play(g);
            Use(g, "defeat", seat); Play(g);
            Use(g, "weaken-two", seat); Play(g);
        }
        Need(g.CreateSnapshot(0).Players.All(p => p.IsAlive && p.Hp == 1 && Nirvanas(g, p.Seat) == 1) &&
            E<WenhePlayerRankedEvent>(g).Length == 0,
            "Every real five-tier survivor must spend its native one-game rescue and remain alive at one health before famine; none may be deleted or pre-ranked.");
        End(g);
        PlayInRound(g, 2, WenheRuleKind.Famine);
        g = Cold(g, r);
        End(g);
        Drive(g, () => !V(g, 0).IsAlive);
        var issue = E<WenheRuleFlowIssuedEvent>(g).Single(e =>
            e.Operation == SkillProgramEffectOp.ResolveWenheRoundEnd && e.RoundNumber == 2);
        Need(issue is { OwnerSeat: 0, Amount: 2, Count: 8 } && g.CreateSnapshot(0).Winner == Winner.None &&
            g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == issue.ProgramFrameId && f.WenheRuleFlow is not null),
            "The first actual famine death must retain the original frozen eight-player rule and await its remaining real death children without declaring a winner.");
        g = Cold(g, r);
        Drive(g, () => g.CreateSnapshot(0).Winner != Winner.None);
        var snapshot = g.CreateSnapshot(0);
        var facts = g.Events.Select(e => e.Payload).ToArray();
        var winnerIndex = Array.FindIndex(facts, e => e is WinnerDeterminedEvent);
        var lastDeath = Array.FindLastIndex(facts, e => e is PlayerDiedEvent);
        Need(snapshot is { Winner: Winner.Draw, WinnerSeat: null } && snapshot.Players.All(p => !p.IsAlive) &&
            snapshot.Players.Select(p => p.SurvivalRank).Order().SequenceEqual(Enumerable.Range(1, 8).Select(rank => (int?)rank)) &&
            E<WenheRuleParticipantEvent>(g).Where(e => e.ProgramFrameId == issue.ProgramFrameId)
                .Select(e => e.Seat).SequenceEqual(Enumerable.Range(0, 8)) &&
            E<WenheRuleFlowIssuedEvent>(g).Count(e => e.Operation == SkillProgramEffectOp.ResolveWenheRoundEnd && e.RoundNumber == 2) == 1 &&
            winnerIndex > lastDeath && !facts.Skip(winnerIndex + 1).Any(e => e is TurnStartedEvent) &&
            g.ResolutionStack.Count == 0,
            "One genuine all-player famine must finish all eight ranked native deaths and its rule subtree, then report Draw without entering another dead player's turn.");
        Cold(g, r);
    }

    public static void SweepAddsAtTheSourceOnceWhileChainAndHpLossStayExact()
    {
        var (g, r) = Start(27);
        PlayInRound(g, 2, WenheRuleKind.Sweep);
        Use(g, "chain", 1); Play(g);
        Use(g, "chain", 2); Play(g);
        Need(V(g, 1).IsChained && V(g, 2).IsChained,
            "Two actual state operations must prepare real chained opponents before the native elemental damage.");
        var health = g.CreateSnapshot(0).Players.ToDictionary(p => p.Seat, p => p.Hp);
        var before = g.Events.Count;
        g = Cold(g, r);
        Use(g, "fire", 1);
        g = Cold(g, r);
        Play(g);
        var applied = g.Events.Skip(before).Select(e => e.Payload).OfType<DamageAppliedEvent>().ToArray();
        Need(applied.Length == 2 && applied.Select(e => e.TargetSeat).SequenceEqual([1, 2]) &&
            applied.All(e => e.SourceSeat == 0 && e.Amount == 2 && e.Nature == DamageNature.Fire) &&
            E<ChainedDamagePropagatedEvent>(g) is [{ SourceSeat: 0, FromSeat: 1, TargetSeat: 2, Amount: 2, Nature: DamageNature.Fire }] &&
            V(g, 1).Hp == health[1] - 2 && V(g, 2).Hp == health[2] - 2 &&
            !V(g, 1).IsChained && !V(g, 2).IsChained &&
            g.CreateSnapshot(0).Players.Where(p => p.Seat is not (1 or 2)).All(p => p.Hp == health[p.Seat]),
            "A native one-fire source must become two once; its real chained copy stays two rather than being increased again, and unrelated seats take no damage.");
        var damageCount = E<DamageAppliedEvent>(g).Length;
        before = g.Events.Count;
        Use(g, "lose-one-other", 3); Play(g);
        Need(V(g, 3).Hp == health[3] - 1 && E<DamageAppliedEvent>(g).Length == damageCount &&
            g.Events.Skip(before).Select(e => e.Payload).OfType<ProgramSkillHpLostEvent>().ToArray() is [{ TargetSeat: 3, Amount: 1 }],
            "The same Sweep round must leave an actual one-point HP loss at one and never convert it into amplified damage.");
        Cold(g, r);
    }

    private static (GameEngine Game, ContentRegistry Registry) Start(int seed, bool armorPile = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(armorPile));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = seed, PlayerCount = 8, HumanSeat = 0,
            HumanRole = null, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 40 }, registry);
        Accept(g, new StartGameCommand());
        Drive(g, () => P(g) is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new RequestGeneralRosterCommand(0, true, g.Revision, P(g)!.PromptId));
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId));
        return (g, registry);
    }
    private static void PlayInRound(GameEngine g, int round, WenheRuleKind rule)
    {
        Drive(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && g.CreateSnapshot(0).WenheRule?.RoundNumber == round, endPlay: true);
        Need(g.CreateSnapshot(0).WenheRule?.Rule == rule, "The fixed development-profile seed must select its exact second-round rule.");
    }
    private static void Play(GameEngine g) => Drive(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, int? target = null) => Accept(g,
        new UseProgramSkillCommand(0, Driver, id, [], target is { } seat ? [seat] : [], g.Revision, P(g)!.PromptId));
    private static void SpendHumanNirvana(GameEngine g)
    {
        for (var hit = 0; hit < 2 && Nirvanas(g, 0) == 0; hit++) { Use(g, "self-twenty"); Play(g); }
        Need(Nirvanas(g, 0) == 1 && V(g, 0) is { IsAlive: true, Hp: 3 },
            "Single real losses must stop immediately after the first successful native Nirvana, whatever earlier activity losses changed the human's health.");
    }
    private static int Nirvanas(GameEngine g, int seat) => E<ProgramBindingResolvedEvent>(g)
        .Count(e => e.OwnerSeat == seat && e.SkillId == "wenhe:niepan" && e.Activated && e.Completed);
    private static PendingDecision? P(GameEngine g) => g.PendingDecision;
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(0).Players[seat];
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Wenhe round command rejected."); }
    private static void Drive(GameEngine g, Func<bool> done, bool endPlay = false)
    {
        for (var step = 0; step < 500; step++)
        {
            if (done()) return;
            var p = P(g);
            if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && endPlay) End(g);
            else if (p is { PlayerSeat: 0 } && p.Kind != DecisionKind.PlayCard && p.Kind != DecisionKind.SelectGeneral)
            {
                var choice = p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("skill-id") == "wenhe:niepan") ??
                    p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die") ??
                    p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "skip") ?? p.Choices.SingleOrDefault();
                Need(choice is not null, "The bounded round fixture requires an explicit safe native response.");
                Accept(g, new AnswerPromptCommand(0, p.PromptId, choice!.Id, g.Revision));
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Wenhe round fixture missed its bounded native child: " + SnapshotJson.Serialize(g.CreateSnapshot(0)));
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(Enumerable.Range(0, 8).All(seat => SnapshotJson.Serialize(g.CreateSnapshot(seat)) == SnapshotJson.Serialize(copy.CreateSnapshot(seat))) &&
            JsonSerializer.Serialize(g.CardMovements) == JsonSerializer.Serialize(copy.CardMovements) &&
            JsonSerializer.Serialize(g.ResolutionStack) == JsonSerializer.Serialize(copy.ResolutionStack) &&
            g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType()))
                .SequenceEqual(copy.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType()))),
            "The exact old-round rule, typed events, native children and materials must cold-replay.");
        return copy;
    }

    private sealed class Fixture(bool armorPile) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("wenhe-round-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var a = typeof(StandardContentPackage).Assembly;
            using var rules = new StreamReader(a.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.wenhe-activity.rules.json")!);
            using var presentation = new StreamReader(a.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.wenhe-activity.presentation.json")!);
            var activity = SkillProgramCatalog.Load(rules.ReadToEnd(), presentation.ReadToEnd());
            foreach (var (id, program) in activity.Programs) b.AddSkill(new(id, activity.Presentations[id].Name, activity.Presentations[id].Description)
            { Program = program, ProgramPresentation = activity.Presentations[id], Tags = id == "wenhe:niepan" ? SkillTag.Limited : id == "wenhe:rules" ? SkillTag.Locked : SkillTag.None });
            var fixtureRules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
             {"id":"{{{Driver}}}","revision":1,"activations":[
              {"id":"self-twenty","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
               "effects":[{"op":"loseHp","target":"owner","amount":20}]},
              {"id":"leave-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
               "effects":[{"op":"loseHp","target":"owner","amount":20}]},
              {"id":"self-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
               "effects":[{"op":"loseHp","target":"owner","amount":2}]},
              {"id":"weaken-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
               "effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]},
              {"id":"lose-one-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
               "effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
              {"id":"defeat","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
               "effects":[{"op":"damage","target":"selectedTarget","amount":20}]},
              {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
               "effects":[{"op":"pendExtraTurn","target":"owner"}]},
              {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
               "effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
              {"id":"fire","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
               "effects":[{"op":"damage","target":"selectedTarget","amount":1,"nature":"fire"}]}],
             "triggers":[{"id":"ordinary-ending","window":"turnEnding","subject":"owner","priority":10,"optional":false,
               "effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]},
                 {"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
             {"id":"{{{Quiet}}}","revision":1,"triggers":[
              {"id":"skip-ai-play","window":"afterNormalDraw","subject":"owner","optional":false,
               "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]},
              {"id":"clear-fixture-hand","window":"turnEnding","subject":"owner","priority":10,"optional":false,
               "effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]}]},
             {"id":"{{{DrawWatch}}}","revision":1,"triggers":[{"id":"native-request","window":"nativeDrawStarting","subject":"owner","optional":true,
               "effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
             {"id":"{{{MoveWatch}}}","revision":1,"triggers":[{"id":"armor-replaced","window":"cardsMoved","subject":"owner","optional":false,
               "sourceZones":["equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["{{{CardMoveReasons.EquipmentReplace.Value}}}"],
               "effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var ids = new[] { Driver, Quiet, DrawWatch, MoveWatch };
            var catalog = SkillProgramCatalog.Load(fixtureRules,
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = ids.ToDictionary(id => id, id => new { name = id, description = "Native bounded Wenhe round boundary fixture.",
                        optionLabels = id == Quiet ? new Dictionary<string,string>() : new Dictionary<string,string> { ["continue"] = "Continue exact child" } }) }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
            { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var generals = new[] { Owner }.Concat(Enumerable.Range(1, 7).Select(i => $"fixture:wenhe-rules-{i}")).ToArray();
            foreach (var id in generals) b.AddGeneral(new(id, id, "", id == Owner ? Driver : Quiet, "wei", 20,
                id == Owner ? [DrawWatch, MoveWatch] : [DrawWatch]));
            b.AddCard(new("fixture:wenhe-round-lion", "白银狮子", "装备", "Actual native replacement fixture.", CardKind.SilverLion));
            b.AddDeck(new("fixture:wenhe-round-deck", "Fixed homogeneous physical round fixture", 4, 2,
                [new(armorPile ? "fixture:wenhe-round-lion" : "standard:dodge", 200)]));
            b.AddMode(new(Mode, "Wenhe round fixture", 8, 8, new Dictionary<string,int>(), "fixture:wenhe-round-deck", 5,
                generals, ContentModeKind.Survival) { WenheActivity = new() });
        }
    }
}
