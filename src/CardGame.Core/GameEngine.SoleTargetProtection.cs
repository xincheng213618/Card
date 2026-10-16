using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string SoleProtectionCostReason = "skill-program.sole-target-protection.cost";
    private const string SoleProtectionQuarantineReason = "skill-program.sole-target-protection.quarantine";
    private bool HasSoleTargetProtectionCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ProtectOtherSoleCardTarget);
    private bool HasSoleTargetProtectionParticipant => HasSoleTargetProtectionCapability &&
        _players.Any(owner => owner.IsAlive && GetSkillBindingShard(owner).ProgramInstances.Any(instance =>
            instance.Program.Triggers.Any(trigger => trigger.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized &&
                trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.ProtectOtherSoleCardTarget))));

    private int[] SoleProtectionMainTargets(CardUseFrame use) =>
        (use.CardKind == CardKind.DrawTwo && use.TargetSeats.Count == 0 ? new[] { use.SourceSeat } :
            use.CardKind == CardKind.BorrowedSword ? use.TargetSeats.Where((_, i) => i % 2 == 0) : use.TargetSeats)
        .Where(s => IsValidPlayerSeat(s) && _players[s].IsAlive).Distinct().ToArray();

    private bool TryGetSoleProtectionUse(int owner, ProgramSkillWindowContext context,
        out CardUseFrame use, out ProgramCardTriggerWindowFrame window)
    {
        use = null!; window = null!;
        if (context is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } identity } ||
            context.OwnerSeat != owner || _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } found ||
            found.ParentFrameId != identity.ParentCardUseFrameId || GetCardActionWindow(found) != context.Window ||
            found.Action.ActionId != identity.CardActionId || found.Action.Type != CardActionType.Use ||
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Id == found.ParentFrameId) is not { Action: { } action } native ||
            native.SourceSeat != action.ActorSeat || found.Action.ActionId != action.ActionId || action.EffectiveKind != native.CardKind ||
            action.ActorSeat != identity.ActorSeat && !IsSoleProtectionReplacedActor(native, identity.ActorSeat) || action.ProviderSeat != found.Action.ProviderSeat ||
            !action.TargetSeats.SequenceEqual(native.TargetSeats) || native.Step == ResolutionFrameStep.Completed || native.SoleTargetQuarantine is not null ||
            found.Continuation is not (ProgramCardContinuation.Slash or ProgramCardContinuation.FinalizedTrick or ProgramCardContinuation.FinalizedSimpleCard or ProgramCardContinuation.FinalizedDelayedPlacement) ||
            !IsSlashCard(native.CardKind) && (CardCatalog.Get(native.CardKind).CategoryName != "锦囊牌" ||
                !(action.EffectiveIsRed == false || action.EffectiveSuit is Suit.Spade or Suit.Club)) ||
            _turnNumber <= 0 || _turnProgression.TurnNumber != _turnNumber || _turnProgression.OwnerSeat != _currentSeat)
            return false;
        var index = _resolutionStack.FindIndex(f => f.Id == found.Id);
        if (index < 1 || _resolutionStack[index - 1].Id != native.Id) return false;
        var materials = (native.PhysicalCardIds ?? (native.CardId == 0 ? [] : new[] { native.CardId })).ToArray();
        if (materials.Distinct().Count() != materials.Length || materials.Any(id => id <= 0 ||
                !IsCurrentUsePhysicalCardClaim(native.Id, id) && !IsExchangedCardClaim(action.ActionId, id) &&
                (IsProgramAlternativeCost(action, id) ? _cardZones.GetLocation(id) == CardLocation.Processing :
                    _cardZones.GetLocation(id) != CardLocation.Processing)) ||
            !CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().Any(e => e.ResolutionId == native.Id && e.CardId == native.CardId &&
                (e.CardKind == native.CardKind || native.CurrentSlashFirePolicy is { Converted: true } policy && policy.OriginalKind == e.CardKind &&
                    IsCurrentSlashFireChangedUse(native) && HasExactAcceptedActualHandGainUse(native, action))))
            return false;
        use = native; window = found; return true;
    }

    private bool IsSoleProtectionReplacedActor(CardUseFrame use, int capturedActor) => use.Action is { } action &&
        CompleteProgramEventHistory().OfType<ProgramCardUseActorReplacedEvent>().Any(e => e.CardUseFrameId == use.Id &&
            e.PreviousActorSeat == capturedActor && e.ProviderSeat == action.ProviderSeat &&
            ActualHandGainMutationBinding(e.FrameId, e.SkillId, e.OwnerSeat, SkillProgramTriggerWindow.CardUseTargetsFinalized,
                SkillProgramEffectOp.ReplaceCurrentCardUseActor)) && HasExactAcceptedActualHandGainUse(use, action);

    private Card[] SoleProtectionCosts(int owner, string skillId, string instanceId) =>
        GetHand(_players[owner]).Concat(GetEquipment(_players[owner])).Where(c =>
            !IsSelfHandCategoryDiscardForbidden(owner, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard) &&
            !IsForeignEquipmentDiscardPrevented(owner, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard)).ToArray();

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 250)]
    private bool CanOfferSoleTargetProtection(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ProtectOtherSoleCardTarget)) return true;
        if (!TryGetSoleProtectionUse(candidate.OwnerSeat, context, out var use, out var window) ||
            !DesignatedExtraTargetCandidateMatches(candidate, context, window) || use.SourceSeat == candidate.OwnerSeat ||
            SoleProtectionMainTargets(use) is not [var protectedSeat] || protectedSeat == candidate.OwnerSeat ||
            _players[protectedSeat].Hp >= _players[candidate.OwnerSeat].Hp ||
            use.SoleTargetProtectionSources?.Contains(new(candidate.SkillId, candidate.BindingId, candidate.OwnerSeat, candidate.SkillInstanceId)) == true)
            return false;
        return SoleProtectionCosts(candidate.OwnerSeat, candidate.SkillId, candidate.SkillInstanceId).Length > 0;
    }

    private SkillProgramStepOutcome BeginSoleTargetProtection(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.SoleTargetProtection is not null || f.TriggerId is null || f.WindowContext is not { } context ||
            !TryGetSoleProtectionUse(f.OwnerSeat, context, out var use, out var window) ||
            !CanOfferSoleTargetProtection(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0), GetProgramTrigger(f), context) ||
            !DesignatedExtraTargetCandidateMatches(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0), context, window))
            throw new InvalidOperationException("Sole-target protection requires its exact eligible current observer and actual use.");
        var protectedSeat = SoleProtectionMainTargets(use).Single(); var action = use.Action!;
        var materials = (use.PhysicalCardIds ?? (use.CardId == 0 ? [] : new[] { use.CardId }))
            .Where(id => _cardZones.GetLocation(id) == CardLocation.Processing).Select(id => new SoleTargetProtectionMaterial(id, GetAttackCard(id).Kind,
                action.PhysicalCards.Single(c => c.CardId == id).From)).ToArray();
        var receipt = new SoleTargetProtectionReceipt(new(f.SkillId, f.TriggerId, f.OwnerSeat, f.SkillInstanceId), f.GameplayHash,
            f.Id, f.InstructionIndex, window.Id, use.Id, action.ActionId, _turnNumber, _currentSeat, use.SourceSeat,
            action.ProviderSeat, use.CardKind, protectedSeat, _players[f.OwnerSeat].Hp, _players[protectedSeat].Hp,
            Array.AsReadOnly(use.TargetSeats.ToArray()), Array.AsReadOnly(materials), SoleTargetProtectionStage.ChoosingCost);
        ReplaceRuntimeTop(f = f with { SoleTargetProtection = receipt }); PublishSoleProtectionChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }

    private PromptChoice SoleProtectionChoice(ProgramSkillFrame f, string branch, string label, int? cardId = null) =>
        new(new($"sole-protection.{f.Id}.{branch}.{cardId ?? -1}"), label, cardId is { } id ? [id] : [], [],
            new Dictionary<string, string> { ["program-action"] = "sole-target-protection", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });

    private IReadOnlyList<PromptChoice> SoleProtectionChoices(ProgramSkillFrame f)
    {
        var r = f.SoleTargetProtection!;
        if (r.Stage == SoleTargetProtectionStage.ChoosingCost)
            return Array.AsReadOnly(SoleProtectionCosts(f.OwnerSeat, f.SkillId, f.SkillInstanceId).Select(c =>
                SoleProtectionChoice(f, "cost", $"弃置【{c.DisplayName}】（{c.RankText}）", c.Id)).ToArray());
        if (r.Stage != SoleTargetProtectionStage.ChoosingBranch) return [];
        var choices = new List<PromptChoice>();
        // Drawing happens before substitution, so an empty hand is not itself a
        // pre-draw prohibition. The actual target is validated again afterwards.
        if (CanSubstituteSoleProtection(f, afterDraw: false)) choices.Add(SoleProtectionChoice(f, "substitute", "摸一张牌，然后代替其成为此牌目标"));
        choices.Add(SoleProtectionChoice(f, "quarantine", "令此牌无效并移出游戏，回合结束后归还使用者"));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishSoleProtectionChoice(ProgramSkillFrame f)
    {
        var choices = SoleProtectionChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat,
            f.SoleTargetProtection!.Stage == SoleTargetProtectionStage.ChoosingCost ? "弃置一张牌，发动镇卫。" : "选择镇卫的保护方式。",
            Array.AsReadOnly(choices.SelectMany(c => c.Cards).ToArray()), [], f.SoleTargetProtection.ActorSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private bool HasExactSoleProtectionRoot(ProgramSkillFrame f)
    {
        if (f.SoleTargetProtection is not { } r || r.ProgramFrameId != f.Id || r.InstructionIndex != f.InstructionIndex ||
            r.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) || r.GameplayHash != f.GameplayHash ||
            f.WindowContext is not { } context || context.ParentFrameId != r.WindowFrameId ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == r.WindowFrameId) is not { } window ||
            window.ParentFrameId != r.CardUseFrameId || window.Action.ActionId != r.ActionId ||
            window.CandidateIndex >= window.Candidates.Count || !MountObserverCandidateMatches(f, ToSharedCandidate(window.Candidates[window.CandidateIndex])) ||
            LifecycleCardUse(r.CardUseFrameId) is not { Action: { } action } use || action.ActionId != r.ActionId ||
            use.SourceSeat != r.ActorSeat || action.ProviderSeat != r.ProviderSeat || use.CardKind != r.EffectiveKind ||
            r.ActualTurnNumber != _turnNumber || r.ActualTurnOwnerSeat != _currentSeat ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash) return false;
        if (r.Cost is null) return r.Stage == SoleTargetProtectionStage.ChoosingCost && r.CostSequenceBefore == 0 && r.CostSequenceAfter == 0;
        var to = r.Cost.Kind == CardKind.GeneralWeapon && r.Cost.From.Zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : CardLocation.DiscardPile;
        return r.Cost.From.OwnerSeat == f.OwnerSeat && r.Cost.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
            r.CostSequenceAfter > r.CostSequenceBefore && _cardMovements.Count(m => m.CardId == r.Cost.CardId &&
                m.Sequence > r.CostSequenceBefore && m.Sequence <= r.CostSequenceAfter && m.From == r.Cost.From && m.To == to &&
                m.Reason.Value == SoleProtectionCostReason) == 1;
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 460)]
    private bool CanContinueSoleTargetProtection(ProgramSkillFrame f) => f.SoleTargetProtection is { Cost: not null } &&
        _players[f.OwnerSeat].IsAlive && _winner == Winner.None && HasExactSoleProtectionRoot(f);

    private void ResolveSoleProtectionChoice(PromptChoice selected)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Protection choice lost its owner.");
        if (!HasExactSoleProtectionRoot(f) || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !SoleProtectionChoices(f).Any(c => c.Id == selected.Id && AssistedChoicesEqual([c], [selected])))
            throw new InvalidOperationException("Protection choice no longer matches its exact owning private prompt.");
        var r = f.SoleTargetProtection!;
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { ClearPendingDecision(); FinishProgramSkill(f, false); return; }
        if (r.Stage == SoleTargetProtectionStage.ChoosingCost)
        {
            if (!HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
                f.WindowContext is not { } context || !CanOfferSoleTargetProtection(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, 0), GetProgramTrigger(f), context))
            { ClearPendingDecision(); FinishProgramSkill(f, false); return; }
            var id = selected.Cards.Single(); var card = SoleProtectionCosts(f.OwnerSeat, f.SkillId, f.SkillInstanceId).Single(c => c.Id == id);
            var cost = new SoleTargetProtectionMaterial(id, card.Kind, _cardZones.GetLocation(id));
            ClearPendingDecision(); ReplaceRuntimeTop(f = f with { SoleTargetProtection = r with { Cost = cost, Stage = SoleTargetProtectionStage.CostChildren,
                CostSequenceBefore = _movementSequence, CostSequenceAfter = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveProgramLogicalDiscard(f, f.OwnerSeat, [id], new(SoleProtectionCostReason));
            f = GetActiveProgramFrame(f.Id); r = f.SoleTargetProtection! with { CostSequenceAfter = _movementSequence };
            ReplaceRuntimeTop(f = f with { SoleTargetProtection = r });
            AdvanceEventRulesAndQueueFact(new SoleTargetProtectionPaidEvent(r));
            var use = LifecycleCardUse(r.CardUseFrameId)!;
            UpdateLifecycleCardUse(use.Id, old => old with { SoleTargetProtectionSources = Array.AsReadOnly((old.SoleTargetProtectionSources ?? []).Append(r.Source).ToArray()),
                SoleTargetProtectionOrigin = old.SoleTargetProtectionOrigin ?? new(old.Id, r.ActionId, r.ActorSeat, r.ProviderSeat,
                    r.EffectiveKind, Array.AsReadOnly(old.TargetSeats.ToArray()), Array.AsReadOnly(r.Materials.ToArray())) });
            AdvanceRuntimeProgram(f.Id); return;
        }
        ClearPendingDecision();
        if (selected.Parameters["branch"] == "substitute")
        {
            ReplaceRuntimeTop(f = f with { SoleTargetProtection = r with { Stage = SoleTargetProtectionStage.DrawChildren, SubstitutionChosen = true,
                RewardSequenceBefore = _movementSequence, RewardSequenceAfter = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            DrawProgramCards(f.Id, f.OwnerSeat, 1, null, null, SkillProgramCardSetVisibility.Private, new("skill-program.sole-target-protection.draw"));
            f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { SoleTargetProtection = f.SoleTargetProtection! with { RewardSequenceAfter = _movementSequence } });
            AdvanceRuntimeProgram(f.Id); return;
        }
        QuarantineSoleProtectionUse(f);
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 20340)]
    private bool ResumeSoleTargetProtection(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { SoleTargetProtection: { } r } f || f.Id != id) return false;
        if (!HasExactSoleProtectionRoot(f)) throw new InvalidOperationException("Sole protection lost its exact actual use and once-paid source.");
        if (r.Stage is SoleTargetProtectionStage.ChoosingCost or SoleTargetProtectionStage.ChoosingBranch)
        { if (_pendingDecision is null) PublishSoleProtectionChoice(f); return true; }
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id)) return true;
        ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null });
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !_players[r.ProtectedSeat].IsAlive)
        { FinishProgramSkill(f, false); return true; }
        if (r.Stage == SoleTargetProtectionStage.CostChildren)
        { ReplaceRuntimeTop(f = f with { SoleTargetProtection = r with { Stage = SoleTargetProtectionStage.ChoosingBranch } }); PublishSoleProtectionChoice(f); return true; }
        if (r.Stage == SoleTargetProtectionStage.DrawChildren) { SubstituteSoleProtectionTarget(f); return true; }
        if (r.Stage == SoleTargetProtectionStage.QuarantineChildren) { FinishProgramSkill(f, true); return true; }
        throw new InvalidOperationException("Unknown sole-target protection continuation stage.");
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 20340)]
    private bool ReturnSoleTargetProtectionMovement(ProgramSkillFrame f)
    { if (f.SoleTargetProtection is null) return false; AdvanceRuntimeProgram(f.Id); return true; }

    private bool CanSubstituteSoleProtection(ProgramSkillFrame f, bool afterDraw)
    {
        var r = f.SoleTargetProtection!; var use = LifecycleCardUse(r.CardUseFrameId); var owner = _players[f.OwnerSeat];
        if (use?.Action is not { } action || !owner.IsAlive || use.SoleTargetQuarantine is not null ||
            SoleProtectionMainTargets(use) is not [var old] || old != r.ProtectedSeat ||
            IsDirectedCardTargetProhibited(use.SourceSeat, owner.Seat, use.CardKind) ||
            HasBeneficiarySuitShield(use.SourceSeat, owner.Seat, action.EffectiveSuit)) return false;
        if (!afterDraw && GetHand(owner).Count == 0 && (IsSlashCard(use.CardKind) || use.CardKind == CardKind.Duel) &&
            CardPolicies(owner, SkillProgramCardPolicyKind.ProhibitTarget, use.CardKind).Any() &&
            CardPolicies(owner, SkillProgramCardPolicyKind.ProhibitTarget, use.CardKind).All(p => p.Policy.Condition is
                { Kind: SkillProgramConditionKind.Not, Children: [{ Kind: SkillProgramConditionKind.HandCountAtLeast, Value: 1 }] }) &&
            !IsBlackTrickTargetProhibited(owner, use.CardKind, ActualTargetPolicyColor(action)) &&
            !CardPolicies(owner, SkillProgramCardPolicyKind.ProhibitTargetBySuit, use.CardKind).Any(p => p.Policy.InputSuit == action.EffectiveSuit)) return true;
        if (IsCardTargetProhibited(owner, use.CardKind, action.EffectiveSuit ?? Suit.None, ActualTargetPolicyColor(action))) return false;
        if (!afterDraw && use.CardKind is (CardKind.FireAttack or CardKind.Snatch or CardKind.Dismantlement)) return owner.Seat != use.SourceSeat;
        if (IsSlashCard(use.CardKind)) return CanBeDistanceFreeDesignatedTarget(use, owner);
        if (IsDelayedCard(use.CardKind)) return !owner.JudgmentAreaAbolished && !HasJudgmentEffectiveCard(owner, use.CardKind) &&
            !HasCardPolicy(owner, SkillProgramCardPolicyKind.ProhibitDelayedTrickTarget);
        if (use.CardKind == CardKind.BorrowedSword)
            return GetWeapon(owner) is not null && use.TargetSeats is [_, var victim] && IsLegalBorrowedSwordSlashTarget(owner, _players[victim]);
        return CanBeDistanceFreeDesignatedTarget(use, owner);
    }

    private void SubstituteSoleProtectionTarget(ProgramSkillFrame f)
    {
        var r = f.SoleTargetProtection!;
        if (!CanSubstituteSoleProtection(f, afterDraw: true)) { FinishProgramSkill(f, true); return; }
        var use = LifecycleCardUse(r.CardUseFrameId)!; var before = use.TargetSeats;
        var targets = before.Select((s, i) => s == r.ProtectedSeat && (use.CardKind != CardKind.BorrowedSword || i % 2 == 0) ? f.OwnerSeat : s).ToArray();
        if (targets.Length == 0 && use.CardKind == CardKind.DrawTwo) targets = [f.OwnerSeat];
        var action = CloneDesignatedExtraTargetAction(use.Action!, targets);
        var attack = use.CardAttack;
        if (attack?.TargetSeat == r.ProtectedSeat) attack = attack with
            { TargetSeat = f.OwnerSeat, IgnoresArmor = HasCardArmorBypass(_players[use.SourceSeat], _players[f.OwnerSeat], use.CardKind) };
        var prepared = use.PreparedTargetAttacks is null ? null : Array.AsReadOnly(use.PreparedTargetAttacks.Select(a =>
            a.TargetSeat == r.ProtectedSeat ? a with { TargetSeat = f.OwnerSeat,
                IgnoresArmor = HasCardArmorBypass(_players[use.SourceSeat], _players[f.OwnerSeat], use.CardKind) } : a).ToArray());
        UpdateProgramRoleCardUse(use with { TargetSeats = Array.AsReadOnly(targets), Action = action, CardAttack = attack,
            PreparedTargetAttacks = prepared, TargetsAdjusted = true }, action);
        AdvanceEventRulesAndQueueFact(new SoleTargetReplacedEvent(use.Id, action.ActionId, r.Source, r.ProtectedSeat, f.OwnerSeat,
            Array.AsReadOnly(before.ToArray()), Array.AsReadOnly(targets.ToArray())));
        if (IsSlashCard(use.CardKind)) QueueSoleTargetRedirect(LifecycleCardUse(use.Id)!, r);
        else
        {
            RefreshCurrentUseAddedTargetCandidates(LifecycleCardUse(use.Id)!, [f.OwnerSeat]);
            RefreshSoleProtectionObservers(LifecycleCardUse(use.Id)!);
        }
        FinishProgramSkill(GetActiveProgramFrame(f.Id), true);
    }

    private void QuarantineSoleProtectionUse(ProgramSkillFrame f)
    {
        var r = f.SoleTargetProtection!; var use = LifecycleCardUse(r.CardUseFrameId)!;
        if (use.SoleTargetQuarantine is not null || SoleProtectionMainTargets(use) is not [var target] || target != r.ProtectedSeat)
            throw new InvalidOperationException("Only the exact still-single actual use can be quarantined.");
        var materials = r.Materials.Where(m => _cardZones.GetLocation(m.CardId) == CardLocation.Processing).ToArray();
        var receipt = new SoleTargetQuarantineReceipt(use.Id, r.ActionId, r.Source, r.GameplayHash, r.ActualTurnNumber,
            r.ActualTurnOwnerSeat, r.ActorSeat, r.ProviderSeat, r.EffectiveKind, Array.AsReadOnly(use.TargetSeats.ToArray()),
            Array.AsReadOnly(materials), _movementSequence, _movementSequence);
        ReplaceRuntimeTop(f = f with { SoleTargetProtection = r with { Stage = SoleTargetProtectionStage.QuarantineChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        UpdateLifecycleCardUse(use.Id, old => old with { SoleTargetQuarantine = receipt,
            IneffectiveTargetSeats = Array.AsReadOnly(old.TargetSeats.Distinct().Order().ToArray()) });
        if (materials.Length > 0) MoveCards(materials.Select(m => GetAttackCard(m.CardId)).ToArray(), CardLocation.Processing, CardLocation.OutsideGame, new(SoleProtectionQuarantineReason));
        receipt = receipt with { QuarantineSequenceAfter = _movementSequence };
        UpdateLifecycleCardUse(use.Id, old => old with { SoleTargetQuarantine = receipt });
        AdvanceEventRulesAndQueueFact(new SoleTargetQuarantineIssuedEvent(receipt)); AdvanceRuntimeProgram(f.Id);
    }

    [ProgramAiChoice(SkillProgramEffectOp.ProtectOtherSoleCardTarget)]
    private PromptChoice SelectAiSoleProtection(PendingDecision decision, ProgramSkillFrame f)
    {
        if (f.SoleTargetProtection!.Stage == SoleTargetProtectionStage.ChoosingCost)
            return decision.Choices.OrderBy(c => GetKeepValue(GetAttackCard(c.Cards.Single()), _players[f.OwnerSeat])).ThenBy(c => c.Cards.Single()).First();
        return decision.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == (_players[f.OwnerSeat].Hp <= 3 ? "quarantine" : "substitute")) ?? decision.Choices[0];
    }
    [ProgramCapability]
    private sealed class SoleTargetProtectionCapability(GameEngine engine) : ISoleTargetProtectionHost
    { public SkillProgramStepOutcome ProtectOtherSoleCardTarget(ProgramSkillFrame frame) => engine.BeginSoleTargetProtection(frame); }
}
