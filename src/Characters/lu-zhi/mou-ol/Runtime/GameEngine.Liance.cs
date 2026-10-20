namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 32902, SkillProgramEffectOp.ResolveMouLuZhiLiance)]
    private bool CanOfferMouLzLiance(ProgramTriggerLegalityContext g)
    {
        if (g.WindowContext?.MovementBatch is not { } b || !HasOngoingWholeActualUseTurn || !_players[g.OwnerSeat].IsAlive || ProgramEventHistory<MouLuZhiOlStartedEvent>().Any(e => e.Source.OwnerSeat == g.OwnerSeat && e.Op == SkillProgramEffectOp.ResolveMouLuZhiLiance && e.BatchId == b.Id)) return false;
        var loc = CardLocation.Hand(g.OwnerSeat); var changes = b.SourceCounts.Concat(b.DestinationCounts ?? []).Where(c => c.Location == loc).ToArray();
        if (changes.Length == 0 || changes.All(c => c.CountBefore == c.CountAfter)) return false;
        var count = GetHand(g.Owner).Count;
        return _players.Where(p => p.IsAlive).All(p => GetHand(p).Count >= count);
    }
    private void StartMouLzDraw(ProgramSkillFrame f)
    {
        var r = f.MouLuZhiOl!; var amount = Math.Max(0, _players[f.OwnerSeat].MaxHp - GetHand(_players[f.OwnerSeat]).Count);
        ReplaceRuntimeTop(f = f with { MouLuZhiOl = r with { Requested = amount, Paid = true } }); BeginOperationProgramDraw(f, f.OwnerSeat, amount, new(MouLzDraw));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouLuZhiLiance)] private void ReturnMouLzDraw(ProgramSkillFrame f, NativeDrawOutcome o)
    {
        if (f.MouLuZhiOl is not { Stage: MouLuZhiOlStage.Drawing, Paid: true, Draw: null } r || o.RecipientSeat != f.OwnerSeat || o.RequestedCount != r.Requested || o.SequenceBefore < 0 || o.SequenceAfter < o.SequenceBefore || o.SequenceAfter > _movementSequence ||
            o.AttemptFrameId is { } attempt && ProgramEventHistory<NativeDrawAttemptDecidedEvent>().Count(e => e.AttemptFrameId == attempt && e.ParentFrameId == f.Id && e.RecipientSeat == f.OwnerSeat && e.RequestedCount == r.Requested && e.Replaced == (o.Replacement is not null)) != 1) throw new InvalidOperationException("Liance must return its exact requested native draw.");
        ReplaceRuntimeTop(f with { MouLuZhiOl = r with { Stage = MouLuZhiOlStage.DrawChildren, Draw = o, Before = o.SequenceBefore, After = o.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouLuZhiOlDrawReturnedEvent(f.Id, o.RequestedCount, o.ActualDrawIds.Count, o.ReplacementGainedIds.Count, o.AttemptFrameId)); ContinueMouLz(f.Id);
    }
    private int MouLzChainCursor(IDamageAttempt a) => a is ProgramAttackHandle ? GetProgramAttackState(a.ResolutionId).ChainedTargetIndex : GetCardAttackState(a.ResolutionId).ChainedTargetIndex;
    private void SetMouLzChainAmount(IDamageAttempt a, int amount)
    { if (!SameAttackOwner(a, CurrentDamageAttempt) || !a.IsChainPropagation) throw new InvalidOperationException("Liance chain return requires the exact current damage instance."); if (a is ProgramAttackHandle) UpdateProgramAttackState(a.ResolutionId, s => s with { DamageAmount = amount }); else UpdateCardAttackState(a.ResolutionId, s => s! with { DamageAmount = amount }); }
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 32902)] private int MouLzLianceBonus(IDamageAttempt attack)
    {
        if (attack.IsSourceLess || !HasOngoingWholeActualUseTurn) return 0;
        var grants = ProgramEventHistory<MouLuZhiOlLianceArmedEvent>().Where(e => e.Turn == _turnNumber && !ProgramEventHistory<MouLuZhiOlLianceConsumedEvent>().Any(c => c.FrameId == e.FrameId)).ToArray();
        foreach (var e in grants) AdvanceEventRulesAndQueueFact(new MouLuZhiOlLianceConsumedEvent(e.FrameId, attack.ResolutionId, attack.SourceSeat, attack.TargetSeat, _turnNumber, attack.IsChainPropagation, attack.IsChainPropagation ? MouLzChainCursor(attack) : 0)); return grants.Length;
    }
    [ProgramDamageHook(ProgramDamagePhase.BeforeChainAdvance, 32902)] private void BeforeMouLzChain(IDamageAttempt attack)
    {
        if (!attack.IsChainPropagation || attack.IsSourceLess) return; var cursor = MouLzChainCursor(attack);
        var remaining = attack is ProgramAttackHandle ? GetProgramAttackState(attack.ResolutionId).ChainedTargetSeats : GetCardAttackState(attack.ResolutionId).ChainedTargetSeats;
        if (!remaining.Skip(cursor).Any(s => _players[s].IsAlive) || ProgramEventHistory<MouLuZhiOlChainIssuedEvent>().SingleOrDefault(e => e.AttackId == attack.ResolutionId && e.Cursor == cursor) is not { } issued || !ProgramEventHistory<MouLuZhiOlLianceConsumedEvent>().Any(e => e.AttackId == attack.ResolutionId && e.Chain && e.Cursor == cursor) || ProgramEventHistory<MouLuZhiOlChainRebasedEvent>().Any(e => e.AttackId == attack.ResolutionId && e.Cursor == cursor)) return;
        SetMouLzChainAmount(attack, issued.Inherited); AdvanceEventRulesAndQueueFact(new MouLuZhiOlChainRebasedEvent(attack.ResolutionId, cursor));
    }
    [ProgramDamageHook(ProgramDamagePhase.AfterChainAdvance, 32902)] private void AfterMouLzChain(IDamageAttempt attack)
    {
        if (!attack.IsChainPropagation || attack.IsSourceLess || !ProgramEventHistory<MouLuZhiOlLianceArmedEvent>().Any(e => e.Turn == _turnNumber && !ProgramEventHistory<MouLuZhiOlLianceConsumedEvent>().Any(c => c.FrameId == e.FrameId))) return;
        var cursor = MouLzChainCursor(attack); AdvanceEventRulesAndQueueFact(new MouLuZhiOlChainIssuedEvent(attack.ResolutionId, cursor, attack.DamageAmount)); if (attack.DamageAmountFinalized) SetMouLzChainAmount(attack, checked(attack.DamageAmount + MouLzLianceBonus(attack)));
    }
}
