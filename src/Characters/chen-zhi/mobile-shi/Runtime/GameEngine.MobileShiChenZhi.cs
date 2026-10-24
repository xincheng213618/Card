using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShiCzQuanchong = "mobile:shi-quanchong", ShiCzRenxing = "mobile:shi-renxing";
    private const string ShiCzDiscard = "skill.mobile-shi-chen-zhi.discard", ShiCzDraw = "skill-program.mobile-shi-chen-zhi.draw";
    private static CardConversionSource ShiCzSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    [ProgramCapability]
    private sealed class MobileShiChenZhiHost(GameEngine engine) : IMobileShiChenZhiHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation) => engine.BeginShiCz(frame, operation); }

    [ProgramCardUsePayment(ProgramCardUsePaymentStage.CaptureMovement, 47600)]
    private void CaptureShiCzFirstDiscard(CardMovementBatchContext batch)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMobileShiChenZhiRenxing) ||
            batch.TurnNumber < 1 || batch.DiscardRecoveryPhase is { Kind: ActualDiscardRecoveryPhaseKind.Discard } ||
            ProgramEventHistory<MobileShiChenZhiFirstDiscardEvent>().Any(e => e.ActualTurn == batch.TurnNumber)) return;
        var first = batch.Movements.FirstOrDefault(m => GetProgramDiscardSource(m) is { } from && IsOwnedDiscardSource(from));
        if (first is not null)
            AdvanceEventRulesAndQueueFact(new MobileShiChenZhiFirstDiscardEvent(batch.TurnNumber, batch.Id, first.Sequence));
    }

    [ProgramPipelineHook(ProgramPipelinePhase.SelectDiscardBatchIndexes, 47600)]
    private int[]? SelectShiCzFirstDiscard(CardMovementBatchContext batch, ProgramTriggerCandidate candidate, ProgramInstructionFeatures features)
    {
        if (!features.HasOperation(SkillProgramEffectOp.ResolveMobileShiChenZhiRenxing)) return null;
        var first = ProgramEventHistory<MobileShiChenZhiFirstDiscardEvent>().SingleOrDefault(e => e.ActualTurn == batch.TurnNumber);
        if (first?.BatchId != batch.Id) return [];
        return batch.Movements.Select((m, i) => (m, i)).Where(x => x.m.Sequence == first.MovementSequence).Select(x => x.i).ToArray();
    }

    private MobileShiChenZhiExtraGrantedEvent? ShiCzPendingExtra(int owner)
    {
        if (_turnProgression.Kind != ActualTurnKind.Extra || _currentSeat != owner || !_players[owner].IsAlive ||
            ProgramEventHistory<ProgramExtraTurnPendedEvent>().LastOrDefault() is not { SkillId: ShiCzQuanchong } last || last.Seat != owner)
            return null;
        return ProgramEventHistory<MobileShiChenZhiExtraGrantedEvent>().SingleOrDefault(e => e.FrameId == last.FrameId &&
            e.Source.OwnerSeat == owner && e.Source.SkillId == ShiCzQuanchong && e.IssuedTurn == _turnNumber &&
            e.GameplayHash == _contentRegistry.GetSkill(ShiCzQuanchong).Program!.GameplayHash &&
            !ProgramEventHistory<MobileShiChenZhiExtraEnteredEvent>().Any(x => x.GrantFrameId == e.FrameId));
    }

    // A paid extra-turn obligation survives loss of its original skill. It is
    // still bound to the last native pend and the original content hash.
    [ProgramPipelineHook(ProgramPipelinePhase.IncludeTriggerCandidate, 47600)]
    private IReadOnlyList<ProgramTriggerCandidate> IncludeShiCzExtraStart(CharacterState owner, SkillProgramTriggerWindow window,
        IReadOnlyList<ProgramTriggerCandidate> ordinary)
    {
        if (window != SkillProgramTriggerWindow.TurnBeforeStart || !_contentRegistry.Skills.ContainsKey(ShiCzQuanchong)) return ordinary;
        var rest = ordinary.Where(c => c.SkillId != ShiCzQuanchong || c.BindingId != "quanchong-extra-start").ToArray();
        var grant = ShiCzPendingExtra(owner.Seat);
        return grant is null ? Array.AsReadOnly(rest) : Array.AsReadOnly(rest.Append(new ProgramTriggerCandidate(owner.Seat,
            ShiCzQuanchong, "quanchong-extra-start", grant.Source.SkillInstanceId, grant.GameplayHash, 100)).ToArray());
    }

    [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate, 47600)]
    private bool IsIssuedShiCzExtraStart(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        candidate.SkillId == ShiCzQuanchong && candidate.BindingId == "quanchong-extra-start" &&
        context.Window == SkillProgramTriggerWindow.TurnBeforeStart && ShiCzPendingExtra(candidate.OwnerSeat) is { } grant &&
        grant.Source.SkillInstanceId == candidate.SkillInstanceId && grant.GameplayHash == candidate.GameplayHash &&
        _resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is ProgramLifecycleTriggerWindowFrame parent &&
        parent.Window == context.Window && parent.CandidateIndex < parent.Candidates.Count && parent.Candidates[parent.CandidateIndex] == candidate;

    private bool ShiCzUsedOrPlayedSlash(int seat) => CompleteProgramEventHistory().Reverse().TakeWhile(e => e is not TurnStartedEvent)
        .OfType<CardActionAcceptedEvent>().Any(e => e.Action.ActorSeat == seat && IsSlashCard(e.Action.EffectiveKind));

    private IReadOnlyList<PromptChoice> ShiCzPaymentChoices(ProgramSkillFrame f, int holder) => BuildOwnedCardPaymentChoices(f.Id,
        f.OwnerSeat, holder, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard,
        canSelect: (zone, card) => !IsSelfHandCategoryDiscardForbidden(f.OwnerSeat, card, new(zone, holder), OwnedCardMoveIntent.Discard));
    private IReadOnlyList<int> ShiCzDiscardTargets(ProgramSkillFrame frame) => _players.Where(p => p.IsAlive &&
        !ShiCzUsedOrPlayedSlash(p.Seat) && ShiCzPaymentChoices(frame, p.Seat).Count > 0).Select(p => p.Seat).ToArray();

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 47600)]
    private bool CanOfferShiCz(ProgramTriggerLegalityContext gate)
    {
        if (gate.SkillId == ShiCzQuanchong)
            return gate.Trigger.Id == "quanchong-ending" ? gate.OwnerSeat == _currentSeat : ShiCzPendingExtra(gate.OwnerSeat) is not null;
        if (gate.SkillId != ShiCzRenxing) return true;
        return gate.WindowContext?.MovementBatch is { } batch && batch.TurnNumber == _turnNumber &&
            ProgramEventHistory<MobileShiChenZhiFirstDiscardEvent>().SingleOrDefault(e => e.ActualTurn == _turnNumber)?.BatchId == batch.Id;
    }

    private SkillProgramStepOutcome BeginShiCz(ProgramSkillFrame input, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.MobileShiChenZhi is not null || f.InstructionIndex != 1 || f.WindowContext is not { } context)
            throw new InvalidOperationException("Shi Chen Zhi needs the original unpaid native trigger instruction.");
        var stage = op == SkillProgramEffectOp.ResolveMobileShiChenZhiRenxing ? MobileShiChenZhiStage.ChooseEffect :
            f.TriggerId == "quanchong-extra-start" ? MobileShiChenZhiStage.ExtraStartChildren : MobileShiChenZhiStage.DiscardChildren;
        var r = new MobileShiChenZhiReceipt(ShiCzSource(f), f.GameplayHash, context.ParentFrameId, _turnNumber,
            _turnProgression.OwnerSeat, stage) { BeforeMovement = _movementSequence, AfterMovement = _movementSequence,
            FirstDiscardBatch = context.MovementBatch?.Id ?? 0 };
        if (stage == MobileShiChenZhiStage.ChooseEffect)
        {
            ReplaceRuntimeTop(f = f with { MobileShiChenZhi = r }); PublishShiCz(f); return SkillProgramStepOutcome.AwaitChoice;
        }
        if (stage == MobileShiChenZhiStage.ExtraStartChildren)
        {
            var grant = ShiCzPendingExtra(f.OwnerSeat) ?? throw new InvalidOperationException("Quanchong lost its exact pending extra-turn lease.");
            var lose = _players.Any(p => p.IsAlive && p.Seat != f.OwnerSeat && p.Hp >= _players[f.OwnerSeat].Hp);
            ReplaceRuntimeTop(f = f with { MobileShiChenZhi = r with { GrantFrameId = grant.FrameId } });
            AdvanceEventRulesAndQueueFact(new MobileShiChenZhiExtraEnteredEvent(grant.FrameId, f.OwnerSeat, _turnNumber + 1, lose));
            ContinueShiCz(f.Id); return SkillProgramStepOutcome.AwaitChild;
        }
        var cards = ShiCzPaymentChoices(f, f.OwnerSeat).SelectMany(c => c.Cards).Distinct().ToArray();
        ReplaceRuntimeTop(f = f with { MobileShiChenZhi = r with { Cards = cards }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (cards.Length > 0) MoveProgramLogicalDiscard(f, f.OwnerSeat, cards, new(ShiCzDiscard));
        SealShiCzMovement(f.Id); ContinueShiCz(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }

    private IReadOnlyList<PromptChoice> ShiCzChoices(ProgramSkillFrame f)
    {
        var r = f.MobileShiChenZhi!;
        PromptChoice C(string branch, string label, int? target = null) => new(new($"shi-chen-zhi.{f.Id}.{r.Stage}.{branch}.{target ?? -1}"),
            label, [], target is { } seat ? [seat] : [], new Dictionary<string, string>
            { ["program-action"] = "mobile-shi-chen-zhi", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
        if (r.Stage == MobileShiChenZhiStage.ChooseEffect)
        {
            var choices = new List<PromptChoice> { C("draw", r.TurnOwner == f.OwnerSeat ? "摸两张牌" : $"你与 {_players[r.TurnOwner].Name} 各摸一张牌") };
            if (ShiCzDiscardTargets(f).Count > 0) choices.Add(C("discard", "弃置一名本回合未使用或打出过【杀】的角色一张牌"));
            return Array.AsReadOnly(choices.ToArray());
        }
        if (r.Stage == MobileShiChenZhiStage.ChooseTarget) return Array.AsReadOnly(ShiCzDiscardTargets(f)
            .Select(seat => C("target", "弃置 " + _players[seat].Name + " 的一张牌", seat)).ToArray());
        if (r.Stage != MobileShiChenZhiStage.ChooseCard) return [];
        return Array.AsReadOnly(ShiCzPaymentChoices(f, r.TargetSeat).Select(c => c with
            { Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = "mobile-shi-chen-zhi", ["branch"] = "card", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) } }).ToArray());
    }

    private void PublishShiCz(ProgramSkillFrame f)
    {
        var choices = ShiCzChoices(f);
        if (choices.Count == 0) { CompleteShiCz(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "任行：请选择效果或弃牌目标。", [], [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    [ProgramAnswer("mobile-shi-chen-zhi")]
    private void AnswerShiCz(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiChenZhi: { } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !ShiCzChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Renxing lost its current native branch, target or opaque card slot.");
        ClearPendingDecision(); var branch = choice.Parameters["branch"];
        if (r.Stage == MobileShiChenZhiStage.ChooseEffect)
        {
            if (branch == "discard") { ReplaceRuntimeTop(f = f with { MobileShiChenZhi = r with { Stage = MobileShiChenZhiStage.ChooseTarget, Branch = branch } }); PublishShiCz(f); return; }
            IssueShiCzDraw(f, f.OwnerSeat, MobileShiChenZhiStage.DrawOwnerChildren); ContinueShiCz(f.Id); return;
        }
        if (r.Stage == MobileShiChenZhiStage.ChooseTarget)
        {
            ReplaceRuntimeTop(f = f with { MobileShiChenZhi = r with { Stage = MobileShiChenZhiStage.ChooseCard, TargetSeat = choice.Targets.Single() } });
            PublishShiCz(f); return;
        }
        var cardId = choice.Cards.Count == 1 ? choice.Cards.Single() : GetHand(_players[r.TargetSeat])[
            int.Parse(choice.Parameters["slot-index"], CultureInfo.InvariantCulture)].Id;
        ReplaceRuntimeTop(f = f with { MobileShiChenZhi = r with { Stage = MobileShiChenZhiStage.ForeignDiscardChildren,
            CardId = cardId, BeforeMovement = _movementSequence, AfterMovement = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramLogicalDiscard(f, f.OwnerSeat, [cardId], new(ShiCzDiscard)); SealShiCzMovement(f.Id); ContinueShiCz(f.Id);
    }

    private void IssueShiCzDraw(ProgramSkillFrame input, int seat, MobileShiChenZhiStage stage)
    {
        var f = GetActiveProgramFrame(input.Id); var r = f.MobileShiChenZhi!;
        ReplaceRuntimeTop(f = f with { MobileShiChenZhi = r with { Stage = stage, Branch = "draw", BeforeMovement = _movementSequence,
            AfterMovement = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (_players[seat].IsAlive) DrawProgramCards(f.Id, seat, 1, null, null, SkillProgramCardSetVisibility.Private, new(ShiCzDraw));
        SealShiCzMovement(f.Id);
    }

    private void SealShiCzMovement(long id)
    { var f = GetActiveProgramFrame(id); ReplaceRuntimeFrame(id, f with { MobileShiChenZhi = f.MobileShiChenZhi! with { AfterMovement = _movementSequence } }); }
    private bool DrainShiCz(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);

    private void ContinueShiCz(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiChenZhi: { } r } f || f.Id != id || _pendingDecision is not null) return;
        if (r.Stage is MobileShiChenZhiStage.ChooseEffect or MobileShiChenZhiStage.ChooseTarget or MobileShiChenZhiStage.ChooseCard) { PublishShiCz(f); return; }
        if (r.Stage == MobileShiChenZhiStage.ExtraStartChildren &&
            ProgramEventHistory<MobileShiChenZhiExtraEnteredEvent>().Single(e => e.GrantFrameId == r.GrantFrameId).LostHp &&
            !ProgramEventHistory<ProgramSkillHpLostEvent>().Any(e => e.FrameId == f.Id && e.SkillId == f.SkillId))
        {
            // Identity appearance can suspend before the loss is actually paid.
            // A Dying return, by contrast, already carries the native loss fact.
            if (_players[f.OwnerSeat].IsAlive && ApplyProgramHpLoss(f.Id, f.SkillId, f.OwnerSeat, 1) == SkillProgramStepOutcome.AwaitChild) return;
        }
        if (DrainShiCz(f)) return;
        ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null }); r = f.MobileShiChenZhi!;
        if (r.Stage == MobileShiChenZhiStage.DiscardChildren && _players[f.OwnerSeat].IsAlive && _winner == Winner.None)
        {
            PendProgramExtraTurn(f, f.OwnerSeat);
            AdvanceEventRulesAndQueueFact(new MobileShiChenZhiExtraGrantedEvent(f.Id, r.Source, r.GameplayHash, _turnNumber, _roundNumber));
        }
        if (r.Stage == MobileShiChenZhiStage.DrawOwnerChildren)
        { IssueShiCzDraw(f, r.TurnOwner, MobileShiChenZhiStage.DrawActorChildren); ContinueShiCz(id); return; }
        CompleteShiCz(GetActiveProgramFrame(id));
    }

    private void CompleteShiCz(ProgramSkillFrame f)
    {
        var r = f.MobileShiChenZhi!;
        if (f.SkillId == ShiCzRenxing)
            AdvanceEventRulesAndQueueFact(new MobileShiChenZhiRenxingCompletedEvent(f.Id, r.Source, r.ActualTurn, r.FirstDiscardBatch,
                r.Branch ?? "discard", r.TargetSeat));
        ReplaceRuntimeTop(f = f with { MobileShiChenZhi = r with { Stage = MobileShiChenZhiStage.Finished }, PendingMovementContinuation = null });
        FinishProgramSkill(f, true);
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 47600)]
    private bool ResumeShiCz(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiChenZhi: not null } f || f.Id != id) return false; ContinueShiCz(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 47600)]
    private bool ReturnShiCz(ProgramSkillFrame f)
    { if (f.MobileShiChenZhi is null) return false; ContinueShiCz(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 47600)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 47600)]
    private bool PaidShiCzCanReturn(ProgramSkillFrame f) => f.MobileShiChenZhi?.Stage is MobileShiChenZhiStage.DiscardChildren or
        MobileShiChenZhiStage.ExtraStartChildren or MobileShiChenZhiStage.DrawOwnerChildren or MobileShiChenZhiStage.DrawActorChildren or MobileShiChenZhiStage.ForeignDiscardChildren;
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 47600)]
    private bool ShiCzMovementProof(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation movement) =>
        f.MobileShiChenZhi is { } r && r.Stage is MobileShiChenZhiStage.DiscardChildren or MobileShiChenZhiStage.DrawOwnerChildren or
            MobileShiChenZhiStage.DrawActorChildren or MobileShiChenZhiStage.ForeignDiscardChildren &&
        effect?.Op is SkillProgramEffectOp.ResolveMobileShiChenZhiQuanchong or SkillProgramEffectOp.ResolveMobileShiChenZhiRenxing;

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 47600)]
    private void AssertShiCzFrame(ProgramSkillFrame f)
    {
        if (f.MobileShiChenZhi is not { } r) return;
        if (r.Source != ShiCzSource(f) || r.GameplayHash != f.GameplayHash || f.WindowContext?.ParentFrameId != r.WindowId ||
            r.ActualTurn != _turnNumber || r.BeforeMovement < 0 || r.AfterMovement < r.BeforeMovement || r.AfterMovement > _movementSequence ||
            !Enum.IsDefined(r.Stage) || r.Cards.Count != r.Cards.Distinct().Count() ||
            f.SkillId == ShiCzRenxing && (r.FirstDiscardBatch <= 0 || !ProgramEventHistory<MobileShiChenZhiFirstDiscardEvent>().Any(e => e.BatchId == r.FirstDiscardBatch && e.ActualTurn == r.ActualTurn)) ||
            f.TriggerId == "quanchong-extra-start" && (r.GrantFrameId <= 0 || !ProgramEventHistory<MobileShiChenZhiExtraEnteredEvent>().Any(e => e.GrantFrameId == r.GrantFrameId && e.OwnerSeat == f.OwnerSeat)))
            throw new InvalidOperationException("Shi Chen Zhi lost his original trigger, first-discard opportunity, extra-turn lease or finite paid continuation.");
    }

    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShiChenZhiRenxing)]
    private PromptChoice ChooseShiCzAi(PendingDecision pending, ProgramSkillFrame f)
    {
        var r = f.MobileShiChenZhi!;
        if (r.Stage == MobileShiChenZhiStage.ChooseEffect)
            return pending.Choices.FirstOrDefault(c => c.Parameters["branch"] == "discard" &&
                !AreProgramDistributionAllies(_players[f.OwnerSeat], _players[r.TurnOwner]) &&
                ShiCzDiscardTargets(f).Any(s => !AreProgramDistributionAllies(_players[f.OwnerSeat], _players[s]))) ?? pending.Choices[0];
        if (r.Stage == MobileShiChenZhiStage.ChooseTarget)
            return pending.Choices.OrderBy(c => AreProgramDistributionAllies(_players[f.OwnerSeat], _players[c.Targets.Single()])).First();
        return pending.Choices.OrderByDescending(c => c.Cards.Count == 1 ? GetKeepValue(GetAdvancedCard(c.Cards[0]), _players[r.TargetSeat]) : 4).First();
    }

    private IReadOnlyList<ProgramPublicRuleStateSnapshot> MobileShiChenZhiPublicStates(CharacterState owner, string skillId)
    {
        if (skillId is not (ShiCzQuanchong or ShiCzRenxing)) return [];
        var binding = skillId == ShiCzQuanchong ? "quanchong-ending" : "renxing-first-discard";
        var limit = skillId == ShiCzQuanchong ? 1 : 2;
        var states = GetSkillBindingShard(owner).ProgramInstances.Where(i => i.SkillId == skillId).Select(i =>
            new ProgramPublicRuleStateSnapshot(i.SkillInstanceId, binding, ProgramPublicRuleStateKind.ActivationLimit, limit,
                _skillRuntimeState.GetUsage(owner.Seat, skillId, $"{binding}@{i.SkillInstanceId}", SkillUsageScope.Round), SkillUsageScope.Round)).ToList();
        if (skillId == ShiCzRenxing)
            foreach (var i in GetSkillBindingShard(owner).ProgramInstances.Where(i => i.SkillId == skillId))
                states.Add(new(i.SkillInstanceId, "turn-first-discard", ProgramPublicRuleStateKind.ActivationLimit, 1,
                    ProgramEventHistory<MobileShiChenZhiFirstDiscardEvent>().Any(e => e.ActualTurn == _turnNumber) ? 1 : 0, SkillUsageScope.Turn));
        return Array.AsReadOnly(states.ToArray());
    }
}
