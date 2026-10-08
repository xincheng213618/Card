using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private long PairedHandRecastSequence => _cardMovements.LastOrDefault()?.Sequence ?? 0;
    private static PairedHandRecastParticipantReceipt CurrentPairedRecast(ProgramPairedHandRecastReceipt r) =>
        r.Cursor == 0 ? r.Owner : r.Cursor == 1 ? r.Partner :
            throw new InvalidOperationException("A completed pair has no current recast participant.");
    private static ProgramPairedHandRecastReceipt WithCurrentPairedRecast(ProgramPairedHandRecastReceipt r,
        PairedHandRecastParticipantReceipt participant) => r.Cursor == 0 ? r with { Owner = participant } :
        r.Cursor == 1 ? r with { Partner = participant } :
            throw new InvalidOperationException("A completed pair cannot replace a current participant.");
    private PairedHandRecastMaterial[] PairedHandMaterials(int seat) => GetHand(_players[seat])
        .Select(card => new PairedHandRecastMaterial(card.Id, card.Kind, CardLocation.Hand(seat))).ToArray();
    private bool PairedRecastMaterialStillOwned(PairedHandRecastParticipantReceipt participant) =>
        participant.Material is { } m && m.From == CardLocation.Hand(participant.Seat) &&
        _cardZones.GetLocation(m.CardId) == m.From && GetHand(_players[participant.Seat])
            .Any(card => card.Id == m.CardId && card.Kind == m.PrintedKind);

    private SkillProgramStepOutcome BeginPairedHandRecast(ProgramSkillFrame supplied, int partnerSeat)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        var plan = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!);
        if (plan.Activation is not { } activation || f.TriggerId is not null || f.WindowContext is not null ||
            f.InstructionIndex != 1 || f.PairedHandRecast is not null ||
            f.SelectedCardIds is not [var ownerCard] || f.SelectedTargetSeats is not [var target] || target != partnerSeat ||
            target == f.OwnerSeat || !IsValidPlayerSeat(target) || _winner != Winner.None ||
            !_players[f.OwnerSeat].IsAlive || !_players[target].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            _phase != TurnPhase.Play || _currentSeat != f.OwnerSeat || _cardUseDebitPhaseInstanceId <= 0 ||
            _cardZones.GetLocation(ownerCard) != CardLocation.Hand(f.OwnerSeat) || GetHand(_players[target]).Count == 0 ||
            _programPhaseUses.GetValueOrDefault((f.OwnerSeat, f.SkillId, activation.UsageGroup)) != 1 ||
            _resolutionStack.Count != 1)
            throw new InvalidOperationException("Paired hand recast lost its original live hand entity, partner, phase debit or sole activation.");
        PairedHandRecastComposition.ValidateActivation(f.SkillId, activation);
        var r = new ProgramPairedHandRecastReceipt { InstructionIndex = f.InstructionIndex,
            Source = new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId), GameplayHash = f.GameplayHash,
            UsageGroup = activation.UsageGroup, ActualTurnNumber = _turnNumber, ActualTurnOwnerSeat = _currentSeat,
            PhaseInstanceId = _cardUseDebitPhaseInstanceId, Stage = PairedHandRecastStage.ChoosingPartner,
            Owner = new(f.OwnerSeat, PairedHandMaterials(f.OwnerSeat).Single(m => m.CardId == ownerCard)),
            Partner = new(target, null), EligiblePartnerMaterials = PairedHandMaterials(target) };
        ReplaceRuntimeTop(f = f with { PairedHandRecast = r });
        AdvanceEventRulesAndQueueFact(new PairedHandRecastStartedEvent(f.Id, r.Source, r.GameplayHash, r.UsageGroup,
            target, r.ActualTurnNumber, r.ActualTurnOwnerSeat, r.PhaseInstanceId));
        PublishPairedHandRecastChoice(f);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> PairedHandRecastChoices(ProgramSkillFrame f) =>
        Array.AsReadOnly(f.PairedHandRecast!.EligiblePartnerMaterials.Select(m => new PromptChoice(
            new($"paired-hand-recast.{f.Id}.{m.CardId}"), $"重铸【{GetAdvancedCard(m.CardId).DisplayName}】", [m.CardId], [],
            new Dictionary<string, string> { ["program-action"] = "paired-hand-recast",
                ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) })).ToArray());
    private void PublishPairedHandRecastChoice(ProgramSkillFrame f) => PublishParticipantHandChoice(f,
        f.PairedHandRecast!.Partner.Seat, PairedHandRecastChoices(f), "选择一张自己的手牌，与发动者各重铸一张牌。",
        f.PairedHandRecast.Partner.Seat);

    private void ResolvePairedHandRecastChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("Paired hand recast lost its private choice owner.");
        AssertPairedHandRecast(f); var r = f.PairedHandRecast!;
        if (r.Stage != PairedHandRecastStage.ChoosingPartner || r.Cursor != 0 || r.Partner.Material is not null ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt || prompt.PlayerSeat != r.Partner.Seat ||
            choice.Parameters.GetValueOrDefault("program-action") != "paired-hand-recast" ||
            choice.Parameters.GetValueOrDefault("frame-id") != f.Id.ToString(CultureInfo.InvariantCulture) ||
            choice.Cards is not [var id] || choice.Targets.Count != 0 || !PairedHandRecastChoices(f).Any(c =>
                c.Id == choice.Id && c.Cards.SequenceEqual(choice.Cards) && c.Targets.SequenceEqual(choice.Targets) &&
                c.Parameters.OrderBy(x => x.Key).SequenceEqual(choice.Parameters.OrderBy(x => x.Key))))
            throw new InvalidOperationException("Paired recast changed its published private chooser, physical hand entity or frame.");
        ClearPendingDecision();
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !_players[r.Partner.Seat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) || !PairedRecastMaterialStillOwned(r.Owner))
        { CancelProgramBindingAndCleanup(f, "双方重铸在选牌完成前失去原发动资格或真实参与者，未支付任何牌。"); return; }
        var material = r.EligiblePartnerMaterials.Single(m => m.CardId == id);
        if (!PairedRecastMaterialStillOwned(r.Partner with { Material = material }))
            throw new InvalidOperationException("The partner must select its original still-owned hand entity.");
        // Both private choices are now frozen. Subsequent source loss cannot
        // revoke this issued operation or exchange an unpaid partner's card.
        ReplaceRuntimeTop(f with { PairedHandRecast = r with { Stage = PairedHandRecastStage.Ready,
            Partner = r.Partner with { Material = material } } });
        AdvanceRuntimeProgram(f.Id);
    }

    private void IssuePairedHandRecastCost(ProgramSkillFrame f)
    {
        var r = f.PairedHandRecast!; var p = CurrentPairedRecast(r); var m = p.Material!;
        if (r.Stage != PairedHandRecastStage.Ready || p.CostIssued || p.Skipped || !PairedRecastMaterialStillOwned(p))
            throw new InvalidOperationException("A pair may pay only its current frozen original hand entity once.");
        var before = PairedHandRecastSequence;
        ReplaceRuntimeTop(f = f with { PairedHandRecast = WithCurrentPairedRecast(r, p with {
            CostIssued = true, CostBefore = before, CostAfter = before }) with { Stage = PairedHandRecastStage.CostChildren },
            PendingMovementContinuation = new(p.Seat, 0, null) });
        // This is a recast move. Do not route it through discard eligibility or
        // discard-origin payments: an un-discardable hand card can be recast.
        MoveProgramCardsFromMultipleSources([m.CardId], CardLocation.DiscardPile, CardMoveReasons.RecastDiscard, (batch, records) =>
        {
            if (records is not [var paid] || paid.CardId != m.CardId || paid.CardKind != m.PrintedKind ||
                paid.From != m.From || paid.To != CardLocation.DiscardPile || paid.Reason != CardMoveReasons.RecastDiscard)
                throw new InvalidOperationException("The paired recast did not move its exact physical hand cost.");
            var current = GetActiveProgramFrame(f.Id); var receipt = current.PairedHandRecast!;
            ReplaceRuntimeTop(current with { PairedHandRecast = WithCurrentPairedRecast(receipt,
                CurrentPairedRecast(receipt) with { CostAfter = paid.Sequence, CostBatchId = batch }) });
            AdvanceEventRulesAndQueueFact(new PairedHandRecastPaidEvent(f.Id, receipt.Cursor, p.Seat, m.CardId,
                m.PrintedKind, m.From, batch, before, paid.Sequence));
        });
        AdvanceRuntimeProgram(f.Id);
    }

    private void IssuePairedHandRecastDraw(ProgramSkillFrame f)
    {
        var r = f.PairedHandRecast!; var p = CurrentPairedRecast(r);
        if (r.Stage != PairedHandRecastStage.CostChildren || !p.CostIssued || p.DrawIssued || f.PendingMovementContinuation is not null)
            throw new InvalidOperationException("A paid paired recast must drain its exact cost before issuing one owed draw.");
        var before = PairedHandRecastSequence;
        ReplaceRuntimeTop(f = f with { PairedHandRecast = WithCurrentPairedRecast(r, p with {
            DrawIssued = true, DrawBefore = before, DrawAfter = before }) with { Stage = PairedHandRecastStage.DrawChildren },
            PendingMovementContinuation = new(p.Seat, 0, null) });
        // A completed cost owes this draw even after the granting skill becomes
        // unqualified. Death or an ended match settles it with actual zero;
        // neither circumstance restores the cost or selects a replacement.
        var actual = _winner == Winner.None && _players[p.Seat].IsAlive
            ? DrawCards(_players[p.Seat], 1, true, CardMoveReasons.RecastDraw).Count : 0;
        var current = GetActiveProgramFrame(f.Id); var receipt = current.PairedHandRecast!;
        ReplaceRuntimeTop(current with { PairedHandRecast = WithCurrentPairedRecast(receipt,
            CurrentPairedRecast(receipt) with { ActualDrawCount = actual, DrawAfter = PairedHandRecastSequence }) });
        AdvanceEventRulesAndQueueFact(new PairedHandRecastDrawIssuedEvent(f.Id, receipt.Cursor, p.Seat,
            actual, before, PairedHandRecastSequence));
        AdvanceEventRulesAndQueueFact(new CardRecastEvent(p.Seat, p.Material!.CardId, p.Material.PrintedKind, actual));
        AdvanceRuntimeProgram(f.Id);
    }

    private bool DrainPairedHandRecastChildren(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);

    private bool ResumePairedHandRecast(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != frameId || f.PairedHandRecast is not { } r) return false;
        AssertPairedHandRecast(f);
        if (r.Stage == PairedHandRecastStage.ChoosingPartner)
        { if (_pendingDecision is null) PublishPairedHandRecastChoice(f); return true; }
        if (r.Stage is PairedHandRecastStage.CostChildren or PairedHandRecastStage.DrawChildren)
        {
            if (DrainPairedHandRecastChildren(f)) return true;
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
            if (r.Stage == PairedHandRecastStage.CostChildren) { IssuePairedHandRecastDraw(f); return true; }
            r = r with { Cursor = r.Cursor + 1, Stage = PairedHandRecastStage.Ready };
            ReplaceRuntimeTop(f = f with { PairedHandRecast = r });
        }
        while (r.Stage == PairedHandRecastStage.Ready && r.Cursor < 2)
        {
            var p = CurrentPairedRecast(r);
            PairedHandRecastSkipReason? skip = _winner != Winner.None ? PairedHandRecastSkipReason.MatchEnded :
                !_players[p.Seat].IsAlive ? PairedHandRecastSkipReason.ParticipantUnavailable :
                !PairedRecastMaterialStillOwned(p) ? PairedHandRecastSkipReason.MaterialUnavailable : null;
            if (skip is null) { IssuePairedHandRecastCost(f); return true; }
            r = WithCurrentPairedRecast(r, p with { Skipped = true, SkipReason = skip }) with { Cursor = r.Cursor + 1 };
            ReplaceRuntimeTop(f = f with { PairedHandRecast = r });
            AdvanceEventRulesAndQueueFact(new PairedHandRecastSkippedEvent(f.Id, r.Cursor - 1, p.Seat, skip.Value));
        }
        if (r.Stage == PairedHandRecastStage.Ready && r.Cursor == 2)
        {
            ReplaceRuntimeTop(f = f with { PairedHandRecast = r with { Stage = PairedHandRecastStage.Complete } });
            AdvanceEventRulesAndQueueFact(new PairedHandRecastCompletedEvent(f.Id, r.Owner.CostIssued, r.Partner.CostIssued,
                r.Owner.ActualDrawCount, r.Partner.ActualDrawCount));
        }
        FinishProgramSkill(f, true); return true;
    }

    private bool ReturnPairedHandRecastMovement(ProgramSkillFrame f)
    {
        if (f.PairedHandRecast is not { Stage: PairedHandRecastStage.CostChildren or PairedHandRecastStage.DrawChildren }) return false;
        AssertPairedHandRecast(f);
        if (f.PendingMovementContinuation is null) throw new InvalidOperationException("A paired recast child lost its exact movement return.");
        AdvanceRuntimeProgram(f.Id); return true;
    }
    private bool IsPairedHandRecastMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.PairedHandRecast && f.PairedHandRecast is
            { Stage: PairedHandRecastStage.CostChildren or PairedHandRecastStage.DrawChildren } r &&
        f.PendingMovementContinuation == pending && pending.SubjectSeat == CurrentPairedRecast(r).Seat &&
        pending.BeforeCount == 0 && pending.CoverageResultBind is null && ValidPairedHandRecastReceipt(f);
    private bool CanContinuePairedHandRecast(ProgramSkillFrame f) =>
        f.PairedHandRecast is { Partner.Material: not null } && ValidPairedHandRecastReceipt(f);
    private PromptChoice SelectAiPairedHandRecast(PendingDecision decision, ProgramSkillFrame f) => decision.Choices
        .OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[f.PairedHandRecast!.Partner.Seat]))
        .ThenBy(c => c.Cards.Single()).First();
    private sealed partial class ProgramSkillHost : IPairedHandRecastProgramHost
    {
        public SkillProgramStepOutcome PairedHandRecast(ProgramSkillFrame f, int partnerSeat) => engine.BeginPairedHandRecast(f, partnerSeat);
        public bool CanContinuePairedHandRecast(ProgramSkillFrame f) => engine.CanContinuePairedHandRecast(f);
    }
}
