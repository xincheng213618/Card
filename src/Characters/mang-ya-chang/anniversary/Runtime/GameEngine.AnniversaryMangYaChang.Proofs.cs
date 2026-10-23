namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool JiedaoFinalRecipientMatches(AnniversaryJiedaoGrant grant, IDamageAttempt attack) => grant.Origin.Target == attack.TargetSeat ||
        attack.DamageRedirected && ProgramEventHistory<ProgramDamageTransferredEvent>().Count(e => e.ResolutionId == attack.ResolutionId &&
            e.SourceSeat == grant.Origin.Source.OwnerSeat && e.TargetSeat == attack.TargetSeat) == 1;
    private void AppendAnniversaryJiedaoDamageCandidates(IDamageAttempt attack, SkillProgramTriggerWindow timing, List<DamageTriggerCandidate> candidates)
    {
        if (!HasAnniversaryJiedao || timing != SkillProgramTriggerWindow.AfterDamageApplied) return;
        candidates.RemoveAll(c => c.ProgramId == AnniversaryJiedaoComposition.Skill && c.ProgramTriggerId == AnniversaryJiedaoComposition.Pay);
        if (attack.IsSourceLess || !_players[attack.SourceSeat].IsAlive || !_players[attack.TargetSeat].IsAlive) return;
        var trigger = _contentRegistry.GetSkill(AnniversaryJiedaoComposition.Skill).Program!.Triggers.Single(t => t.Id == AnniversaryJiedaoComposition.Pay);
        foreach (var grant in ProgramEventHistory<AnniversaryJiedaoChosenEvent>().Select(e => e.Grant).Where(g => g.Bonus > 0 && ValidJiedaoGrant(g) &&
            g.Origin.AttackId == attack.ResolutionId && g.Origin.Source.OwnerSeat == attack.SourceSeat && JiedaoFinalRecipientMatches(g, attack) &&
            g.Origin.ChainPropagation == attack.IsChainPropagation && g.Origin.ChainCursor == JiedaoChainCursor(attack) &&
            !ProgramEventHistory<AnniversaryJiedaoCostBoundEvent>().Any(e => e.GrantFrameId == g.FrameId)))
            candidates.Add(new(grant.Origin.Source.OwnerSeat, $"jiedao:{grant.FrameId}", trigger.Priority, false, AnniversaryJiedaoComposition.Skill,
                trigger.Id, grant.Origin.Source.SkillInstanceId!, grant.Origin.Hash));
    }
    private AnniversaryJiedaoFrame? JiedaoCostWitness(ProgramTriggerCandidate c, ProgramSkillWindowContext w)
    {
        if (c.SkillId != AnniversaryJiedaoComposition.Skill || c.BindingId != AnniversaryJiedaoComposition.Pay || w.Window != SkillProgramTriggerWindow.AfterDamageApplied ||
            w.OwnerSeat != c.OwnerSeat || w.SourceSeat != c.OwnerSeat || w.DamageFrameId is not { } damageId ||
            _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(p => p.Id == w.ParentFrameId) is not { } parent ||
            parent.ParentFrameId != damageId || parent.TriggerWindow != w.Window || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            parent.Candidates[parent.CandidateIndex].ToProgramCandidate() != c ||
            _resolutionStack.OfType<DamageFrame>().SingleOrDefault(d => d.Id == damageId) is not { } damage || damage.SourceSeat != c.OwnerSeat || damage.TargetSeat != w.TargetSeat || damage.Amount != w.Amount)
            return null;
        var attack = GetDamageTriggerAttack(parent);
        if (attack.IsSourceLess || attack.SourceSeat != damage.SourceSeat || attack.TargetSeat != damage.TargetSeat || attack.ResolutionId != damage.ParentFrameId || !attack.DamageWasApplied) return null;
        var grant = ProgramEventHistory<AnniversaryJiedaoChosenEvent>().Select(e => e.Grant).SingleOrDefault(g => g.Bonus > 0 && ValidJiedaoGrant(g) &&
            g.Origin.AttackId == attack.ResolutionId && g.Origin.Source.OwnerSeat == c.OwnerSeat && JiedaoFinalRecipientMatches(g, attack) &&
            g.Origin.Source.SkillInstanceId == c.SkillInstanceId && g.Origin.Hash == c.GameplayHash && g.Origin.ChainPropagation == attack.IsChainPropagation && g.Origin.ChainCursor == JiedaoChainCursor(attack));
        return grant is null ? null : new(AnniversaryJiedaoStage.DiscardChoice, grant.Origin, grant, damageId, parent.Id);
    }
    [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate, 44400)]
    private bool AdmitAnniversaryJiedaoCost(ProgramTriggerCandidate c, ProgramSkillWindowContext w) => JiedaoCostWitness(c, w) is { Grant: { } grant } &&
        _players[c.OwnerSeat].IsAlive && w.TargetSeat is { } actualTarget && _players[actualTarget].IsAlive &&
        !ProgramEventHistory<AnniversaryJiedaoCostBoundEvent>().Any(e => e.GrantFrameId == grant.FrameId);

    private bool ValidJiedaoFrame(ProgramSkillFrame f)
    {
        if (f.AnniversaryJiedao is not { } r || f.InstructionIndex != 1 || f.WindowContext is not { } w || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            !ValidJiedaoOrigin(r.Origin) || f.SkillId != r.Origin.Source.SkillId || f.OwnerSeat != r.Origin.Source.OwnerSeat || f.SkillInstanceId != r.Origin.Source.SkillInstanceId ||
            f.GameplayHash != r.Origin.Hash || r.Selected.Count != r.Selected.Distinct().Count()) return false;
        if (r.Stage == AnniversaryJiedaoStage.BonusChoice) return f.TriggerId == AnniversaryJiedaoComposition.Choose && r.Grant is null && r.Selected.Count == 0 &&
            JiedaoSourceWitness(JiedaoCandidate(f), w) == r.Origin && !ProgramEventHistory<AnniversaryJiedaoChosenEvent>().Any(e => e.Grant.FrameId == f.Id);
        if (f.TriggerId != AnniversaryJiedaoComposition.Pay || JiedaoCostWitness(JiedaoCandidate(f), w) is not { } issued || issued.Grant != r.Grant ||
            issued.DamageFrameId != r.DamageFrameId || issued.ParentWindowId != r.ParentWindowId || r.Required < 0 || r.Required > r.Grant!.Bonus || r.Selected.Count > r.Required ||
            ProgramEventHistory<AnniversaryJiedaoCostBoundEvent>().Count(e => e == new AnniversaryJiedaoCostBoundEvent(f.Id, r.Grant.FrameId, r.DamageFrameId!.Value)) != 1) return false;
        return r.Stage == AnniversaryJiedaoStage.DiscardChoice ? r.Selected.All(id => JiedaoDiscardableCards(f).Any(c => c.Id == id)) : JiedaoPaymentMatches(f);
    }
    private bool JiedaoPaymentMatches(ProgramSkillFrame f)
    {
        if (f.AnniversaryJiedao is not { Stage: AnniversaryJiedaoStage.DiscardChildren, Grant: { } grant } r || r.Before < 0 || r.After < r.Before || r.After > _movementSequence || r.Selected.Count != r.Required ||
            ProgramEventHistory<AnniversaryJiedaoCostPaidEvent>().Count(e => e == new AnniversaryJiedaoCostPaidEvent(f.Id, grant.FrameId, r.Before, r.After, r.Selected.Count)) != 1) return false;
        var invoices = ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>().Where(e => e.ParentFrameId == f.Id && e.Reason.Value == JiedaoDiscardReason).ToArray();
        if (r.Required == 0) return r.Before == r.After && invoices.Length == 0;
        return invoices is [var paid] && paid.InitiatorSeat == f.OwnerSeat && paid.ExecutorSeat == f.OwnerSeat && paid.SubjectSeat == f.OwnerSeat && paid.ActualCount == r.Required &&
            paid.SequenceBefore >= r.Before && paid.SequenceAfter <= r.After &&
            ProgramEventHistory<NativeLogicalDiscardMaterialCompletedEvent>().Where(e => e.InvocationId == paid.InvocationId).OrderBy(e => e.CardId).Select(e => e.CardId).SequenceEqual(r.Selected.Order()) &&
            ProgramEventHistory<NativeLogicalDiscardMaterialCompletedEvent>().Where(e => e.InvocationId == paid.InvocationId).All(e => e.From.OwnerSeat == f.OwnerSeat &&
                e.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment && e.CompletionSequence > r.Before && e.CompletionSequence <= r.After &&
                _cardMovements.Any(m => m.CardId == e.CardId && m.Sequence == e.CompletionSequence && m.To == e.To &&
                    (m.From == e.From || GetProgramDiscardSource(m) == e.From) && (m.To == CardLocation.DiscardPile || IsEphemeralCardDestroyedMaterial(m) || IsRedirectedPaidDiscardMovement(m, e.From, new(JiedaoDiscardReason)))));
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 44400)] private void AssertJiedaoFrame(ProgramSkillFrame f)
    { if (f.AnniversaryJiedao is not null && !ValidJiedaoFrame(f)) throw new InvalidOperationException("Jiedao lost its exact source damage, chosen increase, surviving-victim obligation or real logical discard."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 44400)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 44400)] private bool ContinueIssuedAnniversaryJiedao(ProgramSkillFrame f) => f.AnniversaryJiedao is not null && ValidJiedaoFrame(f) ||
        f.InstructionIndex is 0 or 1 && f.WindowContext is { } w && AdmitAnniversaryJiedaoCost(JiedaoCandidate(f), w);
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 44400)]
    private bool JiedaoDiscardMovementProof(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.PayAnniversaryMangYaChangJiedao && f.AnniversaryJiedao is { Stage: AnniversaryJiedaoStage.DiscardChildren } &&
        ValidJiedaoFrame(f) && pending == f.PendingMovementContinuation && pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null;
    private bool HasAnniversaryJiedaoPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger } prompt &&
        _resolutionStack.LastOrDefault() is ProgramSkillFrame { AnniversaryJiedao: { Stage: AnniversaryJiedaoStage.BonusChoice or AnniversaryJiedaoStage.DiscardChoice }, Step: ResolutionFrameStep.AwaitingResponse } f &&
        prompt.PlayerSeat == f.OwnerSeat && ValidJiedaoFrame(f) && AssistedChoicesEqual(prompt.Choices, JiedaoChoices(f));
}
