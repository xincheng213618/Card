namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsSpSunShangxiangFanxiangTrigger(SkillProgramTrigger t) => t.Effects.Any(e => e.Op == SkillProgramEffectOp.CompleteSpSunShangxiangFanxiang);
    private IEnumerable<int> SpSunShangxiangHelpedSeats(int owner) => ProgramEventHistory<SpSunShangxiangHelpedEvent>().Where(e => e.OwnerSeat == owner).Select(e => e.HelpedSeat).Distinct();
    private int? SpSunShangxiangFanxiangWitness(int owner) => SpSunShangxiangHelpedSeats(owner).Where(s => _players[s].IsAlive && _players[s].HasHpValue && _players[s].Hp < _players[s].MaxHp)
        .OrderBy(s => (s - owner + _playerCount) % _playerCount).Select(s => (int?)s).FirstOrDefault();
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.Collection, 32301)]
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 32301)]
    private bool CanOfferSpSunShangxiangFanxiang(int ownerSeat, SkillProgramTrigger t) => !IsSpSunShangxiangFanxiangTrigger(t) ||
        !ProgramEventHistory<SpSunShangxiangFanxiangAcceptedEvent>().Any(e => e.Receipt.Source.OwnerSeat == ownerSeat) && SpSunShangxiangFanxiangWitness(ownerSeat) is not null;
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 32301)]
    private void AcceptSpSunShangxiangFanxiang(ProgramBindingStartedEvent started)
    {
        if (started.SkillId != SpSunShangxiangOlComposition.Fanxiang) return;
        var f = GetActiveProgramFrame(started.FrameId);
        if (f.SpSunShangxiangFanxiang is not null || f.InstructionIndex != 0 || f.ActivationId != f.TriggerId || f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } context ||
            context.SourceSeat != f.OwnerSeat || SpSunShangxiangFanxiangWitness(f.OwnerSeat) is not { } seat ||
            !CanOfferSpSunShangxiangFanxiang(f.OwnerSeat, GetProgramTrigger(f))) throw new InvalidOperationException("Fanxiang requires an unused actual Preparation with a living wounded assisted role.");
        var r = new SpSunShangxiangFanxiangReceipt(SpSunShangxiangSource(f), f.GameplayHash, context.ParentFrameId, seat, _players[f.OwnerSeat].MaxHp);
        ReplaceRuntimeTop(f with { SpSunShangxiangFanxiang = r }); AdvanceEventRulesAndQueueFact(new SpSunShangxiangFanxiangAcceptedEvent(f.Id, r));
    }
    private bool ExactSpSunShangxiangFanxiangParent(ProgramSkillFrame f, long id)
    {
        var n = _resolutionStack.FindIndex(x => x.Id == f.Id);
        return n > 0 && _resolutionStack[n - 1] is ProgramLifecycleTriggerWindowFrame parent && parent.Id == id && parent.OwnerSeat == f.OwnerSeat &&
            parent.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count &&
            MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex]);
    }
    private bool ValidSpSunShangxiangFanxiang(ProgramSkillFrame f) => f.SpSunShangxiangFanxiang is { } r && r.Source == SpSunShangxiangSource(f) && r.GameplayHash == f.GameplayHash &&
        f.SkillId == SpSunShangxiangOlComposition.Fanxiang && f.TriggerId == "assisted-wounded-awakening" && f.ActivationId == f.TriggerId && f.WindowContext is { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } w &&
        w.ParentFrameId == r.LifecycleId && w.SourceSeat == f.OwnerSeat && _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash == f.GameplayHash &&
        ProgramEventHistory<SpSunShangxiangFanxiangAcceptedEvent>().Count(e => e == new SpSunShangxiangFanxiangAcceptedEvent(f.Id, r)) == 1 &&
        ProgramEventHistory<SpSunShangxiangFanxiangAcceptedEvent>().Count(e => e.Receipt.Source.OwnerSeat == f.OwnerSeat) == 1 &&
        SpSunShangxiangHelpedSeats(f.OwnerSeat).Contains(r.QualifiedSeat) && r.MaximumBefore > 0 && ExactSpSunShangxiangFanxiangParent(f, r.LifecycleId);
    private SkillProgramStepOutcome CompleteSpSunShangxiangFanxiang(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!ValidSpSunShangxiangFanxiang(f) || f.InstructionIndex != 3 ||
            ProgramEventHistory<SpSunShangxiangFanxiangCompletedEvent>().Any(e => e.FrameId == f.Id)) throw new InvalidOperationException("Fanxiang can replace skills only after its native maximum/recovery instructions have returned.");
        var owner = _players[f.OwnerSeat];
        foreach (var grant in owner.SkillGrants.Grants.Where(g => g.IsEnabled && g.SkillId == SpSunShangxiangOlComposition.Liangzhu).ToArray()) owner.SkillGrants.RemoveGrant(grant.GrantId);
        GrantProgramSkills(f, [SpSunShangxiangOlComposition.Xiaoji]);
        AdvanceEventRulesAndQueueFact(new ProgramOwnerSkillsReplacedEvent(f.Id, f.SkillId, f.OwnerSeat, [SpSunShangxiangOlComposition.Liangzhu], SpSunShangxiangOlComposition.Xiaoji));
        AdvanceEventRulesAndQueueFact(new SpSunShangxiangFanxiangCompletedEvent(f.Id, f.OwnerSeat, owner.MaxHp));
        ReplaceRuntimeTop(GetActiveProgramFrame(f.Id) with { SpSunShangxiangFanxiang = null }); return SkillProgramStepOutcome.Continue;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 32301)]
    private bool ContinueAcceptedSpSunShangxiangFanxiang(ProgramSkillFrame f) => _players[f.OwnerSeat].IsAlive && ValidSpSunShangxiangFanxiang(f);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 32301)]
    private void AssertSpSunShangxiangFanxiang(ProgramSkillFrame f)
    { if (f.SpSunShangxiangFanxiang is not null && !ValidSpSunShangxiangFanxiang(f)) throw new InvalidOperationException("Fanxiang lost its once-per-game accepted Preparation receipt."); }
}
