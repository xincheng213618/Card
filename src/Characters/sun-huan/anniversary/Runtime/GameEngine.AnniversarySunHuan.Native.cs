namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static bool SameSunHuanAction(LegalAction a, LegalAction b) => a.Kind == b.Kind && a.CardId == b.CardId && a.PlayedCardKind == b.PlayedCardKind && a.TargetCardId == b.TargetCardId && a.ProgramActivationId == b.ProgramActivationId &&
        a.ConversionSource == b.ConversionSource && (a.AdditionalConversionSources ?? []).SequenceEqual(b.AdditionalConversionSources ?? []) && a.TargetSeats.SequenceEqual(b.TargetSeats);
    private IReadOnlyList<int> SunHuanExpectedTargets(AnniversarySunHuanNative n)
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
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 45400)] private void BindSunHuanNative(CardUseDeclaredEvent fact)
    {
        if (LifecycleCardUse(fact.ResolutionId) is not { Action: { Type: CardActionType.Use } a } use) return;
        var index = _resolutionStack.FindIndex(x => x.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame { AnniversarySunHuan: { Stage: AnniversarySunHuanStage.IssuingUse, Native: { UseId: 0, ActionId: 0 } n } r } f) return;
        if (!ValidSunHuan(f) || use.CardId != n.CardId || use.SourceSeat != f.OwnerSeat || use.CardKind != (n.Action.PlayedCardKind ?? GetAttackCard(n.CardId).Kind) ||
            a.ActorSeat != f.OwnerSeat || a.ProviderSeat != f.OwnerSeat || a.RequesterSeat is not null || a.PhysicalCards is not [var cost] || cost.CardId != n.CardId || cost.From != CardLocation.Hand(f.OwnerSeat) ||
            !a.ConversionChain.SequenceEqual((n.Action.ConversionSource is { } source ? new[] { source } : []).Concat(n.Action.AdditionalConversionSources ?? [])) || !use.TargetSeats.SequenceEqual(SunHuanExpectedTargets(n)))
            throw new InvalidOperationException("Niji must issue its exact drawn Hand entity, actual native kind, conversion and legal targets.");
        var bound = n with { UseId = use.Id, ActionId = a.ActionId };
        ReplaceRuntimeFrame(f.Id, f with { AnniversarySunHuan = r with { Stage = AnniversarySunHuanStage.NativeUse, Native = bound } });
        UpdateLifecycleCardUse(use.Id, u => u with { AnniversarySunHuanUse = bound });
        AdvanceEventRulesAndQueueFact(new AnniversarySunHuanUseIssuedEvent(f.Id, use.Id, a.ActionId, n.CardId));
    }
    private bool IsSunHuanNative(CardUseFrame use) => use.AnniversarySunHuanUse is { } n && use.Id == n.UseId && use.CardId == n.CardId && use.Action is { Type: CardActionType.Use } a &&
        a.ActionId == n.ActionId && a.ActorSeat == n.Actor && a.ProviderSeat == n.Actor && a.PhysicalCards is [var cost] && cost.CardId == n.CardId && cost.From == CardLocation.Hand(n.Actor) &&
        _resolutionStack.SingleOrDefault(f => f.Id == n.FrameId) is ProgramSkillFrame { AnniversarySunHuan.Native: { } pending } parent && ValidSunHuan(parent) && SameSunHuanNative(n, pending) &&
        ProgramEventHistory<AnniversarySunHuanUseIssuedEvent>().Count(e => e == new AnniversarySunHuanUseIssuedEvent(parent.Id, use.Id, n.ActionId, n.CardId)) == 1;
    private static bool SameSunHuanNative(AnniversarySunHuanNative a, AnniversarySunHuanNative b) => (a with { Action = b.Action }) == b && SameSunHuanAction(a.Action, b.Action);
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeSlashDebit, 45400)] private bool SunHuanOffTurnSlash(long id, int actor) => actor != _currentSeat && LifecycleCardUse(id) is { } use && use.AnniversarySunHuanUse?.Actor == actor && IsSunHuanNative(use);
    [NativeUseValidation(45400)] private void ValidateSunHuanNativeReturn(CardUseFrame use)
    { if (use.AnniversarySunHuanUse is not null && (use.Step != ResolutionFrameStep.Completed || !IsSunHuanNative(use))) throw new InvalidOperationException("Niji cannot return a different or unfinished native use."); }
    [NativeUseReturn(45400)] private void ReturnSunHuanNative(CardUseFrame use)
    {
        if (use.AnniversarySunHuanUse is not { } n) return;
        var f = GetActiveProgramFrame(n.FrameId); var r = f.AnniversarySunHuan!;
        if (!IsSunHuanNative(use) || r.Returned || LifecycleCardUse(use.Id) is not null || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id && e.CardId == n.CardId) != 1)
            throw new InvalidOperationException("Niji must wait for this physical use and its children exactly once.");
        ReplaceRuntimeFrame(f.Id, f with { AnniversarySunHuan = r with { Returned = true } });
        AdvanceEventRulesAndQueueFact(new AnniversarySunHuanUseReturnedEvent(f.Id, use.Id, n.ActionId));
        if (use.CardAttack is not { Active: true }) SettleSunHuanNative(GetActiveProgramFrame(f.Id));
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 45400)] private bool ReturnSunHuanAttack()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversarySunHuan: { Stage: AnniversarySunHuanStage.NativeUse, Returned: true } } f) return false;
        SettleSunHuanNative(f); return true;
    }
    private void SettleSunHuanNative(ProgramSkillFrame f)
    {
        var r = f.AnniversarySunHuan!; var n = r.Native!;
        if (!r.Returned || LifecycleCardUse(n.UseId) is not null) throw new InvalidOperationException("Niji must retain the native use through its actual completion.");
        var batches = _pendingCardsMovedBatches.Where(b => b.ParentFrameId == n.UseId && b.AwaitingProgramFrameId is null && b.OriginOwnerSeat == f.OwnerSeat && b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId).ToArray();
        foreach (var b in batches) AdvanceEventRulesAndQueueFact(new AnniversarySunHuanNativeBatchEvent(f.Id, n.UseId, b.Id));
        ReplaceRuntimeTop(f = f with { AnniversarySunHuan = r with { Stage = AnniversarySunHuanStage.NativeTail, After = _movementSequence, NativeBatches = batches.Select(b => b.Id).ToArray() }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        ContinueSunHuan(f.Id);
    }
    private bool MatchesSunHuanDeclaration(ProgramSkillFrame root, CardDeclarationFrame d) => root.AnniversarySunHuan is { Stage: AnniversarySunHuanStage.IssuingUse, Native: { UseId: 0, ActionId: 0 } n } r && ValidSunHuan(root) &&
        d.OwnerSeat == n.Actor && d.TurnNumber == r.Turn && d.Return is { Purpose: CardDeclarationPurpose.Use, UseAction: { } a } ret && ret.ParentFrameId == root.Id && ret.ParentKind == root.Kind && ret.ParentStep == root.Step &&
        ret.ActorSeat == n.Actor && SameSunHuanAction(a, n.Action) && d.Payment is { Claimed: false } p && p.DeclarationId == d.Id && p.OwnerFrameId == d.Id && p.ProviderSeat == n.Actor && p.ActorSeat == n.Actor &&
        p.Cost.CardId == n.CardId && p.Cost.From == CardLocation.Hand(n.Actor) && n.Action.ConversionSource == p.Source && ViewAsRule(p.Source) is { DeclarationValidation: not null } rule && rule.OutputKind == p.DeclaredKind &&
        ProgramEventHistory<CardDeclarationCommittedEvent>().Count(e => e.DeclarationId == d.Id && e.OwnerSeat == n.Actor && e.ActorSeat == n.Actor && e.DeclaredKind == p.DeclaredKind && e.TargetSeats.SequenceEqual(a.TargetSeats)) == 1;
    private bool IsSunHuanDeclarationQuery(CharacterState actor, int? id) => id is { } cardId && _resolutionStack.LastOrDefault() is CardDeclarationFrame { Stage: CardDeclarationStage.Applying, Succeeded: true } d &&
        _resolutionStack.Count > 1 && _resolutionStack[^2] is ProgramSkillFrame parent && MatchesSunHuanDeclaration(parent, d) && d.OwnerSeat == actor.Seat && d.Payment.Cost.CardId == cardId && UnclaimedDeclarationPayment(actor.Seat, cardId) == d.Payment;
    private bool TryReturnAnniversarySunHuanDeclaration(CardDeclarationFrame d, bool success)
    {
        if (success || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversarySunHuan: { Stage: AnniversarySunHuanStage.IssuingUse } r } f || !MatchesSunHuanDeclaration(f, d)) return false;
        if (_resolutionStack.Any(x => x.Id == d.Id) || r.Returned || r.Native!.UseId != 0) throw new InvalidOperationException("Niji declaration must return once to its original accepted use request.");
        ReplaceRuntimeTop(f = f with { AnniversarySunHuan = r with { Stage = AnniversarySunHuanStage.Choice, Native = null } }); DiscardSunHuan(f); return true;
    }
}
