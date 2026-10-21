namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed record ShenDaRepeatPlan(string Id, CardKind Kind, LegalActionKind ActionKind,
        IReadOnlyList<int> TargetSeats, string Description, int? TargetCardId = null, CardKind? RequiredKind = null);
    private CardUseFrame? ShenDaCuixinOriginal(ProgramSkillWindowContext w, int owner) => w.CardUse is { } c &&
        w.Window == SkillProgramTriggerWindow.CardUseCompleted && LifecycleCardUse(c.ParentCardUseFrameId) is
        { Step: ResolutionFrameStep.Completed, Action: { Type: CardActionType.Use, PhysicalCards.Count: > 0 } a } use &&
        use.ShenDengAiCuixin is null && a.ActionId == c.CardActionId && a.ActorSeat == owner && a.ProviderSeat == owner &&
        a.PhysicalCards.All(p => p.From == CardLocation.Hand(owner)) ? use : null;
    private IReadOnlyList<ShenDaRepeatPlan> ShenDaRepeatPlans(int owner, CardKind kind, IReadOnlyList<int> originalTargets)
    {
        var actor = _players[owner];
        if (!actor.IsAlive || _winner != Winner.None || IsCardUseForbidden(owner, kind, CardActionType.Use) ||
            !TargetRules.AllowsNativeUseMaterial(new(owner, kind, []))) return [];
        var prev = GetAdjacentLivingSeat(owner, -1); var next = GetAdjacentLivingSeat(owner, 1);
        var targets = new List<int>();
        if (prev is { } p && next is { } n && p != owner && n != owner)
        { if (originalTargets.Contains(p)) targets.Add(n); if (originalTargets.Contains(n)) targets.Add(p); }
        var result = new List<ShenDaRepeatPlan>();
        foreach (var target in targets.Distinct())
        {
            if (IsSlashCard(kind))
            {
                if (CanUseVirtualSlashTarget(actor, _players[target], kind, Suit.None, effectiveColor: null, effectiveRank: 0,
                    physicalCardIds: [], countsTowardLimit: false) && !IsDirectedCardTargetProhibited(owner, target, kind) &&
                    !IsCardTargetProhibited(_players[target], kind, Suit.None))
                    result.Add(new("slash:" + target, kind, LegalActionKind.Slash, [target], "摧心：视为对“" + _players[target].Name + "”使用【" + CardCatalog.Get(kind).DisplayName + "】"));
            }
            else if (IsOrdinaryTrick(kind))
            {
                foreach (var option in BuildProgramOrdinaryTrickUseOptions(actor, kind, Suit.None, enforceUsePermission: true,
                    beneficiaryShieldSuit: Suit.None, actualEffectiveColor: null, hasActualColor: true, physicalCardIds: []))
                {
                    var group = kind is CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.PeachGarden or CardKind.FiveGrains or
                        CardKind.AllianceFeast or CardKind.ReturnSoul or CardKind.StriveUpstream;
                    var eligible = group ? option.TargetSeats.Contains(target) : option.TargetSeats.Count > 0 && option.TargetSeats[0] == target;
                    if (!eligible || option.ActionKind == LegalActionKind.Recast) continue;
                    var seats = group || kind == CardKind.IronChain ? new[] { target } : option.TargetSeats.ToArray();
                    result.Add(new("trick:" + target + ":" + option.Id.Value, kind, option.ActionKind, seats,
                        "摧心：视为对“" + _players[target].Name + "”使用【" + CardCatalog.Get(kind).DisplayName + "】", option.TargetCardId, option.RequiredCardKind));
                }
            }
            else if (IsDelayedCard(kind) && kind != CardKind.Lightning)
            {
                var t = _players[target];
                if (!t.JudgmentAreaAbolished && !HasJudgmentEffectiveCard(t, kind) && !HasCardPolicy(t, SkillProgramCardPolicyKind.ProhibitDelayedTrickTarget) &&
                    (kind != CardKind.SupplyShortage || HasCardPolicy(actor, SkillProgramCardPolicyKind.IgnoreUseDistance, kind) || HasCardDistanceExemption(actor, t, kind) ||
                        GetCombatDistance(owner, target) <= GetCardUseDistanceLimit(actor, kind)) && !IsDirectedCardTargetProhibited(owner, target, kind) &&
                    !IsCardTargetProhibited(t, kind, Suit.None) && !IsShenwuRobeTargetProhibited(owner, target, kind) && !HasBeneficiarySuitShield(owner, target, Suit.None))
                    result.Add(new("delayed:" + target, kind, DelayedPlacementActionKind(kind), [target], "摧心：视为对“" + t.Name + "”使用【" + CardCatalog.Get(kind).DisplayName + "】"));
            }
        }
        return result.GroupBy(p => p.Id, StringComparer.Ordinal).Select(g => g.First()).ToArray();
    }
    private void IssueShenDaCuixin(ProgramSkillFrame f, string option)
    {
        var r = f.ShenDengAi!;
        var original = ShenDaCuixinOriginal(f.WindowContext!, f.OwnerSeat) ?? throw new InvalidOperationException("Cuixin lost its completed hand-card source.");
        var p = ShenDaRepeatPlans(f.OwnerSeat, r.OutputKind!.Value, original.TargetSeats).Single(o => o.Id == option);
        if (r.OriginalUseId != original.Id || r.OriginalActionId != original.Action!.ActionId || r.Stage != ShenDengAiStage.RepeatChoice)
            throw new InvalidOperationException("Cuixin cannot repeat a different original use.");
        r = r with { Stage = ShenDengAiStage.NativeUse, TargetSeat = p.TargetSeats[0],
            OriginalAttackActive = original.CardAttack?.Active == true, OriginalDuelActive = original.Continuations.Duel?.Active == true,
            OriginalGroupActive = original.Continuations.GroupCard?.Active == true };
        UpdateLifecycleCardUse(original.Id, u => u with { CardAttack = r.OriginalAttackActive ? u.CardAttack! with { Active = false } : u.CardAttack,
            Continuations = u.Continuations with { Duel = r.OriginalDuelActive ? u.Continuations.Duel! with { Active = false } : u.Continuations.Duel,
                GroupCard = r.OriginalGroupActive ? u.Continuations.GroupCard! with { Active = false } : u.Continuations.GroupCard } });
        ReplaceRuntimeTop(f = f with { ShenDengAi = r });
        AdvanceEventRulesAndQueueFact(new ShenDengAiCursorSuspendedEvent(f.Id, original.Id, r.OriginalActionId));
        Card card;
        if (IsDelayedCard(p.Kind))
        {
            var generated = checked(CreateCardZoneDiagnostics().Select(c => c.CardId).DefaultIfEmpty(0).Max() + 1);
            card = new(generated, p.Kind, Suit.None, 0); _cardZones.AddGeneratedCard(card); _generatedPhysicalCardIds.Add(generated);
            ReplaceRuntimeTop(f = f with { ShenDengAi = r = r with { CarrierId = generated } });
            AdvanceEventRulesAndQueueFact(new ShenDengAiCarrierCreatedEvent(f.Id, r.Source, f.GameplayHash, generated, p.Kind));
        }
        else card = new(0, p.Kind, Suit.None, 0);
        var id = BeginCardUse(card, f.OwnerSeat, p.TargetSeats, p.Kind, physicalCardIds: []);
        if (LifecycleCardUse(id)?.ShenDengAiCuixin is null) throw new InvalidOperationException("Cuixin must sign its genuine native Use before effects.");
        if (card.Id > 0) MoveCard(card, CardLocation.OutsideGame, CardLocation.Processing, new("skill-program.shen-deng-ai.cuixin.carrier"));
        if (IsSlashCard(p.Kind))
        {
            var actor = _players[f.OwnerSeat]; var targets = LifecycleCardUse(id)!.TargetSeats;
            var attack = new CardAttackHandle(this, id, f.OwnerSeat, targets[0], null, damageAmount: 1 + actor.AlcoholDamageBonus,
                playedCardKind: p.Kind, ignoresArmor: HasCardArmorBypass(actor, _players[targets[0]], p.Kind), programSkillCardUseFrameId: f.Id);
            ActiveCardAttack = attack; CaptureProgramAdjustedSlashBaseDamage(attack);
            MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, p.Kind); CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false;
            AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, p.Kind, actor.Seat, targets[0], IgnoresArmor: attack.IgnoresArmor));
            TryMarkProgramUseCommitted(id);
            if (!TryBeginProgramCardWindow(attack, LifecycleCardUse(id)!.Action!, SkillProgramTriggerWindow.CardUseCommitted, targets, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
            return;
        }
        BeginJizhiOrNullificationWindow(id, card, f.OwnerSeat, LifecycleCardUse(id)!.TargetSeats, p.ActionKind, p.TargetCardId, p.RequiredKind, p.Kind);
    }
    private CardActionContext PrepareShenDaCuixinAction(CardActionContext a)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenDengAi: { Stage: ShenDengAiStage.NativeUse, UseId: 0 } r } f) return a;
        if (f.SkillId != ShenDaCuixin || a.ActorSeat != f.OwnerSeat || a.ProviderSeat != f.OwnerSeat || a.PhysicalCards.Count != 0 || a.ConversionChain.Count != 0 || a.EffectiveKind != r.OutputKind)
            throw new InvalidOperationException("Cuixin cannot normalize another producer's action.");
        return CaptureFactionAction(new CardActionContext(a.ActionId, a.ParentActionId, a.Type, a.ActorSeat, a.ProviderSeat, a.RequesterSeat, a.ResponderSeat,
            a.OpponentSeat, a.EffectiveKind, a.TargetSeats, a.PhysicalCards, a.ConversionChain, a.DesignatedTargetSeats, effectiveSuit: Suit.None, effectiveRank: 0, effectiveIsRed: null));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 38501)]
    private void BindShenDaCuixin(CardUseDeclaredEvent e)
    {
        if (LifecycleCardUse(e.ResolutionId) is not { } use) return;
        var at = _resolutionStack.FindIndex(x => x.Id == use.Id);
        if (at < 1 || _resolutionStack[at - 1] is not ProgramSkillFrame { ShenDengAi: { Stage: ShenDengAiStage.NativeUse, UseId: 0 } r } f) return;
        AssertShenDa(f);
        if (use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0, ConversionChain.Count: 0, EffectiveSuit: Suit.None, EffectiveRank: 0 } a ||
            a.ActorSeat != f.OwnerSeat || a.ProviderSeat != f.OwnerSeat || a.RequesterSeat is not null || use.CardKind != r.OutputKind || use.CardId != r.CarrierId ||
            use.PhysicalCardIds is not { Count: 0 } || use.TargetSeats.Count == 0 || use.TargetSeats[0] != r.TargetSeat)
            throw new InvalidOperationException("Cuixin must issue exactly its accepted neutral native card and opposite-neighbor target.");
        var origin = new ShenDengAiNativeOrigin(f.Id, r.Source, f.GameplayHash, r.OriginalUseId, r.OriginalActionId, use.Id, a.ActionId, f.OwnerSeat, r.TargetSeat, use.CardKind, r.CarrierId);
        ReplaceRuntimeFrame(f.Id, f with { ShenDengAi = r with { UseId = use.Id, ActionId = a.ActionId } });
        UpdateLifecycleCardUse(use.Id, u => u with { ShenDengAiCuixin = origin }); AdvanceEventRulesAndQueueFact(new ShenDengAiNativeIssuedEvent(origin));
    }
    private bool IsShenDaCuixinUse(long id) => LifecycleCardUse(id) is { ShenDengAiCuixin: { } o, Action: { } a } use &&
        id == o.UseId && a.ActionId == o.ActionId && use.CardKind == o.Kind && use.CardId == o.CarrierId && use.PhysicalCardIds is { Count: 0 } &&
        a.Type == CardActionType.Use && a.EffectiveKind == o.Kind && a.PhysicalCards.Count == 0 && a.ConversionChain.Count == 0 &&
        a.EffectiveSuit == Suit.None && a.EffectiveRank == 0 && a.ProviderSeat == o.ActorSeat && a.RequesterSeat is null &&
        ShownEntityUseActorMatches(use, o.ActorSeat, o.ActorSeat) &&
        _resolutionStack.SingleOrDefault(f => f.Id == o.FrameId) is ProgramSkillFrame { ShenDengAi: { Stage: ShenDengAiStage.NativeUse } r } f &&
        r.UseId == id && r.ActionId == a.ActionId && r.Source == o.Source && r.GameplayHash == o.GameplayHash && f.SkillId == ShenDaCuixin &&
        ProgramEventHistory<ShenDengAiNativeIssuedEvent>().Count(e => e.Origin == o) == 1;
    private bool IsShenDaCuixinTrick(long id) => LifecycleCardUse(id)?.CardKind is { } kind && IsOrdinaryTrick(kind) && IsShenDaCuixinUse(id);
    private Card ReadShenDaCuixinAppearance(long id, CardAppearanceReference a) => IsShenDaCuixinUse(id) &&
        LifecycleCardUse(id) is { } u && a.Id == u.CardId && a.Kind == u.CardKind && a.Suit == Suit.None && a.Rank == 0
        ? new(a.Id, a.Kind, Suit.None, 0) : throw new InvalidOperationException("Cuixin lost its exact native continuation appearance.");
    private IReadOnlyList<int>? ShenDaCuixinAncestorProcessing(long id)
    {
        if (!IsShenDaCuixinUse(id)) return null;
        var index = _resolutionStack.FindIndex(x => x.Id == LifecycleCardUse(id)!.ShenDengAiCuixin!.FrameId);
        return _resolutionStack.Take(index).OfType<CardUseFrame>().SelectMany(u => u.PhysicalCardIds ?? []).Where(c => c > 0 && _cardZones.GetLocation(c) == CardLocation.Processing).Distinct().ToArray();
    }
    private bool IsShenDaCuixinAttackConsistent(CardAttackHandle a, IReadOnlyList<Card> processing)
    {
        if (!IsShenDaCuixinUse(a.ResolutionId) || LifecycleCardUse(a.ResolutionId) is not { ShenDengAiCuixin: { } o } use ||
            a.Card is not null || a.PhysicalCards.Count != 0 || a.EffectiveCardKind != use.CardKind ||
            IsSlashCard(use.CardKind) && a.ProgramSkillCardUseFrameId != o.FrameId ||
            a.SourceSeat != use.SourceSeat && !(use.CardKind == CardKind.Duel && use.TargetSeats.Contains(a.SourceSeat))) return false;
        return processing.Select(c => c.Id).Order().SequenceEqual((ShenDaCuixinAncestorProcessing(use.Id) ?? []).Order());
    }
    [ProgramDamageHook(ProgramDamagePhase.IssuedAttackCompletion, 38500)]
    private bool FinishShenDaCuixinAttack(CardAttackHandle a, out bool paused)
    {
        paused = false;
        if (a.Card is not null || !IsShenDaCuixinUse(a.ResolutionId) || LifecycleCardUse(a.ResolutionId) is not { } use ||
            !IsSlashCard(use.CardKind) && use.CardKind is not (CardKind.Duel or CardKind.FireAttack or CardKind.UnexpectedAssault)) return false;
        SetCardUseStep(use.Id, ResolutionFrameStep.Completed); AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, use.CardId, use.CardKind));
        if (_winner == Winner.None && TryBeginProgramCardWindow(a, use.Action!, SkillProgramTriggerWindow.CardUseCompleted, use.TargetSeats, ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: a.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(a); }
        return true;
    }
    [NativeUseValidation(38500)]
    private void ValidateShenDaCuixin(CardUseFrame u)
    { if (u.ShenDengAiCuixin is not null && (!IsShenDaCuixinUse(u.Id) || u.Step != ResolutionFrameStep.Completed)) throw new InvalidOperationException("Cuixin must finish its exact whole native Use."); }
    [NativeUseReturn(38500)]
    private void ReturnShenDaCuixin(CardUseFrame u)
    {
        if (u.ShenDengAiCuixin is not { } o) return;
        var f = GetActiveProgramFrame(o.FrameId); var r = f.ShenDengAi!;
        if (LifecycleCardUse(u.Id) is not null || r.UseId != u.Id || r.ActionId != o.ActionId || r.Returned ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == u.Id) != 1) throw new InvalidOperationException("Cuixin cannot return or issue twice.");
        foreach (var b in _pendingCardsMovedBatches.Where(b => b.ParentFrameId == u.Id && b.AwaitingProgramFrameId is null &&
            b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId && b.OriginOwnerSeat == f.OwnerSeat))
            AdvanceEventRulesAndQueueFact(new ShenDengAiNativeBatchEvent(f.Id, u.Id, b.Id));
        ReplaceRuntimeTop(f with { ShenDengAi = r with { Stage = ShenDengAiStage.NativeChildren, Returned = true }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new ShenDengAiNativeReturnedEvent(f.Id, u.Id, o.ActionId));
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 38500)]
    private bool ReturnShenDaCuixinAttack(AttackCompletionReceipt completion)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenDengAi: { Stage: ShenDengAiStage.NativeChildren } r } f || r.UseId != completion.ResolutionId) return false; ContinueShenDa(f.Id); return true; }
    private bool IsShenDaCuixinSettlementBatch(ProgramSkillFrame f, CardMovementBatchContext b) => f.ShenDengAi is
        { Stage: ShenDengAiStage.NativeChildren, Returned: true } r && b.ParentFrameId == r.UseId && b.AwaitingProgramFrameId is null &&
        b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId && b.OriginOwnerSeat == f.OwnerSeat &&
        ProgramEventHistory<ShenDengAiNativeBatchEvent>().Count(e => e.FrameId == f.Id && e.UseId == r.UseId && e.BatchId == b.Id) == 1;
    private bool HasShenDaSuspendedCompletedParent(ProgramCardTriggerWindowFrame w)
    {
        var i = _resolutionStack.FindIndex(f => f.Id == w.Id);
        if (w.Continuation != ProgramCardContinuation.CompletedSlash || i < 1 || i + 1 >= _resolutionStack.Count ||
            _resolutionStack[i - 1] is not CardUseFrame { Step: ResolutionFrameStep.Completed, Action: { } a } u || u.Id != w.ParentFrameId ||
            a.ActionId != w.Action.ActionId || _resolutionStack[i + 1] is not ProgramSkillFrame { ShenDengAi: { OriginalAttackActive: true } r } f ||
            f.SkillId != ShenDaCuixin || f.WindowContext?.ParentFrameId != w.Id || r.OriginalUseId != u.Id || r.OriginalActionId != a.ActionId || u.CardAttack?.Active != false) return false;
        return ProgramEventHistory<ShenDengAiCursorSuspendedEvent>().Count(e => e.FrameId == f.Id && e.OriginalUseId == u.Id && e.OriginalActionId == a.ActionId) == 1;
    }
    private void RestoreShenDaCuixinCursor(ProgramSkillFrame f)
    {
        if (f.ShenDengAi is not { OriginalUseId: > 0 } r || !ProgramEventHistory<ShenDengAiCursorSuspendedEvent>().Any(e => e.FrameId == f.Id)) return;
        if (LifecycleCardUse(r.UseId) is not null || !r.Returned || LifecycleCardUse(r.OriginalUseId) is not { Action: { } a } original || a.ActionId != r.OriginalActionId)
            throw new InvalidOperationException("Cuixin lost its suspended original card cursor.");
        UpdateLifecycleCardUse(original.Id, u => u with { CardAttack = r.OriginalAttackActive ? u.CardAttack! with { Active = true } : u.CardAttack,
            Continuations = u.Continuations with { Duel = r.OriginalDuelActive ? u.Continuations.Duel! with { Active = true } : u.Continuations.Duel,
                GroupCard = r.OriginalGroupActive ? u.Continuations.GroupCard! with { Active = true } : u.Continuations.GroupCard } });
        AdvanceEventRulesAndQueueFact(new ShenDengAiCursorRestoredEvent(f.Id, original.Id, a.ActionId));
    }
}
