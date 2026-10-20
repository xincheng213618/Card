namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 33200, SkillProgramEffectOp.ResolveMouJiangWeiZhiji)]
    private bool CanAwakenMouJw(ProgramTriggerLegalityContext g) => g.SkillId == MouJiangWeiComposition.Zhiji && g.Context.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && g.Context.SourceSeat == g.OwnerSeat &&
        CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Preparation } p && p.ActorSeat == g.OwnerSeat && g.Owner.IsAlive && MouJwSelectedCount(g.OwnerSeat) >= 4 && !ProgramEventHistory<MouJiangWeiAwakenedEvent>().Any(e => e.OwnerSeat == g.OwnerSeat);
    private void GrantMouJwBeifa(ProgramSkillFrame f)
    {
        var r = f.MouJiangWei!;
        if (r.Op != SkillProgramEffectOp.ResolveMouJiangWeiZhiji || r.Stage != MouJiangWeiStage.MarkTargets || !ProgramEventHistory<MouJiangWeiAwakenedEvent>().Any(e => e.FrameId == f.Id)) throw new InvalidOperationException("Beifa needs its actual paid awakening.");
        foreach (var seat in r.Targets.Where(s => _players[s].IsAlive))
        {
            var player = _players[seat]; var before = player.Markers.GetValueOrDefault(PlayerMarkerKind.MouJiangWeiBeifa); var key = (PlayerMarkerKind.MouJiangWeiBeifa, f.OwnerSeat);
            player.Markers[PlayerMarkerKind.MouJiangWeiBeifa] = before + 1; player.MarkerSourceCounts[key] = player.MarkerSourceCounts.GetValueOrDefault(key) + 1;
            AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(f.Id, seat, PlayerMarkerKind.MouJiangWeiBeifa, 1, before + 1, f.OwnerSeat, f.SkillId));
            AdvanceEventRulesAndQueueFact(new MouJiangWeiBeifaGrantedEvent(f.Id, r.Source, r.GameplayHash, seat, CompleteProgramEventHistory().OfType<TurnStartedEvent>().Count()));
        }
    }
    private IEnumerable<MouJiangWeiBeifaGrantedEvent> ActiveMouJwBeifa(int holder) => ProgramEventHistory<MouJiangWeiBeifaGrantedEvent>().Where(e => e.HolderSeat == holder && !ProgramEventHistory<MouJiangWeiBeifaExpiredEvent>().Any(x => x.FrameId == e.FrameId && x.HolderSeat == holder));
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 33200)]
    private void ExpireMouJwBeifa(TurnStartedEvent turn)
    {
        var ordinal = CompleteProgramEventHistory().OfType<TurnStartedEvent>().Count();
        foreach (var grant in ProgramEventHistory<MouJiangWeiBeifaGrantedEvent>().Where(e => e.Source.OwnerSeat == turn.ActorSeat && e.TurnOrdinal < ordinal && !ProgramEventHistory<MouJiangWeiBeifaExpiredEvent>().Any(x => x.FrameId == e.FrameId && x.HolderSeat == e.HolderSeat)).ToArray())
        {
            var p = _players[grant.HolderSeat]; var count = p.Markers.GetValueOrDefault(PlayerMarkerKind.MouJiangWeiBeifa); var key = (PlayerMarkerKind.MouJiangWeiBeifa, turn.ActorSeat); var sourceCount = p.MarkerSourceCounts.GetValueOrDefault(key);
            if (count <= 1) p.Markers.Remove(PlayerMarkerKind.MouJiangWeiBeifa); else p.Markers[PlayerMarkerKind.MouJiangWeiBeifa] = count - 1;
            if (sourceCount <= 1) p.MarkerSourceCounts.Remove(key); else p.MarkerSourceCounts[key] = sourceCount - 1;
            AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(grant.FrameId, p.Seat, PlayerMarkerKind.MouJiangWeiBeifa, -1, Math.Max(0, count - 1), turn.ActorSeat, MouJiangWeiComposition.Zhiji));
            AdvanceEventRulesAndQueueFact(new MouJiangWeiBeifaExpiredEvent(grant.FrameId, p.Seat, ordinal));
        }
    }
    [ProgramTargetRule(ProgramTargetRulePoint.DirectedProhibition, 33200)]
    [ProgramTargetRule(ProgramTargetRulePoint.DeclaredGlobalProhibition, 33200)]
    private bool MouJwBeifaProhibits(ProgramDirectedTargetQuery q) => ActiveMouJwBeifa(q.ActorSeat).Any(e => q.TargetSeat != q.ActorSeat && q.TargetSeat != e.Source.OwnerSeat && (q.ExistingUseFrameId is not { } id || id > e.FrameId));
    [ProgramCardLegality(ProgramCardLegalityStage.LegalActionProjection, 33200)]
    private LegalAction? ProjectMouJwBeifa(ProgramLegalActionQuery q)
    {
        if (!ActiveMouJwBeifa(q.Actor.Seat).Any() || q.Action.Kind is LegalActionKind.EndPlay or LegalActionKind.Recast or LegalActionKind.UseProgramSkill or LegalActionKind.RevealGeneral) return q.Action;
        if (q.Action.CardId is null && q.Action.Kind == LegalActionKind.UseEquipmentEffect) return q.Action;
        return GetSelfProhibitionPolicyTargets(q.Actor, q.Action).Any(s => ActiveMouJwBeifa(q.Actor.Seat).Any(e => s != q.Actor.Seat && s != e.Source.OwnerSeat)) ? null : q.Action;
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 33201)]
    private void GainMouJwDiscardCharge(NativeLogicalDiscardInvocationRecordedEvent e)
    {
        if (e.Phase is not { Kind: ActualDiscardRecoveryPhaseKind.Discard } phase || phase.ActorSeat != e.ExecutorSeat || e.ActualCount < 1 || !IsValidPlayerSeat(e.ExecutorSeat) || !_players[e.ExecutorSeat].IsAlive || !HasRuntimeSkill(_players[e.ExecutorSeat], MouJiangWeiComposition.Tiaoxin) ||
            ProgramEventHistory<MouJiangWeiChargeChangedEvent>().Any(x => x.DiscardInvocationId == e.InvocationId)) return;
        ChangeMouJwCharge(e.ParentFrameId ?? 0, e.ExecutorSeat, e.ActualCount, e.InvocationId);
    }
}
