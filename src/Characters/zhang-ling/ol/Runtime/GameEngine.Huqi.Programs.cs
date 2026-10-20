namespace CardGame.Core;

public sealed partial class GameEngine
{

    private SkillProgramStepOutcome UseHuqiSlash(ProgramSkillFrame supplied, string bind)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        var binding = frame.CardSetBindings.Single(b => b.Name == bind);
        // A final judgment face is retained even if another real judgment effect
        // has already claimed the card. Only an entity still in Processing is
        // our cleanup cost; a claimed entity must never be taken back from hand.
        if (binding.CardIds is [var cardId] && _cardZones.GetLocation(cardId) == CardLocation.Processing)
        {
            var before = _movementSequence;
            var movement = new CategoryTalismanMovementReceipt(SkillProgramEffectOp.UseRedJudgmentSlashAtDamageSource,
                frame.InstructionIndex, cardId, CardLocation.Processing, CardLocation.DiscardPile, before, before, bind);
            ReplaceRuntimeTop(frame = frame with { CategoryTalismanMovement = movement, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
            MoveCard(GetAttackCard(cardId), movement.From, movement.To, new("program.huqi.judgment-finished"));
            ReplaceRuntimeTop(frame = GetActiveProgramFrame(frame.Id) with { CategoryTalismanMovement = movement with { After = _movementSequence } });
            if (!TryDrainFireTargetMovement(frame)) ReturnCategoryTalismanMovement(frame);
            return SkillProgramStepOutcome.AwaitChild;
        }
        return IssueHuqiSlash(frame, bind);
    }
    private SkillProgramStepOutcome IssueHuqiSlash(ProgramSkillFrame frame, string bind)
    {
        var binding = frame.CardSetBindings.Single(b => b.Name == bind);
        if (binding.CardIds.Count == 0 && binding.FrozenRevealedSuit is null) return SkillProgramStepOutcome.Continue;
        var suit = binding.FrozenRevealedSuit ??
            throw new InvalidOperationException("Huqi requires its frozen actual final judgment suit.");
        var issued = CompleteProgramEventHistory().OfType<ZhangLingBindingIssuedEvent>().Single(e => e.FrameId == frame.Id);
        if (suit is not (Suit.Heart or Suit.Diamond) || issued.DamageSourceSeat is not { } source || !IsValidPlayerSeat(source) ||
            !_players[source].IsAlive || source == frame.OwnerSeat || !_players[frame.OwnerSeat].IsAlive ||
            !CanUseSlashTarget(_players[frame.OwnerSeat], _players[source], new Card(0, CardKind.Slash, Suit.None, 0),
                new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId),
                CardKind.Slash, noEffectiveRank: true, physicalCardIds: []))
            return SkillProgramStepOutcome.Continue;
        var attack = ActiveCardAttack?.ResolutionId;
        var duel = ActiveDuel?.OwnerFrameId;
        var childId = _resolutionSequence + 1;
        ReplaceRuntimeTop(frame = frame with { HuqiSlash = new(frame.InstructionIndex, source, suit, attack, duel, childId) });
        if (attack is { } attackId) UpdateCardAttackState(attackId, state => state! with { Active = false });
        if (duel is { } duelId) UpdateCardContinuations(duelId, state => state with { Duel = state.Duel! with { Active = false } });
        return UseTriggeredProgramVirtualSlash(frame, source);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.BeforeParentResume, 26050)]
    private bool RestoreHuqiSuspendedAttack(AttackCompletionReceipt completion)
    {
        if (completion.ProgramFrameId is not { } parentId || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { HuqiSlash: { } receipt } parent || parent.Id != parentId) return false;
        if (receipt.ChildUseId != completion.ResolutionId || receipt.InstructionIndex != parent.InstructionIndex ||
            _resolutionStack.Any(f => f.Id == receipt.ChildUseId)) throw new InvalidOperationException("Huqi lost its exact retired native Slash child.");
        if (receipt.SuspendedAttackId is { } attackId) UpdateCardAttackState(attackId, state => state! with { Active = true });
        if (receipt.SuspendedDuelId is { } duelId) UpdateCardContinuations(duelId, state => state with { Duel = state.Duel! with { Active = true } });
        ReplaceRuntimeTop(parent with { HuqiSlash = null }); AdvanceRuntimeProgram(parent.Id); return true;
    }
}
