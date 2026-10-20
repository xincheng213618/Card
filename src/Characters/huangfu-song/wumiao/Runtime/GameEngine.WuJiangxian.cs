namespace CardGame.Core;
public sealed partial class GameEngine
{
    private WuHuangFuSongJiangxianArmedEvent? WuHfOutstandingArm(int owner) => !HasWuHuangFuSongCapability ? null :
        ProgramEventHistory<WuHuangFuSongJiangxianArmedEvent>().SingleOrDefault(e => e.Source.OwnerSeat == owner && e.Turn == WuHfTurn() &&
            !ProgramEventHistory<WuHuangFuSongJiangxianSettledEvent>().Any(s => s.OwnerSeat == owner && s.Turn == e.Turn));
    private int WuHfPriorDamageCount(int owner)
    {
        var h = CompleteProgramEventHistory().ToArray(); var start = Array.FindLastIndex(h, e => e is TurnStartedEvent t && t.TurnNumber == _turnNumber && t.ActorSeat == _currentSeat);
        return Math.Min(5, h.Skip(start + 1).OfType<DamageAppliedEvent>().Count(e => !e.SourceLess && e.SourceSeat == owner && e.Amount > 0));
    }
    [ProgramDamageHook(ProgramDamagePhase.FactModifiers, 50)]
    private IEnumerable<(CardUseEffectSource Source, int Amount)> WuHfDamageBonuses(IDamageAttempt attack)
    {
        // Native elemental propagation already carries the finalized first
        // damage amount, including this bonus; never add the same bonus twice.
        if (attack.IsChainPropagation || attack.IsSourceLess || attack.SourceSeat != attack.CardUserSeat || !HasIssuedWuHfUse(attack.ResolutionId, attack.SourceSeat) ||
            WuHfOutstandingArm(attack.SourceSeat) is not { } arm) return [];
        var amount = WuHfPriorDamageCount(attack.SourceSeat);
        return amount == 0 ? [] : [(new(arm.Source.SkillId, arm.Source.BindingId, arm.Source.OwnerSeat, arm.Source.SkillInstanceId), amount)];
    }
    private IReadOnlyList<TurnEndingBoundaryItem> IncludeWuHfEndingItems(DeferredTurnEndFrame parent, IReadOnlyList<TurnEndingBoundaryItem> ordinary)
    {
        var arm = WuHfOutstandingArm(parent.OwnerSeat); if (arm is null || !_players[parent.OwnerSeat].IsAlive) return ordinary;
        var items = ordinary.Where(i => i.Candidate is not { SkillId: WuHuangFuSongComposition.Jiangxian, BindingId: "lose-after-turn" }).ToList();
        var c = new ProgramTriggerCandidate(parent.OwnerSeat, WuHuangFuSongComposition.Jiangxian, "lose-after-turn", arm.Source.SkillInstanceId, arm.GameplayHash, 0, 0);
        items.Add(new(TurnEndingBoundaryItemKind.Program, 0, $"wu-huangfu-song:{arm.FrameId}", c, CaptureProgramTriggerFacts(_players[c.OwnerSeat])));
        return Array.AsReadOnly(items.ToArray());
    }
    private bool IsIssuedWuHfEnding(ProgramTriggerCandidate c, ProgramSkillWindowContext context) => c.SkillId == WuHuangFuSongComposition.Jiangxian && c.BindingId == "lose-after-turn" &&
        context.Window == SkillProgramTriggerWindow.AfterTurnEnded && context.OwnerSeat == c.OwnerSeat && context.SourceSeat == c.OwnerSeat &&
        WuHfOutstandingArm(c.OwnerSeat) is { } arm && arm.Source.SkillInstanceId == c.SkillInstanceId && arm.GameplayHash == c.GameplayHash &&
        _resolutionStack.OfType<DeferredTurnEndFrame>().SingleOrDefault(p => p.Id == context.ParentFrameId) is { } parent && IsActualAfterTurnEndedParent(parent) &&
        AfterTurnEndedCandidate(parent) == c && parent.OwnerSeat == c.OwnerSeat && parent.TurnNumber == arm.Turn.Number;

}
