using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class PublicPileCashOutChecks
{
    private const string Skill = "fixture:cash-out-pile";
    private const string Actual = "ol:tuifeng";
    private const string Driver = "fixture:cash-out-driver";
    private const string StoreChild = "fixture:cash-out-store-child";
    private const string PaymentChild = "fixture:cash-out-payment-child";
    private const string DrawChild = "fixture:cash-out-draw-child";
    private const string HpChild = "fixture:cash-out-hp-child";
    private const string Other = "fixture:cash-out-other-source";
    private const string Owner = "fixture:cash-out-owner";
    private const string Mode = "identity:classic-cash-out-fixture";

    public static void DamagePointsStoreHandOrEquipmentAndDeclineWithColdReturn()
    {
        foreach (var (equipment, sourceLess) in new[] { (false, false), (true, false), (false, true) })
        {
            var (game, registry) = Start(actual: true, armor: equipment);
            var armor = equipment ? game.CreateSnapshot(0).Players[0].Hand.First().Id : 0;
            if (equipment)
            {
                Accept(game, new PlayCardCommand(0, armor, [], game.Revision, P(game)!.PromptId));
                Reach(game, IsHumanPlay);
            }
            var hp = game.CreateSnapshot(0).Players[0].Hp;
            var amount = equipment || sourceLess ? 1 : 2;
            UseDriver(game, sourceLess ? "source-less-damage" : "damage-two"); Reach(game, p => IsOffer(p, Actual));
            var retained = game.CreateSnapshot(0); var retainedJson = SnapshotJson.Serialize(retained);
            RejectWrongActor(game); game = Cold(game, registry);
            var first = equipment ? armor : game.CreateSnapshot(0).Players[0].Hand.First().Id;
            game = StoreOne(game, registry, Actual, first, equipment);
            Require(Facts<DamageAppliedEvent>(game).Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == amount && e.SourceLess == sourceLess) == 1 &&
                    Facts<PublicPileCashOutPaidEvent>(game).Count(e => e.Operation == SkillProgramEffectOp.StoreBoundCardsInPublicPile) == 1,
                "One true applied damage event pays its first opportunity once, preserving actual capped amount and nullable source attribution.");
            if (amount == 2)
            {
                Reach(game, p => IsOffer(p, Actual));
                Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            }
            int[] stored = [first];
            Reach(game, IsHumanPlay);
            var starts = Facts<PublicPileCashOutStartedEvent>(game).Where(e => e.Operation == SkillProgramEffectOp.StoreBoundCardsInPublicPile).ToArray();
            Require(starts.Length == stored.Length && starts.Select(e => e.ParentId).Distinct().Count() == 1 &&
                    starts.All(e => e.Issuer.SkillId == Actual && e.Issuer.OwnerSeat == 0 && e.FrozenCount == 1 &&
                        e.Pile.SkillId == Actual && e.Pile.SkillInstanceId == e.Issuer.SkillInstanceId && e.Pile.Capacity == int.MaxValue) &&
                    Facts<ProgramBindingResolvedEvent>(game).Count(e => e.SkillId == Actual && e.Activated && e.Completed) == stored.Length &&
                    Facts<ProgramBindingResolvedEvent>(game).Count(e => e.SkillId == Actual && !e.Activated && !e.Completed) == (amount == 2 ? 1 : 0) &&
                    Facts<AfterDamageEvent>(game).Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == amount && e.SourceLess == sourceLess) == 1 &&
                    Facts<DamageRequestedEvent>(game).Single(e => e.TargetSeat == 0).SourceLess == sourceLess &&
                    Facts<SilverLionDamageCappedEvent>(game).Count(e => e.TargetSeat == 0) == (equipment ? 1 : 0) &&
                    game.CreateSnapshot(0).Players[0].Hp == hp - amount + (equipment ? 1 : 0) &&
                    Facts<SilverLionRemovedRecoveryEvent>(game).Count(e => e.PlayerSeat == 0 && e.RecoveredAmount == 1) == (equipment ? 1 : 0) &&
                    game.CardMovements.Count(m => m.Reason.Value == StoreReason(Actual)) == stored.Length &&
                    Enumerable.Range(0, 4).All(s => PileCards(game, s, Actual).Select(c => c.Id).SequenceEqual(stored)) &&
                    SnapshotJson.Serialize(retained) == retainedJson,
                "Actual registered Tuifeng distinguishes real damage points and source-less damage, pays only activated whole HE costs, publicly isolates its exact instance and returns once through armor recovery. " +
                JsonSerializer.Serialize(new { starts, stored, Hp = game.CreateSnapshot(0).Players[0].Hp,
                    Resolved = Facts<ProgramBindingResolvedEvent>(game).Where(e => e.SkillId == Actual).ToArray() }));
            Frozen(PileCards(game, 2, Actual)); _ = Cold(game, registry);
        }
    }
    private static GameEngine StoreOne(GameEngine game, ContentRegistry registry, string skill, int cardId, bool equipment = false)
    {
        var offer = P(game)!; var activate = offer.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Answer(game, c => c.Id == activate.Id); Reach(game, IsOwnedPicker);
        var frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == skill);
        var id = frame.Id; var context = frame.WindowContext!;
        Require(frame.InstructionIndex == 1 && frame.OwnedCardSelection is { RequiredCount: 1, MinimumCount: 1 } &&
                P(game)!.Choices.All(c => c.Cards.Count == 1) &&
                context.Window == SkillProgramTriggerWindow.AfterDamageApplied && context.OwnerSeat == 0,
            "Each actual damage point starts an exact one-card own HE draft with no zero-card finish.");
        AssertPrivate(game, 0); Frozen(P(game)!.Choices); Frozen(P(game)!.Choices[0].Cards);
        RejectWrongActor(game); RejectUnpublished(game); game = Cold(game, registry);
        Answer(game, c => c.Cards.SequenceEqual([cardId]));
        Reject(game, new AnswerPromptCommand(0, offer.PromptId, activate.Id, game.Revision));
        if (equipment)
        {
            Reach(game, p => IsContinue(p, HpChild));
            frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == id);
            Require(frame.PublicPileCashOut is { Operation: SkillProgramEffectOp.StoreBoundCardsInPublicPile,
                    Stage: PublicPileCashOutStage.PaymentChildren, FrozenCount: 1 } paid &&
                    paid.PaidCardIds.SequenceEqual([cardId]) && paid.PaidFrom.SequenceEqual([CardLocation.Equipment(0)]) &&
                    paid.ParentId == context.ParentFrameId && !Facts<PublicPileCashOutResolvedEvent>(game).Any(e => e.FrameId == id),
                "The equipment entity is already paid while its real Silver Lion HP child suspends the exact original damage-point store.");
            game = Cold(game, registry); Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        }
        Reach(game, p => IsContinue(p, StoreChild));
        frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == id);
        var receipt = frame.PublicPileCashOut!;
        var movement = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.ParentFrameId == id);
        Require(frame.InstructionIndex == 2 && receipt.Operation == SkillProgramEffectOp.StoreBoundCardsInPublicPile &&
                receipt.PaidCardIds.SequenceEqual([cardId]) && receipt.PaidFrom.SequenceEqual([equipment ? CardLocation.Equipment(0) : CardLocation.Hand(0)]) &&
                receipt.FrozenCount == 1 && receipt.Issuer.SkillId == skill && receipt.Issuer.SkillInstanceId == frame.SkillInstanceId &&
                receipt.Pile.SkillId == skill && receipt.Pile.SkillInstanceId == frame.SkillInstanceId &&
                receipt.ParentId == context.ParentFrameId && movement.Batch.OriginSkillId == skill &&
                movement.Batch.OriginSkillInstanceId == frame.SkillInstanceId && movement.Batch.Movements.Count == 1 &&
                movement.Batch.Movements[0].CardId == cardId && movement.Batch.Movements[0].To == receipt.Pile.Location &&
                movement.Batch.Movements[0].Sequence > receipt.Before && movement.Batch.Movements[0].Sequence <= receipt.After &&
                Enumerable.Range(0, 4).All(s => PileCards(game, s, skill).Any(c => c.Id == cardId)),
            "The paid one-card store waits on its real movement child, with exact source, instance, damage parent and public entity evidence. " + JsonSerializer.Serialize(receipt));
        Frozen(receipt.PaidCardIds); Frozen(receipt.PaidFrom); Frozen(movement.Batch.Movements); game = Cold(game, registry);
        Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(game, _ => !game.ResolutionStack.Any(f => f.Id == id));
        Require(Facts<PublicPileCashOutStartedEvent>(game).Count(e => e.FrameId == id && e.Operation == SkillProgramEffectOp.StoreBoundCardsInPublicPile) == 1 &&
                Facts<PublicPileCashOutPaidEvent>(game).Count(e => e.FrameId == id && e.FrozenCount == 1) == 1 &&
                Facts<PublicPileCashOutResolvedEvent>(game).Single(e => e.FrameId == id) is { FrozenCount: 1, DrawActual: 0, SlashLimitGranted: 0 } &&
                game.CardMovements.Count(m => m.Reason.Value == StoreReason(skill) && m.CardId == cardId) == 1,
            "A completed storage child returns once without paying again, drawing cards or granting quota.");
        return game;
    }
    private static string StoreReason(string skill) => $"skill-program.{skill}.StoreBoundCardsInPublicPile.payment";
    private static string PaymentReason(string skill) => $"skill-program.{skill}.CashOutPublicPile.payment";
    private static string DrawReason(string skill) => $"skill-program.{skill}.CashOutPublicPile.draw";
    private static IReadOnlyList<CardSnapshot> PileCards(GameEngine game, int viewer, string skill)
    {
        var owner = game.CreateSnapshot(viewer).Players[0];
        return owner.PublicPersistentPiles is { } sources
            ? sources.Single(s => s.SourceSkillId == skill).Cards
            : owner.PublicPersistentPileSkillId == skill ? owner.PublicPersistentPileCards ?? [] : [];
    }
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool IsHumanPlay(PendingDecision? p) => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    private static bool IsOffer(PendingDecision? p, string skill) => p is not null && p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool IsOwnedPicker(PendingDecision? p) => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards") == true;
    private static bool IsContinue(PendingDecision? p, string skill) => p is not null && p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static T[] Facts<T>(GameEngine game) => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void UseDriver(GameEngine game, string activation) => Accept(game,
        new UseProgramSkillCommand(0, Driver, activation, [], [], game.Revision, P(game)!.PromptId));
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate) => Accept(game,
        new AnswerPromptCommand(P(game)!.PlayerSeat, P(game)!.PromptId, P(game)!.Choices.First(predicate).Id, game.Revision));
    private static void Reach(GameEngine game, Func<PendingDecision?, bool> stop)
    {
        for (var i = 0; i < 256; i++)
        {
            var prompt = P(game);
            if (stop(prompt)) return;
            if (prompt is { PlayerSeat: 0 })
            {
                if (prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
                else throw new InvalidOperationException("Unexpected actual human cash-out boundary: " + JsonSerializer.Serialize(new
                {
                    Prompt = prompt, game.State.TurnNumber,
                    Damage = Facts<DamageAppliedEvent>(game).Where(e => e.TargetSeat == 0).TakeLast(6).ToArray(),
                    Bindings = Facts<ProgramBindingStartedEvent>(game).Where(e => e.OwnerSeat == 0).TakeLast(8).ToArray(),
                    Paid = Facts<PublicPileCashOutPaidEvent>(game).TakeLast(6).ToArray()
                }));
            }
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The bounded cash-out fixture missed its real native boundary: " + JsonSerializer.Serialize(P(game)));
    }
    private static void AssertPrivate(GameEngine game, int actor) => Require(P(game) is { IsPrivate: true } &&
        Enumerable.Range(0, 4).Where(s => s != actor).All(s => game.CreateSnapshot(s).PendingDecision is null && game.CreateSnapshot(s).Players[actor].Hand.Count == 0),
        "Only the actual owner sees the private unpaid HE draft and its child choices; all viewers retain public pile faces.");
    private static void Frozen<T>(IReadOnlyList<T> list)
    {
        Require(list.Count > 0, "The collection immutability assertion needs real entities or choices.");
        var frozen = false; try { ((IList<T>)list)[0] = list[0]; } catch (NotSupportedException) { frozen = true; }
        Require(frozen, "A committed receipt, event batch or nested prompt collection must reject observer mutation.");
    }
    private static void RejectWrongActor(GameEngine game) => Reject(game, new AnswerPromptCommand(1, P(game)!.PromptId, P(game)!.Choices.First().Id, game.Revision));
    private static void RejectUnpublished(GameEngine game) => Reject(game, new AnswerPromptCommand(0, P(game)!.PromptId, new ChoiceId("cash-out:not-published"), game.Revision));
    private static void Accept(GameEngine game, GameCommand command)
    { var r = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(r.Accepted, r.Error?.Message ?? "The real serialized command was rejected."); }
    private static void Reject(GameEngine game, GameCommand command)
    {
        var before = State(game); var r = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(!r.Accepted && r.Error is not null && State(game) == before, "Wrong actors, stale/private choices and exhausted Slash quota reject without changing costs, views, parent cursors or command history.");
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack), game.CardMovements,
        Events = game.Events.Select(e => $"{e.Sequence}|{JsonSerializer.Serialize(e.Payload, e.Payload.GetType())}").ToArray(),
        Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics()
    });
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(copy) == State(game), "Accepted-command cold replay preserves each real paid stage, exact source instance, immutable invoice, parent cursor, quota and private player view."); return copy;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string Rules(bool nested = false) => $$$"""
    {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
      {"id":"{{{Skill}}}","revision":1,"triggers":[
        {"id":"store-one-per-point","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamagePoint","optional":true,"effects":[
          {"op":"selectOwnedCards","target":"owner","minimumCards":1,"maximumCards":1,"zones":["hand","equipment"],"resultBind":"feng-cost"},
          {"op":"storeBoundCardsInPublicPile","target":"owner","sourceBind":"feng-cost"}]},
        {"id":"cash-out-own-preparation","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,
          "condition":{"kind":"compare","left":{"kind":"currentOwnedZoneCount","zone":"publicPersistentPile"},"operator":"greaterThan","right":{"kind":"integerConstant","value":0}},
          "effects":[{"op":"cashOutPublicPile","target":"owner","drawMultiplier":2}]}]},
      {"id":"{{{Driver}}}","revision":1,"viewAs":[{"id":"actual-slash","inputKinds":["dodge"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"extendedUse":true,"sourceZones":["hand"]}],"activations":[
        {"id":"damage-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":2}]},
        {"id":"source-less-damage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":1}]},
        {"id":"turn-over","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"turnOver","target":"owner"}]}]},
      {"id":"{{{StoreChild}}}","revision":1,"triggers":[{"id":"store-payment","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["{{{StoreReason(Skill)}}}","{{{StoreReason(Actual)}}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"store-child","options":[{"id":"continue"}]}]}]},
      {"id":"{{{PaymentChild}}}","revision":1,"states":[{"id":"payment-child-issued","initialValue":false,"visibility":"private","resetScope":"game","reacquirePolicy":"preserveUntilGameEnd"}],"triggers":[{"id":"real-pile-discard","window":"discardPileReceived","subject":"owner","discardOwnerScope":"own","movementReasons":["{{{PaymentReason(Skill)}}}","{{{PaymentReason(Actual)}}}"],"optional":false,"evaluateConditionAtResolution":true,"condition":{"kind":"booleanState","stateId":"payment-child-issued","expectedValue":false},
        "effects":[{"op":"setBooleanState","target":"owner","stateId":"payment-child-issued","value":true},{"op":"chooseOption","target":"owner","resultBind":"payment-child","options":[{"id":"continue"}]} {{{(nested ? ", {\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}" : "")}}} ]}]},
      {"id":"{{{DrawChild}}}","revision":1,"states":[{"id":"draw-child-issued","initialValue":false,"visibility":"private","resetScope":"game","reacquirePolicy":"preserveUntilGameEnd"}],"triggers":[{"id":"native-cash-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{{DrawReason(Skill)}}}","{{{DrawReason(Actual)}}}"],"optional":false,"evaluateConditionAtResolution":true,"condition":{"kind":"booleanState","stateId":"draw-child-issued","expectedValue":false},
        "effects":[{"op":"setBooleanState","target":"owner","stateId":"draw-child-issued","value":true},{"op":"chooseOption","target":"owner","resultBind":"draw-child","options":[{"id":"continue"}]},{"op":"draw","target":"owner","amount":1}]}]},
      {"id":"{{{HpChild}}}","revision":1,"triggers":[{"id":"real-armor-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-child","options":[{"id":"continue"}]}]}]},
      {"id":"{{{Other}}}","revision":1,"triggers":[{"id":"legacy-game-start-hand-storage","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"selectOwnedCards","target":"owner","amount":1,"zones":["hand"],"resultBind":"other-card"},{"op":"storeBoundHandInPublicPile","target":"owner","sourceBind":"other-card"}]}]}]}
    """;
    private static string Presentation() => JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
        skills = new[] { Skill, Driver, StoreChild, PaymentChild, DrawChild, HpChild, Other }.ToDictionary(id => id, id => new
        { name = id, description = "真实公开牌堆存入与准备兑现", authorityName = id == Skill || id == Other ? "锋" : null,
            optionLabels = id is StoreChild or PaymentChild or DrawChild or HpChild ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) },
        new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
    private static (GameEngine, ContentRegistry) Start(bool actual = false, bool armor = false, bool nested = false, bool drawObserver = false, bool other = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(actual, armor, nested, drawObserver, other));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, P(game)!.PromptId));
        if (other) { Reach(game, IsOwnedPicker); Answer(game, c => c.Cards.Count == 1); }
        Reach(game, IsHumanPlay); return (game, registry);
    }
    private sealed class Fixture(bool actual, bool armor, bool nested, bool drawObserver, bool other) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("public-pile-cash-out-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(Rules(nested), Presentation());
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, id, "公共机制真实帧") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Owner, "真实锋牌本人", "supporter", actual ? Actual : Skill, "wei", 8,
                [Driver, StoreChild, PaymentChild, HpChild, .. (drawObserver ? new[] { DrawChild } : []), .. (other ? new[] { Other } : [])]) { InitialHp = 6 });
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:cash-out-peer-{i}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "固定普通参与者", "supporter", "standard:none", "wei", 8));
            builder.AddDeck(new("fixture:cash-out-deck", "固定真实费用实体", 13, 0, []) { PhysicalCards = Enumerable.Range(0, 144).Select(_ =>
                new ContentDeckPhysicalCard(armor ? "classic:silver-lion" : "standard:dodge", Suit.Heart, 7)).ToArray() });
            builder.AddMode(new(Mode, "公开牌堆准备兑现", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:cash-out-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
