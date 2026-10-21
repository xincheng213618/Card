namespace CardGame.Core;
internal sealed partial class MoCaoCaoBachaoRuntime
{
    internal IReadOnlyList<ProgramTriggerCandidate> IncludeBaibi(int ownerSeat, SkillProgramTriggerWindow window, IReadOnlyList<ProgramTriggerCandidate> ordinary)
    {
        var result = ordinary.Where(c => c.SkillId != MoCaoCaoComposition.Baibi).ToArray();
        if (window != SkillProgramTriggerWindow.AfterDamageApplied || facts.DamageOrigin is not { } origin ||
            origin.SourceLess || origin.Amount <= 0 || origin.ActorSeat != ownerSeat || !IsSlash(origin.Kind) || !facts.Participant(ownerSeat).Alive ||
            !facts.Participant(origin.TargetSeat).Alive || facts.Hand(origin.TargetSeat).Count == 0 || facts.WeaponAuthorization(ownerSeat,CardKind.BaiBiDao) is not { } weapon ||
            facts.Program(MoCaoCaoComposition.Baibi) is not { } program) return result;
        var instance = Instance(origin,weapon);
        return Array.AsReadOnly(result.Append(new(ownerSeat,MoCaoCaoComposition.Baibi,MoCaoCaoComposition.BaibiBinding,instance,program.GameplayHash,0)).ToArray());
    }
    private static bool IsSlash(CardKind k) => k is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;
    private static string Instance(MoCaoCaoDamageOrigin origin, MoCaoCaoWeaponAuthorization source) =>
        $"native-baibi:{origin.DamageId}:{origin.TargetSeat}:{source.SourceId}";
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 33801)]
    private void FreezeBaibi(IGameEvent payload)
    {
        if (payload is not DamageTriggerWindowOpenedEvent window || frames.Find(window.ResolutionId) is not DamageTriggerWindowFrame native ||
            native.TriggerWindow != SkillProgramTriggerWindow.AfterDamageApplied || facts.DamageOrigin is not { } origin || origin.SourceLess || origin.Amount <= 0 ||
            origin.DamageId != window.DamageFrameId || origin.ActorSeat != window.SourceSeat || origin.TargetSeat != window.TargetSeat || !IsSlash(origin.Kind) ||
            facts.WeaponAuthorization(origin.ActorSeat,CardKind.BaiBiDao) is not { } source || facts.Program(MoCaoCaoComposition.Baibi) is not { } p) return;
        var instance = Instance(origin,source);
        if (!window.Candidates.Any(c => c.ProgramId == MoCaoCaoComposition.Baibi && c.ProgramTriggerId == MoCaoCaoComposition.BaibiBinding && c.OwnerSeat == origin.ActorSeat && c.SkillInstanceId == instance && c.GameplayHash == p.GameplayHash)) return;
        events.AdvanceFact(new MoCaoCaoBaibiIssuedEvent(new(window.ResolutionId,origin.DamageId,origin.UseId,origin.ActionId,origin.ActorSeat,origin.TargetSeat,
            source.PhysicalCardId,source.VirtualIssueId,p.GameplayHash,instance,source.SourceId)));
    }
    private bool RetainIssuedBaibi(ProgramSkillFrame f)
    {
        if(f.MoCaoCaoBachao is not null || f.MoCaoCaoBaibi is not null || f.SkillId!=MoCaoCaoComposition.Baibi ||
            f.TriggerId!=MoCaoCaoComposition.BaibiBinding || f.ActivationId!=f.TriggerId || f.InstructionIndex is not (0 or 1) ||
            f.SelectedCardIds.Count!=0 || f.SelectedTargetSeats.Count!=0 || !facts.Participant(f.OwnerSeat).Alive ||
            facts.Program(f.SkillId)?.GameplayHash!=f.GameplayHash || f.WindowContext is not {Window:SkillProgramTriggerWindow.AfterDamageApplied} context ||
            frames.ParentOf(f.Id) is not DamageTriggerWindowFrame parent || parent.Id!=context.ParentFrameId ||
            parent.CandidateIndex<0 || parent.CandidateIndex>=parent.Candidates.Count || parent.Candidates[parent.CandidateIndex].ToProgramCandidate() is not { } candidate ||
            candidate.OwnerSeat!=f.OwnerSeat || candidate.SkillId!=f.SkillId || candidate.BindingId!=f.TriggerId || candidate.SkillInstanceId!=f.SkillInstanceId ||
            candidate.GameplayHash!=f.GameplayHash || candidate.OccurrenceIndex!=context.OccurrenceIndex ||
            facts.History<ProgramBindingStartedEvent>().Count(e=>e.FrameId==f.Id && e.SkillId==f.SkillId && e.BindingId==f.TriggerId &&
                e.SkillInstanceId==f.SkillInstanceId && e.OwnerSeat==f.OwnerSeat && e.Window==context.Window)!=1) return false;
        return IssuedBaibi(candidate,context);
    }
    [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate, 33800)]
    private bool IssuedBaibi(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        candidate.SkillId == MoCaoCaoComposition.Baibi && candidate.BindingId == MoCaoCaoComposition.BaibiBinding &&
        context.Window == SkillProgramTriggerWindow.AfterDamageApplied && facts.History<MoCaoCaoBaibiIssuedEvent>().Any(e =>
            e.Claim.DamageWindowId == context.ParentFrameId && e.Claim.InstanceId == candidate.SkillInstanceId && e.Claim.GameplayHash == candidate.GameplayHash &&
            e.Claim.ActorSeat == candidate.OwnerSeat && e.Claim.ActorSeat == context.SourceSeat && e.Claim.TargetSeat == context.TargetSeat) && facts.ExactIssuedCandidate(candidate,context);
}
