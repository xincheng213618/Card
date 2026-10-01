namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Optional target windows are enabled by the loaded catalog, retaining old
    // card actions and event shapes for historical content fingerprints.

    private bool HasFinalizedTargetProgram(CardKind kind)
    {
        var category = CardCatalog.Get(kind).CategoryName switch
        {
            "基本牌" => SkillProgramCardCategory.Basic,
            "装备牌" => SkillProgramCardCategory.Equipment,
            _ => SkillProgramCardCategory.Trick
        };
        return _contentRegistry.Skills.Values.Any(skill => skill.Program?.Triggers.Any(trigger =>
            trigger.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized &&
            (trigger.CardKinds.Count == 0 || trigger.CardKinds.Contains(kind)) &&
            (trigger.CardCategories.Count == 0 || trigger.CardCategories.Contains(category))) == true);
    }

    private IReadOnlyList<int> GetImplicitSelfCardUseTargets(CardKind kind, int sourceSeat,
        IReadOnlyList<int> targets) => targets.Count == 0 && HasFinalizedTargetProgram(kind) &&
        (kind == CardKind.Alcohol || CardCatalog.Get(kind).CategoryName == "装备牌")
            ? [sourceSeat] : targets;

    private bool TryBeginEquipmentTargetPrograms(CharacterState source, Card equipment, long frameId)
    {
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
