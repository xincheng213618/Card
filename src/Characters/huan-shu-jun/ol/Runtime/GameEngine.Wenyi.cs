namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool WenyiActualTurnOpen => _setupComplete && _turnNumber > 0 && _turnNumber == _turnProgression.TurnNumber && !ProgramEventHistory<ActualTurnCursorReturnedEvent>().Any(e => e.ActualTurn == _turnNumber && e.OwnerSeat == _turnProgression.OwnerSeat);
    private bool IsWenyiHpTrigger(SkillProgramTrigger t) => t.Effects is [{ Op: SkillProgramEffectOp.ResolveWenyiHealthOneAid }];
    private bool HasWenyiHealthSubject(int targetSeat) => HasHuanShuJunCapability && IsValidPlayerSeat(targetSeat) &&
        _players.Any(p => p.IsAlive && WenyiRemaining(p.Seat) > 0 && GetSkillBindingShard(p).ProgramInstances.Any(i => i.Program.Triggers.Any(IsWenyiHpTrigger)));
    private IReadOnlyList<ProgramTriggerCandidate> CollectWenyiHpCandidates(HpChangeContext change)
    {
        if (!HasHuanShuJunCapability || !WenyiActualTurnOpen || change.HpBefore == 1 || change.HpAfter != 1 || !IsValidPlayerSeat(change.TargetSeat) || !_players[change.TargetSeat].IsAlive) return [];
        var result = new List<ProgramTriggerCandidate>();
        foreach (var owner in _players.Where(p => p.IsAlive && WenyiRemaining(p.Seat) > 0).OrderBy(p => (p.Seat - _turnProgression.OwnerSeat + _players.Count) % _players.Count))
            foreach (var i in GetSkillBindingShard(owner).ProgramInstances)
                foreach (var t in i.Program.Triggers.Where(IsWenyiHpTrigger))
                    if (HasRuntimeSkillInstance(owner, i.SkillId, i.SkillInstanceId)) result.Add(new(owner.Seat, i.SkillId, t.Id, i.SkillInstanceId, i.Program.GameplayHash, t.Priority));
        return Array.AsReadOnly(result.GroupBy(c => c.OwnerSeat).Select(g => g.OrderByDescending(c => c.Priority).ThenBy(c => c.SkillInstanceId, StringComparer.Ordinal).First()).ToArray());
    }
    private ProgramSkillWindowContext CreateWenyiHpContext(HpChangeContext change, ProgramTriggerCandidate c) =>
        new(SkillProgramTriggerWindow.AfterHealthChanged, change.Id, c.OwnerSeat, SourceSeat: change.SourceSeat, TargetSeat: change.TargetSeat, Amount: change.Amount, OccurrenceIndex: c.OccurrenceIndex,
            Facts: CaptureProgramTriggerFacts(_players[c.OwnerSeat]) with { HpChangeAmount = change.Amount, HpBeforeChange = change.HpBefore, HpAfterChange = change.HpAfter }, HpChange: change);
    private bool WenyiHpRoot(ProgramSkillFrame f)
    {
        if (f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterHealthChanged, HpChange: { HpAfter: 1 } change } c || change.HpBefore == 1 || c.ParentFrameId != change.Id || c.TargetSeat != change.TargetSeat || c.OwnerSeat != f.OwnerSeat ||
            _resolutionStack.SingleOrDefault(x => x.Id == c.ParentFrameId) is not HpChangedTriggerWindowFrame w || w.Change != change || w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count) return false;
        var candidate = w.Candidates[w.CandidateIndex];
        return candidate.OwnerSeat == f.OwnerSeat && candidate.SkillId == f.SkillId && candidate.BindingId == f.TriggerId && candidate.SkillInstanceId == f.SkillInstanceId && candidate.GameplayHash == f.GameplayHash && w.Contexts[w.CandidateIndex].HpChange == change;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 28101)]
    private bool CanOfferWenyi(ProgramTriggerCandidate c, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!IsWenyiHpTrigger(trigger)) return true;
        return WenyiActualTurnOpen && _players[c.OwnerSeat].IsAlive && WenyiRemaining(c.OwnerSeat) > 0 && context.HpChange is { HpAfter: 1 } h && h.HpBefore != 1 && context.TargetSeat == h.TargetSeat && _players[h.TargetSeat].IsAlive;
    }
    private bool WenyiTargetCanReceivePeach(ProgramSkillFrame f) => f.HuanShuJun is { TargetSeat: var seat } && IsValidPlayerSeat(seat) && _players[seat].IsAlive && IsRuleWounded(_players[seat]) && !IsCardUseForbidden(f.OwnerSeat, CardKind.Peach, CardActionType.Use) && !IsDirectedCardTargetProhibited(f.OwnerSeat, seat, CardKind.Peach) && (seat != f.OwnerSeat || !HasSelfCardTargetProhibition(f.OwnerSeat));
    private bool IsWenyiPeachRequest(ProgramSkillFrame f) => f.HuanShuJun is { Op: SkillProgramEffectOp.ResolveWenyiHealthOneAid, Stage: HuanShuJunStage.WenyiPeachRequest, Paid: false } && WenyiHpRoot(f) && _players[f.OwnerSeat].IsAlive && HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) && WenyiRemaining(f.OwnerSeat) > 0 && WenyiTargetCanReceivePeach(f);
    private bool TryGetWenyiPeachRequest(ProgramSkillFrame f, out int actor, out int target)
    {
        actor = f.OwnerSeat; target = f.HuanShuJun?.TargetSeat ?? -1;
        return f.HuanShuJun is { Op: SkillProgramEffectOp.ResolveWenyiHealthOneAid } r && WenyiHpRoot(f) && IsValidPlayerSeat(target) &&
            (IsWenyiPeachRequest(f) || r.Paid && r.Kind == CardKind.Peach && r.Stage is HuanShuJunStage.NativeIssuing or HuanShuJunStage.NativeUse or HuanShuJunStage.NativeChildren);
    }
    private void PrepareWenyiSuppliedPeach(ProgramSkillFrame supplied, int provider, IReadOnlyList<Card> materials,
        CardConversionSource? source, IReadOnlyList<CardConversionSource>? additionalSources = null, WeijiePaidOrigin? paidWeijie = null)
    {
        var f = _resolutionStack.OfType<ProgramSkillFrame>().Single(x => x.Id == supplied.Id);
        if ((!IsWenyiPeachRequest(f) && (paidWeijie is null || !IsWenyiPeachRequestAfterPaidSupply(f, paidWeijie))) || provider != f.OwnerSeat || materials.Select(c => c.Id).Distinct().Count() != materials.Count || materials.Any(c => c.Id <= 0 || !_cardZones.CardsAt(_cardZones.GetLocation(c.Id)).Contains(c))) throw new InvalidOperationException("Wenyi lost its genuine pending Peach need or actual supplied entities.");
        var sources = (source is null ? Array.Empty<CardConversionSource>() : new[] { source }).Concat(additionalSources ?? []).ToArray();
        if (materials.Count == 0 && sources.Length == 0) throw new InvalidOperationException("Wenyi cannot invent an unprovided virtual Peach.");
        var r = f.HuanShuJun!;
        if (paidWeijie is not null && ProgramEventHistory<WeijieAttemptResolvedEvent>().Count(e => e.FrameId == paidWeijie.AttemptId && e.Matched && e.NativeLegal) != 1) throw new InvalidOperationException("Wenyi cannot accept an unsuccessful paid Peach supply.");
        if (_resolutionStack.LastOrDefault()?.Id != f.Id &&
            (_resolutionStack.LastOrDefault() is not ProgramSkillFrame child || !HuanpeiDraftRidesOn(child, f)))
            throw new InvalidOperationException("Wenyi cannot reserve another program's secondary cost.");
        ReplaceRuntimeFrame(f.Id, f with { HuanShuJun = r with { Paid = true, Kind = CardKind.Peach, Materials = materials.Select(c => new HuanShuJunMaterial(c.Id, c.Kind, _cardZones.GetLocation(c.Id))).ToArray(), Sources = sources, Stage = HuanShuJunStage.NativeIssuing, Before = _movementSequence, After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new WenyiPaidEvent(f.Id, f.OwnerSeat, r.TargetSeat, r.WindowId, "peach"));
    }
    private bool IsWenyiPeachRequestAfterPaidSupply(ProgramSkillFrame f, WeijiePaidOrigin paid) =>
        f.HuanShuJun is { Op: SkillProgramEffectOp.ResolveWenyiHealthOneAid, Paid: false, Stage: HuanShuJunStage.WenyiPeachRequest } r &&
        WenyiHpRoot(f) && r.Source == HuanShuJunSource(f) && r.GameplayHash == f.GameplayHash &&
        paid.Need.OwnerFrameId == f.Id && paid.Need.RequestFrameId == f.Id && paid.Need.InstructionIndex == f.InstructionIndex &&
        paid.Need.RequestOp == r.Op && paid.Need.ActorSeat == f.OwnerSeat && paid.Need.ProviderSeat == f.OwnerSeat &&
        paid.OutputKind == CardKind.Peach && paid.TargetSeat == r.TargetSeat && paid.Need.Targets.SequenceEqual([r.TargetSeat]) &&
        ValidWeijiePayment(paid) && WenyiRemaining(f.OwnerSeat) > 0;
    private bool CanWenyiUseEquipment(int recipient, Card c) => IsValidPlayerSeat(recipient) && _players[recipient].IsAlive && EquipmentCatalog.IsEquipment(c.Kind) && !c.IsGeneralWeapon &&
        _players[recipient].EquipmentSlotCapacity(EquipmentCatalog.Get(c.Kind).Slot) > 0 && !IsCardUseForbidden(recipient, c.Kind, CardActionType.Use) &&
        !IsTurnHandCardRestricted(_players[recipient], c) && !IsTurnPhysicalUseForbidden(recipient, [c.Id]) && !HasSelfCardTargetProhibition(recipient) &&
        !IsCardTargetProhibited(_players[recipient], c.Kind, EffectiveSuit(_players[recipient], c)) && !HasBeneficiarySuitShield(recipient, recipient, EffectiveSuit(_players[recipient], c));
    private void GiveWenyiEquipment(ProgramSkillFrame f, int cardId)
    {
        var r = f.HuanShuJun!; var owner = _players[f.OwnerSeat]; var from = _cardZones.GetLocation(cardId); var card = _cardZones.CardsAt(from).Single(c => c.Id == cardId);
        if (r.Paid || r.Stage != HuanShuJunStage.WenyiChoice || WenyiRemaining(f.OwnerSeat) == 0 || !owner.IsAlive || !HasRuntimeSkillInstance(owner, f.SkillId, f.SkillInstanceId) || r.TargetSeat == f.OwnerSeat || !_players[r.TargetSeat].IsAlive || from.OwnerSeat != f.OwnerSeat || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || !EquipmentCatalog.IsEquipment(card.Kind) || card.IsGeneralWeapon) throw new InvalidOperationException("Wenyi cannot give a changed or unowned equipment entity.");
        ReplaceRuntimeTop(f = f with { HuanShuJun = r = r with { Paid = true, Kind = card.Kind, Materials = [new(card.Id, card.Kind, from)], Stage = HuanShuJunStage.EquipmentGiftChildren, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new WenyiPaidEvent(f.Id, f.OwnerSeat, r.TargetSeat, r.WindowId, "equipment"));
        MoveCard(card, from, CardLocation.Hand(r.TargetSeat), new("skill-program.huan-shu-jun.wenyi.give"), NativeGiveCompletion(f.Id,f.OwnerSeat,r.TargetSeat,[card.Id],_movementSequence,m => AdvanceEventRulesAndQueueFact(new WenyiEquipmentGivenEvent(f.Id, card.Id, r.TargetSeat, m.Sequence))));
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { HuanShuJun = f.HuanShuJun! with { After = _movementSequence } }); ContinueHuanShuJun(f.Id);
    }
    private void IssueWenyiEquipmentUse(ProgramSkillFrame f)
    {
        var r = f.HuanShuJun!; var material = r.Materials.Single();
        if (_winner != Winner.None || !_players[r.TargetSeat].IsAlive || _cardZones.GetLocation(material.CardId) != CardLocation.Hand(r.TargetSeat) || !CanWenyiUseEquipment(r.TargetSeat, GetAdvancedCard(material.CardId))) { RestoreWenyiRecipient(f, true); return; }
        var card = GetAdvancedCard(material.CardId);
        ReplaceRuntimeTop(f = f with { HuanShuJun = r with { Stage = HuanShuJunStage.NativeIssuing, Sources = [], Before = _movementSequence, After = _movementSequence } });
        var id = BeginCardUse(card, r.TargetSeat, []);
        MoveCard(card, CardLocation.Hand(r.TargetSeat), CardLocation.Processing, CardMoveReasons.EquipmentUse);
        if (!TryBeginEquipmentTargetPrograms(_players[r.TargetSeat], card, id)) CompleteEquipmentUse(_players[r.TargetSeat], card, id);
    }
    private void BindWenyiNativeUse(CardUseFrame supplied)
    {
        var index = _resolutionStack.FindIndex(x => x.Id == supplied.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame { HuanShuJun: { Op: SkillProgramEffectOp.ResolveWenyiHealthOneAid, Paid: true, Stage: HuanShuJunStage.NativeIssuing } r } f) return;
        var use = LifecycleCardUse(supplied.Id)!; var actor = r.Kind == CardKind.Peach ? f.OwnerSeat : r.TargetSeat;
        if (use.WenyiUse is not null || r.Native is not null || use.Action is not { Type: CardActionType.Use } a || a.ActorSeat != actor || a.ProviderSeat != actor || a.RequesterSeat is not null || use.CardKind != r.Kind || !a.PhysicalCards.Select(c => c.CardId).SequenceEqual(r.Materials.Select(m => m.CardId)) || !a.ConversionChain.SequenceEqual(r.Sources) ||
            (r.Kind == CardKind.Peach ? !use.TargetSeats.SequenceEqual([r.TargetSeat]) : use.CardId != r.Materials.Single().CardId)) throw new InvalidOperationException("Wenyi's true native Use changed its provider, actor, card or recipient.");
        var origin = new WenyiNativeOrigin(f.Id, r.Source, r.GameplayHash, actor, actor, r.TargetSeat, use.CardKind, use.Id, a.ActionId, a);
        ReplaceRuntimeFrame(f.Id, f with { HuanShuJun = r with { Stage = HuanShuJunStage.NativeUse, Native = origin } });
        UpdateLifecycleCardUse(use.Id, u => u with { WenyiUse = origin }); AdvanceEventRulesAndQueueFact(new WenyiNativeIssuedEvent(f.Id, use.Id, a.ActionId, actor, actor, r.TargetSeat, use.CardKind));
    }
    private bool IsIssuedWenyiUse(CardUseFrame use)
    {
        if (use.WenyiUse is not { } o || use.Action is not { Type: CardActionType.Use } a || o.UseId != use.Id || a.ActionId != o.ActionId || use.CardKind != o.Kind || !a.PhysicalCards.SequenceEqual(o.OriginalAction.PhysicalCards) || !a.ConversionChain.SequenceEqual(o.OriginalAction.ConversionChain) ||
            _resolutionStack.SingleOrDefault(x => x.Id == o.ProgramFrameId) is not ProgramSkillFrame { HuanShuJun: { Stage: HuanShuJunStage.NativeUse, NativeReturned: false, Native: { } n } } f || n.UseId != o.UseId || n.ActionId != o.ActionId || !SameHuanShuJunAction(n.OriginalAction, o.OriginalAction) || !WenyiHpRoot(f)) return false;
        return ProgramEventHistory<WenyiNativeIssuedEvent>().Count(e => e.FrameId == f.Id && e.UseId == use.Id && e.ActionId == a.ActionId && e.ActorSeat == o.ActorSeat && e.ProviderSeat == o.ProviderSeat && e.TargetSeat == o.TargetSeat && e.Kind == o.Kind) == 1;
    }
    [NativeUseValidation(28101)] private void ValidateWenyiNative(CardUseFrame use) { if (use.WenyiUse is not null && !IsIssuedWenyiUse(use)) throw new InvalidOperationException("Wenyi lost its exact paid native child."); }
    [NativeUseReturn(28101)] private void ReturnWenyiNative(CardUseFrame use)
    {
        if (use.WenyiUse is not { } o) return;
        var f = GetActiveProgramFrame(o.ProgramFrameId); var r = f.HuanShuJun!;
        if (r.NativeReturned || r.Native?.UseId != use.Id || r.Native.ActionId != o.ActionId || use.Step != ResolutionFrameStep.Completed || LifecycleCardUse(use.Id) is not null || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) != 1) throw new InvalidOperationException("Wenyi's native use returned before completion or more than once.");
        ReplaceRuntimeTop(f = f with { HuanShuJun = r with { NativeReturned = true, Stage = HuanShuJunStage.NativeChildren, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new WenyiNativeReturnedEvent(f.Id, use.Id, o.ActionId)); ContinueHuanShuJun(f.Id);
    }
    private void RestoreWenyiRecipient(ProgramSkillFrame f, bool face)
    {
        var r = f.HuanShuJun!;
        if (!_players[r.TargetSeat].IsAlive || _winner != Winner.None) { FinishHuanShuJun(f); return; }
        ReplaceRuntimeTop(f = f with { HuanShuJun = r with { Stage = face ? HuanShuJunStage.FaceChildren : HuanShuJunStage.ChainChildren, Before = _movementSequence, After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new WenyiRestorationIssuedEvent(f.Id, r.TargetSeat, face));
        if (face) SetProgramTargetFaceState(f.Id, f.OwnerSeat, r.TargetSeat, false); else SetProgramChainedState(f, false, r.TargetSeat);
        ContinueHuanShuJun(f.Id);
    }
    private void FinishUnissuedWenyiPeach(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.HuanShuJun is not { Op: SkillProgramEffectOp.ResolveWenyiHealthOneAid, Paid: true, Stage: HuanShuJunStage.NativeIssuing, Native: null }) throw new InvalidOperationException("Only Wenyi's exact paid but unissued Peach can take the native failure return.");
        ClearPendingDecision(); RestoreWenyiRecipient(f, true);
    }
    private static bool SameHuanShuJunAction(CardActionContext a, CardActionContext b) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_huan_shu_jun_ol_Wenyi.SameHuanShuJunAction(a,b);
}
