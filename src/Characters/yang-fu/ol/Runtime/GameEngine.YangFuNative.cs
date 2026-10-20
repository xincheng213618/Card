namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void BindSuyiEquipmentUse(CardUseFrame use)
    {
        var at = _resolutionStack.FindIndex(frame => frame.Id == use.Id);
        if (at < 1 || _resolutionStack[at - 1] is not ProgramSkillFrame { YangFu: { Op: SkillProgramEffectOp.ResolveSuyiEquipmentThenSlash, Stage: YangFuStage.EquipmentUse } r } f) return;
        if (r.NativeOrigin is not null || use.Action is not { Type: CardActionType.Use } action || use.SourceSeat != r.TargetSeat || action.ActorSeat != r.TargetSeat || action.ProviderSeat != r.TargetSeat ||
            !EquipmentCatalog.IsEquipment(use.CardKind) || use.PhysicalCardIds is not [var id] || id != use.CardId || action.PhysicalCards is not [var cost] || cost.CardId != id || cost.From != CardLocation.DrawPile ||
            f.CardSetBindings.SingleOrDefault(b => b.Name == SuyiEquipmentBind) is not { CardIds: [var bound] } || bound != id ||
            !action.ConversionChain.SequenceEqual([r.Source]) || f.PendingMovementContinuation?.SubjectSeat != r.TargetSeat)
            throw new InvalidOperationException("Suyi must issue the exact real stock equipment for its selected foreign actor.");
        IssueSuyiNative(f, use, action);
    }
    private void IssueSuyiSlashUse(long id, ProgramSkillFrame supplied, CardActionContext action)
    {
        if (supplied.YangFu is not { Op: SkillProgramEffectOp.ResolveSuyiEquipmentThenSlash, Stage: YangFuStage.SlashUse }) return;
        var at = _resolutionStack.FindIndex(frame => frame.Id == id);
        if (at < 1 || _resolutionStack[at - 1] is not ProgramSkillFrame { YangFu: { } r } f || f != supplied || LifecycleCardUse(id) is not { } use ||
            r.NativeOrigin is not null || action.Type != CardActionType.Use || action.ActorSeat != f.OwnerSeat || action.ProviderSeat != f.OwnerSeat ||
            action.EffectiveKind != CardKind.Slash || !action.TargetSeats.SequenceEqual([r.TargetSeat]) || action.PhysicalCards.Count != 0 ||
            use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } || !action.ConversionChain.SequenceEqual([r.Source]))
            throw new InvalidOperationException("Suyi cannot replace its exact zero-entity ordinary Slash invoice.");
        IssueSuyiNative(f, use, action);
        if (_phase == TurnPhase.Play && f.OwnerSeat == _currentSeat && !IgnoresProgramSlashLimit(_players[f.OwnerSeat], r.Source) &&
            !(LifecycleCardUse(id)?.UnlimitedUse == true) && !HasIssuedJudgedRankSlashQuota(id, f.OwnerSeat) && !HasIssuedWuLuKangDixianUse(id, f.OwnerSeat) && !HasIssuedRoundGainedBasicBonus(id, f.OwnerSeat))
        {
            RecordSlashUseDebit(id, f.OwnerSeat);
            if (GetCardUseDebit(action.ActionId) is not null) AdvanceEventRulesAndQueueFact(new SuyiSlashQuotaPaidEvent(f.Id, id, f.OwnerSeat));
        }
    }
    private void IssueSuyiNative(ProgramSkillFrame f, CardUseFrame use, CardActionContext action)
    {
        var r = f.YangFu!; var origin = new YangFuNativeOrigin(f.Id, r.Source, r.GameplayHash, r.ActualTurn, use.Id, action.ActionId, action.ActorSeat, action.ProviderSeat, use.CardId, use.CardKind, _movementSequence);
        ReplaceRuntimeFrame(f.Id, f with { YangFu = r with { NativeOrigin = origin, Before = _movementSequence } });
        UpdateLifecycleCardUse(use.Id, u => u with { SuyiOrigin = origin }); AdvanceEventRulesAndQueueFact(new SuyiNativeIssuedEvent(origin));
    }
    private bool IsIssuedSuyiUse(CardUseFrame use)
    {
        if (use.SuyiOrigin is not { } o || use.Action is not { } current) return false;
        var issued = current;
        if (use.CurrentSlashFirePolicy is { } fire)
        {
            if (o.Kind != CardKind.Slash || fire.OriginalKind != o.Kind || fire.OriginalAction.ActionId != o.ActionId) return false;
            AssertCurrentSlashFirePolicy(use); issued = fire.OriginalAction;
        }
        else if (o.Kind != use.CardKind) return false;
        if (o.UseId != use.Id || o.ActionId != issued.ActionId || issued.EffectiveKind != o.Kind || o.CardId != use.CardId ||
            !YangFuActorChain(use, o.ActorSeat, o.ProviderSeat, o.ActionId) || ProgramEventHistory<SuyiNativeIssuedEvent>().Count(e => e.Origin == o) != 1) return false;
        var at = _resolutionStack.FindIndex(frame => frame.Id == use.Id);
        if (at < 1 || _resolutionStack[at - 1] is not ProgramSkillFrame { YangFu: { Op: SkillProgramEffectOp.ResolveSuyiEquipmentThenSlash, NativeCompleted: false } r } f ||
            f.Id != o.ProgramFrameId || r.NativeOrigin != o || r.Source != o.Source || r.GameplayHash != o.GameplayHash || r.ActualTurn != o.ActualTurn ||
            !issued.ConversionChain.SequenceEqual([r.Source])) return false;
        return r.Stage == YangFuStage.EquipmentUse ? o.ActorSeat == r.TargetSeat && EquipmentCatalog.IsEquipment(o.Kind) && use.PhysicalCardIds is [var id] && id == o.CardId &&
            issued.PhysicalCards is [var cost] && cost.CardId == id && cost.From == CardLocation.DrawPile :
            r.Stage == YangFuStage.SlashUse && o.ActorSeat == f.OwnerSeat && o.CardId == 0 && o.Kind == CardKind.Slash && use.PhysicalCardIds is { Count: 0 } && issued.PhysicalCards.Count == 0;
    }
    private bool IsSuyiSlashUse(long id) => LifecycleCardUse(id) is { SuyiOrigin.Kind: CardKind.Slash } use && IsIssuedSuyiUse(use);
    private bool TryGetSuyiVirtualPrimaryReturn(CardUseFrame use, ProgramSkillFrame parent, out IReadOnlyList<int> targets)
    {
        targets = [];
        if (use.SuyiOrigin is not { Kind: CardKind.Slash, CardId: 0 } origin || !IsSuyiSlashUse(use.Id) || origin.ProgramFrameId != parent.Id ||
            parent.YangFu is not { Stage: YangFuStage.SlashUse, NativeCompleted: false } r || r.NativeOrigin != origin ||
            parent.SelectedTargetSeats is not [var selected] || selected != r.TargetSeat ||
            use.CardAttack is not { CardId: null, PhysicalCardIds.Count: 0 } attack || attack.ProgramSkillCardUseFrameId != parent.Id ||
            !((HasBingzhongTargetTail(use) || HasPijingTargetTail(use)) || HasWeifuTargetTail(use) || HasDesignatedExtraTargetTail(use) || HasRecipientCategorySlashTargetTail(use) || HasZhangYanLangdaoTargetTail(use)) || use.TargetSeats.Count == 0 ||
            ProgramEventHistory<TargetsConfirmedEvent>().SingleOrDefault(e => e.ResolutionId == use.Id) is not { TargetSeats: [var original] } || original != r.TargetSeat ||
            ProgramEventHistory<CardUseDeclaredEvent>().SingleOrDefault(e => e.ResolutionId == use.Id) is not { CardId: 0, CardKind: CardKind.Slash } declared || declared.SourceSeat != parent.OwnerSeat)
            return false;
        targets = Array.AsReadOnly(new[] { use.TargetSeats[0] }); return true;
    }
    private bool IsSuyiEquipmentRide(ResolutionFrame ride, ResolutionFrame beneath) => ride is CardUseFrame { SuyiOrigin: { } o } use &&
        beneath is ProgramSkillFrame { YangFu.Stage: YangFuStage.EquipmentUse } f && o.ProgramFrameId == f.Id && EquipmentCatalog.IsEquipment(o.Kind) && IsIssuedSuyiUse(use);
    [NativeUseValidation(27310)] private void ValidateSuyiNative(CardUseFrame use)
    { if (use.SuyiOrigin is not null && !IsIssuedSuyiUse(use)) throw new InvalidOperationException("Suyi lost its exact issued native use and original provider."); }
    [NativeUseReturn(27310)] private void ReturnSuyiNative(CardUseFrame use)
    {
        if (use.SuyiOrigin is not { } origin) return;
        var f = GetActiveProgramFrame(origin.ProgramFrameId); var r = f.YangFu;
        if (r is null || r.NativeOrigin != origin || r.NativeCompleted || use.Step != ResolutionFrameStep.Completed || LifecycleCardUse(use.Id) is not null ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) != 1 || origin.CardId != 0 &&
            _cardMovements.Count(m => m.Sequence > origin.Before && m.CardId == origin.CardId && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.EquipmentUse) != 1)
            throw new InvalidOperationException("Suyi lost its whole once-paid native return.");
        ReplaceRuntimeTop(f with { YangFu = r with { NativeCompleted = true, After = _movementSequence } });
        if (use.CardAttack is { Active: true }) return; SettleSuyiNative(origin);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 27310)]
    private bool ReturnSuyiAttack(AttackCompletionReceipt completion)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { YangFu: { Stage: YangFuStage.SlashUse, NativeCompleted: true, NativeOrigin: { } o } } || o.UseId != completion.ResolutionId) return false;
        if (completion.ProgramFrameId is { } parent && parent != o.ProgramFrameId) throw new InvalidOperationException("Suyi's real Slash returned to another owner.");
        SettleSuyiNative(o); return true;
    }
    private void SettleSuyiNative(YangFuNativeOrigin origin)
    {
        var f = GetActiveProgramFrame(origin.ProgramFrameId); var r = f.YangFu!;
        if (!r.NativeCompleted || r.NativeOrigin != origin || LifecycleCardUse(origin.UseId) is not null || ProgramEventHistory<SuyiNativeReturnedEvent>().Any(e => e.Origin == origin))
            throw new InvalidOperationException("Suyi cannot return or pay the same native child twice.");
        ReplaceRuntimeTop(f with { YangFu = r with { Stage = r.Stage == YangFuStage.EquipmentUse ? YangFuStage.EquipmentChildren : YangFuStage.SlashChildren }, PendingMovementContinuation = new(r.Stage == YangFuStage.EquipmentUse ? r.TargetSeat : f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new SuyiNativeReturnedEvent(origin));
    }
}
