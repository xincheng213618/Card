namespace CardGame.Core;

public interface IAvailableBoundSubsetProgramEffectHost
{
    SkillProgramStepOutcome SelectAvailableBoundSubset(ProgramSkillFrame frame, SkillProgramEffect effect);
}

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IAvailableBoundSubsetProgramEffectHost
    {
        public SkillProgramStepOutcome SelectAvailableBoundSubset(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.SelectProgramCardSubset(frame.Id, frame.OwnerSeat, effect.SourceBind!, effect.ResultBind!,
                effect.MinimumCards, effect.MaximumCards, effect.MaximumRankSum, effect.AiOrder!.Value,
                effect.AllowFewerWhenInsufficient, effect.OnePerSuit, availableAtSourceOnly: true);
    }

    private int CountAvailableBoundCards(ProgramSkillCardSetBinding source) =>
        source.CardIds.Select((id, index) => (Id: id, Index: index))
            .Count(item => _cardZones.GetLocation(item.Id) == source.SourceLocations[item.Index]);

    private int CountAvailableBoundSuits(ProgramSkillCardSetBinding source) =>
        source.CardIds.Select((id, index) => (Id: id, Index: index))
            .Where(item => _cardZones.GetLocation(item.Id) == source.SourceLocations[item.Index])
            .Select(item => _cardZones.CardsAt(source.SourceLocations[item.Index]).Single(card => card.Id == item.Id).Suit)
            .Distinct().Count();

    // The executor commits its cursor before Draw suspends. Only that actual
    // paused producer, its frozen own-hand receipt, the immediate opt-in consumer
    // and its exact movement continuation enable existing typed observer rides.
    private bool HasAvailableBoundDamageObserver(long damageWindowId) =>
        GetAvailableBoundDamageObserverRoot(damageWindowId) is not null;

    private ProgramSkillFrame? GetAvailableBoundDamageObserverRoot(long damageWindowId)
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame frame ||
                frame.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied } context ||
                context.ParentFrameId != damageWindowId || frame.InstructionIndex < 1 ||
                _resolutionStack[index + 1] is not CardsMovedTriggerWindowFrame movement ||
                movement.Batch.ParentFrameId != frame.Id ||
                movement.Batch.AwaitingProgramFrameId is { } awaiting && awaiting != frame.Id ||
                movement.Batch.OriginOwnerSeat != frame.OwnerSeat ||
                movement.Batch.OriginSkillId != frame.SkillId ||
                movement.Batch.OriginSkillInstanceId != frame.SkillInstanceId)
                continue;
            var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
            if (frame.InstructionIndex >= plan.Instructions.Count) continue;
            var producer = plan.GetPausedInstruction(frame.InstructionIndex).Effect;
            var consumer = plan.GetInstruction(frame.InstructionIndex).Effect;
            if (producer.Op != SkillProgramEffectOp.Draw || producer.Target != SkillProgramEffectTarget.Owner ||
                producer.ResultBind is null || consumer.Op != SkillProgramEffectOp.SelectCardSubset ||
                consumer.AvailableAtSourceOnly != true || consumer.SourceBind != producer.ResultBind)
                continue;
            var receipt = frame.CardSetBindings.SingleOrDefault(binding => binding.Name == producer.ResultBind);
            if (receipt is null || receipt.CardIds.Count == 0 || receipt.CardIds.Count != receipt.SourceLocations.Count ||
                receipt.CardIds.Distinct().Count() != receipt.CardIds.Count ||
                receipt.SourceLocations.Any(location => location != CardLocation.Hand(frame.OwnerSeat)))
                continue;
            // Draw returns the complete immutable receipt once, while each real
            // entity may have its own atomic movement batch. Validate every
            // intersecting movement in this exact producer batch, not current
            // locations (which a lawful child or death cleanup may have changed).
            var receiptMovements = movement.Batch.Movements.Where(move => receipt.CardIds.Contains(move.CardId)).ToArray();
            if (receiptMovements.Length == 0 ||
                receiptMovements.Select(move => move.CardId).Distinct().Count() != receiptMovements.Length ||
                receiptMovements.Any(move => move.From != CardLocation.DrawPile ||
                    move.To != CardLocation.Hand(frame.OwnerSeat) ||
                    move.Reason.Value != $"skill-program.{frame.SkillId}.{SkillProgramEffectOp.Draw}"))
                continue;
            return frame;
        }
        return null;
    }

    private bool IsAvailableBoundDamageProgramDying()
    {
        if (ActiveDamageTrigger is not { } trigger ||
            ActiveDying is not { ResumesProgramSkill: true } dying ||
            GetAvailableBoundDamageObserverRoot(trigger.Id) is not { } root ||
            _resolutionStack.OfType<DyingFrame>().SingleOrDefault(frame => frame.Id == dying.FrameId) is not { } dyingFrame ||
            dyingFrame.ParentFrameId != dying.ParentFrameId)
            return false;
        var parentIndex = -1;
        for (var index = 0; index < _resolutionStack.Count; index++)
            if (_resolutionStack[index] is ProgramSkillFrame parent && parent.Id == dying.ParentFrameId)
                parentIndex = index;
        if (parentIndex < 0 || parentIndex + 1 >= _resolutionStack.Count ||
            _resolutionStack[parentIndex + 1].Id != dyingFrame.Id)
            return false;
        var top = _resolutionStack.LastOrDefault();
        if (!(top is DyingFrame topDying && topDying.Id == dyingFrame.Id ||
              top is ProgramSkillFrame { WindowContext: { } response } && response.ParentFrameId == dyingFrame.Id &&
              response.Window is SkillProgramTriggerWindow.DyingResponse or SkillProgramTriggerWindow.SelfDyingResponse ||
              IsAvailableBoundPeachRescueRide(parentIndex + 1, dyingFrame)))
            return false;
        while (parentIndex >= 1 &&
               (DamageFrameRidesOn(_resolutionStack[parentIndex], _resolutionStack[parentIndex - 1]) ||
                DamageObserverRidesOn(_resolutionStack[parentIndex], _resolutionStack[parentIndex - 1]) ||
                PileEquipmentFrameRidesOn(_resolutionStack[parentIndex], _resolutionStack[parentIndex - 1]) ||
                RandomEquipmentFrameRidesOn(_resolutionStack[parentIndex], _resolutionStack[parentIndex - 1])))
            parentIndex--;
        return _resolutionStack[parentIndex].Id == root.Id;
    }

    // This is an opt-in observer ride only. The rescue itself has
    // already paid and retains the engine's original Dying response token.
    private bool IsAvailableBoundPeachRescueRide(int dyingIndex, DyingFrame dying)
    {
        var useIndex = dyingIndex + 1;
        if (useIndex >= _resolutionStack.Count ||
            _resolutionStack[useIndex] is not CardUseFrame rescue ||
            rescue.DyingResponse is not { UsedPeach: true, UsedAlcohol: false } response ||
            response.ResolutionId != dying.Id || response.ResponderSeat != dying.ResponderSeat ||
            response.PeachCardId != rescue.CardId || response.AlcoholCardId is not null ||
            rescue.SourceSeat != response.ResponderSeat || rescue.CardKind != CardKind.Peach ||
            !(rescue.PhysicalCardIds ?? [rescue.CardId]).SequenceEqual([rescue.CardId]) ||
            !rescue.TargetSeats.SequenceEqual([dying.VictimSeat]) ||
            rescue.Action is not { Type: CardActionType.Use, EffectiveKind: CardKind.Peach } action ||
            action.ActorSeat != rescue.SourceSeat || action.ProviderSeat != rescue.SourceSeat ||
            !action.EffectiveDesignatedTargetSeats.SequenceEqual(rescue.TargetSeats) ||
            action.PhysicalCards.Count != 1 || action.PhysicalCards[0].CardId != rescue.CardId ||
            action.PhysicalCards[0].From != CardLocation.Hand(rescue.SourceSeat) ||
            !_cardZones.CardsAt(_cardZones.GetLocation(rescue.CardId))
                .Any(card => card.Id == rescue.CardId && card.Kind == action.PhysicalCards[0].CardKind))
            return false;
        var physicalKind = action.PhysicalCards[0].CardKind;
        if (physicalKind == CardKind.Peach)
        {
            if (response.UsedPeachPhysicalCardKind is not null) return false;
        }
        else if (response.UsedPeachPhysicalCardKind != physicalKind ||
                 action.ConversionChain.Count != 1 ||
                 action.ConversionChain[0].OwnerSeat != rescue.SourceSeat ||
                 string.IsNullOrWhiteSpace(action.ConversionChain[0].SkillId) ||
                 string.IsNullOrWhiteSpace(action.ConversionChain[0].BindingId) ||
                 string.IsNullOrWhiteSpace(action.ConversionChain[0].SkillInstanceId))
            return false;
        var index = _resolutionStack.Count - 1;
        while (index > useIndex &&
               (DamageFrameRidesOn(_resolutionStack[index], _resolutionStack[index - 1]) ||
                DamageObserverRidesOn(_resolutionStack[index], _resolutionStack[index - 1]) ||
                PileEquipmentFrameRidesOn(_resolutionStack[index], _resolutionStack[index - 1]) ||
                RandomEquipmentFrameRidesOn(_resolutionStack[index], _resolutionStack[index - 1])))
            index--;
        return index == useIndex;
    }
}
