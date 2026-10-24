namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MobileShiChenQunFaenSkill = "mobile:shi-faen";
    private bool HasMobileShiChenQunFaen => _contentRegistry.Skills.ContainsKey(MobileShiChenQunFaenSkill);

    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 47300)]
    private void ObserveMobileShiChenQunUse(IGameEvent payload)
    {
        if (!HasMobileShiChenQunFaen) return;
        if (payload is CardUseDeclaredEvent declared && LifecycleCardUse(declared.ResolutionId) is { } use)
        {
            if (use.SourceSeat != declared.SourceSeat || use.CardKind != declared.CardKind ||
                use.Action is { Type: not CardActionType.Use }) return;
            RecordMobileShiChenQunUse(use.Id, use.Action?.ActionId, use.SourceSeat);
        }
        else if (payload is CardActionAcceptedEvent { Action.Type: CardActionType.Response } accepted &&
            IsAlternateColorActualUse(accepted.Action))
        {
            var response = accepted.Action;
            var native = response.EffectiveKind == CardKind.Nullification ? ActiveNullificationWindow?.Id :
                _resolutionStack.OfType<CardUseFrame>().LastOrDefault(frame => frame.Action?.ActionId == response.ParentActionId)?.Id;
            if (native is { } frameId) RecordMobileShiChenQunUse(frameId, response.ActionId, response.ActorSeat);
        }
    }

    private void RecordMobileShiChenQunUse(long native, long? action, int actor)
    {
        var uses = ProgramEventHistory<MobileShiChenQunUseRecordedEvent>().ToArray();
        if (uses.Any(fact => action is { } id ? fact.ActionId == id : fact.ActionId is null && fact.NativeFrameId == native)) return;
        AdvanceEventRulesAndQueueFact(new MobileShiChenQunUseRecordedEvent(uses.Length + 1, _turnNumber, _currentSeat,
            actor, native, action, uses.LastOrDefault()?.ActorSeat));
    }

    private static bool MobileShiChenQunOwnsZone(CardLocation location, int owner) => location.OwnerSeat == owner &&
        location.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment;

    private IReadOnlyList<int> MobileShiChenQunMaximumLossSeats()
    {
        var counts = _players.Select(player => new { player.Seat, player.IsAlive,
            Count = _cardMovements.Where(move => move.TurnNumber == _turnNumber &&
                MobileShiChenQunOwnsZone(move.From, player.Seat) && !MobileShiChenQunOwnsZone(move.To, player.Seat))
                .Select(move => move.Sequence).Distinct().Count() }).ToArray();
        var maximum = counts.Where(item => item.IsAlive).Select(item => item.Count).DefaultIfEmpty(0).Max();
        return Array.AsReadOnly(counts.Where(item => item.IsAlive && item.Count == maximum).Select(item => item.Seat).ToArray());
    }

    private (ProgramCardTriggerWindowFrame Window, MobileShiChenQunUseRecordedEvent Use)? MobileShiChenQunCurrentUse(ProgramSkillWindowContext context)
    {
        if (context.Window != SkillProgramTriggerWindow.CardUseCommitted || context.CardUse is not { } identity ||
            _resolutionStack.SingleOrDefault(frame => frame.Id == context.ParentFrameId) is not ProgramCardTriggerWindowFrame window ||
            GetCardActionWindow(window) != SkillProgramTriggerWindow.CardUseCommitted || window.Action.ActionId != identity.CardActionId ||
            context.SourceSeat != window.Action.ActorSeat || !IsAlternateColorActualUse(window.Action)) return null;
        var use = ProgramEventHistory<MobileShiChenQunUseRecordedEvent>().SingleOrDefault(fact => fact.ActionId == identity.CardActionId);
        return use is not null && use.ActorSeat == identity.ActorSeat && use.ActualTurn == _turnNumber && use.TurnOwner == _currentSeat
            ? (window, use) : null;
    }

    private MobileShiChenQunFaenDebtIssuedEvent? MobileShiChenQunDebt(int owner, string instance, string hash, int sequence) =>
        ProgramEventHistory<MobileShiChenQunFaenDebtIssuedEvent>().LastOrDefault(debt => debt.Source.OwnerSeat == owner &&
            debt.Source.SkillInstanceId == instance && debt.GameplayHash == hash && debt.ActualTurn == _turnNumber && debt.TurnOwner == _currentSeat &&
            debt.AfterUseSequence + 1 == sequence && !ProgramEventHistory<MobileShiChenQunFaenDebtClaimedEvent>().Any(claim => claim.IssuingFrameId == debt.FrameId));

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 47300)]
    private bool CanOfferMobileShiChenQun(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (candidate.SkillId == "mobile:shi-dingpin") return MobileShiChenQunMaximumLossSeats().Count > 0;
        if (candidate.SkillId != MobileShiChenQunFaenSkill) return true;
        var pair = MobileShiChenQunCurrentUse(context);
        if (pair is null || !_players[pair.Value.Use.ActorSeat].IsAlive) return false;
        return trigger.Id == "faen-use" ? pair.Value.Use.PreviousActorSeat == candidate.OwnerSeat :
            trigger.Id == "faen-next-use" && MobileShiChenQunDebt(candidate.OwnerSeat, candidate.SkillInstanceId, candidate.GameplayHash, pair.Value.Use.Sequence) is not null;
    }

    private static CardConversionSource MobileShiChenQunSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, frame.TriggerId!, frame.OwnerSeat, frame.SkillInstanceId);

    private SkillProgramStepOutcome ClaimMobileShiChenQunFaenDebt(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.MobileShiChenQunFaen is not null || frame.InstructionIndex != 1 || frame.WindowContext is not { } context ||
            MobileShiChenQunCurrentUse(context) is not { } pair) throw new InvalidOperationException("Faen lost its original unpaid native use.");
        var debt = frame.TriggerId == "faen-next-use" ? MobileShiChenQunDebt(frame.OwnerSeat, frame.SkillInstanceId, frame.GameplayHash, pair.Use.Sequence) : null;
        if (frame.TriggerId == "faen-next-use" && debt is null || frame.TriggerId == "faen-use" && pair.Use.PreviousActorSeat != frame.OwnerSeat)
            throw new InvalidOperationException("Faen changed its immediate predecessor or exact next-use obligation.");
        var receipt = new MobileShiChenQunFaenReceipt(MobileShiChenQunSource(frame), frame.GameplayHash, pair.Window.Id,
            pair.Window.ParentFrameId, pair.Window.Action.ActionId, pair.Use.Sequence, pair.Use.ActorSeat, _turnNumber, _currentSeat,
            _movementSequence, debt, debt?.Effect);
        ReplaceRuntimeTop(frame = frame with { MobileShiChenQunFaen = receipt });
        if (debt is not null)
        {
            AdvanceEventRulesAndQueueFact(new MobileShiChenQunFaenDebtClaimedEvent(debt.FrameId, frame.Id, receipt.UseSequence, receipt.ActorSeat));
            BindMobileShiChenQunEffect(frame, debt.Effect);
            return SkillProgramStepOutcome.Continue;
        }
        PublishMobileShiChenQunChoice(frame);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> MobileShiChenQunChoices(ProgramSkillFrame frame)
    {
        var receipt = frame.MobileShiChenQunFaen!;
        var effects = BuildOwnedCardPaymentChoices(frame.Id, receipt.ActorSeat, receipt.ActorSeat,
            [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard).Count > 0 ? new[] { "draw", "discard" } : ["draw"];
        return Array.AsReadOnly(effects.Select(effect => new PromptChoice(new($"shi-faen.{frame.Id}.{effect}"),
            effect == "draw" ? $"令 {_players[receipt.ActorSeat].Name} 摸一张牌" : $"令 {_players[receipt.ActorSeat].Name} 弃置一张牌", [], [],
            new Dictionary<string, string> { ["program-action"] = "mobile-shi-faen", ["effect"] = effect })).ToArray());
    }

    private void PublishMobileShiChenQunChoice(ProgramSkillFrame frame)
    {
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        ReplaceRuntimeTop(frame with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, "请选择法恩的效果。", [], [], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = MobileShiChenQunChoices(frame),
            SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }

    private void BindMobileShiChenQunEffect(ProgramSkillFrame frame, string effect) => ReplaceRuntimeTop(frame with
    {
        MobileShiChenQunFaen = frame.MobileShiChenQunFaen! with { Effect = effect },
        ChoiceBindings = Array.AsReadOnly(frame.ChoiceBindings.Append(new ProgramChoiceResultBinding("faen-effect", effect, frame.OwnerSeat)).ToArray())
    });

    [ProgramAnswer("mobile-shi-faen")]
    private void AnswerMobileShiChenQun(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiChenQunFaen: { Debt: null, Effect: null } } frame ||
            _pendingDecision?.PlayerSeat != frame.OwnerSeat || !AssistedChoicesEqual([choice], MobileShiChenQunChoices(frame).Where(current => current.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Faen changed its current chooser or exact legal branch.");
        RequireMobileShiChenQunFrame(frame);
        ClearPendingDecision(); BindMobileShiChenQunEffect(frame, choice.Parameters["effect"]);
        AdvanceRuntimeProgram(frame.Id); AdvanceRulesAndPublishState();
    }

    [ProgramAiChoice(SkillProgramEffectOp.ClaimMobileShiChenQunFaenDebt)]
    private PromptChoice ChooseMobileShiChenQunAi(ProgramSkillFrame frame) =>
        _aiBrains[frame.OwnerSeat].ScoreProgramTarget(CreateSnapshot(frame.OwnerSeat), frame.MobileShiChenQunFaen!.ActorSeat,
            new(0, 0, 0, 1, 0, 0, false, false)) >= 0 ? MobileShiChenQunChoices(frame)[0] : MobileShiChenQunChoices(frame)[^1];

    private void IssueMobileShiChenQunFaenDebt(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id); RequireMobileShiChenQunFrame(frame);
        var receipt = frame.MobileShiChenQunFaen!;
        if (frame.InstructionIndex != 5 || receipt.Completed || receipt.Effect is not ("draw" or "discard"))
            throw new InvalidOperationException("Faen cannot finalize an unpaid or already completed effect.");
        var actual = receipt.Effect == "draw"
            ? ProgramEventHistory<NativeDrawInvocationRecordedEvent>().Where(item => item.ParentFrameId == frame.Id && item.OwnerSeat == receipt.ActorSeat && item.SequenceBefore >= receipt.BeforeMovement).Sum(item => item.ActualCount)
            : ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>().Where(item => item.ParentFrameId == frame.Id && item.ExecutorSeat == receipt.ActorSeat && item.SequenceBefore >= receipt.BeforeMovement).Sum(item => item.ActualCount);
        ReplaceRuntimeTop(frame with { MobileShiChenQunFaen = receipt with { Completed = true } });
        AdvanceEventRulesAndQueueFact(new MobileShiChenQunFaenEffectCompletedEvent(frame.Id, receipt.Source, receipt.UseSequence,
            receipt.ActorSeat, receipt.Effect, actual, receipt.Debt is not null));
        if (receipt.Debt is null && (receipt.Effect == "draw" || actual > 0))
            AdvanceEventRulesAndQueueFact(new MobileShiChenQunFaenDebtIssuedEvent(frame.Id, receipt.Source, receipt.GameplayHash,
                receipt.ActualTurn, receipt.TurnOwner, receipt.UseSequence, receipt.Effect == "draw" ? "discard" : "draw"));
    }

    private bool IsMobileShiChenQunFaenChoiceResult(ProgramSkillFrame frame, ProgramChoiceResultBinding binding) =>
        frame.MobileShiChenQunFaen is { Effect: { } effect } receipt && binding.Name == "faen-effect" && binding.OptionId == effect &&
        binding.ChooserSeat == frame.OwnerSeat && receipt.Source == MobileShiChenQunSource(frame) && receipt.GameplayHash == frame.GameplayHash &&
        receipt.Effect is "draw" or "discard" && frame.InstructionIndex >= 1;

    private void RequireMobileShiChenQunFrame(ProgramSkillFrame frame)
    {
        if (frame.MobileShiChenQunFaen is not { } receipt || frame.WindowContext is not { } context ||
            MobileShiChenQunCurrentUse(context) is not { } pair || receipt.Source != MobileShiChenQunSource(frame) ||
            receipt.GameplayHash != frame.GameplayHash || receipt.WindowId != pair.Window.Id || receipt.NativeParentId != pair.Window.ParentFrameId ||
            receipt.ActionId != pair.Window.Action.ActionId || receipt.UseSequence != pair.Use.Sequence || receipt.ActorSeat != pair.Use.ActorSeat ||
            receipt.ActualTurn != _turnNumber || receipt.TurnOwner != _currentSeat || receipt.BeforeMovement > _movementSequence ||
            receipt.Debt is not null && (!ProgramEventHistory<MobileShiChenQunFaenDebtIssuedEvent>().Contains(receipt.Debt) ||
                receipt.Debt.AfterUseSequence + 1 != receipt.UseSequence || receipt.Debt.Source.OwnerSeat != frame.OwnerSeat ||
                receipt.Debt.Source.SkillInstanceId != frame.SkillInstanceId || receipt.Debt.GameplayHash != frame.GameplayHash ||
                ProgramEventHistory<MobileShiChenQunFaenDebtClaimedEvent>().Count(claim => claim.IssuingFrameId == receipt.Debt.FrameId && claim.ClaimingFrameId == frame.Id && claim.UseSequence == receipt.UseSequence && claim.ActorSeat == receipt.ActorSeat) != 1))
            throw new InvalidOperationException("Faen lost its original source, native use, predecessor, turn or already-claimed obligation.");
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 47300)]
    private void AssertMobileShiChenQunFrame(ProgramSkillFrame frame)
    { if (frame.MobileShiChenQunFaen is not null) RequireMobileShiChenQunFrame(frame); }

    private IReadOnlyList<ProgramPublicRuleStateSnapshot> MobileShiChenQunPublicStates(CharacterState owner, string skillId)
    {
        if (skillId is not ("mobile:shi-dingpin" or MobileShiChenQunFaenSkill)) return [];
        var dingpin = skillId == "mobile:shi-dingpin";
        var binding = dingpin ? "maximum-loss-ending" : "faen-use";
        var scope = dingpin ? SkillUsageScope.Round : SkillUsageScope.Turn;
        return Array.AsReadOnly(GetSkillBindingShard(owner).ProgramInstances.Where(instance => instance.SkillId == skillId)
            .Select(instance => new ProgramPublicRuleStateSnapshot(instance.SkillInstanceId, binding, ProgramPublicRuleStateKind.ActivationLimit,
                dingpin ? 1 : 2, _skillRuntimeState.GetUsage(owner.Seat, skillId, $"{binding}@{instance.SkillInstanceId}", scope), scope)).ToArray());
    }

    [ProgramCapability]
    private sealed class MobileShiChenQunCapability(GameEngine engine) : IMobileShiChenQunProgramHost, IMobileShiChenQunSelectionQueries
    {
        public SkillProgramStepOutcome Claim(ProgramSkillFrame frame) => engine.ClaimMobileShiChenQunFaenDebt(frame);
        public void Issue(ProgramSkillFrame frame) => engine.IssueMobileShiChenQunFaenDebt(frame);
        public IReadOnlyList<int> MaximumLossSeats => engine.MobileShiChenQunMaximumLossSeats();
    }
}
