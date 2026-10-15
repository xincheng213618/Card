using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class BoundaryWolongZhugeLiangChecks
{
    private const string Mode = "identity:classic-current-wolong-fixture";
    private const string Driver = "fixture:wolong-driver";
    private const string Hp = "fixture:wolong-hp";
    private const string Gain = "fixture:wolong-gain";
    private const string Loss = "fixture:wolong-loss";
    private const string Completed = "fixture:wolong-completed";
    private const string EquipmentChild = "fixture:wolong-equipment-child";
    private const string FaceChild = "fixture:wolong-face-child";
    private const string AlcoholCompleted = "fixture:wolong-alcohol-completed";
    private static readonly Lazy<ContentRegistry> FormalBase = new(StandardContentRegistry.CreateWithClassicGenerals);

    public static void RandomRevealColorHePaymentAndChildrenCold()
    {
        var (game, registry) = Create();
        Require(game.State.Players[0].Hp == 2 && game.State.Players[0].MaxHp == 4,
            "The real formal Lord begins wounded at two HP.");
        var armor = Hand(game, 0).First().Id;
        Play(game, armor, []); ReachPlay(game);
        Require(game.CreateSnapshot(0).Players[0].Equipment.Any(card => card.Id == armor), "A real red Silver Lion is equipped.");
        var material = Hand(game, 0).First().Id;
        var beforeTargetHp = game.State.Players[1].Hp;
        PlayAs(game, material, [1], CardKind.FireAttack); Reach(game, prompt => prompt.Kind == DecisionKind.FireAttackDiscard);
        var use = game.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.ColorFireAttack is { RevealedCardId: not null });
        var receipt = use.ColorFireAttack!;
        Require(P(game) is { PlayerSeat: 0 } && P(game)!.ValidCardIds.Contains(armor) &&
            receipt.RevealedIsRed == true && receipt.PaidCardId is null &&
            Facts<FireAttackCardRevealedEvent>(game).Count(fact => fact.ResolutionId == use.Id) == 1 &&
            !game.AcceptedCommands.OfType<AnswerPromptCommand>().Any(command => command.ActorSeat == 1),
            "The target's real hand is randomly revealed once, without a target choice; the source can pay equipped same-color armor.");
        PrivateAndFrozen(game); Cold(game, registry);
        var before = State(game);
        Require(!game.Submit(new AnswerPromptCommand(0, P(game)!.PromptId, new("unpublished-payment"), game.Revision)).Accepted && State(game) == before,
            "Unpublished payment cannot change the frozen reveal, RNG or accepted prefix.");
        Answer(game, choice => choice.Cards.SequenceEqual([armor])); Reach(game, prompt => prompt.SkillPrompt?.SkillId == Hp);
        var paid = game.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.Id == use.Id);
        var hp = game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single(frame => frame.ResumeFrameId == use.Id);
        Require(paid.ColorFireAttack is { PaidCardId: var paidId, PaidFrom: var paidFrom } && paidId == armor &&
            paidFrom == CardLocation.Equipment(0) && hp.Continuation == PostEventContinuation.ColorFireAttackPayment &&
            hp.Change.ParentFrameId == use.Id && game.State.Players[0].Hp == 3 && game.State.Players[1].Hp == beforeTargetHp &&
            CostMoves(game, armor).Length == 1 && !Facts<FireAttackResolvedEvent>(game).Any(fact => fact.ResolutionId == use.Id),
            "Real Silver Lion equipment payment and its exact HP child precede the fire effect, once.");
        Cold(game, registry); Continue(game); Reach(game, prompt => prompt.SkillPrompt?.SkillId == Gain &&
            game.ResolutionStack.OfType<CardUseFrame>().Any(frame => frame.Id == use.Id && frame.ColorFireAttack?.PaidCardId == armor));
        Require(game.State.Players[1].Hp == beforeTargetHp && CostMoves(game, armor).Length == 1,
            "The recovery observer's actual draw/gain child still returns to the same already-paid Fire Attack.");
        Cold(game, registry); Continue(game); Reach(game, prompt => prompt.SkillPrompt?.SkillId == Loss);
        var movement = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(frame => frame.ResumeColorFireAttackFrameId == use.Id);
        Require(movement.Batch.ParentFrameId == use.Id && movement.Batch.AwaitingProgramFrameId is null &&
            movement.ResumeProgramFrameId is null && movement.Batch.Movements.Any(move => move.CardId == armor && move.From == CardLocation.Equipment(0)),
            "The real armor loss window has a direct typed paid CardUse return, not a borrowed Program continuation.");
        Cold(game, registry); Continue(game); ReachPlay(game);
        Require(CostMoves(game, armor).Length == 1 && Facts<ColorFireAttackPaidEvent>(game).Count(fact => fact.CardUseFrameId == use.Id) == 1 &&
            Facts<FireAttackResolvedEvent>(game).Single(fact => fact.ResolutionId == use.Id).CausedDamage &&
            game.State.Players[1].Hp == beforeTargetHp - 1 &&
            Facts<CardUseFinishedEvent>(game).Any(fact => fact.ResolutionId == use.Id && fact.CardKind == CardKind.FireAttack) &&
            !game.CreateCardZoneDiagnostics().Any(card => card.Location == CardLocation.Processing),
            "Only after recovery/gain/loss children does the original real fire use damage and clean Processing.");
        Cold(game, registry);

        // A separate fixed native turn exercises the AI's published HE payment
        // chooser; neither selected target card nor hidden opponent hand is fed to it.
        var (native, nativeRegistry) = Create(nativeFire: true);
        Accept(native, new EndPlayPhaseCommand(0, native.Revision, P(native)!.PromptId));
        ReachUntil(native, () => Facts<ColorFireAttackPaidEvent>(native).Any(fact => fact.SourceSeat != 0));
        var nativePaid = Facts<ColorFireAttackPaidEvent>(native).First(fact => fact.SourceSeat != 0);
        Require(native.AiThoughts.Any(thought => thought.ActorSeat == nativePaid.SourceSeat && thought.Summary.Contains("颜色火攻", StringComparison.Ordinal)),
            "A native AI actually pays a published color Fire Attack candidate using its own filtered view.");
        Cold(native, nativeRegistry);
    }

    public static void ActualTurnTrickHistoryAndCangzhuoDiscardCold()
    {
        var (unused, unusedRegistry) = Create(tricks: true);
        var ids = Hand(unused, 0).Select(card => card.Id).ToArray();
        Require(ids.Length > unused.State.Players[0].Hp, "The fixed real trick hand exceeds ordinary HP hand limit.");
        Accept(unused, new EndPlayPhaseCommand(0, unused.Revision, P(unused)!.PromptId));
        ReachUntil(unused, () => Facts<TurnHandLimitCardKindExemptionGrantedEvent>(unused).Any(fact => fact.Policy.Source.SkillId == "boundary:cangzhuo"));
        var policy = Facts<TurnHandLimitCardKindExemptionGrantedEvent>(unused).Single(fact => fact.Policy.Source.SkillId == "boundary:cangzhuo").Policy;
        Require(policy.TurnNumber == 1 && policy.Source.OwnerSeat == 0 && policy.CardKinds.Contains(CardKind.DrawTwo) &&
            policy.CardKinds.Contains(CardKind.Nullification) && policy.CardKinds.Contains(CardKind.Lightning) &&
            !policy.CardKinds.Contains(CardKind.Slash) && Facts<ActualTurnTrickUseRecordedEvent>(unused).Length == 0,
            "No actual own trick use grants all current instant and delayed trick kinds, while Basic cards remain counted.");
        Frozen(policy.CardKinds); Cold(unused, unusedRegistry);
        ReachUntil(unused, () => unused.State.TurnNumber > 1);
        Require(!unused.CardMovements.Any(move => ids.Contains(move.CardId) && move.Reason == CardMoveReasons.HandLimitDiscard) &&
            Enumerable.Range(0, 4).All(seat => unused.CreateSnapshot(seat).Players[0].TurnHandLimitCardKindExemptions is null),
            "Exempt actual tricks survive that ordinary discard phase; the issued turn policy expires at the actual turn end.");
        Cold(unused, unusedRegistry);

        var (used, usedRegistry) = Create(tricks: true);
        var useCard = Hand(used, 0).First().Id; Play(used, useCard, []); ReachPlay(used);
        var accepted = Facts<ActualTurnTrickUseRecordedEvent>(used).Single();
        Require(accepted.ActorSeat == 0 && accepted.TurnNumber == 1 && accepted.EffectiveKind == CardKind.DrawTwo &&
            Facts<CardActionAcceptedEvent>(used).Any(fact => fact.Action.ActionId == accepted.ActionId),
            "The actual physical trick records one action despite declared/accepted/completed windows.");
        Cold(used, usedRegistry); Accept(used, new EndPlayPhaseCommand(0, used.Revision, P(used)!.PromptId));
        Reach(used, prompt => prompt.Kind == DecisionKind.DiscardCards && prompt.PlayerSeat == 0);
        Require(Facts<TurnHandLimitCardKindExemptionGrantedEvent>(used).All(fact => fact.Policy.Source.SkillId != "boundary:cangzhuo") &&
            P(used)!.RequiredCardCount == Hand(used, 0).Count - used.State.Players[0].Hp &&
            P(used)!.ValidCardIds.Order().SequenceEqual(Hand(used, 0).Select(card => card.Id).Order()),
            "After one real trick use, Cangzhuo does not hide trick cards from the ordinary exact-ID discard prompt.");
        PrivateAndFrozen(used); Cold(used, usedRegistry);
    }

    private static CardMovementRecord[] CostMoves(GameEngine game, int id) => game.CardMovements.Where(move => move.CardId == id && move.Reason == CardMoveReasons.FireAttackDiscard).ToArray();
    private static IReadOnlyList<CardSnapshot> Hand(GameEngine game, int seat) => game.CreateSnapshot(seat).Players[seat].Hand;
    private static T[] Facts<T>(GameEngine game) where T : IGameEvent => game.Events.Select(item => item.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    private static void Play(GameEngine game, int id, int[] targets) => Accept(game, new PlayCardCommand(0, id, targets, game.Revision, P(game)!.PromptId));
    private static void PlayAs(GameEngine game, int id, int[] targets, CardKind kind)
    {
        var action = game.GetHumanLegalActions().First(item => item.CardId == id && item.PlayedCardKind == kind &&
            (item.TargetSeats.Count > 0 ? item.TargetSeats : item.TargetSeat is { } seat ? [seat] : Array.Empty<int>()).SequenceEqual(targets));
        Accept(game, new PlayCardCommand(0, id, targets, game.Revision, P(game)!.PromptId, kind, action.TargetCardId)
        { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
    }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> choose)
    { var prompt = P(game)!; Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(choose).Id, game.Revision)); }
    private static void Continue(GameEngine game) => Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "choose-option" && choice.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void ReachPlay(GameEngine game) => Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard && prompt.PlayerSeat == 0);
    private static void Reach(GameEngine game, Func<PendingDecision, bool> stop) => ReachUntil(game, () => P(game) is { } prompt && stop(prompt));
    private static void ReachUntil(GameEngine game, Func<bool> stop)
    { for (var step = 0; step < 400 && !stop(); step++) Step(game); Require(stop(), $"Expected real boundary was not reached: {P(game)?.Kind}/{P(game)?.SkillPrompt?.SkillId}."); }
    private static void Step(GameEngine game)
    {
        var prompt = P(game);
        if (prompt is { PlayerSeat: 0 } && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "choose-option")) Continue(game);
        else if (prompt is { PlayerSeat: 0, Kind: DecisionKind.Nullification }) Answer(game, choice => choice.Parameters.GetValueOrDefault("response") == "pass");
        else if (prompt is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(game, new DiscardCardsCommand(0, prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(), prompt.PromptId, game.Revision));
        else if (prompt is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(game, choice => choice.Parameters.GetValueOrDefault("response") == "let-die");
        else if (prompt is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) Accept(game, new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId));
        else if (prompt is { PlayerSeat: 0 } && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "skip");
        else Accept(game, new AdvanceOneStepCommand(game.Revision));
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Actual command rejected."); }
    private static void Frozen<T>(IReadOnlyList<T> values)
    { Require(values is IList<T> { IsReadOnly: true }, "The prepared collection is frozen."); try { ((IList<T>)values).Add(default!); throw new InvalidOperationException("Prepared collection allowed mutation."); } catch (NotSupportedException) { } }
    private static void PrivateAndFrozen(GameEngine game)
    {
        var state = State(game); var prompt = P(game)!;
        foreach (var seat in Enumerable.Range(0, 4))
        {
            var view = game.CreateSnapshot(seat);
            Require(view.Players.Where(player => player.Seat != seat).All(player => player.Hand.Count == 0), "No other hand IDs leak through prepared views.");
            if (seat != prompt.PlayerSeat) Require(view.PendingDecision is null, "Only the exact picker receives private payment/option choices.");
            else
            {
                var own = view.PendingDecision!; Frozen(own.ValidCardIds); Frozen(own.ValidTargetSeats); Frozen(own.Choices);
                foreach (var choice in own.Choices)
                {
                    Frozen(choice.Cards); Frozen(choice.Targets); Frozen(choice.ContentIds);
                    Require(choice.Parameters is IDictionary<string, string> { IsReadOnly: true }, "Prepared nested choice parameters are frozen.");
                    try { ((IDictionary<string, string>)choice.Parameters).Add("mutation", "probe"); throw new InvalidOperationException("Choice parameters allowed mutation."); } catch (NotSupportedException) { }
                }
            }
        }
        Require(State(game) == state, "Four-view mutation probes preserve the accepted prefix and RNG.");
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray(), Frames = JsonSerializer.Serialize(game.ResolutionStack), Events = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(), game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine game, ContentRegistry registry) => Require(State(game) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry)), "Real accepted commands cold-restore four views, RNG, typed payment/response parents and the exact card ledger.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(bool black = false, bool tricks = false, bool nativeFire = false, bool physicalCounterspell = false, bool nativeCounterspell = false, bool qingxian = false, bool qingxianDying = false, string? otherRescue = null, bool emptyOtherHands = false)
    {
        var formal = FormalBase.Value;
        var packages = formal.Packages.Select((manifest, index) => new FrozenBasePackage(manifest, index == 0 ? formal : null));
        var registry = ContentRegistry.Build([.. packages, new Fixture(black, tricks, nativeFire, physicalCounterspell, nativeCounterspell, qingxian, qingxianDying, otherRescue, emptyOtherHands)]);
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, prompt => prompt.Kind == DecisionKind.SelectGeneral && prompt.PlayerSeat == 0);
        Accept(game, new SelectGeneralCommand(0, "fixture:wolong-owner", game.Revision, P(game)!.PromptId)); ReachPlay(game);
        if (emptyOtherHands)
            Require(Hand(game, 0).Count == 6 && Enumerable.Range(1, 3).All(seat => Hand(game, seat).Count == 0),
                "A legal zero-card base deal and owner-only initial-hand bonus preserve the owner's six real cards while other participants begin without a hand fallback.");
        return (game, registry);
    }

    // Replay frozen definitions once while retaining every original package manifest.
    private sealed class FrozenBasePackage(PackageManifest manifest, ContentRegistry? source) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = manifest;
        public void Register(IContentRegistryBuilder builder)
        {
            if (source is null) return;
            foreach (var definition in source.Cards.Values) builder.AddCard(definition);
            foreach (var definition in source.Skills.Values) builder.AddSkill(definition);
            foreach (var definition in source.Generals.Values) builder.AddGeneral(definition);
            foreach (var definition in source.Decks.Values) builder.AddDeck(definition);
            foreach (var definition in source.Modes.Values) builder.AddMode(definition);
        }
    }

    private sealed class Fixture(bool black, bool tricks, bool nativeFire, bool physicalCounterspell, bool nativeCounterspell, bool qingxian, bool qingxianDying, string? otherRescue, bool emptyOtherHands) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-current-wolong", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = JsonNode.Parse("""
                {"skills":[
                 {"id":"fixture:wolong-driver","revision":1,"viewAs":[{"id":"he-dismantlement","sourceZones":["hand"],"inputKinds":[],"inputSuits":[],"outputKind":"dismantlement","forPlay":true,"forResponse":false}],"activations":[
                  {"id":"equip-self","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"self-equipment"}]},
                  {"id":"equip-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipment"}]},
                  {"id":"hurt-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                  {"id":"prepare-one-hp","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":7}]},
                  {"id":"empty-other-hand","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"discardParticipantCards","target":"selectedTarget","amount":64,"zones":["hand"]}]}]},
                 {"id":"fixture:wolong-hp","revision":1,"triggers":[{"id":"recover","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-seen","options":[{"id":"continue"}]},{"op":"draw","target":"owner","amount":1}]}]},
                 {"id":"fixture:wolong-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"fixture:wolong-loss","revision":1,"triggers":[{"id":"paid-loss","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementReasons":["card.effect.fire-attack-discard"],"movementOccurrence":"perOwnerBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"loss-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"fixture:wolong-completed","revision":1,"triggers":[{"id":"counterspell-completed","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["nullification"],"includeResponseUses":true,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"completed-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"fixture:wolong-equipment-child","revision":1,"triggers":[{"id":"exact-random-equipment","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardCategories":["equipment"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"equipment-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"fixture:wolong-face-child","revision":1,"triggers":[{"id":"face","window":"characterTurnedOver","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"face-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"fixture:wolong-alcohol-completed","revision":1,"triggers":[{"id":"alcohol-completed","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["alcohol"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"alcohol-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"fixture:wolong-quiet","revision":1,"triggers":[{"id":"quiet-turn","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            if (emptyOtherHands)
                rules["skills"]!.AsArray().Single(skill => skill!["id"]!.GetValue<string>() == Driver)!["modifiers"] =
                    JsonNode.Parse("""[{"id":"owner-initial-cards","query":"initialHandSize","operation":"add","value":4,"priority":0}]""");
            var presentation = new Dictionary<string, object>();
            foreach (var skill in new[] { Driver, Hp, Gain, Loss, Completed, EquipmentChild, FaceChild, AlcoholCompleted, "fixture:wolong-quiet" })
                presentation[skill] = skill is Hp or Gain or Loss or Completed or EquipmentChild or FaceChild or AlcoholCompleted ? new { name = skill, description = "真实子窗暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } : new { name = skill, description = "固定命令夹具" };
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = 3, skills = presentation }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, id, "真实命令夹具") { Program = program });
            builder.AddSkill(new("fixture:wolong-pick", "稳定选将偏好", "无运行程序的前序AI选择") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            builder.AddGeneral(new("fixture:wolong-owner", "当前卧龙真实能力", "supporter", Driver, "shu", 3,
                new[] { "classic:bazhen", "boundary:huoji-current", "boundary:kanpo-current", "boundary:cangzhuo", Hp, Gain, Loss, Completed }
                    .Concat(qingxian ? new[] { "classic:qingxian" } : Array.Empty<string>())
                    .Concat(otherRescue == "bound" ? new[] { "classic:chunlao" } : Array.Empty<string>()).ToArray()) { InitialHp = 1 });
            for (var index = 1; index < 4; index++) builder.AddGeneral(new($"fixture:wolong-other-{index}", "固定其他角色", "supporter", "fixture:wolong-pick", "wei", 8,
                otherRescue == "virtual" ? ["fixture:wolong-quiet", "boundary:jiushi", FaceChild, EquipmentChild] : otherRescue == "bound" ? ["fixture:wolong-quiet", Hp, Gain, AlcoholCompleted, EquipmentChild] : nativeFire ? ["boundary:huoji-current"] : nativeCounterspell ? ["fixture:wolong-quiet", "boundary:kanpo-current", Hp, Gain, Completed] : qingxianDying ? ["fixture:wolong-quiet", "classic:niepan", Hp, Gain] : qingxian ? ["fixture:wolong-quiet", EquipmentChild] : ["fixture:wolong-quiet"]));
            var kind = tricks ? "standard:draw_two" : physicalCounterspell ? "standard:nullification" : "classic:silver-lion";
            builder.AddDeck(new("fixture:wolong-deck", "固定小牌组", emptyOtherHands ? 0 : 4, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(index => new ContentDeckPhysicalCard(otherRescue == "bound" && index % 2 == 1 ? "standard:slash" : kind, black ? Suit.Spade : Suit.Diamond, 7)).ToArray() });
            builder.AddMode(new(Mode, "当前卧龙公共能力命令检查", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:wolong-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:wolong-owner", "fixture:wolong-other-1", "fixture:wolong-other-2", "fixture:wolong-other-3"]));
        }
    }
}
