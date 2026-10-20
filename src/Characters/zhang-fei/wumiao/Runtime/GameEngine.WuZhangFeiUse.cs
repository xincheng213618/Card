using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string WuZhangFeiZisheng = "wumiao:zisheng";
    private const string WuZhangFeiSampleReason = "skill-program.wu-zhang-fei.zisheng.obtain";
    private bool HasWuZhangFeiUseCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveWuZhangFeiUseSample);
    private static CardConversionSource WuZhangFeiSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_zhang_fei_wumiao_WuZhangFeiUse.WuZhangFeiSource(f);

    // Called while the accepted physical materials still have their original
    // appearance. This is a scalar rules fact, never a parallel pending action.
    private CardActionContext CaptureWuZhangFeiUseRankAction(CardActionContext action)
    {
        if (!HasWuZhangFeiUseCapability) return action;
        if (ProgramEventHistory<WuZhangFeiUseRankCapturedEvent>().Any(e => e.ActionId == action.ActionId)) return action;
        var rank = action.ConversionChain.Any(IsXieweiSource) ? 0 : action.PhysicalCards.Count == 1 ? action.EffectiveRank ??
            EffectiveOwnedCardRank(_players[action.ProviderSeat], GetAdvancedCard(action.PhysicalCards[0].CardId)) :
            action.PhysicalCards.Sum(cost => EffectiveOwnedCardRank(_players[action.ProviderSeat], GetAdvancedCard(cost.CardId)));
        AdvanceEventRulesAndQueueFact(new WuZhangFeiUseRankCapturedEvent(action.ActionId, action.ActorSeat,
            action.ProviderSeat, action.PhysicalCards.Count, rank));
        return action;
    }
    private int? CaptureWuZhangFeiResponseRank(CharacterState provider, CardKind kind,
        IReadOnlyList<CardActionCost> costs, CardConversionSource? source) =>
        HasWuZhangFeiUseCapability && costs is [var cost]
            ? ApplyProgramUseAppearance(provider, GetAdvancedCard(cost.CardId), source).Rank : null;

    private void PrepareWuZhangFeiResponseRanks(CharacterState provider, CardKind kind,
        IReadOnlyList<Card> cards, CardConversionSource? source, int? actorSeat = null)
    {
        if (!HasWuZhangFeiUseCapability || cards.Count == 0 || source is not null && IsXieweiSource(source) ||
            actorSeat is { } actor && actor != provider.Seat ||
            kind != CardKind.Nullification && !IsProgramResponseCardUse(provider, kind)) return;
        var parent = _resolutionStack.LastOrDefault() ?? throw new InvalidOperationException("A response rank payment requires its native owning frame.");
        if (parent.WuZhangFeiResponseRanks is not null) throw new InvalidOperationException("A native response cannot pay another unbound rank invoice.");
        var materials = cards.Select(card => new WuZhangFeiResponseMaterialRank(card.Id, card.Kind, FindOwnedCardLocation(provider, card),
            source is not null && IsXieweiSource(source) ? 0 : EffectiveOwnedCardRank(provider, card))).ToArray();
        var payment = new WuZhangFeiResponseRankPayment(++_resolutionSequence, parent.Id, provider.Seat, kind,
            _movementSequence, materials.Sum(card => card.Rank)) { Materials = materials };
        ReplaceRuntimeFrame(parent.Id, parent with { WuZhangFeiResponseRanks = payment });
        AdvanceEventRulesAndQueueFact(new WuZhangFeiResponseRankPreparedEvent(payment.CaptureId, parent.Id, provider.Seat,
            kind, payment.Before, materials.Length, payment.RankSum));
        for (var i = 0; i < materials.Length; i++)
            AdvanceEventRulesAndQueueFact(new WuZhangFeiResponseMaterialRankEvent(payment.CaptureId, i, materials[i]));
    }
    private void BindWuZhangFeiResponseRanks(CardActionContext action)
    {
        if (!HasWuZhangFeiUseCapability || action.Type != CardActionType.Response) return;
        var matches = _resolutionStack.Where(frame => frame.WuZhangFeiResponseRanks is { } p &&
            p.ProviderSeat == action.ProviderSeat && p.EffectiveKind == action.EffectiveKind &&
            p.Materials.Select(card => card.CardId).SequenceEqual(action.PhysicalCards.Select(card => card.CardId))).ToArray();
        if (matches.Length == 0) return;
        if (matches is not [var parent] || parent.WuZhangFeiResponseRanks is not { } payment || parent.Id != payment.NativeParentId ||
            action.ActorSeat != action.ProviderSeat || action.RequesterSeat is not null ||
            payment.RankSum != payment.Materials.Sum(card => card.Rank) || payment.Materials.Any(card => card.Rank is < 0 or > 13) ||
            !payment.Materials.Zip(action.PhysicalCards).All(pair => pair.First.From == pair.Second.From && pair.First.PrintedKind == pair.Second.CardKind) ||
            ProgramEventHistory<WuZhangFeiResponseRankPreparedEvent>().Count(e => e == new WuZhangFeiResponseRankPreparedEvent(payment.CaptureId,
                parent.Id, action.ProviderSeat, action.EffectiveKind, payment.Before, payment.Materials.Count, payment.RankSum)) != 1 ||
            !ProgramEventHistory<WuZhangFeiResponseMaterialRankEvent>().Where(e => e.CaptureId == payment.CaptureId).OrderBy(e => e.Index)
                .Select(e => e.Material).SequenceEqual(payment.Materials) ||
            ProgramEventHistory<WuZhangFeiResponseRankBoundEvent>().Any(e => e.CaptureId == payment.CaptureId))
            throw new InvalidOperationException("Zisheng response ranks must bind once to their exact paid native materials and original owner.");
        if (payment.Materials.Any(card => !_cardMovements.Any(move => move.Sequence > payment.Before && move.CardId == card.CardId &&
            move.From == card.From && move.CardKind == card.PrintedKind &&
            (move.Reason == CardMoveReasons.Respond || move.Reason == CardMoveReasons.Nullification))))
            throw new InvalidOperationException("Zisheng cannot bind an unpaid original response rank.");
        ReplaceRuntimeFrame(parent.Id, parent with { WuZhangFeiResponseRanks = null });
        AdvanceEventRulesAndQueueFact(new WuZhangFeiResponseRankBoundEvent(payment.CaptureId, parent.Id, action.ActionId));
        AdvanceEventRulesAndQueueFact(new WuZhangFeiUseRankCapturedEvent(action.ActionId, action.ActorSeat,
            action.ProviderSeat, action.PhysicalCards.Count, payment.RankSum));
    }

    private bool IsWuZhangFeiTrueUse(CardActionContext action)
    {
        if (action.Type == CardActionType.Use) return true;
        if (action.EffectiveKind == CardKind.Nullification)
            return IsCompletedResponseUse(action, ProgramCardContinuation.NullificationResponse);
        if (!IsCompletedResponseUse(action, ProgramCardContinuation.Dodge)) return false;
        return _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(use => use.Action?.ActionId == action.ParentActionId)
            is { CardAttack: { EffectiveCardKind: { } incoming } attack } &&
            IsSlashCard(incoming) && attack.TargetSeat == action.ActorSeat;
    }
    private void CaptureWuZhangFeiUseQualification(CardActionContext action, SkillProgramTriggerWindow window)
    {
        if (!HasWuZhangFeiUseCapability || window != SkillProgramTriggerWindow.CardUseCommitted ||
            !IsWuZhangFeiTrueUse(action) || !_players[action.ActorSeat].IsAlive) return;
        var rank = ProgramEventHistory<WuZhangFeiUseRankCapturedEvent>().SingleOrDefault(e => e.ActionId == action.ActionId);
        // Native response-use ranks are captured by their original paid-response
        // constructor. They do not pass through CaptureCardUseAction.
        if (rank is null && action.Type == CardActionType.Response)
        {
            rank = new(action.ActionId, action.ActorSeat, action.ProviderSeat, action.PhysicalCards.Count, action.EffectiveRank ?? 0);
            AdvanceEventRulesAndQueueFact(rank);
        }
        if (rank is null || rank.ActorSeat != action.ActorSeat || rank.ProviderSeat != action.ProviderSeat ||
            rank.MaterialCount != action.PhysicalCards.Count || rank.RankSum <= 3 || rank.RankSum % 3 != 0) return;
        var owner = _players[action.ActorSeat];
        foreach (var binding in GetSkillBindingShard(owner).GetInstanceTriggers(window).Where(b =>
            b.SkillId == WuZhangFeiZisheng && b.Trigger.Id == "rank-use-sample" && b.Trigger.Effects is [{ Op: SkillProgramEffectOp.ResolveWuZhangFeiUseSample }]))
        {
            var source = new CardConversionSource(binding.SkillId, binding.Trigger.Id, owner.Seat, binding.SkillInstanceId);
            if (ProgramEventHistory<WuZhangFeiUseQualifiedEvent>().Any(e => e.Qualification.Source == source && e.Qualification.ActionId == action.ActionId)) continue;
            AdvanceEventRulesAndQueueFact(new WuZhangFeiUseQualifiedEvent(new(action.ActionId, source,
                binding.Program.GameplayHash, action.ProviderSeat, rank.MaterialCount, rank.RankSum)));
        }
    }
    private WuZhangFeiUseQualification? WuZhangFeiQualification(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        candidate.SkillId != WuZhangFeiZisheng || candidate.BindingId != "rank-use-sample" ||
        context.Window != SkillProgramTriggerWindow.CardUseCommitted || context.CardUse is not { } use ? null :
        ProgramEventHistory<WuZhangFeiUseQualifiedEvent>().Select(e => e.Qualification).SingleOrDefault(q =>
            q.ActionId == use.CardActionId && q.Source == new CardConversionSource(candidate.SkillId, candidate.BindingId,
                candidate.OwnerSeat, candidate.SkillInstanceId) && q.GameplayHash == candidate.GameplayHash);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 29060, SkillProgramEffectOp.ResolveWuZhangFeiUseSample)]
    private bool CanOfferWuZhangFeiUse(ProgramTriggerLegalityContext gate) =>
        gate.Owner.IsAlive && WuZhangFeiQualification(gate.Candidate, gate.Context) is { } q &&
        !ProgramEventHistory<WuZhangFeiUseStartedEvent>().Any(e => e.Qualification.Source == q.Source && e.Qualification.ActionId == q.ActionId);

    private SkillProgramStepOutcome BeginWuZhangFeiUse(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.WuZhangFeiUse is not null || f.TriggerId != "rank-use-sample" || f.ActivationId != f.TriggerId || f.InstructionIndex != 1 ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { } context ||
            WuZhangFeiQualification(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash,
                GetProgramTrigger(f).Priority, context.OccurrenceIndex), context) is not { } q ||
            _resolutionStack.SingleOrDefault(item => item.Id == context.ParentFrameId) is not ProgramCardTriggerWindowFrame window ||
            !WuZhangFeiUseWindowMatches(window, q) || !DesignatedExtraTargetCandidateMatches(new(f.OwnerSeat, f.SkillId,
                f.TriggerId!, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex), context, window) ||
            ProgramEventHistory<WuZhangFeiUseStartedEvent>().Any(e => e.Qualification.Source == q.Source && e.Qualification.ActionId == q.ActionId))
            throw new InvalidOperationException("Zisheng needs its exact native true Use, original unbounded rank sum and source-bound commitment.");
        var sample = _cardZones.CardsAt(CardLocation.DrawPile).Where(card => card.Rank == 3).Select(card => card.Id).ToList();
        var beforeRandom = _random.State; _random.Shuffle(sample);
        var receipt = new WuZhangFeiUseReceipt(q, window.Id, window.ParentFrameId, WuZhangFeiUseStage.Selecting,
            beforeRandom, _random.State) { Sample = sample.Take(3).ToArray(), Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { WuZhangFeiUse = receipt });
        AdvanceEventRulesAndQueueFact(new WuZhangFeiUseStartedEvent(f.Id, q, window.Id, window.ParentFrameId,
            beforeRandom, _random.State, receipt.Sample.Count));
        for (var i = 0; i < receipt.Sample.Count; i++)
            AdvanceEventRulesAndQueueFact(new WuZhangFeiSampleEntityEvent(f.Id, i, receipt.Sample[i]));
        if (receipt.Sample.Count == 0) { FinishWuZhangFeiUse(f); return SkillProgramStepOutcome.AwaitChild; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        var choices = WuZhangFeiSampleChoices(f);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "恣胜：获得一张点数为3的牌。", receipt.Sample, [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description), Choices = choices };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanCardSelection : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private bool WuZhangFeiUseWindowMatches(ProgramCardTriggerWindowFrame window, WuZhangFeiUseQualification q) =>
        GetCardActionWindow(window) == SkillProgramTriggerWindow.CardUseCommitted && window.Action.ActionId == q.ActionId &&
        window.Action.ActorSeat == q.Source.OwnerSeat && window.Action.ProviderSeat == q.ProviderSeat && IsWuZhangFeiTrueUse(window.Action) &&
        window.Action.PhysicalCards.Count == q.MaterialCount && ProgramEventHistory<WuZhangFeiUseRankCapturedEvent>().Count(e =>
            e.ActionId == q.ActionId && e.ActorSeat == q.Source.OwnerSeat && e.ProviderSeat == q.ProviderSeat && e.RankSum == q.RankSum && e.MaterialCount == q.MaterialCount) == 1;
    private bool HasWuZhangFeiUseContinuation(long nativeUseId)
    {
        var nativeIndex = _resolutionStack.FindIndex(frame => frame.Id == nativeUseId && frame is CardUseFrame);
        if (nativeIndex < 0 || _resolutionStack.OfType<ProgramSkillFrame>().Where(frame =>
                frame.WuZhangFeiUse?.NativeParentId == nativeUseId).ToArray() is not [var owner] ||
            !WuZhangFeiOperationRootMatches(owner)) return false;
        var index = _resolutionStack.FindIndex(frame => frame.Id == owner.Id);
        if (index != nativeIndex + 2 || _resolutionStack[nativeIndex + 1].Id != owner.WuZhangFeiUse!.WindowId) return false;
        // A private sample replaces the response prompt only for this exact
        // committed native Use. Its real obtain keeps the same owner while
        // the proved movement and observer children drain.
        AssertWuZhangFeiOperation(owner);
        if (index == _resolutionStack.Count - 1)
            return owner.WuZhangFeiUse.Stage == WuZhangFeiUseStage.Selecting
                ? _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt &&
                  prompt.PlayerSeat == owner.OwnerSeat && AssistedChoicesEqual(prompt.Choices, WuZhangFeiSampleChoices(owner))
                : owner.WuZhangFeiUse.Stage == WuZhangFeiUseStage.MovementChildren && _pendingDecision is null;
        return owner.WuZhangFeiUse.Stage == WuZhangFeiUseStage.MovementChildren &&
            WuZhangFeiFirstChild(owner, _resolutionStack[index + 1]) && WuZhangFeiObserverSuffix(index);
    }
    private IReadOnlyList<PromptChoice> WuZhangFeiSampleChoices(ProgramSkillFrame f) =>
        f.WuZhangFeiUse!.Sample.Select(id => new PromptChoice(new($"wu-zhang-fei.sample.{f.Id}.{id}"),
            $"获得【{GetAdvancedCard(id).DisplayName}】。", [id], [], new Dictionary<string, string>
            { ["program-action"] = "wu-zhang-fei-sample", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) })).ToArray();
    [ProgramAnswer("wu-zhang-fei-sample")]
    private void AnswerWuZhangFeiSample(PromptChoice choice)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id); AssertWuZhangFeiOperation(f);
        if (f.WuZhangFeiUse is not { Stage: WuZhangFeiUseStage.Selecting } r ||
            !AssistedChoicesEqual([choice], WuZhangFeiSampleChoices(f).Where(item => item.Id == choice.Id).ToArray()) ||
            choice.Cards is not [var id] || choice.Targets.Count != 0 || _cardZones.GetLocation(id) != CardLocation.DrawPile)
            throw new InvalidOperationException("Zisheng sample must be one of its original real private deck entities.");
        ClearPendingDecision();
        if (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
        { FinishWuZhangFeiUse(f); return; }
        ReplaceRuntimeTop(f with { WuZhangFeiUse = r with { Stage = WuZhangFeiUseStage.MovementChildren,
            ObtainedCardId = id, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveCard(GetAdvancedCard(id), CardLocation.DrawPile, CardLocation.Hand(f.OwnerSeat), new(WuZhangFeiSampleReason), movement =>
        {
            var current = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(current with { WuZhangFeiUse = current.WuZhangFeiUse! with { After = movement.Sequence } });
            AdvanceEventRulesAndQueueFact(new WuZhangFeiSampleObtainedEvent(f.Id, id, movement.Sequence));
        });
        ContinueWuZhangFeiOperation(f.Id);
    }
    private void FinishWuZhangFeiUse(ProgramSkillFrame f)
    {
        AdvanceEventRulesAndQueueFact(new WuZhangFeiUseCompletedEvent(f.Id, f.WuZhangFeiUse!.Qualification.ActionId, f.WuZhangFeiUse.ObtainedCardId));
        FinishProgramSkill(f, true);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveWuZhangFeiUseSample)]
    private PromptChoice SelectAiWuZhangFeiSample(PendingDecision decision, ProgramSkillFrame frame) =>
        decision.Choices.OrderByDescending(choice => GetKeepValue(GetAdvancedCard(choice.Cards.Single()), _players[frame.OwnerSeat]))
            .ThenBy(choice => choice.Cards.Single()).First();

    private bool WuZhangFeiOperationRootMatches(ProgramSkillFrame f)
    {
        if ((f.WuZhangFeiUse is null) == (f.WuZhangFeiXianlue is null) ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash) return false;
        if (f.WuZhangFeiUse is { } sample)
        {
            var q = sample.Qualification;
            if (f.SkillId != WuZhangFeiZisheng || f.TriggerId != "rank-use-sample" || f.ActivationId != f.TriggerId ||
                f.SelectedTargetSeats.Count != 0 || q.Source != WuZhangFeiSource(f) || q.GameplayHash != f.GameplayHash ||
                q.RankSum <= 3 || q.RankSum % 3 != 0 || f.WindowContext is not { } context || context.ParentFrameId != sample.WindowId ||
                _resolutionStack.SingleOrDefault(item => item.Id == sample.WindowId) is not ProgramCardTriggerWindowFrame window ||
                window.ParentFrameId != sample.NativeParentId || !WuZhangFeiUseWindowMatches(window, q) ||
                !DesignatedExtraTargetCandidateMatches(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId,
                    f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex), context, window)) return false;
            var index = _resolutionStack.FindIndex(item => item.Id == f.Id);
            return index > 0 && _resolutionStack[index - 1].Id == window.Id && sample.Sample.Count <= 3 &&
                sample.Sample.Distinct().Count() == sample.Sample.Count && sample.Sample.All(id => GetAdvancedCard(id).Rank == 3) &&
                ProgramEventHistory<WuZhangFeiUseQualifiedEvent>().Count(e => e.Qualification == q) == 1 &&
                ProgramEventHistory<WuZhangFeiUseStartedEvent>().Count(e => e == new WuZhangFeiUseStartedEvent(f.Id, q,
                    window.Id, window.ParentFrameId, sample.RandomBefore, sample.RandomAfter, sample.Sample.Count)) == 1 &&
                ProgramEventHistory<WuZhangFeiSampleEntityEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index)
                    .Select(e => e.CardId).SequenceEqual(sample.Sample);
        }
        if (f.WuZhangFeiXianlue is not { } observed || f.SkillId != WuZhangFeiXianlue || f.TriggerId is not null ||
            f.ActivationId != "observe-ranks" || f.WindowContext is not null || f.SelectedTargetSeats is not [var target] ||
            target != observed.TargetSeat || observed.Source != WuZhangFeiSource(f) || observed.GameplayHash != f.GameplayHash ||
            observed.OriginCycleId < 1 || observed.BeforeMask < 0 || observed.BeforeMask > 8191 || observed.ObservedMask < 0 || observed.ObservedMask > 8191 ||
            observed.EarnedGates < 0 || observed.EarnedGates > 7 || (observed.ReturnedGates & ~observed.EarnedGates) != 0 ||
            observed.Hand.Select(card => card.CardId).Distinct().Count() != observed.Hand.Count ||
            observed.Hand.Any(card => card.CardId <= 0 || GetAdvancedCard(card.CardId).Kind != card.PrintedKind || card.Rank is < 0 or > 13)) return false;
        return ProgramEventHistory<WuZhangFeiXianlueStartedEvent>().Count(e => e == new WuZhangFeiXianlueStartedEvent(f.Id,
            observed.Source, observed.GameplayHash, target, observed.ActualTurn, observed.PhaseInstance, observed.OriginCycleId,
            observed.BeforeMask, observed.Hand.Count)) == 1 &&
            ProgramEventHistory<WuZhangFeiXianlueObservedEvent>().Count(e => e == new WuZhangFeiXianlueObservedEvent(f.Id,
                f.OwnerSeat, target, observed.OriginCycleId, observed.BeforeMask, observed.ObservedMask & ~observed.BeforeMask, observed.EarnedGates)) == 1;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 29060)]
    private void AssertWuZhangFeiOperation(ProgramSkillFrame f)
    {
        if (f.WuZhangFeiUse is null && f.WuZhangFeiXianlue is null) return;
        if (!WuZhangFeiOperationRootMatches(f)) throw new InvalidOperationException("Wu ZhangFei lost his exact issued action or paid private observation.");
        if (f.WuZhangFeiUse is { } use)
        {
            if (!Enum.IsDefined(use.Stage) || use.Before < 0 || use.After < use.Before || use.After > _movementSequence)
                throw new InvalidOperationException("Zisheng's movement interval is invalid.");
            if (use.Stage == WuZhangFeiUseStage.Selecting)
            {
                if (use.ObtainedCardId is not null || use.Sample.Any(id => _cardZones.GetLocation(id) != CardLocation.DrawPile))
                    throw new InvalidOperationException("Zisheng's unchosen real sample left its original deck.");
                if (use.Sample.Count > 0 && _resolutionStack.LastOrDefault()?.Id == f.Id &&
                    (_pendingDecision is not { IsPrivate: true, Kind: DecisionKind.ProgramTrigger } prompt ||
                     prompt.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual(prompt.Choices, WuZhangFeiSampleChoices(f))))
                    throw new InvalidOperationException("Zisheng's sample escaped its original private chooser.");
            }
            else if (use.ObtainedCardId is not { } card || !use.Sample.Contains(card) ||
                ProgramEventHistory<WuZhangFeiSampleObtainedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var paid] ||
                paid.CardId != card || paid.MovementSequence != use.After || !_cardMovements.Any(m =>
                    m.Sequence == paid.MovementSequence && WuZhangFeiOperationMovement(f, m)))
                throw new InvalidOperationException("Zisheng cannot repeat or invent its deck-to-Hand obtain.");
            return;
        }
        var x = f.WuZhangFeiXianlue!;
        if (!Enum.IsDefined(x.Stage) || x.CurrentGate is < 0 or > 2 ||
            x.CurrentGate is { } current && ((x.EarnedGates & 1 << current) == 0 || (x.ReturnedGates & 1 << current) != 0))
            throw new InvalidOperationException("Xianlue's frozen gate cursor is invalid.");
        if (x.Stage == WuZhangFeiXianlueStage.Observing && _resolutionStack.LastOrDefault()?.Id == f.Id &&
            (_pendingDecision is not { IsPrivate: true, Kind: DecisionKind.ProgramTrigger } observation ||
             observation.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual(observation.Choices, WuZhangFeiObservedChoices(f)) ||
             x.Hand.Any(card => _cardZones.GetLocation(card.CardId) != CardLocation.Hand(x.TargetSeat))))
            throw new InvalidOperationException("Xianlue's original Hand observation escaped its private owning frame.");
        if (x.Stage == WuZhangFeiXianlueStage.DrawChildren)
        {
            if (x.CurrentGate is not { } gate || x.DrawOutcome is not { RecipientSeat: var owner, RequestedCount: 3 } outcome || owner != f.OwnerSeat ||
                ProgramEventHistory<WuZhangFeiXianlueDrawReturnedEvent>().Where(e => e.FrameId == f.Id && e.Gate == gate).ToArray() is not [var returned] ||
                returned != new WuZhangFeiXianlueDrawReturnedEvent(f.Id, x.OriginCycleId, gate, outcome.ActualDrawIds.Count,
                    outcome.ReplacementGainedIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId))
                throw new InvalidOperationException("Xianlue lost its actual native requested Draw3 invoice.");
        }
        else if (x.DrawOutcome is not null) throw new InvalidOperationException("Xianlue cannot reuse a returned draw as a fresh request.");
    }
    private bool WuZhangFeiOperationMovement(ProgramSkillFrame f, CardMovementRecord move)
    {
        if (!_cardMovements.Contains(move)) return false;
        if (f.WuZhangFeiUse is { Stage: WuZhangFeiUseStage.MovementChildren, ObtainedCardId: { } id } sample)
            return move.Sequence > sample.Before && move.Sequence <= sample.After && move.CardId == id &&
                move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(f.OwnerSeat) && move.Reason.Value == WuZhangFeiSampleReason;
        if (f.WuZhangFeiXianlue is not { Stage: WuZhangFeiXianlueStage.DrawChildren, DrawOutcome: { } draw } ||
            move.Sequence <= draw.SequenceBefore || move.Sequence > draw.SequenceAfter) return false;
        return draw.ActualDrawIds.Contains(move.CardId) && move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(f.OwnerSeat) && move.Reason.Value == WuZhangFeiXianlueDrawReason ||
            draw.ReplacementGainedIds.Contains(move.CardId) && move.To == CardLocation.Hand(f.OwnerSeat) ||
            move.From == CardLocation.DiscardPile && move.To == CardLocation.DrawPile && move.Reason == CardMoveReasons.Reshuffle;
    }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 290)]
    private bool IsWuZhangFeiMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        (effect?.Op == SkillProgramEffectOp.ResolveWuZhangFeiUseSample && f.WuZhangFeiUse is { Stage: WuZhangFeiUseStage.MovementChildren } ||
         effect?.Op == SkillProgramEffectOp.ResolveWuZhangFeiXianlue && f.WuZhangFeiXianlue is { Stage: WuZhangFeiXianlueStage.DrawChildren });
    private bool AwaitWuZhangFeiChildren(long id)
    {
        var f = GetActiveProgramFrame(id);
        ReplaceRuntimeTop(f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id)) return true;
        f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f with { PendingMovementContinuation = null }); return false;
    }
    private void ContinueWuZhangFeiOperation(long id)
    {
        var f = GetActiveProgramFrame(id); AssertWuZhangFeiOperation(f);
        if (f.WuZhangFeiXianlue is not null) { ContinueWuZhangFeiXianlue(id); return; }
        if (f.WuZhangFeiUse?.Stage != WuZhangFeiUseStage.MovementChildren || AwaitWuZhangFeiChildren(id)) return;
        FinishWuZhangFeiUse(GetActiveProgramFrame(id));
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 29060)]
    private bool ResumeWuZhangFeiOperation(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.WuZhangFeiUse is null && f.WuZhangFeiXianlue is null) return false;
        ContinueWuZhangFeiOperation(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 29060)]
    private bool ReturnWuZhangFeiMovement(ProgramSkillFrame f)
    {
        if (f.WuZhangFeiUse is null && f.WuZhangFeiXianlue is null || f.PendingMovementContinuation is null) return false;
        ContinueWuZhangFeiOperation(f.Id); return true;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29060)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 29060)]
    private bool PermitPaidWuZhangFei(ProgramSkillFrame f)
    {
        if (f.WuZhangFeiXianlue is null && f.WuZhangFeiUse is not { ObtainedCardId: not null }) return false;
        AssertWuZhangFeiOperation(f); return true;
    }
    private bool WuZhangFeiFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if ((f.WuZhangFeiUse is null && f.WuZhangFeiXianlue is null) || !WuZhangFeiOperationRootMatches(f)) return false;
        if (f.WuZhangFeiXianlue is { Stage: WuZhangFeiXianlueStage.Drawing } && f.NativeDraw is not null)
            return NativeDrawAttemptRidesOn(child, f);
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state)
            return state.ResumeProgramFrameId == f.Id && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
                ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id &&
                    e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        return child is CardsMovedTriggerWindowFrame moved && ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id &&
            moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
            moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(move => WuZhangFeiOperationMovement(f, move));
    }
    private bool WuZhangFeiObserverSuffix(int index) => HasObserverSuffix(index, static (g, child) =>
        g.IsMovementObserverEdge(child) || g.IsSameNameHandObserverEdge(child) || g.IsYanPuDyingExitEdge(child) ||
        g.TypedDamagePostEventEdge(g._resolutionStack[child - 1], g._resolutionStack[child]) ||
        g._resolutionStack[child - 1] is ProgramSkillFrame parent && (g.WuZhangFeiFirstChild(parent, g._resolutionStack[child]) ||
            parent.Zenrun is not null && g.ZenrunFirstChild(parent, g._resolutionStack[child])) ||
        NativeDrawAttemptRidesOn(g._resolutionStack[child], g._resolutionStack[child - 1]) ||
        g.RecoveryReplacementFrameRidesOn(g._resolutionStack[child], g._resolutionStack[child - 1]),
        static (g, child, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(child, dying));
    private ProgramSkillFrame? WuZhangFeiObserverRoot() => FindProgramObserverRoot(
        static (g, f, child) => g.WuZhangFeiFirstChild(f, child), static (g, index) => g.WuZhangFeiObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1240)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1350)]
    private bool HasWuZhangFeiDamageObserver(long id) => WuZhangFeiObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1380)]
    private bool HasWuZhangFeiBeforeDamageObserver(long id) => WuZhangFeiObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 1210)]
    private bool IsWuZhangFeiDying() => ActiveDying is { } dying && WuZhangFeiObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1780)]
    private bool AllowsWuZhangFeiNestedDamage(ProgramSkillFrame f, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || WuZhangFeiObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f);
        return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, effect.Target));
    }
    private bool IsWuZhangFeiRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.WuZhangFeiUse is null && f.WuZhangFeiXianlue is null || recovery.ParentFrameId == f.Id) return false;
        var parent = _resolutionStack.FindIndex(item => item.Id == f.Id); var child = _resolutionStack.FindIndex(item => item.Id == recovery.Id);
        return parent >= 0 && child > parent + 1 && WuZhangFeiFirstChild(f, _resolutionStack[parent + 1]) &&
            WuZhangFeiObserverSuffix(parent) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 29060)]
    private bool AdvanceWuZhangFeiChildren() => _pendingDecision is null && WuZhangFeiObserverRoot() is not null && TryAdvanceObserverSubtree(true);

    [ProgramCapability]
    private sealed class WuZhangFeiUseCapability(GameEngine engine) : IWuZhangFeiUseHost
    { public SkillProgramStepOutcome Sample(ProgramSkillFrame frame) => engine.BeginWuZhangFeiUse(frame); }
}
