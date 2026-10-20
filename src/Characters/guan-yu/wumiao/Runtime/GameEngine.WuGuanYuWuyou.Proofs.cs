namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidWuyouSample(long frameId)
    {
        var facts = ProgramEventHistory<WuyouNameSampledEvent>().Where(e => e.FrameId == frameId).OrderBy(e => e.Index).ToArray();
        if (facts.Length != 5 || facts[0].RandomBefore == 0) return false;
        var random = new DeterministicRandom(unchecked((int)facts[0].RandomBefore)); var pool = WuyouComposition.Names.ToList();
        for (var i = 0; i < facts.Length; i++)
        {
            var before = random.State; var index = random.Next(pool.Count); var kind = pool[index]; pool.RemoveAt(index);
            if (facts[i].Index != i || facts[i].Kind != kind || facts[i].RandomBefore != before || facts[i].RandomAfter != random.State)
                return false;
        }
        return true;
    }
    private bool ValidWuyouNames(ProgramSkillFrame f)
    {
        var r = f.Wuyou!;
        var facts = ProgramEventHistory<WuyouNameSampledEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
        if (r.Names.Count == 0) return facts.Length == 0 && r.RandomBefore == 0 && r.RandomAfter == 0;
        if (r.Names.Count != 5 || facts.Length != 5 || r.Names.Distinct().Count() != 5 ||
            r.Names is not System.Collections.IList { IsReadOnly: true } || r.RandomBefore == 0) return false;
        var random = new DeterministicRandom(unchecked((int)r.RandomBefore)); var pool = WuyouComposition.Names.ToList();
        for (var i = 0; i < 5; i++)
        {
            var before = random.State; var index = random.Next(pool.Count); var kind = pool[index]; pool.RemoveAt(index);
            if (facts[i].Index != i || facts[i].Kind != kind || r.Names[i] != kind ||
                facts[i].RandomBefore != before || facts[i].RandomAfter != random.State) return false;
        }
        return random.State == r.RandomAfter;
    }

    private bool ValidWuyouFrame(ProgramSkillFrame f, bool requireContribution)
    {
        if (f.Wuyou is not { } r || f.SkillId != "wumiao:wuyou" || f.ActivationId != WuyouBinding ||
            f.TriggerId is not null || f.WindowContext is not null || f.InstructionIndex is < 0 or > 1 ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            r.Source != new CardConversionSource(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId) ||
            r.GameplayHash != f.GameplayHash || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            !IsValidPlayerSeat(r.DonorSeat) || r.Phase.ActorSeat != r.DonorSeat ||
            r.Phase.Kind != ActualDiscardRecoveryPhaseKind.Play || !IsRecordedActualDiscardRecoveryPhase(r.Phase) ||
            r.Before < 0 || r.After < r.Before || r.After > _movementSequence || !Enum.IsDefined(r.Stage) ||
            ProgramEventHistory<WuyouStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var started] ||
            started.Source != r.Source || started.GameplayHash != r.GameplayHash || started.Phase != r.Phase ||
            started.DonorSeat != r.DonorSeat || started.CardId != r.ContributionCardId ||
            started.PrintedKind != r.ContributionPrintedKind || started.Before != r.Before ||
            ProgramEventHistory<WuyouStartedEvent>().Count(e => e.Source.OwnerSeat == f.OwnerSeat &&
                e.DonorSeat == r.DonorSeat && e.Phase == r.Phase) != 1 ||
            ProgramEventHistory<ProgramSkillStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat &&
                e.SkillId == f.SkillId && e.ActivationId == f.ActivationId) != 1 || !ValidWuyouNames(f)) return false;
        var payments = ProgramEventHistory<WuyouContributionPaidEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (!r.ContributionPaid) return !requireContribution && r.Stage == WuyouStage.ContributionChildren &&
            r.ContributionBatchId == 0 && r.ContributionSequence == 0 && payments.Length == 0 && r.Names.Count == 0;
        if (f.InstructionIndex != 1 || payments is not [var paid] || paid.DonorSeat != r.DonorSeat ||
            paid.RecipientSeat != f.OwnerSeat || paid.CardId != r.ContributionCardId ||
            paid.PrintedKind != r.ContributionPrintedKind || paid.Before != r.Before ||
            paid.BatchId != r.ContributionBatchId || paid.SelfGift != (f.OwnerSeat == r.DonorSeat)) return false;
        if (paid.SelfGift)
        {
            if (paid.BatchId != 0 || paid.After != paid.Before || r.ContributionSequence != 0) return false;
        }
        else if (paid.After != r.ContributionSequence || paid.BatchId <= f.Id || paid.After <= paid.Before ||
            _cardMovements.Count(m => m.Sequence == paid.After && m.CardId == paid.CardId &&
                m.CardKind == paid.PrintedKind && m.From == CardLocation.Hand(r.DonorSeat) &&
                m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == WuyouContributionReason) != 1) return false;
        var selected = ProgramEventHistory<WuyouReturnSelectedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.ChosenKind is null) return r.ReturnCardId is null && r.ReturnPrintedKind is null && !r.ReturnPaid &&
            r.ReturnBatchId == 0 && r.ReturnSequence == 0 && selected.Length == 0 &&
            !ProgramEventHistory<WuyouLeaseIssuedEvent>().Any(e => e.Lease.FrameId == f.Id);
        if (r.Stage != WuyouStage.ReturnChildren || !r.Names.Contains(r.ChosenKind.Value) ||
            r.ReturnCardId is not { } id || r.ReturnPrintedKind is not { } printed || selected is not [var chosen] ||
            chosen.CardId != id || chosen.PrintedKind != printed || chosen.EffectiveKind != r.ChosenKind ||
            chosen.Before != r.ReturnBefore || r.ReturnBefore < paid.After) return false;
        if (!r.ReturnPaid) return r.ReturnBatchId == 0 && r.ReturnSequence == 0;
        return ProgramEventHistory<WuyouLeaseIssuedEvent>().Where(e => e.Lease.FrameId == f.Id).ToArray() is [var issued] &&
            issued.Lease.CardId == id && issued.Lease.PrintedKind == printed && issued.Lease.EffectiveKind == r.ChosenKind &&
            issued.Lease.MovementCutoff == r.ReturnSequence && issued.Lease.ReturnBatchId == r.ReturnBatchId &&
            ValidWuyouLease(issued.Lease);
    }

    private bool ValidWuyouLease(WuyouHandLease lease)
    {
        if (lease.Source.SkillId != "wumiao:wuyou" || lease.Source.BindingId != WuyouBinding ||
            !IsValidPlayerSeat(lease.HolderSeat) || !IsValidPlayerSeat(lease.Source.OwnerSeat) ||
            !WuyouComposition.Names.Contains(lease.EffectiveKind) ||
            _contentRegistry.GetSkill(lease.Source.SkillId).Program?.GameplayHash != lease.GameplayHash ||
            ProgramEventHistory<WuyouLeaseIssuedEvent>().Count(e => e.Lease == lease) != 1 ||
            ProgramEventHistory<WuyouStartedEvent>().Where(e => e.FrameId == lease.FrameId).ToArray() is not [var started] ||
            started.Source != lease.Source || started.GameplayHash != lease.GameplayHash || started.DonorSeat != lease.HolderSeat ||
            !IsRecordedActualDiscardRecoveryPhase(started.Phase) || started.Phase.ActorSeat != lease.HolderSeat ||
            started.Phase.Kind != ActualDiscardRecoveryPhaseKind.Play ||
            ProgramEventHistory<WuyouStartedEvent>().Count(e => e.Source.OwnerSeat == lease.Source.OwnerSeat &&
                e.DonorSeat == lease.HolderSeat && e.Phase == started.Phase) != 1 ||
            ProgramEventHistory<WuyouContributionPaidEvent>().Where(e => e.FrameId == lease.FrameId).ToArray() is not [var contribution] ||
            contribution.DonorSeat != lease.HolderSeat || contribution.RecipientSeat != lease.Source.OwnerSeat ||
            contribution.CardId != started.CardId || contribution.PrintedKind != started.PrintedKind || contribution.Before != started.Before ||
            contribution.SelfGift != (lease.HolderSeat == lease.Source.OwnerSeat) ||
            ProgramEventHistory<WuyouReturnSelectedEvent>().Where(e => e.FrameId == lease.FrameId).ToArray() is not [var selected] ||
            selected.CardId != lease.CardId || selected.PrintedKind != lease.PrintedKind || selected.EffectiveKind != lease.EffectiveKind ||
            !ValidWuyouSample(lease.FrameId) ||
            !ProgramEventHistory<WuyouNameSampledEvent>().Any(e => e.FrameId == lease.FrameId && e.Kind == lease.EffectiveKind) ||
            ProgramEventHistory<WuyouReturnPaidEvent>().Where(e => e.FrameId == lease.FrameId).ToArray() is not [var returned] ||
            returned.CardId != lease.CardId || returned.BatchId != lease.ReturnBatchId || returned.After != lease.MovementCutoff ||
            returned.Before != selected.Before || returned.SelfGift != lease.SelfReturn ||
            lease.SelfReturn != (lease.HolderSeat == lease.Source.OwnerSeat)) return false;
        if (contribution.SelfGift)
        {
            if (contribution.BatchId != 0 || contribution.After != contribution.Before) return false;
        }
        else if (contribution.BatchId <= lease.FrameId || contribution.After <= contribution.Before ||
            _cardMovements.Count(m => m.Sequence == contribution.After && m.CardId == contribution.CardId &&
                m.CardKind == contribution.PrintedKind && m.From == CardLocation.Hand(lease.HolderSeat) &&
                m.To == CardLocation.Hand(lease.Source.OwnerSeat) && m.Reason.Value == WuyouContributionReason) != 1) return false;
        if (lease.SelfReturn) return returned.BatchId == 0 && returned.After == returned.Before;
        return returned.BatchId > lease.FrameId && returned.After > returned.Before &&
            _cardMovements.Count(m => m.Sequence == lease.MovementCutoff && m.CardId == lease.CardId &&
                m.CardKind == lease.PrintedKind && m.From == CardLocation.Hand(lease.Source.OwnerSeat) &&
                m.To == CardLocation.Hand(lease.HolderSeat) && m.Reason.Value == WuyouReturnReason) == 1;
    }

    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 630)]
    private bool IsWuyouMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) =>
        effect?.Op == SkillProgramEffectOp.ResolveWuyouContributionAndNamedReturn && f.Wuyou is
            { Stage: WuyouStage.ContributionChildren or WuyouStage.ReturnChildren, ContributionPaid: true } &&
        p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 &&
        p.CoverageResultBind is null && ValidWuyouFrame(f, true);

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 28520)]
    private void AssertWuyouFrame(ProgramSkillFrame f)
    {
        if (f.Wuyou is null) return;
        if (!ValidWuyouFrame(f, f.Wuyou.ContributionPaid))
            throw new InvalidOperationException("Wuyou lost its native contribution phase, original Hand transfers or exact random-name return.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && f.Wuyou.Stage == WuyouStage.ReturnChoice &&
            (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt ||
                prompt.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual(prompt.Choices, WuyouReturnChoices(f))))
            throw new InvalidOperationException("Wuyou's private return choices changed their original five names or own Hand cohort.");
    }
}
