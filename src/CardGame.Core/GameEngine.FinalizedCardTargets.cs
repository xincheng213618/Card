namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Optional target windows are enabled by the loaded catalog, retaining old
    // card actions and event shapes for historical content fingerprints.

    private bool HasFinalizedTargetProgram(CardKind kind)
    {
        if (TargetWindowProtocols.HasCardWindow(ProgramTargetWindowStage.FinalizedBeforeActivity, new(kind))) return true;
        if (IsGuanduActivity) return true;
        if (HasActivityOrdinaryTrickProjection && (kind is CardKind.AllianceFeast or CardKind.SowBeans or CardKind.TransplantWood or CardKind.ReturnSoul || IsWenheCard(kind) || IsDragonBoatTrick(kind) || IsGuanduTrick(kind)) &&
            _players.Where(player => player.IsAlive).Any(player => GetSkillBindingShard(player)
                .GetInstanceTriggers(SkillProgramTriggerWindow.CardUseTargetsFinalized)
                .Any(binding => binding.Trigger.CardKinds.Contains(kind)))) return true;
        var category = CardCatalog.Get(kind).CategoryName switch
        {
            "基本牌" => SkillProgramCardCategory.Basic,
            "装备牌" => SkillProgramCardCategory.Equipment,
            _ => SkillProgramCardCategory.Trick
        };
        // Response exchanges and paired responses need the original trick's
        // accepted Use identity even without a target-changing subscriber.
        return TargetWindowProtocols.HasCardWindow(ProgramTargetWindowStage.FinalizedAfterCategory, new(kind, category)) ||
            _contentRegistry.ProgramDependencies.HasFinalizedCardTrigger(kind, category) ||
            (TargetWindowProtocols.HasCardWindow(ProgramTargetWindowStage.FinalizedInstantTrickRequirement, new(kind, category)) &&
                IsOrdinaryTrick(kind) && _contentRegistry.ProgramDependencies.HasFinalizedCardTrigger(kind, SkillProgramCardCategory.InstantTrick));
    }

    private IReadOnlyList<int> GetImplicitSelfCardUseTargets(CardKind kind, int sourceSeat,
        IReadOnlyList<int> targets) => TargetWindowProtocols.HasCardWindow(ProgramTargetWindowStage.ImplicitSelfDrawTwo, new(kind)) && kind == CardKind.DrawTwo && targets.Count == 0
            ? [sourceSeat] : targets.Count == 0 &&
        (HasFinalizedTargetProgram(kind) && (kind == CardKind.Alcohol || CardCatalog.Get(kind).CategoryName == "装备牌") ||
         HasBeforeTargetCardProgram(kind) && kind is CardKind.Alcohol or CardKind.DrawTwo)
            ? [sourceSeat] : targets;

    private bool TryBeginEquipmentTargetPrograms(CharacterState source, Card equipment, long frameId)
    {
        if (TryHappyWeiYanEquipmentCommitted(frameId, equipment) || TryMouCyEquipmentCommitted(frameId, equipment) ||
            TryAnniversaryHzEquipmentCommitted(frameId, equipment) || TryBoundarySidiEquipmentCommitted(frameId, equipment)) return true;
        if (!HasFinalizedTargetProgram(equipment.Kind)) return false;
        // Equipment commitment observes its installed slot, after target hooks finish.
        if (!TryBeginFinalizedSimpleCardPrograms(frameId,
                new(equipment.Id, SimpleCardUseEffect.EquipmentPlacement)))
            CompleteEquipmentUse(source, equipment, frameId);
        return true;
    }

    private bool TryBeginFinalizedSimpleCardPrograms(long frameId, ProgramSimpleCardContinuation continuation)
    {
        var use = _resolutionStack.OfType<CardUseFrame>().Single(frame => frame.Id == frameId);
        if (continuation.Effect == SimpleCardUseEffect.Equipment || !HasFinalizedTargetProgram(use.CardKind) ||
            !TryMarkFinalizedSimpleProgramsStarted(frameId)) return false;
        var action = use.Action ?? throw new InvalidOperationException("A finalized card use lost its action.");
        AdvanceEventRulesAndQueueFact(new CardActionAcceptedEvent(action));
        return TryBeginProgramCardWindow(null, action, SkillProgramTriggerWindow.CardUseTargetsFinalized,
            action.TargetSeats, ProgramCardContinuation.FinalizedSimpleCard, simpleContinuation: continuation);
    }
}
