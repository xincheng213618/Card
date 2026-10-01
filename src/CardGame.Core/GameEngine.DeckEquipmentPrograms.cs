namespace CardGame.Core;

public sealed record BeneficiarySuitShield(long Id, int BeneficiarySeat, Suit Suit, CardConversionSource Source);
public sealed record BeneficiarySuitShieldGrantedEvent(BeneficiarySuitShield Shield) : IGameEvent;
public sealed record RandomSkillGrantedEvent(int BeneficiarySeat, string SkillId, CardConversionSource Source) : IGameEvent;

public sealed partial class GameEngine
{
    private readonly List<BeneficiarySuitShield> _beneficiarySuitShields = [];
    private long _beneficiarySuitShieldSequence;

    private sealed partial class ProgramSkillHost : IDeckEquipmentProgramHost
    {
        public SkillProgramStepOutcome UseRandomDeckEquipment(ProgramSkillFrame f, int seat, string bind) => engine.UseProgramRandomDeckEquipment(f, seat, bind);
        public void GrantRandomSkillAndSuitShield(ProgramSkillFrame f, int seat, IReadOnlyList<string> skills, Suit suit) => engine.GrantProgramRandomSkillAndSuitShield(f, seat, skills, suit);
    }

    private bool IsShieldedRescueSelection(CharacterState owner, ProgramMultiCardViewAsSelection selection) =>
        ActiveDying is { } dying && HasBeneficiarySuitShield(owner.Seat, dying.VictimSeat,
            selection.Cards.Select(card => EffectiveSuit(owner, card)).Distinct().ToArray() is [var suit] ? suit : null);

    private bool HasBeneficiarySuitShield(int actor, int target, Suit? suit) => actor != target && suit is { } actual &&
        _beneficiarySuitShields.Any(s => s.BeneficiarySeat == target && s.Suit == actual);

    private IReadOnlyList<LegalAction> FilterBeneficiarySuitShieldActions(CharacterState actor, IReadOnlyList<LegalAction> actions)
    {
        if (_beneficiarySuitShields.Count == 0) return actions;
        return actions.Where(action =>
        {
            if (action.Kind is LegalActionKind.Recast || action.CardId is not { } id) return true;
            var card = _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(c => c.Id == id);
            var suit = EffectiveSuit(actor, ApplyProgramUseAppearance(actor, card, action.ConversionSource));
            var targets = action.Kind == LegalActionKind.BorrowedSword ? action.TargetSeats.Where((_, index) => index % 2 == 0) : GetDeclaredCardTargets(actor, action.Kind, action.TargetSeats);
            return action.Kind is LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage or LegalActionKind.PeachGarden or LegalActionKind.FiveGrains
                ? targets.Any(t => !HasBeneficiarySuitShield(actor.Seat, t, suit))
                : targets.All(t => !HasBeneficiarySuitShield(actor.Seat, t, suit));
        }).ToArray();
    }

    private void ObserveBeneficiarySuitShield(IGameEvent payload)
    {
        if (payload is TurnStartedEvent turn) _beneficiarySuitShields.RemoveAll(s => s.BeneficiarySeat == turn.ActorSeat);
    }

    private SkillProgramStepOutcome UseProgramRandomDeckEquipment(ProgramSkillFrame frame, int targetSeat, string bind)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var target = _players[targetSeat];
        if (active.SelectedTargetSeats is not [var selected] || selected != targetSeat)
            throw new InvalidOperationException("Random equipment lost its exact selected participant.");
        var candidates = target.IsAlive && target.Hp > 0
            ? _cardZones.CardsAt(CardLocation.DrawPile).Where(c => EquipmentCatalog.IsEquipment(c.Kind) &&
                target.EquipmentSlotCapacity(EquipmentCatalog.Get(c.Kind).Slot) > 0 &&
                !IsCardUseForbidden(targetSeat, c.Kind, CardActionType.Use) &&
                !IsSelfTargetForbiddenAction(target, c.Kind, [targetSeat]) &&
                !IsResponseEntityRestricted(targetSeat, c.Id) &&
                !IsPlayPhasePhysicalCardRestricted(target, c))
                .OrderBy(c => c.Id).ToArray() : [];
        if (candidates.Length == 0)
        {
            SetProgramCardSet(frame.Id, bind, [], SkillProgramCardSetVisibility.Public, []);
            return SkillProgramStepOutcome.Continue;
        }
        var card = candidates[_random.Next(candidates.Length)];
        SetProgramCardSet(frame.Id, bind, [card.Id], SkillProgramCardSetVisibility.Public, [CardLocation.DrawPile], EffectiveSuit(target, card));
        active = GetActiveProgramFrame(frame.Id);
        ReplaceRuntimeTop(active with { PendingMovementContinuation = new ProgramMovementContinuation(targetSeat, 0, null) });
        var source = new CardConversionSource(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
        var id = BeginCardUse(card, targetSeat, [], conversionSource: source);
        MoveCard(card, CardLocation.DrawPile, CardLocation.Processing, CardMoveReasons.EquipmentUse);
        if (!TryBeginEquipmentTargetPrograms(target, card, id)) CompleteEquipmentUse(target, card, id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private void ContinueProgramAfterRandomEquipmentUse()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.PendingMovementContinuation is null) return;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        if (frame.InstructionIndex == 0 || plan.Instructions[frame.InstructionIndex - 1].Op != SkillProgramEffectOp.UseRandomDeckEquipment) return;
        if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
    }

    private bool RandomEquipmentFrameRidesOn(ResolutionFrame ride, ResolutionFrame beneath)
    {
        if (ride is ProgramSkillFrame { WindowContext: { } observer } && observer.ParentFrameId == beneath.Id &&
            beneath is HpChangedTriggerWindowFrame or CardsMovedTriggerWindowFrame)
        {
            var resume = beneath switch
            {
                HpChangedTriggerWindowFrame hp => hp.ResumeFrameId,
                CardsMovedTriggerWindowFrame movement => movement.Batch.AwaitingProgramFrameId ?? movement.Batch.ParentFrameId,
                _ => null
            };
            var producer = _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == resume);
            if (producer?.TriggerId is { } binding && ProgramInstructionResolver.Default.Resolve(_contentRegistry.GetSkill(producer.SkillId).Program!,
                ProgramInstructionSourceKind.Trigger, binding).Trigger is { NoDyingAtActivation: true } trigger &&
                ProgramInstructionResolver.Default.Features(trigger).HasEquipmentObserverInterruptOptIn)
                return observer.Window == (beneath is HpChangedTriggerWindowFrame ? SkillProgramTriggerWindow.AfterHpRecovered : SkillProgramTriggerWindow.CardsMoved);
        }
        if (ride is ProgramSkillFrame { WindowContext: { } context } && beneath is ProgramCardTriggerWindowFrame window)
            return context.ParentFrameId == window.Id && context.CardUse?.ParentCardUseFrameId == window.ParentFrameId && EquipmentCatalog.IsEquipment(window.Action.EffectiveKind);
        if (ride is ProgramCardTriggerWindowFrame cardWindow && beneath is CardUseFrame use)
            return cardWindow.ParentFrameId == use.Id && cardWindow.Action.ActionId == use.Action?.ActionId && EquipmentCatalog.IsEquipment(use.CardKind);
        if (ride is not CardUseFrame equipment || beneath is not ProgramSkillFrame parent || parent.PendingMovementContinuation is not { } pending || equipment.Action is not { } action)
            return false;
        var plan = ProgramInstructionResolver.Default.Resolve(parent, _contentRegistry.GetSkill(parent.SkillId).Program!);
        if (parent.InstructionIndex == 0 || plan.Instructions[parent.InstructionIndex - 1] is not { Op: SkillProgramEffectOp.UseRandomDeckEquipment, ResultBind: { } bind } ||
            parent.SelectedTargetSeats is not [var target] || pending.SubjectSeat != target || action.ActorSeat != target || !EquipmentCatalog.IsEquipment(action.EffectiveKind) ||
            parent.CardSetBindings.SingleOrDefault(b => b.Name == bind) is not { CardIds: [var cardId] } || cardId != equipment.CardId)
            return false;
        return action.ConversionChain.Any(source => source.OwnerSeat == parent.OwnerSeat && source.SkillId == parent.SkillId && source.BindingId == GetProgramBindingId(parent) && source.SkillInstanceId == parent.SkillInstanceId);
    }

    private void GrantProgramRandomSkillAndSuitShield(ProgramSkillFrame frame, int targetSeat, IReadOnlyList<string> skills, Suit suit)
    {
        if (frame.WindowContext?.Window != SkillProgramTriggerWindow.OwnerDied || frame.SelectedTargetSeats is not [var selected] || selected != targetSeat || !_players[targetSeat].IsAlive)
            throw new InvalidOperationException("Random death bequest lost its living beneficiary.");
        var source = new CardConversionSource(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
        var chosen = skills[_random.Next(skills.Count)];
        AcquireRuntimeSkills(_players[targetSeat], $"bequest:{frame.OwnerSeat}:{frame.SkillInstanceId}:{frame.Id}", [chosen]);
        AdvanceEventRulesAndQueueFact(new RandomSkillGrantedEvent(targetSeat, chosen, source));
        var shield = new BeneficiarySuitShield(++_beneficiarySuitShieldSequence, targetSeat, suit, source);
        _beneficiarySuitShields.Add(shield);
        AdvanceEventRulesAndQueueFact(new BeneficiarySuitShieldGrantedEvent(shield));
    }
}
