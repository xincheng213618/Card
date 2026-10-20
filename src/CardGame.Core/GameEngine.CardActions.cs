namespace CardGame.Core;

public sealed partial class GameEngine
{

    private bool TryBeginFinalizedTrickPrograms(JizhiResolution pending)
    {
        if (!HasFinalizedTargetProgram(pending.EffectiveCardKind) ||
            !TryMarkFinalizedTrickProgramsStarted(pending.ResolutionId)) return false;
        var use = NormalizeImplicitDrawTwoUseTarget(_resolutionStack.OfType<CardUseFrame>().Single(frame => frame.Id == pending.ResolutionId));
        var action = use.Action!;
        AdvanceEventRulesAndQueueFact(new CardActionAcceptedEvent(action));
        return TryBeginProgramCardWindow(null, action, SkillProgramTriggerWindow.CardUseTargetsFinalized,
            use.TargetSeats, ProgramCardContinuation.FinalizedTrick,
            trickContinuation: new(pending.Card.Id, pending.ActionKind, pending.TargetCardId, pending.RequiredCardKind));
    }
    private CommandResult SubmitRecast(RecastCardCommand command)
    {
        var validation = ValidateHumanPrompt(command.ActorSeat, DecisionKind.PlayCard,
            command.PromptId, CommandErrorCode.IllegalAction);
        if (validation is not null) return Reject(validation.Code, validation.Message);
        var actor = _players[command.ActorSeat];
        if (FindOwnedPlayableCard(actor, command.CardId) is null)
            return Reject(CommandErrorCode.InvalidCard, "The recast card is not in the actor's playable zones.");
        var action = BuildLegalActions(actor, selectedPhysicalCardId: command.CardId).SingleOrDefault(action =>
            action.Kind == LegalActionKind.Recast && action.CardId == command.CardId &&
            action.ConversionSource == command.ConversionSource);
        if (action is null) return Reject(CommandErrorCode.IllegalAction, "This card cannot be recast under the current rules.");
        return Accept(() =>
        {
            ClearPendingDecision();
            ExecuteAction(actor, action);
            AdvanceRulesAndPublishState();
            if (_options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary();
        });
    }

    private void ResolveRecast(CharacterState actor, Card card, CardKind? playedCardKind = null)
    {
        _selectedUseConversion = null;
        _hasSelectedUseConversionChoice = false;
        if (IsGuanduActivity && card.Kind == CardKind.UnitedEffort && playedCardKind is null)
        { BeginEquipmentRecast(actor, card, card.Kind); return; }
        if (playedCardKind is null && (CanHulaoWeaponRecast(card) || CanHellDitingMountRecast(actor, card)) &&
            FindOwnedCardLocation(actor, card) == CardLocation.Hand(actor.Seat))
        { BeginEquipmentRecast(actor, card, card.Kind); return; }
        if ((playedCardKind is null && card.Kind != CardKind.IronChain) ||
            (playedCardKind is not null && playedCardKind != CardKind.IronChain))
            throw new InvalidOperationException("Only Iron Chain may be recast.");
        if (FindOwnedCardLocation(actor, card).Zone == CardZoneKind.Equipment)
        { BeginEquipmentRecast(actor, card, playedCardKind ?? card.Kind); return; }
        var recast = CaptureNativeRecastInvocation(actor.Seat, actor.Seat, [card.Id]);
        MoveCards([card], FindOwnedCardLocation(actor, card), CardLocation.DiscardPile, CardMoveReasons.RecastDiscard);
        var drawn = DrawCards(actor, 1, log: false, reason: CardMoveReasons.RecastDraw);
        if (_aiBrains.TryGetValue(actor.Seat, out var brain)) brain.ObserveRecast(_turnNumber, card.Id);
        AdvanceEventRulesAndQueueFact(new CardRecastEvent(actor.Seat, card.Id, playedCardKind ?? card.Kind, drawn.Count));
        CompleteNativeRecastInvocation(recast, drawn.Count);
        AddLog("Recast", $"{actor.Name} 重铸【铁索连环】，摸 {drawn.Count} 张牌。", actor.Seat);
    }

    private long _cardActionSequence;
    private CardAttackHandle? ProgramCardAttack => _resolutionStack.OfType<ProgramCardTriggerWindowFrame>()
        .LastOrDefault(frame => frame.AttackOwnerFrameId is not null)?.AttackOwnerFrameId is { } id
            ? new CardAttackHandle(this, id) : null;
    private IReadOnlyList<CardAttackState>? PreparedTargetAttacks(long id) =>
        _resolutionStack.OfType<CardUseFrame>().Single(frame => frame.Id == id).PreparedTargetAttacks;
    private int GetFangtianEffectiveTarget(FangtianHalberdHandle pending, int index) =>
        (LifecycleCardUse(pending.ResolutionId)?.ProgramUseAccepted == true) && PreparedTargetAttacks(pending.ResolutionId) is { } prepared
            ? prepared[index].TargetSeat : pending.TargetSeats[index];

    private CardActionContext? CaptureCardUseAction(Card card, int actorSeat,
        IReadOnlyList<int> targets, CardKind effectiveKind, IReadOnlyList<int> physicalIds,
        CardConversionSource? explicitConversion = null,
        IReadOnlyList<CardConversionSource>? additionalConversions = null,
        IReadOnlyList<int>? designatedTargetSeats = null, bool isTrueZhangbaSlash = false)
    {
        var costs = physicalIds.Select(id => new CardActionCost(id,
            _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(item => item.Id == id).Kind,
            CapturedDeclarationOrigin(id, _cardZones.GetLocation(id)), CapturePhysicalCardColor(actorSeat,_cardZones.CardsAt(_cardZones.GetLocation(id)).Single(c=>c.Id==id)))).ToArray();
        var provider = costs.FirstOrDefault()?.From.OwnerSeat ?? actorSeat;
        CardConversionSource? conversion;
        if (explicitConversion is not null)
        {
            ValidateLastZoneConversionCommit(explicitConversion, _players[provider],
                physicalIds.Select(id => _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(c => c.Id == id)).ToArray(), forResponse: false);
            var selectedConversion = _selectedUseConversion ?? _selectedResponseConversion;
            _selectedUseConversion = null;
            _selectedResponseConversion = null;
            _hasSelectedUseConversionChoice = false;
            _hasSelectedResponseConversionChoice = false;
            if (selectedConversion is not null && selectedConversion != explicitConversion)
            {
                throw new InvalidOperationException("The explicit card-use conversion no longer matches the selected action.");
            }
            conversion = explicitConversion;
        }
        else
        {
            conversion = physicalIds.Count == 1
                ? GetSelectedUseConversion(_players[actorSeat], _players[provider], card, effectiveKind)
                : null;
        }
        ValidateOwnPublicPileConversionCommit(conversion, _players[provider],
            physicalIds.Select(id => _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(c => c.Id == id)).ToArray(), forResponse: false);
        var conversionChain = new List<CardConversionSource>();
        if (conversion is not null) conversionChain.Add(conversion);
        if (additionalConversions is not null)
        {
            if (additionalConversions.Any(source => source is null) ||
                additionalConversions.Distinct().Count() != additionalConversions.Count ||
                conversion is not null && additionalConversions.Contains(conversion))
            {
                throw new InvalidOperationException("A card-use conversion chain must contain distinct sources.");
            }
            conversionChain.AddRange(additionalConversions);
        }
        if (!IsMouGuquUseAllowed(actorSeat, effectiveKind, physicalIds, conversionChain.FirstOrDefault()))
            throw new InvalidOperationException("Guqu forbids this true Use material or conversion.");
        ConsumeProgramViewAsUsage(conversionChain);
        var trackAppearance = _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouHuaXiongYaowu) || HasWuZhangFeiUseCapability || HasMouZuifengCapability || HasWuLuXunZhangcaiCapability || HasLeZouShiCapability || HasWuyouCapability || HasWuGuanYuJuewuCapability || HasSiruiCapability || HasLiFengCapability || HasCuiYanCapability || conversionChain.Any(source => ViewAsRule(source)?.SourcePileSkillId is not null) || HasLiuYanCapability || HasQuHuangCapability || HasRuanYuCapability || HasLiuPanCapability || HasWuJingLiubing || HasWuJingHeji || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveKenshangTargetRewrite) || HasMaMiDiCapability || HasWuAnGuoCapability || HasFeiYiHezhong || HasBianFuRenCapability || TracksDamageTargetBudgetTargets || HasAdjacentRedTargetCapability || HasSoleTargetProtectionCapability || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.RevealRecordedSuitSlash) || effectiveKind is CardKind.UnexpectedAssault or CardKind.WumiaoFlood || (TracksUniqueLeaderTrickTargets || TracksRoundGainedSourceUses || HasTargetedOrdinaryTrickAdjustment) && IsOrdinaryTrick(effectiveKind) || HasSourceCurseActionColors || TracksOwnPlayHistory || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.OfferOriginalTargetAddition) || HasBlackTrickTargetPolicy || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DrawByDamageCardColor) || HasShownCardTurnCapability || HasTurnRedSlashCapability || TracksPlayCardHistory || TracksCurrentTurnUseKinds || _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.AlternatingSuitDrawDiscard) || TracksPaidColorUseAppearance(actorSeat, effectiveKind, conversionChain) || HasSameTypeActualUseAid && IsSameTypeAidEffectiveCard(effectiveKind) || (HasComparedBlackSlashPolicies || HasYuanchouCardPolicy) && IsSlashCard(effectiveKind);
        var usedSuit = WuJingLiubingUsedSuit(actorSeat, card, effectiveKind, physicalIds, conversionChain, trackAppearance ? CaptureUsedCardSuit(actorSeat, physicalIds, card) : null);
        if (IsCuiYanNextUseForbidden(actorSeat, physicalIds, usedSuit))
            throw new InvalidOperationException("The native use violates its issued next-use suit restriction.");
        return CaptureWuZhangFeiUseRankAction(CaptureFactionAction(new CardActionContext(++_cardActionSequence,
            _resolutionStack.OfType<CardUseFrame>().LastOrDefault()?.Action?.ActionId,
            CardActionType.Use, actorSeat, provider, provider == actorSeat ? null : actorSeat,
            null, null, effectiveKind, targets, costs, conversionChain,
            (designatedTargetSeats ?? targets ),
            effectiveSuit: usedSuit,
            effectiveRank: CaptureJuewuActionRank(provider, physicalIds) ?? CaptureAlcoholActionRank(provider, effectiveKind, physicalIds) ??
                (HasZhouQunCapability && IsSlashCard(effectiveKind) && physicalIds.Count == 1 ? card.Rank : (int?)null) ??
                (HasRankSlashRange(_players[actorSeat], effectiveKind)
                ? physicalIds.Count == 1 && card.Rank > 0 ? card.Rank
                    : isTrueZhangbaSlash && physicalIds.Count == 2 && conversionChain.Count == 0
                        ? ZhangbaSpecificSlashRank(_players[actorSeat], physicalIds.Select(id => GetAttackCard(id)).ToArray(), _players[provider]) : null
                : trackAppearance && physicalIds.Count == 1 && card.Rank > 0 ? card.Rank : null), effectiveIsRed: CaptureActionColor(costs,usedSuit))));
    }

    private bool TryBeginCardResponsePrograms(CardAttackHandle attack, CharacterState actor,
        CharacterState provider, int? requesterSeat, int opponentSeat, CardKind effectiveKind,
        IReadOnlyList<CardActionCost> costs, ProgramCardContinuation continuation,
        CardConversionSource? conversionSource = null, Suit? completedResponseUseSuit = null)
    {
        var parent = _resolutionStack.OfType<CardUseFrame>().LastOrDefault(frame => frame.Id == attack.ResolutionId);
        var pileMaterial = OwnPublicPileResponseMaterial(costs, conversionSource);
        completedResponseUseSuit ??= pileMaterial is null ? null : EffectiveSuit(provider, pileMaterial);
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence, parent?.Action?.ActionId,
            CardActionType.Response, actor.Seat, provider.Seat, requesterSeat, actor.Seat,
            opponentSeat, effectiveKind, [], costs, conversionSource is null ? [] : [conversionSource], effectiveSuit: completedResponseUseSuit, effectiveRank: CaptureJuewuResponseRank(provider, costs) ?? SiruiResponseRank(conversionSource, costs) ?? CaptureAlcoholResponseRank(provider, effectiveKind, costs) ?? CaptureWuLuXunResponseRank(provider, effectiveKind, costs, conversionSource) ?? CaptureWuZhangFeiResponseRank(provider, effectiveKind, costs, conversionSource) ?? (pileMaterial is { Rank: > 0 } ? pileMaterial.Rank : null),effectiveIsRed:CaptureActionColor(costs,completedResponseUseSuit)));
        BindWuZhangFeiResponseRanks(action);
        if (parent is not null)
        {
            BindSiruiResponseAction(parent, action);
            parent = LifecycleCardUse(parent.Id);
        }
        if (parent is not null && actor.Seat == provider.Seat && requesterSeat is null && IsProgramResponseCardUse(actor, effectiveKind))
            TryIssueTieredRoundResponse(parent, action);
        if (actor.Seat == provider.Seat && requesterSeat is null && IsProgramResponseCardUse(actor, effectiveKind))
        {
            RecordProgramUsedBasicCard(actor.Seat, effectiveKind);
            RecordActualPlayPhaseUse(action);
        }
        AdvanceEventRulesAndQueueFact(new CardActionAcceptedEvent(action));
        CaptureGuanduChariotResponsePayments(action, attack.ResolutionId, continuation);
        CaptureFriendMinimumHandUse(action);
        CaptureOffTurnResponseUseOrigin(attack.ResolutionId, action);
        if (TryBeginCommittedResponseUsePrograms(attack, action, continuation)) return true;
        if (TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardResponseAccepted,
                [opponentSeat], continuation)) return true;
        if (!HasResponseUseCompletionObserver(action, continuation) && !HasCardResponseCompletedObserver(action, continuation)) return false;
        ContinueAcceptedCardResponse(attack, action, continuation);
        return true;
    }

    // Target redirection is completed for every target before any use trigger or
    // response starts. Keep the prepared attack objects so Liuli cannot run twice.
    private bool PrepareProgramSlashTargets(CardAttackHandle attack)
    {
        if ((LifecycleCardUse(attack.ResolutionId)?.ProgramUseAccepted == true)) return false;
        var frame = _resolutionStack.OfType<CardUseFrame>().Single(item => item.Id == attack.ResolutionId);
        if (frame.Action is not { } captured) return false;
        if (ActiveFangtianHalberd is { } multi && multi.ResolutionId == attack.ResolutionId)
        {
            var prepared = (frame.PreparedTargetAttacks ?? []).Append(GetCardAttackState(attack.ResolutionId)).ToArray();
            var ownerIndex = _resolutionStack.FindIndex(item => item.Id == frame.Id);
            ReplaceRuntimeFrame(_resolutionStack[ownerIndex].Id, frame with { PreparedTargetAttacks = Array.AsReadOnly(prepared) });
            if (prepared.Length < multi.TargetSeats.Count)
            {
                multi.TargetIndex++;
                BeginNextFangtianHalberdTarget(multi);
                return true;
            }
            multi.TargetIndex = 0;
            UpdateCardAttackState(attack.ResolutionId, _ => prepared[0]);
            multi.CurrentAttack = attack;
            ActiveCardAttack = attack;
            SetCardUseTargetIndex(multi.ResolutionId, 0);
            frame = _resolutionStack.OfType<CardUseFrame>().Single(item => item.Id == attack.ResolutionId);
        }
        var finalized = CaptureFactionAction(new CardActionContext(captured.ActionId, captured.ParentActionId, captured.Type,
            captured.ActorSeat, captured.ProviderSeat, captured.RequesterSeat, captured.ResponderSeat,
            captured.OpponentSeat, captured.EffectiveKind, frame.TargetSeats, captured.PhysicalCards, captured.ConversionChain,
            (frame.TargetSeats.Where(seat => _players[seat].IsAlive).Distinct().ToArray() ),
            captured.EffectiveSuit, captured.EffectiveRank,captured.EffectiveIsRed, captured.FactionOrigin));
        var index = _resolutionStack.FindLastIndex(item => item.Id == frame.Id);
        ReplaceRuntimeFrame(_resolutionStack[index].Id, frame with { Action = finalized, ProgramUseAccepted = true });
        AdvanceEventRulesAndQueueFact(new CardActionAcceptedEvent(finalized));
        if (TryBeginProgramCardWindow(attack, finalized, SkillProgramTriggerWindow.CardUseTargetsFinalized,
                finalized.TargetSeats, ProgramCardContinuation.Slash)) return true;
        if (PreparedTargetAttacks(attack.ResolutionId) is not null)
        {
            ContinueSlashAfterFinalizedTargets(attack);
            return true;
        }
        return false;
    }

    private bool TryBeginProgramCardWindow(CardAttackHandle? attack, CardActionContext action,
        SkillProgramTriggerWindow window, IReadOnlyList<int> opponents, ProgramCardContinuation? continuation,
        bool? cardUseCausedDamage = null, ProgramTrickContinuation? trickContinuation = null,
        ProgramSimpleCardContinuation? simpleContinuation = null,
        ProgramCompletedResponseReturn? completedResponseReturn = null,
        CaoChunHandUsePayment? caoChunPayment = null, bool caoChunDebtOnly = false)
    {
        if ((continuation is null) != (completedResponseReturn is not null))
            throw new InvalidOperationException("A card trigger window needs exactly one return continuation.");
        RecordGuanduCardAction(action, window);
        if (window == SkillProgramTriggerWindow.CardUseCommitted && TracksAlternateActualUseColor)
            RecordAlternateActualUseColor(action);
        if (window is SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardResponseAccepted &&
            action.ConversionChain.Count != 0) { PayMeiYingConversion(action); PayZhenyiHoutu(action); }
        if ((HasWangYunPlanCapability || HasWangYunTrickCapability) &&
            window is SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardUseCommitted)
        {
            for (var nativeIndex = _resolutionStack.Count - 1; nativeIndex >= 0; nativeIndex--)
                if (_resolutionStack[nativeIndex] is CardUseFrame { WangYunNativeOrigin: not null } nativeUse &&
                    nativeUse.Action?.ActionId == action.ActionId)
                {
                    CompleteWangYunNativePayment(nativeUse.Id);
                    break;
                }
        }
        if (window == SkillProgramTriggerWindow.CardUseTargetsFinalized && IsDragonBoatActivity)
        {
            var nativeUse = _resolutionStack.OfType<CardUseFrame>().Single(use => use.Action?.ActionId == action.ActionId);
            CaptureDragonBoatFinalizedUse(nativeUse.Id, action);
        }
        if (window == SkillProgramTriggerWindow.CardUseTargetsFinalized && HasSuFeiCapability)
        {
            var nativeUse = _resolutionStack.OfType<CardUseFrame>().Single(use => use.Action?.ActionId == action.ActionId);
            CaptureSuFeiFinalizedUse(nativeUse.Id, action);
        }
        if (HasGuoSiCapability && action.Type == CardActionType.Use &&
            window is SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardUseCompleted)
        {
            var nativeUse = _resolutionStack.OfType<CardUseFrame>().Single(use => use.Action?.ActionId == action.ActionId);
            if (window == SkillProgramTriggerWindow.CardUseTargetsFinalized)
                CaptureGuoSiFinalizedUse(nativeUse.Id, action);
            else CaptureGuoSiCompletedUse(nativeUse.Id, action);
        }
        if (window == SkillProgramTriggerWindow.CardUseTargetsFinalized && HasNeifaCapability)
        {
            var nativeUse = _resolutionStack.OfType<CardUseFrame>().Single(use => use.Action?.ActionId == action.ActionId);
            CaptureNeifaPaidEquipmentUse(nativeUse.Id, action);
        }
        if (window == SkillProgramTriggerWindow.CardUseTargetsFinalized && HasHuaManCapability && action.Type == CardActionType.Use)
        {
            var nativeUse = _resolutionStack.OfType<CardUseFrame>().Single(use => use.Action?.ActionId == action.ActionId);
            CaptureHuaManFinalizedUse(nativeUse.Id, action);
        }
        if (window == SkillProgramTriggerWindow.CardUseCompleted && HasHuaManCapability && action.Type == CardActionType.Use &&
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(use => use.Action?.ActionId == action.ActionId) is { } huaManCompleted)
            CaptureHuaManCompletedUse(huaManCompleted.Id, action);
        if (_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveQigongActualDodgedSlashRequest) &&
            window is SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardUseCommitted &&
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(use => use.Action?.ActionId == action.ActionId) is { Qigong: not null } qigongUse)
            CompleteQigongNativePayment(qigongUse.Id);
        RegisterFirstTurnTargetActions(action, window);
        CaptureUniqueLeaderTrickQualification(action, window);
        var candidates = caoChunDebtOnly ? new List<ProgramCardTriggerCandidate>() : CollectSharedCardActionCandidates(action, window, opponents,
            cardUseCausedDamage, continuation).ToList();
        if (window == SkillProgramTriggerWindow.CardUseBeforeTargetEffects &&
            (caoChunPayment ?? (action.Type == CardActionType.Use ? CaoChunPaidUseQualification(action) : null)) is { } debt &&
            CaoChunDebtCandidate(debt) is { } issued)
            candidates.Add(new(issued.OwnerSeat, -1, issued.SkillId, issued.BindingId, issued.GameplayHash,
                issued.SkillInstanceId, issued.Priority, CaoChunDebtContext(debt, 0)));
        candidates = candidates
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.OwnerSeat)
            .ThenBy(candidate => candidate.SkillId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.SkillInstanceId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.TriggerId, StringComparer.Ordinal)
            .ToList();
        RecordActorHandLimitTargetAnnouncements(action, window);
        if (candidates.Count == 0) return false;
        RecordFactionRewardOffers(action, candidates);
        var frameId = ++_resolutionSequence;
        candidates = candidates.Select(candidate => candidate.FrozenContext is { } frozen
                ? candidate with
                {
                    FrozenContext = frozen with
                    {
                        ParentFrameId = frameId,
                        CardUse = frozen.CardUse! with { ParentCardUseFrameId = _resolutionStack[^1].Id }
                    }
                }
                : candidate)
            .ToList();
        var frame = new ProgramCardTriggerWindowFrame(frameId,
            _resolutionStack[^1].Id, action, continuation, Array.AsReadOnly(candidates.ToArray()),
            TrickContinuation: trickContinuation, SimpleContinuation: simpleContinuation,
            CompletedResponseReturn: completedResponseReturn, AttackOwnerFrameId: attack?.ResolutionId)
            { CaoChunDebtBeforeEffects = caoChunDebtOnly ? true : null };
        PushRuntimeFrame(frame);
        CaptureMoQuanyuSingleton(frame, window);
        if (HasGuoSiCapability && window == SkillProgramTriggerWindow.CardUseCompleted &&
            completedResponseReturn is not null)
            CaptureGuoSiCompletedUse(frame.Id, action);
        AdvanceRuntimeTop<ProgramCardTriggerWindowFrame>();
        return true;
    }

    /// <summary>Called after target redirection and before the first Slash target effect.</summary>
    private bool TryBeginProgramCardUseBeforeTargetEffects(CardAttackHandle attack)
    {
        var frame = _resolutionStack.OfType<CardUseFrame>().Single(item => item.Id == attack.ResolutionId);
        var action = frame.Action ?? throw new InvalidOperationException(
            "A before-target-effects program window requires the frozen card action.");
        return TryBeginProgramCardWindow(attack, action,
            SkillProgramTriggerWindow.CardUseBeforeTargetEffects, [attack.TargetSeat],
            ProgramCardContinuation.BeforeTargetEffects);
    }

    private bool TryBeginProgramSlashStage(CardAttackHandle attack,
        SkillProgramTriggerWindow window, ProgramCardContinuation continuation)
    {
        if (!SameAttackOwner(ActiveCardAttack, attack))
            throw new InvalidOperationException("The Slash program stage lost its active attack.");
        // Legacy and virtual Slash flows can reach the Dodge boundary without a
        // frozen card action. They have no card-action facts for program triggers.
        var action = _resolutionStack.OfType<CardUseFrame>()
            .Single(frame => frame.Id == attack.ResolutionId).Action;
        if (action is null) return false;
        return TryBeginProgramCardWindow(attack, action, window, [attack.TargetSeat], continuation);
    }

    /// <summary>Called before Jizhi/Nullification and the first trick target effect.</summary>
    private bool TryBeginProgramCardUseBeforeTargetEffects(JizhiResolution pending)
    {
        if (TryBeginActualUseTargetPrograms(pending)) return true;
        var frame = _resolutionStack.OfType<CardUseFrame>().Single(item => item.Id == pending.ResolutionId);
        var action = frame.Action ?? throw new InvalidOperationException(
            "A trick before-target-effects program window requires the frozen card action.");
        return TryBeginProgramCardWindow(
            null,
            action,
            SkillProgramTriggerWindow.CardUseBeforeTargetEffects,
            frame.TargetSeats,
            ProgramCardContinuation.BeforeTrickTargetEffects,
            trickContinuation: new(
                pending.Card.Id,
                pending.ActionKind,
                pending.TargetCardId,
                pending.RequiredCardKind));
    }

    private IReadOnlyList<ProgramCardTriggerCandidate> CollectSharedCardActionCandidates(
        CardActionContext action, SkillProgramTriggerWindow window, IReadOnlyList<int> eventTargets,
        bool? cardUseCausedDamage, ProgramCardContinuation? continuation = null)
    {
        CaptureWuLuXunZhangcai(action, window);
        CaptureWuZhangFeiUseQualification(action, window);
        CaptureLeCaiYongFeibaiQualification(action, window);
        CaptureLeCaoZhiWenlanQualification(action, window);
        CaptureMouLiuChengCompletion(action, window);
        CaptureMouLiuChengAccrual(action, window);
        var targets = eventTargets.Distinct().ToHashSet();
        var result = new List<ProgramCardTriggerCandidate>();
        var capturedOwnerFacts = new SkillProgramTriggerFacts?[_players.Count];
        foreach (var owner in _players.Where(player => player.IsAlive).OrderBy(player => player.Seat))
        foreach (var binding in GetSkillBindingShard(owner)?.GetInstanceTriggers(window) ?? [])
        {
            var trigger = binding.Trigger;
            if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveBiguoTargetedPindianAndCategoryBan)) continue;
            if (trigger.Effects.Any(e => e.Op is SkillProgramEffectOp.CollectJiyunCompletedUseEntities or SkillProgramEffectOp.ResolveLeiluanLastHandUseBenefit or SkillProgramEffectOp.ResolveChenglieRedHolderOutcome or SkillProgramEffectOp.ResolveXieweiTwoHandBaitBasicUse)) continue;
            if (ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.ResolveHuiyunCompletedFireAttack) ||
                ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.ResolvePijingNextSoleTargetChoice)) continue;
            if (ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.PayShanjiaHandUseDebt) ||
                trigger.Effects.Any(effect => effect.Op is SkillProgramEffectOp.ResolveBingjieIssuedTargetDiscards or SkillProgramEffectOp.RewardHejiUnconvertedUse)) continue;
            if (!CanCollectFirstSlashDuelCandidate(owner.Seat, trigger, action)) continue;
            if (trigger.RequireNoCardConversion == true && !IsUnconvertedActualCardUse(action, window, continuation)) continue;
            var reward = trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.RewardOutOfTurnFactionSlash);
            var rewardChooser = reward ? FactionActionRewardChooser(owner.Seat, trigger, action) : null;
            if (reward && (rewardChooser is null || WasFactionRewardOffered(owner.Seat, binding.SkillId, action) || !HasSkillRoleQualification(owner, binding.SkillId, binding.SkillInstanceId, Role.Lord))) continue;
            if (ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.DiscardNonFinalTargetCardThenDraw) &&
                result.Any(candidate => candidate.OwnerSeat == owner.Seat && candidate.SkillId == binding.SkillId && candidate.TriggerId == trigger.Id)) continue;
            if (ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.FirstCategoryCompletedTop) &&
                (!IsFirstCategoryUse(owner.Seat,binding.SkillId,action) || AvailableCompletedTopCosts(action, _resolutionStack.OfType<CardUseFrame>().LastOrDefault(f=>f.Action?.ActionId==action.ActionId)?.Id ?? ActiveNullificationWindow?.Id ?? 0).Length==0)) continue;
            if (trigger.Effects.Any(e=>e.Op==SkillProgramEffectOp.IssueCardNoResponseAndPlayUseBan) &&
                (_phase!=TurnPhase.Play || action.ActorSeat!=_currentSeat || owner.Seat!=action.ActorSeat || !IsEligibleIssuedNoResponseCard(action.EffectiveKind) || HasIssuedPolicyForAction(action.ActionId))) continue;
            if (window is (SkillProgramTriggerWindow.CardUseCompleted or SkillProgramTriggerWindow.CardUseCommitted) && action.Type == CardActionType.Response &&
                !trigger.IncludeResponseUses) continue;
            if (trigger.OwnerRelation is not { } relation ||
                trigger.CardKinds.Count > 0 && !trigger.CardKinds.Contains(action.EffectiveKind) ||
                trigger.CardCategories.Count > 0 && (action.Type != CardActionType.Use && !trigger.IncludeResponseUses ||
                    !MatchesProgramCardCategory(action.EffectiveKind, trigger.CardCategories))) continue;
            var bindingTargets = trigger.OnlyDesignatedCardTargets
                ? targets.Intersect(action.EffectiveKind == CardKind.BorrowedSword
                    ? action.TargetSeats.Where((seat, index) => index % 2 == 0)
                    : action.EffectiveDesignatedTargetSeats).ToHashSet() : targets;
            var matchingSources = action.ConversionChain.Where(source =>
                source.OwnerSeat == owner.Seat &&
                source.SkillId == trigger.SourceSkillId &&
                (trigger.SourceViewAsId is null || source.BindingId == trigger.SourceViewAsId)).ToArray();
            if (trigger.SourceSkillId is not null && matchingSources.Length == 0) continue;
            var matches = relation switch
            {
                SkillProgramCardActionOwnerRelation.Actor => owner.Seat == action.ActorSeat,
                SkillProgramCardActionOwnerRelation.Target => bindingTargets.Contains(owner.Seat),
                SkillProgramCardActionOwnerRelation.Observer => true,
                SkillProgramCardActionOwnerRelation.ConversionSource => matchingSources.Length > 0,
                _ => false
            };
            if (!matches) continue;
            if (relation == SkillProgramCardActionOwnerRelation.ConversionSource && targets.Count == 0 && !trigger.AllowNoEventTarget) continue;
            var eventTargetsForBinding = window == SkillProgramTriggerWindow.CardUseTargetsFinalized && HasFinalTargetSlashEffects(trigger.Effects)
                ? targets.Where(seat => _players[seat].IsAlive).Order().ToArray()
                : relation == SkillProgramCardActionOwnerRelation.ConversionSource && targets.Count > 0 && !trigger.SingleActionInstance
                ? targets.Where(seat => _players[seat].IsAlive).Order().ToArray()
                : [relation == SkillProgramCardActionOwnerRelation.Target
                    ? owner.Seat : targets.Count == 1 ? targets.Single() : -1];
            if (relation == SkillProgramCardActionOwnerRelation.ConversionSource &&
                eventTargetsForBinding.Length == 0 && trigger.AllowNoEventTarget)
                eventTargetsForBinding = [-1];
            foreach (var eventTarget in eventTargetsForBinding)
            {
                var capturedFacts = capturedOwnerFacts[owner.Seat] ??= CaptureProgramTriggerFacts(owner, action);
                var facts = capturedFacts with
                {
                    // Explicit completion conditions read the response appearance frozen before
                    // payment. Other candidates retain their existing captured facts.
                    CardActionSuit = window == SkillProgramTriggerWindow.CardUseCompleted &&
                        action.Type == CardActionType.Response && trigger.IncludeResponseUses &&
                        ProgramInstructionResolver.Default.Features(trigger).UsesCondition(SkillProgramTriggerConditionKind.CardActionSuitIs)
                        ? action.EffectiveSuit : capturedFacts.CardActionSuit,
                    CardUseCausedDamage = cardUseCausedDamage,
                    EventTargetRuleWounded = UsesTreatedWounded && eventTarget >= 0 ? IsRuleWounded(_players[eventTarget]) : null,
                    EventTargetHandCount = eventTarget >= 0 ? GetHand(_players[eventTarget]).Count : 0,
                    EventTargetHp = window == SkillProgramTriggerWindow.CardUseTargetsFinalized && HasFinalTargetSlashEffects(trigger.Effects) && eventTarget >= 0
                        ? _players[eventTarget].Hp : capturedFacts.EventTargetHp,
                    OwnerEventTargetDistance = eventTarget >= 0
                        ? GetCombatDistance(owner.Seat, eventTarget)
                        : int.MaxValue,
                    CardActionTargetIsOwner = bindingTargets.Contains(owner.Seat)
                };
                var context = CreateCardActionProgramContext(action, window, parentFrameId: 0,
                    owner.Seat, eventTarget, facts) with { OptionalChooserSeat = rewardChooser };
                if (trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.GrantActorHandLimitPenalty) &&
                    !ShouldCollectActorHandLimitPenalty(action, owner.Seat, window)) continue;
                if (!CanOfferCompletedUndamagedTargetReveal(owner.Seat, trigger, context)) continue;
                if (ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.DrawAfterPaidMeiYingConversion) &&
                    !CanOfferMeiYingConversionDraw(ToSharedCandidate(new(owner.Seat, eventTarget, binding.SkillId, trigger.Id,
                        binding.Program.GameplayHash, binding.SkillInstanceId, trigger.Priority, context)), trigger, context)) continue;
                if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.DrawForSourceCurseUse))
                {
                    var curse = EligibleSourceCurseUse(owner.Seat, binding.SkillId, binding.SkillInstanceId, action, trigger.Effects.Single());
                    if (window != SkillProgramTriggerWindow.CardUseCommitted || curse is null) continue;
                    context = context with { SourceCurseUse = new(curse, action.ActionId, action.ActorSeat, action.EffectiveIsRed) };
                }
                if (AttachMouLiuChengAccrual(action, binding.SkillId, binding.SkillInstanceId, context) is not { } liuChengContext) continue;
                context = liuChengContext;
                result.Add(new(owner.Seat, eventTarget, binding.SkillId, trigger.Id,
                    binding.Program.GameplayHash, binding.SkillInstanceId, trigger.Priority, context));
            }
        }
        CollectIssuedOriginalTargetCandidates(action, window, result);
        CollectIssuedGiftedSlashRewardCandidates(action, window, result);
        CollectIssuedJoinedTrickDamageRewardCandidates(action, window, result);
        CollectIssuedRoundGainedUseCandidates(action, window, result);
        CollectIssuedLeiluanCandidates(action, window, result);
        CollectIssuedXieweiCandidates(action, window, result);
        CollectIssuedChenglieCandidates(action, window, result);
        CollectIssuedNeifaCandidates(action, window, result);
        CollectIssuedMaWanHunjiangCandidates(action, window, result);
        CollectIssuedBingjieCandidates(action, window, result);
        CollectIssuedKenshangCandidates(action, window, result);
        CollectIssuedFulueCandidates(action, window, result);
        CollectIssuedMiuyanCandidates(action, window, result);
        CollectIssuedWuJingHejiReward(action, window, result);
        CollectIssuedWeifuCandidates(action, window, result);
        CollectIssuedYinjuCandidates(action, window, result);
        CollectIssuedSunHaoCanshiCandidates(action, window, result);
        CollectIssuedHuiyunCandidates(action, window, result);
        CollectIssuedPijingNextUseCandidates(action, window, result);
        CollectIssuedBiguoCandidates(action, window, result);
        CollectIssuedMouGuquCandidates(action, window, result);
        CollectIssuedDiouCandidates(action, window, result);
        string GrantIdentity(ProgramCardTriggerCandidate candidate)
        {
            var trigger=_contentRegistry.GetSkill(candidate.SkillId).Program!.Triggers.Single(t=>t.Id==candidate.TriggerId);
            return ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.GiveOwnedCardToOtherFinalTargetAndDraw)
                || trigger.Effects.Any(e => e.Op is SkillProgramEffectOp.OfferUniqueLargestHandTrickTargetAddition or
                    SkillProgramEffectOp.JoinUniqueLargestHpTrickTargetAndDrawAfterDamage or SkillProgramEffectOp.DrawAfterJoinedTrickDamage or
                    SkillProgramEffectOp.AdjustOneRoundGainedOrdinaryTrickTarget or SkillProgramEffectOp.DrawForRoundGainedEquipmentUse)
                || trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.GrantActorHandLimitPenalty)
                || trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.RewardOutOfTurnFactionSlash) ? "" : candidate.SkillInstanceId;
        }
        if (window == SkillProgramTriggerWindow.CardUseCompleted)
            result.AddRange(CollectJiyunCompletedUseCandidates(action, continuation));
        return IncludeRuanYuMiaoxianRedCandidates(result.DistinctBy(candidate => (action.ActionId, candidate.OwnerSeat, candidate.SkillId,
            GrantIdentity(candidate), candidate.TriggerId, candidate.OpponentSeat)).ToArray(), action, window);
    }

    private static ProgramTriggerCandidate ToSharedCandidate(ProgramCardTriggerCandidate candidate) =>
        new(candidate.OwnerSeat, candidate.SkillId, candidate.TriggerId, candidate.SkillInstanceId,
            candidate.GameplayHash, candidate.Priority);

    private ProgramSkillWindowContext CreateCardActionProgramContext(
        ProgramCardTriggerWindowFrame frame, ProgramCardTriggerCandidate candidate)
    {
        var context = candidate.FrozenContext ?? CreateCardActionProgramContext(frame.Action,
            GetCardActionWindow(frame), frame.Id, candidate.OwnerSeat, candidate.OpponentSeat,
            CaptureProgramTriggerFacts(_players[candidate.OwnerSeat], frame.Action));
        return CreateBiguoTargetContext(frame, candidate, CreateJiyunCompletedUseContext(frame, candidate, CreatePijingIssuedContext(frame, candidate, CreateMaMiDiIssuedContext(frame, candidate,
            CreateHuaManCardUseContext(frame, ToSharedCandidate(candidate), context)))));
    }

    private ProgramSkillWindowContext CreateCardActionProgramContext(
        CardActionContext action, SkillProgramTriggerWindow window, long parentFrameId,
        int ownerSeat, int eventTargetSeat, SkillProgramTriggerFacts facts)
    {
        var suits = action.PhysicalCards.Select(cost =>
        {
            var location = _cardZones.GetLocation(cost.CardId);
            return _cardZones.CardsAt(location).Single(card => card.Id == cost.CardId).Suit;
        }).ToArray();
        var publicSuit = suits.Length == 1 ? suits[0] : (Suit?)null;
        bool? isRed = suits.Length == 0 ? null : suits.All(suit => suit is Suit.Heart or Suit.Diamond);
        var debit = GetCardUseDebit(action.ActionId);
        return new(window, parentFrameId, ownerSeat, SourceSeat: action.ActorSeat,
            TargetSeat: eventTargetSeat >= 0 ? eventTargetSeat : null, Facts: facts,
            CardUse: new(action.ActionId, parentFrameId, action.ActorSeat,
                eventTargetSeat >= 0 ? eventTargetSeat : null,
                action.EffectiveKind, publicSuit, isRed, debit is not null, debit,
                action.DesignatedTargetSeats))
        {
            HuaManSouying = HasHuaManCapability
                ? _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(use => use.Action?.ActionId == action.ActionId)?.HuaManSouyingQualification
                : null
        };
    }

    private static SkillProgramTriggerWindow GetCardActionWindow(ProgramCardTriggerWindowFrame frame) =>
        frame.CaoChunDebtBeforeEffects == true ? SkillProgramTriggerWindow.CardUseBeforeTargetEffects :
        frame.CardSupplyCompletion is not null ? SkillProgramTriggerWindow.CardSupplyCompleted :
        frame.ResponseCompletion is not null ? SkillProgramTriggerWindow.CardResponseCompleted :
        frame.CompletedResponseReturn is { IsCommitted: true } ? SkillProgramTriggerWindow.CardUseCommitted :
        frame.CompletedResponseReturn is not null ||
        frame.Continuation is ProgramCardContinuation.CompletedSlash or ProgramCardContinuation.CompletedCard
            ? SkillProgramTriggerWindow.CardUseCompleted
            : frame.Continuation == ProgramCardContinuation.SlashTargetRedirecting
            ? SkillProgramTriggerWindow.SlashTargetRedirecting
            : frame.Continuation == ProgramCardContinuation.SlashBeforeResponse
            ? SkillProgramTriggerWindow.SlashBeforeResponse
            : frame.Continuation == ProgramCardContinuation.SlashFullyDodged
            ? SkillProgramTriggerWindow.SlashFullyDodged
            : frame.Continuation is ProgramCardContinuation.CommittedSlash or
                ProgramCardContinuation.CommittedTrick or ProgramCardContinuation.CommittedSimpleCard
            ? SkillProgramTriggerWindow.CardUseCommitted
            : frame.Continuation is ProgramCardContinuation.BeforeTargetEffects or
                ProgramCardContinuation.BeforeTrickTargetEffects
                ? SkillProgramTriggerWindow.CardUseBeforeTargetEffects
                : frame.Action.Type == CardActionType.Use
                    ? SkillProgramTriggerWindow.CardUseTargetsFinalized
                    : SkillProgramTriggerWindow.CardResponseAccepted;

    private void ContinueProgramCardWindowCore() =>
        ProgramTriggerWindowDriver.Run(new CardActionTriggerWindowHost(this));

    private ProgramTriggerWindowPreparation<ProgramCardTriggerWindowFrame> PrepareProgramCardWindow(ProgramCardTriggerWindowFrame frame)
    {
        if (TryDrainCardSupplyCompletionCosts(frame)) return new(ProgramTriggerWindowFlow.Yield, frame);
        frame = (ProgramCardTriggerWindowFrame)_resolutionStack[^1];
        if (TryDrainCardResponseCompletionCosts(frame)) return new(ProgramTriggerWindowFlow.Yield, frame);
        frame = (ProgramCardTriggerWindowFrame)_resolutionStack[^1];
        if (TryBeginSoleTargetRedirect(frame)) return new(ProgramTriggerWindowFlow.Retry, frame);
        frame = AppendActorHandLimitTargetCandidates(frame);
        frame = AppendMouLiuChengAccrual(frame);
        frame = DeferFirstSlashDuelCandidates(frame);
        return new(ProgramTriggerWindowFlow.Ready, frame);
    }

    private ProgramTriggerWindowFlow CompleteProgramCardWindow(ProgramCardTriggerWindowFrame frame)
    {
        if (TryReturnSoleTargetRedirect(frame)) return ProgramTriggerWindowFlow.Retry;
        if (GetCardActionWindow(frame) == SkillProgramTriggerWindow.CardUseTargetsFinalized)
            SettleDamageMarkerBudgetTargets(frame.ParentFrameId);
        var attack = frame.AttackOwnerFrameId is { } attackId ? new CardAttackHandle(this, attackId) : null;
        PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramCardTriggerWindow);
        if (TryReturnLiuYanLegacyAlcohol(frame)) return ProgramTriggerWindowFlow.Yield;
        if (TryReturnCuiYanLegacyAlcohol(frame)) return ProgramTriggerWindowFlow.Yield;
        if (TryReturnZhuJunLegacyAlcohol(frame)) return ProgramTriggerWindowFlow.Yield;
        if (TryReturnMouBingcaiLegacyAlcohol(frame)) return ProgramTriggerWindowFlow.Yield;
        if (TryCompleteOverflowCanceledUse(frame)) return ProgramTriggerWindowFlow.Yield;
        if (GetCardActionWindow(frame) == SkillProgramTriggerWindow.CardUseTargetsFinalized &&
            TryCompletePijingZeroTargetUse(frame)) return ProgramTriggerWindowFlow.Yield;
        if (GetCardActionWindow(frame) == SkillProgramTriggerWindow.CardUseTargetsFinalized &&
            TryCompleteSoleTargetQuarantinedUse(frame.ParentFrameId)) return ProgramTriggerWindowFlow.Yield;
        if (frame.CardSupplyCompletion is not null)
            ContinueCardSupplyAfterCompletion(attack ?? throw new InvalidOperationException("A supplied card lost its native Slash."), frame);
        else if (frame.ResponseCompletion is not null)
            ContinueCardResponseAfterCompletion(attack, frame);
        else if (frame.Continuation is ProgramCardContinuation.CommittedSimpleCard or ProgramCardContinuation.FinalizedSimpleCard)
            ContinueSimpleCardUse(frame.ParentFrameId, frame.SimpleContinuation ??
                throw new InvalidOperationException("The simple card use lost its continuation."));
        else if (frame.Continuation == ProgramCardContinuation.CommittedTrick)
            ContinueCommittedTrickUse(frame);
        else if (frame.Continuation == ProgramCardContinuation.CompletedCard)
            PopFinishedCardUse(frame.ParentFrameId);
        else if (frame.CompletedResponseReturn is { IsCommitted: true })
            ContinueCommittedResponseUse(attack, frame);
        else if (frame.CompletedResponseReturn is not null)
            ContinueCompletedResponseUse(attack, frame);
        else if (frame.Continuation == ProgramCardContinuation.CommittedSlash)
            ContinueCommittedSlashAfterPrograms(attack ??
                throw new InvalidOperationException("A committed Slash trigger lost its attack continuation."));
        else if (frame.Continuation == ProgramCardContinuation.BeforeTargetEffects)
            ContinueSlashAfterProgramTargetEffects(attack ??
                throw new InvalidOperationException("A before-target-effects trigger lost its attack continuation."));
        else if (frame.Continuation == ProgramCardContinuation.SlashTargetRedirecting)
            ContinueSlashAfterRedirectPrograms(attack ??
                throw new InvalidOperationException("A Slash redirection trigger lost its attack continuation."));
        else if (frame.Continuation == ProgramCardContinuation.SlashBeforeResponse)
            ContinueSlashAfterResponsePrograms(attack ??
                throw new InvalidOperationException("A Slash response trigger lost its attack continuation."));
        else if (frame.Continuation == ProgramCardContinuation.SlashFullyDodged)
            ContinueSlashAfterDodgePrograms(attack ??
                throw new InvalidOperationException("A dodged Slash trigger lost its attack continuation."));
        else if (frame.Continuation is ProgramCardContinuation.BeforeTrickTargetEffects or ProgramCardContinuation.FinalizedTrick)
            ContinueTrickAfterProgramTargetEffects(frame);
        else if (frame.Continuation == ProgramCardContinuation.Slash)
            ContinueSlashAfterFinalizedTargets(attack ??
                throw new InvalidOperationException("A Slash card trigger lost its attack continuation."));
        else if (frame.Continuation == ProgramCardContinuation.CompletedSlash)
            ContinueCompletedSlashAfterPrograms(attack ??
                throw new InvalidOperationException("A completed Slash trigger lost its attack continuation."));
        else if (frame.Continuation == ProgramCardContinuation.DelayedCard)
            ContinueAcceptedDelayedCardUse(frame);
        else if (frame.Continuation == ProgramCardContinuation.FinalizedDelayedPlacement)
            ContinueFinalizedDelayedPlacement(frame);
        else if (frame.Continuation == ProgramCardContinuation.NullificationResponse)
            ContinueNullificationAfterResponseUse(frame.Action);
        else ContinueAcceptedCardResponse(attack ??
            throw new InvalidOperationException("A response card trigger lost its attack continuation."),
            frame.Action, frame.Continuation ??
            throw new InvalidOperationException("A response card trigger lost its return continuation."));
        return ProgramTriggerWindowFlow.Yield;
    }

    private void ContinueCompletedSlashAfterPrograms(CardAttackHandle attack)
    {
        var completion = CaptureAttackCompletion(attack);
        PopFinishedCardUse(attack.ResolutionId);
        CompleteFinishedAttackCardUse(attack);
        CompleteAttackAfterCardResolution(completion);
    }

    private bool TryBeginDelayedCardUsePrograms(long resolutionId)
    {
        if (!TryMarkProgramUseAccepted(resolutionId)) return false;
        TryMarkFinalizedTrickProgramsStarted(resolutionId);
        var frame = _resolutionStack.OfType<CardUseFrame>().Single(item => item.Id == resolutionId);
        var action = frame.Action ??
            throw new InvalidOperationException("A direct delayed-card trigger requires a captured card action.");
        AdvanceEventRulesAndQueueFact(new CardActionAcceptedEvent(action));
        return TryBeginProgramCardWindow(
            attack: null,
            action,
            SkillProgramTriggerWindow.CardUseTargetsFinalized,
            action.TargetSeats,
            ProgramCardContinuation.DelayedCard);
    }

    private void ContinueAcceptedDelayedCardUse(ProgramCardTriggerWindowFrame frame)
    {
        var action = frame.Action;
        if (action.EffectiveKind != CardKind.Lightning || action.PhysicalCards.Count != 1 ||
            action.TargetSeats is not [var targetSeat] || targetSeat != action.ActorSeat)
        {
            throw new InvalidOperationException("The delayed-card trigger continuation is not a self-targeted Lightning.");
        }
        var card = _cardZones.CardsAt(CardLocation.Processing)
            .Single(item => item.Id == action.PhysicalCards[0].CardId);
        BeginJizhiOrNullificationWindow(
            frame.ParentFrameId,
            card,
            action.ActorSeat,
            action.TargetSeats,
            LegalActionKind.Lightning,
            playedCardKind: action.EffectiveKind);
    }

    private void ContinueTrickAfterProgramTargetEffects(ProgramCardTriggerWindowFrame frame)
    {
        var continuation = frame.TrickContinuation ??
            throw new InvalidOperationException("A trick program window lost its frozen continuation.");
        var use = _resolutionStack.OfType<CardUseFrame>().Single(use => use.Id == frame.ParentFrameId);
        var action = use.Action!;
        if (action.Type != CardActionType.Use ||
            CardCatalog.Get(action.EffectiveKind).CategoryName != "锦囊牌" ||
            action.PhysicalCards.All(cost => cost.CardId != continuation.EffectCardId) &&
                !MatchesSelectedActorDuelAction(frame.ParentFrameId, action, continuation.EffectCardId) &&
                !MatchesTianChouShandaoUseAction(frame.ParentFrameId, action, continuation.EffectCardId) &&
                !MatchesQinLangXianyingUseAction(frame.ParentFrameId, action, continuation.EffectCardId) &&
                !MatchesVirtualOrdinaryTrickUseAction(frame.ParentFrameId, action, continuation.EffectCardId) &&
                !MatchesTieredRoundZeroUseAction(frame.ParentFrameId, action, continuation.EffectCardId) &&
                !MatchesMouZhijueUseAction(frame.ParentFrameId, action, continuation.EffectCardId) &&
                !MatchesMouLiuChengNativeAction(frame.ParentFrameId, action, continuation.EffectCardId) &&
                !MatchesMouXuHuangNativeAction(frame.ParentFrameId, action, continuation.EffectCardId) &&
                !MatchesChosenAssaultUseAction(frame.ParentFrameId, action, continuation.EffectCardId))
            throw new InvalidOperationException("The trick trigger continuation does not match its card action.");
        var card = GetTrickRepresentation(frame.ParentFrameId, continuation.EffectCardId, requireProcessing: true);
        var pending = new JizhiResolution(
            action.ActorSeat,
            frame.ParentFrameId,
            card,
            action.EffectiveKind,
            action.EffectiveKind == CardKind.BorrowedSword
                ? use.TargetSeats.Skip(use.TargetIndex).Take(2).ToArray()
                : action.TargetSeats,
            continuation.ActionKind,
            continuation.TargetCardId,
            continuation.RequiredCardKind);
        if (frame.Continuation == ProgramCardContinuation.FinalizedTrick && TryBeginProgramCardUseBeforeTargetEffects(pending)) return;
        ContinueJizhiOrNullificationAfterTargetTriggers(pending);
    }

    private void ContinueCommittedSlashAfterPrograms(CardAttackHandle attack)
    {
        if (_winner != Winner.None && (IsHuanpeiUse(attack.ResolutionId) || IsWeijieUse(attack.ResolutionId) || IsYouqueUse(attack.ResolutionId) || IsZongluanUse(attack.ResolutionId) || IsZonghuUse(attack.ResolutionId) || IsRoundOnceCardlessBasicUse(attack.ResolutionId)))
        {
            CompleteAttack(attack);
            return;
        }
        if (LifecycleCardUse(attack.ResolutionId)?.SlashTargetsCancelled == true)
        {
            UpdateLifecycleCardUse(attack.ResolutionId, use => use with { SlashTargetsCancelled = false });
            CompleteAttack(attack);
            return;
        }
        BeginSlashTargetResolution(attack);
    }

    private void AdvanceProgramCardCandidate(ProgramCardTriggerWindowFrame frame)
    {
        var candidate = frame.Candidates[frame.CandidateIndex];
        AdvanceEventRulesAndQueueFact(new ProgramCardTriggerResolvedEvent(frame.Id, candidate.SkillId, candidate.TriggerId,
            candidate.OwnerSeat, candidate.OpponentSeat, frame.Activated));
        ReplaceRuntimeTop(frame with
        {
            CandidateIndex = frame.CandidateIndex + 1,
            Activated = false
        });
    }

    // Legacy validation for a saved completed-card prompt. True Qinglong
    // insertions keep the original use unfinished and cannot enter this ride.
    private bool IsDrawFundedQinglongCompletedWindowRide(ProgramCardTriggerWindowFrame window)
    {
        var index = _resolutionStack.FindIndex(item => item.Id == window.Id);
        if (index < 1 || index + 1 >= _resolutionStack.Count ||
            _resolutionStack[index - 1] is not CardUseFrame original ||
            window.Continuation != ProgramCardContinuation.CompletedSlash ||
            window.ParentFrameId != original.Id || window.AttackOwnerFrameId != original.Id ||
            original.Step != ResolutionFrameStep.Completed || original.Action is not { Type: CardActionType.Use } action ||
            window.Action.ActionId != action.ActionId || !IsSlashCard(original.CardKind) ||
            original.CardAttack is not { } oldAttack || oldAttack.SuccessfulDodgeResponses != oldAttack.RequiredDodgeResponses ||
            original.Continuations.QinglongFollowup is not { Active: true, Decision: { } saved } suspension ||
            suspension.NextAttackOwnerId != original.Id || suspension.FangtianOwnerId is not null ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count) return false;
        var bound = _resolutionStack[index + 1] as ProgramSkillFrame;
        var followupIndex = index + (bound is null ? 1 : 2);
        if (followupIndex >= _resolutionStack.Count || _resolutionStack[followupIndex] is not CardUseFrame followup ||
            window.Activated != (bound is not null) ||
            !IsIssuedDrawFundedDistinctBasicUse(followup) || followup.DrawFundedDistinctBasicUse!.Payment is not
                { Intent: DrawFundedDistinctBasicIntent.Qinglong, Cursor: 0 } payment ||
            payment.ParentFrameId != original.Id || payment.RequestFrameId != original.Id || payment.ParentActionId != action.ActionId ||
            payment.Source.OwnerSeat != original.SourceSeat || payment.TargetSeat != oldAttack.TargetSeat ||
            followup.SourceSeat != original.SourceSeat || followup.Action!.ParentActionId != action.ActionId ||
            !followup.TargetSeats.SequenceEqual([oldAttack.TargetSeat]) ||
            !followup.Action.TargetSeats.SequenceEqual(followup.TargetSeats) || !IsSlashCard(followup.CardKind)) return false;
        var candidate = window.Candidates[window.CandidateIndex];
        var context = candidate.FrozenContext;
        if (context is not { Window: SkillProgramTriggerWindow.CardUseCompleted, CardUse: { } useContext } ||
            context.ParentFrameId != window.Id || useContext.ParentCardUseFrameId != original.Id ||
            useContext.CardActionId != action.ActionId || CreateCardActionProgramContext(window, candidate) != context ||
            saved.Kind != DecisionKind.ProgramTrigger || !saved.IsPrivate || saved.SkillPrompt?.SkillId != candidate.SkillId ||
            saved.Choices.Count == 0) return false;
        if (bound is null)
        {
            if (!GetProgramTrigger(ToSharedCandidate(candidate)).Optional ||
                saved.PlayerSeat != (context.OptionalChooserSeat ?? candidate.OwnerSeat) ||
                saved.SourceSeat != (context.OptionalChooserSeat is not null ? candidate.OwnerSeat : context.SourceSeat) ||
                saved.TargetSeat != (context.OptionalChooserSeat ?? context.TargetSeat ?? candidate.OwnerSeat) ||
                saved.Choices.Count != 2 ||
                !saved.Choices.Select(choice => choice.Parameters.GetValueOrDefault("program-action")).Order().SequenceEqual(["activate", "skip"]) ||
                saved.Choices.Any(choice => choice.Cards.Count != 0 || choice.Targets.Count != 0 ||
                    choice.Parameters.GetValueOrDefault("skill-id") != candidate.SkillId ||
                    choice.Parameters.GetValueOrDefault("binding-id") != candidate.TriggerId ||
                    choice.Parameters.GetValueOrDefault("skill-instance-id") != candidate.SkillInstanceId)) return false;
        }
        else
        {
            if (bound.WindowContext != context || bound.OwnerSeat != candidate.OwnerSeat || bound.SkillId != candidate.SkillId ||
                bound.SkillInstanceId != candidate.SkillInstanceId || bound.TriggerId != candidate.TriggerId ||
                bound.GameplayHash != candidate.GameplayHash || bound.InstructionIndex < 1) return false;
            var effect = ProgramInstructionResolver.Default.Resolve(bound, _contentRegistry.GetSkill(bound.SkillId).Program!)
                .GetPausedInstruction(bound.InstructionIndex).Effect;
            if (effect.Op != SkillProgramEffectOp.ChooseOption || bound.ChoiceBindings.Any(binding => binding.Name == effect.ResultBind) ||
                saved.PlayerSeat != (effect.ChooserRef is { } chooser ? ResolveProgramParticipant(bound, chooser) : ResolveProgramEffectTarget(bound, effect.Target)) ||
                saved.SourceSeat != bound.OwnerSeat || saved.TargetSeat != saved.PlayerSeat ||
                saved.Choices.Select(choice => choice.Id).Distinct().Count() != saved.Choices.Count ||
                saved.Choices.Any(choice => choice.Cards.Count != 0 || choice.Targets.Count != 0 ||
                    choice.Parameters.GetValueOrDefault("program-action") != "choose-option" ||
                    choice.Parameters.GetValueOrDefault("frame-id") != bound.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                    choice.Parameters.GetValueOrDefault("result-bind") != effect.ResultBind ||
                    !effect.Options.Any(option => option.Id == choice.Parameters.GetValueOrDefault("option-id") &&
                        choice.Id.Value == $"program-option.frame-{bound.Id}.{effect.ResultBind}.{option.Id}"))) return false;
        }
        return CompleteProgramEventHistory().OfType<QinglongCrescentBladeResolvedEvent>().LastOrDefault(fact => fact.ResolutionId == original.Id) is
            { Used: true, SlashCardIds.Count: 0 } issued && issued.SourceSeat == payment.Source.OwnerSeat &&
            issued.TargetSeat == payment.TargetSeat && issued.EffectiveSlashKind == payment.EffectiveKind &&
            CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().Where(fact => fact.ResolutionId == followup.Id).ToArray() is [var declared] &&
            declared.SourceSeat == payment.Source.OwnerSeat && declared.CardId == 0 && declared.CardKind == payment.EffectiveKind;
    }
    private void AssertProgramCardWindowState()
    {
        var frames = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().ToArray();
        if (frames.Length == 0)
        {
            return;
        }
        foreach (var frame in frames)
        {
            var frameIndex = _resolutionStack.FindLastIndex(item => ReferenceEquals(item, frame));
            if (frame.CardSupplyCompletion is { CostsDrained: false })
            {
                if (!ValidCardSupplyCompletionWindow(frame) || frameIndex < 1 ||
                    _resolutionStack[frameIndex - 1].Id != frame.ParentFrameId ||
                    frameIndex + 1 < _resolutionStack.Count && !CardSupplyCompletionFirstChild(frame, _resolutionStack[frameIndex + 1]))
                    throw new InvalidOperationException("A supplied card lost its exact native cost prelude.");
                continue;
            }
            if (frame.ResponseCompletion is { CostsDrained: false })
            {
                if (!ValidResponseCompletionWindow(frame) || frameIndex < 1 ||
                    _resolutionStack[frameIndex - 1].Id != frame.ParentFrameId ||
                    frameIndex + 1 < _resolutionStack.Count && !ResponseCompletionFirstChild(frame, _resolutionStack[frameIndex + 1]))
                    throw new InvalidOperationException("A response completion lost its exact native cost movement prelude.");
                continue;
            }
            var resolvingProgramJudgmentDamage =
                ActiveCardAttack is { IsProgramJudgmentDamage: true } &&
                ActiveJudgment?.Continuation == JudgmentContinuationKind.ProgramSkill;
            var suspendedCompletedGiftFollowup = frameIndex + 1 < _resolutionStack.Count &&
                _resolutionStack[frameIndex + 1] is ProgramSkillFrame gift &&
                gift.WindowContext?.ParentFrameId == frame.Id && IsUnselectedCompletedGiftQinglongRide(gift);
            var completedGiftRecipientFollowup = TryGetCompletedGiftRecipientSlashRide(frame, out var recipientUse);
            var suspendedGuoSiParent = LifecycleCardUse(frame.ParentFrameId) is { } guoSiParent &&
                IsSuspendedGuoSiCompletedParent(guoSiParent);
            var suspendedDiouParent = LifecycleCardUse(frame.ParentFrameId) is { } diouParent &&
                (IsDiouSuspendedCompletedAttack(diouParent, frame.Id) || IsMouLiuChengSuspendedCompletedAttack(diouParent, frame.Id));
            var drawFundedQinglongFollowup = IsDrawFundedQinglongCompletedWindowRide(frame) || IsChainedStateQinglongCompletedWindowRide(frame);
            var attackMatches = (HasHuiyunCompletedUseObserver(frame.ParentFrameId) || HasChenglieCompletedUseObserver(frame.ParentFrameId)) || HasGoudeCompletedSlashChild(frame) || HasFulueCompletedCounterChild(frame) || HasWuJingCompletedUseObserver(frame) || HasWoLongFengChuUseObserver(frame.ParentFrameId) || HasLvKuangLvXiangCardObserver(frame.ParentFrameId) || HasXinPiCardObserver(frame.ParentFrameId) || HasHuaManCardObserver(frame.ParentFrameId) || IsHuaManSouyingEmptyTargetWindow(frame) || HasSoleTargetProtectionCardObserver(frame.ParentFrameId) || frame.CardSupplyCompletion is not null && ValidCardSupplyCompletionWindow(frame) || HasPairedColorDispositionCardObserver(frame.ParentFrameId) || HasJoinedTrickDamageRewardCardObserver(frame.ParentFrameId) || HasRoundGainedEquipmentDrawCardObserver(frame.ParentFrameId) || HasSameNameHandCardObserver(frame.ParentFrameId) ||
                CompletedUndamagedTargetRevealObserverRoot()?.CompletedUndamagedTargetReveal?.CardUseFrameId == frame.ParentFrameId ||
                OverflowTargetCancellationObserverRoot()?.OverflowTargetCancellation?.CardUseFrameId == frame.ParentFrameId ||
                frame.ResponseCompletion is not null && ValidResponseCompletionWindow(frame) || HasSameTypeAidTargetObserver(frame.ParentFrameId) || frame != frames[^1] || resolvingProgramJudgmentDamage || suspendedCompletedGiftFollowup || completedGiftRecipientFollowup || suspendedGuoSiParent || suspendedDiouParent || drawFundedQinglongFollowup ||
                (frame.Continuation is ProgramCardContinuation.DelayedCard or ProgramCardContinuation.FinalizedDelayedPlacement or
                        ProgramCardContinuation.BeforeTrickTargetEffects or ProgramCardContinuation.FinalizedTrick or
                        ProgramCardContinuation.NullificationResponse or ProgramCardContinuation.CommittedTrick or
                        ProgramCardContinuation.CommittedSimpleCard or ProgramCardContinuation.FinalizedSimpleCard or ProgramCardContinuation.CompletedCard ||
                        frame.CompletedResponseReturn?.Kind == ProgramCompletedResponseKind.Nullification
                    ? frame.AttackOwnerFrameId is null
                    : ProgramCardAttack is not null && SameAttackOwner(ProgramCardAttack, ActiveCardAttack));
            var candidateCursorValid = frame.CandidateIndex >= 0 &&
                                       frame.CandidateIndex < frame.Candidates.Count;
            var candidate = candidateCursorValid ? frame.Candidates[frame.CandidateIndex] : null;
            var sharedContext = candidate?.FrozenContext;
            if (candidate is not null && sharedContext is not null)
                sharedContext = CreateCardActionProgramContext(frame, candidate);
            var sharedIdentityMatches = candidate is not null &&
                sharedContext is { CardUse: { } cardUse } && sharedContext.ParentFrameId == frame.Id &&
                cardUse.ParentCardUseFrameId == frame.ParentFrameId && cardUse.CardActionId == frame.Action.ActionId;
            var sharedPromptMatches = sharedIdentityMatches && ReferenceEquals(_resolutionStack.Last(), frame) &&
                _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } sharedPrompt &&
                sharedPrompt.PlayerSeat == (sharedContext!.OptionalChooserSeat ?? candidate!.OwnerSeat) && sharedPrompt.SkillPrompt?.SkillId == candidate!.SkillId &&
                sharedPrompt.Choices.Count > 0;
            var sharedChildMatches = sharedIdentityMatches && frameIndex + 1 < _resolutionStack.Count &&
                _resolutionStack[frameIndex + 1] is ProgramSkillFrame child &&
                child.WindowContext == sharedContext && child.OwnerSeat == candidate!.OwnerSeat &&
                child.SkillId == candidate.SkillId && child.SkillInstanceId == candidate.SkillInstanceId &&
                child.TriggerId == candidate.TriggerId && child.GameplayHash == candidate.GameplayHash;
            var trickContinuationMatches = frame.Continuation == ProgramCardContinuation.FinalizedDelayedPlacement
                ? IsExactFinalizedDelayedPlacementWindow(frame)
                : frame.Continuation is ProgramCardContinuation.BeforeTrickTargetEffects or ProgramCardContinuation.CommittedTrick or ProgramCardContinuation.FinalizedTrick
                ? frame.TrickContinuation is { } trick &&
                  frame.Action.Type == CardActionType.Use &&
                  (frame.Action.PhysicalCards.Any(cost => cost.CardId == trick.EffectCardId) ||
                   MatchesSelectedActorDuelAction(frame.ParentFrameId, frame.Action, trick.EffectCardId) ||
                   MatchesTianChouShandaoUseAction(frame.ParentFrameId, frame.Action, trick.EffectCardId) ||
                   MatchesQinLangXianyingUseAction(frame.ParentFrameId, frame.Action, trick.EffectCardId) ||
                   MatchesVirtualOrdinaryTrickUseAction(frame.ParentFrameId, frame.Action, trick.EffectCardId) ||
                   MatchesTieredRoundZeroUseAction(frame.ParentFrameId, frame.Action, trick.EffectCardId) ||
                   MatchesMouZhijueUseAction(frame.ParentFrameId, frame.Action, trick.EffectCardId) ||
                   MatchesMouLiuChengNativeAction(frame.ParentFrameId, frame.Action, trick.EffectCardId) ||
                   MatchesMouXuHuangNativeAction(frame.ParentFrameId, frame.Action, trick.EffectCardId) ||
                   MatchesChosenAssaultUseAction(frame.ParentFrameId, frame.Action, trick.EffectCardId))
                : frame.TrickContinuation is null;
            var completedResponseMatches = frame.CardSupplyCompletion is not null ? ValidCardSupplyCompletionWindow(frame) : frame.ResponseCompletion is not null ? ValidResponseCompletionWindow(frame) : frame.CompletedResponseReturn is { } completedResponse
                ? frame.Continuation is null && IsCompletedResponseUse(frame.Action, completedResponse.Kind) &&
                  IsCompletedResponseParentConsistent(frame)
                : frame.Continuation is not null;
            if (frame.SoleTargetRedirectReturn is not null && !IsExactSoleTargetRedirectReturn(frame) || !attackMatches ||
                frameIndex < 1 || _resolutionStack[frameIndex - 1].Id != frame.ParentFrameId ||
                !trickContinuationMatches || !completedResponseMatches ||
                !candidateCursorValid ||
                !sharedPromptMatches && !sharedChildMatches && !HasSameTypeAidTargetObserver(frame.ParentFrameId) && !HasWuJingCompletedUseObserver(frame) && !drawFundedQinglongFollowup ||
                 frame.Action.PhysicalCards.Where(cost =>
                     !(frame.CardSupplyCompletion is not null && ValidCardSupplyCompletionWindow(frame) && IsCardSupplyCompletionCostPaid(frame, cost)) &&
                     !(frame.ResponseCompletion is not null && ValidResponseCompletionWindow(frame) && IsResponseCompletionCostFinished(frame, cost)) &&
                     !(completedGiftRecipientFollowup && _cardZones.GetLocation(cost.CardId) == CardLocation.Processing &&
                       recipientUse.Action!.PhysicalCards.Any(payment => payment.CardId == cost.CardId)) &&
                     !(suspendedGuoSiParent && _cardZones.GetLocation(cost.CardId) == CardLocation.Processing &&
                       _resolutionStack.OfType<CardUseFrame>().Any(use => use.GuoSiNative is { } paid &&
                           paid.Material == cost.CardId && _resolutionStack.OfType<ProgramSkillFrame>().Any(program =>
                               program.WindowContext?.ParentFrameId == frame.Id && GuoSiNativeRidesOn(program, use)))) &&
                     !IsExchangedCardClaim(frame.Action.ActionId,cost.CardId) &&
                     !TryGetSiruiDestroyedResponseCost(frame.Action,cost.CardId,out _) &&
                     !(frame.Action.Type == CardActionType.Use && IsJiyunClaimedUseEntity(frame.ParentFrameId, cost.CardId)) &&
                     !(frame.CompletedResponseReturn is { } jiyunResponse &&
                       IsJiyunClaimedResponseEntity(frame.Action, jiyunResponse.Kind, cost.CardId)) &&
                     !IsSoleTargetQuarantinedMaterial(frame.ParentFrameId, cost.CardId) &&
                     GuanduChariotResponsePayment(frame.Action, frame.ParentFrameId, cost) is null &&
                     !(frame.Action.Type == CardActionType.Use &&
                       LifecycleCardUse(frame.ParentFrameId)?.Action is { } nativeAction &&
                       nativeAction.ActionId == frame.Action.ActionId && nativeAction.PhysicalCards.Contains(cost) &&
                       ((IsHellDestroyedSoulMaterial(frame.ParentFrameId, cost.CardId) || (IsHuiyunOutsideGeneralWeaponMaterial(frame.ParentFrameId, cost.CardId) || (IsSiruiOutsideGeneralWeaponMaterial(frame.ParentFrameId, cost.CardId) || IsMouLixianRemovedMaterial(frame.ParentFrameId, cost.CardId))) || IsGuanduDestroyedChariotMaterial(frame.ParentFrameId, cost.CardId)) || IsSelectedForeignCardSlashRemovedMaterial(frame.ParentFrameId, cost.CardId) || IsZhangYanSujiRemovedMaterial(frame.ParentFrameId, cost.CardId) || ((IsKenshangRemovedMaterial(frame.ParentFrameId, cost.CardId) || IsXieweiRemovedMaterial(frame.ParentFrameId, cost.CardId)) || (IsWuJingHejiRemovedMaterial(frame.ParentFrameId, cost.CardId) || IsSayingRemovedMaterial(frame.ParentFrameId, cost.CardId))))) &&
                     !(frame.Action.Type == CardActionType.Use &&
                       _resolutionStack.OfType<CardUseFrame>().Any(use => use.Id == frame.ParentFrameId && use.Action?.ActionId == frame.Action.ActionId) &&
                       (IsXinPiPhysicalClaim(frame.ParentFrameId, cost.CardId) || IsHuaManClaimedEntity(frame.ParentFrameId, cost.CardId) || IsCurrentUsePhysicalCardClaim(frame.ParentFrameId, cost.CardId)))).Any(cost =>
                     frame.CompletedResponseReturn is { IsCommitted: false } ||
                     frame.Continuation is ProgramCardContinuation.CompletedSlash or ProgramCardContinuation.CompletedCard
                         ? _cardZones.GetLocation(cost.CardId).Zone is not
                             (CardZoneKind.DiscardPile or CardZoneKind.Hand or CardZoneKind.DrawPile or CardZoneKind.Equipment or CardZoneKind.Judgment)
                         : (frame.Continuation == ProgramCardContinuation.NullificationResponse || frame.CompletedResponseReturn is { IsCommitted: true, Kind: ProgramCompletedResponseKind.Nullification })
                             ? _cardZones.GetLocation(cost.CardId) != CardLocation.DiscardPile
                         : IsProgramAlternativeCost(frame.Action, cost.CardId)
                             ? _cardZones.GetLocation(cost.CardId) == CardLocation.Processing
                         : frame.SimpleContinuation?.Effect == SimpleCardUseEffect.Equipment
                             ? _cardZones.GetLocation(cost.CardId) != CardLocation.Equipment(frame.Action.ActorSeat)
                         : IsForeignPublicPileSlashUse(frame.ParentFrameId) && cost.From.Zone == CardZoneKind.Authority
                             ? _cardZones.GetLocation(cost.CardId) != CardLocation.DiscardPile
                         : _cardZones.GetLocation(cost.CardId) != CardLocation.Processing))
                throw new InvalidOperationException($"A card trigger window has an invalid cursor, prompt or paid card (continuation={frame.Continuation}, attack={attackMatches}, cursor={candidateCursorValid}, prompt={sharedPromptMatches}, child={sharedChildMatches}, trick={trickContinuationMatches}, response={completedResponseMatches}, materials=[{string.Join(',', frame.Action.PhysicalCards.Select(cost => cost.CardKind + "/" + _cardZones.GetLocation(cost.CardId).Zone))}]).");
        }
    }
}
