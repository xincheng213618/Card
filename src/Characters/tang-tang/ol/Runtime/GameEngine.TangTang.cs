using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ZexingTakeReason = "skill-program.tang-tang.zexing.take", ZexingGiftReason = "skill-program.tang-tang.zexing.gift", ZhiyiDrawReason = "skill-program.tang-tang.zhiyi.draw";
    private static CardConversionSource TangTangSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool ZexingVisited(int owner, int peer) => ProgramEventHistory<ZexingPeerChosenEvent>().Any(e => e.OwnerSeat == owner && e.Peer == peer && e.ActualTurn == _turnNumber && e.TurnOwner == _turnProgression.OwnerSeat);
    private IReadOnlyList<Card> TangTangTransferCards(int chooser, int holder) => GetHand(_players[holder]).Concat(GetEquipment(holder)).Where(c =>
        !c.IsGeneralWeapon && !IsForeignEquipmentDiscardPrevented(chooser, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Transfer)).ToArray();
    private TangTangEntity TangTangEntityAt(int holder, int id)
    { var c = GetAdvancedCard(id); return new(id, c.Kind, _cardZones.GetLocation(id), GetProgramEffectiveSuit(_players[holder], c)); }
    private bool ZexingShownStillOwned(ProgramSkillFrame f) => f.TangTang?.Shown is { } card && TangTangTransferCards(f.OwnerSeat, f.OwnerSeat).Any(c => c.Id == card.CardId);
    private IReadOnlyList<TangTangEntity> ZexingTakeCandidates(ProgramSkillFrame f) => f.TangTang is { Shown: { } shown, Peer: >= 0 } r ?
        TangTangTransferCards(f.OwnerSeat, r.Peer).Where(c => GetProgramEffectiveSuit(_players[r.Peer], c) != shown.Suit).Select(c => TangTangEntityAt(r.Peer, c.Id)).ToArray() : [];
    private bool ZexingTakeAllowed(ProgramSkillFrame f) => f.TangTang is { Peer: >= 0 } r && _players[f.OwnerSeat].IsAlive && _players[r.Peer].IsAlive && GetHand(_players[r.Peer]).Count >= 2 && ZexingTakeCandidates(f).Count >= 2;
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 27530, SkillProgramEffectOp.ResolveZexingShownCardChain)]
    private bool CanActivateZexing(ProgramActivationLegalityContext c) => c.Owner.IsAlive && _phase == TurnPhase.Play && c.OwnerSeat == _currentSeat &&
        CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase && phase.ActorSeat == c.OwnerSeat && IsRecordedActualDiscardRecoveryPhase(phase) &&
        TangTangTransferCards(c.OwnerSeat, c.OwnerSeat).Count > 0 && _players.Any(p => p.IsAlive && p.Seat != c.OwnerSeat && !ZexingVisited(c.OwnerSeat, p.Seat));

    [ProgramCapability]
    private sealed class TangTangCapability(GameEngine engine) : ITangTangHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginTangTang(f, op); }
    private SkillProgramStepOutcome BeginTangTang(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.TangTang is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || !TangTangComposition.Handles(op)) throw new InvalidOperationException("Tang Tang requires her exact new owning instruction.");
        var r = new TangTangReceipt(TangTangSource(f), f.GameplayHash, op, _turnNumber, _turnProgression.OwnerSeat, f.WindowContext?.ParentFrameId ?? 0,
            op == SkillProgramEffectOp.ResolveZexingShownCardChain ? TangTangStage.ShownCard : TangTangStage.BenefitTarget);
        if (op == SkillProgramEffectOp.ResolveZexingShownCardChain)
        {
            if (f.TriggerId is not null || f.ActivationId != "shown-card-chain" || CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != f.OwnerSeat || !IsRecordedActualDiscardRecoveryPhase(phase)) throw new InvalidOperationException("Zexing lost the real current Play phase.");
            r = r with { PlayPhase = phase };
        }
        else
        {
            if (f.TriggerId is null || f.WindowContext is not { } context || CurrentTangTangGains(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex), context) is not { Count: > 0 } gains || !ZhiyiAvailable(f.OwnerSeat))
                throw new InvalidOperationException("Zhiyi lost its exact frozen gain batch or shared actual-turn opportunity.");
            r = r with { Paid = true, Gains = gains };
        }
        ReplaceRuntimeTop(f = f with { TangTang = r });
        AdvanceEventRulesAndQueueFact(new TangTangStartedEvent(f.Id, r.Source, r.GameplayHash, op, r.ActualTurn, r.TurnOwner, r.WindowId, r.PlayPhase?.Token ?? 0));
        if (op == SkillProgramEffectOp.ResolveZhiyiGainColorBenefit) AdvanceEventRulesAndQueueFact(new ZhiyiOpportunityPaidEvent(f.Id, f.OwnerSeat, r.ActualTurn, r.TurnOwner, r.WindowId));
        PublishTangTang(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private int TangTangChooser(ProgramSkillFrame f) => f.TangTang is { Stage: TangTangStage.PeerOption, Peer: >= 0 } r ? r.Peer : f.OwnerSeat;
    private IReadOnlyList<PromptChoice> TangTangChoices(ProgramSkillFrame f)
    {
        var r = f.TangTang ?? throw new InvalidOperationException("No owning Tang Tang instruction.");
        PromptChoice Choice(string branch, string label, int? card = null, int? target = null) => new(new($"tang-tang.{f.Id}.{r.Step}.{r.Stage}.{branch}.{card ?? target ?? -1}"), label,
            card is { } id ? [id] : [], target is { } peer ? [peer] : [], new Dictionary<string, string> { ["program-action"] = "tang-tang", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
        if (r.Stage == TangTangStage.ShownCard) return Array.AsReadOnly(TangTangTransferCards(f.OwnerSeat, f.OwnerSeat).Select(c => Choice("show", $"展示【{c.DisplayName}】", c.Id)).ToArray());
        if (r.Stage is TangTangStage.FirstPeer or TangTangStage.RepeatPeer)
        {
            var choices = _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat && !ZexingVisited(f.OwnerSeat, p.Seat)).Select(p => Choice("peer", $"对{p.Name}发动择行", target: p.Seat)).ToList();
            if (r.Stage == TangTangStage.RepeatPeer) choices.Add(Choice("decline", "结束择行"));
            return Array.AsReadOnly(choices.ToArray());
        }
        if (r.Stage == TangTangStage.PeerOption)
        {
            var choices = new List<PromptChoice>();
            if (ZexingTakeAllowed(f)) choices.Add(Choice("take", "令其观看你的手牌并获得两张异花色牌，然后你获得展示牌"));
            if (_players[r.Peer].IsAlive) choices.Add(Choice("lose-hp", "失去1点体力，令其可继续对其他角色发动择行"));
            return Array.AsReadOnly(choices.ToArray());
        }
        if (r.Stage == TangTangStage.TakeCards)
        {
            var legal = ZexingTakeCandidates(f); var choices = r.Candidates.Where(c => legal.Contains(c) && !r.Selected.Contains(c.CardId) && r.Selected.Count < 2)
                .Select(c => Choice("pick", $"获得【{GetAdvancedCard(c.CardId).DisplayName}】（{c.Suit}）", c.CardId)).ToList();
            choices.AddRange(r.Selected.Select(id => Choice("unpick", $"撤回【{GetAdvancedCard(id).DisplayName}】", id)));
            if (r.Selected.Count == 2) choices.Add(Choice("take-pay", "获得已选两张牌"));
            return Array.AsReadOnly(choices.ToArray());
        }
        if (r.Stage == TangTangStage.BenefitTarget) return Array.AsReadOnly(_players.Where(p => p.IsAlive).Select(p => Choice("benefit", $"令{p.Name}摸一张牌", target: p.Seat)).ToArray());
        return [];
    }
    private void PublishTangTang(ProgramSkillFrame f)
    {
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishTangTang(f); return; }
        var choices = TangTangChoices(f);
        if (choices.Count == 0) { FinishTangTang(f); return; }
        var r = f.TangTang!;
        PublishParticipantHandChoice(f, TangTangChooser(f), choices, r.Stage switch
        { TangTangStage.ShownCard => "择行：选择一张真实手牌或装备牌展示。", TangTangStage.PeerOption => "择行：请选择一项。", TangTangStage.TakeCards => "择行：观看其手牌并选择两张异花色牌。", TangTangStage.BenefitTarget => "知意：令一名角色摸一张牌。", _ => "择行：选择一名本回合未指定过的其他角色。" }, r.Peer >= 0 ? r.Peer : f.OwnerSeat);
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("tang-tang")]
    private void AnswerTangTang(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { TangTang: { } r } f || _pendingDecision?.PlayerSeat != TangTangChooser(f) || !AssistedChoicesEqual([choice], TangTangChoices(f).Where(c => c.Id == choice.Id).ToArray())) throw new InvalidOperationException("Tang Tang lost her exact private chooser, original card or live option.");
        AssertTangTang(f); ClearPendingDecision(); var branch = choice.Parameters["branch"];
        if (branch == "decline") { FinishTangTang(f); return; }
        if (r.Stage == TangTangStage.ShownCard)
        { ReplaceRuntimeTop(f = f with { TangTang = r with { Shown = TangTangEntityAt(f.OwnerSeat, choice.Cards.Single()), Stage = TangTangStage.FirstPeer } }); PublishTangTang(f); return; }
        if (r.Stage is TangTangStage.FirstPeer or TangTangStage.RepeatPeer)
        {
            var peer = choice.Targets.Single();
            if (!r.Paid)
            {
                if (!ZexingShownStillOwned(f) || r.Shown is not { } shown) { FinishTangTang(f); return; }
                ReplaceRuntimeTop(f = f with { TangTang = r with { Shown = TangTangEntityAt(f.OwnerSeat, shown.CardId), Paid = true, Peer = peer, Stage = TangTangStage.ShowChildren } });
                var frozen = f.TangTang!.Shown!;
                AdvanceEventRulesAndQueueFact(new ZexingShownEvent(f.Id, f.OwnerSeat, frozen.CardId, frozen.Kind, frozen.From, frozen.Suit));
                AdvanceEventRulesAndQueueFact(new ProgramCardsRevealedEvent(f.Id, f.SkillId, GetProgramBindingId(f), f.OwnerSeat, "zexing-shown", [ToSnapshot(GetAdvancedCard(frozen.CardId))]));
            }
            else ReplaceRuntimeTop(f = f with { TangTang = r with { Peer = peer, Stage = TangTangStage.ShowChildren, Selected = [], Candidates = [], Viewed = [] } });
            AdvanceEventRulesAndQueueFact(new ZexingPeerChosenEvent(f.Id, f.OwnerSeat, r.ActualTurn, r.TurnOwner, peer, ProgramEventHistory<ZexingPeerChosenEvent>().Count(e => e.FrameId == f.Id)));
            ContinueTangTang(f.Id); return;
        }
        if (r.Stage == TangTangStage.PeerOption)
        {
            AdvanceEventRulesAndQueueFact(new ZexingOptionChosenEvent(f.Id, r.Peer, branch == "take" ? 1 : 2));
            if (branch == "take")
            {
                var viewed = GetHand(_players[r.Peer]).Select(c => c.Id).ToArray();
                ReplaceRuntimeTop(f = f with { TangTang = r with { Stage = TangTangStage.TakeCards, Viewed = viewed, Candidates = ZexingTakeCandidates(f), Selected = [] } });
                AdvanceEventRulesAndQueueFact(new ZexingHandViewedEvent(f.Id, f.OwnerSeat, r.Peer, viewed.Length)); PublishTangTang(f); return;
            }
            ReplaceRuntimeTop(f = f with { TangTang = r with { Stage = TangTangStage.HpIssuing, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(r.Peer, 0, null) });
            ContinueTangTang(f.Id); return;
        }
        if (r.Stage == TangTangStage.TakeCards)
        {
            if (branch != "take-pay")
            { var id = choice.Cards.Single(); ReplaceRuntimeTop(f = f with { TangTang = r with { Selected = branch == "pick" ? r.Selected.Append(id).ToArray() : r.Selected.Where(c => c != id).ToArray() } }); PublishTangTang(f); return; }
            if (r.Selected.Count != 2 || r.Selected.Any(id => !ZexingTakeCandidates(f).Any(c => c.CardId == id))) throw new InvalidOperationException("Zexing cannot obtain a partial or replaced two-card set.");
            IssueTangTangMovement(f, r.Selected, CardLocation.Hand(f.OwnerSeat), TangTangStage.TakeChildren, ZexingTakeReason); ContinueTangTang(f.Id); return;
        }
        if (r.Stage == TangTangStage.BenefitTarget)
        {
            var recipient = choice.Targets.Single();
            var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { TangTang = r with { Recipient = recipient, Stage = TangTangStage.DrawChildren, Step = r.Step + 1, Before = before, After = before }, PendingMovementContinuation = new(recipient, 0, null) });
            AdvanceEventRulesAndQueueFact(new ZhiyiRecipientChosenEvent(f.Id, recipient));
            DrawProgramCards(f.Id, recipient, 1, null, null, SkillProgramCardSetVisibility.Private, new(ZhiyiDrawReason));
            f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { TangTang = f.TangTang! with { After = _movementSequence, ActualCount = _cardMovements.Count(m => m.Sequence > before && m.Sequence <= _movementSequence && m.To == CardLocation.Hand(recipient) && m.From == CardLocation.DrawPile && m.Reason.Value == ZhiyiDrawReason) } });
            AdvanceEventRulesAndQueueFact(new TangTangMovementIssuedEvent(f.Id, f.TangTang!.Step, TangTangStage.DrawChildren, f.TangTang.Before, f.TangTang.After, f.TangTang.ActualCount));
            ContinueTangTang(f.Id); return;
        }
        throw new InvalidOperationException("Tang Tang cannot answer outside her actual choice.");
    }
    private void IssueTangTangMovement(ProgramSkillFrame supplied, IReadOnlyList<int> ids, CardLocation destination, TangTangStage stage, string reason)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.TangTang!;
        var materials = ids.Select(id => TangTangEntityAt(_cardZones.GetLocation(id).OwnerSeat!.Value, id)).ToArray();
        var grain = materials.Where(c => c.Kind == CardKind.WoodenOx && c.From.Zone == CardZoneKind.Equipment).SelectMany(c => GetWoodenOxGrain(_players[c.From.OwnerSeat!.Value])).Select(c => c.Id).ToArray();
        r = r with { Stage = stage, Step = r.Step + 1, Before = _movementSequence, After = _movementSequence, Materials = materials, Grain = grain, ActualCount = ids.Count };
        ReplaceRuntimeTop(f = f with { TangTang = r, PendingMovementContinuation = new(stage == TangTangStage.TakeChildren ? r.Peer : f.OwnerSeat, 0, null) });
        for (var i = 0; i < materials.Length; i++) { var c = materials[i]; AdvanceEventRulesAndQueueFact(new TangTangMovementMaterialEvent(f.Id, r.Step, i, c.CardId, c.Kind, c.From, c.Suit)); }
        for (var i = 0; i < grain.Length; i++) AdvanceEventRulesAndQueueFact(new TangTangGrainEvent(f.Id, r.Step, i, grain[i]));
        MoveProgramCardsFromMultipleSources(ids, destination, new(reason), (batch, records) =>
        {
            foreach (var c in materials)
            { var m = records.Single(m => m.CardId == c.CardId); AdvanceEventRulesAndQueueFact(new TangTangEntityMovedEvent(f.Id, r.Step, stage, batch, m.CardId, m.CardKind, m.From, m.To, m.Sequence)); }
        });
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { TangTang = f.TangTang! with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new TangTangMovementIssuedEvent(f.Id, r.Step, stage, r.Before, _movementSequence, ids.Count));
    }
    private bool DrainTangTang(ProgramSkillFrame f) => TryBeginAdvancedSkillsChanged(f.Id) || TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, f.TangTang?.Stage == TangTangStage.HpChildren ? PostEventContinuation.Program : PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id);
    private void ContinueTangTang(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { TangTang: { } r } f || f.Id != id) return;
        AssertTangTang(f);
        if (r.Stage == TangTangStage.HpIssuing)
        {
            if (!_players[r.Peer].IsAlive || _winner != Winner.None) { FinishTangTang(f); return; }
            new ProgramSkillHost(this).LoseHp(id, f.SkillId, r.Peer, 1);
            if (_resolutionStack.LastOrDefault()?.Id != id) return;
            f = GetActiveProgramFrame(id); r = f.TangTang!;
            if (r.Stage == TangTangStage.HpIssuing) return;
        }
        if (r.Stage is TangTangStage.ShownCard or TangTangStage.FirstPeer or TangTangStage.RepeatPeer or TangTangStage.PeerOption or TangTangStage.TakeCards or TangTangStage.BenefitTarget) { if (_pendingDecision is null) PublishTangTang(f); return; }
        if (DrainTangTang(f)) return;
        f = GetActiveProgramFrame(id); r = f.TangTang!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive) { FinishTangTang(f); return; }
        if (r.Stage == TangTangStage.ShowChildren)
        { if (!_players[r.Peer].IsAlive) { FinishTangTang(f); return; } ReplaceRuntimeTop(f = f with { TangTang = r with { Stage = TangTangStage.PeerOption } }); PublishTangTang(f); return; }
        if (r.Stage == TangTangStage.TakeChildren)
        {
            if (_players[r.Peer].IsAlive && ZexingShownStillOwned(f)) { IssueTangTangMovement(f, [r.Shown!.CardId], CardLocation.Hand(r.Peer), TangTangStage.GiftChildren, ZexingGiftReason); ContinueTangTang(id); }
            else FinishTangTang(f);
            return;
        }
        if (r.Stage == TangTangStage.HpChildren)
        { ReplaceRuntimeTop(f = f with { TangTang = r with { Stage = TangTangStage.RepeatPeer, Selected = [], Viewed = [], Candidates = [] } }); PublishTangTang(f); return; }
        if (r.Stage is TangTangStage.GiftChildren or TangTangStage.DrawChildren) { FinishTangTang(f); return; }
        throw new InvalidOperationException("Tang Tang returned to an unissued native stage.");
    }
    private void FinishTangTang(ProgramSkillFrame f)
    { var applied = f.TangTang?.Paid == true; AdvanceEventRulesAndQueueFact(new TangTangCompletedEvent(f.Id, applied)); ReplaceRuntimeTop(f = f with { TangTang = null, PendingMovementContinuation = null }); FinishProgramSkill(f, applied); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 27530)] private bool ResumeTangTang(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { TangTang: not null } f || f.Id != id) return false; ContinueTangTang(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 27530)] private bool ReturnTangTangMovement(ProgramSkillFrame f)
    { if (f.TangTang is null) return false; ContinueTangTang(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 27530)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 27530)]
    private bool ContinuePaidTangTang(ProgramSkillFrame f) { if (f.TangTang is not { Paid: true }) return false; AssertTangTang(f); return true; }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 27530)]
    private void CaptureZexingHpLoss(ProgramSkillHpLostEvent e)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { TangTang: { Stage: TangTangStage.HpIssuing, Paid: true } r } f || e.FrameId != f.Id || e.SkillId != f.SkillId || e.TargetSeat != r.Peer || e.Amount != 1) return;
        ReplaceRuntimeTop(f with { TangTang = r with { Stage = TangTangStage.HpChildren } });
        AdvanceEventRulesAndQueueFact(new ZexingHpLossIssuedEvent(f.Id, r.Peer, e.RemainingHp + e.Amount, e.RemainingHp));
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveZexingShownCardChain, SkillProgramEffectOp.ResolveZhiyiGainColorBenefit)]
    private PromptChoice TangTangAi(PendingDecision p, ProgramSkillFrame f) => f.TangTang?.Stage == TangTangStage.PeerOption ? p.Choices.First() : p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") is not ("decline" or "unpick")) ?? p.Choices.Last();
}
