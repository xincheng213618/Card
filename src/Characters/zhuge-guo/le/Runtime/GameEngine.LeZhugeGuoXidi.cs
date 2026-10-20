namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string LeXidi = "le:xidi", LeChengyan = "le:chengyan", LeChengyanDrawReason = "skill-program.le:chengyan.draw";
    private bool HasLeZhugeGuoCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveLeZhugeGuoChengyan) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.InitializeLeZhugeGuoXidi);
    private static CardConversionSource LeZhugeGuoSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool HasLeXidiEntityMark(int cardId) => HasLeZhugeGuoCapability && cardId > 0 && ProgramEventHistory<LeXidiEntityMarkedEvent>().Any(e => e.CardId == cardId);
    private bool IsLeXidiHandLimitExempt(CharacterState owner, int cardId) => HasRuntimeSkill(owner, LeXidi) &&
        _cardZones.GetLocation(cardId) == CardLocation.Hand(owner.Seat) && HasLeXidiEntityMark(cardId);
    private ProgramLifecycleTriggerWindowFrame CaptureLeXidiOpening(ProgramLifecycleTriggerWindowFrame w)
    {
        if (!HasLeZhugeGuoCapability || w.Window != SkillProgramTriggerWindow.GameStarting) return w;
        return w with { LeXidiOpening = w.Candidates.Where(c => c.SkillId == LeXidi && c.BindingId == "mark-initial-flutes")
            .Select(c => c.OwnerSeat).Distinct().SelectMany(s => GetHand(_players[s]).Select(card => new LeXidiOpeningEntity(s, card.Id,
                _cardMovements.Last(m => m.CardId == card.Id && m.To == CardLocation.Hand(s)).Sequence))).ToArray() };
    }
    private SkillProgramStepOutcome InitializeLeXidi(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != LeXidi || f.TriggerId != "mark-initial-flutes" || f.ActivationId != f.TriggerId ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.GameStarting } c ||
            _resolutionStack.SingleOrDefault(x => x.Id == c.ParentFrameId) is not ProgramLifecycleTriggerWindowFrame { LeXidiOpening: not null } w ||
            ProgramEventHistory<LeXidiInitializedEvent>().Any(e => e.Source.OwnerSeat == f.OwnerSeat))
            throw new InvalidOperationException("Xidi requires its original frozen opening-Hand window.");
        var cards = w.LeXidiOpening.Where(e => e.OwnerSeat == f.OwnerSeat).ToArray();
        foreach (var e in cards)
        {
            if (!_cardMovements.Any(m => m.Sequence == e.EntrySequence && m.CardId == e.CardId && m.To == CardLocation.Hand(e.OwnerSeat)))
                throw new InvalidOperationException("Xidi opening entity lost its actual Hand entry.");
            AdvanceEventRulesAndQueueFact(new LeXidiEntityMarkedEvent(f.Id, LeZhugeGuoSource(f), e.CardId, e.EntrySequence, true));
        }
        AdvanceEventRulesAndQueueFact(new LeXidiInitializedEvent(f.Id, LeZhugeGuoSource(f), w.Id, cards.Length));
        return SkillProgramStepOutcome.Continue;
    }
    private SkillProgramStepOutcome ReorderLeXidi(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != LeXidi || f.ActivationId != f.TriggerId || f.WindowContext?.Window is not
            (SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or SkillProgramTriggerWindow.TurnEnding))
            throw new InvalidOperationException("Xidi reorder requires its real preparation or ending window.");
        var count = GetHand(_players[f.OwnerSeat]).Count(c => HasLeXidiEntityMark(c.Id));
        AdvanceEventRulesAndQueueFact(new LeXidiReorderStartedEvent(f.Id, f.OwnerSeat, count, Math.Max(1, count)));
        return BeginProgramTopReorder(f, Math.Max(1, count), null);
    }
    [ProgramCapability] private sealed class LeZhugeGuoCapability(GameEngine g) : ILeZhugeGuoHost
    {
        public SkillProgramStepOutcome Initialize(ProgramSkillFrame f) => g.InitializeLeXidi(f);
        public SkillProgramStepOutcome Reorder(ProgramSkillFrame f) => g.ReorderLeXidi(f);
        public SkillProgramStepOutcome Chengyan(ProgramSkillFrame f) => g.StartLeChengyan(f);
    }
}
