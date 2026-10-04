namespace CardGame.Core;

internal static class PrivateOfferComposition
{
    internal static void ValidateActivation(string path, IReadOnlyList<SkillProgramEffect> effects,
        int minCards, int maxCards, int minTargets, int maxTargets, SkillProgramTargetKind targetKind,
        int? turnUses, int? phaseUses, int? gameUses, bool afterDeath)
    {
        if (effects.Any(e => e.Op is SkillProgramEffectOp.DepositBoundPrivateCardOffer or SkillProgramEffectOp.ResolveDeferredPrivateCardOffer))
            throw new InvalidOperationException($"{path}: private offers require their own Ending and due-start triggers.");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.ResolveGameTargetHandHpChoice) &&
            (effects is not [{ Op: SkillProgramEffectOp.ResolveGameTargetHandHpChoice }] || minCards != 0 || maxCards != 0 ||
             minTargets != 1 || maxTargets != 1 || targetKind != SkillProgramTargetKind.AnyLiving ||
             turnUses is not null || phaseUses is not null || gameUses is not null || afterDeath))
            throw new InvalidOperationException($"{path}: game-target hand/HP choice requires one standalone zero-card living-target activation and its own game-target ledger.");
    }

    internal static void ValidateTrigger(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow window, SkillProgramTriggerSubject subject, SkillProgramTurnOwnerScope scope, bool optional)
    {
        if (effects.Any(e => e.Op == SkillProgramEffectOp.ResolveGameTargetHandHpChoice))
            throw new InvalidOperationException($"{path}: game-target hand/HP choice requires an active Play command.");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.DepositBoundPrivateCardOffer))
        {
            if (window != SkillProgramTriggerWindow.TurnEnding || subject != SkillProgramTriggerSubject.Owner || scope != SkillProgramTurnOwnerScope.Own ||
                effects is not [{ Op: SkillProgramEffectOp.SelectOwnedCards, Target: SkillProgramEffectTarget.Owner,
                    MinimumCards: 1, MaximumCards: 1, ResultBind: { } selected, Condition.Kind: SkillProgramConditionKind.Always } own,
                    { Op: SkillProgramEffectOp.SelectTarget, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } target,
                    { Op: SkillProgramEffectOp.DepositBoundPrivateCardOffer, SourceBind: { } bind }] ||
                !own.Zones.SequenceEqual([CardZoneKind.Hand]) || selected != bind ||
                target.TargetKind is not (SkillProgramTargetKind.OtherLiving or SkillProgramTargetKind.OtherLivingMale))
                throw new InvalidOperationException($"{path}: deposit requires own Ending, one exact owner Hand selection, one other-living target and a terminal private offer.");
        }
        if (effects.Any(e => e.Op == SkillProgramEffectOp.ResolveDeferredPrivateCardOffer) &&
            (window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || subject != SkillProgramTriggerSubject.Owner || optional ||
             effects is not [{ Op: SkillProgramEffectOp.ResolveDeferredPrivateCardOffer }]))
            throw new InvalidOperationException($"{path}: deferred resolution requires one mandatory owner actual-turn-start operation.");
    }

    internal static void ValidateBindings(string path, IReadOnlyList<SkillProgramTrigger> triggers)
    {
        foreach (var deposit in triggers.SelectMany(t => t.Effects).Where(e => e.Op == SkillProgramEffectOp.DepositBoundPrivateCardOffer))
            if (triggers.SingleOrDefault(t => t.Id == deposit.StateId) is not { Optional: false, Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } resolver ||
                resolver.Effects is not [{ Op: SkillProgramEffectOp.ResolveDeferredPrivateCardOffer }])
                throw new InvalidOperationException($"{path}: private offer references no exact mandatory due resolver '{deposit.StateId}'.");
        foreach (var resolver in triggers.Where(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveDeferredPrivateCardOffer)))
            if (!triggers.SelectMany(t => t.Effects).Any(e => e.Op == SkillProgramEffectOp.DepositBoundPrivateCardOffer && e.StateId == resolver.Id))
                throw new InvalidOperationException($"{path}: a due resolver must belong to an actual deposit in the same program.");
    }
}
