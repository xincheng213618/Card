namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void AddCurrentNonFireConvertedTargetActions(ICollection<LegalAction> actions, CharacterState actor)
    {
        if (!HasCommittedSlashFireCapability(actor)) return;
        foreach (var card in GetHand(actor).Concat(GetEquipment(actor)).Where(c => c.Kind == CardKind.ThunderSlash))
        foreach (var source in GetProgramViewAsConversions(actor, card, CardKind.FireSlash, false))
            AddProgramTargetCountSlashActions(actions, actor, card,
                GetFangtianOrderedSlashTargets(actor, card, source, effectiveKind: CardKind.FireSlash),
                "火杀", CardKind.FireSlash, source);
    }

    private bool HasCommittedSlashFireCapability(CharacterState owner) => owner.IsAlive &&
        GetSkillBindingShard(owner).ProgramInstances.Any(i => i.Program.Triggers.Any(t =>
            t.Window == SkillProgramTriggerWindow.CardUseCommitted && t.Effects.Any(e => e.Op == SkillProgramEffectOp.OfferCurrentSlashFireAndExtraTarget)));

    private CardUseFrame CurrentSlashFireUse(ProgramSkillFrame f)
    {
        if (f.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseCommitted, CardUse: { } context } ||
            context.ActorSeat != f.OwnerSeat || LifecycleCardUse(context.ParentCardUseFrameId) is not { Action: { } action } use ||
            action.ActionId != context.CardActionId || action.Type != CardActionType.Use || use.SourceSeat != f.OwnerSeat ||
            !SlashKinds.Contains(use.CardKind) || use.TargetIndex < 0 || use.TargetIndex >= use.TargetSeats.Count ||
            use.CardAttack is not { } attack || attack.TargetSeat != use.TargetSeats[use.TargetIndex] || attack.EffectiveCardKind != use.CardKind)
            throw new InvalidOperationException("A current Slash conversion lost its exact committed action and attack.");
        return use;
    }

    private bool CanOfferCurrentSlashFire(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.OfferCurrentSlashFireAndExtraTarget)) return true;
        if (context.CardUse is not { } card || card.ActorSeat != candidate.OwnerSeat ||
            LifecycleCardUse(card.ParentCardUseFrameId) is not { Action: { } action } use || action.ActionId != card.CardActionId ||
            !SlashKinds.Contains(use.CardKind) || use.CurrentSlashFirePolicy is not null || use.CardAttack is null || use.TargetSeats.Count == 0) return false;
        return CurrentFireSlashOriginalTargetsAreLegal(use) && (use.CardKind != CardKind.FireSlash || CanAddCurrentFireSlashTarget(use));
    }

    private bool CurrentFireSlashOriginalTargetsAreLegal(CardUseFrame use) =>
        !IsCardUseForbidden(use.SourceSeat, CardKind.FireSlash, CardActionType.Use) && use.TargetSeats.All(seat =>
            IsValidPlayerSeat(seat) && _players[seat].IsAlive && !IsSlashProhibited(_players[seat]) &&
            !HasBeneficiarySuitShield(use.SourceSeat, seat, use.Action!.EffectiveSuit) &&
            !IsDirectedCardTargetProhibited(use.SourceSeat, seat, CardKind.FireSlash) &&
            !IsCardTargetProhibited(_players[seat], CardKind.FireSlash, use.Action!.EffectiveSuit ?? Suit.None, ActualTargetPolicyColor(use.Action!)));

    private int CurrentFireSlashMaximum(CardUseFrame use)
    {
        var actor = _players[use.SourceSeat];
        var baseMaximum = UsesFormalFangtianHalberd && use.Action!.PhysicalCards.Count == 1 &&
            GetHand(actor).Count == 0 && HasWeaponAbility(actor, CardKind.FangtianHalberd) ? 3 : 1;
        return baseMaximum + ((FiniteRuleQueryValue)EvaluateCardTargetCount(actor, CardKind.FireSlash).Value).Value - 1;
    }

    private bool CanAddCurrentFireSlashTarget(CardUseFrame use) =>
        use.TargetSeats.Count < CurrentFireSlashMaximum(use) && CurrentFireSlashExtraTargets(use).Length != 0;

    private int[] CurrentFireSlashExtraTargets(CardUseFrame use)
    {
        var actor = _players[use.SourceSeat]; var action = use.Action!;
        // A granted virtual use can carry an explicit distance exemption on its exact producer.
        var ignoreDistance = HasIssuedKuangfuDistance(use.Id, actor.Seat) || use.UnlimitedUse || use.CardAttack?.ProgramSkillCardUseFrameId is { } parentId &&
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == parentId) is { } parent &&
            ProgramInstructionResolver.Default.Resolve(parent, _contentRegistry.GetSkill(parent.SkillId).Program!)
                .GetPausedInstruction(parent.InstructionIndex).Effect is { Op: SkillProgramEffectOp.UseVirtualCard, TargetRestriction: SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget };
        return _players.Where(t => t.IsAlive && t.Seat != actor.Seat && !use.TargetSeats.Contains(t.Seat) &&
            !HasBeneficiarySuitShield(actor.Seat, t.Seat, action.EffectiveSuit) &&
            !IsSlashProhibited(t) && !IsDirectedCardTargetProhibited(actor.Seat, t.Seat, CardKind.FireSlash) &&
            !IsCardTargetProhibited(t, CardKind.FireSlash, action.EffectiveSuit ?? Suit.None, ActualTargetPolicyColor(action)) &&
            (ignoreDistance || HasIssuedProvenanceUseDistance(use.Id, actor.Seat) || HasIssuedGrantedPhaseEntityDistance(use.Id, actor.Seat) || HasProvenanceUseDistance(actor, use.PhysicalCardIds) || HasGrantedPhaseEntityDistance(actor, use.PhysicalCardIds) ||
             HasSlashUseDistanceBySuit(actor, CardKind.FireSlash, action.EffectiveSuit) ||
             HasTurnRedSlashPolicyForColor(actor.Seat, CardKind.FireSlash, ActualTargetPolicyColor(action)) ||
             HasPhaseSuitAllowance(actor.Seat, action.EffectiveSuit) || HasCardDistanceExemption(actor, t, CardKind.FireSlash) ||
             IgnoresSlashUseDistance(actor) ||
             IsWithinSpecificSlashRange(actor, t, CardKind.FireSlash, action.EffectiveRank, use.Id)))
            .Select(t => t.Seat).ToArray();
    }

    private SkillProgramStepOutcome BeginCurrentSlashFireOffer(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id); var use = CurrentSlashFireUse(f);
        if (f.CurrentSlashFireDraft is not null || use.CurrentSlashFirePolicy is not null)
            throw new InvalidOperationException("A committed Slash fire offer was issued twice.");
        ReplaceRuntimeTop(f = f with { CurrentSlashFireDraft = new(use.Id) });
        PublishCurrentSlashFirePrompt(f); return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> CurrentSlashFireChoices(ProgramSkillFrame f)
    {
        var use = CurrentSlashFireUse(f);
        Dictionary<string, string> Parameters(string branch) => new()
        { ["program-action"] = "current-slash-fire", ["branch"] = branch, ["frame-id"] = f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        var choices = new List<PromptChoice>();
        if (use.CardKind != CardKind.FireSlash && CurrentFireSlashOriginalTargetsAreLegal(use))
            choices.Add(new(new ChoiceId($"slash-fire.{f.Id}.convert"), "改为火杀", [], [], Parameters("convert")));
        if (CurrentFireSlashOriginalTargetsAreLegal(use) && use.TargetSeats.Count < CurrentFireSlashMaximum(use))
            choices.AddRange(CurrentFireSlashExtraTargets(use).Select(t => new PromptChoice(new ChoiceId($"slash-fire.{f.Id}.extra.{t}"),
                use.CardKind == CardKind.FireSlash ? $"额外指定 {_players[t].Name}" : $"改为火杀并额外指定 {_players[t].Name}", [], [t], Parameters("extra"))));
        choices.Add(new(new ChoiceId($"slash-fire.{f.Id}.skip"), "保持当前杀及目标", [], [], Parameters("skip")));
        return choices;
    }

    private void PublishCurrentSlashFirePrompt(ProgramSkillFrame f)
    {
        var choices = CurrentSlashFireChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "选择改为火杀及至多一个额外合法目标。", [],
            choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = false, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveCurrentSlashFireChoice(PromptChoice selected)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Slash fire producer disappeared.");
        AssertCurrentSlashFireDraft(f);
        selected = CurrentSlashFireChoices(f).SingleOrDefault(c => c.Id == selected.Id) ?? throw new InvalidOperationException("The extra Slash target became illegal.");
        var use = CurrentSlashFireUse(f); var branch = selected.Parameters["branch"]; var action = use.Action!;
        ClearPendingDecision();
        if (branch != "skip" && _winner == Winner.None && _players[f.OwnerSeat].IsAlive && HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
        {
            var source = new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
            var converted = use.CardKind != CardKind.FireSlash;
            var targets = use.TargetSeats.Concat(selected.Targets).ToArray();
            var chain = converted ? action.ConversionChain.Append(source).ToArray() : action.ConversionChain;
            var updated = CaptureFactionAction(new CardActionContext(action.ActionId, action.ParentActionId, action.Type, action.ActorSeat,
                action.ProviderSeat, action.RequesterSeat, action.ResponderSeat, action.OpponentSeat, CardKind.FireSlash, targets,
                action.PhysicalCards, chain, targets, action.EffectiveSuit, action.EffectiveRank, action.EffectiveIsRed, action.FactionOrigin));
            UpdateLifecycleCardUse(use.Id, current => current with
            {
                CardKind = CardKind.FireSlash, TargetSeats = Array.AsReadOnly(targets), Action = updated,
                CurrentSlashFirePolicy = new(f.Id, source, action, use.CardKind, selected.Targets.Count == 0 ? null : selected.Targets[0], converted),
                CardAttack = current.CardAttack! with { EffectiveCardKind = CardKind.FireSlash, DamageNatureOverride = DamageNature.Fire,
                    IgnoresArmor = current.CardAttack.IgnoresArmor || HasCardArmorBypass(_players[current.SourceSeat], _players[current.CardAttack.TargetSeat], CardKind.FireSlash) },
                PreparedTargetAttacks = current.PreparedTargetAttacks is null ? null : Array.AsReadOnly(current.PreparedTargetAttacks.Select(a => a with
                    { EffectiveCardKind = CardKind.FireSlash, DamageNatureOverride = DamageNature.Fire,
                      IgnoresArmor = a.IgnoresArmor || HasCardArmorBypass(_players[current.SourceSeat], _players[a.TargetSeat], CardKind.FireSlash) }).ToArray()),
                Continuations = current.Continuations with { FangtianHalberd = current.Continuations.FangtianHalberd is not { } multi ? null : multi with
                    { EffectiveCardKind = CardKind.FireSlash, TargetSeats = Array.AsReadOnly(targets) } }
            });
            AdvanceEventRulesAndQueueFact(new ProgramCurrentSlashFireChangedEvent(f.Id, use.Id, action.ActionId, source,
                use.CardKind, CardKind.FireSlash, selected.Targets.Count == 0 ? null : selected.Targets[0]));
            // Accepted/declared history retains its original action. This new fact records the typed effective-kind change.
        }
        ReplaceRuntimeTop(f with { CurrentSlashFireDraft = null }); AdvanceRuntimeProgram(f.Id);
    }

    private void AssertCurrentSlashFireDraft(ProgramSkillFrame f)
    {
        if (f.CurrentSlashFireDraft is not { } draft) return;
        var use = CurrentSlashFireUse(f);
        var e = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
        if (e.Op != SkillProgramEffectOp.OfferCurrentSlashFireAndExtraTarget || draft.CardUseFrameId != use.Id ||
            use.CurrentSlashFirePolicy is not null || f.ParentFrameId is not { } parentId ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == parentId) is not
                { Continuation: ProgramCardContinuation.CommittedSlash } window || window.ParentFrameId != use.Id || window.Action.ActionId != use.Action!.ActionId)
            throw new InvalidOperationException("A fire conversion draft lost its exact committed Slash producer.");
    }

    private (int[] Designated, int[] Actual, int[] Planned) RebuildCurrentSlashFireTargets(CardUseFrame use)
    {
        var p = use.CurrentSlashFirePolicy!;
        var confirmed = CompleteProgramEventHistory().OfType<TargetsConfirmedEvent>().Single(e => e.ResolutionId == use.Id);
        var designated = confirmed.TargetSeats.ToList();
        var actual = designated.ToList(); var planned = designated.ToList(); var after = false;
        foreach (var e in CompleteProgramEventHistory())
        {
            if (e is ProgramCurrentSlashFireChangedEvent changed && changed.CardUseFrameId == use.Id && changed.ProgramFrameId == p.ProgramFrameId)
            {
                if (after || !designated.SequenceEqual(p.OriginalAction.TargetSeats))
                    throw new InvalidOperationException("A fire conversion lost its exact earlier target enhancements.");
                after = true;
                if (p.ExtraTargetSeat is { } extra) { designated.Add(extra); actual.Add(extra); planned.Add(extra); }
                continue;
            }
            if (e is ShortRangeSlashTargetResolvedEvent { Added: true } shortRange && shortRange.CardUseFrameId == use.Id)
            {
                if (!IsShortRangeSlashTargetFact(use, shortRange, designated) ||
                    !shortRange.Receipt!.OriginalTargets.SequenceEqual(actual))
                    throw new InvalidOperationException("A short-range fire tail lost its complete native finalized prefix.");
                designated.Add(shortRange.Receipt.AddedTargetSeat); actual.Add(shortRange.Receipt.AddedTargetSeat);
                // Finalized-tail issuance does not enlarge or rewrite the already
                // prepared original Fangtian plan. Its native cursor handles the
                // extra target after that original group completes.
                continue;
            }
            int? added = e switch
            {
                CurrentCardEnhancedEvent { Enhancement: CurrentCardEnhancement.ExtraTarget } enhancement when enhancement.CardUseFrameId == use.Id && enhancement.CardActionId == p.OriginalAction.ActionId => enhancement.ExtraTargetSeat,
                OriginalTargetAdditionResolvedEvent addition when addition.CardUseFrameId == use.Id && addition.ActionId == p.OriginalAction.ActionId && addition.Added => addition.TargetSeat,
                SameTypeAidTargetAddedEvent aid when IsSameTypeAidTargetFact(use, aid) => aid.RecipientSeat,
                _ => null
            };
            if (added is { } target) { designated.Add(target); actual.Add(target); planned.Add(target); }
            if (e is SlashTargetsReplacedEvent replaced && replaced.CardUseFrameId == use.Id)
            {
                if (!replaced.PreviousTargets.SequenceEqual(actual))
                    throw new InvalidOperationException("A converted Slash target replacement lost its exact preceding target chain.");
                designated = replaced.Targets.ToList(); actual = designated.ToList(); planned = designated.ToList();
            }
            if (e is ProgramCurrentSlashFireRedirectedEvent redirected && redirected.CardUseFrameId == use.Id)
            {
                if (!after || redirected.ActionId != p.OriginalAction.ActionId || redirected.Source.OwnerSeat != redirected.OriginalTargetSeat ||
                    redirected.TargetIndex < 0 || redirected.TargetIndex >= actual.Count || actual[redirected.TargetIndex] != redirected.OriginalTargetSeat)
                    throw new InvalidOperationException("A converted Slash redirection lost its exact original target.");
                actual[redirected.TargetIndex] = redirected.NewTargetSeat;
            }
            if (after && e is CardActionAcceptedEvent accepted && accepted.Action.ActionId == p.OriginalAction.ActionId)
            {
                if (!accepted.Action.TargetSeats.SequenceEqual(actual))
                    throw new InvalidOperationException("A finalized converted Slash lost its ordered actual targets.");
                designated = actual.ToList();
            }
        }
        if (!after) throw new InvalidOperationException("A changed Slash lost its effective-kind change fact.");
        return (designated.ToArray(), actual.ToArray(), planned.ToArray());
    }

    private void RecordCurrentSlashFireRedirect(ProgramSkillFrame redirector, long useId, int originalSeat, int newSeat)
    {
        if (LifecycleCardUse(useId) is not { CurrentSlashFirePolicy: not null, Action: { } action } use) return;
        if (redirector.WindowContext is not { Window: SkillProgramTriggerWindow.SlashTargetRedirecting, CardUse: { } context } ||
            context.ParentCardUseFrameId != useId || context.CardActionId != action.ActionId || redirector.OwnerSeat != originalSeat ||
            use.TargetIndex < 0 || use.TargetIndex >= use.TargetSeats.Count || use.TargetSeats[use.TargetIndex] != newSeat)
            throw new InvalidOperationException("A changed Slash redirect lost its exact owning program.");
        AdvanceEventRulesAndQueueFact(new ProgramCurrentSlashFireRedirectedEvent(redirector.Id, useId, action.ActionId,
            new(redirector.SkillId, GetProgramBindingId(redirector), redirector.OwnerSeat, redirector.SkillInstanceId), use.TargetIndex, originalSeat, newSeat));
    }

    private bool TryGetCurrentSlashFirePrimaryReturn(CardUseFrame use, ProgramSkillFrame parent, out IReadOnlyList<int> primary)
    {
        primary = [];
        if (use.CurrentSlashFirePolicy is not { OriginalKind: CardKind.Slash } policy || use.CardId != 0 ||
            use.PhysicalCardIds is not { Count: 0 } || policy.OriginalAction.PhysicalCards.Count != 0 ||
            CompleteProgramEventHistory().OfType<TargetsConfirmedEvent>().SingleOrDefault(e => e.ResolutionId == use.Id)?.TargetSeats is not [var target] ||
            use.CardAttack?.ProgramSkillCardUseFrameId != parent.Id ||
            use.SourceSeat != policy.OriginalAction.ActorSeat || parent.InstructionIndex < 1) return false;
        AssertCurrentSlashFirePolicy(use);
        var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1].Id != parent.Id) return false;
        var definition = _contentRegistry.GetSkill(parent.SkillId).Program!;
        var effect = ProgramInstructionResolver.Default.Resolve(parent, definition).GetPausedInstruction(parent.InstructionIndex).Effect;
        var action = policy.OriginalAction;
        var exact = effect.Op switch
        {
            SkillProgramEffectOp.UseVirtualCard or SkillProgramEffectOp.OfferUnlimitedVirtualSlash => parent.OwnerSeat == use.SourceSeat &&
                (parent.SelectedTargetSeats.SequenceEqual([target]) || use.TargetSeats.Count > 0 && parent.SelectedTargetSeats.SequenceEqual([use.TargetSeats[0]])) && effect.OutputKind == CardKind.Slash &&
                effect.TargetRestriction == SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget && action.ConversionChain.Count == 0,
            SkillProgramEffectOp.OfferVirtualSlashOrDraw => parent.SelectedTargetSeats.SequenceEqual([use.SourceSeat]) && action.ConversionChain.Count == 0,
            SkillProgramEffectOp.UseVirtualSlash => parent.OwnerSeat == use.SourceSeat && action.ConversionChain.SequenceEqual([
                new CardConversionSource(parent.SkillId, GetProgramBindingId(parent), parent.OwnerSeat, parent.SkillInstanceId)]),
            _ => false
        };
        if (!exact) return false;
        if (use.ShortRangeSlashTarget is not null)
        {
            // This owning tail belongs to the native use. A redirect must not
            // rewrite the paused producer's original singleton selection.
            // The tail may have been issued after fire already added a target.
            var originalSelection = effect.Op switch
            {
                SkillProgramEffectOp.UseVirtualCard or SkillProgramEffectOp.OfferUnlimitedVirtualSlash =>
                    parent.SelectedTargetSeats.SequenceEqual([target]),
                SkillProgramEffectOp.OfferVirtualSlashOrDraw => parent.SelectedTargetSeats.SequenceEqual([use.SourceSeat]),
                SkillProgramEffectOp.UseVirtualSlash => effect.TargetReference is null
                    ? parent.SelectedTargetSeats.SequenceEqual([target])
                    : effect.TargetReference.Kind == ProgramParticipantRef.EventTarget && parent.WindowContext?.TargetSeat == target,
                _ => false
            };
            if (!originalSelection || definition.GameplayHash != parent.GameplayHash || !HasShortRangeSlashTail(use)) return false;
            primary = Array.AsReadOnly(new[] { target }); return true;
        }
        primary = Array.AsReadOnly(new[] { use.TargetSeats.Count == 0 ? target : use.TargetSeats[0] }); return true;
    }

    private bool IsCurrentSlashFireChangedUse(CardUseFrame use) => use.CurrentSlashFirePolicy is { Converted: true } && use.CardKind == CardKind.FireSlash;

    private void AssertCurrentSlashFirePolicy(CardUseFrame use)
    {
        if (use.CurrentSlashFirePolicy is not { } p) return;
        var old = p.OriginalAction; var current = use.Action; var targets = RebuildCurrentSlashFireTargets(use);
        if (current is null || p.Source.OwnerSeat != use.SourceSeat || old.ActionId != current.ActionId ||
            old.ActorSeat != current.ActorSeat || old.ProviderSeat != current.ProviderSeat || old.Type != current.Type ||
            old.EffectiveKind != p.OriginalKind || !SlashKinds.Contains(p.OriginalKind) || use.CardKind != CardKind.FireSlash ||
            current.EffectiveKind != CardKind.FireSlash || !old.PhysicalCards.SequenceEqual(current.PhysicalCards) ||
            p.Converted != (p.OriginalKind != CardKind.FireSlash) ||
            !current.ConversionChain.SequenceEqual(p.Converted ? old.ConversionChain.Append(p.Source) : old.ConversionChain) ||
            p.ExtraTargetSeat is { } extra && (old.TargetSeats.Contains(extra) || !IsValidPlayerSeat(extra)) ||
            !current.TargetSeats.SequenceEqual(targets.Designated) || !use.TargetSeats.SequenceEqual(targets.Actual) ||
            (use.TargetSeats.Count == 0 ? !use.SlashTargetsCancelled || use.TargetIndex != 0 : use.TargetIndex < 0 || use.TargetIndex >= use.TargetSeats.Count) ||
            use.CardAttack is not { EffectiveCardKind: CardKind.FireSlash } attack || use.TargetSeats.Count > 0 && attack.TargetSeat != use.TargetSeats[use.TargetIndex] ||
            attack.DamageNatureOverride is not (null or DamageNature.Fire) ||
            use.PreparedTargetAttacks?.Any(a => a.EffectiveCardKind != CardKind.FireSlash || a.DamageNatureOverride is not (null or DamageNature.Fire)) == true ||
            use.Continuations.FangtianHalberd is { } multi && (multi.EffectiveCardKind != CardKind.FireSlash || !multi.TargetSeats.SequenceEqual(targets.Planned)) ||
            !CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().Any(e => e.ResolutionId == use.Id && e.SourceSeat == use.SourceSeat && e.CardKind == p.OriginalKind) ||
            !CompleteProgramEventHistory().OfType<ProgramCurrentSlashFireChangedEvent>().Any(e => e.ProgramFrameId == p.ProgramFrameId &&
                e.CardUseFrameId == use.Id && e.ActionId == old.ActionId && e.Source == p.Source && e.OriginalKind == p.OriginalKind &&
                e.FinalKind == CardKind.FireSlash && e.ExtraTargetSeat == p.ExtraTargetSeat))
            throw new InvalidOperationException("A changed Slash lost its original frozen action, exact effective change or extra-target proof.");
    }
}
