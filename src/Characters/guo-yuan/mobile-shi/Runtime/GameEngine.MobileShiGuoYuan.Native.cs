using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsShiGyHandQuery(CharacterState actor, int? id)
    {
        if (IsShiGyDeclarationQuery(actor, id)) return true;
        return id is > 0 && actor.IsAlive && _cardZones.GetLocation(id.Value) == CardLocation.Hand(actor.Seat) &&
            _resolutionStack.LastOrDefault() is ProgramSkillFrame { ShiGuoYuan: { Stage: ShiGyStage.UseChoice or ShiGyStage.IssuingUse, Damaged: false } r } f &&
            f.OwnerSeat == actor.Seat && ValidShiGy(f) && (r.Stage == ShiGyStage.UseChoice || r.Native is { UseId: 0, ActionId: 0 } n && n.CardId == id);
    }
    private IReadOnlyList<LegalAction> ShiGyUseActions(ProgramSkillFrame f) => !_players[f.OwnerSeat].IsAlive || _winner != Winner.None ? [] :
        GetHand(_players[f.OwnerSeat]).SelectMany(c => BuildLegalActions(_players[f.OwnerSeat], false, c.Id).Where(a => a.CardId == c.Id && a.MinCardCount <= 1 && a.MaxCardCount <= 1 &&
            (a.ConversionSource is null || ViewAsRule(a.ConversionSource) is not { } rule || rule.InputCount == 1) &&
            a.Kind is not (LegalActionKind.EndPlay or LegalActionKind.Recast or LegalActionKind.UseProgramSkill or LegalActionKind.UseEquipmentEffect or LegalActionKind.RevealGeneral)))
            .Select(ShiGyFreezeAction).ToArray();
    private static LegalAction ShiGyFreezeAction(LegalAction a) => a with
    {
        TargetSeats = Array.AsReadOnly(a.TargetSeats.ToArray()), AdditionalConversionSources = Array.AsReadOnly((a.AdditionalConversionSources ?? []).ToArray()),
        SelectableCardIds = Array.AsReadOnly(a.SelectableCardIds.ToArray()), SelectableTargetSeats = Array.AsReadOnly(a.SelectableTargetSeats.ToArray()),
        SelectableCardTargets = a.SelectableCardTargets is null ? null : Array.AsReadOnly(a.SelectableCardTargets.ToArray()),
        SelectableTargetSequences = a.SelectableTargetSequences is null ? null : Array.AsReadOnly(a.SelectableTargetSequences.Select(s => (IReadOnlyList<int>)Array.AsReadOnly(s.ToArray())).ToArray())
    };
    private IReadOnlyList<PromptChoice> ShiGyUseChoices(ProgramSkillFrame f) => ShiGyUseActions(f).Select((a, i) => new PromptChoice(new($"shi-guo-yuan.{f.Id}.use.{i}"), a.Description,
        [a.CardId!.Value], a.TargetSeats, new Dictionary<string, string> { ["program-action"] = "shi-guo-yuan", ["branch"] = "native", ["index"] = i.ToString(CultureInfo.InvariantCulture) }))
        .Append(new(new($"shi-guo-yuan.{f.Id}.use.skip"), "不使用手牌", [], [], new Dictionary<string, string> { ["program-action"] = "shi-guo-yuan", ["branch"] = "skip" })).ToArray();
    private void AnswerShiGyUse(ProgramSkillFrame f, PromptChoice choice)
    {
        if (choice.Parameters["branch"] == "skip") { FinishShiGy(f); return; }
        var action = ShiGyUseActions(f)[int.Parse(choice.Parameters["index"], CultureInfo.InvariantCulture)]; var r = f.ShiGuoYuan!;
        var n = new ShiGyNativeUse(f.Id, r.Source, r.Hash, r.Turn, f.OwnerSeat, action.CardId!.Value, action, 0, 0);
        ReplaceRuntimeTop(f with { ShiGuoYuan = r with { Stage = ShiGyStage.IssuingUse, Native = n, Before = _movementSequence, After = _movementSequence } });
        ExecuteAction(_players[f.OwnerSeat], action);
    }
    private static bool SameShiGyAction(LegalAction a, LegalAction b) => a.Kind == b.Kind && a.CardId == b.CardId && a.PlayedCardKind == b.PlayedCardKind && a.TargetCardId == b.TargetCardId &&
        a.ProgramActivationId == b.ProgramActivationId && a.ConversionSource == b.ConversionSource && (a.AdditionalConversionSources ?? []).SequenceEqual(b.AdditionalConversionSources ?? []) && a.TargetSeats.SequenceEqual(b.TargetSeats);
    private static bool SameShiGyNative(ShiGyNativeUse a, ShiGyNativeUse b) => a with { Action = b.Action } == b && SameShiGyAction(a.Action, b.Action);
    private IReadOnlyList<int> ShiGyExpectedTargets(ShiGyNativeUse n)
    {
        var a = n.Action; var actor = _players[n.Actor]; var card = ApplyProgramUseAppearance(actor, GetAttackCard(n.CardId), a.ConversionSource);
        if (a.ConversionSource is { } source && ViewAsRule(source) is { SingleCardTrickUse: true } rule)
            return BuildProgramOrdinaryTrickUseOptions(actor, rule.OutputKind, EffectiveSuit(actor, GetAttackCard(n.CardId)), rule.ExcludeOwnerEffects, enforceUsePermission: true,
                beneficiaryShieldSuit: EffectiveSuit(actor, card), actualEffectiveColor: SuitColor(EffectiveSuit(actor, card)), hasActualColor: true, physicalCardIds: [n.CardId])
                .Where(o => o.ActionKind == a.Kind && o.TargetCardId == a.TargetCardId && (o.TargetSeats.SequenceEqual(a.TargetSeats) || a.ProgramActivationId == "red-additional-targets" && o.TargetSeats.SequenceEqual(a.TargetSeats.Take(o.TargetSeats.Count))))
                .OrderByDescending(o => o.TargetSeats.Count).Select(o => o.TargetSeats).First();
        if (GuanduExpectedNativeTargets(actor, card, a) is { } guandu) return guandu;
        if (DragonBoatExpectedNativeTargets(actor, card, a) is { } dragon) return dragon;
        if (a.Kind == LegalActionKind.Peach) return [actor.Seat];
        if (a.Kind is not (LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage or LegalActionKind.PeachGarden or LegalActionKind.FiveGrains))
            return GetImplicitSelfCardUseTargets(a.PlayedCardKind ?? card.Kind, actor.Seat, a.TargetSeats);
        var kind = a.PlayedCardKind ?? card.Kind; var offensive = a.Kind is LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage;
        var seats = Enumerable.Range(offensive ? 1 : 0, _playerCount - (offensive ? 1 : 0)).Select(i => _players[TableStepSeat(actor.Seat, i)])
            .Where(p => p.IsAlive && (!offensive || !HasCardPolicy(p, SkillProgramCardPolicyKind.ExcludeGlobalTarget, kind)) && !IsCardTargetProhibited(p, kind, card.Suit, SuitColor(EffectiveSuit(actor, card))) && !HasBeneficiarySuitShield(actor.Seat, p.Seat, EffectiveSuit(actor, card)))
            .Select(p => p.Seat).ToArray();
        if (!offensive) seats = ApplyTurnCardGroupTargetRestrictions(actor, seats).ToArray();
        if (a.Kind == LegalActionKind.FiveGrains) seats = seats.Take(_cardZones.Count(CardLocation.DrawPile) + _cardZones.Count(CardLocation.DiscardPile)).ToArray();
        return Array.AsReadOnly(seats);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 48100)] private void BindShiGyNative(CardUseDeclaredEvent fact)
    {
        if (LifecycleCardUse(fact.ResolutionId) is not { Action: { Type: CardActionType.Use } a } use) return;
        var index = _resolutionStack.FindIndex(x => x.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame { ShiGuoYuan: { Stage: ShiGyStage.IssuingUse, Native: { UseId: 0, ActionId: 0 } n } r } f) return;
        if (!ValidShiGy(f) || use.CardId != n.CardId || use.SourceSeat != n.Actor || use.CardKind != (n.Action.PlayedCardKind ?? GetAttackCard(n.CardId).Kind) ||
            a.ActorSeat != n.Actor || a.ProviderSeat != n.Actor || a.RequesterSeat is not null || a.PhysicalCards is not [var cost] || cost.CardId != n.CardId || cost.From != CardLocation.Hand(n.Actor) ||
            !a.ConversionChain.SequenceEqual((n.Action.ConversionSource is { } source ? new[] { source } : []).Concat(n.Action.AdditionalConversionSources ?? [])) || !use.TargetSeats.SequenceEqual(ShiGyExpectedTargets(n)))
            throw new InvalidOperationException("Qingdao must issue the exact legal one-Hand entity with its native actor, targets and conversions.");
        var bound = n with { UseId = use.Id, ActionId = a.ActionId };
        ReplaceRuntimeFrame(f.Id, f with { ShiGuoYuan = r with { Stage = ShiGyStage.NativeUse, Native = bound } });
        UpdateLifecycleCardUse(use.Id, u => u with { ShiGuoYuanUse = bound });
        AdvanceEventRulesAndQueueFact(new ShiGyNativeIssuedEvent(f.Id, use.Id, a.ActionId, n.CardId));
    }
    private bool IsShiGyNative(CardUseFrame use) => use.ShiGuoYuanUse is { } n && use.Id == n.UseId && use.CardId == n.CardId && use.Action is { Type: CardActionType.Use } a &&
        a.ActionId == n.ActionId && a.ProviderSeat == n.Actor && ShownEntityUseActorMatches(use, n.Actor, n.Actor) && a.PhysicalCards is [var cost] && cost.CardId == n.CardId && cost.From == CardLocation.Hand(n.Actor) &&
        _resolutionStack.SingleOrDefault(x => x.Id == n.FrameId) is ProgramSkillFrame { ShiGuoYuan.Native: { } pending } parent && ValidShiGy(parent) && SameShiGyNative(n, pending) &&
        ProgramEventHistory<ShiGyNativeIssuedEvent>().Count(e => e == new ShiGyNativeIssuedEvent(parent.Id, use.Id, a.ActionId, n.CardId)) == 1;
    private bool HasShiGyUseDistance(int actor, IReadOnlyList<int>? ids, long? useId)
    {
        var use = useId is { } id ? LifecycleCardUse(id) : _resolutionStack.LastOrDefault() as CardUseFrame;
        if (use is null || use.SourceSeat != actor || ids is { Count: > 0 } && !ids.SequenceEqual(use.PhysicalCardIds ?? [])) return false;
        if (use.ShiGuoYuanUse is not null) return IsShiGyNative(use);
        // BeginCardUse has pushed its exact child before the declaration fact
        // binds the invoice. Target verification still needs this issued grant.
        var index = _resolutionStack.FindIndex(x => x.Id == use.Id);
        return index > 0 && _resolutionStack[index - 1] is ProgramSkillFrame { ShiGuoYuan: { Stage: ShiGyStage.IssuingUse, Native: { UseId: 0, ActionId: 0 } n } } parent &&
            ValidShiGy(parent) && n.Actor == actor && n.CardId == use.CardId && use.Action is { Type: CardActionType.Use, PhysicalCards: [var cost] } a &&
            a.ActorSeat == actor && a.ProviderSeat == actor && cost.CardId == n.CardId && cost.From == CardLocation.Hand(actor) &&
            a.ConversionChain.SequenceEqual((n.Action.ConversionSource is { } source ? new[] { source } : []).Concat(n.Action.AdditionalConversionSources ?? []));
    }
    [NativeUseValidation(48100)] private void ValidateShiGyNative(CardUseFrame use)
    { if (use.ShiGuoYuanUse is not null && (use.Step != ResolutionFrameStep.Completed || !IsShiGyNative(use))) throw new InvalidOperationException("Qingdao lost its exact completed child use."); }
    [NativeUseReturn(48100)] private void ReturnShiGyNative(CardUseFrame use)
    {
        if (use.ShiGuoYuanUse is not { } n) return;
        var f = GetActiveProgramFrame(n.FrameId); var r = f.ShiGuoYuan!;
        if (!IsShiGyNative(use) || r.Returned || LifecycleCardUse(use.Id) is not null || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id && e.CardId == n.CardId) != 1)
            throw new InvalidOperationException("Qingdao cannot return or repay an unfinished or different native use.");
        ReplaceRuntimeTop(f = f with { ShiGuoYuan = r with { Returned = true } });
        AdvanceEventRulesAndQueueFact(new ShiGyNativeReturnedEvent(f.Id, use.Id, n.ActionId));
        if (use.CardAttack is not { Active: true }) SettleShiGyNative(f);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 48100)] private bool ReturnShiGyAttack()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiGuoYuan: { Stage: ShiGyStage.NativeUse, Returned: true } } f) return false;
        SettleShiGyNative(f); return true;
    }
    private void SettleShiGyNative(ProgramSkillFrame f)
    {
        var r = f.ShiGuoYuan!; var n = r.Native!;
        if (!r.Returned || LifecycleCardUse(n.UseId) is not null) throw new InvalidOperationException("Qingdao still owns an unreturned use.");
        var batches = _pendingCardsMovedBatches.Where(b => b.ParentFrameId == n.UseId && b.AwaitingProgramFrameId is null && b.OriginOwnerSeat == f.OwnerSeat && b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId).ToArray();
        foreach (var b in batches) AdvanceEventRulesAndQueueFact(new ShiGyNativeBatchEvent(f.Id, n.UseId, b.Id));
        ReplaceRuntimeTop(f = f with { ShiGuoYuan = r with { Stage = ShiGyStage.NativeTail, After = _movementSequence, NativeBatches = batches.Select(b => b.Id).ToArray() }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        ContinueShiGy(f.Id);
    }
    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 48100)] private bool ShiGyCardDistance(ProgramCardDistanceQuery q) =>
        HasShiGyUseDistance(q.Actor.Seat, q.PhysicalCardIds, q.CardUseFrameId) ||
        q.PhysicalCardIds is [var card] && IsShiGyHandQuery(q.Actor, card);
    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 48100)] [ProgramDistanceRule(ProgramDistanceStage.VirtualSlash, 48100)]
    [ProgramDistanceRule(ProgramDistanceStage.ProvidedSlash, 48100)] [ProgramDistanceRule(ProgramDistanceStage.BorrowedSlash, 48100)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPhysicalSlash, 48100)] [ProgramDistanceRule(ProgramDistanceStage.SpecificSlashExemption, 48100)]
    private bool ShiGySlashDistance(ProgramSlashDistanceQuery q) => HasShiGyUseDistance(q.Actor.Seat, q.PhysicalCardIds ?? (q.PhysicalCard is { } c ? [c.Id] : null), q.ExistingUseFrameId) ||
        IsShiGyHandQuery(q.Actor, q.PhysicalCardIds is [var card] ? card : q.PhysicalCard?.Id);
    private bool IsShiGyOffTurnQuery(CharacterState actor, int? card) => (actor.Seat != _currentSeat || _phase != TurnPhase.Play) && IsShiGyHandQuery(actor, card);
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit, 48100)] private bool ShiGySlashQuota(ProgramSlashQuotaQuery q) => IsShiGyOffTurnQuery(q.Actor, q.PhysicalCardIds is [var card] ? card : q.PhysicalCard?.Id);
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeSlashDebit, 48100)] private bool ShiGyOffTurnSlash(long id, int actor) =>
        (actor != _currentSeat || _phase != TurnPhase.Play) && LifecycleCardUse(id) is { ShiGuoYuanUse: { } n } use && n.Actor == actor && IsShiGyNative(use);
    private bool MatchesShiGyDeclaration(ProgramSkillFrame root, CardDeclarationFrame d) => root.ShiGuoYuan is { Stage: ShiGyStage.IssuingUse, Native: { UseId: 0, ActionId: 0 } n } r && ValidShiGy(root) &&
        d.OwnerSeat == n.Actor && d.TurnNumber == r.Turn && d.Return is { Purpose: CardDeclarationPurpose.Use, UseAction: { } a } ret && ret.ParentFrameId == root.Id && ret.ParentKind == root.Kind && ret.ParentStep == root.Step &&
        ret.ActorSeat == n.Actor && SameShiGyAction(a, n.Action) && d.Payment is { Claimed: false } p && p.DeclarationId == d.Id && p.OwnerFrameId == d.Id && p.ProviderSeat == n.Actor && p.ActorSeat == n.Actor &&
        p.Cost.CardId == n.CardId && p.Cost.From == CardLocation.Hand(n.Actor) && n.Action.ConversionSource == p.Source && ViewAsRule(p.Source) is { DeclarationValidation: not null } rule && rule.OutputKind == p.DeclaredKind &&
        ProgramEventHistory<CardDeclarationCommittedEvent>().Count(e => e.DeclarationId == d.Id && e.OwnerSeat == n.Actor && e.ActorSeat == n.Actor && e.DeclaredKind == p.DeclaredKind && e.TargetSeats.SequenceEqual(a.TargetSeats)) == 1;
    private bool IsShiGyDeclarationQuery(CharacterState actor, int? id) => id is { } card && _resolutionStack.LastOrDefault() is CardDeclarationFrame { Stage: CardDeclarationStage.Applying, Succeeded: true } d &&
        _resolutionStack.Count > 1 && _resolutionStack[^2] is ProgramSkillFrame root && MatchesShiGyDeclaration(root, d) && d.OwnerSeat == actor.Seat && d.Payment.Cost.CardId == card && UnclaimedDeclarationPayment(actor.Seat, card) == d.Payment;
    private bool TryReturnShiGyDeclaration(CardDeclarationFrame d, bool success)
    {
        if (success || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiGuoYuan: { Stage: ShiGyStage.IssuingUse } r } f || !MatchesShiGyDeclaration(f, d)) return false;
        ReplaceRuntimeTop(f = f with { ShiGuoYuan = r with { Stage = ShiGyStage.Movement, Native = null, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        ContinueShiGy(f.Id); return true;
    }
}
