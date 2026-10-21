namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static CardConversionSource LianZhuSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_dong_bai_ol_DongBaiPrograms.LianZhuSource(f);
    private static string LianZhuReason(LianZhuStage stage) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_dong_bai_ol_DongBaiPrograms.LianZhuReason(stage);
    private SkillProgramStepOutcome BeginLianZhu(ProgramSkillFrame supplied, int recipient)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.LianZhu is not null || f.InstructionIndex != 1 || f.TriggerId is not null || f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play || f.SelectedTargetSeats is not [var selected] || selected != recipient || recipient == f.OwnerSeat || !IsValidPlayerSeat(recipient) || f.SelectedCardIds is not [var id]) throw new InvalidOperationException("LianZhu lost its exact unpaid hand gift.");
        var dynamic = UsesRecipientHpLianZhu(f);
        var from = _cardZones.GetLocation(id);
        if (!_players[f.OwnerSeat].IsAlive || !_players[recipient].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            (!dynamic ? from != CardLocation.Hand(f.OwnerSeat) :
                !_players[f.OwnerSeat].HasHpValue || !_players[recipient].HasHpValue || _players[recipient].Hp <= 0 ||
                !CanDeliverLianZhuGift(f.OwnerSeat, recipient, id))) { CancelProgramBindingAndCleanup(f, "连诛赠牌前来源失效。"); return SkillProgramStepOutcome.AwaitChild; }
        var card = GetAdvancedCard(id); var before = _movementSequence;
        var r = new LianZhuReceipt(LianZhuSource(f), f.GameplayHash, f.InstructionIndex, _turnNumber, _currentSeat, recipient, id, EffectiveSuit(_players[f.OwnerSeat], card), before, before, LianZhuStage.GiftChildren, before, before, 1)
            { GiftFrom = dynamic ? from : null };
        ReplaceRuntimeTop(f with { LianZhu = r, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new CardsRevealedEvent(f.Id, [ToSnapshot(card)]));
        if(TryBeginPublicHandRevealWindow(f.Id,"lianzhu-gift-hand"))return SkillProgramStepOutcome.AwaitChild;
        ContinueLianzhuAfterPublicReveal(GetActiveProgramFrame(f.Id));return SkillProgramStepOutcome.AwaitChild;
    }
    private void IssueLianZhuSegment(long id, LianZhuReceipt r, int actor) => AdvanceEventRulesAndQueueFact(new LianZhuSegmentPaidEvent(id, r.Source, r.GameplayHash, r.Stage, actor, r.Before, r.After, r.Count));
    private IReadOnlyList<PromptChoice> LianZhuDiscardOptions(ProgramSkillFrame f) => BuildOwnedCardPaymentChoices(f.Id, f.LianZhu!.RecipientSeat, f.LianZhu.RecipientSeat, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard, canSelect: (_, c) => !f.LianZhu.Selected.Contains(c.Id));
    private PromptChoice LianZhuChoice(ProgramSkillFrame f, string branch, string text, IReadOnlyList<int>? cards = null) => new(new($"lianzhu.{f.Id}.{branch}"), text, cards ?? [], [], new Dictionary<string,string> { ["program-action"] = "lianzhu", ["branch"] = branch });
    private IReadOnlyList<PromptChoice> LianZhuChoices(ProgramSkillFrame f)
    {
        if (f.LianZhu!.Stage == LianZhuStage.SelectingDiscard) return Array.AsReadOnly(LianZhuDiscardOptions(f).Select(c => LianZhuChoice(f, "card-" + c.Cards.Single(), c.Label, c.Cards)).ToArray());
        var amount = LianZhuChoiceAmount(f.LianZhu!);
        var choices = new List<PromptChoice> { LianZhuChoice(f, "draw", f.LianZhu!.CountPolicy is null ? "令连诛拥有者摸两张牌" : $"令连诛拥有者摸{amount}张牌") };
        if (LianZhuDiscardOptions(f).Count >= amount) choices.Add(LianZhuChoice(f, "discard", f.LianZhu.CountPolicy is null ? "弃置自己的两张牌" : $"弃置自己的{amount}张牌"));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishLianZhuChoice(ProgramSkillFrame f)
    {
        var seat = f.LianZhu!.RecipientSeat; var choices = LianZhuChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse, PendingMovementContinuation = null });
        _pendingDecision = new(DecisionKind.ProgramTrigger, seat, "请选择连诛结算。", choices.SelectMany(c => c.Cards).ToArray(), [], f.OwnerSeat) { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = seat, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[seat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAiChoice(SkillProgramEffectOp.RevealHandGiftAndResolveRecipientChoice)]
    private PromptChoice LianZhuAiChoice(ProgramSkillFrame f) => ChooseLianZhuAiChoice(f);
    [ProgramAnswer("lianzhu")]
    private void ResolveLianZhuChoice(PromptChoice choice)
    {
        var f = (ProgramSkillFrame)_resolutionStack.Last(); RequireLianZhu(f); var r = f.LianZhu!;
        if (_pendingDecision?.PlayerSeat != r.RecipientSeat || !AssistedChoicesEqual([choice], LianZhuChoices(f).Where(c => c.Id == choice.Id).ToArray())) throw new InvalidOperationException("LianZhu lost recipient authority."); ClearPendingDecision();
        if (!_players[r.RecipientSeat].IsAlive) { FinishLianZhu(f); return; }
        if (choice.Parameters["branch"] == "draw") { PayLianZhuDraw(f); return; }
        if (choice.Parameters["branch"] == "discard") { ReplaceRuntimeTop(f = f with { LianZhu = r with { Stage = LianZhuStage.SelectingDiscard } }); PublishLianZhuChoice(f); return; }
        var selected = r.Selected.Append(choice.Cards.Single()).ToArray(); r = r with { Selected = selected };
        ReplaceRuntimeTop(f = f with { LianZhu = r }); if (selected.Length < LianZhuChoiceAmount(r)) { PublishLianZhuChoice(f); return; }
        r = r with { Stage = LianZhuStage.DiscardChildren, Locations = selected.Select(_cardZones.GetLocation).ToArray(), Before = _movementSequence, After = _movementSequence, Count = LianZhuChoiceAmount(r) };
        ReplaceRuntimeTop(f with { LianZhu = r, PendingMovementContinuation = new(r.RecipientSeat, 0, null), Step = ResolutionFrameStep.ResolvingEffect });
        MoveProgramLogicalDiscard(f, r.RecipientSeat, selected, new(LianZhuReason(r.Stage)), (_, records) => { var active = GetActiveProgramFrame(f.Id); var paid = r with { After = records.Max(m => m.Sequence) }; ReplaceRuntimeTop(active with { LianZhu = paid }); IssueLianZhuSegment(f.Id, paid, r.RecipientSeat); }); AdvanceRuntimeProgram(f.Id);
    }
    private void PayLianZhuDraw(ProgramSkillFrame f)
    {
        var r = f.LianZhu! with { Stage = LianZhuStage.DrawChildren, Before = _movementSequence, After = _movementSequence, Count = 0 }; ReplaceRuntimeTop(f with { LianZhu = r, PendingMovementContinuation = new(f.OwnerSeat, 0, null), Step = ResolutionFrameStep.ResolvingEffect });
        var count = _players[f.OwnerSeat].IsAlive ? DrawCards(_players[f.OwnerSeat], LianZhuChoiceAmount(r), true, new(LianZhuReason(r.Stage))).Count : 0;
        f = GetActiveProgramFrame(f.Id); r = r with { After = _movementSequence, Count = count }; ReplaceRuntimeTop(f with { LianZhu = r }); IssueLianZhuSegment(f.Id, r, f.OwnerSeat); AdvanceRuntimeProgram(f.Id);
    }
    private void FinishLianZhu(ProgramSkillFrame f) { ReplaceRuntimeTop(f = f with { LianZhu = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    private bool DrainLianZhu(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 22400)]
    private bool ResumeLianZhu(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { LianZhu: { } r } f || f.Id != id || r.Stage is LianZhuStage.RecipientChoice or LianZhuStage.SelectingDiscard) return false;
        RequireLianZhu(f); if (DrainLianZhu(f)) return true;
        if (r.Stage == LianZhuStage.GiftChildren && r.GiftSuit is Suit.Spade or Suit.Club && _players[r.RecipientSeat].IsAlive &&
            (!UsesRecipientHpLianZhu(f) || _players[r.RecipientSeat].HasHpValue && _players[r.RecipientSeat].Hp > 0))
        {
            ReplaceRuntimeTop(f = f with { LianZhu = r with { Stage = LianZhuStage.RecipientChoice } });
            f = FreezeLianZhuChoiceAmount(f);
            PublishLianZhuChoice(f);
        }
        else FinishLianZhu(f); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 22400)]
    private bool ReturnLianZhuMovement(ProgramSkillFrame f) => f.LianZhu is not null && ResumeLianZhu(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 80)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 80)]
    private bool CanContinueLianZhu(ProgramSkillFrame f) { if (f.LianZhu is null) return false; RequireLianZhu(f); return true; }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 100)]
    private bool IsLianZhuMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) => f.LianZhu is { Stage: LianZhuStage.GiftChildren or LianZhuStage.DiscardChildren or LianZhuStage.DrawChildren } && effect?.Op == SkillProgramEffectOp.RevealHandGiftAndResolveRecipientChoice && CanContinueLianZhu(f) && p.BeforeCount == 0 && p.CoverageResultBind is null && p.SubjectSeat == (f.LianZhu!.Stage == LianZhuStage.DiscardChildren ? f.LianZhu.RecipientSeat : f.OwnerSeat);
    private void RequireLianZhu(ProgramSkillFrame f)
    {
        if (f.LianZhu is { Stage:LianZhuStage.GiftChildren } revealing && IsPublicHandRevealProducerSuspended(f.Id,"lianzhu-gift-hand") &&
            revealing.Source==LianZhuSource(f)&&revealing.GameplayHash==f.GameplayHash&&revealing.InstructionIndex==f.InstructionIndex&&
            f.SelectedCardIds.SequenceEqual([revealing.GiftCardId])&&f.SelectedTargetSeats.SequenceEqual([revealing.RecipientSeat])&&
            revealing.GiftBefore==revealing.GiftAfter&&revealing.Before==revealing.After&&
            ProgramEventHistory<CardsRevealedEvent>().Any(e=>e.ResolutionId==f.Id&&e.Cards.Select(c=>c.Id).SequenceEqual([revealing.GiftCardId]))&&
            !ProgramEventHistory<LianZhuSegmentPaidEvent>().Any(e=>e.FrameId==f.Id))return;
        if (f.LianZhu is not { } r || r.Source != LianZhuSource(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex || r.TurnNumber != _turnNumber || r.TurnOwnerSeat != f.OwnerSeat || f.SelectedCardIds is not [var id] || id != r.GiftCardId || f.SelectedTargetSeats is not [var seat] || seat != r.RecipientSeat || r.GiftAfter <= r.GiftBefore || r.Before > r.After || r.After > _movementSequence || r.Selected.Distinct().Count() != r.Selected.Count || r.Selected.Count > LianZhuChoiceAmount(r)) throw new InvalidOperationException("LianZhu lost its frozen source, recipient or exact gift invoice.");
        RequireLianZhuChoiceAmount(f, r);
        if (_cardMovements.Count(m => m.Sequence > r.GiftBefore && m.Sequence <= r.GiftAfter && m.CardId == r.GiftCardId && m.From == (r.GiftFrom ?? CardLocation.Hand(f.OwnerSeat)) && m.To == CardLocation.Hand(r.RecipientSeat) && m.Reason.Value == LianZhuReason(LianZhuStage.GiftChildren)) != 1) throw new InvalidOperationException("LianZhu gift was not paid exactly once.");
        if (!Enum.IsDefined(r.Stage) || CompleteProgramEventHistory().OfType<LianZhuSegmentPaidEvent>().Count(e => e == new LianZhuSegmentPaidEvent(f.Id,r.Source,r.GameplayHash,LianZhuStage.GiftChildren,f.OwnerSeat,r.GiftBefore,r.GiftAfter,1)) != 1) throw new InvalidOperationException("LianZhu requires its once-issued original gift fact in every continuation stage.");
        if (r.Stage is LianZhuStage.RecipientChoice or LianZhuStage.SelectingDiscard)
        {
            if (f.PendingMovementContinuation is not null || r.Stage == LianZhuStage.RecipientChoice && r.Selected.Count != 0 || r.Stage == LianZhuStage.SelectingDiscard && r.Selected.Count >= LianZhuChoiceAmount(r)) throw new InvalidOperationException("LianZhu choice cannot masquerade as a paid movement.");
            if (_resolutionStack.LastOrDefault()?.Id == f.Id && _pendingDecision is { } decision && (decision.PlayerSeat != r.RecipientSeat || !decision.IsPrivate || decision.SkillPrompt?.SkillId != f.SkillId || !AssistedChoicesEqual(decision.Choices,LianZhuChoices(f)))) throw new InvalidOperationException("LianZhu paid continuation lost its exact recipient prompt.");
            return;
        }
        var actor = r.Stage == LianZhuStage.DiscardChildren ? r.RecipientSeat : f.OwnerSeat;
        if (CompleteProgramEventHistory().OfType<LianZhuSegmentPaidEvent>().Count(e => e == new LianZhuSegmentPaidEvent(f.Id,r.Source,r.GameplayHash,r.Stage,actor,r.Before,r.After,r.Count)) != 1 || f.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } p || p.SubjectSeat != actor) throw new InvalidOperationException("LianZhu cannot resume an unissued payment.");
        if (r.Stage == LianZhuStage.DrawChildren && (r.Count < 0 || r.Count > LianZhuChoiceAmount(r) || _cardMovements.Count(m => m.Sequence > r.Before && m.Sequence <= r.After && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == LianZhuReason(r.Stage)) != r.Count)) throw new InvalidOperationException("LianZhu draw invoice changed.");
        if (r.Stage == LianZhuStage.DiscardChildren && (r.Selected.Count != LianZhuChoiceAmount(r) || r.Locations.Count != LianZhuChoiceAmount(r) || r.Count != LianZhuChoiceAmount(r) || r.Selected.Select((card,n) => _cardMovements.Count(m => m.Sequence > r.Before && m.Sequence <= r.After && m.CardId == card && m.From == r.Locations[n] && m.Reason.Value == LianZhuReason(r.Stage) && (m.To == CardLocation.DiscardPile || m.To == CardLocation.OutsideGame && (GetAdvancedCard(card).IsGeneralWeapon || IsEphemeralCardDestroyedMaterial(m))))).Any(count => count != 1))) throw new InvalidOperationException("LianZhu discard invoice changed.");
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 140)]
    private void AssertRegisteredLianZhu(ProgramSkillFrame frame)
    {
        if (frame.LianZhu is not null) RequireLianZhu(frame);
    }
    [ProgramCapability]
    private sealed class DongBaiProgramCapability(GameEngine engine) : IDongBaiProgramHost
    { public SkillProgramStepOutcome BeginLianZhu(ProgramSkillFrame f, int recipient) => engine.BeginLianZhu(f, recipient); }
    [PublicHandRevealReturn("lianzhu-gift-hand")]
    private void ContinueLianzhuAfterPublicReveal(ProgramSkillFrame f)
    {
        var r=f.LianZhu??throw new InvalidOperationException("LianZhu lost its original selected real gift.");
        var id=f.SelectedCardIds.Single();var recipient=r.RecipientSeat;
        if(!_players[recipient].IsAlive||_cardZones.GetLocation(id)!=(r.GiftFrom??CardLocation.Hand(f.OwnerSeat))){FinishLianZhu(f);return;}
        var before=_movementSequence;r=r with{GiftBefore=before,GiftAfter=before,Before=before,After=before};ReplaceRuntimeTop(f=f with{LianZhu=r});
        MoveProgramCardsFromMultipleSources([id], CardLocation.Hand(recipient), new(LianZhuReason(r.Stage)), NativeGiveCompletionRecords(f.Id,f.OwnerSeat,recipient,[id],_movementSequence,(_, records) =>
        {
            var active = GetActiveProgramFrame(f.Id); var paid = r with { GiftAfter = records.Single().Sequence, After = records.Single().Sequence };
            ReplaceRuntimeTop(active with { LianZhu = paid }); IssueLianZhuSegment(active.Id, paid, f.OwnerSeat);
        }));
        AdvanceRuntimeProgram(f.Id); return;
    }

}
