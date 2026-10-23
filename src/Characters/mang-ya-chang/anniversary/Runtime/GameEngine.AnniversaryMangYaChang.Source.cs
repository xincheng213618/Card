namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasAnniversaryJiedao => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ChooseAnniversaryMangYaChangJiedao);
    private int JiedaoChainCursor(IDamageAttempt attack) => attack.IsChainPropagation ? attack is ProgramAttackHandle
        ? GetProgramAttackState(attack.ResolutionId).ChainedTargetIndex : GetCardAttackState(attack.ResolutionId).ChainedTargetIndex : 0;
    private bool FirstActualJiedaoSourceDamage(int seat) => !EventsSinceLastBoundary(e => e is TurnStartedEvent)
        .OfType<DamageAppliedEvent>().Any(e => e.Amount > 0 && !e.SourceLess && e.SourceSeat == seat);

    // Called before the ordinary finalizer. This frame has only the source's
    // optional Jiedao candidate, and never substitutes for the victim window.
    private bool TryBeginAnniversaryJiedaoSourceWindow(IDamageAttempt attack)
    {
        if (!HasAnniversaryJiedao || attack.IsSourceLess || attack.DamageAmountFinalized || attack.DamageWasApplied || attack.DamageAmount <= 0 ||
            !IsValidPlayerSeat(attack.SourceSeat) || !_players[attack.SourceSeat].IsAlive ||
            !HasRuntimeSkill(_players[attack.SourceSeat], AnniversaryJiedaoComposition.Skill) || !FirstActualJiedaoSourceDamage(attack.SourceSeat)) return false;
        var cursor = JiedaoChainCursor(attack);
        if (ProgramEventHistory<AnniversaryJiedaoWindowIssuedEvent>().Any(e => e.Receipt.ActualTurn == _turnNumber && e.Receipt.AttackId == attack.ResolutionId &&
            e.Receipt.Target == attack.TargetSeat && e.Receipt.ChainCursor == cursor && e.Receipt.ChainPropagation == attack.IsChainPropagation)) return false;
        var owner = _players[attack.SourceSeat];
        if (!owner.HasHpValue || owner.MaxHp - owner.Hp <= 0) return false;
        var candidate = CollectProgramTriggerCandidates(owner, SkillProgramTriggerWindow.BeforeDamageApplied)
            .FirstOrDefault(c => c.SkillId == AnniversaryJiedaoComposition.Skill && c.BindingId == AnniversaryJiedaoComposition.Choose);
        if (candidate is null) return false;
        var parent = _resolutionStack.LastOrDefault()?.Id ?? throw new InvalidOperationException("Jiedao requires a real native damage owner.");
        var id = ++_resolutionSequence;
        var receipt = new AnniversaryJiedaoSourceReceipt(id, parent, attack.ResolutionId,
            new(candidate.SkillId, candidate.BindingId, candidate.OwnerSeat, candidate.SkillInstanceId), candidate.GameplayHash, _turnNumber,
            attack.TargetSeat, cursor, attack.IsChainPropagation, attack.DamageAmount, owner.MaxHp - owner.Hp);
        var window = new BeforeDamageProgramWindowFrame(id, parent, attack.SourceSeat, attack.TargetSeat, attack.DamageAmount, GetDamageNature(attack),
            BeforeDamageProgramContinuation.Attack, Array.AsReadOnly(new[] { new BeforeDamageProgramCandidate(candidate, CaptureProgramTriggerFacts(owner)) }))
        { AnniversaryJiedaoSource = receipt, ContinuationAttackResolutionId = attack.ResolutionId };
        PushRuntimeFrame(window);
        AdvanceEventRulesAndQueueFact(new AnniversaryJiedaoWindowIssuedEvent(receipt));
        AdvanceRuntimeTop<BeforeDamageProgramWindowFrame>(); return true;
    }
    private bool ResumeAnniversaryJiedaoSourceWindow(BeforeDamageProgramWindowFrame window, IDamageAttempt attack)
    {
        if (window.AnniversaryJiedaoSource is not { } r) return false;
        if (!ValidJiedaoOrigin(r) || window.Id != r.WindowId || window.ParentFrameId != r.ParentId || window.CandidateIndex != window.Candidates.Count ||
            window.Prevented || window.RedirectedTargetSeat is not null || attack.ResolutionId != r.AttackId || attack.IsSourceLess || attack.SourceSeat != r.Source.OwnerSeat ||
            attack.TargetSeat != r.Target || JiedaoChainCursor(attack) != r.ChainCursor || attack.IsChainPropagation != r.ChainPropagation ||
            attack.DamageAmountFinalized || attack.DamageAmount != r.OriginalAmount || _resolutionStack.LastOrDefault()?.Id != r.ParentId ||
            ProgramEventHistory<AnniversaryJiedaoWindowReturnedEvent>().Any(e => e.WindowId == r.WindowId))
            throw new InvalidOperationException("Jiedao source choice lost its exact unfinalized native damage return.");
        AdvanceEventRulesAndQueueFact(new AnniversaryJiedaoWindowReturnedEvent(r.WindowId, r.AttackId, r.Target, r.ChainCursor));
        if (!ApplyAttackDamage(attack)) CompleteDamageAttack(attack);
        return true;
    }
    private bool ValidJiedaoOrigin(AnniversaryJiedaoSourceReceipt r) => r.Source.SkillId == AnniversaryJiedaoComposition.Skill &&
        r.Source.BindingId == AnniversaryJiedaoComposition.Choose && _contentRegistry.GetSkill(r.Source.SkillId).Program?.GameplayHash == r.Hash &&
        r.MaximumBonus > 0 && r.OriginalAmount > 0 && r.ActualTurn == _turnNumber &&
        ProgramEventHistory<AnniversaryJiedaoWindowIssuedEvent>().Count(e => e.Receipt == r) == 1;
    private bool ValidJiedaoGrant(AnniversaryJiedaoGrant g) => ValidJiedaoOrigin(g.Origin) && g.Bonus >= 0 && g.Bonus <= g.Origin.MaximumBonus &&
        ProgramEventHistory<AnniversaryJiedaoChosenEvent>().Count(e => e.Grant == g) == 1;
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 44400)] private int JiedaoSourceDamageBonus(IDamageAttempt attack)
    {
        if (!HasAnniversaryJiedao || attack.IsSourceLess) return 0;
        return ProgramEventHistory<AnniversaryJiedaoChosenEvent>().Where(e => e.Grant.Origin.AttackId == attack.ResolutionId && e.Grant.Origin.Target == attack.TargetSeat &&
            e.Grant.Origin.Source.OwnerSeat == attack.SourceSeat && e.Grant.Origin.ChainPropagation == attack.IsChainPropagation && e.Grant.Origin.ChainCursor == JiedaoChainCursor(attack) &&
            e.Grant.Origin.ActualTurn == _turnNumber && ValidJiedaoGrant(e.Grant)).Sum(e => e.Grant.Bonus);
    }
    private AnniversaryJiedaoSourceReceipt? JiedaoSourceWitness(ProgramTriggerCandidate c, ProgramSkillWindowContext w) =>
        w.Window == SkillProgramTriggerWindow.BeforeDamageApplied && _resolutionStack.OfType<BeforeDamageProgramWindowFrame>().SingleOrDefault(p => p.Id == w.ParentFrameId) is
        { AnniversaryJiedaoSource: { } r } parent && ValidJiedaoOrigin(r) && c.SkillId == r.Source.SkillId && c.BindingId == r.Source.BindingId &&
        c.OwnerSeat == r.Source.OwnerSeat && c.SkillInstanceId == r.Source.SkillInstanceId && c.GameplayHash == r.Hash && w.OwnerSeat == c.OwnerSeat && w.SourceSeat == c.OwnerSeat &&
        w.TargetSeat == r.Target && w.Amount == r.OriginalAmount && parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count &&
        parent.Candidates[parent.CandidateIndex].Candidate == c && CurrentDamageAttempt is { IsSourceLess: false, DamageAmountFinalized: false } a &&
        a.ResolutionId == r.AttackId && a.TargetSeat == r.Target && a.SourceSeat == c.OwnerSeat && JiedaoChainCursor(a) == r.ChainCursor ? r : null;

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 44400,
        SkillProgramEffectOp.ChooseAnniversaryMangYaChangJiedao, SkillProgramEffectOp.PayAnniversaryMangYaChangJiedao)]
    private bool CanOfferAnniversaryJiedao(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w) =>
        t.Id == AnniversaryJiedaoComposition.Choose ? JiedaoSourceWitness(c, w) is not null && FirstActualJiedaoSourceDamage(c.OwnerSeat) :
            _players[c.OwnerSeat].IsAlive && w.TargetSeat is { } target && _players[target].IsAlive && JiedaoCostWitness(c, w) is not null;
}
