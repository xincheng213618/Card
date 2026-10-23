namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void AppendDagongCheIssuedDamageCandidates(IDamageAttempt attack, SkillProgramTriggerWindow window, List<DamageTriggerCandidate> result)
    {
        if (window != SkillProgramTriggerWindow.AfterDamageApplied || attack.IsSourceLess || !attack.DamageWasApplied ||
            LifecycleCardUse(attack.ResolutionId) is not { DagongCheSlash: { } o } use || !_players[o.Source.OwnerSeat].IsAlive ||
            !_players[attack.TargetSeat].IsAlive || !HasDiscardableHeBy(o.Source.OwnerSeat, attack.TargetSeat) ||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == o.FrameId) is not { } parent || !ExactDagongCheUse(use, parent)) return;
        var t = _contentRegistry.GetSkill(ZhangFenComposition.Cart).Program!.Triggers.Single(t => t.Id == "cart-slash-discard");
        result.RemoveAll(c => c.OwnerSeat == o.Source.OwnerSeat && c.ProgramId == ZhangFenComposition.Cart && c.ProgramTriggerId == t.Id);
        result.Add(new(o.Source.OwnerSeat, $"dagong-che:{use.Id}:{attack.TargetSeat}", t.Priority, false,
            ZhangFenComposition.Cart, t.Id, o.Source.SkillInstanceId!, o.Hash, 0));
    }
    [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate, 43500)]
    private bool IsIssuedDagongCheDamageCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext w)
    {
        if (c.SkillId != ZhangFenComposition.Cart || c.BindingId != "cart-slash-discard" || c.OccurrenceIndex != 0 || w.OccurrenceIndex != 0 ||
            w.Window != SkillProgramTriggerWindow.AfterDamageApplied || w.Amount is not > 0 || w.DamageFrameId is not { } damageId ||
            _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(p => p.Id == w.ParentFrameId) is not { } parent ||
            parent.ParentFrameId != damageId || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            parent.Candidates[parent.CandidateIndex].ToProgramCandidate() != c ||
            _resolutionStack.OfType<DamageFrame>().SingleOrDefault(d => d.Id == damageId) is not { } damage || damage.TargetSeat != w.TargetSeat || damage.Amount != w.Amount ||
            LifecycleCardUse(damage.ParentFrameId) is not { DagongCheSlash: { } o } use ||
            c.OwnerSeat != o.Source.OwnerSeat || w.OwnerSeat != c.OwnerSeat || w.SourceSeat != c.OwnerSeat || c.SkillInstanceId != o.Source.SkillInstanceId ||
            c.GameplayHash != o.Hash || !_players[c.OwnerSeat].IsAlive || !_players[damage.TargetSeat].IsAlive || !HasDiscardableHeBy(c.OwnerSeat, damage.TargetSeat) ||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == o.FrameId) is not { } f || !ExactDagongCheUse(use, f)) return false;
        return !ProgramEventHistory<ZhangFenStartedEvent>().Any(e => e.Op == SkillProgramEffectOp.ResolveAnniversaryDagongCheAttack && e.ParentId == parent.Id);
    }
    private bool IsUnbegunIssuedDagongCheDamage(ProgramSkillFrame f) => f.ZhangFen is null && f.InstructionIndex == 0 &&
        f.WindowContext is { } w && IsIssuedDagongCheDamageCandidate(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f),
            f.SkillInstanceId, f.GameplayHash, 0, OccurrenceIndex: w.OccurrenceIndex), w);
    private bool HasBoundDagongCheDiscardOrigin(ProgramSkillFrame f) => f.ZhangFen is { } r && f.WindowContext is { DamageFrameId: { } damageId } w &&
        _resolutionStack.OfType<DamageFrame>().SingleOrDefault(d => d.Id == damageId) is { } damage && damage.SourceSeat == f.OwnerSeat &&
        damage.TargetSeat == r.TargetSeat && damage.TargetSeat == w.TargetSeat && damage.Amount == w.Amount &&
        LifecycleCardUse(damage.ParentFrameId) is { DagongCheSlash: { } o } use && o.CartId == r.CartId &&
        o.Source.OwnerSeat == f.OwnerSeat && o.Source.SkillInstanceId == f.SkillInstanceId && o.Hash == r.Hash &&
        _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(p => p.Id == o.FrameId) is { } producer && ExactDagongCheUse(use, producer);
}
