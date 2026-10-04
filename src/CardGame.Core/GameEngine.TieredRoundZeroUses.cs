namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IEnumerable<(CardConversionSource Source, SkillProgramViewAs Rule, string Hash)> TieredRoundZeroRules(
        CharacterState owner, CardKind? output = null, bool responseUse = false, bool dyingUse = false)
    {
        foreach (var instance in GetSkillBindingShard(owner).ProgramInstances)
        foreach (var rule in instance.Program.ViewAs.Where(r => r.InputCount == 0 && r.TieredRoundConversion is not null))
            if ((output is null || rule.OutputKind == output) && rule.Condition.Evaluate(CreateSkillContext(owner)) &&
                (dyingUse ? !rule.NoDying && (rule.ForResponse || rule.ForPlay && rule.UseOnly) : responseUse ? rule.ForResponse : rule.ForPlay) &&
                CanUseTieredRoundConversion(owner, instance, rule, responseUse, dyingUse) &&
                !IsCardUseForbidden(owner.Seat, rule.OutputKind, CardActionType.Use))
                yield return (new(instance.SkillId, rule.Id, owner.Seat, instance.SkillInstanceId), rule, instance.Program.GameplayHash);
    }

    private IReadOnlyList<LegalAction> TieredRoundZeroPlayActions(CharacterState actor)
    {
        if (_phase != TurnPhase.Play || actor.Seat != _currentSeat || !actor.IsAlive) return [];
        var result = new List<LegalAction>();
        foreach (var (source, rule, _) in TieredRoundZeroRules(actor))
        {
            var kind = rule.OutputKind; var card = new Card(0, kind, Suit.None, 0);
            if (IsSlashCard(kind))
            {
                var targets = GetFangtianOrderedSlashTargets(actor, card, source, effectiveKind: kind);
                foreach (var target in targets)
                    result.Add(new(LegalActionKind.Slash, 0, target.Seat, DescribeConversion(source,
                        $"视为使用【{card.DisplayName}】对 {target.Name} 使用"), kind) { ConversionSource = source });
                // The normal target-count policy can offer a real multi-target
                // action; it does not turn a zero-material use into a last-hand
                // Fangtian payment.
                AddProgramTargetCountSlashActions(result, actor, card, targets, card.DisplayName, kind, source, null);
            }
            else if (kind == CardKind.Peach && actor.Hp < actor.MaxHp && !HasSelfCardTargetProhibition(actor.Seat) &&
                !HasBeneficiarySuitShield(actor.Seat, actor.Seat, Suit.None))
                result.Add(new(LegalActionKind.Peach, 0, null, DescribeConversion(source, "视为使用【桃】"), kind) { ConversionSource = source });
            else if (kind == CardKind.Alcohol && !actor.HasAlcoholEffect && !HasSelfCardTargetProhibition(actor.Seat) &&
                (!actor.UsedPlayPhaseAlcoholThisTurn || HasNextUnlimitedCard(actor) ||
                 HasCardPolicy(actor, SkillProgramCardPolicyKind.UnlimitedAlcoholUse, CardKind.Alcohol)) &&
                !HasBeneficiarySuitShield(actor.Seat, actor.Seat, Suit.None))
                result.Add(new(LegalActionKind.Alcohol, 0, null, DescribeConversion(source, "视为使用【酒】"), kind) { ConversionSource = source });
            else if (IsOrdinaryTrick(kind) && kind != CardKind.Nullification)
                foreach (var option in BuildProgramOrdinaryTrickUseOptions(actor, kind, Suit.None,
                    enforceUsePermission: true, beneficiaryShieldSuit: Suit.None, actualEffectiveColor: null, hasActualColor: true,
                    physicalCardIds: [], includeNextActualUseAdjustment: true))
                    result.Add(new(option.ActionKind, 0, option.TargetSeats.Count == 1 ? option.TargetSeats[0] : null,
                        DescribeConversion(source, option.Description), kind, option.TargetCardId, option.TargetSeats)
                    { ConversionSource = source });
        }
        return Array.AsReadOnly(result.Select(action => action with
        { TieredRoundZeroUse = ViewAsRule(action.ConversionSource!)!.TieredRoundConversion }).ToArray());
    }

    private bool TrySubmitTieredRoundZeroPlay(PlayCardCommand command, out CommandResult result)
    {
        result = null!;
        if (command.CardId != 0 || command.ConversionSource is not { } source || ViewAsRule(source)?.TieredRoundConversion is null) return false;
        var actor = _players[command.ActorSeat]; var targets = command.TargetSeats ?? [];
        var action = TieredRoundZeroPlayActions(actor).SingleOrDefault(a => a.ConversionSource == source &&
            a.PlayedCardKind == command.PlayedCardKind && a.TargetCardId == command.TargetCardId && a.TargetSeats.SequenceEqual(targets));
        if (action is null || command.AdditionalConversionSources is { Count: > 0 })
        { result = Reject(CommandErrorCode.IllegalAction, "The zero-material use changed its published source, card name or exact legal targets."); return true; }
        result = Accept(() =>
        {
            ClearPendingDecision(); SelectUseConversion(action); ExecuteTieredRoundZeroPlay(actor, action);
            AdvanceRulesAndPublishState(); if (_options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary();
        }); return true;
    }

    private IReadOnlyList<PromptChoice> TieredRoundZeroForcedSlashChoices(CharacterState actor, CharacterState target,
        long originalId, bool qinglong)
    {
        if (qinglong ? ActiveQinglongCrescentBlade is not { } blade || blade.Attack.ResolutionId != originalId ||
                blade.Attack.SourceSeat != actor.Seat || blade.Attack.TargetSeat != target.Seat || !SameAttackOwner(ActiveCardAttack, blade.Attack) :
            ActiveBorrowedSword is not { } borrowed || borrowed.ResolutionId != originalId || borrowed.WeaponOwnerSeat != actor.Seat ||
                borrowed.SlashTargetSeat != target.Seat || borrowed.ActiveAttack is not null || GetWeapon(actor) is null)
            return [];
        var result = new List<PromptChoice>();
        foreach (var (source, rule, _) in TieredRoundZeroForcedSlashRules(actor, target, qinglong))
        {
            var kind = rule.OutputKind;
            var parameters = new Dictionary<string, string> { [qinglong ? "action" : "response"] = "tiered-round-zero-forced-slash",
                ["output-kind"] = kind.ToString(), ["original-frame-id"] = originalId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["forced-use"] = qinglong ? "qinglong" : "borrowed-sword" };
            AddConversionParameters(parameters, source);
            result.Add(new(new ChoiceId($"tiered-zero.forced-{parameters["forced-use"]}.{originalId}.{source.BindingId}.{source.SkillInstanceId}"),
                $"发动【{ProgramConversionName(source)}】，视为使用【{CardCatalog.Get(kind).DisplayName}】攻击 {target.Name}。", [], [target.Seat], parameters));
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private IEnumerable<(CardConversionSource Source, SkillProgramViewAs Rule, string Hash)> TieredRoundZeroForcedSlashRules(
        CharacterState actor, CharacterState target, bool qinglong) => TieredRoundZeroRules(actor).Where(r => IsSlashCard(r.Rule.OutputKind) &&
        (qinglong ? CanUseQinglongCrescentBladeTarget(actor, target, r.Rule.OutputKind, null, false, Suit.None, 0) :
            IsLegalBorrowedSwordSlashTarget(actor, target, r.Rule.OutputKind, Suit.None, false, null, 0, [])) &&
        !HasBeneficiarySuitShield(actor.Seat, target.Seat, Suit.None) && !IsCardTargetProhibited(target, r.Rule.OutputKind, Suit.None));

    private bool TryResolveTieredRoundZeroForcedSlash(PromptChoice selected, bool advance)
    {
        if (selected.Parameters.GetValueOrDefault("forced-use") is not ("borrowed-sword" or "qinglong")) return false;
        var qinglong = selected.Parameters["forced-use"] == "qinglong";
        if (_pendingDecision is not { } prompt || prompt.Kind != (qinglong ? DecisionKind.QinglongCrescentBlade : DecisionKind.RespondSlash) ||
            !prompt.Choices.Any(c => AssistedChoicesEqual([c], [selected])) || selected.Cards.Count != 0 || selected.Targets is not [var targetSeat] ||
            !long.TryParse(selected.Parameters.GetValueOrDefault("original-frame-id"), out var originalId) ||
            !Enum.TryParse<CardKind>(selected.Parameters.GetValueOrDefault("output-kind"), out var kind))
            throw new InvalidOperationException("A zero forced Slash changed its exact published producer, target or name.");
        var actor = _players[prompt.PlayerSeat]; var target = _players[targetSeat];
        if (!TieredRoundZeroForcedSlashChoices(actor, target, originalId, qinglong).Any(c => AssistedChoicesEqual([c], [selected])))
            throw new InvalidOperationException("A forced zero Slash lost its original legal same-target use and shared allowance.");
        var conversion = RequireConversionSource(selected); CaptureSelectedResponseConversion(selected);
        if (qinglong)
        {
            var old = ActiveQinglongCrescentBlade!.Attack;
            ActiveQinglongCrescentBlade = null; ClearPendingDecision();
            SetCardUseStep(old.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            AdvanceEventRulesAndQueueFact(new QinglongCrescentBladeResolvedEvent(old.ResolutionId, actor.Seat, target.Seat,
                Used: true, SlashCardIds: [], EffectiveSlashKind: kind));
            CompleteAttack(old); SuspendContinuationForQinglongFollowup(old.ResolutionId);
            BeginTieredRoundForcedZeroSlash(actor, target, kind, conversion, null, false);
        }
        else
        {
            var original = ActiveBorrowedSword!;
            PopResponseWindow(original.ResolutionId); ClearPendingDecision();
            SetCardUseStep(original.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            original.AwaitingSlashChoice = false; original.SlashCardId = 0; original.EffectiveSlashKind = kind;
            BeginTieredRoundForcedZeroSlash(actor, target, kind, conversion, original, true);
        }
        AdvanceRulesAndPublishState(); if (advance) AdvanceToHumanBoundary(); return true;
    }

    private void BeginTieredRoundForcedZeroSlash(CharacterState actor, CharacterState target, CardKind kind,
        CardConversionSource source, BorrowedSwordHandle? borrowed, bool countsTowardLimit)
    {
        var card = new Card(0, kind, Suit.None, 0);
        var id = BeginCardUse(card, actor.Seat, [target.Seat], kind,
            ignoresArmor: HasCardArmorBypass(actor, target, kind), physicalCardIds: [], conversionSource: source);
        var attack = new CardAttackHandle(this, id, actor.Seat, target.Seat, card: null,
            damageAmount: actor.HasAlcoholEffect ? 2 : 1, playedCardKind: kind,
            ignoresArmor: HasCardArmorBypass(actor, target, kind));
        if (borrowed is not null) borrowed.ActiveAttack = attack;
        ActiveCardAttack = attack; CaptureProgramAdjustedSlashBaseDamage(attack);
        if (countsTowardLimit && _phase == TurnPhase.Play && actor.Seat == _currentSeat &&
            LifecycleCardUse(id)?.UnlimitedUse != true && !IgnoresProgramSlashLimit(actor, source)) RecordSlashUseDebit(id, actor.Seat);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, kind);
        CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false;
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, kind, actor.Seat, target.Seat, IgnoresArmor: attack.IgnoresArmor));
        NotifyAiOfSlash(actor, target);
        if (!TryMarkProgramUseCommitted(id) || !TryBeginProgramCardWindow(attack, LifecycleCardUse(id)!.Action!,
            SkillProgramTriggerWindow.CardUseCommitted, [target.Seat], ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
    }

    private bool TryExecuteTieredRoundZeroPlay(CharacterState actor, LegalAction action)
    {
        if (action.CardId != 0 || action.ConversionSource is not { } source || ViewAsRule(source)?.TieredRoundConversion is null) return false;
        if (!TieredRoundZeroPlayActions(actor).Any(a => a.Kind == action.Kind && a.ConversionSource == source &&
            a.PlayedCardKind == action.PlayedCardKind && a.TargetCardId == action.TargetCardId && a.TargetSeats.SequenceEqual(action.TargetSeats)))
            throw new InvalidOperationException("A zero-material use lost its published legal action before acceptance.");
        ExecuteTieredRoundZeroPlay(actor, action); return true;
    }

    private void ExecuteTieredRoundZeroPlay(CharacterState actor, LegalAction action)
    {
        var source = action.ConversionSource!; var kind = action.PlayedCardKind!.Value; var card = new Card(0, kind, Suit.None, 0);
        if (kind == CardKind.Peach)
        { ResolveRecoveryCard(actor, actor, card, "桃", CardKind.Peach, conversionSource: source, physicalCards: []); return; }
        // Resolve the published option before BeginCardUse consumes any
        // one-use target adjustment or quota and freezes its accepted action.
        var option = IsOrdinaryTrick(kind) ? BuildProgramOrdinaryTrickUseOptions(actor, kind, Suit.None, enforceUsePermission: true,
            beneficiaryShieldSuit: Suit.None, actualEffectiveColor: null, hasActualColor: true, physicalCardIds: [],
            includeNextActualUseAdjustment: true).Single(o => o.ActionKind == action.Kind && o.TargetCardId == action.TargetCardId && o.TargetSeats.SequenceEqual(action.TargetSeats)) : null;
        var id = BeginCardUse(card, actor.Seat, action.TargetSeats, kind, physicalCardIds: [], conversionSource: source);
        if (kind == CardKind.Alcohol)
        { actor.UsedPlayPhaseAlcoholThisTurn = true; BeginSimpleCardUse(id, new(0, SimpleCardUseEffect.Alcohol)); return; }
        if (IsSlashCard(kind))
        {
            var targets = LifecycleCardUse(id)!.TargetSeats;
            if (targets.Count == 0) throw new InvalidOperationException("A virtual Slash requires its real selected targets.");
            var attack = new CardAttackHandle(this, id, actor.Seat, targets[0], card: null,
                damageAmount: actor.HasAlcoholEffect ? 2 : 1, playedCardKind: kind,
                ignoresArmor: HasCardArmorBypass(actor, _players[targets[0]], kind));
            CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false; ActiveCardAttack = attack;
            CaptureProgramAdjustedSlashBaseDamage(attack);
            if (!IgnoresProgramSlashLimit(actor, source) && LifecycleCardUse(id)?.UnlimitedUse != true) RecordSlashUseDebit(id, actor.Seat);
            MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, kind);
            AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, kind, actor.Seat, targets[0]));
            TryMarkProgramUseCommitted(id);
            if (!TryBeginProgramCardWindow(attack, LifecycleCardUse(id)!.Action!, SkillProgramTriggerWindow.CardUseCommitted,
                targets, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
            return;
        }
        BeginJizhiOrNullificationWindow(id, card, actor.Seat, LifecycleCardUse(id)!.TargetSeats,
            option!.ActionKind, option.TargetCardId, option.RequiredCardKind, kind);
    }

    // Called between the real Push(CardUse) and CardUseDeclared. CaptureCardUseAction
    // has already accepted the conversion and consumed its exact allowance once.
    private void IssueTieredRoundConversionUse(CardUseFrame use)
    {
        if (use.Action is not { Type: CardActionType.Use } action || action.ConversionChain.FirstOrDefault(s =>
            ViewAsRule(s)?.TieredRoundConversion is not null) is not { } source) return;
        var rule = ViewAsRule(source)!; var policy = rule.TieredRoundConversion!;
        if (source.OwnerSeat != use.SourceSeat || action.ActorSeat != use.SourceSeat || action.ProviderSeat != use.SourceSeat ||
            action.RequesterSeat is not null || action.ConversionChain.Count != 1 || action.EffectiveKind != rule.OutputKind ||
            action.PhysicalCards.Count != rule.InputCount || source.SkillInstanceId is null ||
            _contentRegistry.GetSkill(source.SkillId).Program is not { } program)
            throw new InvalidOperationException("A tiered use lost its actual actor/provider/material and conversion tuple.");
        var receipt = new TieredRoundConversionUseReceipt(source, program.GameplayHash, policy.StateId, policy.UsageId,
            TieredRoundTier(use.SourceSeat, policy.StateId), _roundNumber, _turnNumber, _turnProgression.OwnerSeat,
            _cardUseDebitPhaseInstanceId, action.ActionId, use.Id, use.SourceSeat, use.CardKind, action.PhysicalCards.Count,
            use.CardKind is CardKind.Peach or CardKind.Alcohol && ActiveDying is { } dying && dying.ResponderSeat == use.SourceSeat &&
                use.TargetSeats.SequenceEqual([dying.VictimSeat]) ? dying.FrameId : null);
        ReplaceRuntimeFrame(use.Id, use with { TieredRoundConversionUse = receipt });
        AdvanceEventRulesAndQueueFact(new TieredRoundConversionUseIssuedEvent(receipt));
    }

    private bool IsIssuedTieredRoundUse(CardUseFrame use, bool zeroOnly = false)
    {
        if (use.TieredRoundConversionUse is not { } r || use.Action is not { Type: CardActionType.Use } ||
            TieredRoundIssuedOriginalOutputAction(use, r) is not { } action) return false;
        // This comparison-only view retains the original accepted output for
        // the mature actor-replacement proof. It never changes the runtime use,
        // its current FireSlash effect or the issued material/quota receipt.
        var issuedView = use.CurrentSlashFirePolicy is null ? use : use with { CardKind = r.EffectiveKind, Action = action };
        if (r.OwnerFrameId != use.Id || r.CardActionId != action.ActionId || !TieredRoundIssuedActorMatches(issuedView, r) ||
            action.ProviderSeat != r.ActorSeat || action.RequesterSeat is not null || action.EffectiveKind != r.EffectiveKind ||
            r.MaterialCount != action.PhysicalCards.Count || !(use.PhysicalCardIds ?? []).SequenceEqual(action.PhysicalCards.Select(c => c.CardId)) ||
            action.ConversionChain is not [var source] || source != r.Source || r.Source.OwnerSeat != r.ActorSeat ||
            string.IsNullOrWhiteSpace(r.Source.SkillInstanceId) || string.IsNullOrWhiteSpace(r.Source.BindingId) ||
            r.FrozenTier is < 0 or > 2 || r.MaterialCount != (r.FrozenTier == 2 ? 0 : 1) || zeroOnly && (r.FrozenTier != 2 || use.CardId != 0) ||
            _contentRegistry.GetSkill(r.Source.SkillId).Program is not { } definition || definition.GameplayHash != r.GameplayHash ||
            definition.ViewAs.SingleOrDefault(v => v.Id == r.Source.BindingId) is not { TieredRoundConversion: { } policy } rule ||
            policy.StateId != r.StateId || policy.UsageId != r.UsageId || rule.OutputKind != r.EffectiveKind || rule.InputCount != r.MaterialCount ||
            CompleteProgramEventHistory().OfType<TieredRoundConversionUseIssuedEvent>().Count(e => e.Receipt == r) != 1) return false;
        // This is issued identity, not a second current-shard permission query.
        return _skillRuntimeState.GetUsage(r.ActorSeat, r.Source.SkillId, TieredRoundUsage(policy),
            r.FrozenTier == 0 ? SkillUsageScope.Phase : SkillUsageScope.Round) == 1;
    }

    private CardActionContext? TieredRoundIssuedOriginalOutputAction(CardUseFrame use, TieredRoundConversionUseReceipt receipt)
    {
        if (use.Action is not { Type: CardActionType.Use } action) return null;
        if (use.CurrentSlashFirePolicy is not { } fire) return use.CardKind == receipt.EffectiveKind ? action : null;
        if (!IsSlashCard(receipt.EffectiveKind) || fire.OriginalKind != receipt.EffectiveKind ||
            fire.OriginalAction.ActionId != receipt.CardActionId || fire.OriginalAction.Type != CardActionType.Use ||
            fire.OriginalAction.EffectiveKind != receipt.EffectiveKind) return null;
        // Only the real 5403 issued policy can append a fire conversion source.
        // Its mature assertion proves original/current actors and materials,
        // the exact source chain, current targets/attack and the changed fact.
        // No current source shard is queried after the accepted cost.
        AssertCurrentSlashFirePolicy(use);
        return fire.OriginalAction;
    }

    private bool IsTieredRoundZeroUse(long id) => LifecycleCardUse(id) is { } use && IsIssuedTieredRoundUse(use, true);
    private bool TieredRoundIssuedActorMatches(CardUseFrame use, TieredRoundConversionUseReceipt receipt)
    {
        if (use.Action is not { } action || action.ProviderSeat != receipt.ActorSeat) return false;
        var changes = CompleteProgramEventHistory().OfType<ProgramCardUseActorReplacedEvent>().Where(e => e.CardUseFrameId == use.Id).ToArray();
        if (changes.Length == 0) return action.ActorSeat == receipt.ActorSeat && use.SourceSeat == receipt.ActorSeat;
        // The original material owner and quota remain issued; mutable actual
        // actor changes only along the mature exact replacement producer chain.
        return CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().Count(e => e.ResolutionId == use.Id &&
            e.SourceSeat == receipt.ActorSeat && e.CardId == use.CardId && e.CardKind == receipt.EffectiveKind) == 1 &&
            MatchesDeclaredActualDamageUse(use, action.ActorSeat, receipt.ActorSeat);
    }
    private Card TieredRoundZeroRepresentation(long id)
    {
        var use = LifecycleCardUse(id);
        if (use is null || !IsIssuedTieredRoundUse(use, true)) throw new InvalidOperationException("A logical card has no exact zero-material issuance.");
        return new(0, use.CardKind, Suit.None, 0);
    }

    private bool ContinueTieredRoundZeroSimpleUse(long id, ProgramSimpleCardContinuation continuation)
    {
        if (continuation.CardId != 0 || !IsTieredRoundZeroUse(id)) return false;
        var use = LifecycleCardUse(id)!; var card = TieredRoundZeroRepresentation(id); var actor = _players[use.SourceSeat];
        SetCardUseStep(id, ResolutionFrameStep.ResolvingEffect);
        if (!actor.IsAlive || _winner != Winner.None) { FinishCardUse(id, card, use.CardKind); return true; }
        if (continuation.Effect == SimpleCardUseEffect.Recovery && use.CardKind is CardKind.Peach or CardKind.Alcohol)
            CompleteRecoveryCardUse(actor, _players[use.TargetSeats[use.TargetIndex]], card, id, use.CardKind,
                continuation.RecoveryAmount, continuation.RecoveryPolicySources ?? []);
        else if (continuation.Effect == SimpleCardUseEffect.Alcohol && use.CardKind == CardKind.Alcohol)
            CompleteAlcoholUse(actor, card, id);
        else throw new InvalidOperationException("A zero-material basic use changed its exact simple effect.");
        return true;
    }

    private bool FinishTieredRoundZeroAttack(CardAttackHandle attack, out bool paused)
    {
        paused = false;
        if (attack.Card is not null || !IsTieredRoundZeroUse(attack.ResolutionId) ||
            attack.EffectiveCardKind is not { } kind ||
            !IsSlashCard(kind) && kind != CardKind.Duel &&
                !(kind == CardKind.FireAttack && MatchesTieredRoundZeroFireAttackAttack(attack))) return false;
        var use = LifecycleCardUse(attack.ResolutionId)!;
        SetCardUseStep(use.Id, ResolutionFrameStep.Completed);
        AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, use.CardKind));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted,
            use.TargetSeats, ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(attack); }
        return true;
    }

    private void AssertTieredRoundConversionUses()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(u => u.TieredRoundConversionUse is not null))
            if (!IsIssuedTieredRoundUse(use) || use.TieredRoundConversionUse!.FrozenTier == 2 && use.CardId != 0)
                throw new InvalidOperationException("A tiered conversion lost its accepted material count, source tuple or issued allowance.");
    }
}
