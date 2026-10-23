namespace CardGame.Core;

public sealed record BeneficiarySuitShield(long Id, int BeneficiarySeat, Suit Suit, CardConversionSource Source);
public sealed record BeneficiarySuitShieldGrantedEvent(BeneficiarySuitShield Shield) : IGameEvent;
public sealed record RandomSkillGrantedEvent(int BeneficiarySeat, string SkillId, CardConversionSource Source) : IGameEvent;

public sealed partial class GameEngine
{
    private readonly List<BeneficiarySuitShield> _beneficiarySuitShields = [];
    private long _beneficiarySuitShieldSequence;

    [ProgramCapability]
    private sealed class DeckEquipmentProgramCapability(GameEngine engine) : IDeckEquipmentProgramHost
    {
        public SkillProgramStepOutcome UseRandomDeckEquipment(ProgramSkillFrame f, int seat, string bind) => engine.UseProgramRandomDeckEquipment(f, seat, bind);
        public void GrantRandomSkillAndSuitShield(ProgramSkillFrame f, int seat, IReadOnlyList<string> skills, Suit suit) => engine.GrantProgramRandomSkillAndSuitShield(f, seat, skills, suit);
    }

    private bool IsShieldedRescueSelection(CharacterState owner, ProgramMultiCardViewAsSelection selection) =>
        ActiveDying is { } dying && HasBeneficiarySuitShield(owner.Seat, dying.VictimSeat,
            IsWushuangJinzhiSelection(selection) ? Suit.None :
                selection.Cards.Select(card => EffectiveSuit(owner, card)).Distinct().ToArray() is [var suit] ? suit : null);

    private bool HasBeneficiarySuitShield(int actor, int target, Suit? suit) => actor != target && suit is { } actual &&
        (_beneficiarySuitShields.Any(s => s.BeneficiarySeat == target && s.Suit == actual) ||
         IsDuXiSuitTargetProhibited(actor, target, actual));

    private IReadOnlyList<LegalAction> FilterBeneficiarySuitShieldActions(CharacterState actor, IReadOnlyList<LegalAction> actions)
    {
        if (_beneficiarySuitShields.Count == 0 && !HasAnyDuXiSuitProhibition()) return actions;
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

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 200)]
    private void ObserveBeneficiarySuitShield(IGameEvent payload)
    {
        if (payload is TurnStartedEvent turn) _beneficiarySuitShields.RemoveAll(s => s.BeneficiarySeat == turn.ActorSeat);
    }

    private SkillProgramStepOutcome UseProgramRandomDeckEquipment(ProgramSkillFrame frame, int targetSeat, string bind)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var target = _players[targetSeat];
        var instruction = ProgramInstructionResolver.Default.Resolve(active, _contentRegistry.GetSkill(active.SkillId).Program!)
            .GetPausedInstruction(active.InstructionIndex).Effect;
        var ownRecipient = instruction.TargetReference?.Kind == ProgramParticipantRef.Owner && targetSeat == active.OwnerSeat;
        if (!ownRecipient && (active.SelectedTargetSeats is not [var selected] || selected != targetSeat))
            throw new InvalidOperationException("Random equipment lost its exact selected participant.");
        var candidates = target.IsAlive
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

    [NativeUseReturn(130)]
    private void ContinueProgramAfterRandomEquipmentUse()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.PendingMovementContinuation is null) return;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        if (frame.InstructionIndex == 0 || plan.Instructions[frame.InstructionIndex - 1].Op != SkillProgramEffectOp.UseRandomDeckEquipment) return;
        if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
    }

    private bool RandomEquipmentFrameRidesOn(ResolutionFrame ride, ResolutionFrame beneath)
    {
        if (ride is CardsMovedTriggerWindowFrame returned && beneath is ProgramSkillFrame returnedProducer &&
            ReturnedRandomEquipmentMovementMatches(returnedProducer, returned)) return true;
        if (IsShouliStartingEquipmentRide(ride, beneath)) return true;
        if (IsXiangChongGuyingEquipmentRide(ride, beneath) || IsSuyiEquipmentRide(ride, beneath)) return true;
        if (IsHuoJunFenruiNativeEquipment(ride, beneath)) return true;
        if (IsAnniversaryGuoZhaoNativeEquipment(ride, beneath)) return true;
        if (FriendXuanjianFrameRidesOn(ride, beneath)) return true;
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
        if (parent.InstructionIndex == 0 || plan.Instructions[parent.InstructionIndex - 1] is not { Op: SkillProgramEffectOp.UseRandomDeckEquipment, ResultBind: { } bind } effect)
            return false;
        var target = effect.TargetReference?.Kind == ProgramParticipantRef.Owner
            ? parent.OwnerSeat : parent.SelectedTargetSeats.Count == 1 ? parent.SelectedTargetSeats[0] : -1;
        if (target < 0 || pending.SubjectSeat != target || action.ActorSeat != target || !EquipmentCatalog.IsEquipment(action.EffectiveKind) ||
            parent.CardSetBindings.SingleOrDefault(b => b.Name == bind) is not { CardIds: [var cardId] } || cardId != equipment.CardId)
            return false;
        return action.ConversionChain.Any(source => source.OwnerSeat == parent.OwnerSeat && source.SkillId == parent.SkillId && source.BindingId == GetProgramBindingId(parent) && source.SkillInstanceId == parent.SkillInstanceId);
    }

    private bool ReturnedRandomEquipmentMovementMatches(ProgramSkillFrame parent, CardsMovedTriggerWindowFrame moved)
    {
        var batch = moved.Batch;
        if (parent.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending ||
            parent.InstructionIndex < 1 || moved.Id != batch.Id || batch.ParentFrameId is not { } useId || useId <= parent.Id ||
            LifecycleCardUse(useId) is not null || batch.AwaitingProgramFrameId != parent.Id ||
            moved.ResumeProgramFrameId is { } resume && resume != parent.Id ||
            RuntimeSkills.RuntimeRules_Core_CommonRules.HasAlternateProgramMovementReturn(moved) ||
            batch.OriginOwnerSeat != parent.OwnerSeat || batch.OriginSkillId != parent.SkillId ||
            batch.OriginSkillInstanceId != parent.SkillInstanceId || batch.TurnNumber != _turnNumber ||
            batch.Movements.Count == 0 || batch.Movements.Any(m => !_cardMovements.Contains(m))) return false;
        var definition = _contentRegistry.GetSkill(parent.SkillId).Program!;
        if (definition.GameplayHash != parent.GameplayHash) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(parent, definition);
        if (plan.Instructions[parent.InstructionIndex - 1] is not
            { Op: SkillProgramEffectOp.UseRandomDeckEquipment, ResultBind: { } bind } effect ||
            !new ProgramSkillHost(this).EvaluateCondition(parent, effect.Condition, CreateSkillContext(_players[parent.OwnerSeat]))) return false;
        var target = effect.TargetReference?.Kind == ProgramParticipantRef.Owner ? parent.OwnerSeat :
            parent.SelectedTargetSeats is [var selected] ? selected : -1;
        if (target < 0 || pending.SubjectSeat != target ||
            parent.CardSetBindings.SingleOrDefault(b => b.Name == bind) is not
                { CardIds: [var cardId], SourceLocations: [var source] } || source != CardLocation.DrawPile) return false;
        var history = CompleteProgramEventHistory().ToArray();
        var declarations = history.OfType<CardUseDeclaredEvent>().Where(e => e.ResolutionId == useId).ToArray();
        var completions = history.OfType<CardUseFinishedEvent>().Where(e => e.ResolutionId == useId).ToArray();
        var equipmentChanges = history.OfType<EquipmentChangedEvent>().Where(e => e.ResolutionId == useId).ToArray();
        if (declarations is not [var declared] || declared.CardId != cardId || declared.SourceSeat != target ||
            !EquipmentCatalog.IsEquipment(declared.CardKind) || completions is not [var completed] ||
            completed.CardId != cardId || completed.CardKind != declared.CardKind || equipmentChanges is not [var changed] ||
            changed.PlayerSeat != target || changed.CardId != cardId || changed.CardKind != declared.CardKind) return false;
        var declarationIndex = Array.IndexOf(history, declared); var completionIndex = Array.IndexOf(history, completed);
        if (completionIndex <= declarationIndex || history.Skip(declarationIndex + 1).Take(completionIndex - declarationIndex - 1)
            .OfType<CardMovedEvent>().Count(e => e.CardId == cardId && e.From == CardLocation.DrawPile &&
                e.To == CardLocation.Processing && e.Reason == CardMoveReasons.EquipmentUse) != 1) return false;
        // The native use has left the stack; only its exact paid entry/replacement batch may return to this instruction.
        return batch.Movements.All(m =>
            m.CardId == cardId && m.From == CardLocation.Processing && m.To == CardLocation.Equipment(target) && m.Reason == CardMoveReasons.EquipmentEnter ||
            m.CardId == changed.ReplacedCardId && m.From == CardLocation.Equipment(target) && m.Reason == CardMoveReasons.EquipmentReplace &&
                (m.To == NormalizeEphemeralCardDestination(GetAdvancedCard(m.CardId), m.From,
                    GetAdvancedCard(m.CardId).IsGeneralWeapon ? CardLocation.OutsideGame : CardLocation.DiscardPile) ||
                 IsRedirectedPaidDiscardMovement(m, m.From, CardMoveReasons.EquipmentReplace)) ||
            changed.ReplacedCardId is { } replaced && GetAdvancedCard(replaced).Kind == CardKind.WoodenOx &&
                m.From == CardLocation.WoodenOxGrain(target) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard);
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
