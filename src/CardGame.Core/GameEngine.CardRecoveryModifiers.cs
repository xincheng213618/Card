namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void GrantProgramCardRecoveryModifier(ProgramSkillFrame frame, IReadOnlyList<CardKind> cardKinds,
        int amount, SkillProgramDamageModifierExpiration expiration = SkillProgramDamageModifierExpiration.CurrentTurnEnd)
    {
        ValidateProgramTurnEffectGrant(frame);
        var grant = _turnCardUseEffects.GrantRecoveryModifier(_turnNumber, _currentSeat, frame.Id,
            frame.InstructionIndex - 1, CreateProgramTurnEffectSource(frame), cardKinds, amount, expiration);
        AdvanceEventRulesAndQueueFact(new CardRecoveryModifierGrantedEvent(grant));
    }

    // Every real Use declares once, including producers with no physical card or Action context.
    // The declarer's seat is the user of a supplied/converted card, not its provider or skill owner.
    private void ObserveIssuedCardRecoveryModifiers(IGameEvent payload)
    {
        if (payload is not CardUseDeclaredEvent declared ||
            declared.CardKind is not (CardKind.Peach or CardKind.Alcohol)) return;
        var modifiers = _turnCardUseEffects.GetRecoveryModifiers(_turnNumber, _currentSeat,
            declared.SourceSeat, declared.CardKind);
        if (modifiers.Count == 0) return;
        var use = LifecycleCardUse(declared.ResolutionId);
        if (use is null || use.SourceSeat != declared.SourceSeat || use.CardKind != declared.CardKind)
            throw new InvalidOperationException("A recovery use declaration lost its owning frame.");
        if (use.IssuedRecoveryModifiers is not null)
            throw new InvalidOperationException("A recovery use was issued more than once.");
        UpdateLifecycleCardUse(use.Id, frame => frame with { IssuedRecoveryModifiers = modifiers });
    }

    // Freeze the final requested amount before opening a replacement child. Re-entry returns the
    // same amount and never adds the grant twice; each additional target has its own receipt.
    private int ApplyIssuedCardRecoveryModifiers(long useId, int actorSeat, int targetSeat, int baseAmount)
    {
        var use = LifecycleCardUse(useId);
        if (use?.IssuedRecoveryModifiers is not { Count: > 0 } modifiers) return baseAmount;
        if (use.SourceSeat != actorSeat || modifiers.Any(item => item.Source.OwnerSeat != actorSeat))
            throw new InvalidOperationException("A recovery modifier must belong to the actual card user.");
        var previous = use.RecoveryModifierApplications?.SingleOrDefault(item => item.TargetSeat == targetSeat);
        if (previous is not null)
        {
            if (previous.BaseAmount != baseAmount)
                throw new InvalidOperationException("A resumed card recovery changed its base amount.");
            return previous.ModifiedAmount;
        }
        var amount = checked(baseAmount + modifiers.Sum(item => item.Amount));
        UpdateLifecycleCardUse(useId, frame => frame with
        {
            RecoveryModifierApplications = (frame.RecoveryModifierApplications ?? [])
                .Append(new(targetSeat, baseAmount, amount)).ToArray()
        });
        var current = baseAmount;
        foreach (var modifier in modifiers)
        {
            AdvanceEventRulesAndQueueFact(new ProgramCardRecoveryModifiedEvent(useId, modifier.Source,
                actorSeat, targetSeat, use.CardKind, current, current + modifier.Amount));
            current += modifier.Amount;
        }
        return amount;
    }
}
