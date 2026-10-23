namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShanheS5JixianX = "shanhe-s5-jixian-x";
    private static readonly CardMoveReason ShanheS5JixianDrawReason = new("shanhe.s5.jixian.draw");

    private sealed partial class ProgramSkillHost : IShanheS5JixianProgramHost
    {
        public SkillProgramStepOutcome ResolveShanheS5Jixian(ProgramSkillFrame frame, string stage) => engine.ResolveShanheS5Jixian(frame, stage);
    }

    private int ShanheS5JixianSkillCount(CharacterState p) => OwnedRuntimeSkills(p, p.General, true)
        .Concat(p.SecondaryGeneral is { } secondary ? OwnedRuntimeSkills(p, secondary, false) : [])
        .Select(s => s.ContentId).OfType<string>().Distinct(StringComparer.Ordinal).Count();

    private int ShanheS5JixianMask(int owner, CharacterState target) =>
        (GetEquipment(target).Any(c => FieldEquipmentSlot(c) == EquipmentSlot.Armor) ? 1 : 0) |
        (ShanheS5JixianSkillCount(target) > ShanheS5JixianSkillCount(_players[owner]) ? 2 : 0) |
        (target.HasHpValue && !IsRuleWounded(target) ? 4 : 0);

    private bool IsShanheS5JixianTarget(int owner, CharacterState target) =>
        _players[owner].IsAlive && target.IsAlive && owner != target.Seat && ShanheS5JixianMask(owner, target) != 0 &&
        CanUseSlashTarget(_players[owner], target, new(0, CardKind.Slash, Suit.None, 0),
            effectiveKind: CardKind.Slash, noEffectiveRank: true, physicalCardIds: []);

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 5716, SkillProgramEffectOp.ResolveShanheS5Jixian)]
    private bool CanOfferShanheS5Jixian(ProgramTriggerLegalityContext c) =>
        c.OwnerSeat == _currentSeat && _phase == TurnPhase.Draw && c.Context.Window == SkillProgramTriggerWindow.DrawPhaseEnded &&
        _players.Any(target => IsShanheS5JixianTarget(c.OwnerSeat, target));

    private SkillProgramStepOutcome ResolveShanheS5Jixian(ProgramSkillFrame supplied, string stage)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (stage == "prepare")
        {
            if (f.InstructionIndex != 2 || f.SelectedTargetSeats is not [var target] || !IsShanheS5JixianTarget(f.OwnerSeat, _players[target]) ||
                f.OwnerSeat != _currentSeat || _phase != TurnPhase.Draw ||
                f.WindowContext is not { Window: SkillProgramTriggerWindow.DrawPhaseEnded } context ||
                !_resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(w => w.Id == context.ParentFrameId && w.Window == context.Window && w.OwnerSeat == f.OwnerSeat) ||
                f.NumberBindings.Any(n => n.Name == ShanheS5JixianX))
                throw new InvalidOperationException("Jixian requires one legal target at its real owner Draw end.");
            var mask = ShanheS5JixianMask(f.OwnerSeat, _players[target]);
            var x = System.Numerics.BitOperations.PopCount((uint)mask);
            ReplaceRuntimeTop(f = f with { NumberBindings = f.NumberBindings.Append(new ProgramSkillNumberBinding(ShanheS5JixianX, x)).ToArray() });
            AdvanceEventRulesAndQueueFact(new ShanheS5JixianFrozenEvent(f.Id, f.SkillId, f.SkillInstanceId, f.GameplayHash, context.ParentFrameId,
                f.OwnerSeat, target, _turnNumber, mask, x, ShanheS5JixianSkillCount(_players[f.OwnerSeat]), ShanheS5JixianSkillCount(_players[target])));
            return SkillProgramStepOutcome.Continue;
        }
        var frozen = RequireShanheS5JixianFrozen(f);
        var issued = ProgramEventHistory<ShanheS5JixianSlashIssuedEvent>().Single(e => e.FrameId == f.Id);
        if (issued.OwnerSeat != f.OwnerSeat || issued.OriginalTargetSeat != frozen.TargetSeat ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == issued.UseId && IsSlashCard(e.CardKind)) != 1)
            throw new InvalidOperationException("Jixian lost its exact completed native Slash.");
        if (stage == "draw")
        {
            if (f.InstructionIndex != 4 || ProgramEventHistory<ShanheS5JixianDrawPaidEvent>().Any(e => e.FrameId == f.Id))
                throw new InvalidOperationException("Jixian cannot repeat its frozen-X draw.");
            return BeginOperationProgramDraw(f, f.OwnerSeat, frozen.X, ShanheS5JixianDrawReason);
        }
        if (f.InstructionIndex != 5 || ProgramEventHistory<ShanheS5JixianDrawPaidEvent>().Count(e => e.FrameId == f.Id && e.UseId == issued.UseId && e.Requested == frozen.X) != 1 ||
            ProgramEventHistory<ShanheS5JixianResolvedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Jixian lost its once-only paid draw before original-target resolution.");
        var damaged = ProgramEventHistory<DamageAppliedEvent>().Any(e => e.SourceCardUseResolutionId == issued.UseId &&
            e.TargetSeat == frozen.TargetSeat && e.Amount > 0);
        AdvanceEventRulesAndQueueFact(new ShanheS5JixianResolvedEvent(f.Id, issued.UseId, frozen.TargetSeat, damaged, !damaged));
        return damaged ? SkillProgramStepOutcome.Continue : new ProgramSkillHost(this).LoseHp(f.Id, f.SkillId, f.OwnerSeat, 1);
    }

    private void IssueShanheS5JixianSlashUse(long useId, ProgramSkillFrame f, CardActionContext action)
    {
        if (!f.NumberBindings.Any(n => n.Name == ShanheS5JixianX)) return;
        var frozen = RequireShanheS5JixianFrozen(f);
        if (f.InstructionIndex != 3 || action.Type != CardActionType.Use || action.EffectiveKind != CardKind.Slash ||
            action.ActorSeat != f.OwnerSeat || !action.TargetSeats.SequenceEqual([frozen.TargetSeat]) || action.PhysicalCards.Count != 0 ||
            ProgramEventHistory<ShanheS5JixianSlashIssuedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Jixian's virtual Slash lost its frozen original target or unique producer.");
        AdvanceEventRulesAndQueueFact(new ShanheS5JixianSlashIssuedEvent(f.Id, useId, action.ActionId, f.OwnerSeat, frozen.TargetSeat));
    }

    private ShanheS5JixianFrozenEvent RequireShanheS5JixianFrozen(ProgramSkillFrame f)
    {
        var facts = ProgramEventHistory<ShanheS5JixianFrozenEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var values = f.NumberBindings.Where(n => n.Name == ShanheS5JixianX).ToArray();
        if (facts is not [var fact] || values is not [var value] || value.Value != fact.X || fact.X is < 1 or > 3 ||
            fact.ConditionMask is < 1 or > 7 || System.Numerics.BitOperations.PopCount((uint)fact.ConditionMask) != fact.X ||
            fact.OwnerSeat != f.OwnerSeat || fact.SkillId != f.SkillId || fact.SkillInstanceId != f.SkillInstanceId || fact.GameplayHash != f.GameplayHash ||
            f.WindowContext?.ParentFrameId != fact.DrawWindowId || f.WindowContext.Window != SkillProgramTriggerWindow.DrawPhaseEnded ||
            fact.ActualTurnNumber != _turnNumber || !IsValidPlayerSeat(fact.TargetSeat) || fact.TargetSeat == f.OwnerSeat ||
            ((fact.ConditionMask & 2) != 0) != (fact.TargetSkillCount > fact.OwnerSkillCount))
            throw new InvalidOperationException("Jixian's X lost its original Draw-end target and frozen public condition proof.");
        return fact;
    }

    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveShanheS5Jixian)]
    private void ReturnShanheS5JixianDraw(ProgramSkillFrame supplied, NativeDrawOutcome outcome)
    {
        var f = GetActiveProgramFrame(supplied.Id); var frozen = RequireShanheS5JixianFrozen(f);
        var issued = ProgramEventHistory<ShanheS5JixianSlashIssuedEvent>().Single(e => e.FrameId == f.Id);
        if (f.InstructionIndex != 4 || f.NativeDraw is not null || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != frozen.X ||
            outcome.SequenceAfter != _movementSequence || ProgramEventHistory<ShanheS5JixianDrawPaidEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Jixian's X draw lost its exact native request return.");
        AdvanceEventRulesAndQueueFact(new ShanheS5JixianDrawPaidEvent(f.Id, issued.UseId, frozen.X, outcome.ActualDrawIds.Count,
            outcome.SequenceBefore, outcome.SequenceAfter));
        AdvanceRuntimeProgram(f.Id);
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 5716)]
    private void AssertShanheS5JixianFrozen(ProgramSkillFrame f)
    {
        if (f.NumberBindings.Any(n => n.Name == ShanheS5JixianX)) _ = RequireShanheS5JixianFrozen(f);
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 5716)]
    private bool CanContinueIssuedShanheS5Jixian(ProgramSkillFrame f)
    {
        if (!f.NumberBindings.Any(n => n.Name == ShanheS5JixianX) || f.InstructionIndex < 3) return false;
        var frozen = RequireShanheS5JixianFrozen(f);
        return ProgramEventHistory<ShanheS5JixianSlashIssuedEvent>().Count(e => e.FrameId == f.Id &&
            e.OwnerSeat == f.OwnerSeat && e.OriginalTargetSeat == frozen.TargetSeat) == 1;
    }
}
