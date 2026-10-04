namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string NextActualUseAdjustmentBinding = "next-actual-use-target-adjustment";

    private sealed partial class ProgramSkillHost : INextActualUseTargetAdjustmentProgramHost
    {
        public void GrantNextActualUseTargetAdjustment(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.GrantNextActualUseTargetAdjustment(frame, effect);
    }

    private void GrantNextActualUseTargetAdjustment(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        ValidateProgramTurnEffectGrant(frame);
        var active = GetActiveProgramFrame(frame.Id);
        var program = _contentRegistry.GetSkill(frame.SkillId).Program!;
        var paid = active.PindianResultBindings.SingleOrDefault(b => b.Name == effect.Condition.SourceBind);
        var paused = ProgramInstructionResolver.Default.Resolve(active, program)
            .GetPausedInstruction(active.InstructionIndex).Effect;
        if (active.TriggerId is not null || paused.Op != SkillProgramEffectOp.GrantNextActualUseTargetAdjustment ||
            paused.StateId != effect.StateId || paused.Condition.SourceBind != effect.Condition.SourceBind ||
            paid is not { SourceWon: true } || paid.SourceSeat != frame.OwnerSeat ||
            active.SelectedTargetSeats.Count != 1 || paid.OpponentSeat != active.SelectedTargetSeats[0] ||
            GetProgramBooleanState(active, effect.StateId!))
            throw new InvalidOperationException("Next actual-use adjustment lost its paid winning contest and enabled source.");
        AdvanceEventRulesAndQueueFact(new NextActualUseTargetAdjustmentGrantedEvent(
            active.Id, active.InstructionIndex - 1, _turnNumber, _currentSeat,
            new(active.SkillId, NextActualUseAdjustmentBinding, active.OwnerSeat, active.SkillInstanceId),
            active.GameplayHash, effect.StateId!));
    }

    private NextActualUseTargetAdjustmentGrantedEvent? CurrentNextActualUseAdjustment(int ownerSeat)
    {
        if (!IsValidPlayerSeat(ownerSeat) || !_players[ownerSeat].IsAlive ||
            !GetSkillBindingShard(_players[ownerSeat]).ProgramInstances.Any(instance => instance.Program.Activations
                .Any(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.GrantNextActualUseTargetAdjustment)))) return null;
        var turnFacts = EventsSinceLastBoundary(e => e is TurnStartedEvent or TurnEndedEvent).ToArray();
        var grant = turnFacts.OfType<NextActualUseTargetAdjustmentGrantedEvent>().LastOrDefault(e =>
            e.TurnNumber == _turnNumber && e.TurnOwnerSeat == _currentSeat && e.Source.OwnerSeat == ownerSeat);
        if (grant is null || turnFacts.OfType<NextActualUseTargetAdjustmentConsumedEvent>().Any(e =>
                e.GrantProgramFrameId == grant.ProgramFrameId) || !IsValidPlayerSeat(ownerSeat) ||
            !_players[ownerSeat].IsAlive || !HasRuntimeSkillInstance(_players[ownerSeat], grant.Source.SkillId, grant.Source.SkillInstanceId) ||
            _contentRegistry.GetSkill(grant.Source.SkillId).Program?.GameplayHash != grant.GameplayHash ||
            GetProgramBooleanState(ownerSeat, grant.Source.SkillId, grant.Source.SkillInstanceId, grant.DisabledStateId)) return null;
        return grant;
    }

    private bool HasNextActualUseAdjustment(CharacterState owner) => CurrentNextActualUseAdjustment(owner.Seat) is not null;
    private bool IsNextActualUseAdjustmentAction(CharacterState actor, LegalAction action) =>
        action.ProgramActivationId == NextActualUseAdjustmentBinding && CurrentNextActualUseAdjustment(actor.Seat) is { } grant &&
        action.ProgramSkillId == grant.Source.SkillId;

    private void ObserveNextActualUseAdjustment(IGameEvent payload)
    {
        long frameId; int actor; long? actionId; CardKind kind; bool nullification;
        if (payload is CardUseDeclaredEvent declared &&
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(f => f.Id == declared.ResolutionId) is { } use &&
            use.SourceSeat == declared.SourceSeat && use.CardId == declared.CardId && use.CardKind == declared.CardKind &&
            (use.Action is { Type: CardActionType.Use } || use.Action is null && use.CardId == 0 &&
                use.PhysicalCardIds is { Count: 0 } && IsActualTurnLegacyVirtualUse(use)))
        {
            MarkNextActualUseProgramChild(use);
            frameId = use.Id; actor = use.SourceSeat; actionId = use.Action?.ActionId; kind = use.CardKind; nullification = false;
        }
        else if (payload is CardActionAcceptedEvent { Action: { Type: CardActionType.Response, EffectiveKind: CardKind.Nullification } action } &&
            action.ActorSeat == action.ProviderSeat && action.ResponderSeat == action.ActorSeat && action.RequesterSeat is null &&
            _resolutionStack.OfType<NullificationWindowFrame>().LastOrDefault() is { } window &&
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(f => f.Id == window.ParentFrameId) is { } parent &&
            action.ParentActionId == parent.Action?.ActionId && action.OpponentSeat == window.SourceSeat)
        {
            frameId = window.Id; actor = action.ActorSeat; actionId = action.ActionId; kind = action.EffectiveKind; nullification = true;
        }
        else return;
        if (CurrentNextActualUseAdjustment(actor) is not { } grant) return;
        AdvanceEventRulesAndQueueFact(new NextActualUseTargetAdjustmentConsumedEvent(
            grant.ProgramFrameId, _turnNumber, _currentSeat, grant.Source, frameId, actionId, kind, nullification));
    }

    // The new program's paid Use prohibition also applies to the Response
    // syntax of a true counterspell. Old bans and ordinary responses keep their
    // old interpretation. Lookup retains an already-issued prohibition after
    // source loss, and does not read hidden cards.
    private bool IsNextActualUseCounterspellForbidden(int seat, CardKind kind, CardActionType type) =>
        type == CardActionType.Response && kind == CardKind.Nullification &&
        _turnCardUseEffects.ActionProhibitions.Any(p => p.TurnNumber == _turnNumber && p.TurnSeat == _currentSeat &&
            p.Source.OwnerSeat == seat && p.ActionTypes.Contains(CardActionType.Use) && p.CardKinds.Contains(kind) &&
            _contentRegistry.GetSkill(p.Source.SkillId).Program?.Activations.Any(a =>
                a.Effects.Any(e => e.Op == SkillProgramEffectOp.GrantNextActualUseTargetAdjustment)) == true);

    private void AddNextActualUseAdjustmentActions(List<LegalAction> actions, CharacterState actor)
    {
        if (CurrentNextActualUseAdjustment(actor.Seat) is not { } grant) return;
        for (var i = 0; i < actions.Count; i++)
            if (actions[i] is { Kind: LegalActionKind.UseEquipmentEffect, EquipmentKind: CardKind.ZhangbaSerpentSpear,
                    MinTargetCount: 1, MaxTargetCount: 1 } spear)
                actions[i] = spear with { MaxTargetCount = 2 };
        var keys = actions.Select(a => (a.CardId, a.Kind, a.PlayedCardKind, a.ConversionSource, a.TargetCardId,
            Targets: string.Join(",", a.TargetSeats))).ToHashSet();
        foreach (var action in actions.ToArray().Where(a => a.CardId is not null && a.ProgramActivationId is null && a.Kind != LegalActionKind.Recast))
        {
            var physical = FindOwnedPlayableCard(actor, action.CardId)!;
            var kind = action.PlayedCardKind ?? physical.Kind;
            var suit = EffectiveSuit(actor, ApplyProgramUseAppearance(actor, physical, action.ConversionSource));
            var normal = action.Kind is LegalActionKind.DrawTwo or LegalActionKind.Peach or LegalActionKind.Alcohol
                ? new[] { actor.Seat } : GetDeclaredCardTargets(actor, action.Kind, action.TargetSeats).ToArray();
            if (normal.Length == 0) continue;
            if (kind == CardKind.BorrowedSword)
            {
                normal = action.TargetSeats.ToArray();
                foreach (var holder in _players.Where(p => p.IsAlive && p.Seat != actor.Seat && !normal.Where((_, i) => i % 2 == 0).Contains(p.Seat) &&
                    GetWeapon(p) is not null && !IsDirectedCardTargetProhibited(actor.Seat, p.Seat, kind) &&
                    !IsCardTargetProhibited(p, kind, suit, SuitColor(suit)) && !HasBeneficiarySuitShield(actor.Seat, p.Seat, suit)))
                foreach (var victim in _players.Where(p => IsLegalBorrowedSwordSlashTarget(holder, p))) Add([..normal, holder.Seat, victim.Seat]);
                if (normal.Length >= 4) for (var i = 0; i < normal.Length; i += 2) Add(normal.Where((_, j) => j != i && j != i + 1).ToArray());
                continue;
            }
            if (EquipmentCatalog.IsEquipment(kind) || kind is CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning) continue;
            var possible = kind is CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.PeachGarden or CardKind.FiveGrains
                ? normal : _players.Where(p => CanBeExtraNextCardTarget(actor, p, action, kind, suit) &&
                    !(p.Seat == actor.Seat && action.ConversionSource is { } source && ViewAsRule(source)?.ExcludeOwnerEffects == true)).Select(p => p.Seat);
            foreach (var seat in possible.Except(normal)) Add([..normal, seat]);
            if (normal.Length > 1) foreach (var seat in normal) Add(normal.Where(s => s != seat).ToArray());
            void Add(IReadOnlyList<int> targets)
            {
                if (targets.Count == 0 || !keys.Add((action.CardId, action.Kind, action.PlayedCardKind, action.ConversionSource,
                        action.TargetCardId, string.Join(",", targets)))) return;
                actions.Add(action with { TargetSeat = targets[0], TargetSeats = Array.AsReadOnly(targets.ToArray()),
                    ProgramSkillId = grant.Source.SkillId, ProgramActivationId = NextActualUseAdjustmentBinding,
                    Description = action.Description + "（巧说目标调整）" });
            }
        }
    }

    private static SkillProgramEffect? NextActualUseSelectedProducer(ProgramExecutionPlan plan) =>
        plan.Activation is { MinTargets: 1, MaxTargets: 1 } && plan.Instructions.Count == 1 &&
            plan.Instructions[0] is { Op: SkillProgramEffectOp.UseSelectedCardsAs, OutputKind: CardKind.Slash or CardKind.FireSlash } slash ? slash :
        plan.Activation is { MinTargets: 0, MaxTargets: 0 } && plan.Instructions.Count == 1 &&
            plan.Instructions[0] is { Op: SkillProgramEffectOp.UseSelectedCardsAs, OutputKind: CardKind.ArrowBarrage or CardKind.Peach } global ? global : null;

    private int NextActualUseProgramMaximum(CharacterState owner, ProgramExecutionPlan plan) =>
        HpLossSlashProgramMaximum(owner, plan, CurrentNextActualUseAdjustment(owner.Seat) is not null && NextActualUseSelectedProducer(plan) is not null
            ? plan.Activation!.MaxTargets + 1 : plan.Activation!.MaxTargets);

    private IReadOnlyList<int> NextActualUseProgramTargets(CharacterState owner, ProgramExecutionPlan plan,
        IReadOnlyList<int> normal, IReadOnlyList<ProgramMultiCardViewAsSelection> selections) =>
        CurrentNextActualUseAdjustment(owner.Seat) is not null && NextActualUseSelectedProducer(plan) is { OutputKind: CardKind.ArrowBarrage }
            ? _players.Where(p => p.IsAlive && p.Seat != owner.Seat && selections.Any(s =>
                !IsCardTargetProhibited(p, CardKind.ArrowBarrage, PhysicalGroupSuit(owner, s.Cards), PhysicalGroupColor(owner, s.Cards)) &&
                !HasBeneficiarySuitShield(owner.Seat, p.Seat, PhysicalGroupSuit(owner, s.Cards))))
                .Select(p => p.Seat).Order().ToArray() :
        CurrentNextActualUseAdjustment(owner.Seat) is not null && NextActualUseSelectedProducer(plan) is { OutputKind: CardKind.Peach }
            ? _players.Where(p => p.IsAlive && p.Seat != owner.Seat && p.Hp < p.MaxHp && selections.Any(s =>
                !IsCardTargetProhibited(p, CardKind.Peach, PhysicalGroupSuit(owner, s.Cards), PhysicalGroupColor(owner, s.Cards)) &&
                !HasBeneficiarySuitShield(owner.Seat, p.Seat, PhysicalGroupSuit(owner, s.Cards))))
                .Select(p => p.Seat).Order().ToArray() : normal;

    private CommandError? ValidateNextActualUseProgramSelection(LegalAction action, ProgramExecutionPlan? plan,
        IReadOnlyList<int> cards, IReadOnlyList<int> targets)
    {
        if (plan?.Activation is not { } activation || targets.Count <= activation.MaxTargets) return null;
        var owner = _players[_currentSeat];
        if (HasHpLossSlashTargets(owner) && HpLossSlashProgramProducer(plan) is not null)
            return ValidateHpLossSlashProgramSelection(action, plan, cards, targets);
        var effect = NextActualUseSelectedProducer(plan);
        var source = effect is null ? null : new CardConversionSource(action.ProgramSkillId!, effect.SourceBind!, owner.Seat,
            GetRuntimeSkillInstanceId(owner, action.ProgramSkillId!));
        var selection = source is null ? null : FindProgramMultiCardViewAsSelection(owner, cards, effect!.OutputKind!.Value, false, source);
        if (effect is null || CurrentNextActualUseAdjustment(owner.Seat) is null || targets.Count != activation.MaxTargets + 1 ||
            selection is null || (effect.OutputKind is CardKind.Slash or CardKind.FireSlash
                ? targets.Any(t => !CanUseNextActualUseSelectedSlash(owner, _players[t], selection))
                : !NextActualUseProgramTargets(owner, plan, [], [selection]).Contains(targets[0])))
            return new(CommandErrorCode.InvalidTarget, "The next actual-use target adjustment lost its exact materials, source or legal targets.");
        return null;
    }

    private bool CanUseNextActualUseSelectedSlash(CharacterState owner, CharacterState target, ProgramMultiCardViewAsSelection selection) =>
        CanUseVirtualSlashTarget(owner, target, selection.OutputKind, PhysicalGroupSuit(owner, selection.Cards),
            PhysicalGroupColor(owner, selection.Cards), physicalCardIds: selection.Cards.Select(c => c.Id).ToArray()) &&
        !IsDirectedCardTargetProhibited(owner.Seat, target.Seat, selection.OutputKind) &&
        !IsCardTargetProhibited(target, selection.OutputKind, PhysicalGroupSuit(owner, selection.Cards), PhysicalGroupColor(owner, selection.Cards)) &&
        !HasBeneficiarySuitShield(owner.Seat, target.Seat, PhysicalGroupSuit(owner, selection.Cards));

    private ProgramNextActualUseAdjustment? FreezeNextActualUseProgramSelection(CharacterState owner,
        ProgramExecutionPlan plan, IReadOnlyList<int> targets)
    {
        if (targets.Count <= plan.Activation!.MaxTargets) return null;
        if (HasHpLossSlashTargets(owner) && HpLossSlashProgramProducer(plan) is not null) return null;
        var grant = CurrentNextActualUseAdjustment(owner.Seat) ?? throw new InvalidOperationException("The selected new adjustment expired before its producer started.");
        var effect = NextActualUseSelectedProducer(plan) ?? throw new InvalidOperationException("The producer cannot adjust targets.");
        return effect.OutputKind switch
        {
            CardKind.ArrowBarrage => new(grant, ProgramNextActualUseAdjustmentKind.RemoveGlobalTarget, CardKind.ArrowBarrage, owner.Seat, targets.Single()),
            CardKind.Peach => new(grant, ProgramNextActualUseAdjustmentKind.AddRecoveryTarget, CardKind.Peach, owner.Seat, targets.Single()),
            _ => new(grant, ProgramNextActualUseAdjustmentKind.AddSlashTarget, effect.OutputKind!.Value, targets[0], targets[1])
        };
    }

    private bool HasExactNextActualUseProgramSelection(ProgramSkillFrame frame, ProgramExecutionPlan plan)
    {
        if (frame.NextActualUseAdjustment is not { } receipt || NextActualUseSelectedProducer(plan) is not { } producer ||
            producer.OutputKind != receipt.OutputKind || receipt.Grant.Source.OwnerSeat != frame.OwnerSeat ||
            receipt.Grant.TurnNumber != _turnNumber || receipt.Grant.TurnOwnerSeat != _currentSeat ||
            !CompleteProgramEventHistory().OfType<NextActualUseTargetAdjustmentGrantedEvent>().Any(e => e == receipt.Grant)) return false;
        return receipt.Kind switch
        {
            ProgramNextActualUseAdjustmentKind.AddSlashTarget => frame.SelectedTargetSeats.SequenceEqual(
                receipt.Stage == ProgramNextActualUseAdjustmentStage.Canceled ? [receipt.OriginalTargetSeat] : new[] { receipt.OriginalTargetSeat, receipt.ChangedTargetSeat }),
            ProgramNextActualUseAdjustmentKind.RemoveGlobalTarget or ProgramNextActualUseAdjustmentKind.AddRecoveryTarget => frame.SelectedTargetSeats.SequenceEqual(
                receipt.Stage == ProgramNextActualUseAdjustmentStage.Canceled ? [] : new[] { receipt.ChangedTargetSeat }),
            _ => false
        };
    }

    private bool ApplyNextActualUseGlobalRemoval(ProgramSkillFrame frame, IReadOnlyList<int> normal, out IReadOnlyList<int> targets)
    {
        targets = normal;
        if (frame.NextActualUseAdjustment is not { Kind: ProgramNextActualUseAdjustmentKind.RemoveGlobalTarget } receipt) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        if (!HasExactNextActualUseProgramSelection(frame, plan)) throw new InvalidOperationException("Global removal lost its exact owning selection.");
        if (CurrentNextActualUseAdjustment(frame.OwnerSeat)?.ProgramFrameId == receipt.Grant.ProgramFrameId && normal.Contains(receipt.ChangedTargetSeat))
            targets = Array.AsReadOnly(normal.Where(t => t != receipt.ChangedTargetSeat).ToArray());
        else ReplaceRuntimeFrame(frame.Id, frame with { SelectedTargetSeats = [], NextActualUseAdjustment = receipt with { Stage = ProgramNextActualUseAdjustmentStage.Canceled } });
        return true;
    }

    private SkillProgramStepOutcome BeginNextActualUseSelectedSlash(ProgramSkillFrame frame, ProgramMultiCardViewAsSelection selection)
    {
        var receipt = frame.NextActualUseAdjustment ?? throw new InvalidOperationException("Adjusted Slash requires its owning producer receipt.");
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        if (receipt.Kind != ProgramNextActualUseAdjustmentKind.AddSlashTarget || !HasExactNextActualUseProgramSelection(frame, plan))
            throw new InvalidOperationException("Adjusted material Slash lost its exact current producer.");
        var owner = _players[frame.OwnerSeat];
        if (CurrentNextActualUseAdjustment(owner.Seat)?.ProgramFrameId != receipt.Grant.ProgramFrameId ||
            !CanUseNextActualUseSelectedSlash(owner, _players[receipt.ChangedTargetSeat], selection))
        {
            ReplaceRuntimeTop(frame with { SelectedTargetSeats = [receipt.OriginalTargetSeat], NextActualUseAdjustment = receipt with { Stage = ProgramNextActualUseAdjustmentStage.Canceled } });
            if (!CanUseNextActualUseSelectedSlash(owner, _players[receipt.OriginalTargetSeat], selection)) return SkillProgramStepOutcome.Continue;
            ResolveSlashCore(owner, _players[receipt.OriginalTargetSeat], selection.Cards[0], selection.OutputKind,
                owner.Seat, physicalCards: selection.Cards, conversionSource: selection.Source);
            return SkillProgramStepOutcome.AwaitChild;
        }
        if (!CanUseNextActualUseSelectedSlash(owner, _players[receipt.OriginalTargetSeat], selection))
            throw new InvalidOperationException("The original Slash target became illegal before payment.");
        var seats = Array.AsReadOnly(new[] { receipt.OriginalTargetSeat, receipt.ChangedTargetSeat });
        BeginNextActualUseMaterialSlash(owner, selection.Cards, selection.OutputKind, seats, selection.Source, receipt.Grant, frame);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private void BeginNextActualUseMaterialSlash(CharacterState owner, IReadOnlyList<Card> cards, CardKind kind,
        IReadOnlyList<int> seats, CardConversionSource? source, NextActualUseTargetAdjustmentGrantedEvent grant, ProgramSkillFrame? frame)
    {
        var armor = HasArmorBypass(owner);
        var id = BeginCardUse(cards[0], owner.Seat, seats, kind,
            ignoresArmor: armor, physicalCardIds: cards.Select(c => c.Id).ToArray(), conversionSource: source, isTrueZhangbaSlash: frame is null);
        var typedReturn = new ProgramAdjustedSlashReturn(frame?.Id, grant.ProgramFrameId, id, frame?.GameplayHash, frame?.SkillInstanceId, frame is null,
            CardMovements.Count == 0 ? 1 : CardMovements[^1].Sequence + 1);
        UpdateLifecycleCardUse(id, use => use with { AdjustedSlashReturn = typedReturn, TargetsAdjusted = true });
        if (frame?.NextActualUseAdjustment is { } receipt)
            ReplaceRuntimeFrame(frame.Id, frame with { NextActualUseAdjustment = receipt with { Stage = ProgramNextActualUseAdjustmentStage.Applied, CardUseFrameId = id } });
        var nuzhan = GetNuzhanModifiers(id, owner);
        foreach (var card in cards) MoveCard(card, FindOwnedCardLocation(owner, card), CardLocation.Processing, CardMoveReasons.Use);
        typedReturn = typedReturn with { PaidMovementLastSequence = CardMovements[^1].Sequence };
        UpdateLifecycleCardUse(id, use => use with { AdjustedSlashReturn = typedReturn });
        var counted = !(LifecycleCardUse(id)?.UnlimitedUse == true) && !nuzhan.IgnoresSlashLimit && !IgnoresProgramSlashLimit(owner, source) &&
            _phase == TurnPhase.Play && owner.Seat == _currentSeat;
        if (counted) RecordSlashUseDebit(id, owner.Seat);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(owner.Seat, kind);
        var damage = (owner.HasAlcoholEffect ? 2 : 1) + nuzhan.DamageBonus;
        CaptureProgramAlcoholConsumption(id, owner); owner.HasAlcoholEffect = false;
        var pending = new FangtianHalberdHandle(this, id, owner.Seat, cards[0], kind,
            armor, damage, seats, false, counted, source);
        ActiveFangtianHalberd = pending;
        if (source is not null)
            AdvanceEventRulesAndQueueFact(new ProgramViewAsConvertedEvent(id, source.SkillId, source.BindingId,
                owner.Seat, Array.AsReadOnly(cards.Select(c => c.Id).ToArray()), kind, IsUse: true, seats));
        else AdvanceEventRulesAndQueueFact(new ZhangbaSerpentSpearConvertedEvent(id, owner.Seat,
            Array.AsReadOnly(cards.Select(c => c.Id).ToArray()), IsUse: true, seats[0]));
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(cards[0].Id, kind, owner.Seat, seats[0], IgnoresArmor: armor));
        foreach (var seat in seats) NotifyAiOfSlash(owner, _players[seat]);
        BeginNextFangtianHalberdTarget(pending);
    }

    private CommandError? ValidateNextActualUseZhangba(CharacterState owner, IReadOnlyList<int> ids, IReadOnlyList<int> targets)
    {
        if (targets.Count <= 1) return null;
        if (HasHpLossSlashTargets(owner)) return ValidateHpLossSlashZhangba(owner, ids, targets);
        var cards = GetZhangbaSlashPairs(owner).FirstOrDefault(pair => pair.Select(c => c.Id).Order().SequenceEqual(ids.Order()));
        if (targets.Count != 2 || CurrentNextActualUseAdjustment(owner.Seat) is null || !CanUseZhangbaSerpentSpear(owner) || cards is null ||
            targets.Any(t => !CanUseVirtualSlashTarget(owner, _players[t], CardKind.Slash, PhysicalGroupSuit(owner, cards),
                PhysicalGroupColor(owner, cards), ZhangbaSpecificSlashRank(owner, cards), ids) ||
                IsDirectedCardTargetProhibited(owner.Seat, t, CardKind.Slash) ||
                IsCardTargetProhibited(_players[t], CardKind.Slash, PhysicalGroupSuit(owner, cards), PhysicalGroupColor(owner, cards)) ||
                HasBeneficiarySuitShield(owner.Seat, t, PhysicalGroupSuit(owner, cards))))
            return new(CommandErrorCode.InvalidTarget, "The adjusted Zhangba Slash lost its exact two materials and legal targets.");
        return null;
    }

    private void ResolveNextActualUseZhangba(CharacterState owner, IReadOnlyList<int> targets, IReadOnlyList<Card> cards)
    {
        if (ValidateNextActualUseZhangba(owner, cards.Select(c => c.Id).ToArray(), targets) is { } error)
            throw new InvalidOperationException(error.Message);
        if (HasHpLossSlashTargets(owner)) { ResolveHpLossSlashZhangba(owner, targets, cards); return; }
        BeginNextActualUseMaterialSlash(owner, cards, CardKind.Slash, targets, null,
            CurrentNextActualUseAdjustment(owner.Seat) ?? throw new InvalidOperationException("Adjusted spear source expired before payment."), null);
    }

    private void AssertNextActualUseProgramSelection(ProgramSkillFrame frame, ProgramExecutionPlan plan)
    {
        if (frame.NextActualUseAdjustment is not { } receipt) return;
        if (!HasExactNextActualUseProgramSelection(frame, plan) || !IsValidPlayerSeat(receipt.OriginalTargetSeat) ||
            !IsValidPlayerSeat(receipt.ChangedTargetSeat) || receipt.OriginalTargetSeat == receipt.ChangedTargetSeat ||
            receipt.Stage == ProgramNextActualUseAdjustmentStage.Applied && (receipt.CardUseFrameId is null ||
                !CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().Any(e =>
                    e.ResolutionId == receipt.CardUseFrameId && e.SourceSeat == frame.OwnerSeat && e.CardKind == receipt.OutputKind)))
            throw new InvalidOperationException("Next actual-use selection lost its exact issuance, producer or actual child.");
    }

    private void PrepareNextActualUseSelectedRecovery(ProgramSkillFrame frame, ProgramMultiCardViewAsSelection selection)
    {
        if (frame.NextActualUseAdjustment is not { Kind: ProgramNextActualUseAdjustmentKind.AddRecoveryTarget } receipt) return;
        var owner = _players[frame.OwnerSeat];
        if (!HasExactNextActualUseProgramSelection(frame, ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)))
            throw new InvalidOperationException("Recovery adjustment lost its exact material producer.");
        var target = _players[receipt.ChangedTargetSeat];
        if (CurrentNextActualUseAdjustment(owner.Seat)?.ProgramFrameId != receipt.Grant.ProgramFrameId ||
            !target.IsAlive || target.Hp >= target.MaxHp || IsCardTargetProhibited(target, CardKind.Peach,
                PhysicalGroupSuit(owner, selection.Cards), PhysicalGroupColor(owner, selection.Cards)) ||
            HasBeneficiarySuitShield(owner.Seat, target.Seat, PhysicalGroupSuit(owner, selection.Cards)))
        { ReplaceRuntimeTop(frame with { SelectedTargetSeats = [], NextActualUseAdjustment = receipt with { Stage = ProgramNextActualUseAdjustmentStage.Canceled } }); return; }
        _selectedNextCardTargetSeats = Array.AsReadOnly(new[] { owner.Seat, target.Seat });
    }

    private void MarkNextActualUseProgramChild(CardUseFrame use)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
        if (index <= 0 || _resolutionStack[index - 1] is not ProgramSkillFrame parent ||
            parent.NextActualUseAdjustment is not { Stage: ProgramNextActualUseAdjustmentStage.Selected } receipt ||
            parent.OwnerSeat != use.SourceSeat || receipt.OutputKind != use.CardKind) return;
        var plan = ProgramInstructionResolver.Default.Resolve(parent, _contentRegistry.GetSkill(parent.SkillId).Program!);
        if (!HasExactNextActualUseProgramSelection(parent, plan) ||
            plan.GetPausedInstruction(parent.InstructionIndex).Effect.Op != SkillProgramEffectOp.UseSelectedCardsAs ||
            use.Action is not { Type: CardActionType.Use } action || action.ActorSeat != parent.OwnerSeat || action.ProviderSeat != parent.OwnerSeat ||
            use.PhysicalCardIds is null || !use.PhysicalCardIds.Order().SequenceEqual(parent.SelectedCardIds.Order()) ||
            !action.PhysicalCards.Select(c => c.CardId).SequenceEqual(use.PhysicalCardIds) ||
            action.ConversionChain is not [var conversion] || conversion.SkillId != parent.SkillId ||
            conversion.SkillInstanceId != parent.SkillInstanceId || conversion.OwnerSeat != parent.OwnerSeat ||
            conversion.BindingId != plan.GetPausedInstruction(parent.InstructionIndex).Effect.SourceBind)
            throw new InvalidOperationException("The material-use child lost its exact adjusted producer and frozen actual costs.");
        ReplaceRuntimeFrame(parent.Id, parent with { NextActualUseAdjustment = receipt with {
            Stage = ProgramNextActualUseAdjustmentStage.Applied, CardUseFrameId = use.Id } });
    }

    private IReadOnlyList<ProgramOrdinaryTrickUseOption> NextActualUseOrdinaryTrickOptions(CharacterState actor,
        IReadOnlyList<ProgramOrdinaryTrickUseOption> ordinary, Suit? suit, bool? color, Suit? shieldSuit, bool excludeOwner)
    {
        if (CurrentNextActualUseAdjustment(actor.Seat) is null) return ordinary;
        var options = ordinary.ToList();
        var keys = ordinary.Select(o => (o.EffectiveCardKind, o.TargetCardId, Targets: string.Join(",", o.TargetSeats))).ToHashSet();
        foreach (var option in ordinary)
        {
            var kind = option.EffectiveCardKind;
            var normal = option.TargetSeats.Count == 0 && kind == CardKind.DrawTwo ? new[] { actor.Seat } : option.TargetSeats.ToArray();
            if (normal.Length == 0) continue;
            if (kind == CardKind.BorrowedSword)
            {
                foreach (var holder in _players.Where(p => p.IsAlive && p.Seat != actor.Seat &&
                    !normal.Where((_, i) => i % 2 == 0).Contains(p.Seat) && GetWeapon(p) is not null &&
                    !IsDirectedCardTargetProhibited(actor.Seat, p.Seat, kind) && !IsCardTargetProhibited(p, kind, suit, color) &&
                    !HasBeneficiarySuitShield(actor.Seat, p.Seat, shieldSuit)))
                foreach (var victim in _players.Where(p => IsLegalBorrowedSwordSlashTarget(holder, p))) Add([..normal, holder.Seat, victim.Seat]);
                if (normal.Length >= 4) for (var i = 0; i < normal.Length; i += 2) Add(normal.Where((_, j) => j != i && j != i + 1).ToArray());
                continue;
            }
            if (kind is not (CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.PeachGarden or CardKind.FiveGrains))
                foreach (var target in _players.Where(p => p.IsAlive && (!excludeOwner || p.Seat != actor.Seat) &&
                    !normal.Contains(p.Seat) && !IsDirectedCardTargetProhibited(actor.Seat, p.Seat, kind) &&
                    !IsCardTargetProhibited(p, kind, suit, color) && !HasBeneficiarySuitShield(actor.Seat, p.Seat, shieldSuit) &&
                    (kind switch {
                        CardKind.DrawTwo or CardKind.IronChain => true,
                        CardKind.Duel => p.Seat != actor.Seat,
                        CardKind.FireAttack => GetHand(p).Count > 0,
                        CardKind.Snatch or CardKind.Dismantlement => p.Seat != actor.Seat && GetHand(p).Count + GetEquipment(p).Count + GetJudgment(p).Count > 0,
                        _ => false }))) Add([..normal, target.Seat]);
            if (normal.Length > 1) foreach (var target in normal) Add(normal.Where(t => t != target).ToArray());
            void Add(IReadOnlyList<int> targets)
            {
                if (targets.Count == 0 || !keys.Add((kind, option.TargetCardId, string.Join(",", targets)))) return;
                options.Add(option with { Id = new(option.Id.Value + ".next-actual.targets-" + string.Join('-', targets)),
                    TargetSeats = Array.AsReadOnly(targets.ToArray()), NextActualUseAdjusted = true,
                    Description = option.Description + "（巧说目标调整）" });
            }
        }
        return Array.AsReadOnly(options.ToArray());
    }

    private bool IsNextActualUseTrickForbidden(int seat, CardKind kind) =>
        _turnCardUseEffects.ActionProhibitions.Any(p => p.TurnNumber == _turnNumber && p.TurnSeat == _currentSeat &&
            p.Source.OwnerSeat == seat && p.ActionTypes.Contains(CardActionType.Use) && p.CardKinds.Contains(kind) &&
            _contentRegistry.GetSkill(p.Source.SkillId).Program?.Activations.Any(a =>
                a.Effects.Any(e => e.Op == SkillProgramEffectOp.GrantNextActualUseTargetAdjustment)) == true);

    private long? NextActualUseAdjustedSlashParent(CardAttackHandle attack)
    {
        if (GetCardAttackState(attack.ResolutionId).AdjustedSlashReturn is not { } typed) return null;
        if (typed.IsZhangba && typed.ParentProgramFrameId is null && typed.ParentGameplayHash is null && typed.ParentSkillInstanceId is null) return null;
        var parent = _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(p => p.Id == typed.ParentProgramFrameId);
        if (typed.CardUseFrameId != attack.ResolutionId || parent is null || parent.GameplayHash != typed.ParentGameplayHash ||
            parent.SkillInstanceId != typed.ParentSkillInstanceId || parent.OwnerSeat != attack.SourceSeat ||
            parent.NextActualUseAdjustment is not { Stage: ProgramNextActualUseAdjustmentStage.Applied } receipt ||
            receipt.CardUseFrameId != typed.CardUseFrameId || receipt.Grant.ProgramFrameId != typed.GrantProgramFrameId ||
            !HasExactNextActualUseProgramSelection(parent, ProgramInstructionResolver.Default.Resolve(parent, _contentRegistry.GetSkill(parent.SkillId).Program!)))
            throw new InvalidOperationException("Adjusted Slash completion lost its exact typed original producer.");
        return parent.Id;
    }

    // PopFinishedCardUse's older selected-card hook normally returns directly.
    // The new multi-target Slash already captured its typed completion parent;
    // leave that exact parent for CompleteAttackAfterCardResolution to resume
    // once, including after a Completed observer window. No parentless equipment
    // use or older selected-material activation changes its return path.
    private bool HasExactNextActualUseAdjustedProgramReturn(CardUseFrame use)
    {
        if (use.AdjustedSlashReturn is not { IsZhangba: false, ParentProgramFrameId: { } parentId } typed) return false;
        var parent = _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == parentId);
        if (parent is null || typed.CardUseFrameId != use.Id || parent.OwnerSeat != use.SourceSeat ||
            parent.GameplayHash != typed.ParentGameplayHash || parent.SkillInstanceId != typed.ParentSkillInstanceId ||
            use.CardAttack?.AdjustedSlashReturn != typed || use.CardKind is not (CardKind.Slash or CardKind.FireSlash) ||
            parent.NextActualUseAdjustment is not { Stage: ProgramNextActualUseAdjustmentStage.Applied } receipt ||
            receipt.CardUseFrameId != use.Id || receipt.Grant.ProgramFrameId != typed.GrantProgramFrameId ||
            !HasExactNextActualUseProgramSelection(parent, ProgramInstructionResolver.Default.Resolve(parent, _contentRegistry.GetSkill(parent.SkillId).Program!)))
            throw new InvalidOperationException("The completed adjusted Slash cannot bypass the older return without its exact already-captured parent.");
        return true;
    }

    private bool IsExactNextActualUseMaterialSlash(FangtianHalberdHandle multi, CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { AdjustedSlashReturn: { } typed, Action: { Type: CardActionType.Use } action } use ||
            typed.CardUseFrameId != use.Id || GetCardAttackState(use.Id).AdjustedSlashReturn != typed ||
            multi.ResolutionId != use.Id || multi.SourceSeat != use.SourceSeat ||
            action.ActorSeat != use.SourceSeat || action.ProviderSeat != use.SourceSeat || action.EffectiveKind != use.CardKind ||
            attack.EffectiveCardKind != use.CardKind || multi.ConversionSource != action.ConversionChain.SingleOrDefault() ||
            use.PhysicalCardIds is not { Count: >= 2 } cards ||
            !cards.SequenceEqual(attack.PhysicalCards.Select(c => c.Id)) ||
            !cards.SequenceEqual(action.PhysicalCards.Select(c => c.CardId)) || cards[0] != multi.Card.Id ||
            typed.PaidMovementFirstSequence <= 0 || typed.PaidMovementLastSequence < typed.PaidMovementFirstSequence ||
            action.PhysicalCards.Any(cost => CardMovements.Count(m => m.Sequence >= typed.PaidMovementFirstSequence &&
                m.Sequence <= typed.PaidMovementLastSequence && m.CardId == cost.CardId && m.From == cost.From &&
                m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use && m.TurnNumber == _turnNumber) != 1) ||
            !CompleteProgramEventHistory().OfType<NextActualUseTargetAdjustmentConsumedEvent>().Any(e =>
                e.GrantProgramFrameId == typed.GrantProgramFrameId && e.OriginFrameId == use.Id && e.CardActionId == action.ActionId &&
                e.Source.OwnerSeat == action.ActorSeat && e.TurnNumber == _turnNumber && e.TurnOwnerSeat == _currentSeat && !e.IsNullificationUse)) return false;
        if (typed.IsZhangba)
        {
            if (typed.ParentProgramFrameId is not null || typed.ParentGameplayHash is not null || typed.ParentSkillInstanceId is not null ||
                cards.Count != 2 || action.ConversionChain.Count != 0 || action.EffectiveKind != CardKind.Slash ||
                !CompleteProgramEventHistory().OfType<TargetsConfirmedEvent>().Any(e => e.ResolutionId == use.Id && e.TargetSeats.SequenceEqual(multi.TargetSeats)) ||
                !CompleteProgramEventHistory().OfType<ZhangbaSerpentSpearConvertedEvent>()
                    .Any(e => e.ResolutionId == use.Id && e.UserSeat == action.ActorSeat && e.IsUse && e.PhysicalCardIds.SequenceEqual(cards))) return false;
        }
        else if (NextActualUseAdjustedSlashParent(attack) is not { } parentId ||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(p => p.Id == parentId) is not { } parent ||
            !cards.Order().SequenceEqual(parent.SelectedCardIds.Order()) ||
            !multi.TargetSeats.SequenceEqual(new[] { parent.NextActualUseAdjustment!.OriginalTargetSeat, parent.NextActualUseAdjustment.ChangedTargetSeat })) return false;
        var retained = cards.Where(id => !IsCurrentUsePhysicalCardClaim(use.Id, id)).ToHashSet();
        return processing.All(c => retained.Contains(c.Id)) && retained.All(id =>
            _cardZones.GetLocation(id).Zone is CardZoneKind.Processing or CardZoneKind.Hand or CardZoneKind.DrawPile or CardZoneKind.DiscardPile);
    }
}
