namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string LeJiuCostReason = "skill-program.anniversary:cuijin.SelectAndMoveOwnedCard";
    [ProgramCapability] private sealed class AnniversaryLeJiuCapability(GameEngine e) : IAnniversaryLeJiuHost
    { public void Arm(ProgramSkillFrame f) => e.ArmLeJiuCuijin(f); public void Bind(ProgramSkillFrame f) => e.BindLeJiuCuijin(f); }
    private static CardConversionSource LeJiuSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private CardUseFrame? LeJiuContextUse(ProgramSkillWindowContext w) => w.CardUse is { } info && LifecycleCardUse(info.ParentCardUseFrameId) is
        { Action: { Type: CardActionType.Use } a } use && a.ActionId == info.CardActionId && a.ActorSeat == info.ActorSeat && use.SourceSeat == a.ActorSeat &&
        use.CardKind == a.EffectiveKind && (IsSlashCard(use.CardKind) || use.CardKind == CardKind.Duel) ? use : null;
    private bool LeJiuHasCost(int owner) => GetHand(_players[owner]).Concat(GetEquipment(owner)).Any(c =>
        !IsForeignEquipmentDiscardPrevented(owner, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard));
    private bool HasAnniversaryLeJiuUseObserver(CharacterState actor) =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ArmAnniversaryLeJiuCuijin) && actor.IsAlive &&
        _players.Any(owner => owner.IsAlive && HasRuntimeSkill(owner, AnniversaryLeJiuComposition.Skill) &&
            (owner.Seat == actor.Seat || IsWithinAttackRange(owner.Seat, actor.Seat)));
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 44100,
        SkillProgramEffectOp.ArmAnniversaryLeJiuCuijin, SkillProgramEffectOp.BindAnniversaryLeJiuCuijinCompletion)]
    private bool CanOfferLeJiu(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w)
    {
        if (LeJiuContextUse(w) is not { } use) return false;
        if (t.Id == AnniversaryLeJiuComposition.Pay) return w.Window == SkillProgramTriggerWindow.CardUseCommitted &&
            (c.OwnerSeat == use.SourceSeat || IsWithinAttackRange(c.OwnerSeat, use.SourceSeat)) && LeJiuHasCost(c.OwnerSeat) &&
            !(use.LeJiuCuijin ?? []).Any(g => g.Source.OwnerSeat == c.OwnerSeat && g.Source.SkillInstanceId == c.SkillInstanceId);
        return t.Id == AnniversaryLeJiuComposition.Reward && w.Window == SkillProgramTriggerWindow.CardUseCompleted &&
            use.Step == ResolutionFrameStep.Completed && !use.CausedDamage && LeJiuGrantFor(use, c) is not null;
    }
    private void ArmLeJiuCuijin(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != AnniversaryLeJiuComposition.Skill || f.TriggerId != AnniversaryLeJiuComposition.Pay ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseCommitted } w || LeJiuContextUse(w) is not { } use || !LeJiuParent(f))
            throw new InvalidOperationException("Cuijin requires the exact original committed native Slash or Duel and observer candidate.");
        if (f.InstructionIndex == 1)
        {
            if (f.LeJiuCuijin is not null || !_players[f.OwnerSeat].IsAlive || !_players[use.SourceSeat].IsAlive ||
                f.OwnerSeat != use.SourceSeat && !IsWithinAttackRange(f.OwnerSeat, use.SourceSeat) || !LeJiuHasCost(f.OwnerSeat))
                throw new InvalidOperationException("Cuijin must freeze its legal range and original use before the real discard cost.");
            var r = new LeJiuCuijinFrame(LeJiuSource(f), f.GameplayHash, w.ParentFrameId, use.Id, use.Action!.ActionId, use.SourceSeat, _turnNumber, _movementSequence);
            ReplaceRuntimeTop(f with { LeJiuCuijin = r });
            AdvanceEventRulesAndQueueFact(new LeJiuCuijinQualifiedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.UseId, r.ActionId, r.Actor, r.Turn, r.Before));
            return;
        }
        if (f.InstructionIndex != 3 || f.LeJiuCuijin is not { Reward: false, Grant: null } before || !ValidLeJiuFrame(f) ||
            LeJiuPaidCost(f) is not { } paid || (use.LeJiuCuijin ?? []).Any(g => g.Source == before.Source))
            throw new InvalidOperationException("Cuijin cannot arm before its exact real discard invoice or pay one source twice.");
        var grant = new LeJiuCuijinGrant(f.Id, before.Source, before.Hash, use.Id, before.ActionId, before.Actor, before.Turn,
            paid.Invocation.InvocationId, paid.Material.CardId, paid.Material.From, paid.Material.To, paid.Material.CompletionSequence);
        ReplaceRuntimeTop(f with { LeJiuCuijin = before with { Grant = grant } });
        UpdateLifecycleCardUse(use.Id, u => u with { LeJiuCuijin = (u.LeJiuCuijin ?? []).Append(grant).ToArray() });
        AdvanceEventRulesAndQueueFact(new LeJiuCuijinArmedEvent(grant));
    }
    private (NativeLogicalDiscardInvocationRecordedEvent Invocation, NativeLogicalDiscardMaterialCompletedEvent Material)? LeJiuPaidCost(ProgramSkillFrame f)
    {
        if (f.LeJiuCuijin is not { Reward: false } r) return null;
        var invoices = ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>().Where(e => e.ParentFrameId == f.Id && e.Reason.Value == LeJiuCostReason).ToArray();
        if (invoices is not [var invoice] || invoice.ActualCount != 1 || invoice.InitiatorSeat != f.OwnerSeat || invoice.ExecutorSeat != f.OwnerSeat ||
            invoice.SubjectSeat != f.OwnerSeat || invoice.TurnNumber != r.Turn || invoice.SequenceBefore < r.Before || invoice.SequenceAfter > _movementSequence ||
            ProgramEventHistory<NativeLogicalDiscardMaterialCompletedEvent>().Where(e => e.InvocationId == invoice.InvocationId).ToArray() is not [var material] ||
            material.From.OwnerSeat != f.OwnerSeat || material.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            material.CompletionSequence <= invoice.SequenceBefore || material.CompletionSequence > invoice.SequenceAfter ||
            !_cardMovements.Any(m => m.Sequence == material.CompletionSequence && m.CardId == material.CardId && m.To == material.To &&
                (m.From == material.From || GetProgramDiscardSource(m) == material.From) &&
                (m.To == CardLocation.DiscardPile || IsEphemeralCardDestroyedMaterial(m) || IsRedirectedPaidDiscardMovement(m, material.From, new(LeJiuCostReason))))) return null;
        return (invoice, material);
    }
    private bool ValidLeJiuGrant(CardUseFrame use, LeJiuCuijinGrant g) => use.Action is { Type: CardActionType.Use } a &&
        g.Source.SkillId == AnniversaryLeJiuComposition.Skill && g.Source.BindingId == AnniversaryLeJiuComposition.Pay &&
        _contentRegistry.GetSkill(g.Source.SkillId).Program?.GameplayHash == g.Hash && g.UseId == use.Id && g.ActionId == a.ActionId &&
        g.Actor == use.SourceSeat && g.Actor == a.ActorSeat && a.EffectiveKind == use.CardKind && (IsSlashCard(use.CardKind) || use.CardKind == CardKind.Duel) &&
        ProgramEventHistory<LeJiuCuijinArmedEvent>().Count(e => e.Grant == g) == 1 &&
        ProgramEventHistory<LeJiuCuijinQualifiedEvent>().Count(e => e.FrameId == g.FrameId && e.Source == g.Source && e.Hash == g.Hash &&
            e.UseId == g.UseId && e.ActionId == g.ActionId && e.Actor == g.Actor && e.Turn == g.Turn && e.Before < g.MovementSequence) == 1 &&
        ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.InvocationId == g.InvocationId && e.ParentFrameId == g.FrameId &&
            e.InitiatorSeat == g.Source.OwnerSeat && e.ExecutorSeat == g.Source.OwnerSeat && e.SubjectSeat == g.Source.OwnerSeat && e.ActualCount == 1 &&
            e.Reason.Value == LeJiuCostReason && e.SequenceBefore < g.MovementSequence && e.SequenceAfter >= g.MovementSequence) == 1 &&
        ProgramEventHistory<NativeLogicalDiscardMaterialCompletedEvent>().Count(e => e.InvocationId == g.InvocationId && e.CardId == g.CardId &&
            e.From == g.From && e.To == g.To && e.CompletionSequence == g.MovementSequence) == 1;
    [NativeUseValidation(44100)] private void AssertLeJiuNativeUse(CardUseFrame use)
    {
        if (use.LeJiuCuijin is { } grants && (grants.Count == 0 || grants.Select(g => g.Source).Distinct().Count() != grants.Count || grants.Any(g => !ValidLeJiuGrant(use, g))))
            throw new InvalidOperationException("Cuijin lost a real native use's immutable paid source grant.");
    }
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 44100)] private int LeJiuCuijinDamage(IDamageAttempt attack)
    {
        if (attack.IsChainPropagation || LifecycleCardUse(attack.ResolutionId) is not { LeJiuCuijin: { } grants } use) return 0;
        if (grants.Any(g => !ValidLeJiuGrant(use, g))) throw new InvalidOperationException("Cuijin damage must belong to its original paid whole native use.");
        return grants.Count;
    }
}
