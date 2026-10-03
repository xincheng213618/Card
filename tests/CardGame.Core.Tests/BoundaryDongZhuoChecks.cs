using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryDongZhuoChecks
{
    private const string Jiuchi = "boundary:jiuchi-current";
    private const string Benghuai = "boundary:benghuai-current";
    private const string Baonue = "boundary:baonue-current";
    private const string Driver = "fixture:dz-driver";
    private const string Damage = "fixture:dz-damage";
    private const string Hp = "fixture:dz-hp";
    private const string Gain = "fixture:dz-gain";
    private const string Mode = "identity:classic-boundary-dong-zhuo-fixture";
    private const string JudgmentBinding = "other-qun-damage-point-judgment";
    private const string JudgmentReason = "skill.boundary.baonue-current";

    public static void UnlimitedAlcoholActualConsumptionAndTurnExpiry()
    {
        var (game, registry) = Create(); Use(game, "draw"); ReachPlay(game); Use(game, "slash-budget"); ReachPlay(game);
        Require(game.CreateSnapshot(0).Players[0].Hand.Count == 10 && game.State.Players[0].Hp == 9,
            "The fixed eighteen-card deck gives the real Lord one initial card plus nine real draws and the formal eight-plus-one HP.");
        var physicalWine = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Alcohol && a.ConversionSource is null);
        Play(game, physicalWine); ReachPlay(game);
        Require(game.CreateSnapshot(0).Players[0].HasAlcoholEffect && !game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Alcohol),
            "Unlimited Alcohol keeps the existing gate against stacking an unconsumed wine effect.");
        SlashAndPause(game, registry, true, expectedDamage: 2); Continue(game); ReachPlay(game);
        Require(Suppressions(game).Length == 1 && game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Alcohol && a.ConversionSource is null),
            "The consumed first physical wine permits a second real Alcohol use in the same play phase.");
        var convertedWine = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Alcohol && a.ConversionSource?.SkillId == Jiuchi &&
            game.CreateSnapshot(0).Players[0].Hand.Single(c => c.Id == a.CardId).Kind == CardKind.Slash);
        Play(game, convertedWine); ReachPlay(game); SlashAndPause(game, registry, true, expectedDamage: 2); Continue(game); ReachPlay(game);
        var issued = Suppressions(game);
        Require(issued.Length == 2 && issued.All(s => s.Source.OwnerSeat == 0 && s.Source.SkillId == Jiuchi &&
                s.Source.BindingId == "alcohol-slash-disables-own-decay" && !string.IsNullOrWhiteSpace(s.Source.SkillInstanceId) &&
                s.SuppressedSkillId == Benghuai && s.TurnNumber == game.State.TurnNumber && s.TurnOwnerSeat == 0) &&
            issued[0].Source == issued[1].Source && issued.Select(s => s.CardActionId).Distinct().Count() == 2 &&
            game.Events.Count(e => e.Payload is CardUseAlcoholConsumptionCapturedEvent) == 2,
            "Two separately paid wine Slashes freeze the same issuing instance, distinct original actions and one actual-turn expiry.");
        foreach (var wineId in new[] { physicalWine.CardId!.Value, convertedWine.CardId!.Value })
            Require(game.CardMovements.Count(m => m.CardId == wineId && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                game.CardMovements.Count(m => m.CardId == wineId && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
                "Cold-resumed wine and conversion payments each leave Hand and Processing exactly once.");
        Replay(game, registry); EndTurn(game);
        Require(!game.Events.Any(e => e.Payload is ProgramBindingStartedEvent { SkillId: Benghuai, OwnerSeat: 0 }) &&
            game.State.Players[0].Hp == 9 && game.Events.Count(e => e.Payload is CurrentTurnOwnSkillSuppressionsExpiredEvent) == 1,
            "The named locked decay is inactive at this actual ending and its issued facts expire once after that turn.");
        Replay(game, registry); ReachPlay(game);
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
        Reach(game, p => p.SkillPrompt?.SkillId == Benghuai && Option(p, "lose-hp")); Replay(game, registry); Reject(game);
        Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "lose-hp");
        for (var step = 0; step < 100 && game.Events.Count(e => e.Payload is TurnEndedEvent { ActorSeat: 0 }) < 2; step++) Advance(game);
        Require(game.State.Players[0].Hp == 8 && game.Events.Count(e => e.Payload is ProgramBindingStartedEvent { SkillId: Benghuai, OwnerSeat: 0 }) == 1,
            "The next actual turn restores the locked skill, whose real choice pays one HP once.");
        Require(game.Events.Count(e => e.Payload is TurnEndedEvent { ActorSeat: 0 }) == 2,
            "The exact second owner turn ends once without requiring an unrelated later decision prompt.");
        Replay(game, registry);

        var (suppressed, suppressedRegistry) = Create(); Use(suppressed, "draw"); ReachPlay(suppressed);
        Play(suppressed, suppressed.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Alcohol && a.ConversionSource is null)); ReachPlay(suppressed);
        SlashAndPause(suppressed, suppressedRegistry, true, 2); Continue(suppressed); ReachPlay(suppressed);
        Use(suppressed, "suppress"); ReachPlay(suppressed);
        Require(suppressed.State.Players[0].Hp == 1 && !suppressed.GetHumanLegalActions().Any(a => a.ConversionSource?.SkillId == Jiuchi),
            "Real HP loss disables the issuing program before the already-issued turn fact expires.");
        Replay(suppressed, suppressedRegistry); EndTurn(suppressed);
        Require(suppressed.Events.Count(e => e.Payload is CurrentTurnOwnSkillSuppressionsExpiredEvent) == 1,
            "Losing the issuing source does not erase or repeat the issued fact's actual-turn expiry.");
        Replay(suppressed, suppressedRegistry);
    }

    public static void NonWineDamageBonusAndDodgedVirtualSlashDoNotSuppress()
    {
        var (game, registry) = Create(); Use(game, "draw"); ReachPlay(game); Use(game, "damage-bonus"); ReachPlay(game);
        SlashAndPause(game, registry, false, 2); Continue(game); ReachPlay(game);
        Require(Suppressions(game).Length == 0 && !game.Events.Any(e => e.Payload is CardUseAlcoholConsumptionCapturedEvent),
            "An actual two-damage Slash supplied by a different modifier is not evidence of Alcohol consumption.");
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
        Reach(game, p => p.SkillPrompt?.SkillId == Benghuai && Option(p, "reduce-max-hp")); Replay(game, registry);
        Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "reduce-max-hp");
        Require(game.State.Players[0].MaxHp == 8, "The unconsumed decay remains usable and its other actual choice reduces MaxHP."); Replay(game, registry);

        var (dodged, dodgedRegistry) = Create(dodgeDeck: true);
        var wine = dodged.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Alcohol && a.ConversionSource?.SkillId == Jiuchi);
        Play(dodged, wine); ReachPlay(dodged); Use(dodged, "virtual");
        Reach(dodged, p => Action(p, "virtual-basic")); Replay(dodged, dodgedRegistry);
        Answer(dodged, c => c.Parameters.GetValueOrDefault("basic-option") == "Slash:1"); ReachPlay(dodged);
        Require(dodged.Events.Count(e => e.Payload is CardUseAlcoholConsumptionCapturedEvent) == 1 &&
            !dodged.Events.Any(e => e.Payload is DamageAppliedEvent { SourceSeat: 0, TargetSeat: 1 }) &&
            dodged.Events.Any(e => e.Payload is CardRespondedEvent { ResponderSeat: 1, EffectiveCardKind: CardKind.Dodge }) &&
            Suppressions(dodged).Length == 0 && !dodged.CreateSnapshot(0).Players[0].HasAlcoholEffect &&
            dodged.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Alcohol && a.ConversionSource?.SkillId == Jiuchi),
            "A native AI's actual Dodge cancels the zero-entity wine Slash: the original wine was consumed, another Alcohol is legal, and no damage suppression is issued.");
        Replay(dodged, dodgedRegistry);
    }

    public static void PerPointOwnedJudgmentRecoveryClaimChildrenAndNativeAi()
    {
        var (game, registry) = Create(baonue: true);
        Require(game.State.Players[0].Hp == 5, "The injured actual Lord starts below its formal nine-HP maximum.");
        Use(game, "other-hit", [1]);
        for (var point = 0; point < 2; point++)
        {
            Reach(game, p => Activation(p, Baonue, JudgmentBinding)); Replay(game, registry); Reject(game);
            Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(game, p => p.SkillPrompt?.SkillId == Hp && p.PlayerSeat == 0);
            AssertOwnedJudgment(game, registry, 0, point, expectedDamageSource: 1, expectedDamageTarget: 0);
            var judgment = game.ResolutionStack.OfType<JudgmentFrame>().Single(); var cardId = judgment.CardId!.Value;
            Require(game.State.Players[0].Hp == 4 + point && game.CreateSnapshot(0).Players[0].Hand.All(c => c.Id != cardId),
                "Each Spade judgment first recovers one HP and pauses its exact producer before the card claim.");
            Replay(game, registry); Continue(game);
            Reach(game, p => p.SkillPrompt?.SkillId == Gain && p.PlayerSeat == 0);
            AssertOwnedJudgment(game, registry, 0, point, 1, 0);
            Require(game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == cardId) && ClaimMoves(game, cardId, 0) == 1 &&
                game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(w => w.Batch.Movements.Any(m =>
                    m.CardId == cardId && m.From == CardLocation.Judgment(0) && m.To == CardLocation.Hand(0))),
                "The exact real judgment entity is claimed once before its gain observer returns.");
            Replay(game, registry); Reject(game); Continue(game);
        }
        ReachPlay(game);
        Require(game.State.Players[0].Hp == 5 && game.Events.Count(e => e.Payload is ProgramJudgmentCardClaimedEvent { SkillId: Baonue, OwnerSeat: 0 }) == 2 &&
            game.Events.Count(e => e.Payload is DamageAppliedEvent { SourceSeat: 1, TargetSeat: 0, Amount: 2 }) == 1 &&
            game.Events.Select(e => e.Payload).OfType<JudgmentRequestedEvent>().Where(e => e.Reason == JudgmentReason).All(e => e.TargetSeat == 0),
            "One actual two-point damage gives the current Lord two independent owner judgments and two recover-and-claim results.");
        var claimed = game.Events.Select(e => e.Payload).OfType<ProgramJudgmentCardClaimedEvent>()
            .Where(e => e.SkillId == Baonue && e.OwnerSeat == 0).ToArray();
        Require(claimed.Length == 2 && claimed.Select(e => e.CardId).Distinct().Count() == 2 &&
            claimed.All(e => game.CreateSnapshot(0).Players[0].Hand.Any(card => card.Id == e.CardId) &&
                !game.CardMovements.Any(move => move.CardId == e.CardId && move.From == CardLocation.Hand(0) &&
                    move.To.Zone is CardZoneKind.DiscardPile or CardZoneKind.OutsideGame)),
            "Both claimed judgment entities remain in the owner's actual hand after the original producer completes; its cleanup never discards an already claimed card.");
        Replay(game, registry);

        var (native, nativeRegistry) = Create(baonue: true, nativeLord: true); var lord = native.State.Players.Single(p => p.Role == Role.Lord).Seat;
        var target = new[] { 1, 3 }.First(seat => seat != lord);
        Use(native, "draw"); ReachPlay(native);
        Play(native, native.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Alcohol && a.ConversionSource is null)); ReachPlay(native);
        SlashAndPause(native, nativeRegistry, true, 2, target); Continue(native);
        for (var point = 0; point < 2; point++)
        {
            Reach(native, p => p.SkillPrompt?.SkillId == Hp && p.PlayerSeat == lord);
            AssertOwnedJudgment(native, nativeRegistry, lord, point, 0, target);
            Replay(native, nativeRegistry); Accept(native, new AdvanceOneStepCommand(native.Revision));
            Reach(native, p => p.SkillPrompt?.SkillId == Gain && p.PlayerSeat == lord);
            Replay(native, nativeRegistry); Accept(native, new AdvanceOneStepCommand(native.Revision));
        }
        ReachPlay(native);
        Require(native.Events.Count(e => e.Payload is ProgramJudgmentCardClaimedEvent claimed && claimed.SkillId == Baonue && claimed.OwnerSeat == lord) == 2 &&
            native.Events.Count(e => e.Payload is RecoveryAppliedEvent recovery && recovery.TargetSeat == lord && recovery.Amount == 1) == 2 &&
            native.Events.Count(e => e.Payload is DamageAppliedEvent actual && actual.SourceSeat == 0 && actual.TargetSeat == target && actual.Amount == 2) == 1,
            "The native AI Lord actually chooses both positive owner judgments beneath a real physical wine Slash and finishes its HP and claim children without changing the original damage.");
        Replay(native, nativeRegistry);
    }

    private static void AssertOwnedJudgment(GameEngine game, ContentRegistry registry, int owner, int point, int expectedDamageSource, int expectedDamageTarget)
    {
        var root = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Baonue && f.TriggerId == JudgmentBinding);
        var paid = root.OwnedDamagePointJudgment!;
        var damage = game.ResolutionStack.OfType<DamageTriggerWindowFrame>().Single(f => f.Id == paid.DamageWindowId);
        var judgment = game.ResolutionStack.OfType<JudgmentFrame>().Single(f => f.Id == paid.JudgmentFrameId);
        var candidate = damage.Candidates[damage.CandidateIndex];
        Require(paid.InstructionIndex == 1 && paid.OccurrenceIndex == point && paid.DamageOwnerFrameId == damage.ParentFrameId &&
            paid.DamageSourceSeat == expectedDamageSource && paid.DamageTargetSeat == expectedDamageTarget &&
            root.OwnerSeat == owner && root.WindowContext!.ParentFrameId == damage.Id && root.WindowContext.OccurrenceIndex == point &&
            candidate.OwnerSeat == root.OwnerSeat && candidate.ProgramId == root.SkillId && candidate.ProgramTriggerId == root.TriggerId &&
            candidate.SkillInstanceId == root.SkillInstanceId && candidate.GameplayHash == root.GameplayHash && candidate.OccurrenceIndex == point &&
            judgment.ParentFrameId == root.Id && judgment.TargetSeat == owner && judgment.SourceSeat == expectedDamageSource &&
            judgment.Continuation == JudgmentContinuationKind.ProgramSkill && judgment.Reason == JudgmentReason && judgment.ProgramResultBind == paid.ResultBind,
            "A real HP or claim child retains the exact original damage point, source instance, current candidate and own public judgment return.");
        if (game.ResolutionStack.OfType<CardUseFrame>().SingleOrDefault(f => f.SourceSeat == expectedDamageSource && f.CardKind == CardKind.Slash) is { } physical)
            Require(physical.ConsumedAlcoholBoost && physical.Action!.ActorSeat == expectedDamageSource && physical.PhysicalCardIds is [var entity] &&
                physical.Action.PhysicalCards.Single().CardId == entity,
                "The physical wine Slash and its frozen original actor/action survive both owner-benefit child chains.");
        foreach (var viewer in Enumerable.Range(0, 4).Where(seat => seat != owner))
            Require(game.CreateSnapshot(viewer).PendingDecision is null && game.CreateSnapshot(viewer).Players[owner].Hand.Count == 0,
                "Other viewers see public judgment facts but neither the benefit owner's hand nor its private child prompt.");
        Replay(game, registry);
    }

    private static void SlashAndPause(GameEngine game, ContentRegistry registry, bool consumed, int expectedDamage, int target = 1)
    {
        var slash = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeat == target && a.ConversionSource is null);
        Play(game, slash); Reach(game, p => p.SkillPrompt?.SkillId == Damage && p.PlayerSeat == 0);
        var original = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == slash.CardId);
        Require(original.ConsumedAlcoholBoost == consumed && original.Action is { Type: CardActionType.Use, ActorSeat: 0 } &&
            game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Last(e => e.SourceSeat == 0 && e.TargetSeat == target).Amount == expectedDamage,
            "Before the damage-source binding runs, the original actual Slash has the precise consumed-wine bit and applied amount.");
        Replay(game, registry); Reject(game);
    }

    private static CurrentTurnOwnSkillSuppression[] Suppressions(GameEngine game) => game.Events.Select(e => e.Payload)
        .OfType<CurrentTurnOwnSkillSuppressionIssuedEvent>().Select(e => e.Suppression).ToArray();
    private static int ClaimMoves(GameEngine game, int card, int owner) => game.CardMovements.Count(m => m.CardId == card &&
        m.From == CardLocation.Judgment(owner) && m.To == CardLocation.Hand(owner) && m.Reason.Value == $"skill-program.{Baonue}.claimJudgmentCard");
    private static PendingDecision? Prompt(GameEngine game) => Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Action(PendingDecision prompt, string action) => prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static bool Option(PendingDecision prompt, string option) => prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option);
    private static bool Activation(PendingDecision prompt, string skill, string binding) => prompt.Choices.Any(c =>
        c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill && c.Parameters.GetValueOrDefault("binding-id") == binding);
    private static void Play(GameEngine game, LegalAction action) => Accept(game, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
        game.Revision, Prompt(game)!.PromptId, action.PlayedCardKind) { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
    private static void Use(GameEngine game, string activation, IReadOnlyList<int>? targets = null) => Accept(game,
        new UseProgramSkillCommand(0, Driver, activation, [], targets ?? [], game.Revision, Prompt(game)!.PromptId));
    private static void Continue(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate) { var prompt = Prompt(game)!; Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(predicate).Id, game.Revision)); }
    private static void ReachPlay(GameEngine game) => Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 180; step++)
        {
            var prompt = Prompt(game); if (prompt is not null && predicate(prompt)) return;
            if (game.State.Status == EngineStatus.Completed)
                throw new InvalidOperationException("The fixed Dong Zhuo fixture completed before its requested boundary.");
            Advance(game);
        }
        throw new InvalidOperationException("The fixed Dong Zhuo fixture missed its actual boundary: " + JsonSerializer.Serialize(Prompt(game)));
    }
    private static void Advance(GameEngine game)
    {
        var prompt = Prompt(game);
        if (prompt is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(game, new DiscardCardsCommand(0, prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(), prompt.PromptId, game.Revision));
        else if (prompt is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) Accept(game, new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId));
        else if (prompt is { PlayerSeat: 0 } && Option(prompt, "continue")) Continue(game);
        else if (prompt is { PlayerSeat: 0 } && Option(prompt, "lose-hp"))
            throw new InvalidOperationException("A fixture advance attempted an unrequested decay payment: " + JsonSerializer.Serialize(new
            { game.State.TurnNumber, prompt.Kind, Skill = prompt.SkillPrompt?.SkillId, prompt.PromptId,
                Ended = game.Events.Select(e => e.Payload).OfType<TurnEndedEvent>().Where(e => e.ActorSeat == 0).ToArray(),
                Ending = game.ResolutionStack.OfType<TurnEndingBoundaryFrame>().ToArray() }));
        else if (prompt is { PlayerSeat: 0 } && Action(prompt, "skip")) Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else Accept(game, new AdvanceOneStepCommand(game.Revision));
    }
    private static void EndTurn(GameEngine game)
    {
        var before = game.Events.Count(e => e.Payload is TurnEndedEvent { ActorSeat: 0 });
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
        for (var step = 0; step < 100 && game.Events.Count(e => e.Payload is TurnEndedEvent { ActorSeat: 0 }) == before; step++) Advance(game);
        Require(game.Events.Count(e => e.Payload is TurnEndedEvent { ActorSeat: 0 }) == before + 1, "The actual source turn ends once.");
    }
    private static void Reject(GameEngine game) { var before = State(game); var prompt = Prompt(game)!; Require(!game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, new ChoiceId("unpublished"), game.Revision)).Accepted && before == State(game), "Rejecting a forged choice changes no paid state."); }
    private static void Accept(GameEngine game, GameCommand command) { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray(), Frames = JsonSerializer.Serialize(game.ResolutionStack), Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics() });
    private static void Replay(GameEngine game, ContentRegistry registry) => Require(State(game) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry)), "The real command prefix cold-restores all four views, paid typed ancestry, public events and private movement state.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(bool dodgeDeck = false, bool baonue = false, bool nativeLord = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(dodgeDeck, baonue));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0,
            HumanRole = nativeLord ? Role.Renegade : Role.Lord, ModeId = Mode, UseInteractiveSetup = true,
            UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(game, new SelectGeneralCommand(0, "fixture:dz-owner", game.Revision, Prompt(game)!.PromptId)); ReachPlay(game); return (game, registry);
    }

    private sealed class Fixture(bool dodgeDeck, bool baonue) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-dong-zhuo", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var assembly = typeof(StandardClassicGeneralPackage).Assembly;
            string Embedded(string suffix) { using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal)))!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var actual = SkillProgramCatalog.Load(Embedded("boundary-dong-zhuo.rules.json"), Embedded("boundary-dong-zhuo.presentation.json"));
            foreach (var id in actual.Programs.Keys) builder.AddSkill(new(id, actual.Presentations[id].Name, actual.Presentations[id].Description)
                { Program = actual.Programs[id], ProgramPresentation = actual.Presentations[id], Tags = id == Benghuai || id == "boundary:roulin-current" ? SkillTag.Locked : id == Baonue ? SkillTag.Lord : SkillTag.None });
            const string rules = """
                {"schemaVersion":SCHEMA,"skills":[
                 {"id":"fixture:dz-driver","revision":1,"activations":[
                  {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":9}]},
                  {"id":"slash-budget","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":8}]},
                  {"id":"damage-bonus","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantTurnCardDamageModifier","target":"owner","cardKinds":["slash"],"amount":1}]},
                  {"id":"virtual","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"offerVirtualBasicCard","target":"owner"}]},
                  {"id":"suppress","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":8}]},
                  {"id":"other-hit","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]}]},
                 {"id":"fixture:dz-quiet","revision":1,"triggers":[{"id":"quiet","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]},
                 {"id":"fixture:dz-damage","revision":1,"triggers":[{"id":"damage","window":"afterDamageApplied","subject":"damageSource","damageOccurrence":"perDamage","priority":200,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"damage-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"fixture:dz-hp","revision":1,"triggers":[{"id":"hp","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"fixture:dz-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:baonue-current.claimJudgmentCard"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-seen","options":[{"id":"continue"}]}]}]}]}
                """;
            var catalog = SkillProgramCatalog.Load(rules.Replace("SCHEMA", SkillProgramCatalog.RulesSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                    { [Driver] = new { name = "真实驱动", description = "固定真实命令" }, ["fixture:dz-quiet"] = new { name = "安静回合", description = "实际回合减少杀额度" }, [Damage] = Observer("实际伤害"), [Hp] = Observer("实际回复"), [Gain] = Observer("判定获牌") } }));
            foreach (var id in catalog.Programs.Keys) builder.AddSkill(new(id, id, "共享机制真实子窗") { Program = catalog.Programs[id] });
            builder.AddSkill(new("fixture:dz-suppression", "真实来源失效", "HP1禁用其他技能") { SuppressionRule = new(1) });
            builder.AddSkill(new("fixture:dz-selection", "固定选将", "无运行效果") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            builder.AddCard(new("fixture:dz-alcohol", "酒", "基本牌", "真实酒实体", CardKind.Alcohol));
            builder.AddGeneral(new("fixture:dz-owner", "酒杀机制", "supporter", Jiuchi, "qun", 8,
                [Benghuai, Baonue, Driver, Damage, Hp, Gain, "fixture:dz-suppression"]) { InitialHp = baonue ? 4 : 8 });
            for (var index = 1; index < 4; index++) builder.AddGeneral(new($"fixture:dz-target-{index}", "实际其他来源", "supporter", "fixture:dz-selection", "qun", 8,
                baonue ? ["fixture:dz-quiet", Baonue, Hp, Gain] : ["fixture:dz-quiet"]) { InitialHp = baonue ? 4 : 8 });
            builder.AddDeck(new("fixture:dz-deck", "固定合法实体", dodgeDeck ? 4 : 1, 0, []) { PhysicalCards = Enumerable.Range(0, dodgeDeck ? 32 : 18)
                .Select(index => new ContentDeckPhysicalCard(dodgeDeck ? "standard:dodge" : index % 2 == 0 ? "standard:slash" : "fixture:dz-alcohol", Suit.Spade, index % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "当前普通OL董卓共享机制", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:dz-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:dz-owner", "fixture:dz-target-1", "fixture:dz-target-2", "fixture:dz-target-3"]));
        }
        private static object Observer(string name) => new { name, description = "实际程序子结算暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
    }
}
