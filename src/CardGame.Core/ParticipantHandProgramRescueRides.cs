namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Bound-pile Alcohol is produced by a DyingResponse program, rather than
    // the native responder's DyingResponse card-use token. Borrow only this
    // exact paid program/use chain; its source qualification may change later.
    private bool IsPaidHandRepaymentProgramAlcoholRide(int dyingIndex, DyingFrame dying)
    {
        var programIndex = dyingIndex + 1;
        var useIndex = programIndex + 1;
        if (dyingIndex < 0 || useIndex >= _resolutionStack.Count ||
            _resolutionStack[programIndex] is not ProgramSkillFrame program ||
            program.WindowContext is not { Window: SkillProgramTriggerWindow.DyingResponse } context ||
            context.ParentFrameId != dying.Id || context.TargetSeat != dying.VictimSeat ||
            program.OwnerSeat != dying.ResponderSeat || context.OwnerSeat != program.OwnerSeat ||
            _contentRegistry.Skills.GetValueOrDefault(program.SkillId)?.Program is not { } definition ||
            definition.GameplayHash != program.GameplayHash || program.InstructionIndex < 1 ||
            string.IsNullOrWhiteSpace(program.SkillInstanceId)) return false;

        var plan = ProgramInstructionResolver.Default.Resolve(program, definition);
        if (program.InstructionIndex > plan.Instructions.Count ||
            plan.GetPausedInstruction(program.InstructionIndex).Effect is not
                { Op: SkillProgramEffectOp.UseBoundCardAsDyingAlcohol, SourceBind: { } bind } ||
            program.CardSetBindings.SingleOrDefault(b => b.Name == bind) is not { } bound ||
            bound.CardIds is not [var cardId] || bound.SourceLocations is not [var location] ||
            location.OwnerSeat != program.OwnerSeat || location.Zone is not
                (CardZoneKind.WoodenOxGrain or CardZoneKind.BuquWound or CardZoneKind.Authority or CardZoneKind.Chunlao) ||
            _resolutionStack[useIndex] is not CardUseFrame rescue || rescue.DyingResponse is not null ||
            rescue.CardId != cardId || rescue.CardKind != CardKind.Alcohol || rescue.SourceSeat != dying.VictimSeat ||
            !(rescue.PhysicalCardIds ?? [rescue.CardId]).SequenceEqual([cardId]) ||
            !rescue.TargetSeats.SequenceEqual([dying.VictimSeat]) || rescue.Action is not { Type: CardActionType.Use } action ||
            action.ActorSeat != dying.VictimSeat || action.ProviderSeat != program.OwnerSeat || action.EffectiveKind != CardKind.Alcohol ||
            !action.EffectiveDesignatedTargetSeats.SequenceEqual(rescue.TargetSeats) || action.PhysicalCards is not [var cost] ||
            cost.CardId != cardId || cost.From != location || action.ConversionChain is not [var conversion] ||
            conversion.OwnerSeat != program.OwnerSeat || conversion.SkillId != program.SkillId ||
            conversion.BindingId != (program.TriggerId ?? program.ActivationId) || conversion.SkillInstanceId != program.SkillInstanceId ||
            !_cardZones.CardsAt(_cardZones.GetLocation(cardId)).Any(card => card.Id == cardId && card.Kind == cost.CardKind) ||
            !_cardMovements.Any(move => move.CardId == cardId && move.From == location && move.To == CardLocation.Processing &&
                move.Reason.Value == $"skill-program.{program.SkillId}.{SkillProgramEffectOp.UseBoundCardAsDyingAlcohol}"))
            return false;

        for (var index = _resolutionStack.Count - 1; index > useIndex; index--)
            if (!ParticipantHandRescueObserverRide(_resolutionStack[index], _resolutionStack[index - 1], rescue)) return false;
        return true;
    }
}
