using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryGuoHuaiChecks
{
    private const string Skill = "boundary:jingce-current";
    private const string Driver = "fixture:gh-driver";
    private const string Gain = "fixture:gh-gain";
    private const string Mode = "identity:classic-boundary-guo-huai-fixture";
    private const string DrawReason = "skill-program." + Skill + ".Draw";

    public static void UsedHandSuitsAndActualUseTypesDeduplicateIndependently()
    {
        var (game, registry) = Create(); ReachPlay(game); Stock(game);
        PlayKind(game, LegalActionKind.Equip); ReachPlay(game);
        PlayKind(game, LegalActionKind.DrawTwo);
        Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.Nullification &&
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "nullification"));
        var counter = P(game)!.Choices.First(c => c.Parameters.GetValueOrDefault("response") == "nullification");
        var counterCard = counter.Cards.Single(); Cold(game, registry); Answer(game, c => c.Id == counter.Id); ReachPlay(game);
        var nullification = Facts(game).Single(e => e.ActorSeat == 0 && e.IsNullificationUse);
        var nullificationAction = game.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>()
            .Single(e => e.Action.ActionId == nullification.CardActionId).Action;
        Require(nullification.HandSuitMask == 8 && nullification.CardCategoryMask == 2 &&
            nullificationAction is { Type: CardActionType.Response, EffectiveKind: CardKind.Nullification, ActorSeat: 0, ProviderSeat: 0, EffectiveSuit: Suit.Diamond } &&
            nullificationAction.PhysicalCards is [var physical] && physical.CardId == counterCard && physical.From == CardLocation.Hand(0),
            "A true own-hand Nullification use keeps its exact Response syntax, frozen Diamond suit and effective Trick category.");
        PlayKind(game, LegalActionKind.Slash, target: 1); ReachPlay(game);
        PlayKind(game, LegalActionKind.Equip); ReachPlay(game);
        var own = Facts(game).Where(e => e.ActorSeat == 0 && e.TurnNumber == 1).ToArray();
        Require(own.Select(e => e.HandSuitMask).Aggregate(0, (a, b) => a | b) == 15 &&
            own.Select(e => e.CardCategoryMask).Aggregate(0, (a, b) => a | b) == 7 &&
            own.Select(e => e.CardActionId).Where(id => id is not null).Distinct().Count() == own.Length,
            "Repeated Heart equipment contributes one suit while Basic, Trick and Equipment remain three independently deduplicated types.");
        Use(game, "opponent-duel", targets: [1]);
        ReachUnnullifiedDuelSlashResponse(game);
        var beforeResponse = Facts(game).Count(e => e.ActorSeat == 0 && e.TurnNumber == 1);
        var slashResponse = P(game)!.Choices.First(c => c.Parameters.GetValueOrDefault("response") == "slash");
        Answer(game, c => c.Id == slashResponse.Id); ReachPlay(game);
        Require(Facts(game).Count(e => e.ActorSeat == 0 && e.TurnNumber == 1) == beforeResponse &&
            Facts(game).Any(f => f.ActorSeat == 1 && f.HandSuitMask == 0 && f.CardCategoryMask == 2 &&
                game.Events.Select(e => e.Payload).OfType<CardUseDeclaredEvent>().Any(e => e.ResolutionId == f.OriginFrameId && e.CardId == 0 && e.CardKind == CardKind.Duel)) &&
            game.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>().Any(e =>
                e.Action.ActorSeat == 0 && e.Action.Type == CardActionType.Response && e.Action.EffectiveKind == CardKind.Slash &&
                e.Action.PhysicalCards.Select(c => c.CardId).SequenceEqual(slashResponse.Cards)),
            "The other actor's zero-entity Duel is a true Trick use; the owner's real paid Slash response is not mistaken for an owner use.");
        EndPlay(game); ReachJingce(game); Cold(game, registry); RejectUnpublished(game);
        Activate(game); ReachDiscard(game);
        Require(DrawMoves(game).Length == 3 && P(game)!.RequiredCardCount ==
            game.State.Players[0].HandCount - game.State.Players[0].Hp - 4,
            "The actual Play-ending draw pays exactly three deck entities; central discard observes the four-suit hand-limit bonus.");
        for (var viewer = 1; viewer < 4; viewer++)
            Require(game.CreateSnapshot(viewer).Players[0].Hand.Count == 0 && game.CreateSnapshot(viewer).PendingDecision is null,
                "Public scalar use history and the hand-limit effect do not expose private discard candidates or remaining hand identities.");
        Cold(game, registry);
    }

    public static void EachPlayEndingDrawUsesActualTurnHistoryAndReturnsFromGainChild()
    {
        var (game, registry) = Create(extraPlay: true, observers: true); ReachPlay(game); Stock(game);
        PlayKind(game, LegalActionKind.Equip); ReachPlay(game); EndPlay(game); ReachJingce(game); Activate(game);
        Reach(game, p => p.SkillPrompt?.SkillId == Gain);
        var producer = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Skill);
        var movement = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.ResumeProgramFrameId == producer.Id);
        Require(producer.InstructionIndex == 1 && producer.WindowContext is { Window: SkillProgramTriggerWindow.PlayEnding } context &&
            context.Facts?.CurrentTurnUsedCardCategoryCount == 1 && movement.Batch.ParentFrameId == producer.Id &&
            movement.Batch.OriginSkillId == Skill && movement.Batch.OriginSkillInstanceId == producer.SkillInstanceId &&
            movement.Batch.Movements is [var paid] && paid.From == CardLocation.DrawPile && paid.To == CardLocation.Hand(0) &&
            paid.Reason.Value == DrawReason && DrawMoves(game).Length == 1,
            "The extra Play's one-card draw advances its exact instruction before a typed movement child suspends the same source instance.");
        Cold(game, registry); RejectUnpublished(game); Continue(game); ReachPlay(game);
        Require(game.Events.Select(e => e.Payload).OfType<PhaseChangedEvent>().Count(e => e.ActorSeat == 0 && e.Phase == TurnPhase.Play) == 2 &&
            Facts(game).Where(e => e.ActorSeat == 0 && e.TurnNumber == 1).All(e => e.HandSuitMask == 2 && e.CardCategoryMask == 4) &&
            DrawMoves(game).Length == 1,
            "The normal Play follows the real inserted Play and shares actual-turn history without repaying the returned draw.");
        EndPlay(game); ReachJingce(game); Skip(game); ReachDiscard(game);
        Require(DrawMoves(game).Length == 1 && P(game)!.RequiredCardCount == game.State.Players[0].HandCount - game.State.Players[0].Hp - 1,
            "Declining the second actual Play-ending offer preserves the one-suit limit and adds no draw entities.");
        Cold(game, registry);
        Reach(game, _ => game.Events.Any(e => e.Payload is TurnEndedEvent { ActorSeat: 0, TurnNumber: 1 }));
        ReachPlay(game);
        var turn = game.State.TurnNumber;
        Require(turn > 1 && !Facts(game).Any(e => e.ActorSeat == 0 && e.TurnNumber == turn),
            "A new actual owner turn starts with no inherited suit or type facts, including its inserted Play.");
        EndPlay(game); ReachJingce(game); Activate(game); ReachPlay(game);
        Require(DrawMoves(game).Length == 1 && !game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Skill),
            "Accepting X=0 completes the current phase's program without a fictitious draw or lingering child.");
        EndPlay(game); ReachJingce(game); Skip(game); ReachDiscard(game);
        Require(P(game)!.RequiredCardCount == game.State.Players[0].HandCount - game.State.Players[0].Hp,
            "Both the next-turn actual history and the previous source instance's numerical hand-limit contribution expire at the turn boundary.");
        Cold(game, registry);
    }

    public static void EffectiveConversionsLegacyVirtualUsesAndLostSourcesKeepExactBoundaries()
    {
        var (mixed, mr) = Create(conversion: true); ReachPlay(mixed); Stock(mixed); Use(mixed, "allow-slashes"); ReachPlay(mixed);
        var hand = mixed.CreateSnapshot(0).Players[0].Hand;
        var costs = new[] { hand.First(c => c.Suit == Suit.Heart).Id, hand.First(c => c.Suit == Suit.Spade).Id };
        Accept(mixed, new UseProgramSkillCommand(0, "classic:fuhun", "two-hand-cards-as-slash", costs, [1], mixed.Revision, P(mixed)!.PromptId));
        ReachPlay(mixed);
        var first = Facts(mixed).Single(e => e.ActorSeat == 0);
        var accepted = mixed.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>().Single(e => e.Action.ActionId == first.CardActionId).Action;
        Require(first.HandSuitMask == 0 && first.CardCategoryMask == 1 && accepted.EffectiveSuit is null &&
            accepted.EffectiveKind == CardKind.Slash && accepted.PhysicalCards.Count == 2 && accepted.PhysicalCards.All(c => c.From == CardLocation.Hand(0)),
            "Mixed Heart equipment and Spade trick hand materials create one actual Basic use with no invented suit or material-type union.");
        var sameSuit = mixed.CreateSnapshot(0).Players[0].Hand.Where(c => c.Suit == Suit.Spade).Take(2).Select(c => c.Id).ToArray();
        Accept(mixed, new UseProgramSkillCommand(0, "classic:fuhun", "two-hand-cards-as-slash", sameSuit, [1], mixed.Revision, P(mixed)!.PromptId));
        ReachPlay(mixed);
        Require(Facts(mixed).Where(e => e.ActorSeat == 0).Select(e => e.CardCategoryMask).Aggregate(0, (a, b) => a | b) == 1 &&
            Facts(mixed).Where(e => e.ActorSeat == 0).Select(e => e.HandSuitMask).Aggregate(0, (a, b) => a | b) == 1,
            "The second same-Spade conversion contributes exactly one frozen suit and leaves the effective type count at one.");
        EndPlay(mixed); ReachJingce(mixed); Activate(mixed); ReachDiscard(mixed);
        Require(DrawMoves(mixed).Length == 1 && P(mixed)!.RequiredCardCount == mixed.State.Players[0].HandCount - mixed.State.Players[0].Hp - 1,
            "True converted hand use determines the actual one-type draw and one-suit discard boundary."); Cold(mixed, mr);

        var (legacy, lr) = Create(legacyVirtual: true);
        Reach(legacy, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == "boundary:shensu" &&
            c.Parameters.GetValueOrDefault("binding-id") == "skip-judgment-and-draw"));
        Answer(legacy, c => c.Parameters.GetValueOrDefault("skill-id") == "boundary:shensu" &&
            c.Parameters.GetValueOrDefault("binding-id") == "skip-judgment-and-draw" && c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(legacy, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
        Answer(legacy, c => c.Targets.SequenceEqual([1])); ReachPlay(legacy);
        var virtualFact = Facts(legacy).Single(e => e.ActorSeat == 0);
        Require(virtualFact.CardActionId is null && virtualFact.HandSuitMask == 0 && virtualFact.CardCategoryMask == 1 &&
            legacy.Events.Select(e => e.Payload).OfType<CardUseDeclaredEvent>().Any(e => e.ResolutionId == virtualFact.OriginFrameId && e.CardId == 0 && e.CardKind == CardKind.Slash),
            "Registered Shensu's legacy Action=null zero-entity Slash remains a real Basic use without changing the old virtual action contract.");
        EndPlay(legacy); ReachJingce(legacy); Activate(legacy);
        Reach(legacy, _ => DrawMoves(legacy).Length == 1); Cold(legacy, lr);

        var (alcohol, ar) = Create(legacyAlcohol: true); ReachPlay(alcohol); Use(alcohol, "lose-five");
        Reach(alcohol, p => p.SkillPrompt?.SkillId == "classic:jiushi" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "flip"));
        Answer(alcohol, c => c.Parameters.GetValueOrDefault("option-id") == "flip"); ReachPlay(alcohol);
        var wine = Facts(alcohol).Single(e => e.ActorSeat == 0);
        Require(wine.CardActionId is null && wine.HandSuitMask == 0 && wine.CardCategoryMask == 1 && alcohol.State.Players[0].Hp == 1 &&
            alcohol.Events.Select(e => e.Payload).OfType<CardUseDeclaredEvent>().Any(e => e.ResolutionId == wine.OriginFrameId && e.CardKind == CardKind.Alcohol && e.CardId == 0),
            "Registered Jiushi's exact SelfDyingResponse zero-entity Alcohol remains a true Basic use and completes its real recovery without inventing an Action.");
        Cold(alcohol, ar);

        var (lost, lostRegistry) = Create(); ReachPlay(lost); Stock(lost); PlayKind(lost, LegalActionKind.Equip); ReachPlay(lost);
        Use(lost, "lose-jingce"); ReachPlay(lost); EndPlay(lost); ReachDiscard(lost);
        Require(lost.Events.Select(e => e.Payload).OfType<ProgramOwnerSkillsReplacedEvent>().Any(e => e.OwnerSeat == 0 && e.LostSkillIds.Contains(Skill)) &&
            DrawMoves(lost).Length == 0 && P(lost)!.RequiredCardCount == lost.State.Players[0].HandCount - lost.State.Players[0].Hp,
            "A real terminal replacement removes the qualified source instance; retained actual-use facts do not become an orphaned hand-limit grant or draw."); Cold(lost, lostRegistry);

        var (suppressed, sr) = Create(); ReachPlay(suppressed); Stock(suppressed); PlayKind(suppressed, LegalActionKind.Equip); ReachPlay(suppressed);
        Use(suppressed, "suppress", targets: [0]);
        Reach(suppressed, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("choice") == Skill));
        Answer(suppressed, c => c.Parameters.GetValueOrDefault("choice") == Skill);
        Reach(suppressed, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0 || p.Kind == DecisionKind.DiscardCards && p.PlayerSeat == 0);
        if (P(suppressed)!.Kind == DecisionKind.PlayCard) EndPlay(suppressed);
        ReachDiscard(suppressed);
        Require(suppressed.Events.Select(e => e.Payload).OfType<ProgramSkillSuppressedEvent>().Any(e => e.TargetSeat == 0 && e.SkillId == Skill && e.Suppressed) &&
            Facts(suppressed).Any(e => e.ActorSeat == 0 && e.HandSuitMask == 2) && DrawMoves(suppressed).Length == 0 &&
            P(suppressed)!.RequiredCardCount == suppressed.State.Players[0].HandCount - suppressed.State.Players[0].Hp,
            "Actual skill suppression removes the qualified modifier and ending candidate while preserving the public use history."); Cold(suppressed, sr);
    }

    public static void NativeActualTypeDrawAndStrictPublicExpressionContract()
    {
        var (native, registry) = Create(native: true);
        for (var step = 0; step < 100 && !native.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Any(e => e.SkillId == Skill); step++)
        {
            if (native.State.Status == EngineStatus.Completed) break;
            Accept(native, new AdvanceOneStepCommand(native.Revision));
        }
        var started = native.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Where(e => e.SkillId == Skill).ToArray();
        Require(started.Length > 0 && started.All(e => e.Window == SkillProgramTriggerWindow.PlayEnding) &&
            native.Events.Select(e => e.Payload).OfType<CurrentTurnCardUseKindsRecordedEvent>().Any(e => started.Any(s => s.OwnerSeat == e.ActorSeat)) &&
            DrawMoves(native).Length > 0,
            "A fixed native mixed deck actually uses cards and accepts the public positive-type draw without manually answering its AI choices.");
        Cold(native, registry);
        var presentation = "{\"schemaVersion\":3,\"skills\":{\"fixture:gh-expression\":{\"name\":\"表达式\",\"description\":\"公共实际回合历史\"}}}";
        var effect = "{\"op\":\"draw\",\"target\":\"owner\",\"numberExpression\":\"currentTurnUsedCardCategoryCount\"}";
        foreach (var body in new[]
        {
            "\"triggers\":[{\"id\":\"invalid\",\"window\":\"turnEnding\",\"subject\":\"owner\",\"optional\":true,\"effects\":[" + effect + "]}]",
            "\"triggers\":[{\"id\":\"invalid\",\"window\":\"playEnding\",\"subject\":\"owner\",\"turnOwnerScope\":\"otherLiving\",\"optional\":true,\"effects\":[" + effect + "]}]",
            "\"triggers\":[{\"id\":\"invalid\",\"window\":\"playEnding\",\"subject\":\"owner\",\"optional\":true,\"effects\":[" + effect.Replace("\"owner\"", "\"selectedTarget\"") + "]}]",
            "\"activations\":[{\"id\":\"invalid\",\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"effects\":[" + effect + "]}]",
            "\"modifiers\":[{\"id\":\"invalid\",\"query\":\"slashLimit\",\"operation\":\"add\",\"valueExpression\":\"currentTurnUsedHandSuitCount\",\"priority\":0}]"
        })
        {
            var rejected = false;
            try { SkillProgramCatalog.Load("{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"fixture:gh-expression\",\"revision\":1," + body + "}]}", presentation); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "The new public expression does not widen incompatible trigger, subject, target, activation or modifier contracts.");
        }
    }

    private static (GameEngine, ContentRegistry) Create(bool extraPlay = false, bool observers = false,
        bool conversion = false, bool legacyVirtual = false, bool legacyAlcohol = false, bool native = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(extraPlay, observers, conversion, legacyVirtual, legacyAlcohol, native));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = native ? -1 : 0,
            HumanRole = native ? null : Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false, MaxTurns = native ? 2 : 8 }, registry);
        Accept(game, new StartGameCommand());
        if (!native)
        {
            Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
            Accept(game, new SelectGeneralCommand(0, "fixture:gh-owner", game.Revision, P(game)!.PromptId));
        }
        return (game, registry);
    }
    private static CurrentTurnCardUseKindsRecordedEvent[] Facts(GameEngine game) => game.Events.Select(e => e.Payload).OfType<CurrentTurnCardUseKindsRecordedEvent>().ToArray();
    private static CardMovementRecord[] DrawMoves(GameEngine game) => game.CardMovements.Where(m => m.Reason.Value == DrawReason).ToArray();
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void ReachPlay(GameEngine game) => Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void ReachDiscard(GameEngine game) => Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.DiscardCards);
    private static void ReachJingce(GameEngine game) => Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Skill && c.Parameters.GetValueOrDefault("program-action") == "activate"));
    private static void EndPlay(GameEngine game) => Accept(game, new EndPlayPhaseCommand(0, game.Revision, P(game)!.PromptId));
    private static void Stock(GameEngine game) { Use(game, "stock"); ReachPlay(game); }
    private static void Use(GameEngine game, string id, IReadOnlyList<int>? targets = null) => Accept(game, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], game.Revision, P(game)!.PromptId));
    private static void PlayKind(GameEngine game, LegalActionKind kind, int? target = null)
    {
        var action = game.GetHumanLegalActions().First(a => a.Kind == kind && a.ConversionSource is null &&
            (target is null || a.TargetSeats.Contains(target.Value)));
        Accept(game, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision, P(game)!.PromptId,
            action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
    }
    private static void Activate(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("skill-id") == Skill && c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Skip(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
    private static void Continue(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate) { var p = P(game)!; Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, game.Revision)); }
    private static void ReachUnnullifiedDuelSlashResponse(GameEngine game)
    {
        for (var step = 0; step < 80; step++)
        {
            var p = P(game);
            if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash }) return;
            if (p is { PlayerSeat: 0, Kind: DecisionKind.Nullification } &&
                game.ResolutionStack.OfType<NullificationWindowFrame>().LastOrDefault() is
                    { EffectCardKind: CardKind.Duel, SourceSeat: 1, EffectNullified: true } window &&
                window.TargetSeats.SequenceEqual([0]))
            {
                // This human restores the real Duel with a real hand card after
                // a native counterspell; native decisions still use Advance.
                Answer(game, c => c.Parameters.GetValueOrDefault("response") == "nullification");
            }
            else Advance(game);
        }
        throw new InvalidOperationException("The restored real Duel did not reach its paid Slash response: " + JsonSerializer.Serialize(P(game)));
    }
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 240; step++)
        { var p = P(game); if (p is not null && predicate(p)) return; Advance(game); }
        throw new InvalidOperationException("Fixed Guo Huai fixture did not reach its boundary: " + JsonSerializer.Serialize(P(game)));
    }
    private static void Advance(GameEngine game)
    {
        var p = P(game);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(game, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, game.Revision));
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Skip(game);
        else if (p?.SkillPrompt?.SkillId == Gain) Continue(game);
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass")) Answer(game, c => c.Parameters.GetValueOrDefault("response") == "pass");
        else Accept(game, new AdvanceOneStepCommand(game.Revision));
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack), Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine game, ContentRegistry registry) => Require(State(game) == State(GameReplay.Restore(
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry)), "Four private views, scalar actual-use facts, owning child cursors and physical moves cold-restore exactly.");
    private static void RejectUnpublished(GameEngine game)
    { var before = State(game); var p = P(game)!; Require(!game.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished"), game.Revision)).Accepted && before == State(game), "An unpublished choice cannot change history, draw or the owning cursor."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool extraPlay, bool observers, bool conversion, bool legacyVirtual, bool legacyAlcohol, bool native) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-guo-huai", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                 {"id":"{{Driver}}","revision":1,"activations":[
                  {"id":"stock","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20},{"op":"draw","target":"owner","amount":12}]},
                  {"id":"allow-slashes","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":3}]},
                  {"id":"lose-five","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":5}]},
                  {"id":"opponent-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
                  {"id":"lose-jingce","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Skill}}"],"sourceBind":"fixture:gh-selection"}]},
                  {"id":"suppress","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"suppressGeneralSkill","target":"selectedTarget"}]}]},
                 {"id":"fixture:gh-quiet","revision":1,"triggers":[{"id":"quiet","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]},
                 {"id":"{{Gain}}","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","optional":false,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{DrawReason}}"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Driver] = new { name = "真实命令", description = "固定得牌、转换、来源移除与实际决斗" },
                    ["fixture:gh-quiet"] = new { name = "固定目标", description = "限制普通杀" },
                    [Gain] = new { name = "精策得牌观察", description = "真实收益子链", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } }));
            foreach (var id in new[] { Driver, "fixture:gh-quiet", Gain }) builder.AddSkill(new(id, id, "正式夹具程序") { Program = catalog.Programs[id] });
            builder.AddSkill(new("fixture:gh-selection", "固定选将", "无运行技能") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            builder.AddSkill(new("fixture:gh-native-owner", "原生选将", "主公采用唯一精策来源")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Lord ? 1000d : -1000d) });
            var ownerSkills = new List<string>();
            if (!native) ownerSkills.Add(Driver);
            if (native) ownerSkills.Add("fixture:gh-native-owner");
            if (extraPlay) ownerSkills.Add("classic:dangxian");
            if (observers) ownerSkills.Add(Gain);
            if (conversion) ownerSkills.Add("classic:fuhun");
            if (legacyVirtual) ownerSkills.Add("boundary:shensu");
            if (legacyAlcohol) ownerSkills.Add("classic:jiushi");
            builder.AddGeneral(new("fixture:gh-owner", "界郭淮机制", "supporter", Skill, "wei", 4, ownerSkills));
            for (var i = 1; i < 4; i++) builder.AddGeneral(new($"fixture:gh-target-{i}", "固定目标", "supporter", "fixture:gh-selection", "shu", 8, ["fixture:gh-quiet"]));
            var ids = new[] { "standard:slash", "standard:draw_two", "standard:crossbow", "standard:nullification" };
            var suits = new[] { Suit.Club, Suit.Spade, Suit.Heart, Suit.Diamond };
            builder.AddDeck(new("fixture:gh-deck", "固定四花色三类型实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 64)
                .Select(i => new ContentDeckPhysicalCard(ids[i % 4], suits[i % 4], i % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "真实界郭淮", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:gh-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:gh-owner", "fixture:gh-target-1", "fixture:gh-target-2", "fixture:gh-target-3"]));
        }
    }
}
