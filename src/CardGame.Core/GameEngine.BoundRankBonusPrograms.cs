namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void RevealWithNextBooleanBonus(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        frame = GetActiveProgramFrame(frame.Id);
        var bonus = GetProgramBooleanState(frame, effect.StateId!) ? effect.MaximumCards : 0;
        // The accepted reveal consumes the exact source-instance state once.
        // A declined trigger never reaches this instruction.
        SetProgramBooleanState(frame, effect.StateId!, false);
        RevealProgramTopCards(frame.Id, frame.OwnerSeat, checked(effect.Amount + bonus), null,
            effect.ResultBind!, effect.Visibility);
    }

    private SkillProgramStepOutcome ObtainAndArmNextRevealBonus(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        frame = GetActiveProgramFrame(frame.Id);
        var source = GetProgramCardSet(frame, effect.SourceBind!);
        if (source.CardIds.Count != source.SourceLocations.Count || source.SourceLocations.Any(location => location != CardLocation.Processing))
            throw new InvalidOperationException("A rank-bonus gain requires exact revealed Processing entities.");
        var cards = source.CardIds.Select(id => _cardZones.CardsAt(CardLocation.Processing).Single(card => card.Id == id)).ToArray();
        var sum = cards.Sum(card => card.Rank);
        if (sum > effect.MaximumRankSum) throw new InvalidOperationException("The selected revealed subset exceeds the issuing rank threshold.");
        // MoveProgramBoundCards performs the atomic gain and queues its facts;
        // it does not begin own-hand gain observers. Arm before those children.
        _ = MoveProgramBoundCards(frame.Id, frame.OwnerSeat, effect.SourceBind!, null,
            SkillProgramCardDestination.OwnerHand, null, new($"skill-program.{frame.SkillId}.rank-bonus-obtain"));
        SetProgramBooleanState(frame, effect.StateId!, cards.Length > 0 && sum == effect.MaximumRankSum);
        return AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat);
    }

    private bool TryOtherDyingVictim(ProgramSkillWindowContext context, int ownerSeat, out DyingFrame dying)
    {
        dying = null!;
        if (context.Window != SkillProgramTriggerWindow.DyingEntering || context.TargetSeat is not { } victim ||
            victim == ownerSeat || !IsValidPlayerSeat(victim) || !_players[victim].IsAlive || _players[victim].Hp > 0 ||
            ActiveDying is not { } active || active.VictimSeat != victim ||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().LastOrDefault() is not { } entry ||
            entry.Id != context.ParentFrameId || entry.OwnerSeat != victim || entry.Window != SkillProgramTriggerWindow.DyingEntering ||
            entry.Continuation != ProgramLifecycleContinuation.ResumeDyingEntry || entry.ResumeDyingFrameId != active.FrameId ||
            _resolutionStack.OfType<DyingFrame>().SingleOrDefault(f => f.Id == active.FrameId) is not { } current)
            return false;
        dying = current; return true;
    }

    // Hook only when the trigger contains the new op, before any equipment cost.
    private bool CanRunOtherDyingVictimRecovery(SkillProgramTrigger trigger, ProgramSkillWindowContext context, int ownerSeat) =>
        !trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.RecoverOtherDyingVictimTo) || TryOtherDyingVictim(context, ownerSeat, out _);

    private void RecoverOtherDyingVictim(ProgramSkillFrame frame, int hp)
    {
        frame = GetActiveProgramFrame(frame.Id);
        if (frame.WindowContext is not { } context || !TryOtherDyingVictim(context, frame.OwnerSeat, out var dying))
        { CancelProgramBindingAndCleanup(frame, "原濒死进入窗口或他人目标已失效，已付装备不重复支付。"); return; }
        if (_resolutionStack.Count < 2 || _resolutionStack[^2] is not ProgramLifecycleTriggerWindowFrame entry ||
            entry.Id != context.ParentFrameId || entry.CandidateIndex < 0 || entry.CandidateIndex >= entry.Candidates.Count ||
            entry.Candidates[entry.CandidateIndex] is not { } candidate || candidate.OwnerSeat != frame.OwnerSeat ||
            candidate.SkillId != frame.SkillId || candidate.BindingId != frame.TriggerId ||
            candidate.SkillInstanceId != frame.SkillInstanceId || candidate.GameplayHash != frame.GameplayHash)
            throw new InvalidOperationException("A dying recovery lost its exact current program candidate and source instance.");
        RecoverProgramTargetTo(frame.Id, frame.OwnerSeat, dying.VictimSeat,
            SkillProgramNumberExpression.IntegerConstant, hp, true);
    }

    private sealed partial class ProgramSkillHost : IBoundRankBonusProgramHost
    {
        public void RevealTopCardsWithNextBooleanBonus(ProgramSkillFrame f, SkillProgramEffect e) => engine.RevealWithNextBooleanBonus(f, e);
        public SkillProgramStepOutcome ObtainBoundCardsAndArmNextRevealBonus(ProgramSkillFrame f, SkillProgramEffect e) => engine.ObtainAndArmNextRevealBonus(f, e);
        public void RecoverOtherDyingVictimTo(ProgramSkillFrame f, int hp) => engine.RecoverOtherDyingVictim(f, hp);
    }
}
