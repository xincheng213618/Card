using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

// Focused drafts only: no compiler, loader, game or test execution was performed.
internal static class PublicPilePreparationChecks
{
    private const string Owner = "fixture:preparation-owner", Observe = "fixture:preparation-observe", Attack = "fixture:preparation-attack";
    private const string Mode = "identity:classic-public-pile-preparation";

    public static void EmptyPreparationWorksWithoutTheReferencedSource()
    {
        var (fill, registry) = Start(empty: true);
        var max = fill.State.Players[0].MaxHp;
        Require(fill.State.Players[0].Skills?.Any(s => s.Id == "ol:yinbing") != true, "Juedi is independently owned with no paired Yinbing grant.");
        Require(PileChoice(fill) && P(fill)!.Choices.Any(c => Action(c) == "discard") &&
            P(fill).Choices.Any(c => c.Targets.SequenceEqual([1])), "Zero cards still publish both legal branches.");
        Cold(fill, registry);
        Choose(fill, c => Action(c) == "discard"); ReachPlay(fill);
        var paid = Facts<PublicPilePreparationPaidEvent>(fill).Single();
        var draw = Facts<PublicPilePreparationDrawIssuedEvent>(fill).Single();
        Require(paid.Pile is null && paid.CardIds.Count == 0 && paid.Before == paid.After &&
            fill.CreateSnapshot(0).Players[0].Hand.Count == max && draw.Requested == max - 2 && draw.Actual == max - 2,
            "The zero-card payment creates no fake movement and fills then-current maximum HP.");
        Cold(fill, registry);

        var (gift, giftRegistry) = Start(empty: true);
        var hand = gift.CreateSnapshot(1).Players[1].Hand.Count;
        Choose(gift, c => c.Targets.SequenceEqual([1])); ReachPlay(gift);
        Require(Facts<PublicPilePreparationPaidEvent>(gift).Single() is { Branch: 2, CardIds.Count: 0 } &&
            Facts<PublicPilePreparationRecoveryIssuedEvent>(gift).Single() is { BeneficiarySeat: 1, Requested: 1 } &&
            Facts<PublicPilePreparationDrawIssuedEvent>(gift).Single() is { Requested: 0, Actual: 0 } &&
            gift.CreateSnapshot(1).Players[1].Hand.Count == hand,
            "The full-HP beneficiary receives an actual requested recovery and zero draw without a fabricated card transfer.");
        Cold(gift, giftRegistry);
        RealSlashRemovesExactlyOne();
    }

    private static void RealSlashRemovesExactlyOne()
    {
        var (game, registry) = Start(attackObserver: true);
        Choose(game, c => Action(c) == "discard"); ReachPlay(game);
        var stored = StoreWholeHand(game, registry);
        var hp = game.State.Players[0].Hp;
        Reach(game, () => P(game)?.SkillPrompt?.SkillId == Attack);
        Choose(game, c => c.Targets.SequenceEqual([0]));
        Reach(game, () => PileChoice(game) && game.ResolutionStack.LastOrDefault() is ProgramSkillFrame
            { PublicPilePreparation.Operation: SkillProgramEffectOp.RemovePublicPileAfterAttackDamage });
        var remove = (ProgramSkillFrame)game.ResolutionStack.Last();
        var receipt = remove.PublicPilePreparation!;
        Require(game.State.Players[0].Hp == hp - 1 &&
            Facts<CardUseDeclaredEvent>(game).Any(e => e.SourceSeat == 1 && e.CardKind == CardKind.Slash) &&
            Facts<DamageRequestedEvent>(game).Any(e => e.SourceSeat == 1 && e.TargetSeat == 0 && e.Amount == 1 && e.SourceCard == CardKind.Slash) &&
            Facts<DamageAppliedEvent>(game).Any(e => e.SourceSeat == 1 && e.TargetSeat == 0 && e.Amount == 1) &&
            P(game)!.Choices.Count == stored.Length && P(game).Choices.All(c => c.Cards.Count == 1),
            "A real virtual Slash reaches actual damage and the mandatory public one-card removal, rather than an untyped direct HP effect.");
        Cold(game, registry);
        var id = stored[0]; Choose(game, c => c.Cards.SequenceEqual([id]));
        Reach(game, () => !game.ResolutionStack.Any(f => f.Id == remove.Id) &&
            Facts<AfterDamageEvent>(game).Any(e => e.SourceSeat == 1 && e.TargetSeat == 0 && e.Amount == 1));
        var paid = Facts<PublicPilePreparationPaidEvent>(game).Single(e => e.ProgramFrameId == remove.Id);
        Require(paid.CardIds.SequenceEqual([id]) && paid.From.SequenceEqual([receipt.Pile!.Location]) &&
            game.CardMovements.Count(m => m.From == receipt.Pile.Location && m.To == CardLocation.DiscardPile) == 1 &&
            Enumerable.Range(0, 4).All(s => game.CreateSnapshot(s).Players[0].PublicPersistentPileCards?.Select(c => c.Id).SequenceEqual(stored.Skip(1)) == true),
            "Exactly the chosen original pile entity is discarded once, leaving every other public card in order.");
        Cold(game, registry);
    }

    public static void WholePileFillRetainsPaidChild()
    {
        var (game, registry) = Start(fillObserver: true);
        Choose(game, c => Action(c) == "discard"); ReachPlay(game);
        var stored = StoreWholeHand(game, registry);
        Reach(game, () => PileChoice(game) && game.State.TurnNumber > 1);
        Choose(game, c => Action(c) == "discard");
        Reach(game, () => P(game)?.SkillPrompt?.SkillId == Observe);
        var root = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.PublicPilePreparation is
            { Stage: PublicPilePreparationStage.DrawChildren });
        var receipt = root.PublicPilePreparation!;
        Require(root.SkillId == "ol:juedi" && receipt.Branch == 1 && receipt.PaidIds.SequenceEqual(stored) &&
            receipt.DrawRequested == game.State.Players[0].MaxHp && root.PendingMovementContinuation is not null,
            "ALL actual pile cards leave before the one issued fill batch, whose child retains its typed producer.");
        var window = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.ParentFrameId == root.Id);
        Require(window.ResumeProgramFrameId is null && window.Batch.AwaitingProgramFrameId == root.Id &&
            window.Batch.OriginSkillId == root.SkillId && window.Batch.OriginSkillInstanceId == root.SkillInstanceId &&
            window.Batch.Movements.All(m => m.Sequence > receipt.DrawBefore && m.Sequence <= receipt.DrawAfter),
            "The paid draw child uses only Parent=root, Resume=null, Awaiting=root and its exact physical invoice.");
        Frozen(receipt.PaidIds); Frozen(receipt.PaidFrom);
        Frozen(Facts<PublicPilePreparationPaidEvent>(game).Last().CardIds);
        Cold(game, registry); RejectStale(game);
        Choose(game, _ => true); ReachPlay(game);
        Require(game.State.Players[0].Hp == game.State.Players[0].MaxHp - 1 &&
            game.CreateSnapshot(0).Players[0].Hand.Count == game.State.Players[0].MaxHp + 1 &&
            Facts<PublicPilePreparationDrawIssuedEvent>(game).Count(e => e.ProgramFrameId == root.Id) == 1 &&
            game.CardMovements.Count(m => stored.Contains(m.CardId) && m.From == receipt.Pile!.Location && m.To == CardLocation.DiscardPile) == stored.Length,
            "The paid draw child really draws one and loses one HP, then returns without paying the whole pile twice.");
        Cold(game, registry);
    }

    public static void GiftChildDrainsBeforeRequestedRecoveryAndOriginalQuantityDraw()
    {
        var (game, registry) = Start(giftObserver: true);
        Choose(game, c => Action(c) == "discard"); ReachPlay(game);
        var stored = StoreWholeHand(game, registry);
        Reach(game, () => PileChoice(game) && game.State.TurnNumber > 1);
        var hand = game.CreateSnapshot(1).Players[1].Hand.Count;
        var hp = game.State.Players[1].Hp;
        Choose(game, c => c.Targets.SequenceEqual([1]));
        Reach(game, () => P(game)?.SkillPrompt?.SkillId == Observe);
        var root = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.PublicPilePreparation is
            { Stage: PublicPilePreparationStage.PaymentChildren, Branch: 2 });
        var receipt = root.PublicPilePreparation!;
        Require(receipt.FrozenCount == stored.Length && receipt.PaidIds.SequenceEqual(stored) &&
            game.CreateSnapshot(1).Players[1].Hand.Count == hand + stored.Length &&
            !Facts<PublicPilePreparationRecoveryIssuedEvent>(game).Any(e => e.ProgramFrameId == root.Id) &&
            !Facts<PublicPilePreparationDrawIssuedEvent>(game).Any(e => e.ProgramFrameId == root.Id),
            "The real full gift is paid, while recovery and the original-X draw wait for its child.");
        Cold(game, registry); Choose(game, _ => true); ReachPlay(game);
        Require(Facts<PublicPilePreparationRecoveryIssuedEvent>(game).Single(e => e.ProgramFrameId == root.Id).Requested == 1 &&
            Facts<PublicPilePreparationDrawIssuedEvent>(game).Single(e => e.ProgramFrameId == root.Id).Requested == stored.Length &&
            game.State.Players[1].Hp == hp && game.CreateSnapshot(1).Players[1].Hand.Count == hand + 2 * stored.Length + 1,
            "The gift child really draws one and loses one HP; recovery restores that HP before exactly original-X cards are drawn.");
        Require(game.CardMovements.Count(m => stored.Contains(m.CardId) && m.From == receipt.Pile!.Location && m.To == CardLocation.Hand(1)) == stored.Length,
            "Every original entity reaches its selected beneficiary once.");
        Cold(game, registry);
    }

    private static (GameEngine Game, ContentRegistry Registry) Start(bool empty = false, bool fillObserver = false, bool giftObserver = false, bool attackObserver = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(empty, fillObserver, giftObserver, attackObserver));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, P(game)!.PromptId));
        Reach(game, () => PileChoice(game)); return (game, registry);
    }
    private static int[] StoreWholeHand(GameEngine game, ContentRegistry registry)
    {
        var ids = game.CreateSnapshot(0).Players[0].Hand.Select(c => c.Id).ToArray();
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, P(game)!.PromptId));
        Reach(game, () => P(game)?.SkillPrompt?.SkillId == "ol:yinbing" &&
            P(game)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        Choose(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(game, () => PileChoice(game) && game.ResolutionStack.LastOrDefault() is ProgramSkillFrame
            { PublicPilePreparation.Operation: SkillProgramEffectOp.StoreNonBasicOwnedPublicPile });
        Require(game.CreateSnapshot(2).PendingDecision is null, "The other viewer cannot see the private HE picker.");
        foreach (var id in ids) Choose(game, c => c.Cards.SequenceEqual([id]));
        Require(P(game)!.Choices.Count == 1 && Action(P(game).Choices.Single()) == "finish",
            "The linear picker permits every payable card with no subset explosion or artificial cap.");
        Choose(game, c => Action(c) == "finish");
        Require(Enumerable.Range(0, 4).All(s => game.CreateSnapshot(s).Players[0].PublicPersistentPileCards?.Select(c => c.Id).SequenceEqual(ids) == true),
            "Each viewer sees the same actual public pile after payment.");
        Frozen(game.CreateSnapshot(2).Players[0].PublicPersistentPileCards!); Cold(game, registry); return ids;
    }
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool PileChoice(GameEngine game) => P(game)?.Choices.Any(c =>
        c.Parameters.GetValueOrDefault("program-action") == "public-pile-preparation") == true;
    private static string? Action(PromptChoice choice) => choice.Parameters.GetValueOrDefault("pile-action");
    private static void ReachPlay(GameEngine game) => Reach(game, () => P(game) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine game, Func<bool> stop)
    {
        for (var i = 0; i < 512 && !stop(); i++)
        {
            if (P(game) is { } p)
            {
                Require(p.Kind != DecisionKind.PlayCard, "One bounded round must not enter an unrelated published Play phase.");
                {
                    var choice = p.Choices.FirstOrDefault(c => c.Cards.Count == 0 && c.Targets.Count == 0 &&
                        (c.Id.Value.Contains("skip", StringComparison.Ordinal) || c.Id.Value.Contains("pass", StringComparison.Ordinal))) ?? p.Choices.First();
                    Choose(game, c => c.Id == choice.Id);
                }
            }
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        Require(stop(), "The finite homogeneous fixture must reach its exact named window.");
    }
    private static void Choose(GameEngine game, Func<PromptChoice, bool> pick)
    {
        var p = P(game)!; var choice = p.Choices.First(pick);
        Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice.Id, game.Revision));
    }
    private static T[] Facts<T>(GameEngine game) => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void RejectStale(GameEngine game)
    {
        var before = GameCheckpointJson.Serialize(game.CreateCheckpoint()); var p = P(game)!;
        var result = game.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("fixture:not-published"), game.Revision));
        Require(!result.Accepted && before == GameCheckpointJson.Serialize(game.CreateCheckpoint()), "Unpublished choices reject atomically.");
    }
    private static void Cold(GameEngine game, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, 4).All(s => SnapshotJson.Serialize(game.CreateSnapshot(s)) == SnapshotJson.Serialize(copy.CreateSnapshot(s))) &&
            game.CardMovements.SequenceEqual(copy.CardMovements) && game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).SequenceEqual(
                copy.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType()))), "Cold accepted-command restoration preserves all viewers and actual journal facts.");
    }
    private static void Frozen<T>(IReadOnlyList<T> values)
    {
        if (values is IList<T> list && list.Count > 0) { try { list[0] = list[0]; } catch (NotSupportedException) { return; } }
        throw new InvalidOperationException("A nonempty exposed collection must reject mutation.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var r = game.Submit(command); Require(r.Accepted, r.Error?.Message ?? "Real command accepted."); }
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException(text); }

    private sealed class Fixture(bool empty, bool fillObserver, bool giftObserver, bool attackObserver) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:public-pile-preparation", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var ending = "skill-program.ol:juedi.public-pile-preparation.draw";
            var gift = "skill-program.ol:juedi.public-pile-preparation.payment";
            var tail = "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1},{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}";
            var rules = $$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Observe}}","revision":1,"triggers":[{"id":"paid-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{(fillObserver ? ending : gift)}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]},{{tail}}]}]},{"id":"{{Attack}}","revision":1,"triggers":[{"id":"actual-slash","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingVirtualSlashTarget"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","useCardActionWindows":true,"targetRestriction":"distanceUnlimitedAgainstTarget"}]}]}]}""";
            var catalog = SkillProgramCatalog.Load(rules, $$"""{"schemaVersion":3,"skills":{"{{Observe}}":{"name":"付款观察","description":"子流程返回后再推进","optionLabels":{"continue":"继续"} },"{{Attack}}":{"name":"真实杀伤","description":"真实准备阶段对选定目标使用杀"} } }""");
            foreach (var skill in catalog.Programs) builder.AddSkill(new(skill.Key, skill.Key, skill.Key) { Program = skill.Value });
            builder.AddGeneral(new(Owner, "公开牌区拥有者", "supporter", empty ? "ol:juedi" : "ol:yinbing", "wu", 4,
                empty ? [] : fillObserver ? ["ol:juedi", Observe] : ["ol:juedi"]));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:preparation-peer-" + i).ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "固定受益者", "supporter", "standard:none", "wei", 4,
                attackObserver ? [Attack] : giftObserver ? [Observe] : []));
            // All 80 physical cards share kind/suit/rank, so the actual setup shuffle
            // cannot break the recipe. Only one bounded round is crossed.
            builder.AddDeck(new("fixture:preparation-deck", "固定非基本牌", empty ? 2 : 5, 0, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:crossbow", Suit.Club, 7)).ToArray() });
            builder.AddMode(new(Mode, "公开牌区准备机制", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:preparation-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
