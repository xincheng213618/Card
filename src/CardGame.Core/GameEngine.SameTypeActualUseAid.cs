using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string SameTypeAidGiftReason = "program.same-type-actual-use-aid.gift";
    private bool HasSameTypeActualUseAid =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.OfferSameTypeDifferentNameOrExtraTarget);
    // 2018 official-site community tutorial is auxiliary classification evidence.
    // This is an opt-in engineering interpretation, not a new global card category.
    private static bool IsSameTypeAidEffectiveCard(CardKind kind) => IsSlashCard(kind) ||
        kind is CardKind.Duel or CardKind.FireAttack or CardKind.BarbarianAssault or CardKind.ArrowBarrage;
    private static bool IsSameTypeAidCard(CardKind paid, CardKind effective) =>
        CardCatalog.Get(paid).CategoryName == CardCatalog.Get(effective).CategoryName &&
        ProgramBasicCardName(paid) != ProgramBasicCardName(effective);
    private CardConversionSource SameTypeAidSource(ProgramSkillFrame f) =>
        new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    private SameTypeAidIdentity? FreezeSameTypeAidUse(int ownerSeat, ProgramSkillWindowContext context)
    {
        if (!HasSameTypeActualUseAid) return null;
        if (context.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized &&
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is { } window &&
            window.Id == context.ParentFrameId && GetCardActionWindow(window) == context.Window &&
            window.Action is { Type: CardActionType.Use } action &&
            LifecycleCardUse(window.ParentFrameId) is { } use && use.Action?.ActionId == action.ActionId &&
            action.ActorSeat == use.SourceSeat && action.EffectiveKind == use.CardKind &&
            use.TargetSeats.Contains(ownerSeat) && action.TargetSeats.Contains(ownerSeat) &&
            IsSameTypeAidEffectiveCard(action.EffectiveKind))
            return new(use.Id, action.ActionId, action.ActorSeat, action.ProviderSeat, action.EffectiveKind,
                ownerSeat, _turnNumber, _currentSeat);
        // Mature virtual Shensu has no Action. Only this true producer enters the
        // second trigger; physical/action-bearing uses are served by Finalized.
        if (context.Window == SkillProgramTriggerWindow.OtherActualUseTargeted &&
            context.ActualUseTarget is { ActionId: null, LegacyProducerProgramId: { } producer } legacy &&
            legacy.TargetSeat == ownerSeat && IsSlashCard(legacy.EffectiveKind) && MatchesActualUseTarget(legacy))
            return new(legacy.CardUseFrameId, null, legacy.ActorSeat, legacy.ProviderSeat, legacy.EffectiveKind,
                ownerSeat, legacy.ActualTurnNumber, legacy.ActualTurnOwnerSeat, producer);
        return null;
    }
    private bool MatchesSameTypeAidUse(SameTypeAidIdentity identity)
    {
        if (LifecycleCardUse(identity.CardUseFrameId) is not { } use || use.SourceSeat != identity.ActorSeat ||
            use.CardKind != identity.EffectiveKind || !use.TargetSeats.Contains(identity.OriginalOwnerTarget) ||
            identity.ActualTurnNumber != _turnNumber || identity.ActualTurnOwnerSeat != _currentSeat ||
            !IsSameTypeAidEffectiveCard(identity.EffectiveKind)) return false;
        if (identity.CardActionId is { } actionId)
            return identity.LegacyProducerProgramId is null && use.Action is { Type: CardActionType.Use } action &&
                action.ActionId == actionId && action.ActorSeat == identity.ActorSeat && action.ProviderSeat == identity.ProviderSeat &&
                action.EffectiveKind == identity.EffectiveKind && action.TargetSeats.Contains(identity.OriginalOwnerTarget);
        return identity.LegacyProducerProgramId is { } producer && use.Action is null &&
            (LegacyDamageJudgmentVirtualProducer(use, identity.OriginalOwnerTarget) == producer ||
             HasSameTypeAidTargetTail(use) && MatchesSameTypeAidLegacyProducer(use, producer));
    }
    private bool CanRunSameTypeAid(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.OfferSameTypeDifferentNameOrExtraTarget)) return true;
        return _players[candidate.OwnerSeat].IsAlive && _players[candidate.OwnerSeat].Hp > 0 &&
            FreezeSameTypeAidUse(candidate.OwnerSeat, context) is { } identity &&
            !IsCardEffectIneffective(identity.CardUseFrameId, candidate.OwnerSeat) &&
            SameTypeAidRecipients(identity).Length > 0;
    }
    private bool CanAddSameTypeAidTarget(SameTypeAidIdentity identity, int seat)
    {
        if (!IsValidPlayerSeat(seat) || LifecycleCardUse(identity.CardUseFrameId) is not { } use ||
            !MatchesSameTypeAidUse(identity) || use.TargetSeats.Contains(seat) || !_players[seat].IsAlive ||
            seat == identity.ActorSeat || HasTurnCardTargetRestriction(identity.ActorSeat, SkillProgramCardTargetRestriction.SelfOnly) ||
            IsDirectedCardTargetProhibited(identity.ActorSeat, seat, identity.EffectiveKind)) return false;
        var action = use.Action; var suit = action?.EffectiveSuit ?? Suit.None;
        var color = action is null ? null : ActualTargetPolicyColor(action);
        var actor = _players[identity.ActorSeat]; var target = _players[seat];
        if (IsCardTargetProhibited(target, identity.EffectiveKind, suit, color) ||
            HasBeneficiarySuitShield(actor.Seat, seat, suit)) return false;
        if (IsSlashCard(identity.EffectiveKind))
        {
            if (action is null)
                return !IsSlashProhibited(target); // exact legacy producer already uses unlimited distance
            var materials = action.PhysicalCards.Select(c => c.CardId).ToArray();
            var card = new Card(0, identity.EffectiveKind, suit, action.EffectiveRank ?? 0);
            return !IsCardUseForbidden(actor.Seat, identity.EffectiveKind, CardActionType.Use, ignoreIssuedPlayBan: true) &&
                !IsSlashProhibited(target) &&
                (SameTypeAidIssuedSlashIgnoresDistance(use) || HasIssuedFirstPlayUseDistance(use.Id) ||
                 HasIssuedProvenanceUseDistance(use.Id, actor.Seat) || HasIssuedGrantedPhaseEntityDistance(use.Id, actor.Seat) ||
                 HasProvenanceUseDistance(actor, materials) || HasGrantedPhaseEntityDistance(actor, materials) ||
                 HasSlashUseDistanceBySuit(actor, identity.EffectiveKind, action.EffectiveSuit) ||
                 HasTurnRedSlashPolicyForColor(actor.Seat, identity.EffectiveKind, color) ||
                 HasPhaseSuitAllowance(actor.Seat, action.EffectiveSuit) || HasCardDistanceExemption(actor, target, identity.EffectiveKind) ||
                 action.ConversionChain.Any(source => IgnoresProgramSlashDistance(actor, source)) ||
                 IgnoresSpGuanYuWushengDistance(actor, card) || HasUnlimitedTurnRuleModifier(actor.Seat, SkillRuleQuery.SlashDistanceLimit) ||
                 IsWithinSpecificSlashRange(actor, target, identity.EffectiveKind, action.EffectiveRank));
        }
        return identity.EffectiveKind switch
        {
            CardKind.Duel => true,
            CardKind.FireAttack => GetHand(target).Count > 0,
            // Their mature declared targets already include all living others.
            // No duplicate target or fictitious extra attack is invented.
            CardKind.BarbarianAssault or CardKind.ArrowBarrage => false,
            _ => false
        };
    }
    private bool SameTypeAidIssuedSlashIgnoresDistance(CardUseFrame use)
    {
        if (use.PindianWinnerSlashReturn is { } winner) return MatchesPindianWinnerUse(use, winner);
        if (use.ForeignTurnContestSlashReturn is not null) return true;
        if (use.CardAttack?.ProgramSkillCardUseFrameId is not { } id ||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == id) is not { } parent ||
            _contentRegistry.GetSkill(parent.SkillId).Program is not { } program || program.GameplayHash != parent.GameplayHash ||
            parent.InstructionIndex < 1) return false;
        var paused = ProgramInstructionResolver.Default.Resolve(parent, program).GetPausedInstruction(parent.InstructionIndex).Effect;
        return paused.Op is SkillProgramEffectOp.UseVirtualCard or SkillProgramEffectOp.OfferUnlimitedVirtualSlash &&
            paused.OutputKind == CardKind.Slash && paused.TargetRestriction == SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget;
    }
    private int[] SameTypeAidRecipients(SameTypeAidIdentity identity) => _players.Where(p => p.IsAlive &&
        p.Seat != identity.OriginalOwnerTarget && p.Seat != identity.ActorSeat &&
        (CanAddSameTypeAidTarget(identity, p.Seat) || GetHand(p).Count + GetEquipment(p).Count > 0))
        .Select(p => p.Seat).Order().ToArray();
    private bool SameTypeAidUnpaidSourceValid(ProgramSkillFrame f) =>
        f.SameTypeAid is { } r && _winner == Winner.None && _players[f.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) && MatchesSameTypeAidUse(r.Use) &&
        !IsCardEffectIneffective(r.Use.CardUseFrameId, f.OwnerSeat) &&
        (r.RecipientSeat is null || _players[r.RecipientSeat.Value].IsAlive);
    private SkillProgramStepOutcome BeginSameTypeAid(ProgramSkillFrame frame)
    {
        var f = GetActiveProgramFrame(frame.Id);
        if (f.SameTypeAid is not null || f.InstructionIndex != 1 || f.WindowContext is not { } context ||
            FreezeSameTypeAidUse(f.OwnerSeat, context) is not { } use)
            throw new InvalidOperationException("Aid requires one exact original target/use opportunity.");
        ReplaceRuntimeTop(f = f with { SameTypeAid = new(f.InstructionIndex, SameTypeAidSource(f), f.GameplayHash,
            use, SameTypeAidStage.ChoosingRecipient) });
        PublishSameTypeAidPrompt(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private Dictionary<string, string> SameTypeAidParameters(ProgramSkillFrame f, string option) => new()
    { ["program-action"] = "same-type-actual-use-aid", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["aid-option"] = option };
    private IReadOnlyList<PromptChoice> SameTypeAidChoices(ProgramSkillFrame f)
    {
        var r = f.SameTypeAid!;
        if (r.Stage == SameTypeAidStage.ChoosingRecipient)
            return Array.AsReadOnly(SameTypeAidRecipients(r.Use).Select(seat => new PromptChoice(new($"same-type-aid.{f.Id}.recipient-{seat}"),
                $"请 {_players[seat].Name} 给牌或成为额外目标", [], [seat], SameTypeAidParameters(f, "recipient"))).ToArray());
        if (r.Stage != SameTypeAidStage.ChoosingAnswer || r.RecipientSeat is not { } recipient) return [];
        var choices = new List<PromptChoice>();
        foreach (var raw in BuildOwnedCardPaymentChoices(f.Id, recipient, recipient, [CardZoneKind.Hand, CardZoneKind.Equipment],
            OwnedCardMoveIntent.Transfer, canSelect: (_, card) => !card.IsGeneralWeapon && IsSameTypeAidCard(card.Kind, r.Use.EffectiveKind),
            destination: SkillProgramCardDestination.OwnerHand, destinationSeat: f.OwnerSeat))
        {
            var parameters = new Dictionary<string, string>(raw.Parameters);
            foreach (var pair in SameTypeAidParameters(f, "give")) parameters[pair.Key] = pair.Value;
            choices.Add(raw with { Parameters = parameters });
        }
        if (CanAddSameTypeAidTarget(r.Use, recipient))
            choices.Add(new(new($"same-type-aid.{f.Id}.become-target"), "成为此牌的额外目标", [], [recipient], SameTypeAidParameters(f, "target")));
        // Candidate seats use only public HE counts. A private hand without a
        // matching card must not leak its eligibility to the original chooser.
        if (choices.Count == 0)
            choices.Add(new(new($"same-type-aid.{f.Id}.unavailable"), "没有可交给的牌且不能成为额外目标", [], [], SameTypeAidParameters(f, "unavailable")));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishSameTypeAidPrompt(ProgramSkillFrame f)
    {
        var r = f.SameTypeAid!; var choices = SameTypeAidChoices(f);
        if (choices.Count == 0) { FinishProgramSkill(f, false); return; }
        var chooser = r.Stage == SameTypeAidStage.ChoosingRecipient ? f.OwnerSeat : r.RecipientSeat!.Value;
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser,
            r.Stage == SameTypeAidStage.ChoosingRecipient ? "选择一名其他角色求援。" : "交给原目标同类型不同牌名的一张牌，或成为原牌额外目标。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = chooser, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private void ResolveSameTypeAidChoice(PromptChoice selected)
    {
        var f = GetActiveProgramFrame(_resolutionStack.Last().Id); AssertSameTypeAid(f);
        var r = f.SameTypeAid ?? throw new InvalidOperationException("Aid answer lost its owning receipt.");
        if (_pendingDecision?.PlayerSeat != (r.Stage == SameTypeAidStage.ChoosingRecipient ? f.OwnerSeat : r.RecipientSeat) ||
            !AssistedChoicesEqual(_pendingDecision.Choices, SameTypeAidChoices(f)) ||
            !SameTypeAidChoices(f).Any(c => c.Id == selected.Id && c.Cards.SequenceEqual(selected.Cards) && c.Targets.SequenceEqual(selected.Targets)))
            throw new InvalidOperationException("Aid changed its frozen chooser, payment slot or current target.");
        ClearPendingDecision();
        if (!SameTypeAidUnpaidSourceValid(f)) { FinishProgramSkill(f, false); return; }
        var option = selected.Parameters.GetValueOrDefault("aid-option");
        if (r.Stage == SameTypeAidStage.ChoosingRecipient)
        {
            if (option != "recipient" || selected.Targets is not [var recipient] || !SameTypeAidRecipients(r.Use).Contains(recipient))
                throw new InvalidOperationException("Aid requires one publicly eligible recipient.");
            ReplaceRuntimeTop(f = f with { SameTypeAid = r with { RecipientSeat = recipient, Stage = SameTypeAidStage.ChoosingAnswer } });
            AdvanceEventRulesAndQueueFact(new SameTypeAidOfferedEvent(f.Id, r.Source, f.GameplayHash, r.Use, recipient));
            PublishSameTypeAidPrompt(f); return;
        }
        if (r.Stage != SameTypeAidStage.ChoosingAnswer || r.RecipientSeat is not { } donor)
            throw new InvalidOperationException("Aid is not awaiting its original recipient.");
        if (option == "unavailable") { ReplaceRuntimeTop(f = f with { SameTypeAid = r with { Stage = SameTypeAidStage.Complete } }); FinishProgramSkill(f, false); return; }
        if (option == "target")
        {
            if (!CanAddSameTypeAidTarget(r.Use, donor)) throw new InvalidOperationException("Aid cannot add an illegal or duplicate target.");
            AddSameTypeAidTarget(f, donor); ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with
            { SameTypeAid = r with { AddedTarget = true, Stage = SameTypeAidStage.Complete } });
            FinishProgramSkill(f, true); return;
        }
        if (option != "give" || selected.Cards is not [var id] ||
            _cardZones.GetLocation(id) is not { OwnerSeat: { } sourceOwner } from || sourceOwner != donor ||
            from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))
            throw new InvalidOperationException("Aid requires one real recipient-owned HE entity.");
        var card = _cardZones.CardsAt(from).Single(c => c.Id == id);
        if (card.IsGeneralWeapon || !IsSameTypeAidCard(card.Kind, r.Use.EffectiveKind))
            throw new InvalidOperationException("Aid payment changed its exact type or card name.");
        ReplaceRuntimeTop(f = f with { SameTypeAid = r with { Stage = SameTypeAidStage.GiftChildren },
            PendingMovementContinuation = new(donor, 0, null) });
        MoveProgramCardsFromMultipleSources([id], CardLocation.Hand(f.OwnerSeat), new(SameTypeAidGiftReason), (_, records) =>
        {
            if (records is not [var moved] || moved.From != from || moved.To != CardLocation.Hand(f.OwnerSeat) || moved.CardId != id)
                throw new InvalidOperationException("Aid cannot claim payment without its real HE delivery.");
            var current = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(current with { SameTypeAid = current.SameTypeAid! with
            { Payment = new(id, card.Kind, from, moved.Sequence, moved.Sequence, true) } });
        });
        // Removal hooks can replace the root with queued recovery attempts.
        f = GetActiveProgramFrame(f.Id);
        var paid = f.SameTypeAid!.Payment! with { SequenceAfter = _cardMovements.Last().Sequence };
        ReplaceRuntimeTop(f = f with { SameTypeAid = f.SameTypeAid with { Payment = paid } });
        AdvanceEventRulesAndQueueFact(new SameTypeAidGiftPaidEvent(f.Id, r.Source, r.Use, donor,
            paid.MovementSequence, paid.SequenceAfter, paid.Delivered));
        AdvanceRuntimeProgram(f.Id);
    }
    private void AddSameTypeAidTarget(ProgramSkillFrame f, int target)
    {
        var r = f.SameTypeAid!; var use = LifecycleCardUse(r.Use.CardUseFrameId)!;
        var targets = Array.AsReadOnly(use.TargetSeats.Append(target).ToArray());
        var action = use.Action is { } original
            ? new CardActionContext(original.ActionId, original.ParentActionId, original.Type, original.ActorSeat, original.ProviderSeat,
                original.RequesterSeat, original.ResponderSeat, original.OpponentSeat, original.EffectiveKind, targets,
                original.PhysicalCards, original.ConversionChain, targets, original.EffectiveSuit, original.EffectiveRank, original.EffectiveIsRed, original.FactionOrigin)
            : null;
        if (action is not null)
        {
            UpdateProgramRoleCardUse(use with { TargetSeats = targets, Action = action }, action);
            AppendSameTypeAidFinalizedCandidates(use.Id, action, target);
        }
        else UpdateLifecycleCardUse(use.Id, u => u with { TargetSeats = targets });
        AdvanceEventRulesAndQueueFact(new SameTypeAidTargetAddedEvent(f.Id, r.Source, r.Use, target));
        AdvanceEventRulesAndQueueFact(new ProgramCardUseTargetAddedEvent(f.Id, f.SkillId, f.OwnerSeat, use.Id, target));
    }
    private void AppendSameTypeAidFinalizedCandidates(long useId, CardActionContext action, int newTarget)
    {
        var window = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault(w =>
            w.ParentFrameId == useId && GetCardActionWindow(w) == SkillProgramTriggerWindow.CardUseTargetsFinalized);
        if (window is null) return;
        var additions = CollectSharedCardActionCandidates(action, SkillProgramTriggerWindow.CardUseTargetsFinalized, [newTarget], null, window.Continuation)
            .Where(c => c.OwnerSeat == newTarget && _contentRegistry.GetSkill(c.SkillId).Program!.Triggers.Single(t => t.Id == c.TriggerId)
                .Effects.Any(e => e.Op == SkillProgramEffectOp.OfferSameTypeDifferentNameOrExtraTarget) &&
                !window.Candidates.Any(old => old.OwnerSeat == c.OwnerSeat && old.SkillId == c.SkillId &&
                    old.TriggerId == c.TriggerId && old.SkillInstanceId == c.SkillInstanceId))
            .Select(c => c.FrozenContext is { } context ? c with { FrozenContext = context with
            { ParentFrameId = window.Id, CardUse = context.CardUse! with { ParentCardUseFrameId = useId } } } : c).ToArray();
        if (additions.Length > 0) ReplaceRuntimeFrame(window.Id, window with
        { Candidates = Array.AsReadOnly(window.Candidates.Concat(additions).ToArray()) });
    }
    private bool ResumeSameTypeAid(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != frameId || f.SameTypeAid is not { } r) return false;
        AssertSameTypeAid(f);
        if (r.Stage is SameTypeAidStage.ChoosingRecipient or SameTypeAidStage.ChoosingAnswer)
        { if (!SameTypeAidUnpaidSourceValid(f)) { ClearPendingDecision(); FinishProgramSkill(f, false); } else if (_pendingDecision is null) PublishSameTypeAidPrompt(f); return true; }
        if (r.Stage == SameTypeAidStage.GiftChildren)
        {
            if (TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
                TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
                TryBeginCardsMovedProgramWindow(f.Id, subjectSeat: r.RecipientSeat)) return true;
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, SameTypeAid = r with { Stage = SameTypeAidStage.Complete } });
        }
        FinishProgramSkill(f, true); return true;
    }
    private PromptChoice SelectAiSameTypeAid(PendingDecision decision, ProgramSkillFrame f)
    {
        var snapshot = CreateSnapshot(decision.PlayerSeat); var r = f.SameTypeAid!;
        if (r.Stage == SameTypeAidStage.ChoosingRecipient)
        {
            var harm = new SkillProgramAiHint(0, 0, 0, 0, 0, 1, false, false);
            return decision.Choices.OrderByDescending(c => _aiBrains[decision.PlayerSeat].ScoreProgramTarget(snapshot, c.Targets.Single(), harm))
                .ThenBy(c => c.Targets.Single()).First();
        }
        var give = decision.Choices.Where(c => c.Parameters.GetValueOrDefault("aid-option") == "give")
            .OrderBy(c => GetKeepValue(GetAttackCard(c.Cards.Single()), _players[decision.PlayerSeat])).ThenBy(c => c.Cards.Single()).FirstOrDefault();
        var target = decision.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("aid-option") == "target");
        var ownerBenefit = _aiBrains[decision.PlayerSeat].ScoreProgramTarget(snapshot, f.OwnerSeat, new(0, 0, 0, 1, 0, 0, false, false));
        return give is not null && (ownerBenefit > 0 || target is null || _players[decision.PlayerSeat].Hp <= 1) ? give :
            target ?? give ?? decision.Choices.Single();
    }
    private bool IsSameTypeAidTargetFact(CardUseFrame use, SameTypeAidTargetAddedEvent fact)
    {
        if (fact.Use.CardUseFrameId != use.Id || fact.Use.ActualTurnNumber != _turnNumber ||
            fact.Use.ActualTurnOwnerSeat != _currentSeat || !use.TargetSeats.Contains(fact.RecipientSeat) ||
            fact.Use.OriginalOwnerTarget == fact.RecipientSeat || fact.Use.ActorSeat == fact.RecipientSeat ||
            fact.Source.OwnerSeat != fact.Use.OriginalOwnerTarget ||
            _contentRegistry.Skills.GetValueOrDefault(fact.Source.SkillId)?.Program is not { } program ||
            ProgramInstructionResolver.Default.FindTrigger(program, fact.Source.BindingId) is not { } trigger ||
            trigger.Effects is not [{ Op: SkillProgramEffectOp.OfferSameTypeDifferentNameOrExtraTarget }]) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<SameTypeAidOfferedEvent>().Count(e => e.ProgramFrameId == fact.ProgramFrameId &&
                e.Source == fact.Source && e.GameplayHash == program.GameplayHash && e.Use == fact.Use &&
                e.RecipientSeat == fact.RecipientSeat) != 1 ||
            history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == fact.ProgramFrameId &&
                e.SkillId == fact.Source.SkillId && e.SkillInstanceId == fact.Source.SkillInstanceId &&
                e.BindingId == fact.Source.BindingId && e.OwnerSeat == fact.Source.OwnerSeat && e.Window == trigger.Window) != 1)
            return false;
        // The same ActionId survives legitimate role and color rewrites. Its
        // original material/actor is not replaced by a later mutable use actor.
        return fact.Use.CardActionId is { } actionId
            ? use.Action?.ActionId == actionId && history.OfType<CardActionAcceptedEvent>().Any(e =>
                e.Action.ActionId == actionId && e.Action.Type == CardActionType.Use) &&
              (use.CardKind == fact.Use.EffectiveKind || IsCurrentSlashFireChangedUse(use) && IsSlashCard(fact.Use.EffectiveKind))
            : use.Action is null && fact.Use.LegacyProducerProgramId is { } producer &&
              MatchesSameTypeAidLegacyProducer(use, producer);
    }
    private bool HasSameTypeAidTargetTail(CardUseFrame use) =>
        CompleteProgramEventHistory().OfType<SameTypeAidTargetAddedEvent>().Any(e => IsSameTypeAidTargetFact(use, e));
    private bool MatchesSameTypeAidLegacyProducer(CardUseFrame use, long producer)
    {
        if (use.Action is not null || use.CardId != 0 || use.CardKind != CardKind.Slash ||
            use.PhysicalCardIds is not { Count: 0 } || use.TargetSeats.Count == 0 ||
            use.TargetSeats.Distinct().Count() != use.TargetSeats.Count || use.CardAttack is not
                { IsSourceLess: false, EffectiveCardKind: CardKind.Slash, CardId: null, PhysicalCardIds.Count: 0 } attack ||
            attack.ProgramSkillCardUseFrameId != producer || attack.SourceSeat != use.SourceSeat) return false;
        var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame parent || parent.Id != producer ||
            parent.OwnerSeat != use.SourceSeat || parent.TriggerId is null ||
            parent.SelectedTargetSeats is not [var original] ||
            _contentRegistry.GetSkill(parent.SkillId).Program is not { } program || program.GameplayHash != parent.GameplayHash ||
            CompleteProgramEventHistory().OfType<TargetsConfirmedEvent>().Count(e =>
                e.ResolutionId == use.Id && e.TargetSeats.SequenceEqual([original])) != 1 ||
            CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().Count(e => e.ResolutionId == use.Id &&
                e.CardId == 0 && e.CardKind == CardKind.Slash && e.SourceSeat == use.SourceSeat) != 1) return false;
        var paused = ProgramInstructionResolver.Default.Resolve(parent, program).GetPausedInstruction(parent.InstructionIndex).Effect;
        return paused is { Op: SkillProgramEffectOp.UseVirtualCard, OutputKind: CardKind.Slash, UseCardActionWindows: false,
            TargetRestriction: SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget };
    }
    private long? SameTypeAidLegacyCurrentProducer(CardUseFrame use, int target) =>
        use.TargetSeats.Contains(target) && use.CardAttack is { Active: true, ProgramSkillCardUseFrameId: { } producer } attack &&
        attack.TargetSeat == target && HasSameTypeAidTargetTail(use) && MatchesSameTypeAidLegacyProducer(use, producer) ? producer : null;
}
