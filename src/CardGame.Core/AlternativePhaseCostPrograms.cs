namespace CardGame.Core;

public enum ProgramAlternativePhaseCostKind { DiscardCard, RemoveMarker }
public sealed record ProgramAlternativePhaseCostReceipt(int InstructionIndex,
    ProgramAlternativePhaseCostKind Kind, PlayerMarkerKind Marker,
    int? CardId = null, CardLocation? SourceLocation = null);
public sealed record ProgramAlternativePhaseCostPaidEvent(long FrameId, string SkillId,
    string BindingId, string SkillInstanceId, int OwnerSeat,
    ProgramAlternativePhaseCostKind Kind, PlayerMarkerKind Marker, int? CardId,
    CardLocation? SourceLocation) : IGameEvent;
public sealed record ProgramTurnPhaseSubstitutedEvent(long FrameId, string SkillId,
    string BindingId, string SkillInstanceId, int OwnerSeat, int ActualTurnNumber,
    SkillProgramTriggerWindow Window, SkillProgramTurnPhase SkippedPhase) : IGameEvent;
public sealed record ProgramActualPhaseSubstitution(long ParentFrameId, long ProducerFrameId,
    int OwnerSeat, string SkillId, string SkillInstanceId, int ActualTurnNumber,
    SkillProgramTurnPhase SkippedPhase);

public interface IAlternativePhaseCostProgramHost
{
    SkillProgramStepOutcome PayOwnedCardOrMarker(ProgramSkillFrame frame,
        IReadOnlyList<CardZoneKind> zones, PlayerMarkerKind marker);
}
public sealed class PayOwnedCardOrMarkerHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.PayOwnedCardOrMarker;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) =>
        ((IAlternativePhaseCostProgramHost)host).PayOwnedCardOrMarker(frame, effect.Zones, effect.Marker!.Value);
}
internal sealed class PayOwnedCardOrMarkerDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PayOwnedCardOrMarker;
    public override ISkillProgramEffectHandler Handler { get; } = new PayOwnedCardOrMarkerHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.DiscardSelected,
        static (_, context) => context.PublicControlValue(-4d));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "zones", "marker", "condition");
        var owner = FilterBoundCardsProgramOperationDescriptor.Owner(r);
        var zones = r.RequiredEnumArray<CardZoneKind>("zones");
        if (zones.Count == 0 || zones.Distinct().Count() != zones.Count ||
            zones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.zones: one-card alternative costs accept distinct owned hand/equipment areas.");
        var effect = new SkillProgramEffect(Op, owner, 1, r.Condition(), zones: zones,
            marker: r.RequiredEnum<PlayerMarkerKind>("marker"));
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindows([SkillProgramTriggerWindow.JudgmentPhaseStarting,
            SkillProgramTriggerWindow.DrawPhaseStarting, SkillProgramTriggerWindow.AfterNormalDraw,
            SkillProgramTriggerWindow.DiscardPhaseStarting]), new RequireOwnTurnBoundary()];
}

internal static class AlternativePhaseCostCompositionContract
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow window, SkillProgramTriggerSubject? subject,
        SkillProgramTurnOwnerScope turnOwnerScope)
    {
        var payment = effects.Select((effect, index) => (effect, index)).Where(item => item.effect.Op == SkillProgramEffectOp.PayOwnedCardOrMarker).ToArray();
        if (window is SkillProgramTriggerWindow.JudgmentPhaseStarting or SkillProgramTriggerWindow.DrawPhaseStarting &&
            effects.Any(e => e.Op == SkillProgramEffectOp.SkipTurnPhases) && payment.Length == 0)
            throw new InvalidOperationException($"Invalid skill program at {path}: actual starting substitution requires an explicit alternative paid producer.");
        if (payment.Length > 0)
        {
            var phase = window switch
            {
                SkillProgramTriggerWindow.JudgmentPhaseStarting => SkillProgramTurnPhase.Judgment,
                SkillProgramTriggerWindow.DrawPhaseStarting => SkillProgramTurnPhase.Draw,
                SkillProgramTriggerWindow.AfterNormalDraw => SkillProgramTurnPhase.Play,
                SkillProgramTriggerWindow.DiscardPhaseStarting => SkillProgramTurnPhase.Discard,
                _ => throw new InvalidOperationException($"Invalid skill program at {path}: alternative payment has no supported actual phase boundary.")
            };
            if (payment.Length != 1 || payment[0].index != 0 || effects.Count < 2 ||
                effects[1].Op != SkillProgramEffectOp.SkipTurnPhases || !effects[1].SkippedPhases.SequenceEqual([phase]) ||
                subject != SkillProgramTriggerSubject.Owner || turnOwnerScope != SkillProgramTurnOwnerScope.Own)
                throw new InvalidOperationException($"Invalid skill program at {path}: one initial alternative payment must precede its exact own phase skip.");
        }
        if (effects.Any(e => e.Op == SkillProgramEffectOp.RecordEndHandCountAndGrantMarker) &&
            (window != SkillProgramTriggerWindow.TurnEnding || subject != SkillProgramTriggerSubject.Owner || turnOwnerScope != SkillProgramTurnOwnerScope.Own))
            throw new InvalidOperationException($"Invalid skill program at {path}: ending history requires its own ending boundary.");
    }
}

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IAlternativePhaseCostProgramHost
    {
        public SkillProgramStepOutcome PayOwnedCardOrMarker(ProgramSkillFrame frame,
            IReadOnlyList<CardZoneKind> zones, PlayerMarkerKind marker) =>
            engine.BeginAlternativePhaseCost(frame, zones, marker);
    }

    private bool CanOfferAlternativePhaseCost(CharacterState owner, SkillProgramTrigger trigger, ProgramTriggerCandidate candidate)
    {
        var effect = trigger.Effects.FirstOrDefault(e => e.Op == SkillProgramEffectOp.PayOwnedCardOrMarker);
        if (effect is null) return true;
        if (owner.Markers.GetValueOrDefault(effect.Marker!.Value) >= 1) return true;
        return effect.Zones.Any(zone => _cardZones.CardsAt(new CardLocation(zone, owner.Seat)).Any(card =>
            !IsForeignEquipmentDiscardPrevented(owner.Seat, card, new(zone, owner.Seat), OwnedCardMoveIntent.Discard) &&
            !(zone == CardZoneKind.Equipment && IsActiveProgramSourceEquipmentCard(owner.Seat,
                candidate.SkillId, candidate.SkillInstanceId, card))));
    }

    private IReadOnlyList<PromptChoice> BuildAlternativePhaseCostChoices(ProgramSkillFrame frame,
        SkillProgramEffect effect)
    {
        var choices = BuildOwnedCardPaymentChoices(frame.Id, frame.OwnerSeat, frame.OwnerSeat,
            effect.Zones, OwnedCardMoveIntent.Discard).Select(choice => choice with
            {
                Parameters = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                    choice.Parameters.ToDictionary(pair => pair.Key, pair => pair.Key == "program-action"
                        ? "alternative-phase-cost-card" : pair.Value))
            }).ToList();
        if (_players[frame.OwnerSeat].Markers.GetValueOrDefault(effect.Marker!.Value) >= 1)
            choices.Add(new(new($"program-phase-cost.frame-{frame.Id}.marker"),
                $"移除1枚“{PlayerMarkerCatalog.GetDisplayName(effect.Marker.Value)}”", [], [],
                new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>
                { ["program-action"] = "alternative-phase-cost-marker", ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) })));
        return Array.AsReadOnly(choices.ToArray());
    }

    private SkillProgramStepOutcome BeginAlternativePhaseCost(ProgramSkillFrame frame,
        IReadOnlyList<CardZoneKind> zones, PlayerMarkerKind marker)
    {
        frame = GetActiveProgramFrame(frame.Id);
        if (frame.AlternativePhaseCost is not null || frame.WindowContext is not { } context ||
            context.OwnerSeat != _currentSeat || frame.OwnerSeat != _currentSeat ||
            _resolutionStack.Count < 2 || _resolutionStack[^2] is not ProgramLifecycleTriggerWindowFrame parent ||
            parent.Id != context.ParentFrameId || parent.Window != context.Window)
            throw new InvalidOperationException("An alternative phase cost requires its unpaid exact lifecycle owner.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.PayOwnedCardOrMarker || effect.Marker != marker || !effect.Zones.SequenceEqual(zones))
            throw new InvalidOperationException("The alternative phase cost does not match its instruction.");
        var choices = BuildAlternativePhaseCostChoices(frame, effect);
        if (choices.Count == 0)
        {
            CancelProgramBindingAndCleanup(frame, "没有可弃置的牌或可移除的标记，阶段照常继续。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat,
            $"【{skill.Name}】弃置一张自有牌，或移除1枚“{PlayerMarkerCatalog.GetDisplayName(marker)}”。",
            Array.AsReadOnly(choices.SelectMany(c => c.Cards).Distinct().Order().ToArray()), [], frame.OwnerSeat)
        {
            PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = frame.OwnerSeat,
            SkillPrompt = new(frame.SkillId, skill.Name, skill.Name + " · 选择费用", skill.Description), Choices = choices
        };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private bool IsAlternativePhaseCostChoiceLegal(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.AlternativePhaseCost is not null ||
            !_players[frame.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId)) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        return effect.Op == SkillProgramEffectOp.PayOwnedCardOrMarker &&
            BuildAlternativePhaseCostChoices(frame, effect).Any(current => current.Id == choice.Id &&
                current.Cards.SequenceEqual(choice.Cards) && current.Targets.SequenceEqual(choice.Targets) &&
                current.Parameters.Count == choice.Parameters.Count &&
                current.Parameters.All(pair => choice.Parameters.GetValueOrDefault(pair.Key) == pair.Value));
    }

    private void ResolveAlternativePhaseCost(PromptChoice choice)
    {
        if (!IsAlternativePhaseCostChoiceLegal(choice)) throw new InvalidOperationException("The published alternative cost is no longer payable.");
        var frame = (ProgramSkillFrame)_resolutionStack[^1];
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        var marker = effect.Marker!.Value;
        var markerPayment = choice.Parameters["program-action"] == "alternative-phase-cost-marker";
        Card? card = null; CardLocation? from = null;
        if (!markerPayment)
        {
            var zone = Enum.Parse<CardZoneKind>(choice.Parameters["source-zone"]);
            from = new(zone, frame.OwnerSeat);
            card = _cardZones.CardsAt(from.Value).Single(c => c.Id == choice.Cards.Single());
        }
        ClearPendingDecision();
        var receipt = new ProgramAlternativePhaseCostReceipt(frame.InstructionIndex,
            markerPayment ? ProgramAlternativePhaseCostKind.RemoveMarker : ProgramAlternativePhaseCostKind.DiscardCard,
            marker, card?.Id, from);
        ReplaceRuntimeTop(frame with { AlternativePhaseCost = receipt });
        if (markerPayment) PayProgramMarkerCost(_players[frame.OwnerSeat], new(marker, 1), frame.SkillId, GetProgramBindingId(frame));
        else MoveCard(card!, from!.Value, CardLocation.DiscardPile, new($"skill-program.{frame.SkillId}.{effect.Op}"));
        AdvanceEventRulesAndQueueFact(new ProgramAlternativePhaseCostPaidEvent(frame.Id, frame.SkillId,
            GetProgramBindingId(frame), frame.SkillInstanceId, frame.OwnerSeat, receipt.Kind, marker, card?.Id, from));
        AdvanceRuntimeProgram(frame.Id);
    }

    private bool ResumeAlternativePhaseCost(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != frameId || frame.AlternativePhaseCost is null) return false;
        AssertAlternativePhaseCost(frame);
        if (TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.Program) ||
            TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(frame.Id)) return true;
        ReplaceRuntimeTop(frame with { AlternativePhaseCost = null });
        if (_winner != Winner.None || !_players[frame.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        {
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(frame.Id), "已付费用的回复和移牌结清后，剩余阶段替代结算取消。");
            return true;
        }
        return false;
    }

    private void AssertAlternativePhaseCost(ProgramSkillFrame frame)
    {
        if (frame.AlternativePhaseCost is not { } receipt) return;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        if (!Enum.IsDefined(receipt.Kind) || receipt.InstructionIndex != frame.InstructionIndex || receipt.InstructionIndex < 1 ||
            receipt.InstructionIndex > plan.Instructions.Count || plan.Instructions[receipt.InstructionIndex - 1].Op != SkillProgramEffectOp.PayOwnedCardOrMarker ||
            plan.Instructions[receipt.InstructionIndex - 1].Marker != receipt.Marker ||
            receipt.Kind == ProgramAlternativePhaseCostKind.DiscardCard && (receipt.CardId is null || receipt.SourceLocation is not { OwnerSeat: var owner, Zone: CardZoneKind.Hand or CardZoneKind.Equipment } || owner != frame.OwnerSeat) ||
            receipt.Kind == ProgramAlternativePhaseCostKind.RemoveMarker && (receipt.CardId is not null || receipt.SourceLocation is not null))
            throw new InvalidOperationException("A paid alternative phase cost lost its exact producer receipt.");
    }

    private PromptChoice SelectAiAlternativePhaseCost(PendingDecision decision, ProgramSkillFrame frame)
    {
        // The owner may inspect its own materials; another player's private hand is never read.
        var marker = decision.Choices.SingleOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "alternative-phase-cost-marker");
        if (marker is not null) return marker;
        return decision.Choices.OrderBy(choice => GetKeepValue(
                _cardZones.CardsAt(_cardZones.GetLocation(choice.Cards.Single())).Single(card => card.Id == choice.Cards[0]),
                _players[frame.OwnerSeat]))
            .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal).First();
    }

    private void RecordAlternativePhaseSkip(ProgramSkillFrame frame, IReadOnlyList<SkillProgramTurnPhase> phases)
    {
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        if (!plan.Instructions.Any(effect => effect.Op == SkillProgramEffectOp.PayOwnedCardOrMarker)) return;
        if (frame.WindowContext is not { } context || _resolutionStack.Count < 2 ||
            _resolutionStack[^2] is not ProgramLifecycleTriggerWindowFrame parent || parent.Id != context.ParentFrameId ||
            parent.Window != context.Window || phases.Count != 1)
            throw new InvalidOperationException("A paid actual phase skip lost its starting parent.");
        ReplaceRuntimeFrame(parent.Id, parent with { ActualPhaseSubstitution = new(parent.Id, frame.Id,
            frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId, _turnNumber, phases[0]) });
        foreach (var phase in phases) AdvanceEventRulesAndQueueFact(new ProgramTurnPhaseSubstitutedEvent(frame.Id,
            frame.SkillId, GetProgramBindingId(frame), frame.SkillInstanceId, frame.OwnerSeat, _turnNumber,
            frame.WindowContext!.Window, phase));
    }

    private void CompleteActualJudgmentStarting(ProgramLifecycleTriggerWindowFrame frame)
    {
        var current = _players[frame.OwnerSeat];
        if (frame.ActualPhaseSubstitution is { SkippedPhase: SkillProgramTurnPhase.Judgment } receipt && receipt.ParentFrameId == frame.Id)
            CompleteTurnStart(current, _pendingTurnDelayedEffects);
        else BeginDelayedJudgmentOrTurnStart(current);
    }

    private bool IsConfirmedActualStartingSubstitution(ProgramSkillWindowContext context) =>
        _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(frame => frame.Id == context.ParentFrameId &&
            frame.Window == context.Window && frame.OwnerSeat == _currentSeat && frame.ActualPhaseSubstitution is { } receipt &&
            receipt.ParentFrameId == frame.Id && receipt.OwnerSeat == frame.OwnerSeat && receipt.ActualTurnNumber == _turnNumber &&
            (frame.Window == SkillProgramTriggerWindow.JudgmentPhaseStarting && receipt.SkippedPhase == SkillProgramTurnPhase.Judgment ||
             frame.Window == SkillProgramTriggerWindow.DrawPhaseStarting && receipt.SkippedPhase == SkillProgramTurnPhase.Draw));

    private void ResolveDeclinedOwnedFieldMove(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame ||
            choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            choice.Cards.Count != 0 || choice.Targets.Count != 0)
            throw new InvalidOperationException("A declined field move lost its owning choice.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.SelectAndMoveOwnedCard || !effect.AllowDecline || !effect.AwaitMovementTriggers ||
            effect.Destination != SkillProgramCardDestination.SelectedTargetCorrespondingZone)
            throw new InvalidOperationException("Only an explicitly optional public field move may be declined.");
        ClearPendingDecision(); AdvanceRuntimeProgram(frame.Id);
    }
}
