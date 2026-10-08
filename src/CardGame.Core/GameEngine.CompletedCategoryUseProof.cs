namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Accepted actions are immutable issuance snapshots. Native target and actor
    // changes keep their ActionId, so completion must prove that history rather
    // than equating the first snapshot with the final action.
    private bool HasExactAcceptedActualHandGainUse(CardUseFrame use, CardActionContext completed)
    {
        if (use.Action is not { Type: CardActionType.Use } current || completed.Type != CardActionType.Use ||
            current.ActionId != completed.ActionId || current.ActorSeat != completed.ActorSeat ||
            current.ProviderSeat != completed.ProviderSeat || current.EffectiveKind != completed.EffectiveKind ||
            !current.TargetSeats.SequenceEqual(completed.TargetSeats) ||
            !current.PhysicalCards.SequenceEqual(completed.PhysicalCards) ||
            !current.ConversionChain.SequenceEqual(completed.ConversionChain)) return false;
        var history = CompleteProgramEventHistory().ToArray();
        var declarations = history.OfType<CardUseDeclaredEvent>().Where(e => e.ResolutionId == use.Id && e.CardId == use.CardId).ToArray();
        if (declarations is not [var declared] || !ShownEntityUseActorMatches(use, declared.SourceSeat, completed.ProviderSeat)) return false;
        for (var i = history.Length - 1; i >= 0; i--)
        {
            if (history[i] is not CardActionAcceptedEvent { Action: var accepted } || accepted.ActionId != completed.ActionId ||
                accepted.Type != CardActionType.Use || accepted.ProviderSeat != completed.ProviderSeat ||
                accepted.ParentActionId != completed.ParentActionId || accepted.RequesterSeat != completed.RequesterSeat ||
                accepted.ResponderSeat != completed.ResponderSeat || accepted.OpponentSeat != completed.OpponentSeat ||
                accepted.EffectiveSuit != completed.EffectiveSuit || accepted.EffectiveRank != completed.EffectiveRank ||
                !accepted.PhysicalCards.SequenceEqual(completed.PhysicalCards)) continue;
            if (RebuildAcceptedActualHandGainUse(use, accepted, completed, history, i)) return true;
        }
        return false;
    }

    private bool ActualHandGainMutationBinding(long frameId, string skill, int owner, SkillProgramTriggerWindow window,
        SkillProgramEffectOp operation)
    {
        var started = CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Where(e => e.FrameId == frameId &&
            e.SkillId == skill && e.OwnerSeat == owner && e.Window == window).ToArray();
        return started is [var fact] && _contentRegistry.GetSkill(skill).Program is { } program &&
            ProgramInstructionResolver.Default.FindTrigger(program, fact.BindingId) is { } trigger &&
            ProgramInstructionResolver.Default.Features(trigger).HasOperation(operation);
    }

    private bool RebuildAcceptedActualHandGainUse(CardUseFrame use, CardActionContext accepted, CardActionContext completed,
        IReadOnlyList<IGameEvent> history, int acceptedIndex)
    {
        var actor = accepted.ActorSeat; var kind = accepted.EffectiveKind;
        var chain = accepted.ConversionChain.ToList();
        var designated = accepted.TargetSeats.ToList(); var actual = accepted.TargetSeats.ToList();
        // A later native finalized acceptance already proves its complete targets;
        // actual redirect targets may remain distinct from the designated action.
        if (actor == completed.ActorSeat && kind == completed.EffectiveKind && chain.SequenceEqual(completed.ConversionChain) &&
            designated.SequenceEqual(completed.TargetSeats)) return true;
        foreach (var fact in history.Skip(acceptedIndex + 1))
        {
            if (fact is ProgramCardUseActorReplacedEvent changedActor && changedActor.CardUseFrameId == use.Id)
            {
                if (changedActor.PreviousActorSeat != actor || changedActor.OwnerSeat != actor || changedActor.ProviderSeat != completed.ProviderSeat ||
                    !ActualHandGainMutationBinding(changedActor.FrameId, changedActor.SkillId, changedActor.OwnerSeat,
                        SkillProgramTriggerWindow.CardUseTargetsFinalized, SkillProgramEffectOp.ReplaceCurrentCardUseActor)) return false;
                actor = changedActor.ActorSeat;
            }
            else if (fact is ShortRangeSlashTargetResolvedEvent shortRange && shortRange.CardUseFrameId == use.Id && shortRange.Added)
            {
                if (!IsShortRangeSlashTargetFact(use, shortRange, designated)) return false;
                var issued = shortRange.Receipt!;
                designated.Add(issued.AddedTargetSeat); actual.Add(issued.AddedTargetSeat);
            }
            else if (fact is SameTypeAidTargetAddedEvent aid && aid.Use.CardUseFrameId == use.Id)
            {
                if (aid.Use.CardActionId != completed.ActionId || !IsSameTypeAidTargetFact(use, aid) || actual.Contains(aid.RecipientSeat)) return false;
                designated.Add(aid.RecipientSeat); actual.Add(aid.RecipientSeat);
            }
            else if (fact is OverflowUseTargetsCanceledEvent canceled && canceled.Receipt.CardUseFrameId == use.Id)
            {
                if (canceled.Receipt.Source.OwnerSeat != actor || !IsOverflowTargetCancellationFact(use, canceled, actual)) return false;
                designated = canceled.Receipt.ResultTargetSeats.ToList(); actual = canceled.Receipt.ResultTargetSeats.ToList();
            }
            else if (fact is ProgramCardUseTargetAddedEvent added && added.CardUseFrameId == use.Id)
            {
                // These producers also emit a typed issuance proof; consume that
                // proof once instead of counting its public summary a second time.
                if (history.OfType<ShortRangeSlashTargetResolvedEvent>().Any(e => e.CardUseFrameId == use.Id && e.Added &&
                        e.Receipt is { } issued && issued.ProgramFrameId == added.FrameId && issued.AddedTargetSeat == added.TargetSeat) ||
                    history.OfType<SameTypeAidTargetAddedEvent>().Any(e => e.Use.CardUseFrameId == use.Id &&
                        e.ProgramFrameId == added.FrameId && e.RecipientSeat == added.TargetSeat) ||
                    history.OfType<CompletedCategoryCompoundTargetsIssuedEvent>().Any(e => e.CardUseFrameId == use.Id &&
                        e.ProgramFrameId == added.FrameId && e.Targets.Count == e.OriginalTargets.Count + 2 && e.Targets[^2] == added.TargetSeat) ||
                    history.OfType<DesignatedExtraTargetResolvedEvent>().Any(e => e.CardUseFrameId == use.Id &&
                        e.ProgramFrameId == added.FrameId && e.AddedTargetSeats.Count > 0 && e.AddedTargetSeats[0] == added.TargetSeat) ||
                    history.OfType<RoundGainedTrickTargetResolvedEvent>().Any(e => e.Qualification.CardUseFrameId == use.Id &&
                        e.ProgramFrameId == added.FrameId && e.AddedTargetSeats.Count > 0 && e.AddedTargetSeats[0] == added.TargetSeat) ||
                    history.OfType<UniqueLeaderTrickTargetResolvedEvent>().Any(e => e.CardUseFrameId == use.Id &&
                        e.ProgramFrameId == added.FrameId && e.AddedTargetSeats.Count > 0 && e.AddedTargetSeats[0] == added.TargetSeat) ||
                    history.OfType<RecipientCategorySlashTargetsResolvedEvent>().Any(e => e.CardUseFrameId == use.Id &&
                        e.FrameId == added.FrameId && e.AddedTargets.Contains(added.TargetSeat))) continue;
                if (actual.Contains(added.TargetSeat) || !ActualHandGainMutationBinding(added.FrameId, added.SkillId, added.OwnerSeat,
                        SkillProgramTriggerWindow.CardUseTargetsFinalized, SkillProgramEffectOp.AddCurrentCardUseTarget)) return false;
                designated.Add(added.TargetSeat); actual.Add(added.TargetSeat);
                if (kind == CardKind.BorrowedSword) return false;
            }
            else if (fact is CompletedCategoryCompoundTargetsIssuedEvent compound && compound.CardUseFrameId == use.Id)
            {
                if (kind != CardKind.BorrowedSword || compound.ActionId != completed.ActionId ||
                    compound.Source.OwnerSeat != actor ||
                    !designated.SequenceEqual(compound.OriginalTargets) || !actual.SequenceEqual(compound.OriginalTargets) ||
                    compound.Targets.Count != compound.OriginalTargets.Count + 2 ||
                    !compound.Targets.Take(compound.OriginalTargets.Count).SequenceEqual(compound.OriginalTargets) ||
                    !IsValidPlayerSeat(compound.Targets[^2]) || !IsValidPlayerSeat(compound.Targets[^1]) || compound.Targets[^2] == compound.Targets[^1] ||
                    _contentRegistry.GetSkill(compound.Source.SkillId).Program is not { } program || program.GameplayHash != compound.GameplayHash ||
                    ProgramInstructionResolver.Default.Find(program, ProgramInstructionSourceKind.Trigger, compound.Source.BindingId) is not { } plan ||
                    plan.Trigger?.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || compound.InstructionIndex < 1 ||
                    compound.InstructionIndex > plan.Instructions.Count || plan.GetPausedInstruction(compound.InstructionIndex).Effect.Op != SkillProgramEffectOp.AddCurrentCardUseTarget ||
                    history.OfType<CompletedCategoryCompoundTargetsIssuedEvent>().Count(e => e.ProgramFrameId == compound.ProgramFrameId &&
                        e.CardUseFrameId == use.Id && e.InstructionIndex == compound.InstructionIndex) != 1 ||
                    history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == compound.ProgramFrameId && e.SkillId == compound.Source.SkillId &&
                        e.BindingId == compound.Source.BindingId && e.SkillInstanceId == compound.Source.SkillInstanceId &&
                        e.OwnerSeat == compound.Source.OwnerSeat && e.Window == plan.Trigger.Window) != 1 ||
                    history.OfType<ProgramCardUseTargetAddedEvent>().Count(e => e.FrameId == compound.ProgramFrameId &&
                        e.SkillId == compound.Source.SkillId && e.OwnerSeat == compound.Source.OwnerSeat && e.CardUseFrameId == use.Id &&
                        e.TargetSeat == compound.Targets[^2]) != 1) return false;
                designated = compound.Targets.ToList(); actual = compound.Targets.ToList();
            }
            else if (fact is DesignatedExtraTargetResolvedEvent designatedExtra && designatedExtra.CardUseFrameId == use.Id && designatedExtra.AddedTargetSeats.Count > 0)
            {
                IReadOnlyList<int> originalTargets = designated;
                // Only this exact issuance may normalize the old implicit self
                // DrawTwo acceptance before extending its designated targets.
                if (kind == CardKind.DrawTwo && designated.Count == 0 && actual.Count == 0 &&
                    designatedExtra.OriginalTargetSeats is [var self] && self == actor)
                    originalTargets = [actor];
                if (designatedExtra.Source.OwnerSeat != actor || !actual.SequenceEqual(designated) ||
                    !IsDesignatedExtraTargetFact(use, designatedExtra, originalTargets)) return false;
                designated = designatedExtra.ResultTargetSeats.ToList(); actual = designatedExtra.ResultTargetSeats.ToList();
            }
            else if (fact is UniqueLeaderTrickTargetResolvedEvent leaderTarget && leaderTarget.CardUseFrameId == use.Id &&
                (leaderTarget.AddedTargetSeats.Count > 0 || kind == CardKind.DrawTwo && designated.Count == 0 && actual.Count == 0 &&
                    leaderTarget.OriginalTargetSeats is [var implicitSelf] && implicitSelf == actor))
            {
                IReadOnlyList<int> originalTargets = designated;
                if (kind == CardKind.DrawTwo && designated.Count == 0 && actual.Count == 0 &&
                    leaderTarget.OriginalTargetSeats is [var self] && self == actor)
                    originalTargets = [actor];
                if (!actual.SequenceEqual(designated) || !IsUniqueLeaderTrickTargetFact(use, leaderTarget, originalTargets)) return false;
                designated = leaderTarget.ResultTargetSeats.ToList(); actual = leaderTarget.ResultTargetSeats.ToList();
            }
            else if (fact is RoundGainedTrickTargetResolvedEvent roundTarget && roundTarget.Qualification.CardUseFrameId == use.Id)
            {
                IReadOnlyList<int> originalTargets = designated;
                if (kind == CardKind.DrawTwo && designated.Count == 0 && actual.Count == 0 &&
                    roundTarget.OriginalTargetSeats is [var roundSelf] && roundSelf == actor)
                    originalTargets = [actor];
                if (!actual.SequenceEqual(designated) || !IsRoundGainedTrickTargetFact(use, roundTarget, originalTargets)) return false;
                designated = roundTarget.ResultTargetSeats.ToList(); actual = roundTarget.ResultTargetSeats.ToList();
            }
            else if (fact is RecipientCategorySlashTargetsResolvedEvent categoryTargets && categoryTargets.CardUseFrameId == use.Id)
            {
                if (!actual.SequenceEqual(designated) || !IsRecipientCategorySlashTargetFact(use, categoryTargets, designated)) return false;
                designated = categoryTargets.ResultTargets.ToList(); actual = categoryTargets.ResultTargets.ToList();
            }
            else if (fact is CurrentCardEnhancedEvent { Enhancement: CurrentCardEnhancement.ExtraTarget, ExtraTargetSeat: { } target } enhanced &&
                enhanced.CardUseFrameId == use.Id && enhanced.CardActionId == completed.ActionId)
            {
                if (actual.Contains(target) || (use.Enhancements & CurrentCardEnhancement.ExtraTarget) == 0) return false;
                designated.Add(target); actual.Add(target);
            }
            else if (fact is OriginalTargetAdditionResolvedEvent { Added: true } original && original.CardUseFrameId == use.Id &&
                original.ActionId == completed.ActionId)
            {
                if (actual.Contains(original.TargetSeat) || original.ActorSeat != actor || use.OriginalTargetAddition is not { Added: true } receipt ||
                    !receipt.Grants.Any(g => g.Source.OwnerSeat == actor && g.TargetSeat == original.TargetSeat &&
                        history.OfType<OriginalTargetAdditionIssuedEvent>().Count(e => e.CardUseFrameId == use.Id &&
                            e.ActionId == completed.ActionId && e.Grant == g) == 1)) return false;
                if (kind == CardKind.DrawTwo && designated.Count == 0) { designated.Add(actor); actual.Add(actor); }
                designated.Add(original.TargetSeat); actual.Add(original.TargetSeat);
            }
            else if (fact is ProgramCurrentSlashFireChangedEvent changedKind && changedKind.CardUseFrameId == use.Id)
            {
                if (use.CurrentSlashFirePolicy is not { } policy || changedKind.ActionId != completed.ActionId ||
                    changedKind.ProgramFrameId != policy.ProgramFrameId || changedKind.Source != policy.Source ||
                    changedKind.OriginalKind != kind || changedKind.OriginalKind != policy.OriginalKind ||
                    changedKind.FinalKind != CardKind.FireSlash || changedKind.ExtraTargetSeat != policy.ExtraTargetSeat ||
                    policy.OriginalAction.ActionId != completed.ActionId || policy.OriginalAction.ActorSeat != actor ||
                    policy.OriginalAction.ProviderSeat != completed.ProviderSeat || !designated.SequenceEqual(policy.OriginalAction.TargetSeats) ||
                    !chain.SequenceEqual(policy.OriginalAction.ConversionChain) ||
                    history.OfType<ProgramCurrentSlashFireChangedEvent>().Count(e => e.CardUseFrameId == use.Id) != 1 ||
                    history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == policy.ProgramFrameId &&
                        e.SkillId == policy.Source.SkillId && e.OwnerSeat == policy.Source.OwnerSeat && e.BindingId == policy.Source.BindingId &&
                        e.SkillInstanceId == policy.Source.SkillInstanceId && e.Window == SkillProgramTriggerWindow.CardUseCommitted) != 1 ||
                    !ActualHandGainMutationBinding(policy.ProgramFrameId, policy.Source.SkillId, policy.Source.OwnerSeat,
                        SkillProgramTriggerWindow.CardUseCommitted, SkillProgramEffectOp.OfferCurrentSlashFireAndExtraTarget)) return false;
                if (policy.Converted) chain.Add(policy.Source);
                kind = CardKind.FireSlash;
                if (policy.ExtraTargetSeat is { } extra)
                { if (actual.Contains(extra)) return false; designated.Add(extra); actual.Add(extra); }
            }
            else if (fact is SlashTargetsReplacedEvent replaced && replaced.CardUseFrameId == use.Id)
            {
                if (!actual.SequenceEqual(replaced.PreviousTargets) || !ActualHandGainMutationBinding(replaced.FrameId, replaced.SkillId,
                        replaced.OwnerSeat, SkillProgramTriggerWindow.CardUseCommitted, SkillProgramEffectOp.ReplaceAllSlashTargets)) return false;
                designated = replaced.Targets.ToList(); actual = replaced.Targets.ToList();
            }
            else if (fact is ProgramActualSlashTargetRedirectedEvent redirected && redirected.CardUseFrameId == use.Id)
            {
                if (!IsShortRangeSlashRedirectFact(use, redirected, actual)) return false;
                if (history.OfType<DesignatedExtraTargetRedirectedEvent>().Any(e =>
                        e.ProgramFrameId == redirected.ProgramFrameId && e.CardUseFrameId == redirected.CardUseFrameId &&
                        e.ActionId == redirected.ActionId && e.Source == redirected.Source && e.GameplayHash == redirected.GameplayHash &&
                        e.InstructionIndex == redirected.InstructionIndex && e.TargetIndex == redirected.TargetIndex &&
                        e.OriginalTargetSeat == redirected.OriginalTargetSeat && e.NewTargetSeat == redirected.NewTargetSeat)) continue;
                actual[redirected.TargetIndex] = redirected.NewTargetSeat;
            }
            else if (fact is ProgramCurrentSlashFireRedirectedEvent fire && fire.CardUseFrameId == use.Id)
            {
                if (use.CurrentSlashFirePolicy is null || fire.ActionId != completed.ActionId || fire.TargetIndex < 0 ||
                    fire.TargetIndex >= actual.Count || actual[fire.TargetIndex] != fire.OriginalTargetSeat ||
                    fire.Source.OwnerSeat != fire.OriginalTargetSeat || !ActualHandGainMutationBinding(fire.ProgramFrameId, fire.Source.SkillId,
                        fire.Source.OwnerSeat, SkillProgramTriggerWindow.SlashTargetRedirecting, SkillProgramEffectOp.RedirectCurrentAttack)) return false;
                if (history.OfType<DesignatedExtraTargetRedirectedEvent>().Any(e =>
                        e.ProgramFrameId == fire.ProgramFrameId && e.CardUseFrameId == fire.CardUseFrameId && e.ActionId == fire.ActionId &&
                        e.Source == fire.Source && e.TargetIndex == fire.TargetIndex && e.OriginalTargetSeat == fire.OriginalTargetSeat &&
                        e.NewTargetSeat == fire.NewTargetSeat)) continue;
                actual[fire.TargetIndex] = fire.NewTargetSeat;
            }
            else if (fact is DesignatedExtraTargetRedirectedEvent redirectedExtra && redirectedExtra.CardUseFrameId == use.Id)
            {
                if (!IsDesignatedExtraTargetRedirectFact(use, redirectedExtra, actual)) return false;
                actual[redirectedExtra.TargetIndex] = redirectedExtra.NewTargetSeat;
            }
            else if (fact is CardActionAcceptedEvent finalized && finalized.Action.ActionId == completed.ActionId)
            {
                if (finalized.Action.ActorSeat != actor || finalized.Action.ProviderSeat != completed.ProviderSeat ||
                    finalized.Action.EffectiveKind != kind || !finalized.Action.ConversionChain.SequenceEqual(chain) ||
                    !finalized.Action.PhysicalCards.SequenceEqual(completed.PhysicalCards) || !actual.SequenceEqual(finalized.Action.TargetSeats)) return false;
                designated = actual.ToList();
            }
        }
        return actor == completed.ActorSeat && kind == completed.EffectiveKind && chain.SequenceEqual(completed.ConversionChain) &&
            designated.SequenceEqual(completed.TargetSeats) && actual.SequenceEqual(use.TargetSeats);
    }
}
