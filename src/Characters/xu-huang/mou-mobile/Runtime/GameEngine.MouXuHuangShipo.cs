using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MouXuHuangGiveReason = "skill-program.mou-mobile:shipo.give", MouXuHuangTransferReason = "skill-program.mou-mobile:shipo.redistribute";
    private int[] MouXuHuangShipoEligible(int owner, bool all) => _players.Where(p => p.IsAlive && p.Seat != owner &&
        (all ? HasJudgmentEffectiveCard(p, CardKind.SupplyShortage) : p.Hp < _players[owner].Hp))
        .OrderBy(p => (p.Seat - owner + _players.Count) % _players.Count).Select(p => p.Seat).ToArray();
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 29501, SkillProgramEffectOp.ResolveMouXuHuangShipo)]
    private bool CanOfferMouXuHuangShipo(ProgramTriggerLegalityContext c) => c.Owner.IsAlive && MouXuHuangComposition.IsShipo(c.Program) &&
        c.Context.Window == SkillProgramTriggerWindow.TurnEnding && c.Context.SourceSeat == c.OwnerSeat &&
        CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Ending } phase && phase.ActorSeat == c.OwnerSeat &&
        (MouXuHuangShipoEligible(c.OwnerSeat, false).Length > 0 || MouXuHuangShipoEligible(c.OwnerSeat, true).Length > 0);
    private bool MouXuHuangShipoRoot(ProgramSkillFrame f)
    {
        if (f.SkillId != MouXuHuangComposition.Shipo || f.TriggerId != "ending-gift-or-damage" || f.ActivationId != f.TriggerId || f.InstructionIndex != 1 ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } c ||
            c.OwnerSeat != f.OwnerSeat || c.SourceSeat != f.OwnerSeat || c.TargetSeat != f.OwnerSeat ||
            _contentRegistry.GetSkill(f.SkillId).Program is not { } p || p.GameplayHash != f.GameplayHash || !MouXuHuangComposition.IsShipo(p) ||
            _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is not { } parent ||
            parent.OwnerSeat != f.OwnerSeat || parent.ItemIndex < 0 || parent.ItemIndex >= parent.Items.Count ||
            parent.Items[parent.ItemIndex].Candidate is not { } candidate || !MountObserverCandidateMatches(f, candidate)) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        return index > 0 && _resolutionStack[index - 1].Id == parent.Id;
    }
    private SkillProgramStepOutcome BeginMouXuHuangShipo(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!MouXuHuangShipoRoot(f) || f.MouXuHuangShipo is not null || !_players[f.OwnerSeat].IsAlive ||
            CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Ending } phase || phase.ActorSeat != f.OwnerSeat || !IsRecordedActualDiscardRecoveryPhase(phase))
            throw new InvalidOperationException("Shipo requires its exact actual own Ending and one fresh optional resolution.");
        var r = new MouXuHuangShipoReceipt(MouXuHuangSource(f), f.GameplayHash, f.WindowContext!.ParentFrameId, phase, _players[f.OwnerSeat].Hp);
        ReplaceRuntimeTop(f = f with { MouXuHuangShipo = r });
        AdvanceEventRulesAndQueueFact(new MouXuHuangShipoStartedEvent(f.Id, r.Source, r.GameplayHash, r.EndingWindowId, phase, r.OwnerHp));
        PublishMouXuHuang(f, f.OwnerSeat, MouXuHuangShipoChoices(f), "势迫：选择一名体力值更低的角色，或所有判定区有兵粮寸断的其他角色。");
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private Dictionary<string, string> MouXuHuangShipoParameters(ProgramSkillFrame f, string branch) => new()
    { ["program-action"] = "mou-xu-huang-shipo", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch,
        ["cursor"] = f.MouXuHuangShipo!.Cursor.ToString(CultureInfo.InvariantCulture), ["stage"] = f.MouXuHuangShipo.Stage.ToString() };
    private int[] MouXuHuangTransferable(ProgramSkillFrame f) => !_players[f.OwnerSeat].IsAlive ? [] : f.MouXuHuangShipo!.Gifts
        .Where(g => g.To == CardLocation.Hand(f.OwnerSeat) && _cardZones.GetLocation(g.CardId) == CardLocation.Hand(f.OwnerSeat))
        .Select(g => g.CardId).Distinct().ToArray();
    private IReadOnlyList<PromptChoice> MouXuHuangShipoChoices(ProgramSkillFrame f)
    {
        var r = f.MouXuHuangShipo!; var choices = new List<PromptChoice>();
        void Add(string branch, string label, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) =>
            choices.Add(new(new($"mou-shipo.{f.Id}.{r.Stage}.{r.Cursor}.{branch}.{cards?.FirstOrDefault() ?? -1}.{targets?.FirstOrDefault() ?? -1}"), label, cards ?? [], targets ?? [], MouXuHuangShipoParameters(f, branch)));
        if (r.Stage == MouXuHuangShipoStage.ModeChoice)
        {
            foreach (var seat in MouXuHuangShipoEligible(f.OwnerSeat, false)) Add("mode-single", $"令{_players[seat].Name}选择交给手牌或受到伤害", targets: [seat]);
            var all = MouXuHuangShipoEligible(f.OwnerSeat, true);
            if (all.Length > 0) Add("mode-all", $"令所有判定区有兵粮寸断的其他角色选择（{all.Length}人）", targets: all);
        }
        else if (r.Stage == MouXuHuangShipoStage.RecipientChoice)
        {
            var seat = r.Targets[r.Cursor];
            if (_players[f.OwnerSeat].IsAlive)
                choices.AddRange(BuildOwnedCardPaymentChoices(f.Id, seat, seat, [CardZoneKind.Hand], OwnedCardMoveIntent.Transfer,
                    destination: SkillProgramCardDestination.OwnerHand).Select(c => c with
                { Parameters = new Dictionary<string, string>(c.Parameters.Concat(MouXuHuangShipoParameters(f, "give")).GroupBy(k => k.Key).ToDictionary(g => g.Key, g => g.Last().Value)) }));
            Add("damage", "受到技能发动者造成的1点伤害");
        }
        else if (r.Stage == MouXuHuangShipoStage.TransferCards)
        {
            foreach (var id in MouXuHuangTransferable(f).Except(r.SelectedCardIds)) Add("select-card", $"选择转交【{GetAdvancedCard(id).DisplayName}】", [id]);
            if (r.SelectedCardIds.Count > 0) Add("finish-cards", $"将所选{r.SelectedCardIds.Count}张牌交给一名其他角色");
            Add("decline-transfer", "不转交牌");
        }
        else if (r.Stage == MouXuHuangShipoStage.TransferRecipient)
        {
            foreach (var seat in _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat).Select(p => p.Seat)) Add("recipient", $"交给{_players[seat].Name}", targets: [seat]);
            Add("decline-transfer", "不转交牌");
        }
        return Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray());
    }
    [ProgramAnswer("mou-xu-huang-shipo")]
    private void AnswerMouXuHuangShipo(PromptChoice selected)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id); var r = f.MouXuHuangShipo!;
        var chooser = r.Stage == MouXuHuangShipoStage.RecipientChoice ? r.Targets[r.Cursor] : f.OwnerSeat;
        if (!ValidMouXuHuangShipo(f) || _pendingDecision?.PlayerSeat != chooser ||
            !AssistedChoicesEqual([selected], MouXuHuangShipoChoices(f).Where(c => c.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("Shipo requires the actual participant's exact private published branch, material and cursor.");
        var branch = selected.Parameters["branch"]; ClearPendingDecision();
        if (r.Stage == MouXuHuangShipoStage.ModeChoice)
        {
            var all = branch == "mode-all"; var expected = MouXuHuangShipoEligible(f.OwnerSeat, all);
            var targets = all ? expected : selected.Targets.ToArray();
            if (targets.Length == 0 || !all && (targets.Length != 1 || !expected.Contains(targets[0])) || all && !selected.Targets.SequenceEqual(expected))
                throw new InvalidOperationException("Shipo must freeze the complete actual selected mode and eligible roster.");
            ReplaceRuntimeTop(f = f with { MouXuHuangShipo = r with { Accepted = true, Mode = branch, Targets = targets, Stage = MouXuHuangShipoStage.RecipientChoice } });
            AdvanceEventRulesAndQueueFact(new MouXuHuangShipoModeAcceptedEvent(f.Id, branch, targets.Length));
            for (var i = 0; i < targets.Length; i++) AdvanceEventRulesAndQueueFact(new MouXuHuangShipoTargetFrozenEvent(f.Id, i, targets[i]));
            ContinueMouXuHuangShipo(f.Id); return;
        }
        if (branch == "decline-transfer") { FinishMouXuHuangShipo(f); return; }
        if (r.Stage == MouXuHuangShipoStage.RecipientChoice)
        {
            if (branch == "damage")
            {
                ReplaceRuntimeTop(f = f with { MouXuHuangShipo = r with { Stage = MouXuHuangShipoStage.Damage } });
                AdvanceEventRulesAndQueueFact(new MouXuHuangShipoDamageIssuedEvent(f.Id, r.Cursor, f.OwnerSeat, chooser, 1));
                BeginProgramSkillDamage(f, chooser, 1); return;
            }
            if (branch != "give" || !_players[f.OwnerSeat].IsAlive || selected.Cards is not [var id] || _cardZones.GetLocation(id) != CardLocation.Hand(chooser))
                throw new InvalidOperationException("Shipo can receive one actual Hand entity only from its current living participant.");
            var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { MouXuHuangShipo = r with { Stage = MouXuHuangShipoStage.GiftChildren, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveCard(GetAdvancedCard(id), CardLocation.Hand(chooser), CardLocation.Hand(f.OwnerSeat), new(MouXuHuangGiveReason), NativeGiveCompletion(f.Id,chooser,f.OwnerSeat,[id],_movementSequence,m =>
            {
                var current = GetActiveProgramFrame(f.Id); var receipt = new MouXuHuangGiftReceipt(r.Cursor, chooser, m.CardId, m.From, m.To, m.Sequence);
                ReplaceRuntimeTop(current with { MouXuHuangShipo = current.MouXuHuangShipo! with { Gifts = current.MouXuHuangShipo.Gifts.Append(receipt).ToArray() } });
                AdvanceEventRulesAndQueueFact(new MouXuHuangShipoGiftPaidEvent(f.Id, r.Cursor, chooser, m.CardId, m.To, m.Sequence));
            }), disclosure: CardMovementDisclosure.FaceDown);
            SealMouXuHuangMovements(f.Id, before); ContinueMouXuHuangShipo(f.Id); return;
        }
        if (r.Stage == MouXuHuangShipoStage.TransferCards)
        {
            if (branch == "select-card")
            {
                var id = selected.Cards.Single();
                ReplaceRuntimeTop(f = f with { MouXuHuangShipo = r with { SelectedCardIds = r.SelectedCardIds.Append(id).ToArray() } });
                AdvanceEventRulesAndQueueFact(new MouXuHuangShipoTransferSelectedEvent(f.Id, r.SelectedCardIds.Count, id));
                PublishMouXuHuang(f, f.OwnerSeat, MouXuHuangShipoChoices(f), "势迫：可继续选择本次实际获得的牌，或确认转交。"); return;
            }
            if (branch != "finish-cards" || r.SelectedCardIds.Count == 0) throw new InvalidOperationException("Shipo must select a nonempty exact received subset before its one recipient.");
            ReplaceRuntimeTop(f = f with { MouXuHuangShipo = r with { Stage = MouXuHuangShipoStage.TransferRecipient } });
            PublishMouXuHuang(f, f.OwnerSeat, MouXuHuangShipoChoices(f), "势迫：选择一名其他角色接收所选牌。"); return;
        }
        if (r.Stage != MouXuHuangShipoStage.TransferRecipient || branch != "recipient" || selected.Targets is not [var target] ||
            target == f.OwnerSeat || !_players[target].IsAlive || !_players[f.OwnerSeat].IsAlive || r.SelectedCardIds.Any(id => !MouXuHuangTransferable(f).Contains(id)))
            throw new InvalidOperationException("Shipo can redistribute only its still-owned original received subset to one living other.");
        var transferBefore = _movementSequence;
        ReplaceRuntimeTop(f = f with { MouXuHuangShipo = r with { Stage = MouXuHuangShipoStage.TransferChildren, TransferRecipient = target, Before = transferBefore, After = transferBefore }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramCardsFromMultipleSources(r.SelectedCardIds, CardLocation.Hand(target), new(MouXuHuangTransferReason), NativeGiveCompletionRecords(f.Id,f.OwnerSeat,target,r.SelectedCardIds,transferBefore,(batch, actual) =>
        {
            AdvanceEventRulesAndQueueFact(new MouXuHuangShipoTransferPaidEvent(f.Id, target, batch, actual.Count, transferBefore, actual.Max(m => m.Sequence)));
        }));
        SealMouXuHuangMovements(f.Id, transferBefore); ContinueMouXuHuangShipo(f.Id);
    }
    private void CompleteMouXuHuangRecipient(ProgramSkillFrame f, bool skipped)
    {
        var r = f.MouXuHuangShipo!;
        AdvanceEventRulesAndQueueFact(new MouXuHuangShipoRecipientCompletedEvent(f.Id, r.Cursor, r.Targets[r.Cursor], skipped));
        ReplaceRuntimeTop(f with { MouXuHuangShipo = r with { Cursor = r.Cursor + 1, Stage = MouXuHuangShipoStage.RecipientChoice }, PendingMovementContinuation = null });
    }
    private void ContinueMouXuHuangShipo(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouXuHuangShipo: { } r } f || f.Id != id) return;
        AssertMouXuHuang(f);
        if (r.Stage == MouXuHuangShipoStage.Damage && f.AttackAttempt is not null) return;
        if (DrainMouXuHuangChildren(f)) return;
        f = GetActiveProgramFrame(id); r = f.MouXuHuangShipo!;
        if (_winner != Winner.None) { FinishMouXuHuangShipo(f); return; }
        if (r.Stage == MouXuHuangShipoStage.TransferChildren) { FinishMouXuHuangShipo(f); return; }
        if (r.Stage is MouXuHuangShipoStage.GiftChildren or MouXuHuangShipoStage.Damage)
        { CompleteMouXuHuangRecipient(f, false); f = GetActiveProgramFrame(id); r = f.MouXuHuangShipo!; }
        if (r.Stage != MouXuHuangShipoStage.RecipientChoice) return;
        while (r.Cursor < r.Targets.Count && !_players[r.Targets[r.Cursor]].IsAlive)
        { CompleteMouXuHuangRecipient(f, true); f = GetActiveProgramFrame(id); r = f.MouXuHuangShipo!; }
        if (r.Cursor < r.Targets.Count)
        { PublishMouXuHuang(f, r.Targets[r.Cursor], MouXuHuangShipoChoices(f), "势迫：交给发动者一张手牌，或受到其造成的1点伤害。"); return; }
        if (!_players[f.OwnerSeat].IsAlive || MouXuHuangTransferable(f).Length == 0 || !_players.Any(p => p.IsAlive && p.Seat != f.OwnerSeat))
        { FinishMouXuHuangShipo(f); return; }
        ReplaceRuntimeTop(f = f with { MouXuHuangShipo = r with { Stage = MouXuHuangShipoStage.TransferCards } });
        PublishMouXuHuang(f, f.OwnerSeat, MouXuHuangShipoChoices(f), "势迫：可以将本次实际获得的任意张牌交给一名其他角色。");
    }
    private void FinishMouXuHuangShipo(ProgramSkillFrame f)
    {
        var r = f.MouXuHuangShipo!; var transfers = ProgramEventHistory<MouXuHuangShipoTransferPaidEvent>().Where(e => e.FrameId == f.Id).Sum(e => e.Count);
        AdvanceEventRulesAndQueueFact(new MouXuHuangShipoCompletedEvent(f.Id, r.Gifts.Count(g => g.To == CardLocation.Hand(f.OwnerSeat)), transfers));
        ReplaceRuntimeTop(f = f with { MouXuHuangShipo = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouXuHuangShipo)]
    private PromptChoice AiMouXuHuangShipo(PendingDecision p, ProgramSkillFrame f)
    {
        var r = f.MouXuHuangShipo!; var view = CreateSnapshot(p.PlayerSeat);
        if (r.Stage == MouXuHuangShipoStage.RecipientChoice)
        {
            var self = view.Players[p.PlayerSeat]; var damageValue = self.Armor > 0 ? -1.5 : self.Hp <= 1 ? -30 : self.Hp == 2 ? -16 : -6;
            var gift = p.Choices.Where(c => c.Parameters["branch"] == "give").Select(c => (Choice: c, Value:
                _aiBrains[p.PlayerSeat].ScoreProgramTarget(view, f.OwnerSeat, new(1, 0, 0, 0, 0, 0, false, false)) - GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[p.PlayerSeat]) * 0.35d))
                .OrderByDescending(x => x.Value).FirstOrDefault();
            return gift.Choice is not null && gift.Value >= damageValue ? gift.Choice : p.Choices.Single(c => c.Parameters["branch"] == "damage");
        }
        double Receive(int seat) => _aiBrains[f.OwnerSeat].ScoreProgramTarget(view, seat, new(1, 0, 0, 0, 0, 0, false, false));
        if (r.Stage == MouXuHuangShipoStage.ModeChoice)
            return p.Choices.OrderByDescending(c => c.Targets.Sum(t => 5 + _aiBrains[f.OwnerSeat].ScoreProgramTarget(view, t,
                new(0, 0, 0, 0, 0, 0, false, false) { TargetDamage = 1, TargetHealthEffects = [new(PublicHealthEffectKind.Damage, 1)] }))).ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
        if (r.Stage == MouXuHuangShipoStage.TransferRecipient)
            return p.Choices.Where(c => c.Parameters["branch"] == "recipient").OrderByDescending(c => Receive(c.Targets.Single())).FirstOrDefault(c => Receive(c.Targets.Single()) > 0) ?? p.Choices.Single(c => c.Parameters["branch"] == "decline-transfer");
        if (!_players.Any(t => t.IsAlive && t.Seat != f.OwnerSeat && Receive(t.Seat) > 0)) return p.Choices.Single(c => c.Parameters["branch"] == "decline-transfer");
        return p.Choices.FirstOrDefault(c => c.Parameters["branch"] == "select-card") ?? p.Choices.Single(c => c.Parameters["branch"] == "finish-cards");
    }
}
