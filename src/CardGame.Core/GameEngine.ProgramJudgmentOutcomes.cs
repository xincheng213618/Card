namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramEffect? JudgmentOutcomeInstruction(ProgramSkillFrame frame, int instructionIndex)
    {
        if (!_contentRegistry.Skills.TryGetValue(frame.SkillId, out var definition) ||
            definition.Program is not { } program || program.GameplayHash != frame.GameplayHash) return null;
        var instructions = ProgramInstructionResolver.Default.Resolve(frame, program).Instructions;
        return instructionIndex >= 1 && instructionIndex <= instructions.Count &&
            instructions[instructionIndex - 1] is { Op: SkillProgramEffectOp.StartJudgment, OutcomeBind: not null } effect
            ? effect : null;
    }

    private bool TryReturnProgramJudgmentOutcome(JudgmentFrame judgment)
    {
        var frame = GetActiveProgramFrame(judgment.ParentFrameId);
        if (JudgmentOutcomeInstruction(frame, frame.InstructionIndex) is not { } effect) return false;
        if (!IsValidProgramJudgmentContinuation(judgment, frame) ||
            frame.JudgmentOutcomeBindings?.Any(binding => binding.Name == effect.OutcomeBind) == true)
            throw new InvalidOperationException("A judgment outcome lost its unique paused producer.");
        var results = ProgramEventHistory<JudgmentResolvedEvent>().Where(result => result.ResolutionId == judgment.Id).ToArray();
        if (results is not [var result] || result.ParentResolutionId != frame.Id ||
            result.TargetSeat != judgment.TargetSeat || result.Reason != effect.JudgmentReason)
            throw new InvalidOperationException("A judgment outcome requires its exact finalized native result.");
        var outcome = new ProgramJudgmentOutcomeBinding(effect.OutcomeBind!, judgment.Id, frame.InstructionIndex,
            judgment.TargetSeat, judgment.Reason, effect.ResultBind!, result.CardId, result.Suit, result.Rank);
        ReplaceRuntimeFrame(frame.Id, frame with
        { JudgmentOutcomeBindings = (frame.JudgmentOutcomeBindings ?? []).Append(outcome).ToArray() });
        AdvanceEventRulesAndQueueFact(new ProgramJudgmentOutcomeRecordedEvent(frame.Id, outcome));
        // A claim transferred ownership. Its public result remains usable, but
        // the original instruction no longer owns that physical card to clean.
        var retained = result.CardId is { } id && IsUnclaimedJudgmentOutcomeCard(judgment, id) ? new[] { id } : [];
        SetProgramCardSet(frame.Id, effect.ResultBind!, retained, SkillProgramCardSetVisibility.Public,
            retained.Select(_ => CardLocation.Processing).ToArray(), retained.Length == 1 ? result.Suit : null);
        AdvanceRuntimeProgram(frame.Id);
        return true;
    }

    private bool IsUnclaimedJudgmentOutcomeCard(JudgmentFrame judgment, int cardId)
    {
        if (_cardZones.GetLocation(cardId) != CardLocation.Processing ||
            ProgramEventHistory<ProgramJudgmentCardClaimedEvent>().Any(claim => claim.JudgmentFrameId == judgment.Id &&
                claim.OwnerSeat == judgment.TargetSeat && claim.CardId == cardId)) return false;
        var last = CompleteProgramEventHistory().SkipWhile(fact => fact is not JudgmentResolvedEvent result || result.ResolutionId != judgment.Id)
            .OfType<CardMovedEvent>().LastOrDefault(move => move.CardId == cardId);
        return last is not null && last.From == CardLocation.Judgment(judgment.TargetSeat) &&
            last.To == CardLocation.Processing && last.Reason.Value == "skill-program.judgment.result";
    }

    private bool IsValidProgramJudgmentOutcome(ProgramSkillFrame frame, ProgramJudgmentOutcomeBinding outcome)
    {
        if (outcome.JudgmentFrameId <= 0 || outcome.InstructionIndex > frame.InstructionIndex ||
            JudgmentOutcomeInstruction(frame, outcome.InstructionIndex) is not { } effect ||
            effect.OutcomeBind != outcome.Name || effect.ResultBind != outcome.ResultBind || effect.JudgmentReason != outcome.Reason ||
            !IsValidPlayerSeat(outcome.TargetSeat) ||
            outcome.CardId.HasValue != outcome.Suit.HasValue || outcome.CardId.HasValue != outcome.Rank.HasValue ||
            outcome.Suit is { } suit && !Enum.IsDefined(suit) || outcome.Rank is < 0 or > 13)
            return false;
        var results = ProgramEventHistory<JudgmentResolvedEvent>().Where(result => result.ResolutionId == outcome.JudgmentFrameId).ToArray();
        return results is [var result] && result.ParentResolutionId == frame.Id && result.TargetSeat == outcome.TargetSeat &&
            result.Reason == outcome.Reason && result.CardId == outcome.CardId && result.Suit == outcome.Suit && result.Rank == outcome.Rank &&
            ProgramEventHistory<ProgramJudgmentOutcomeRecordedEvent>().Count(recorded => recorded.ProgramFrameId == frame.Id && recorded.Outcome == outcome) == 1;
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 1950)]
    private void AssertProgramJudgmentOutcomes(ProgramSkillFrame frame)
    {
        if (frame.JudgmentOutcomeBindings is not { } outcomes) return;
        if (outcomes.Select(outcome => outcome.Name).Distinct(StringComparer.Ordinal).Count() != outcomes.Count ||
            outcomes.Any(outcome => !IsValidProgramJudgmentOutcome(frame, outcome)))
            throw new InvalidOperationException("A program judgment outcome lost its frozen issued value.");
    }

    private bool IsJudgmentOutcomePrevention(SkillProgramTrigger trigger) =>
        trigger.Effects.Any(effect => effect is { Op: SkillProgramEffectOp.StartJudgment, OutcomeBind: not null }) &&
        trigger.Effects.Any(effect => effect is { Op: SkillProgramEffectOp.PreventCurrentDamage,
            Condition.Kind: SkillProgramConditionKind.JudgmentOutcomeMatchesSuits });

    private bool HasRecipientJudgmentOutcomePrevention(int targetSeat) => IsValidPlayerSeat(targetSeat) &&
        _players[targetSeat].IsAlive && CollectProgramTriggerCandidates(_players[targetSeat], SkillProgramTriggerWindow.BeforeDamageApplied)
            .Any(candidate => GetProgramTrigger(candidate) is { Subject: SkillProgramTriggerSubject.DamageTarget } trigger &&
                IsJudgmentOutcomePrevention(trigger));
}
