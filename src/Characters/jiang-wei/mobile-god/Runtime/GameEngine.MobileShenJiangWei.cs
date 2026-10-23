using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MobileJwAction = "mobile-jiang-wei";
    private static CardConversionSource MobileJwSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability] private sealed class MobileJwHost(GameEngine e) : IMobileJiangWeiHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => e.BeginMobileJw(f, op); }
    private bool MobileJwHuitianUsed(int owner) => ProgramEventHistory<ProgramBindingStartedEvent>().Any(e => e.OwnerSeat == owner && e.SkillId == "mobile:huitian" && e.BindingId == "higher-hp-extra-turn");
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 40600, SkillProgramEffectOp.ResolveMobileJiangWeiHuitianDeath)]
    private bool AdmitMobileJwDeath(ProgramTriggerLegalityContext c) => MobileJwHuitianUsed(c.Owner.Seat);
    private SkillProgramStepOutcome BeginMobileJw(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.InstructionIndex != 1 || f.MobileJiangWei is not null || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0)
            throw new InvalidOperationException("Mobile Jiang Wei requires his original unpaid zero-material instruction.");
        var dying = op == SkillProgramEffectOp.ResolveMobileJiangWeiShenpei ? ActiveDying : null;
        if (op == SkillProgramEffectOp.ResolveMobileJiangWeiShenpei && (dying is null || dying.VictimSeat != f.OwnerSeat ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.DyingEntering } w ||
            _resolutionStack.SingleOrDefault(x => x.Id == w.ParentFrameId) is not ProgramLifecycleTriggerWindowFrame { Continuation: ProgramLifecycleContinuation.ResumeDyingEntry } p || p.ResumeDyingFrameId != dying.Id))
            throw new InvalidOperationException("Shenpei requires the genuine entering-Dying window and its actual victim.");
        var stage = op switch
        {
            SkillProgramEffectOp.ResolveMobileJiangWeiXinghun => MobileJiangWeiStage.Exchange,
            SkillProgramEffectOp.ResolveMobileJiangWeiTiantao => MobileJiangWeiStage.Region,
            SkillProgramEffectOp.ResolveMobileJiangWeiShenpei => MobileJiangWeiStage.RecoveryChildren,
            _ => MobileJiangWeiStage.DeathIssued
        };
        var count = dying is null ? 0 : ProgramEventHistory<PlayerDyingEvent>().Where(e => e.VictimSeat == f.OwnerSeat && e.ResolutionId <= dying.Id).Select(e => e.ResolutionId).Distinct().Count();
        if (stage == MobileJiangWeiStage.Exchange) EnsureDrawPile();
        var r = new MobileJiangWeiReceipt(MobileJwSource(f), f.GameplayHash, op, f.WindowContext?.ParentFrameId ?? 0)
        { Stage = stage, DyingId = dying?.Id ?? 0, Amount = count, Top = stage == MobileJiangWeiStage.Exchange ? _cardZones.CardsAt(CardLocation.DrawPile).TakeLast(5).Reverse().Select(c => c.Id).ToArray() : [], Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { MobileJiangWei = r });
        AdvanceEventRulesAndQueueFact(new MobileJiangWeiStartedEvent(f.Id, r.Source, r.GameplayHash, op, r.ParentId, r.DyingId, count));
        if (stage == MobileJiangWeiStage.RecoveryChildren)
        {
            if (count < 1) throw new InvalidOperationException("Shenpei lost its actual including-current dying-entry count.");
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, f.OwnerSeat, count, null, null);
            RecordMobileJwPayment(f.Id); ContinueMobileJw(f.Id); return SkillProgramStepOutcome.AwaitChild;
        }
        if (stage == MobileJiangWeiStage.DeathIssued)
        {
            if (!MobileJwHuitianUsed(f.OwnerSeat)) throw new InvalidOperationException("Huitian death requires a previously accepted real extra-turn binding.");
            AdvanceEventRulesAndQueueFact(new ProgramSkillCauseDeathDeclaredEvent(f.Id, f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.OwnerSeat));
            BeginPlayerDeath(f.Id, _players[f.OwnerSeat], null, null, null, causingProgramSkillFrameId: f.Id);
            return SkillProgramStepOutcome.AwaitChild;
        }
        PublishMobileJw(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<Card> MobileJwDiscardable(ProgramSkillFrame f, int seat, CardZoneKind zone) =>
        _cardZones.CardsAt(new(zone, seat)).Where(c => !IsForeignEquipmentDiscardPrevented(f.OwnerSeat, c, new(zone, seat), OwnedCardMoveIntent.Discard) &&
            !IsSelfHandCategoryDiscardForbidden(f.OwnerSeat, c, new(zone, seat), OwnedCardMoveIntent.Discard)).ToArray();
    private IReadOnlyList<PromptChoice> MobileJwChoices(ProgramSkillFrame f)
    {
        var r = f.MobileJiangWei!; var choices = new List<PromptChoice>();
        PromptChoice C(string branch, string label, int value = 0, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) =>
            new(new($"mobile-jw.{f.Id}.{r.Stage}.{branch}.{value}"), label, cards ?? [], targets ?? [], new Dictionary<string, string>
            { ["program-action"] = MobileJwAction, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch, ["value"] = value.ToString(CultureInfo.InvariantCulture) });
        switch (r.Stage)
        {
            case MobileJiangWeiStage.Exchange:
                foreach (var c in GetHand(_players[f.OwnerSeat]).Where(c => !r.Exchanged.Contains(c.Id))) choices.Add(C("hand", "交换手牌：" + c.DisplayName, c.Id, [c.Id]));
                choices.Add(C("exchange-done", "完成交换，排列牌堆顶")); break;
            case MobileJiangWeiStage.ExchangeTop:
                foreach (var id in r.Top.Where(id => !r.Exchanged.Contains(id) && _cardZones.GetLocation(id) == CardLocation.DrawPile)) choices.Add(C("top", "换取顶牌：" + GetAttackCard(id).DisplayName, id, [id]));
                choices.Add(C("exchange-cancel", "重新选择手牌")); break;
            case MobileJiangWeiStage.Ordering:
                foreach (var id in r.Top.Where(id => !r.Ordered.Contains(id) && _cardZones.GetLocation(id) == CardLocation.DrawPile)) choices.Add(C("order", "下一张顶牌：" + GetAttackCard(id).DisplayName, id, [id]));
                break;
            case MobileJiangWeiStage.Target:
            case MobileJiangWeiStage.ThunderTarget:
                foreach (var p in _players.Where(p => p.IsAlive && (r.Stage == MobileJiangWeiStage.ThunderTarget || p.Seat != f.OwnerSeat))) choices.Add(C("target", "选择" + p.Name, p.Seat, targets: [p.Seat])); break;
            case MobileJiangWeiStage.Showing:
                var hand = GetHand(_players[f.OwnerSeat]);
                for (var i = 0; i < hand.Count; i++) if (!r.Shown.Any(s => s.Id == hand[i].Id)) choices.Add(C("show-hand", "展示一张暗手牌", i));
                if (_cardZones.CardsAt(CardLocation.DrawPile).Any()) choices.Add(C("show-top", "展示牌堆顶一张牌"));
                break;
            case MobileJiangWeiStage.Region:
                foreach (var z in new[] { CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment }) choices.Add(C("region", z == CardZoneKind.Hand ? "弃置手牌区全部牌" : z == CardZoneKind.Equipment ? "弃置装备区全部牌" : "弃置判定区全部牌", (int)z)); break;
            case MobileJiangWeiStage.OtherTargets:
                foreach (var p in _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat && MobileJwDiscardable(f, p.Seat, r.Region).Count > 0)) choices.Add(C(r.Targets.Contains(p.Seat) ? "unselect-target" : "discard-target", (r.Targets.Contains(p.Seat) ? "取消选择" : "选择弃置") + p.Name, p.Seat, targets: [p.Seat]));
                choices.Add(C("targets-done", "完成天涛弃牌")); break;
            case MobileJiangWeiStage.DiscardCard:
                foreach (var old in BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, r.Target, [r.Region], OwnedCardMoveIntent.Discard)) choices.Add(old with
                { Parameters = new Dictionary<string, string>(old.Parameters) { ["program-action"] = MobileJwAction, ["branch"] = "discard-card" } });
                break;
        }
        return Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray());
    }
    private void PublishMobileJw(ProgramSkillFrame f)
    {
        var r = f.MobileJiangWei!;
        if (r.Stage == MobileJiangWeiStage.Ordering && r.Ordered.Count == r.Top.Count)
        {
            _cardZones.ReorderDrawPileSubsetToTop(r.Top, r.Ordered);
            ReplaceRuntimeTop(f = f with { MobileJiangWei = r with { Stage = MobileJiangWeiStage.Target } }); r = f.MobileJiangWei!;
        }
        if (r.Stage == MobileJiangWeiStage.Showing && (r.Shown.Count >= 5 || GetHand(_players[f.OwnerSeat]).Count(c => !r.Shown.Any(s => s.Id == c.Id)) == 0 && _cardZones.Count(CardLocation.DrawPile) == 0))
        {
            SetProgramCardSet(f.Id, "xinghun-shown", r.Shown.Select(s => s.Id).ToArray(), SkillProgramCardSetVisibility.Public, r.Shown.Select(s => s.From).ToArray());
            f = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(f = f with { MobileJiangWei = r with { Stage = MobileJiangWeiStage.Slashes, Index = 0 } }); ContinueMobileJw(f.Id); return;
        }
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None || r.Stage == MobileJiangWeiStage.Showing && !_players[r.Target].IsAlive)
        { ReturnMobileJwShownTop(f); return; }
        var choices = MobileJwChoices(f);
        if (choices.Count == 0) { FinishMobileJw(f); return; }
        var chooser = r.Stage == MobileJiangWeiStage.Showing ? r.Target : f.OwnerSeat;
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, skill.Name + "：选择下一步。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = r.Target >= 0 ? r.Target : f.OwnerSeat, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer(MobileJwAction)] private void AnswerMobileJw(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileJiangWei: { } r } f || !MobileJwChoices(f).Any(c => AssistedChoicesEqual([c], [selected])))
            throw new InvalidOperationException("Mobile Jiang Wei requires the current original chooser and exact private choice.");
        RequireMobileJw(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = selected.Parameters["branch"]; var value = int.TryParse(selected.Parameters.GetValueOrDefault("value"), CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        if (branch is "hand" or "exchange-cancel" or "exchange-done" or "order" or "discard-target" or "unselect-target")
        {
            r = branch switch
            {
                "hand" => r with { Stage = MobileJiangWeiStage.ExchangeTop, ChosenHand = value },
                "exchange-cancel" => r with { Stage = MobileJiangWeiStage.Exchange, ChosenHand = 0 },
                "exchange-done" => r with { Stage = MobileJiangWeiStage.Ordering, Ordered = [] },
                "order" => r with { Ordered = r.Ordered.Append(value).ToArray() },
                "discard-target" => r with { Targets = r.Targets.Append(value).ToArray() },
                _ => r with { Targets = r.Targets.Where(s => s != value).ToArray() }
            };
            ReplaceRuntimeTop(f = f with { MobileJiangWei = r }); PublishMobileJw(f); return;
        }
        if (branch == "top")
        {
            var hand = r.ChosenHand; var top = r.Top.Select(id => id == value ? hand : id).ToArray();
            ReplaceRuntimeTop(f = f with { MobileJiangWei = r with { Top = top, Exchanged = r.Exchanged.Concat([hand, value]).ToArray(), ChosenHand = 0 } });
            MutateMobileJw(f, MobileJiangWeiStage.ExchangeChildren, MobileJiangWeiStage.Exchange, () =>
            {
                MoveCard(GetAttackCard(value), CardLocation.DrawPile, CardLocation.Hand(f.OwnerSeat), new("mobile.xinghun.exchange"), disclosure: CardMovementDisclosure.FaceDown);
                MoveCard(GetAttackCard(hand), CardLocation.Hand(f.OwnerSeat), CardLocation.DrawPile, new("mobile.xinghun.exchange"), disclosure: CardMovementDisclosure.FaceDown);
                _cardZones.ReorderDrawPileSubsetToTop(top, top);
            }); return;
        }
        if (branch == "target")
        {
            if (r.Stage == MobileJiangWeiStage.ThunderTarget)
            {
                ReplaceRuntimeTop(f = f with { MobileJiangWei = r with { Stage = MobileJiangWeiStage.ThunderIssued, Target = value } });
                BeginProgramSkillDamage(f, value, r.Amount, nature: DamageNature.Thunder); return;
            }
            ReplaceRuntimeTop(f = f with { MobileJiangWei = r with { Stage = MobileJiangWeiStage.Showing, Target = value } }); PublishMobileJw(f); return;
        }
        if (branch is "show-hand" or "show-top")
        {
            var card = branch == "show-hand" ? GetHand(_players[f.OwnerSeat])[value] : _cardZones.CardsAt(CardLocation.DrawPile).Last();
            var from = _cardZones.GetLocation(card.Id);
            ReplaceRuntimeTop(f = f with { MobileJiangWei = r with { Shown = r.Shown.Append(new(card.Id, card.Kind, from, from == CardLocation.DrawPile ? _movementSequence + 1 : 0)).ToArray() } });
            AdvanceEventRulesAndQueueFact(new CardsRevealedEvent(f.Id, [ToSnapshot(card)]));
            if (from == CardLocation.DrawPile) MutateMobileJw(f, MobileJiangWeiStage.ReserveChildren, MobileJiangWeiStage.Showing, () => MoveCard(card, from, CardLocation.Processing, new("mobile.xinghun.show")));
            else PublishMobileJw(f); return;
        }
        if (branch == "region")
        {
            var zone = (CardZoneKind)value;
            ReplaceRuntimeTop(f = f with { MobileJiangWei = r with { Region = zone, Target = f.OwnerSeat } });
            DiscardMobileJw(f, MobileJwDiscardable(f, f.OwnerSeat, zone).Select(c => c.Id).ToArray()); return;
        }
        if (branch == "discard-card")
        {
            if (!int.TryParse(selected.Parameters.GetValueOrDefault("slot-index"), CultureInfo.InvariantCulture, out var slot)) throw new InvalidOperationException("Tiantao lost its opaque actual source slot.");
            var card = _cardZones.CardsAt(new(r.Region, r.Target))[slot];
            if (!MobileJwDiscardable(f, r.Target, r.Region).Contains(card)) throw new InvalidOperationException("Tiantao cannot discard this original entity.");
            DiscardMobileJw(f, [card.Id]); return;
        }
        if (branch == "targets-done") { ReplaceRuntimeTop(f = f with { MobileJiangWei = r with { Stage = MobileJiangWeiStage.OtherDiscard, Index = 0, Targets = r.Targets.OrderBy(s => RelativeTableOrder(f.OwnerSeat, s)).ToArray() } }); ContinueMobileJw(f.Id); return; }
        throw new InvalidOperationException("Unknown mobile Jiang Wei branch.");
    }
    private void DiscardMobileJw(ProgramSkillFrame f, IReadOnlyList<int> ids)
    {
        var r = f.MobileJiangWei!;
        MutateMobileJw(f, MobileJiangWeiStage.DiscardChildren, r.Target == f.OwnerSeat ? MobileJiangWeiStage.OtherTargets : MobileJiangWeiStage.OtherDiscard, () =>
        {
            MoveProgramLogicalDiscard(f, f.OwnerSeat, ids, new("mobile.tiantao.discard"));
            var parent = GetActiveProgramFrame(f.Id); var current = parent.MobileJiangWei!;
            var paid = ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>().SingleOrDefault(e => e.ParentFrameId == f.Id && e.Reason.Value == "mobile.tiantao.discard" && e.SequenceBefore == current.Before);
            if (paid is { ActualCount: > 0 } && ProgramEventHistory<NativeLogicalDiscardMaterialCompletedEvent>().Where(e => e.InvocationId == paid.InvocationId).All(e =>
                !IsSlashCard(_cardMovements.Single(m => m.Sequence == e.CompletionSequence).CardKind)))
                ReplaceRuntimeFrame(f.Id, parent with { MobileJiangWei = current with { HpLossSeats = current.HpLossSeats.Append(r.Target).ToArray() } });
        });
    }
    private void MutateMobileJw(ProgramSkillFrame f, MobileJiangWeiStage stage, MobileJiangWeiStage next, Action mutation)
    {
        var r = f.MobileJiangWei!;
        ReplaceRuntimeTop(f = f with { MobileJiangWei = r with { Stage = stage, NextStage = next, Before = _movementSequence, After = _movementSequence, Paid = [], PaymentIndex = r.PaymentIndex + 1 }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        mutation(); RecordMobileJwPayment(f.Id); ContinueMobileJw(f.Id);
    }
    private void RecordMobileJwPayment(long id)
    {
        var f = GetActiveProgramFrame(id); var r = f.MobileJiangWei!;
        var moves = _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= _movementSequence).ToArray();
        ReplaceRuntimeTop(f with { MobileJiangWei = r with { After = _movementSequence, Paid = moves } });
        AdvanceEventRulesAndQueueFact(new MobileJiangWeiPaidEvent(f.Id, r.PaymentIndex, r.Before, _movementSequence, moves.Length));
    }
    private bool DrainMobileJw(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginAdvancedSkillsChanged(f.Id) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id);
    private void ContinueMobileJw(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileJiangWei: { } r } f || f.Id != id) return;
        RequireMobileJw(f);
        if (r.Stage == MobileJiangWeiStage.DeathIssued) { FinishMobileJw(f); return; }
        if (r.Stage == MobileJiangWeiStage.SlashIssued) return;
        if (r.Stage == MobileJiangWeiStage.ThunderIssued)
        {
            if (f.AttackAttempt is not null) return;
            MutateMobileJw(f, MobileJiangWeiStage.GrantChildren, MobileJiangWeiStage.Finished, () => new ProgramSkillHost(this).GrantSkills(GetActiveProgramFrame(f.Id), ["mobile:huitian"])); return;
        }
        if (f.PendingMovementContinuation is not null)
        {
            if (DrainMobileJw(f)) return;
            f = GetActiveProgramFrame(id); r = f.MobileJiangWei!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
            var next = r.Stage switch { MobileJiangWeiStage.RecoveryChildren => MobileJiangWeiStage.ThunderTarget, MobileJiangWeiStage.GrantChildren or MobileJiangWeiStage.ReturnChildren => MobileJiangWeiStage.Finished, _ => r.NextStage };
            ReplaceRuntimeTop(f = f with { MobileJiangWei = r with { Stage = next } }); r = f.MobileJiangWei!;
        }
        if (r.Stage == MobileJiangWeiStage.Finished) { FinishMobileJw(f); return; }
        if (r.Stage == MobileJiangWeiStage.OtherDiscard)
        {
            if (r.Index >= r.Targets.Count)
            { ReplaceRuntimeTop(f = f with { MobileJiangWei = r with { Stage = MobileJiangWeiStage.HpLoss, Index = 0 } }); ContinueMobileJw(f.Id); return; }
            var seat = r.Targets[r.Index];
            ReplaceRuntimeTop(f = f with { MobileJiangWei = r with { Index = r.Index + 1, Target = seat } });
            if (!_players[seat].IsAlive || MobileJwDiscardable(f, seat, r.Region).Count == 0) { ContinueMobileJw(f.Id); return; }
            ReplaceRuntimeTop(f = f with { MobileJiangWei = f.MobileJiangWei with { Stage = MobileJiangWeiStage.DiscardCard } }); PublishMobileJw(f); return;
        }
        if (r.Stage == MobileJiangWeiStage.HpLossIssued)
        { ReplaceRuntimeTop(f = f with { MobileJiangWei = r with { Stage = MobileJiangWeiStage.HpLoss } }); r = f.MobileJiangWei!; }
        if (r.Stage == MobileJiangWeiStage.HpLoss)
        {
            if (r.Index >= r.HpLossSeats.Count || _winner != Winner.None) { FinishMobileJw(f); return; }
            var seat = r.HpLossSeats[r.Index]; ReplaceRuntimeTop(f = f with { MobileJiangWei = r with { Stage = MobileJiangWeiStage.HpLossIssued, Index = r.Index + 1 } });
            if (_players[seat].IsAlive && new ProgramSkillHost(this).LoseHp(f.Id, f.SkillId, seat, 1) == SkillProgramStepOutcome.AwaitChild) return;
            ContinueMobileJw(f.Id); return;
        }
        if (r.Stage == MobileJiangWeiStage.SlashReturned)
        { ReplaceRuntimeTop(f = f with { MobileJiangWei = r with { Stage = MobileJiangWeiStage.Slashes, Use = null } }); r = f.MobileJiangWei!; }
        if (r.Stage == MobileJiangWeiStage.Slashes) { NextMobileJwSlash(f); return; }
        PublishMobileJw(f);
    }
    private void ReturnMobileJwShownTop(ProgramSkillFrame f)
    {
        var r = f.MobileJiangWei!;
        var ids = r.Shown.Where(s => s.From == CardLocation.DrawPile && _cardZones.GetLocation(s.Id) == CardLocation.Processing).Select(s => s.Id).ToArray();
        MutateMobileJw(f, MobileJiangWeiStage.ReturnChildren, MobileJiangWeiStage.Finished, () =>
        { MoveCards(ids.Select(GetAttackCard).ToArray(), CardLocation.Processing, CardLocation.DrawPile, new("mobile.xinghun.return")); if (ids.Length > 0) _cardZones.PlaceDrawPileCardsAtTop(ids); });
    }
    private void FinishMobileJw(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f = f with { MobileJiangWei = f.MobileJiangWei! with { Stage = MobileJiangWeiStage.Finished }, PendingMovementContinuation = null });
        AdvanceEventRulesAndQueueFact(new MobileJiangWeiCompletedEvent(f.Id)); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 40600)] private bool ResumeMobileJw(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileJiangWei: not null } f || f.Id != id) return false; ContinueMobileJw(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 40600)] private bool ReturnMobileJwMovement(ProgramSkillFrame f) => f.MobileJiangWei is not null && ResumeMobileJw(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 40600)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 40600)] private bool KeepPaidMobileJw(ProgramSkillFrame f) => f.MobileJiangWei is not null;
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileJiangWeiXinghun, SkillProgramEffectOp.ResolveMobileJiangWeiTiantao, SkillProgramEffectOp.ResolveMobileJiangWeiShenpei)]
    private PromptChoice ChooseMobileJw(PendingDecision p, ProgramSkillFrame f)
    {
        var r = f.MobileJiangWei!;
        if (r.Stage == MobileJiangWeiStage.Exchange) return p.Choices.First(c => c.Parameters["branch"] == "exchange-done");
        if (r.Stage == MobileJiangWeiStage.Region) return p.Choices.OrderBy(c => _cardZones.Count(new((CardZoneKind)int.Parse(c.Parameters["value"], CultureInfo.InvariantCulture), f.OwnerSeat))).First();
        if (r.Stage == MobileJiangWeiStage.OtherTargets) return p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "discard-target" && c.Targets.Count == 1 && !AreProgramDistributionAllies(_players[f.OwnerSeat], _players[c.Targets[0]])) ?? p.Choices.Last();
        if (r.Stage is MobileJiangWeiStage.Target or MobileJiangWeiStage.ThunderTarget) return p.Choices.FirstOrDefault(c => !AreProgramDistributionAllies(_players[f.OwnerSeat], _players[c.Targets[0]])) ?? p.Choices.First();
        return p.Choices.First();
    }
}
