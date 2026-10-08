namespace CardGame.Core;

public sealed record ProgramPrivateHandTakeDraft(int SourceSeat, int RequiredCount,
    IReadOnlyList<int> ViewedCardIds, IReadOnlyList<int> SelectedCardIds, bool Paid = false);
public sealed record ProgramHandRepaymentDraft(int SourceSeat, int InstructionIndex,
    bool Paid = false, int? PaidCardId = null, bool LostHp = false);
public sealed record ProgramBoundParticipantGiftReceipt(int InstructionIndex, string SourceBind, int RecipientSeat);
public sealed record ProgramPrivateHandViewedEvent(long FrameId, string SkillId, int ViewerSeat, int SourceSeat, int Count) : IGameEvent;
public sealed record ProgramHandRepaymentResolvedEvent(long FrameId, string SkillId, int SourceSeat, int RecipientSeat,
    bool GaveCard, bool LostHp) : IGameEvent;

public interface IParticipantHandProgramHost
{
    SkillProgramStepOutcome ViewAndTakeHand(ProgramSkillFrame frame, int sourceSeat, int count);
    SkillProgramStepOutcome RequestHandRepayment(ProgramSkillFrame frame, int sourceSeat);
}

internal sealed class ViewAndTakeSelectedTargetHandDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ViewAndTakeSelectedTargetHand;
    public override ISkillProgramEffectHandler Handler { get; } = new ParticipantHandProgramHandler(SkillProgramEffectOp.ViewAndTakeSelectedTargetHand);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.TakeRandomHandCards(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target"); var count = r.RequiredInt("amount");
        if (target != SkillProgramEffectTarget.SelectedTarget || count is < 1 or > 2)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: private hand selection requires selectedTarget and amount 1..2.");
        return new(Op, target, count, r.Condition());
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseEnded), new ReadSelectedTarget()];
}

internal sealed class RequestHandBySuitsOrLoseHpDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RequestHandBySuitsOrLoseHp;
    public override ISkillProgramEffectHandler Handler { get; } = new ParticipantHandProgramHandler(SkillProgramEffectOp.RequestHandBySuitsOrLoseHp);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.LoseHp,
        static (effect, context) => context.LoseHp(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "suits", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target"); var suits = r.RequiredEnumArray<Suit>("suits");
        if (target != SkillProgramEffectTarget.SelectedTarget || suits.Count == 0 || suits.Distinct().Count() != suits.Count ||
            suits.Any(suit => suit == Suit.None))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: hand repayment requires selectedTarget and distinct ordinary suits.");
        var effect = new SkillProgramEffect(Op, target, 1, r.Condition(), suits: suits);
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied), new ReadSelectedTarget()];
}

internal sealed class ParticipantHandProgramHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        op == SkillProgramEffectOp.ViewAndTakeSelectedTargetHand
            ? ((IParticipantHandProgramHost)host).ViewAndTakeHand(frame, targetSeat, effect.Amount)
            : ((IParticipantHandProgramHost)host).RequestHandRepayment(frame, targetSeat);
}

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IParticipantHandProgramHost
    {
        public SkillProgramStepOutcome ViewAndTakeHand(ProgramSkillFrame frame, int sourceSeat, int count) =>
            engine.BeginPrivateHandTake(frame.Id, sourceSeat, count);
        public SkillProgramStepOutcome RequestHandRepayment(ProgramSkillFrame frame, int sourceSeat) =>
            engine.BeginHandRepayment(frame.Id, sourceSeat);
    }

    private IEnumerable<CardSnapshot> GetParticipantPrivatelyViewedCards(int viewerSeat) =>
        _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.OwnerSeat == viewerSeat && f.PrivateHandTake is { Paid: false })
            .SelectMany(f => f.PrivateHandTake!.ViewedCardIds.Select(id =>
                _cardZones.CardsAt(CardLocation.Hand(f.PrivateHandTake.SourceSeat)).Single(c => c.Id == id)))
            .Select(ToSnapshot);

    private SkillProgramStepOutcome BeginPrivateHandTake(long frameId, int sourceSeat, int count)
    {
        var frame = GetActiveProgramFrame(frameId);
        if (frame.PrivateHandTake is not null || sourceSeat == frame.OwnerSeat || frame.SelectedTargetSeats is not [var selected] || selected != sourceSeat)
            throw new InvalidOperationException("Private hand selection lost its distinct selected source.");
        var hand = GetHand(_players[sourceSeat]);
        if (!_players[sourceSeat].IsAlive || hand.Count == 0) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(frame = frame with { PrivateHandTake = new(sourceSeat, Math.Min(count, hand.Count),
            Array.AsReadOnly(hand.Select(c => c.Id).ToArray()), Array.AsReadOnly(Array.Empty<int>())) });
        AdvanceEventRulesAndQueueFact(new ProgramPrivateHandViewedEvent(frame.Id, frame.SkillId, frame.OwnerSeat, sourceSeat, hand.Count));
        PublishParticipantHandChoice(frame, frame.OwnerSeat, PrivateHandTakeChoices(frame), "观看手牌，并选择要获得的牌。", sourceSeat);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> PrivateHandTakeChoices(ProgramSkillFrame frame)
    {
        var draft = frame.PrivateHandTake!;
        return Array.AsReadOnly(draft.ViewedCardIds.Except(draft.SelectedCardIds).Select(id =>
        {
            var card = _cardZones.CardsAt(CardLocation.Hand(draft.SourceSeat)).Single(c => c.Id == id);
            return new PromptChoice(new ChoiceId($"private-hand-take.{frame.Id}.{draft.SelectedCardIds.Count}.{id}"),
                $"获得【{card.DisplayName}】（{card.Suit} {card.RankText}）", [id], [],
                new Dictionary<string, string> { ["program-action"] = "private-hand-take", ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }).ToArray());
    }

    private SkillProgramStepOutcome BeginHandRepayment(long frameId, int sourceSeat)
    {
        var frame = GetActiveProgramFrame(frameId);
        if (frame.HandRepayment is not null || sourceSeat == frame.OwnerSeat || frame.SelectedTargetSeats is not [var selected] || selected != sourceSeat)
            throw new InvalidOperationException("Hand repayment lost its distinct selected source.");
        if (!_players[sourceSeat].IsAlive) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(frame = frame with { HandRepayment = new(sourceSeat, frame.InstructionIndex) });
        PublishParticipantHandChoice(frame, sourceSeat, HandRepaymentChoices(frame), "交给技能拥有者一张符合花色的手牌，或失去1点体力。", frame.OwnerSeat);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private SkillProgramEffect ParticipantHandPaused(ProgramSkillFrame frame) =>
        ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;

    private IReadOnlyList<PromptChoice> HandRepaymentChoices(ProgramSkillFrame frame)
    {
        var draft = frame.HandRepayment!; var effect = ParticipantHandPaused(frame);
        var choices = GetHand(_players[draft.SourceSeat]).Where(c => effect.Suits.Contains(GetProgramEffectiveSuit(_players[draft.SourceSeat], c)))
            .Select(c => new PromptChoice(new ChoiceId($"hand-repay.{frame.Id}.card-{c.Id}"),
                $"交出【{c.DisplayName}】（{c.Suit} {c.RankText}）", [c.Id], [],
                new Dictionary<string, string> { ["program-action"] = "hand-repayment", ["repayment"] = "give", ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) })).ToList();
        choices.Add(new(new ChoiceId($"hand-repay.{frame.Id}.lose-hp"), "失去1点体力", [], [],
            new Dictionary<string, string> { ["program-action"] = "hand-repayment", ["repayment"] = "lose-hp", ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        return Array.AsReadOnly(choices.ToArray());
    }

    private void PublishParticipantHandChoice(ProgramSkillFrame frame, int chooser, IReadOnlyList<PromptChoice> choices, string text, int target)
    {
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, text, Array.AsReadOnly(choices.SelectMany(c => c.Cards).Distinct().ToArray()), [], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = target, Choices = choices, SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveParticipantHandChoice(PromptChoice selected)
    {
        var frame = GetActiveProgramFrame(_resolutionStack.Last().Id); var effect = ParticipantHandPaused(frame);
        if (selected.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The participant hand choice lost its owning frame.");
        if (!_players[frame.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        { ClearPendingDecision(); CancelProgramBindingAndCleanup(frame, "技能拥有者或技能实例已失效，未支付未完成的成本。"); return; }
        if (effect.Op == SkillProgramEffectOp.ViewAndTakeSelectedTargetHand && frame.PrivateHandTake is { Paid: false } take)
        {
            if (_pendingDecision?.PlayerSeat != frame.OwnerSeat || !PrivateHandTakeChoices(frame).Any(c => c.Id == selected.Id) ||
                take.ViewedCardIds.Any(id => _cardZones.GetLocation(id) != CardLocation.Hand(take.SourceSeat)) || !_players[take.SourceSeat].IsAlive)
                throw new InvalidOperationException("The private hand choice no longer matches its frozen source.");
            ClearPendingDecision(); var ids = take.SelectedCardIds.Append(selected.Cards.Single()).ToArray();
            if (ids.Length < take.RequiredCount)
            {
                ReplaceRuntimeTop(frame = frame with { PrivateHandTake = take with { SelectedCardIds = Array.AsReadOnly(ids) } });
                PublishParticipantHandChoice(frame, frame.OwnerSeat, PrivateHandTakeChoices(frame), "继续选择，选齐后同时获得。", take.SourceSeat); return;
            }
            ReplaceRuntimeTop(frame with { PrivateHandTake = take with { SelectedCardIds = Array.AsReadOnly(ids), Paid = true } });
            MoveCards(ids.Select(id => _cardZones.CardsAt(CardLocation.Hand(take.SourceSeat)).Single(c => c.Id == id)).ToArray(),
                CardLocation.Hand(take.SourceSeat), CardLocation.Hand(frame.OwnerSeat), new($"skill-program.{frame.SkillId}.private-hand-take"));
            AdvanceRuntimeProgram(frame.Id); return;
        }
        if (effect.Op != SkillProgramEffectOp.RequestHandBySuitsOrLoseHp || frame.HandRepayment is not { Paid: false } repayment ||
            _pendingDecision?.PlayerSeat != repayment.SourceSeat || !_players[repayment.SourceSeat].IsAlive ||
            !HandRepaymentChoices(frame).Any(c => c.Id == selected.Id))
            throw new InvalidOperationException("The repayment choice is no longer legal for its actual hand owner.");
        ClearPendingDecision(); var give = selected.Parameters.GetValueOrDefault("repayment") == "give";
        ReplaceRuntimeTop(frame with { HandRepayment = repayment with { Paid = true, PaidCardId = give ? selected.Cards.Single() : null, LostHp = !give } });
        AdvanceEventRulesAndQueueFact(new ProgramHandRepaymentResolvedEvent(frame.Id, frame.SkillId, repayment.SourceSeat, frame.OwnerSeat, give, !give));
        if (give)
        {
            var card = GetHand(_players[repayment.SourceSeat]).Single(c => c.Id == selected.Cards.Single());
            MoveCard(card, CardLocation.Hand(repayment.SourceSeat), CardLocation.Hand(frame.OwnerSeat), new($"skill-program.{frame.SkillId}.hand-repayment"));
            AdvanceRuntimeProgram(frame.Id);
        }
        else
        {
            var result = new ProgramSkillHost(this).LoseHp(frame.Id, frame.SkillId, repayment.SourceSeat, 1);
            if (result != SkillProgramStepOutcome.AwaitChild) AdvanceRuntimeProgram(frame.Id);
        }
    }

    private SkillProgramStepOutcome BeginBoundParticipantGift(ProgramSkillFrame frame, string bind, IReadOnlyList<int> ids, CardLocation target, CardMoveReason reason)
    {
        frame = GetActiveProgramFrame(frame.Id);
        ValidateBoundParticipantGiftWindow(frame);
        var source = GetProgramCardSet(frame, bind);
        if (_winner != Winner.None || !_players[frame.OwnerSeat].IsAlive || target.OwnerSeat is not { } recipient ||
            !_players[recipient].IsAlive || !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) ||
            !ids.All(id => source.CardIds.Contains(id)) || ids.Any(id =>
                _cardZones.GetLocation(id) != source.SourceLocations[source.CardIds.ToList().IndexOf(id)]))
        { CancelProgramBindingAndCleanup(frame, "赠牌参与者或冻结来源已失效，未交出牌且不继续请求杀。"); return SkillProgramStepOutcome.AwaitChild; }
        if (frame.BoundParticipantGift is not null || target != CardLocation.Hand(frame.SelectedTargetSeats.Single()) || ids.Count == 0 ||
            ids.Any(id => _cardZones.GetLocation(id).OwnerSeat != frame.OwnerSeat || _cardZones.GetLocation(id).Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)))
            throw new InvalidOperationException("An awaited bound gift lost its unpaid owned HE cost.");
        ReplaceRuntimeTop(frame with { BoundParticipantGift = new(frame.InstructionIndex, bind, target.OwnerSeat!.Value) });
        MoveProgramCardsFromMultipleSources(ids, target, reason);
        AdvanceRuntimeProgram(frame.Id); return SkillProgramStepOutcome.AwaitChild;
    }

    private void ValidateBoundParticipantGiftWindow(ProgramSkillFrame frame)
    {
        if (frame.WindowContext is null && frame.TriggerId is null)
        {
            ValidateOwnedHandRankSumGiftActivation(frame);
            return;
        }
        // The original DrawPhaseEnded producer retains its existing contract.
        // The added action window must keep the exact live parent and candidate
        // throughout all gift movement children; no material use is reissued.
        if (frame.WindowContext?.Window == SkillProgramTriggerWindow.DrawPhaseEnded) return;
        var index = _resolutionStack.FindIndex(item => item.Id == frame.Id);
        if (index < 2 || frame.WindowContext is not
                { Window: SkillProgramTriggerWindow.CardUseBeforeTargetEffects, CardUse: { } cardContext } context ||
            _resolutionStack[index - 1] is not ProgramCardTriggerWindowFrame window ||
            window.Id != context.ParentFrameId || GetCardActionWindow(window) != context.Window ||
            window.Continuation is not (ProgramCardContinuation.BeforeTargetEffects or ProgramCardContinuation.BeforeTrickTargetEffects) ||
            _resolutionStack[index - 2] is not CardUseFrame use || use.Id != window.ParentFrameId ||
            window.Action.Type != CardActionType.Use || use.Action is not { Type: CardActionType.Use } action ||
            action.ActionId != window.Action.ActionId || action.ActorSeat != window.Action.ActorSeat ||
            action.ProviderSeat != window.Action.ProviderSeat || action.RequesterSeat != window.Action.RequesterSeat ||
            action.EffectiveKind != window.Action.EffectiveKind ||
            !action.PhysicalCards.SequenceEqual(window.Action.PhysicalCards) ||
            cardContext.CardActionId != action.ActionId || cardContext.ActorSeat != action.ActorSeat ||
            context.SourceSeat != action.ActorSeat || context.OwnerSeat != frame.OwnerSeat ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count)
            throw new InvalidOperationException("An awaited bound gift lost its exact card-use window and owning action.");
        var candidate = window.Candidates[window.CandidateIndex];
        var effect = ParticipantHandPaused(frame);
        if (candidate.OwnerSeat != frame.OwnerSeat || candidate.SkillId != frame.SkillId ||
            candidate.TriggerId != frame.TriggerId || frame.ActivationId != frame.TriggerId ||
            candidate.SkillInstanceId != frame.SkillInstanceId || candidate.GameplayHash != frame.GameplayHash ||
            effect is not { Op: SkillProgramEffectOp.MoveBoundCards, AwaitMovementTriggers: true,
                Destination: SkillProgramCardDestination.SelectedTargetHand })
            throw new InvalidOperationException("An awaited bound gift lost its exact candidate or movement instruction.");
    }

    private bool ResumeParticipantHandPayment(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != frameId ||
            !(frame.BoundParticipantGift is not null || frame.PrivateHandTake is { Paid: true } || frame.HandRepayment is { Paid: true })) return false;
        ValidateParticipantHandDrafts(frame, ParticipantHandPaused(frame));
        if (TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.Program) ||
            TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(frame.Id)) return true;
        ReplaceRuntimeTop(frame with { BoundParticipantGift = null, PrivateHandTake = null, HandRepayment = null });
        if (_winner != Winner.None)
        { CancelProgramBindingAndCleanup(GetActiveProgramFrame(frame.Id), "游戏已结束，已付手牌操作不重复，后续结算取消。"); return true; }
        return false;
    }

    private void ValidateParticipantHandDrafts(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (frame.PrivateHandTake is { } take && (paused.Op != SkillProgramEffectOp.ViewAndTakeSelectedTargetHand ||
            frame.SelectedTargetSeats is not [var source] || source != take.SourceSeat || source == frame.OwnerSeat ||
            take.RequiredCount is < 1 or > 2 || take.SelectedCardIds.Distinct().Count() != take.SelectedCardIds.Count ||
            take.ViewedCardIds.Distinct().Count() != take.ViewedCardIds.Count || take.SelectedCardIds.Any(id => !take.ViewedCardIds.Contains(id)) ||
            (take.Paid ? take.SelectedCardIds.Count != take.RequiredCount : take.SelectedCardIds.Count >= take.RequiredCount)))
            throw new InvalidOperationException("Private hand selection lost its exact instruction or frozen card receipt.");
        if (frame.HandRepayment is { } repay && (paused.Op != SkillProgramEffectOp.RequestHandBySuitsOrLoseHp ||
            repay.InstructionIndex != frame.InstructionIndex || frame.SelectedTargetSeats is not [var giver] || giver != repay.SourceSeat || giver == frame.OwnerSeat ||
            repay.Paid && (repay.LostHp == (repay.PaidCardId is not null))))
            throw new InvalidOperationException("Hand repayment lost its exact one-card or one-HP paid receipt.");
        if (frame.BoundParticipantGift is { } gift && (paused.Op != SkillProgramEffectOp.MoveBoundCards || !paused.AwaitMovementTriggers ||
            paused.Destination != SkillProgramCardDestination.SelectedTargetHand || gift.InstructionIndex != frame.InstructionIndex ||
            paused.SourceBind != gift.SourceBind || frame.SelectedTargetSeats is not [var recipient] || recipient != gift.RecipientSeat))
            throw new InvalidOperationException("A bound participant gift lost its exact paid instruction.");
        if (frame.BoundParticipantGift is not null) ValidateBoundParticipantGiftWindow(frame);
        if (!ReferenceEquals(frame, _resolutionStack.LastOrDefault())) return;
        if (frame.PrivateHandTake is { Paid: false } view && (_pendingDecision?.PlayerSeat != frame.OwnerSeat ||
            !AssistedChoicesEqual(_pendingDecision.Choices, PrivateHandTakeChoices(frame)) ||
            view.ViewedCardIds.Any(id => _cardZones.GetLocation(id) != CardLocation.Hand(view.SourceSeat))))
            throw new InvalidOperationException("The private hand prompt lost its chooser-safe frozen source.");
        if (frame.HandRepayment is { Paid: false } request && (_pendingDecision?.PlayerSeat != request.SourceSeat ||
            !AssistedChoicesEqual(_pendingDecision.Choices, HandRepaymentChoices(frame))))
            throw new InvalidOperationException("The hand repayment prompt changed its valid effective suits.");
    }

    private PromptChoice SelectAiParticipantHandChoice(PendingDecision decision, ProgramSkillFrame frame)
    {
        if (frame.PrivateHandTake is not null)
            return decision.Choices.OrderByDescending(c => CardCatalog.Get(_cardZones.CardsAt(CardLocation.Hand(frame.PrivateHandTake.SourceSeat)).Single(card => card.Id == c.Cards.Single()).Kind).HandKeepValue).ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
        var source = _players[frame.HandRepayment!.SourceSeat];
        var gift = decision.Choices.Where(c => c.Cards.Count == 1).OrderBy(c => GetKeepValue(GetHand(source).Single(card => card.Id == c.Cards.Single()), source)).ThenBy(c => c.Id.Value, StringComparer.Ordinal).FirstOrDefault();
        return gift is not null && (source.Hp <= 1 || GetKeepValue(GetHand(source).Single(c => c.Id == gift.Cards.Single()), source) < 50)
            ? gift : decision.Choices.Single(c => c.Parameters.GetValueOrDefault("repayment") == "lose-hp");
    }
}
