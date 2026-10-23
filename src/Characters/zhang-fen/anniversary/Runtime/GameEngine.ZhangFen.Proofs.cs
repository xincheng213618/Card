namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidZhangFen(ProgramSkillFrame f)
    {
        if (f.ZhangFen is not { } r || f.WindowContext is not { } w || r.Source != ZhangFenSource(f) || r.Hash != f.GameplayHash ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash || r.ParentId != w.ParentFrameId || r.Turn != _turnNumber ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || !Enum.IsDefined(r.Stage) ||
            r.Before < 0 || r.After < r.Before || r.After > _movementSequence || r.Count < 0 || r.Paid < 0 || r.Paid > r.Count ||
            ProgramEventHistory<ZhangFenStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var start] ||
            start.Source != r.Source || start.Hash != r.Hash || start.Op != r.Op || start.ParentId != r.ParentId || start.Turn != r.Turn || start.CartId != r.CartId || start.Target != r.TargetSeat || start.Count != r.Count ||
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id && e.SkillId == f.SkillId && e.OwnerSeat == f.OwnerSeat && e.SkillInstanceId == f.SkillInstanceId && e.BindingId == f.TriggerId && e.Window == w.Window) != 1) return false;
        var index = _resolutionStack.FindIndex(p => p.Id == f.Id); if (index < 1) return false;
        var parent = _resolutionStack[index - 1];
        var candidate = parent switch
        {
            ProgramLifecycleTriggerWindowFrame l when l.Id == r.ParentId && l.CandidateIndex >= 0 && l.CandidateIndex < l.Candidates.Count => l.Candidates[l.CandidateIndex],
            PlayPhaseStartingBoundaryFrame p when p.Id == r.ParentId && p.ItemIndex >= 0 && p.ItemIndex < p.Items.Count => p.Items[p.ItemIndex].Candidate,
            DamageTriggerWindowFrame d when d.Id == r.ParentId && d.CandidateIndex >= 0 && d.CandidateIndex < d.Candidates.Count => d.Candidates[d.CandidateIndex].ToProgramCandidate(),
            CardsMovedTriggerWindowFrame m when m.Id == r.ParentId && m.CandidateIndex >= 0 && m.CandidateIndex < m.Candidates.Count => m.Candidates[m.CandidateIndex],
            _ => null
        };
        if (candidate is null || !MountObserverCandidateMatches(f, candidate)) return false;
        if (r.Op == SkillProgramEffectOp.ResolveAnniversaryZhangFenWanglu) return f.SkillId == ZhangFenComposition.Wanglu && w.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
            f.OwnerSeat == _currentSeat && r.CartId > 0 && IsRealDagongChe(r.CartId) && r.Stage is ZhangFenStage.PlaceChildren or ZhangFenStage.ExtraPhase or ZhangFenStage.Done;
        if (r.Op == SkillProgramEffectOp.ResolveAnniversaryZhangFenXianzhu) return f.SkillId == ZhangFenComposition.Xianzhu && w.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
            w.SourceSeat == f.OwnerSeat && r.CartId > 0 && IsRealDagongChe(r.CartId) && r.Stage is ZhangFenStage.Upgrade or ZhangFenStage.Done;
        if (r.Op == SkillProgramEffectOp.ResolveAnniversaryZhangFenChaixie) return f.SkillId == ZhangFenComposition.Chaixie && w.Window == SkillProgramTriggerWindow.CardsMoved &&
            r.Count == ZhangFenDestroyedCount(f.OwnerSeat, w) && r.Stage is ZhangFenStage.Drawing or ZhangFenStage.DrawChildren or ZhangFenStage.Done;
        return r.Op == SkillProgramEffectOp.ResolveAnniversaryDagongCheAttack && f.SkillId == ZhangFenComposition.Cart && r.CartId > 0 && IsRealDagongChe(r.CartId) &&
            r.Source.SkillInstanceId == DagongGrant(r.CartId) && (w.Window == SkillProgramTriggerWindow.PlayPhaseStarting && r.Count is >= 1 and <= 6 &&
                r.Stage is ZhangFenStage.Targets or ZhangFenStage.NativeUse or ZhangFenStage.NativeReturned or ZhangFenStage.Done ||
            w.Window == SkillProgramTriggerWindow.AfterDamageApplied && w.SourceSeat == f.OwnerSeat && IsValidPlayerSeat(r.TargetSeat) && HasBoundDagongCheDiscardOrigin(f) &&
                r.Count is >= 1 and <= 6 && r.Stage is ZhangFenStage.DiscardChoice or ZhangFenStage.DiscardChildren or ZhangFenStage.Done);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 43500)] private void RequireZhangFen(ProgramSkillFrame f)
    {
        if (f.ZhangFen is not { } r) return;
        if (!ValidZhangFen(f)) throw new InvalidOperationException("Zhang Fen lost his original candidate, source/hash, generated cart or actual-turn receipt.");
        if (f.PendingMovementContinuation is { } p && !ZhangFenMovementProof(f, GetPausedPrivateOfferEffect(f), p))
            throw new InvalidOperationException("Zhang Fen lost a real equipment, discard or paid native draw movement.");
        if (r.Stage is ZhangFenStage.NativeUse or ZhangFenStage.NativeReturned &&
            (r.UseId <= f.Id || r.ActionId <= 0 || r.Targets.Count is < 1 or > 6 || r.Targets.Distinct().Count() != r.Targets.Count ||
            ProgramEventHistory<DagongCheSlashIssuedEvent>().Count(e => e.Origin.FrameId == f.Id && e.Origin.UseId == r.UseId && e.Origin.ActionId == r.ActionId && e.Origin.Source == r.Source && e.Origin.Hash == r.Hash && e.Origin.CartId == r.CartId) != 1))
            throw new InvalidOperationException("The generated cart lost its exact issued native zero-material Slash.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 43500)]
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 43500)]
    private bool ZhangFenMovementProof(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p)
    {
        if (f.ZhangFen is not { } r || !ValidZhangFen(f) || effect?.Op != r.Op || p.SubjectSeat != f.OwnerSeat || p.BeforeCount != 0 || p.CoverageResultBind is not null) return false;
        if (r.Stage == ZhangFenStage.PlaceChildren) return IsRealDagongChe(r.CartId) &&
            ProgramEventHistory<DagongCheCreatedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.Hash == r.Hash && e.CardId == r.CartId) == 1 &&
            _cardMovements.Count(m => m.CardId == r.CartId && m.From == CardLocation.OutsideGame && m.To == CardLocation.Equipment(f.OwnerSeat) && m.Sequence > r.Before && m.Sequence <= r.After) == 1;
        if (r.Stage == ZhangFenStage.DiscardChildren) return r.CardId > 0 && r.From is { } from && r.To is { } to && r.MoveSequence > r.Before && r.MoveSequence <= r.After &&
            _cardMovements.Count(m => m.Sequence == r.MoveSequence && m.CardId == r.CardId && m.From == from && m.To == to && m.Reason.Value == "anniversary.dagong-che.discard" &&
                (to == CardLocation.DiscardPile || IsRedirectedPaidDiscardMovement(m, from, new("anniversary.dagong-che.discard")) || IsEphemeralCardDestroyedMaterial(m))) == 1;
        return r.Stage == ZhangFenStage.DrawChildren && r.Draw is { } draw && draw.RecipientSeat == f.OwnerSeat && draw.RequestedCount == r.Count && draw.SequenceBefore == r.Before && draw.SequenceAfter == r.After &&
            ProgramEventHistory<ZhangFenDrawReturnedEvent>().Count(e => e == new ZhangFenDrawReturnedEvent(f.Id, r.Count, draw.ActualDrawIds.Count, r.Before, r.After)) == 1;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 43500)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 43500)] private bool KeepPaidZhangFen(ProgramSkillFrame f) =>
        IsUnbegunIssuedDagongCheDamage(f) || f.ZhangFen is { Stage: ZhangFenStage.PlaceChildren or ZhangFenStage.DiscardChoice or ZhangFenStage.DiscardChildren or ZhangFenStage.Drawing or ZhangFenStage.DrawChildren or ZhangFenStage.NativeUse or ZhangFenStage.NativeReturned } && ValidZhangFen(f);
    private bool HasZhangFenPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger } p &&
        _resolutionStack.LastOrDefault() is ProgramSkillFrame { Step: ResolutionFrameStep.AwaitingResponse, ZhangFen.Stage: ZhangFenStage.Upgrade or ZhangFenStage.Targets or ZhangFenStage.DiscardChoice } f &&
        p.PlayerSeat == f.OwnerSeat && ValidZhangFen(f) && AssistedChoicesEqual(p.Choices, ZhangFenChoices(f));

    [ProgramPipelineHook(ProgramPipelinePhase.CardEntitySkillLabel, 43500)] private string? DagongCheUpgradeLabel(int cardId)
    {
        if (!IsRealDagongChe(cardId)) return null;
        var upgrades = DagongUpgrades(cardId);
        return $"升级{upgrades.Length}/5 · {(upgrades.Any(e => e.Choice == "range") ? "无距防" : "普通距离防具")} · 目标{1 + upgrades.Count(e => e.Choice == "target")} · 弃牌{1 + upgrades.Count(e => e.Choice == "discard")}";
    }
}
