using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ForeignEndingPaymentChecks
{
    private const int Recipient = 1;
    private const string Skill = "ol:xiaoguo";
    private const string Movement = "fixture:foreign-payment-movement";
    private const string Recovery = "fixture:foreign-payment-recovery";
    private const string Gain = "fixture:foreign-payment-gain";
    private const string PaymentReason = "skill-program.ol:xiaoguo.SelectAndMoveOwnedCard";
    private const string DrawReason = "skill-program.ol:xiaoguo.Draw";
    private const string Mode = "identity:classic-foreign-ending-payment-fixture";

    public static void ForeignOwnerCostAndEquipmentResponseWaitOnce()
    {
        foreach (var scenario in new[] { "hand-equipment", "equipped-lion", "decline", "no-equipment" })
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario == "no-equipment"));
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 7, PlayerCount = 4, HumanSeat = Recipient, HumanRole = Role.Lord, ModeId = Mode,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
            }, registry);
            Accept(game, new StartGameCommand());
            Reach(game, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: Recipient });
            var hand = game.CreateSnapshot(Recipient).Players[Recipient].Hand;
            var equipment = scenario == "no-equipment" ? -1 :
                hand.FirstOrDefault(c => c.Kind == CardKind.SilverLion)?.Id ??
                throw new InvalidOperationException("The fixed seed must deal the recipient a real equipment card.");
            if (scenario == "equipped-lion")
            {
                Accept(game, new PlayCardCommand(Recipient, equipment, [], game.Revision, P(game)!.PromptId));
                Reach(game, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: Recipient });
                Require(game.State.Players[Recipient].Hp < game.State.Players[Recipient].MaxHp &&
                        game.State.Players[Recipient].Equipment.Any(c => c.Id == equipment),
                    "The wounded recipient must equip Silver Lion through its real card-use command.");
            }
            var recipientHp = game.State.Players[Recipient].Hp;
            Accept(game, new EndPlayPhaseCommand(Recipient, game.Revision, P(game)!.PromptId));
            Reach(game, p => p.SkillPrompt?.SkillId == Skill && p.Choices.Any(c => Action(c) == "activate"));
            var owner = P(game)!.PlayerSeat;
            Require(owner != Recipient && !game.State.Players[owner].IsHuman,
                "The skill owner must be a real foreign AI character during the human recipient's turn.");
            var ownerHandBefore = game.CreateSnapshot(owner, true).Players[owner].Hand.Count;
            // Drive a published AI-owner offer explicitly: this checks the real
            // composite resolution independently of optional activation pricing.
            Answer(game, c => Action(c) == "activate");
            Reach(game, p => p.SkillPrompt?.SkillId == Skill && p.Choices.Any(c => c.Cards.Count == 1));
            var basic = game.CreateSnapshot(owner, true).Players[owner].Hand.First(c => c.Kind == CardKind.Dodge).Id;
            Require(P(game)!.PlayerSeat == owner && P(game)!.IsPrivate &&
                    P(game)!.Choices.All(c => c.Cards.Count == 1 &&
                        game.CreateSnapshot(owner, true).Players[owner].Hand.Any(card =>
                            card.Id == c.Cards[0] && card.Kind == CardKind.Dodge)),
                "The owner must first pay only one of its own basic hand cards.");
            WrongOwner(game, Recipient);
            Answer(game, c => c.Cards.SequenceEqual([basic]));
            Reach(game, p => p.SkillPrompt?.SkillId == Movement);
            PaidMovement(game, owner, basic, CardLocation.Hand(owner));
            Require(PaymentCount(game) == 1 && DrawCount(game) == 0 && !E<DamageAppliedEvent>(game).Any(),
                "The owner's basic cost must commit before the foreign response or any benefit.");
            var replay = Restore(game, registry);
            var expected = Finish(game, registry, scenario, owner, basic, equipment, recipientHp, ownerHandBefore, false);
            var cold = Finish(replay, registry, scenario, owner, basic, equipment, recipientHp, ownerHandBefore, true);
            Require(State(expected) == State(cold),
                $"The {scenario} response must finish identically after cold restoration at its paid child boundaries.");
        }
    }

    private static GameEngine Finish(GameEngine game, ContentRegistry registry, string scenario, int owner,
        int basic, int equipment, int recipientHp, int ownerHandBefore, bool restoreChildren)
    {
        Continue(game);
        Reach(game, p => p.SkillPrompt?.SkillId == Skill && p.Choices.Any(c => c.Targets.SequenceEqual([Recipient])));
        Require(P(game)!.PlayerSeat == owner, "The target binding remains controlled by the skill owner.");
        Answer(game, c => c.Targets.SequenceEqual([Recipient]));
        Reach(game, p => p.SkillPrompt?.SkillId == Skill && p.Choices.Any(c => Option(c) == "take-damage"));
        var response = P(game)!;
        Require(response.PlayerSeat == Recipient &&
                response.Choices.Any(c => Option(c) == "discard-equipment") == (scenario != "no-equipment") &&
                PaymentCount(game) == 1,
            "The actual turn recipient controls the response, and equipment availability includes its hand.");
        WrongOwner(game, owner);
        if (restoreChildren) game = Restore(game, registry);
        var paid = scenario is "hand-equipment" or "equipped-lion";
        Answer(game, c => Option(c) == (paid ? "discard-equipment" : "take-damage"));
        if (paid)
        {
            Reach(game, p => p.SkillPrompt?.SkillId == Skill && p.Choices.Any(c => c.Cards.Contains(equipment)));
            var payment = P(game)!;
            var owned = game.CreateSnapshot(Recipient).Players[Recipient];
            Require(payment is { PlayerSeat: Recipient, IsPrivate: true } &&
                    payment.Choices.All(c => c.Cards.Count == 1 &&
                        owned.Hand.Concat(owned.Equipment).Any(card => card.Id == c.Cards[0] &&
                            EquipmentCatalog.IsEquipment(card.Kind))),
                "Equipment payment is a private choice by its actual owner over hand and equipment cards.");
            WrongOwner(game, owner);
            Answer(game, c => c.Cards.SequenceEqual([equipment]));
            if (scenario == "equipped-lion")
            {
                Reach(game, p => p.SkillPrompt?.SkillId == Recovery);
                var root = Root(game, owner);
                var hp = game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
                Require(root.PendingMovementContinuation?.SubjectSeat == Recipient && hp.ResumeFrameId == root.Id &&
                        hp.Change.TargetSeat == Recipient && hp.Change.Kind == HpChangeKind.Recovery &&
                        hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                        game.State.Players[Recipient].Hp == recipientHp + 1 &&
                        PaymentCount(game) == 2 && DrawCount(game) == 0 &&
                        !E<DamageAppliedEvent>(game).Any(),
                    "The paid foreign Lion must recover through the real awaited HP child before movement and reward.");
                if (restoreChildren) game = Restore(game, registry);
                Continue(game);
            }
            Reach(game, p => p.SkillPrompt?.SkillId == Movement);
            PaidMovement(game, owner, equipment, scenario == "equipped-lion"
                ? CardLocation.Equipment(Recipient) : CardLocation.Hand(Recipient));
            Require(PaymentCount(game) == 2 && DrawCount(game) == 0 && !E<DamageAppliedEvent>(game).Any(),
                "Both costs must finish their own movement children before the single-card reward.");
            if (restoreChildren) game = Restore(game, registry);
            Continue(game);
            Reach(game, p => p.SkillPrompt?.SkillId == Gain);
            Require(DrawCount(game) == 1 && PaymentCount(game) == 2 && !E<DamageAppliedEvent>(game).Any(),
                "The reward commits once before its real gained-card observer returns.");
            if (restoreChildren) game = Restore(game, registry);
            Continue(game);
        }
        Until(game, () => E<ProgramBindingResolvedEvent>(game).Any(e =>
            e.SkillId == Skill && e.OwnerSeat == owner && e.Activated && e.Completed));
        var costs = game.CardMovements.Where(m => m.Reason.Value == PaymentReason).ToArray();
        Require(costs.Count(m => m.CardId == basic && m.From == CardLocation.Hand(owner) &&
                    m.To == CardLocation.DiscardPile) == 1 &&
                costs.Length == (paid ? 2 : 1) &&
                E<ProgramBindingResolvedEvent>(game).Count(e =>
                    e.SkillId == Skill && e.OwnerSeat == owner && e.Activated && e.Completed) == 1,
            "The original owner cost and any foreign payment must each move exactly once after all children return.");
        var damage = E<DamageAppliedEvent>(game).ToArray();
        Require(paid
                ? DrawCount(game) == 1 && damage.Length == 0 &&
                  game.CreateSnapshot(owner, true).Players[owner].Hand.Count == ownerHandBefore &&
                  costs.Count(m => m.CardId == equipment && m.From.OwnerSeat == Recipient &&
                      m.To == CardLocation.DiscardPile) == 1 &&
                  game.State.Players[Recipient].Hp == recipientHp + (scenario == "equipped-lion" ? 1 : 0)
                : DrawCount(game) == 0 && damage is [var hit] &&
                  hit.SourceSeat == owner && hit.TargetSeat == Recipient && hit.Amount == 1 && !hit.SourceLess &&
                  game.State.Players[Recipient].Hp == recipientHp - 1 &&
                  game.CreateSnapshot(owner, true).Players[owner].Hand.Count == ownerHandBefore - 1,
            "Paying equipment grants only one owner draw; refusing or lacking equipment deals only owner-sourced damage.");
        return game;
    }

    private static void PaidMovement(GameEngine game, int owner, int card, CardLocation source)
    {
        var root = Root(game, owner);
        var child = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>()
            .Single(w => w.Batch.Movements.Any(m => m.CardId == card && m.Reason.Value == PaymentReason));
        Require(root.PendingMovementContinuation?.SubjectSeat == source.OwnerSeat &&
                child.Batch.Movements is [var actual] && actual.From == source &&
                actual.To == CardLocation.DiscardPile && child.Batch.ParentFrameId == root.Id &&
                game.CardMovements.Count(m => m.CardId == card && m.Reason.Value == PaymentReason) == 1,
            "The owning program must retain its paid participant and exact real movement child.");
    }

    private static ProgramSkillFrame Root(GameEngine game, int owner) => game.ResolutionStack
        .OfType<ProgramSkillFrame>().Single(f => f.SkillId == Skill && f.OwnerSeat == owner);
    private static IEnumerable<T> E<T>(GameEngine game) => game.Events.Select(e => e.Payload).OfType<T>();
    private static int PaymentCount(GameEngine game) => game.CardMovements.Count(m => m.Reason.Value == PaymentReason);
    private static int DrawCount(GameEngine game) => game.CardMovements.Count(m => m.Reason.Value == DrawReason);
    private static string? Action(PromptChoice choice) => choice.Parameters.GetValueOrDefault("program-action");
    private static string? Option(PromptChoice choice) => choice.Parameters.GetValueOrDefault("option-id");
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4)
        .Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Continue(GameEngine game) => Answer(game, c => Option(c) == "continue");
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    {
        var prompt = P(game) ?? throw new InvalidOperationException("The expected real payment prompt is absent.");
        Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            prompt.Choices.First(predicate).Id, game.Revision));
    }
    private static void WrongOwner(GameEngine game, int wrongSeat)
    {
        var prompt = P(game)!;
        var before = State(game);
        Require(prompt.PlayerSeat != wrongSeat && !game.Submit(new AnswerPromptCommand(wrongSeat,
                prompt.PromptId, prompt.Choices.First().Id, game.Revision)).Accepted && State(game) == before,
            "A different participant cannot answer or pay another participant's published prompt.");
    }
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 100; step++)
        {
            if (P(game) is { } prompt && predicate(prompt)) return;
            Require(P(game) is not { PlayerSeat: Recipient },
                $"Unexpected human prompt while awaiting a payment boundary: {P(game)?.SkillPrompt?.SkillId}/{P(game)?.Kind}.");
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The fixed payment fixture exceeded its bounded advance budget.");
    }
    private static void Until(GameEngine game, Func<bool> completed)
    {
        for (var step = 0; step < 100; step++)
        {
            if (completed()) return;
            Require(P(game) is not { PlayerSeat: Recipient }, "An unexpected human prompt blocked the paid return.");
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The fixed payment fixture did not complete its actual binding.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "A real foreign payment command was rejected.");
    }
    private static GameEngine Restore(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(game) == State(restored), "Four private views, typed frames, movements, history and commands must cold restore exactly.");
        return restored;
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack),
        Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics()
    });
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool noEquipment) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("foreign-ending-payment-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var assembly = typeof(StandardClassicGeneralPackage).Assembly;
            string Read(string suffix)
            {
                using var stream = assembly.GetManifestResourceStream(
                    "CardGame.Content.Standard.SkillPrograms.ordinary-yue-jin." + suffix) ??
                    throw new InvalidOperationException("The actual embedded ordinary program is missing.");
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            var actual = SkillProgramCatalog.Load(Read("rules.json"), Read("presentation.json"));
            builder.AddSkill(new(Skill, "骁果", "实际发布程序")
                { Program = actual.Programs[Skill], ProgramPresentation = actual.Presentations[Skill] });
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                 {"id":"{{Movement}}","revision":1,"triggers":[{"id":"paid","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["{{PaymentReason}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{Recovery}}","revision":1,"triggers":[{"id":"healed","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{Gain}}","revision":1,"triggers":[{"id":"reward","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{DrawReason}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}]}
                """;
            var labels = new[] { Movement, Recovery, Gain }.ToDictionary(id => id,
                id => new { name = id, description = "真实共享付款子窗", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } });
            var observers = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
                { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in observers.Programs)
                builder.AddSkill(new(id, id, "共享复合行为")
                    { Program = program, ProgramPresentation = observers.Presentations[id] });
            builder.AddCard(new("fixture:foreign-payment-lion", "白银狮子", "装备牌", "实际防具支付", CardKind.SilverLion));
            var generals = Enumerable.Range(0, 4).Select(i => $"fixture:foreign-payment-{i}").ToArray();
            foreach (var id in generals)
                builder.AddGeneral(new(id, "共享付款角色", "supporter", Skill, "wei", 12,
                    [Movement, Recovery, Gain]) { InitialHp = 11 });
            builder.AddDeck(new("fixture:foreign-payment-deck", "固定小实体牌堆", 8, 0,
                noEquipment ? [new("standard:dodge", 48)] :
                    [new("standard:dodge", 24), new("fixture:foreign-payment-lion", 24)]));
            builder.AddMode(new(Mode, "实际他人回合付款", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 },
                "fixture:foreign-payment-deck", GeneralCandidateCount: 1, GeneralPoolIds: generals));
        }
    }
}
