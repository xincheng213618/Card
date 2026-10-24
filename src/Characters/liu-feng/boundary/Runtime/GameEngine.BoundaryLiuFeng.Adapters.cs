namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void PrepareBoundaryLiuFengRequest(ProgramSkillFrame f)
    {
        if (f.BoundaryLiuFeng is not { Need: { } n } r) return;
        if (r.Stage != BoundaryLiuFengStage.Issuing || r.CostSequence <= 0 || r.Targets is not [var target] || !BoundaryLiuFengNeedCurrent(r) || !BoundaryLiuFengRequestTarget(n, target) || !ValidBoundaryLiuFeng(f)) throw new InvalidOperationException("Qinling cannot consume another or no longer legal native request.");
        // This synchronous reparenting only consumes the old native response
        // cursor. The same paid skill frame is restored before a new Use exists.
        PopResolutionFrame(f.Id, ResolutionFrameKind.ProgramSkill);
        if (n.Intent == BoundaryLiuFengIntent.BorrowedSword)
        {
            var b = ActiveBorrowedSword!; if (b.ResolutionId != n.OwnerFrameId || _resolutionStack.LastOrDefault()?.Id != n.RequestFrameId) throw new InvalidOperationException("Qinling lost its exact Borrowed Sword response.");
            PopResponseWindow(b.ResolutionId); b.AwaitingSlashChoice = false; b.SlashCardId = 0; b.EffectiveSlashKind = CardKind.Slash; SetCardUseStep(b.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        }
        else if (n.Intent == BoundaryLiuFengIntent.Qinglong)
        {
            var old = ActiveQinglongCrescentBlade!.Attack; ActiveQinglongCrescentBlade = null; SetCardUseStep(old.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            AdvanceEventRulesAndQueueFact(new QinglongCrescentBladeResolvedEvent(old.ResolutionId, n.ActorSeat, target, true, [], CardKind.Slash)); SuspendContinuationForQinglongFollowup(old.ResolutionId);
        }
        else
        {
            var parent = GetActiveProgramFrame(n.OwnerFrameId); var e = GetPausedPrivateOfferEffect(parent)!;
            if (parent.Id != n.RequestFrameId || parent.InstructionIndex != n.InstructionIndex || e.Op != n.RequestOp) throw new InvalidOperationException("Qinling changed the original native request instruction.");
            ReplaceRuntimeTop(parent = parent with { Step = ResolutionFrameStep.ResolvingEffect, BoundaryLiuFengCaller = new(f.Id, r.Source, r.Hash, n, r.Cards.Single(), r.From.Single(), r.CostSequence, target) });
            if (PrepareAnniversaryMaTieSuppliedSlash(parent, n.ActorSeat, n.ActorSeat, target, CardKind.Slash, [], r.Source)) { }
            else if (IsSayingSlashRequest(parent)) PrepareSayingSuppliedSlash(parent, n.ActorSeat, n.ActorSeat, target, CardKind.Slash, [], r.Source);
            else if (IsChanshuangSlashRequest(parent)) PrepareChanshuangSuppliedSlash(parent, n.ActorSeat, n.ActorSeat, target, CardKind.Slash, [], r.Source);
            else if (IsQigongRequest(parent)) ReplaceRuntimeTop(parent with { LvKuangLvXiang = parent.LvKuangLvXiang! with { Stage = "issuing", IssuingIds = [], IssuingKind = CardKind.Slash, IssuingProvider = n.ActorSeat } });
            else if (IsWuJingHejiRequest(parent)) ReplaceRuntimeTop(parent with { WuJing = parent.WuJing! with { Stage = WuJingStage.IssuingUse, Selection = new(CardKind.Slash, false, false) { Conversions = [r.Source] }, Before = _movementSequence, After = _movementSequence } });
            else if (IsCaoChunInitialRequest(parent))
            { var cc = parent.CaoChun!; ReplaceRuntimeTop(parent with { CaoChun = cc with { Stage = CaoChunStage.NativeSlash, Slash = new(parent.Id, cc.Source, cc.GameplayHash, cc.InstructionIndex, cc.ActualTurn, cc.PhaseInstance, false, target, CardKind.Slash) { ProviderSeat = n.ActorSeat, Materials = [], PaymentBefore = _movementSequence } } }); }
            else if (n.Intent == BoundaryLiuFengIntent.GenericBasic)
            {
                var action = new LegalAction(LegalActionKind.Slash, 0, target, "侵凌：弃置后视为使用杀", CardKind.Slash, TargetSeats: [target]) { ConversionSource = r.Source };
                AdvanceEventRulesAndQueueFact(new BoundaryLiuFengKeActionEvent(parent.Id, parent.InstructionIndex, f.Id, target));
                ReplaceRuntimeTop(parent with { KeBiNengPingduan = parent.KeBiNengPingduan! with { BasicActions = parent.KeBiNengPingduan.BasicActions.Append(action).ToArray(), SelectedBasicAction = action, Stage = KeBiNengPingduanStage.BasicIssuing } });
            }
            else if (n.Intent == BoundaryLiuFengIntent.ProgramSlash) CommitProgramChoiceResult(parent.Id, e.ResultBind!, RequestSlashByTargetProgramOperationDescriptor.UsedSlashOption, n.ActorSeat, "侵凌：真实弃置后使用杀。");
            else if (n.Intent == BoundaryLiuFengIntent.AssistedSlash) { ReplaceRuntimeTop(parent with { AssistedSlashRequest = null }); CommitProgramChoiceResult(parent.Id, e.ResultBind!, "used-slash", n.ActorSeat, "侵凌：真实弃置后使用杀。"); }
            else if (n.Intent == BoundaryLiuFengIntent.FixedSlash) ReplaceRuntimeTop(parent with { FixedTargetSlash = parent.FixedTargetSlash! with { Issued = true } });
            else if (n.Intent == BoundaryLiuFengIntent.NearestLegalSlash) ReplaceRuntimeTop(parent with { NearestLegalSlashRequest = null });
        }
        PushRuntimeFrame(f);
        AdvanceEventRulesAndQueueFact(new BoundaryLiuFengRequestReleasedEvent(f.Id, n.OwnerFrameId, n.RequestFrameId, true));
    }
    private ProgramSkillFrame? BoundaryLiuFengNativeParent(CardUseFrame use)
    {
        if (use.BoundaryLiuFeng is not { } o || !IsBoundaryLiuFengUse(use)) return null;
        var index = _resolutionStack.FindIndex(x => x.Id == use.Id);
        if (index < 2 || _resolutionStack[index - 1] is not ProgramSkillFrame { BoundaryLiuFeng: { Need: { } n } r } cost || cost.Id != o.FrameId || !ValidBoundaryLiuFeng(cost) || n.RequestFrameId != n.OwnerFrameId || r.UseId != use.Id || r.ActionId != use.Action!.ActionId || r.Source != o.Source || r.Hash != o.Hash || r.CostSequence <= 0 || _resolutionStack[index - 2] is not ProgramSkillFrame parent || parent.Id != n.OwnerFrameId || parent.InstructionIndex != n.InstructionIndex || GetPausedPrivateOfferEffect(parent)?.Op != n.RequestOp || n.ActorSeat != o.Actor || !r.Targets.SequenceEqual(use.TargetSeats) || ProgramEventHistory<BoundaryLiuFengRequestReleasedEvent>().Count(e => e == new BoundaryLiuFengRequestReleasedEvent(cost.Id, n.OwnerFrameId, n.RequestFrameId, true)) != 1) return null;
        return parent;
    }
    private ProgramSkillFrame? BoundaryLiuFengReturnedProgramParent(long parentId, CardUseFrame use)
    {
        if (use.BoundaryLiuFeng is not { } o || !IsBoundaryLiuFengUse(use) || use.Step != ResolutionFrameStep.Completed || LifecycleCardUse(use.Id) is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { BoundaryLiuFeng: { Need: { } n, Returned: true } r } cost || cost.Id != o.FrameId || n.OwnerFrameId != parentId || n.RequestFrameId != parentId || r.UseId != use.Id || r.ActionId != use.Action!.ActionId || !ValidBoundaryLiuFeng(cost)) return null;
        var at = _resolutionStack.FindIndex(f => f.Id == cost.Id);
        return at > 0 && _resolutionStack[at - 1] is ProgramSkillFrame parent && parent.Id == parentId && ValidBoundaryLiuFengCaller(parent) && parent.BoundaryLiuFengCaller!.Returned ? parent : null;
    }
    private bool HasBoundaryLiuFengProgramPayment(ProgramSkillFrame parent, CardUseFrame? use = null)
    {
        if (!ValidBoundaryLiuFengCaller(parent) || parent.BoundaryLiuFengCaller is not { } r) return false;
        var n = r.Need; var target = r.TargetSeat;
        if (use is not null && (use.BoundaryLiuFeng?.FrameId != r.FrameId || r.UseId != use.Id || r.ActionId != use.Action?.ActionId || !IsBoundaryLiuFengUse(use))) return false;
        if (parent.SunYuan is { } sy) return sy.Selection is { Materials.Count: 0, Sources: [var s] } sel && s == r.Source && sel.ActorSeat == n.ActorSeat && sel.ProviderSeat == n.ActorSeat && sel.TargetSeat == target && sel.Kind == CardKind.Slash;
        if (parent.Chanshuang is { } cs) return cs.Selection is { Materials.Count: 0, Sources: [var s] } sel && s == r.Source && sel.ActorSeat == n.ActorSeat && sel.ProviderSeat == n.ActorSeat && sel.TargetSeat == target && sel.Kind == CardKind.Slash;
        if (parent.WuJing is { } wu) return wu.Source == WuJingSource(parent) && wu.Selection is { CardIds.Count: 0, Conversions: [var s], Kind: CardKind.Slash } && s == r.Source && wu.TargetSeat == target;
        if (parent.LvKuangLvXiang is { } qi) return qi.Source == LvKuangLvXiangSource(parent) && qi.Actor == n.ActorSeat && qi.Target == target && qi.IssuingIds.Count == 0 && qi.IssuingKind == CardKind.Slash && qi.IssuingProvider == n.ActorSeat;
        if (parent.CaoChun is { Slash: { } cc }) return cc.ParentFrameId == parent.Id && cc.Materials.Count == 0 && !cc.Ending && cc.TargetSeat == target && cc.ProviderSeat == n.ActorSeat && cc.Kind == CardKind.Slash;
        return parent.AnniversaryMaTie is { } mt && mt.Actors[mt.Cursor] == n.ActorSeat && mt.Target == target && mt.Materials.Count == 0 && mt.Provider == n.ActorSeat || n.Intent == BoundaryLiuFengIntent.GenericBasic && HasAcceptedBoundaryLiuFengKeAction(parent, parent.KeBiNengPingduan!.SelectedBasicAction!);
    }
    private bool HasAcceptedBoundaryLiuFengKeAction(ProgramSkillFrame parent, LegalAction selected)
    {
        if (parent.KeBiNengPingduan is not { } ke || selected.ConversionSource is not { SkillId: BoundaryLiuFengComposition.Qinling } source || selected.CardId != 0 || selected.PlayedCardKind != CardKind.Slash || selected.Kind != LegalActionKind.Slash || selected.TargetSeats is not [var target]) return false;
        return ValidBoundaryLiuFengCaller(parent) && parent.BoundaryLiuFengCaller is { Need: { Intent: BoundaryLiuFengIntent.GenericBasic } n } r && r.Source == source && r.TargetSeat == target && n.ActorSeat == ke.ActorSeat && ProgramEventHistory<BoundaryLiuFengKeActionEvent>().Count(e => e == new BoundaryLiuFengKeActionEvent(parent.Id, parent.InstructionIndex, r.FrameId, target)) == 1;
    }
    private bool ValidBoundaryLiuFengCaller(ProgramSkillFrame parent)
    {
        if (parent.BoundaryLiuFengCaller is not { } r || r.Source.SkillId != BoundaryLiuFengComposition.Qinling || r.Source.BindingId != BoundaryLiuFengComposition.Activation || r.Source.OwnerSeat != r.Need.ActorSeat || r.Need.OwnerFrameId != parent.Id || r.Need.RequestFrameId != parent.Id || r.Need.InstructionIndex != parent.InstructionIndex || r.Need.RequestOp != GetPausedPrivateOfferEffect(parent)?.Op || _contentRegistry.GetSkill(r.Source.SkillId).Program?.GameplayHash != r.Hash || r.CostSequence <= 0 || ProgramEventHistory<BoundaryLiuFengCostPaidEvent>().Where(e => e.FrameId == r.FrameId).ToArray() is not [var paid] || paid.Source != r.Source || paid.Hash != r.Hash || paid.CardId != r.CardId || paid.From != r.From || paid.Sequence != r.CostSequence || !_cardMovements.Any(m => m.Sequence == r.CostSequence && m.CardId == r.CardId && m.From == r.From) || ProgramEventHistory<BoundaryLiuFengRequestReleasedEvent>().Count(e => e == new BoundaryLiuFengRequestReleasedEvent(r.FrameId, parent.Id, parent.Id, true)) != 1) return false;
        if (r.UseId > 0 && (ProgramEventHistory<BoundaryLiuFengUseIssuedEvent>().Where(e => e.Origin.FrameId == r.FrameId).ToArray() is not [var issue] || issue.Origin.Source != r.Source || issue.Origin.Hash != r.Hash || issue.Origin.UseId != r.UseId || issue.Origin.ActionId != r.ActionId || issue.Origin.Actor != r.Need.ActorSeat)) return false;
        return !r.Returned || ProgramEventHistory<BoundaryLiuFengUseReturnedEvent>().Count(e => e.FrameId == r.FrameId && e.UseId == r.UseId && e.ActionId == r.ActionId) == 1;
    }
    private bool BoundaryLiuFengRequestChild(ProgramSkillFrame parent, ResolutionFrame child)
    {
        if (child is not ProgramSkillFrame { BoundaryLiuFeng: { Need: { } n } r } cost || n.OwnerFrameId != parent.Id || n.RequestFrameId != parent.Id || n.InstructionIndex != parent.InstructionIndex || n.RequestOp != GetPausedPrivateOfferEffect(parent)?.Op || !ValidBoundaryLiuFeng(cost)) return false;
        var at = _resolutionStack.FindIndex(f => f.Id == cost.Id);
        return at > 0 && _resolutionStack[at - 1].Id == parent.Id && (r.UseId == 0 && BoundaryLiuFengNeedCurrent(r) || ValidBoundaryLiuFengCaller(parent) && parent.BoundaryLiuFengCaller!.FrameId == cost.Id);
    }
    private bool IsBoundaryLiuFengNativeAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        if (attack.Card is not null || attack.PhysicalCards.Count != 0 || LifecycleCardUse(attack.ResolutionId) is not { BoundaryLiuFeng: { } o } use || !IsBoundaryLiuFengUse(use) || attack.EffectiveCardKind != use.CardKind || attack.SourceSeat != use.SourceSeat) return false;
        var at = _resolutionStack.FindIndex(x => x.Id == o.FrameId);
        if (at < 0 || at + 1 >= _resolutionStack.Count || _resolutionStack[at] is not ProgramSkillFrame { BoundaryLiuFeng: { Stage: BoundaryLiuFengStage.NativeUse, Returned: false } r } f || !ValidBoundaryLiuFeng(f) || _resolutionStack[at + 1].Id != use.Id || r.UseId != use.Id || r.ActionId != use.Action!.ActionId || attack.ProgramSkillCardUseFrameId != f.Id) return false;
        var expected = _resolutionStack.Take(at).OfType<CardUseFrame>().SelectMany(u => u.PhysicalCardIds ?? []).Where(id => id > 0 && _cardZones.GetLocation(id) == CardLocation.Processing).Distinct().Order().ToArray();
        return processing.Select(c => c.Id).Order().SequenceEqual(expected);
    }
    private bool HasBoundaryLiuFengQinglongPaymentRide(CardAttackHandle attack) => BoundaryLiuFengRequestDraft() is { BoundaryLiuFeng: { Need: { Intent: BoundaryLiuFengIntent.Qinglong } n } r } f && n.OwnerFrameId == attack.ResolutionId && n.RequestFrameId == attack.ResolutionId && n.ActorSeat == attack.SourceSeat && ValidBoundaryLiuFeng(f) && BoundaryLiuFengNeedCurrent(r) && ActiveQinglongCrescentBlade is { } q && SameAttackOwner(q.Attack, attack) && SameAttackOwner(ActiveCardAttack, attack);
    private bool HasBoundaryLiuFengQinglongSettlementRide(CardUseFrame original, long childUseId) => _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.BoundaryLiuFeng?.UseId == childUseId) is { BoundaryLiuFeng: { Need: { Intent: BoundaryLiuFengIntent.Qinglong } n, Returned: true } } cost && ValidBoundaryLiuFeng(cost) && n.OwnerFrameId == original.Id && original.Continuations.QinglongFollowup?.FollowupUseId == childUseId && LifecycleCardUse(childUseId) is null;
    private bool HasBoundaryLiuFengBorrowedSettlementRide(BorrowedSwordHandle b)
    {
        if (!SameContinuationOwner(ActiveBorrowedSword,b) || b.AwaitingSlashChoice || b.SlashCardId != 0 || b.EffectiveSlashKind != CardKind.Slash || b.ActiveAttack is not { } attack || LifecycleCardUse(b.ResolutionId) is not { Action:{ Type:CardActionType.Use } parentAction } ||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.BoundaryLiuFeng?.UseId == attack.ResolutionId) is not { BoundaryLiuFeng:{ Need:{ Intent:BoundaryLiuFengIntent.BorrowedSword } n, Returned:true } r } cost || !ValidBoundaryLiuFeng(cost) ||
            r.Source.SkillId != BoundaryLiuFengComposition.Qinling || r.Source.OwnerSeat != b.WeaponOwnerSeat || n.ActorSeat != b.WeaponOwnerSeat || n.OwnerFrameId != b.ResolutionId || n.ParentActionId != parentAction.ActionId || n.RequestFrameId >= cost.Id || !n.Targets.SequenceEqual([b.SlashTargetSeat]) || !r.Targets.SequenceEqual([b.SlashTargetSeat]) || r.CostSequence <= 0 || r.UseId != attack.ResolutionId || LifecycleCardUse(r.UseId) is not null ||
            r.OriginalDecision is not { Kind:DecisionKind.RespondSlash, RequiredCardKind:CardKind.Slash } original || original.PlayerSeat != b.WeaponOwnerSeat || original.SourceSeat != b.SourceSeat || original.TargetSeat != b.SlashTargetSeat || original.IncomingCard != CardKind.BorrowedSword ||
            ProgramEventHistory<BoundaryLiuFengRequestReleasedEvent>().Count(e => e == new BoundaryLiuFengRequestReleasedEvent(cost.Id,n.OwnerFrameId,n.RequestFrameId,true)) != 1 || ProgramEventHistory<BoundaryLiuFengUseIssuedEvent>().Count(e => e.Origin.FrameId == cost.Id && e.Origin.UseId == r.UseId && e.Origin.ActionId == r.ActionId && e.Origin.Source == r.Source && e.Origin.Hash == r.Hash && e.Origin.Actor == b.WeaponOwnerSeat) != 1 || ProgramEventHistory<BoundaryLiuFengUseReturnedEvent>().Count(e => e.FrameId == cost.Id && e.UseId == r.UseId && e.ActionId == r.ActionId && e.Damaged == r.Damaged) != 1) return false;
        return _resolutionStack.LastOrDefault()?.Id == cost.Id || BoundaryLiuFengObserverRoot()?.Id == cost.Id;
    }
    private void ResumeBoundaryLiuFengRequest(BoundaryLiuFengReceipt r)
    {
        var n = r.Need!;
        if (!r.Returned || LifecycleCardUse(r.UseId) is not null) throw new InvalidOperationException("Qinling cannot resume its native caller before its own whole Slash returns.");
        if (n.Intent == BoundaryLiuFengIntent.BorrowedSword) { CompleteBorrowedSwordAfterSlash(ActiveBorrowedSword!); return; }
        if (n.Intent == BoundaryLiuFengIntent.Qinglong) { if (!RestoreContinuationAfterQinglongFollowup(r.UseId)) throw new InvalidOperationException("Qinling lost its exact suspended Qinglong parent."); return; }
        var parent = GetActiveProgramFrame(n.OwnerFrameId);
        if (parent.InstructionIndex != n.InstructionIndex || GetPausedPrivateOfferEffect(parent)?.Op != n.RequestOp) throw new InvalidOperationException("Qinling returned to another native instruction.");
        ReplaceRuntimeTop(parent = parent with { RequestedSlashDamagedOwner = ProgramEventHistory<DamageAppliedEvent>().Any(e => e.SourceCardUseResolutionId == r.UseId && e.TargetSeat == parent.OwnerSeat && e.Amount > 0) });
        if (parent.AnniversaryMaTie is not null) { ContinueMaTie(parent.Id); return; }
        if (parent.SunYuan is not null) { ResumeSayingSuppliedSlash(parent); return; }
        if (parent.Chanshuang is not null) { ResumeChanshuangSuppliedSlash(parent); return; }
        if (parent.LvKuangLvXiang is not null) { RestoreQigongParentAttack(parent); AdvanceRuntimeProgram(parent.Id); return; }
        if (parent.WuJing is { NativeOrigin: { } wu } wr)
        { if (!wr.NativeCompleted || wu.UseId != r.UseId || wu.ActionId != r.ActionId) throw new InvalidOperationException("Qinling lost the exact Heji native return."); AdvanceEventRulesAndQueueFact(new WuJingHejiReturnedEvent(parent.Id, r.UseId)); ReplaceRuntimeTop(parent with { WuJing = wr with { Stage = WuJingStage.NativeChildren }, PendingMovementContinuation = new(parent.OwnerSeat, 0, null) }); AdvanceRuntimeProgram(parent.Id); return; }
        if (parent.CaoChun is { Slash: { } cc } cr)
        { if (cc.NativeUseId != r.UseId || cc.ActionId != r.ActionId || cc.Returned || cc.PaymentAfter != cc.PaymentBefore) throw new InvalidOperationException("Qinling lost the original Shanjia zero-material payment."); AdvanceEventRulesAndQueueFact(new ShanjiaNativeSlashReturnedEvent(parent.Id, r.UseId, r.ActionId)); ReplaceRuntimeTop(parent with { CaoChun = cr with { Slash = cc with { Returned = true } } }); ResumeCaoChun(parent.Id); return; }
        if (parent.FixedTargetSlash is { Issued: true }) TryResumeFixedSlashAndDeclaredDeck(parent.Id); else AdvanceRuntimeProgram(parent.Id);
    }
    private void RepublishBoundaryLiuFengRequest(BoundaryLiuFengReceipt r)
    {
        var n = r.Need!; if (_winner != Winner.None || !_resolutionStack.Any(f => f.Id == n.OwnerFrameId)) return;
        if (n.Intent == BoundaryLiuFengIntent.BorrowedSword)
        { var b = ActiveBorrowedSword!; PopResponseWindow(b.ResolutionId); BeginBorrowedSwordSlashChoice(b, CanRequestBorrowedSwordFactionSlash(b)); return; }
        if (n.Intent == BoundaryLiuFengIntent.Qinglong)
        {
            var q = ActiveQinglongCrescentBlade!;
            if (!_players[n.ActorSeat].IsAlive) { var attack = q.Attack; ActiveQinglongCrescentBlade = null; SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect); AdvanceEventRulesAndQueueFact(new QinglongCrescentBladeResolvedEvent(attack.ResolutionId, n.ActorSeat, attack.TargetSeat, false, [], null)); RestoreFactionRequestAfterQinglongChoice(attack.ResolutionId); CompleteAttack(attack); }
            else PublishQinglongCrescentBladeChoice(q, CanRequestQinglongCrescentBladeFactionSlash(q));
            return;
        }
        var parent = GetActiveProgramFrame(n.OwnerFrameId); var e = GetPausedPrivateOfferEffect(parent)!;
        if (IsSayingSlashRequest(parent)) { if (!_players[n.ActorSeat].IsAlive) FinishSayingSuppliedSlashFailure(parent); else PublishSaying(parent); return; }
        if (IsChanshuangSlashRequest(parent)) { if (!_players[n.ActorSeat].IsAlive) FinishChanshuangSuppliedSlashFailure(parent); else PublishChanshuang(parent); return; }
        if (parent.AnniversaryMaTie is not null) { ContinueMaTie(parent.Id); return; }
        if (IsQigongRequest(parent)) { PublishLvKuangLvXiang(parent); return; }
        if (IsWuJingHejiRequest(parent)) { PublishWuJing(parent); return; }
        if (IsCaoChunInitialRequest(parent)) { PublishCaoChun(parent); return; }
        if (n.Intent == BoundaryLiuFengIntent.GenericBasic) { PublishKeBiNeng(parent); return; }
        if (n.Intent == BoundaryLiuFengIntent.ProgramSlash) { if (RequestProgramSlashByTarget(parent, n.ActorSeat, e.ResultBind!) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(parent.Id); return; }
        if (n.Intent == BoundaryLiuFengIntent.ProgramNearestSlash) { if (RequestProgramSlashByNearest(parent, n.ActorSeat, e.Amount) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(parent.Id); return; }
        if (n.Intent == BoundaryLiuFengIntent.AssistedSlash) { PublishAssistedPhysicalSlashPrompt(parent); return; }
        if (n.Intent == BoundaryLiuFengIntent.FixedSlash) { PublishFixedSlashAndDeclaredDeckPrompt(parent, FixedTargetSlashChoices(parent), "选择杀响应原请求。", n.Targets); return; }
        if (n.Intent == BoundaryLiuFengIntent.NearestLegalSlash) { var choices = NearestLegalSlashChoices(parent); _pendingDecision = BoundaryLiuFengNativeDecision(r.OriginalDecision!) with { PromptId = CreatePromptId(), ValidCardIds = choices.SelectMany(c => c.Cards).Distinct().ToArray(), Choices = choices }; _status = _players[n.ActorSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; }
    }
}
