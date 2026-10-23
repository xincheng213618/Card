namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string Ol2016SimaRenjie = "ol:renjie-2016";
    private bool HasOl2016SimaRenjie => _contentRegistry.Skills.ContainsKey(Ol2016SimaRenjie) ||
        _contentRegistry.Skills.ContainsKey("happy:renjie");

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 40725,
        SkillProgramEffectOp.ChangeAttributedMarker)]
    private bool AllowsOl2016SimaDiscardRen(ProgramTriggerLegalityContext gate)
    {
        if (gate.SkillId != Ol2016SimaRenjie || gate.Trigger.Id != "discard-ren") return true;
        return gate.WindowContext is
            {
                Window: SkillProgramTriggerWindow.CardsMoved,
                MovementBatch: { MovementTiming: { Phase: TurnPhase.Discard } timing } batch,
                MovementIndex: { } index
            } && timing.PhaseActorSeat == gate.OwnerSeat && batch.TurnNumber == _turnNumber &&
            index >= 0 && index < batch.Movements.Count &&
            _cardMovements.Contains(batch.Movements[index]) &&
            GetProgramDiscardSource(batch.Movements[index]) == CardLocation.Hand(gate.OwnerSeat);
    }
}

internal static class Ol2016SimaYiRenjieComposition
{
    [ProgramCatalogValidation(40725)]
    internal static void Validate(string path, SkillProgram program)
    {
        if (program.Id != "ol:renjie-2016") return;
        var discard = program.Triggers.SingleOrDefault(t => t.Id == "discard-ren");
        if (discard is not
            {
                Window: SkillProgramTriggerWindow.CardsMoved,
                Subject: SkillProgramTriggerSubject.Owner,
                MovementOccurrence: SkillProgramMovementOccurrence.PerCard,
                MovementDiscardOnly: true,
                Optional: false,
                MarkerCost: null
            } || !discard.SourceZones.SequenceEqual([CardZoneKind.Hand]) ||
            discard.MovementReasons.Count != 0 || discard.ExcludedMovementReasons.Count != 0 ||
            discard.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            discard.Effects is not
            [{ Op: SkillProgramEffectOp.ChangeAttributedMarker, Target: SkillProgramEffectTarget.Owner,
                Amount: 1, Marker: PlayerMarkerKind.Ren, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": original Renjie requires each actual own hand discard in its frozen Discard phase.");
    }
}
