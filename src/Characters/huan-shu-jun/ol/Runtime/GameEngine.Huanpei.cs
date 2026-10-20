using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HuanpeiRoundSpent(int owner) => ProgramEventHistory<HuanpeiAdjustmentAcceptedEvent>().Any(e => e.OwnerSeat == owner && e.Round == _roundNumber);
    private IEnumerable<CardConversionSource> HuanpeiSources(CharacterState owner)
    {
        if (!owner.IsAlive || !owner.HasHpValue || _winner != Winner.None || _roundNumber < 1 || HuanpeiRoundSpent(owner.Seat)) yield break;
        foreach (var instance in GetSkillBindingShard(owner).ProgramInstances)
            foreach (var activation in instance.Program.Activations.Where(a => a.Effects is [{ Op: SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse }]))
                if (HasRuntimeSkillInstance(owner, instance.SkillId, instance.SkillInstanceId))
                    yield return new(instance.SkillId, activation.Id, owner.Seat, instance.SkillInstanceId);
    }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 28100, SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse)]
    private bool CanActivateHuanpei(ProgramActivationLegalityContext c) => c.Owner.IsAlive && c.Owner.HasHpValue && _phase == TurnPhase.Play && c.OwnerSeat == _currentSeat && HuanpeiSources(c.Owner).Any(s => s.SkillId == c.SkillId) && CanAdjustHuanpeiHand(c.Owner);
    private bool CanAdjustHuanpeiHand(CharacterState actor) => actor.Hp <= GetHand(actor).Count ? GetSelfDiscardableOwnedHand(actor).Count >= GetHand(actor).Count - Math.Max(0, actor.Hp) : true;
    private void PayHuanpeiAdjustment(ProgramSkillFrame f)
    {
        var r = f.HuanShuJun!; var owner = _players[f.OwnerSeat]; var hand = GetHand(owner).Count; var hp = Math.Max(0, owner.Hp); var discard = Math.Max(0, hand - hp);
        if (r.Paid || !HuanpeiSources(owner).Contains(r.Source) || !CanAdjustHuanpeiHand(owner) || r.Selected.Count != discard || r.Selected.Distinct().Count() != discard || r.Selected.Any(id => GetSelfDiscardableOwnedHand(owner).All(c => c.Id != id))) throw new InvalidOperationException("Huanpei changed its exact current Hand adjustment cost or paid round.");
        var before = _movementSequence;
        if (r.Request is { Intent: HuanpeiIntent.WenyiPeach, ParentFrameId: { } wenyiId } && _resolutionStack.SingleOrDefault(x => x.Id == wenyiId) is ProgramSkillFrame wenyi && !wenyi.HuanShuJun!.Paid)
            PrepareWenyiSuppliedPeach(wenyi, f.OwnerSeat, [], r.Source);
        ReplaceRuntimeTop(f = f with { HuanShuJun = r = r with { Paid = true, FrozenHandCount = hand, FrozenHp = hp, RequestedCount = Math.Max(0, hp - hand), Before = before, After = before, Stage = HuanShuJunStage.AdjustmentChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new HuanpeiAdjustmentAcceptedEvent(f.Id, f.OwnerSeat, r.Round, hand, hp, Math.Abs(hand - hp), before));
        if (discard > 0)
            MoveProgramLogicalDiscard(f, f.OwnerSeat, r.Selected, new(HuanpeiDiscardReason), (batch, moves) =>
            {
                for (var i = 0; i < moves.Count; i++) { var m = moves[i]; AdvanceEventRulesAndQueueFact(new HuanpeiCostEntityEvent(f.Id, i, m.CardId, m.CardKind, m.From, m.Sequence)); }
            });
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { HuanShuJun = f.HuanShuJun! with { After = _movementSequence } });
        if (r.RequestedCount > 0) { ReplaceRuntimeTop(f = f with { HuanShuJun = f.HuanShuJun! with { Stage = HuanShuJunStage.Drawing } }); BeginOperationProgramDraw(f, f.OwnerSeat, r.RequestedCount, new(HuanpeiDrawReason)); return; }
        AdvanceEventRulesAndQueueFact(new HuanpeiAdjustmentCompletedEvent(f.Id, before, _movementSequence, 0, 0)); ContinueHuanShuJun(f.Id);
    }
}
