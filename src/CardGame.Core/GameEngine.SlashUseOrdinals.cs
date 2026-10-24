namespace CardGame.Core;

public sealed partial class GameEngine
{
    // The native declaration fact is the receipt. A nested use can finish first,
    // but cannot move an already issued declaration's position in this prefix.
    private bool MatchesSlashUseOrdinalModifier(SkillProgramDamageModifier modifier,
        IDamageAttempt? attack, int actorSeat, CardKind effectiveKind)
    {
        var elemental = modifier.Condition ==
            SkillProgramDamageModifierCondition.OwnerActualTurnElementalSlashUseOrdinalWithinLimit;
        if (!IsSlashCard(effectiveKind) || elemental && effectiveKind == CardKind.Slash ||
            !IsValidPlayerSeat(actorSeat)) return false;
        var dependencies = _contentRegistry.ProgramDependencies;
        if (elemental ? !dependencies.HasActualTurnElementalSlashUseDamageModifiers :
            !dependencies.HasRoundSlashUseDamageModifiers) return false;

        var history = CompleteProgramEventHistory().ToArray();
        var end = history.Length;
        CurrentTurnCardUseKindsRecordedEvent? current = null;
        if (attack is not null)
        {
            if (attack is not CardAttackHandle || attack.IsSourceLess || attack.IsChainPropagation ||
                attack.IsDelayedJudgmentDamage || attack.IsProgramJudgmentDamage ||
                attack.CardUserSeat != actorSeat ||
                _resolutionStack.SingleOrDefault(frame => frame.Id == attack.ResolutionId) is not CardUseFrame use)
                return false;
            var matches = history.Select((fact, index) => (fact, index)).Where(item =>
                item.fact is CurrentTurnCardUseKindsRecordedEvent fact &&
                fact.OriginFrameId == use.Id && !fact.IsNullificationUse &&
                fact.RoundActionType == CardActionType.Use && fact.ProgramInstructionIndex is null).ToArray();
            if (matches is not [var match] || match.fact is not CurrentTurnCardUseKindsRecordedEvent issued ||
                issued.CardActionId != use.Action?.ActionId ||
                issued.RoundProviderSeat != (use.Action?.ProviderSeat ?? issued.ActorSeat) ||
                issued.CardCategoryMask != 1 << (int)SkillProgramCardCategory.Basic)
                throw new InvalidOperationException("Slash damage requires its exact effective native-use declaration.");
            current = issued;
            end = match.index + 1;
            if (SlashUseKindAtPrefix(issued, history, end, current) != effectiveKind || effectiveKind != use.CardKind)
                throw new InvalidOperationException("Slash damage lost its exact native effective-kind evolution.");
        }
        else if (elemental ? !HasOngoingWholeActualUseTurn : _roundNumber < 1)
            return false;

        var boundary = -1;
        for (var index = end - 1; index >= 0; index--)
        {
            if (elemental ? history[index] is TurnStartedEvent : history[index] is RoundStartedEvent)
            { boundary = index; break; }
        }
        if (boundary < 0) return false;
        if (elemental)
        {
            var turn = (TurnStartedEvent)history[boundary];
            if (turn.TurnNumber < 1 || current is not null &&
                    (current.TurnNumber != turn.TurnNumber || current.TurnOwnerSeat != turn.ActorSeat) ||
                history.Skip(boundary + 1).Take(end - boundary - 1)
                    .OfType<ActualTurnCursorReturnedEvent>().Any()) return false;
        }
        else if (((RoundStartedEvent)history[boundary]).RoundNumber < 1) return false;

        var count = CountSlashUsePrefix(history, boundary + 1, end, actorSeat, elemental, current);
        // A preview represents the next real declaration, never consumes a use.
        var ordinal = current is null ? checked(count + 1) : count;
        return ordinal > 0 && ordinal <= modifier.MaximumUseOrdinal;
    }

    private int CountSlashUsePrefix(IReadOnlyList<IGameEvent> history, int start, int end,
        int expectedActorSeat, bool elemental, CurrentTurnCardUseKindsRecordedEvent? current)
    {
        // Reuse the ledger's exact native actor-replacement contract. Defending
        // Responses share an origin with the attack and must not follow it.
        var replacements = history.Select((fact, index) => (fact, index)).Where(item =>
                item.index >= start && item.fact is ProgramCardUseActorReplacedEvent change &&
                (item.index < end || current is not null && change.CardUseFrameId == current.OriginFrameId))
            .Select(item => (ProgramCardUseActorReplacedEvent)item.fact)
            .GroupBy(fact => fact.CardUseFrameId).ToDictionary(group => group.Key, group => group.ToArray());
        var seen = new HashSet<(long? ActionId, long? LegacyUseId, int? InstructionIndex)>();
        var count = 0;
        for (var index = start; index < end; index++)
        {
            if (history[index] is not CurrentTurnCardUseKindsRecordedEvent fact) continue;
            (long? ActionId, long? LegacyUseId, int? InstructionIndex) key = fact.CardActionId is not null
                ? (fact.CardActionId, null, null) : (null, fact.OriginFrameId, fact.ProgramInstructionIndex);
            if (!seen.Add(key) || fact.EffectiveCardKind is null ||
                fact.RoundActionType is not (CardActionType.Use or CardActionType.Response) ||
                fact.RoundProviderSeat is not { } provider || !IsValidPlayerSeat(provider))
                throw new InvalidOperationException("A Slash-use prefix requires unique effective declarations and providers.");
            if (fact.RoundActionType != CardActionType.Use) continue;
            var kind = SlashUseKindAtPrefix(fact, history, end, current);
            var actor = fact.ActorSeat;
            if (replacements.TryGetValue(fact.OriginFrameId, out var changes))
                foreach (var change in changes)
                {
                    if (change.PreviousActorSeat != actor || change.OwnerSeat != actor ||
                        change.ProviderSeat != provider || !IsValidPlayerSeat(change.ActorSeat))
                        throw new InvalidOperationException("A Slash-use prefix lost its native actor-replacement chain.");
                    actor = change.ActorSeat;
                }
            if (fact == current && actor != expectedActorSeat) return 0;
            if (actor == expectedActorSeat && IsSlashCard(kind) && (!elemental || kind != CardKind.Slash))
                count = checked(count + 1);
        }
        return count;
    }

    // Committed native conversions keep the declaration's identity and position.
    // They do not rewrite its immutable original kind. Only the current use may
    // follow its own later change; unrelated later uses cannot change this prefix.
    private CardKind SlashUseKindAtPrefix(CurrentTurnCardUseKindsRecordedEvent fact,
        IReadOnlyList<IGameEvent> history, int end, CurrentTurnCardUseKindsRecordedEvent? current) =>
        SlashUseKindAtPrefix(fact, history, end, current, fact.EffectiveCardKind ??
            throw new InvalidOperationException("A Slash-use ordinal requires its frozen declaration kind."));

    private CardKind SlashUseKindAtPrefix(CurrentTurnCardUseKindsRecordedEvent fact,
        IReadOnlyList<IGameEvent> history, int end, CurrentTurnCardUseKindsRecordedEvent? current, CardKind original)
    {
        if (fact.EffectiveCardKind is { } recorded && recorded != original)
            throw new InvalidOperationException("A proven declaration kind differs from its native declaration receipt.");
        var changes = history.Select((payload, index) => (payload, index)).Where(item =>
            item.payload is ProgramCurrentSlashFireChangedEvent changed && changed.CardUseFrameId == fact.OriginFrameId &&
            (item.index < end || fact == current)).ToArray();
        if (changes.Length == 0) return original;
        var declarationIndexes = history.Select((payload, index) => (payload, index))
            .Where(item => Equals(item.payload, fact)).Select(item => item.index).ToArray();
        if (changes is not [var entry] || entry.payload is not ProgramCurrentSlashFireChangedEvent change ||
            declarationIndexes is not [var declarationIndex] || entry.index <= declarationIndex ||
            fact.RoundActionType != CardActionType.Use || fact.CardActionId != change.ActionId ||
            !IsSlashCard(original) || change.OriginalKind != original || change.FinalKind != CardKind.FireSlash ||
            history.OfType<CardUseDeclaredEvent>().Count(e => e.ResolutionId == fact.OriginFrameId &&
                e.SourceSeat == fact.ActorSeat && e.CardKind == original) != 1)
            throw new InvalidOperationException("An effective Slash kind requires one exact native committed conversion.");

        var starts = history.Select((payload, index) => (payload, index)).Where(item =>
            item.payload is ProgramBindingStartedEvent started && started.FrameId == change.ProgramFrameId).ToArray();
        if (starts is not [var start] || start.index >= entry.index ||
            start.payload is not ProgramBindingStartedEvent binding ||
            binding.SkillId != change.Source.SkillId || binding.BindingId != change.Source.BindingId ||
            binding.SkillInstanceId != change.Source.SkillInstanceId || binding.OwnerSeat != change.Source.OwnerSeat ||
            binding.Window != SkillProgramTriggerWindow.CardUseCommitted ||
            _contentRegistry.Skills.GetValueOrDefault(binding.SkillId)?.Program is not { } program ||
            ProgramInstructionResolver.Default.FindTrigger(program, binding.BindingId) is not { } trigger ||
            trigger.Window != binding.Window || trigger.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor ||
            trigger.CardKinds.Count != 0 && !ProgramCardKindMatch.Contains(trigger.CardKinds, original) || !ProgramInstructionResolver.Default.Features(trigger)
                .HasOperation(SkillProgramEffectOp.OfferCurrentSlashFireAndExtraTarget))
            throw new InvalidOperationException("A converted Slash lost its exact committed binding issuance.");

        var actor = fact.ActorSeat;
        foreach (var replacement in history.Take(entry.index).OfType<ProgramCardUseActorReplacedEvent>()
                     .Where(e => e.CardUseFrameId == fact.OriginFrameId))
        {
            if (replacement.PreviousActorSeat != actor || replacement.OwnerSeat != actor ||
                replacement.ProviderSeat != fact.RoundProviderSeat || !IsValidPlayerSeat(replacement.ActorSeat))
                throw new InvalidOperationException("A converted Slash lost its earlier native actor chain.");
            actor = replacement.ActorSeat;
        }
        if (actor != change.Source.OwnerSeat)
            throw new InvalidOperationException("A committed Slash conversion must belong to its actual card user.");

        if (_resolutionStack.SingleOrDefault(frame => frame.Id == change.ProgramFrameId) is ProgramSkillFrame producer)
        {
            var plan = ProgramInstructionResolver.Default.Resolve(producer, program);
            if (producer.SkillId != binding.SkillId || producer.TriggerId != binding.BindingId ||
                producer.SkillInstanceId != binding.SkillInstanceId || producer.OwnerSeat != binding.OwnerSeat ||
                producer.GameplayHash != program.GameplayHash || producer.WindowContext is not
                    { Window: SkillProgramTriggerWindow.CardUseCommitted, CardUse: { } useContext } ||
                useContext.ParentCardUseFrameId != fact.OriginFrameId || useContext.CardActionId != change.ActionId ||
                producer.InstructionIndex < 1 || producer.InstructionIndex > plan.Instructions.Count ||
                !plan.Instructions.Take(producer.InstructionIndex)
                    .Any(effect => effect.Op == SkillProgramEffectOp.OfferCurrentSlashFireAndExtraTarget))
                throw new InvalidOperationException("A live Slash conversion lost its exact paused committed producer.");
        }
        else if (history.Skip(entry.index + 1).OfType<ProgramBindingResolvedEvent>().Count(resolved =>
                     resolved.FrameId == binding.FrameId && resolved.SkillId == binding.SkillId &&
                     resolved.BindingId == binding.BindingId && resolved.SkillInstanceId == binding.SkillInstanceId &&
                     resolved.OwnerSeat == binding.OwnerSeat && resolved.Window == binding.Window && resolved.Activated) != 1)
            throw new InvalidOperationException("A historical Slash conversion lost its exact native producer return.");

        if (LifecycleCardUse(fact.OriginFrameId) is { } use)
        {
            if (use.CurrentSlashFirePolicy is not { } policy || policy.ProgramFrameId != change.ProgramFrameId ||
                policy.Source != change.Source || policy.OriginalKind != original ||
                policy.OriginalAction.ActionId != change.ActionId || policy.ExtraTargetSeat != change.ExtraTargetSeat)
                throw new InvalidOperationException("A converted Slash lost its owning native kind-change receipt.");
            AssertCurrentSlashFirePolicy(use);
        }
        return change.FinalKind;
    }
}
