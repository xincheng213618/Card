using System.Collections.ObjectModel;
using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private long PairBenefitSequence => _cardMovements.Count == 0 ? 0 : _cardMovements[^1].Sequence;
    private CardConversionSource PairBenefitSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool PairBenefitSourceCurrent(ProgramSkillFrame f) => _winner == Winner.None && _players[f.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId);
    private string PairBenefitMoveReason(ProgramSkillFrame f, bool gift) => $"skill-program.{f.SkillId}.{(gift ? SkillProgramEffectOp.GiveShownCardToLeastOriginalTarget : SkillProgramEffectOp.ObtainOneFromEachSelectedTarget)}";
    private IReadOnlyList<PromptChoice> PairObtainChoices(ProgramSkillFrame f, int target, IReadOnlyList<CardZoneKind> zones) =>
        Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, target, zones, OwnedCardMoveIntent.Obtain)
            .Select(c => c with { Parameters = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(c.Parameters)
                { ["program-action"] = "pair-obtain", ["cursor"] = f.PairObtain!.Cursor.ToString(CultureInfo.InvariantCulture) }) }).ToArray());
    private bool HasPayablePairObtainTarget(int owner, string skill, string instance, int target) => IsValidPlayerSeat(target) && _players[target].IsAlive &&
        new[] { CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment }.Any(zone => _cardZones.CardsAt(new(zone, target)).Any(card =>
            !IsForeignEquipmentDiscardPrevented(owner, card, new(zone, target), OwnedCardMoveIntent.Obtain) &&
            !(zone == CardZoneKind.Equipment && target == owner && IsActiveProgramSourceEquipmentCard(owner, skill, instance, card))));
    private bool CanActivatePairObtain(CharacterState owner, string skill, string instance, SkillProgramActivation activation, IReadOnlyList<int> targets) =>
        !activation.Effects.Any(e => e.Op == SkillProgramEffectOp.ObtainOneFromEachSelectedTarget) || targets is [var a, var b] && a != b &&
        HasPayablePairObtainTarget(owner.Seat, skill, instance, a) && HasPayablePairObtainTarget(owner.Seat, skill, instance, b);

    private SkillProgramStepOutcome ObtainOneFromEachSelectedTarget(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.TriggerId is not null || f.WindowContext is not null || f.InstructionIndex != 1 || f.SelectedTargetSeats is not [var a, var b] || a == b)
            throw new InvalidOperationException("Pair obtain requires its original ordered living pair activation.");
        if (f.PairObtain is null)
        {
            if (!PairBenefitSourceCurrent(f) || !HasPayablePairObtainTarget(f.OwnerSeat, f.SkillId, f.SkillInstanceId, a) ||
                !HasPayablePairObtainTarget(f.OwnerSeat, f.SkillId, f.SkillInstanceId, b))
            { CancelProgramBindingAndCleanup(f, "原两名角色没有各一张合法区域牌。"); return SkillProgramStepOutcome.AwaitChild; }
            var draft = new ProgramPairObtainDraft(0, PairBenefitSource(f), f.GameplayHash, _turnNumber, _currentSeat,
                _cardUseDebitPhaseInstanceId, a, b, 0, false);
            ReplaceRuntimeTop(f = f with { PairObtain = draft });
            AdvanceEventRulesAndQueueFact(new PairObtainStartedEvent(f.Id, draft.Source, draft.GameplayHash,
                draft.ActualTurnNumber, draft.ActualTurnOwnerSeat, draft.PhaseInstanceId, a, b));
        }
        if (!ValidPairObtain(f)) throw new InvalidOperationException("The pair obtain lost its original scalar receipt or actual ledger.");
        var d = f.PairObtain!;
        if (d.AwaitingMovement)
        {
            if (f.PendingMovementContinuation is not null) throw new InvalidOperationException("Pair obtain resumed before its movement children returned.");
            ReplaceRuntimeTop(f = f with { PairObtain = d = d with { Cursor = d.Cursor + 1, AwaitingMovement = false } });
        }
        if (!PairBenefitSourceCurrent(f) || !_players[d.FirstSeat].IsAlive || !_players[d.SecondSeat].IsAlive)
        { CancelProgramBindingAndCleanup(f, "原参与者或来源实例已失效，已经获得的牌保留。"); return SkillProgramStepOutcome.AwaitChild; }
        if (d.Cursor == 2) { ReplaceRuntimeTop(f with { ReexecuteParticipantInstruction = false }); return SkillProgramStepOutcome.Continue; }
        ReplaceRuntimeTop(f = f with { ReexecuteParticipantInstruction = true });
        var target = d.Cursor == 0 ? d.FirstSeat : d.SecondSeat;
        var choices = PairObtainChoices(f, target, effect.Zones);
        if (choices.Count == 0) { CancelProgramBindingAndCleanup(f, "原参与者已没有合法牌，未付后继取消。"); return SkillProgramStepOutcome.AwaitChild; }
        PublishPairBenefitPrompt(f, "获得原角色的一张牌；他人暗手牌只显示牌位。", choices, target);
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private void PublishPairBenefitPrompt(ProgramSkillFrame f, string message, IReadOnlyList<PromptChoice> choices, int? target)
    {
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, message, choices.SelectMany(c => c.Cards).Distinct().ToArray(),
            choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = target, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private void ResolvePairObtainChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Missing pair obtain owner.");
        var d = f.PairObtain ?? throw new InvalidOperationException("Missing pair obtain receipt.");
        var effect = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
        var target = d.Cursor == 0 ? d.FirstSeat : d.SecondSeat;
        if (!ValidPairObtain(f) || d.Cursor is < 0 or > 1 || d.AwaitingMovement || effect.Op != SkillProgramEffectOp.ObtainOneFromEachSelectedTarget ||
            _pendingDecision is not { IsPrivate: true } decision || decision.PlayerSeat != f.OwnerSeat ||
            !decision.Choices.Any(c => c.Id == choice.Id) || choice.Parameters.GetValueOrDefault("cursor") != d.Cursor.ToString(CultureInfo.InvariantCulture) ||
            choice.Parameters.GetValueOrDefault("frame-id") != f.Id.ToString(CultureInfo.InvariantCulture) ||
            choice.Parameters.GetValueOrDefault("card-owner-seat") != target.ToString(CultureInfo.InvariantCulture) ||
            !Enum.TryParse<CardZoneKind>(choice.Parameters.GetValueOrDefault("source-zone"), out var zone) || !effect.Zones.Contains(zone) ||
            !int.TryParse(choice.Parameters.GetValueOrDefault("slot-index"), out var slot))
            throw new InvalidOperationException("The opaque pair choice lost its original frame, cursor or source slot.");
        var expected = PairObtainChoices(f, target, effect.Zones).SingleOrDefault(c => c.Id == choice.Id);
        ClearPendingDecision();
        if (!PairBenefitSourceCurrent(f) || !_players[d.FirstSeat].IsAlive || !_players[d.SecondSeat].IsAlive || expected is null ||
            !expected.Cards.SequenceEqual(choice.Cards) || !expected.Targets.SequenceEqual(choice.Targets))
        { CancelProgramBindingAndCleanup(f, "原牌位、参与者或来源失效，未移动该牌。"); return; }
        var from = new CardLocation(zone, target); var cards = _cardZones.CardsAt(from);
        if (slot < 0 || slot >= cards.Count) throw new InvalidOperationException("The published source slot disappeared.");
        var card = cards[slot]; var same = from == CardLocation.Hand(f.OwnerSeat); var before = PairBenefitSequence;
        var paid = new ProgramPairObtainPayment(d.Cursor, card.Id, from, f.OwnerSeat, before, before, same, false);
        d = d.Cursor == 0 ? d with { FirstPayment = paid } : d with { SecondPayment = paid };
        ReplaceRuntimeTop(f = f with { PairObtain = d with { AwaitingMovement = !same }, ReexecuteParticipantInstruction = true,
            PendingMovementContinuation = same ? null : new(f.OwnerSeat, 0, null) });
        if (same)
        {
            AdvanceEventRulesAndQueueFact(new PairObtainStepCommittedEvent(f.Id, d.Cursor, target, f.OwnerSeat, before, before, true, false));
            ReplaceRuntimeTop(f with { PairObtain = d with { Cursor = d.Cursor + 1 } }); AdvanceRuntimeProgram(f.Id); return;
        }
        MoveProgramCardsFromMultipleSources([card.Id], CardLocation.Hand(f.OwnerSeat), new(PairBenefitMoveReason(f, false)), (_, records) =>
        {
            var after = records.Max(m => m.Sequence);
            var delivered = records.Count(m => m.CardId == card.Id && m.From == from && m.To == CardLocation.Hand(f.OwnerSeat)) == 1;
            var current = GetActiveProgramFrame(f.Id); var draft = current.PairObtain!;
            var actual = paid with { SequenceAfter = after, Delivered = delivered };
            ReplaceRuntimeTop(current with { PairObtain = draft.Cursor == 0 ? draft with { FirstPayment = actual } : draft with { SecondPayment = actual } });
            AdvanceEventRulesAndQueueFact(new PairObtainStepCommittedEvent(f.Id, paid.Cursor, target, f.OwnerSeat, before, after, false, delivered));
        });
        if (AwaitPairBenefitChildren(f.Id) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(f.Id);
    }
    private SkillProgramStepOutcome AwaitPairBenefitChildren(long frameId)
    {
        var f = GetActiveProgramFrame(frameId);
        if (f.PendingMovementContinuation is null) ReplaceRuntimeTop(f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (TryBeginQueuedRecoveryReplacement(frameId, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginHpChangedProgramWindow(frameId, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(frameId))
            return SkillProgramStepOutcome.AwaitChild;
        ReplaceRuntimeTop(GetActiveProgramFrame(frameId) with { PendingMovementContinuation = null });
        return SkillProgramStepOutcome.Continue;
    }
    private SkillProgramStepOutcome GiveShownCardToLeastOriginalTarget(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.InstructionIndex != 4 || !ValidPairObtain(f) || f.PairObtain is not { Cursor: 2, AwaitingMovement: false } d)
            throw new InvalidOperationException("A shown pair gift must follow both original obtain payments.");
        if (f.ShownPairGift is null)
        {
            var binding = GetProgramCardSet(f, effect.SourceBind!);
            if (binding.Visibility != SkillProgramCardSetVisibility.Public || binding.CardIds is not [var id] ||
                binding.SourceLocations is not [var source] || source != CardLocation.Hand(f.OwnerSeat) || _cardZones.GetLocation(id) != source ||
                binding.FrozenRevealedSuit is not { } suit)
                throw new InvalidOperationException("The shown gift requires one exact publicly revealed owned hand entity and frozen suit.");
            ReplaceRuntimeTop(f = f with { ShownPairGift = new(3, effect.SourceBind!, id, suit, d.FirstSeat, d.SecondSeat,
                GetHand(_players[d.FirstSeat]).Count, GetHand(_players[d.SecondSeat]).Count, null,
                ProgramShownPairGiftStage.ChoosingRecipient, PairBenefitSequence, PairBenefitSequence) });
        }
        if (!ValidShownPairGift(f)) throw new InvalidOperationException("The shown pair gift lost its original binding or payment.");
        var r = f.ShownPairGift!;
        if (r.Stage == ProgramShownPairGiftStage.AwaitingGift)
        {
            if (f.PendingMovementContinuation is not null) throw new InvalidOperationException("The shown gift movement has not returned.");
            if (!PairBenefitSourceCurrent(f)) { CancelProgramBindingAndCleanup(f, "已付赠牌保留，未付奖励因来源失效取消。"); return SkillProgramStepOutcome.AwaitChild; }
            if (r.FrozenSuit != Suit.Spade)
            {
                var before = PairBenefitSequence;
                ReplaceRuntimeTop(f = f with { ShownPairGift = r with { Stage = ProgramShownPairGiftStage.AwaitingReward }, ReexecuteParticipantInstruction = true,
                    PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
                DrawProgramCards(f.Id, f.OwnerSeat, 1, null, null, SkillProgramCardSetVisibility.Private, new($"{PairBenefitMoveReason(f, true)}.reward"));
                var after = PairBenefitSequence; var actual = _cardMovements.Count(m => m.Sequence > before && m.Sequence <= after &&
                    m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == $"{PairBenefitMoveReason(f, true)}.reward");
                f = GetActiveProgramFrame(f.Id);
                ReplaceRuntimeTop(f with { ShownPairGift = f.ShownPairGift! with { DrawSequenceBefore = before, DrawSequenceAfter = after, ActualDrawCount = actual } });
                AdvanceEventRulesAndQueueFact(new ShownPairGiftRewardIssuedEvent(f.Id, f.OwnerSeat, 1, actual, before, after));
                return AwaitPairBenefitChildren(f.Id);
            }
            ReplaceRuntimeTop(f = f with { ShownPairGift = r with { Stage = ProgramShownPairGiftStage.Complete } });
        }
        else if (r.Stage == ProgramShownPairGiftStage.AwaitingReward)
        {
            if (f.PendingMovementContinuation is not null) throw new InvalidOperationException("The gift reward gain children have not returned.");
            ReplaceRuntimeTop(f = f with { ShownPairGift = r with { Stage = ProgramShownPairGiftStage.Complete } });
        }
        if (f.ShownPairGift!.Stage == ProgramShownPairGiftStage.Complete)
        { ReplaceRuntimeTop(f with { ReexecuteParticipantInstruction = false }); return SkillProgramStepOutcome.Continue; }
        if (!PairBenefitSourceCurrent(f) || !_players[r.FirstSeat].IsAlive || !_players[r.SecondSeat].IsAlive)
        { CancelProgramBindingAndCleanup(f, "原pair或来源失效，展示的牌留在原区。"); return SkillProgramStepOutcome.AwaitChild; }
        ReplaceRuntimeTop(f = f with { ReexecuteParticipantInstruction = true });
        var targets = new[] { r.FirstSeat, r.SecondSeat }.Where(seat =>
            (seat == r.FirstSeat ? r.FrozenFirstHandCount : r.FrozenSecondHandCount) == Math.Min(r.FrozenFirstHandCount, r.FrozenSecondHandCount));
        var choices = Array.AsReadOnly(targets.Select(seat => new PromptChoice(new($"shown-pair-gift.{f.Id}.{seat}"), $"交给 {_players[seat].Name}。", [], [seat],
            new Dictionary<string, string> { ["program-action"] = "shown-pair-gift", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) })).ToArray());
        PublishPairBenefitPrompt(f, "将已经展示的牌交给原两名角色中当前少手牌者；同数时选择其中一名。", choices, null);
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private void ResolveShownPairGiftChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Missing shown pair gift owner.");
        var r = f.ShownPairGift ?? throw new InvalidOperationException("Missing shown pair gift receipt.");
        if (!ValidShownPairGift(f) || f.InstructionIndex != 4 || r.Stage != ProgramShownPairGiftStage.ChoosingRecipient || choice.Cards.Count != 0 ||
            choice.Targets is not [var target] || _pendingDecision is not { IsPrivate: true } decision || decision.PlayerSeat != f.OwnerSeat ||
            !decision.Choices.Any(c => c.Id == choice.Id && c.Targets.SequenceEqual(choice.Targets)) || choice.Parameters.GetValueOrDefault("frame-id") != f.Id.ToString(CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The shown recipient choice lost its frozen original pair.");
        ClearPendingDecision();
        if (!PairBenefitSourceCurrent(f) || !_players[r.FirstSeat].IsAlive || !_players[r.SecondSeat].IsAlive ||
            target != r.FirstSeat && target != r.SecondSeat || _cardZones.GetLocation(r.CardId) != CardLocation.Hand(f.OwnerSeat) ||
            (target == r.FirstSeat ? r.FrozenFirstHandCount : r.FrozenSecondHandCount) != Math.Min(r.FrozenFirstHandCount, r.FrozenSecondHandCount))
        { CancelProgramBindingAndCleanup(f, "原受赠者或展示实体失效，未支付赠牌。"); return; }
        var before = PairBenefitSequence; var same = target == f.OwnerSeat;
        ReplaceRuntimeTop(f = f with { ShownPairGift = r with { RecipientSeat = target, Stage = ProgramShownPairGiftStage.AwaitingGift,
            SequenceBefore = before, SequenceAfter = before, SameHand = same }, ReexecuteParticipantInstruction = true,
            PendingMovementContinuation = same ? null : new(f.OwnerSeat, 0, null) });
        if (same)
        {
            AdvanceEventRulesAndQueueFact(new ShownPairGiftCommittedEvent(f.Id, r.SourceBind, r.CardId, r.FrozenSuit, target,
                r.FrozenFirstHandCount, r.FrozenSecondHandCount, before, before, true, false));
            AdvanceRuntimeProgram(f.Id); return;
        }
        MoveProgramCardsFromMultipleSources([r.CardId], CardLocation.Hand(target), new(PairBenefitMoveReason(f, true)), (_, records) =>
        {
            var after = records.Max(m => m.Sequence); var delivered = records.Count(m => m.CardId == r.CardId &&
                m.From == CardLocation.Hand(f.OwnerSeat) && m.To == CardLocation.Hand(target)) == 1;
            var current = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(current with { ShownPairGift = current.ShownPairGift! with { SequenceAfter = after, Delivered = delivered } });
            AdvanceEventRulesAndQueueFact(new ShownPairGiftCommittedEvent(f.Id, r.SourceBind, r.CardId, r.FrozenSuit, target,
                r.FrozenFirstHandCount, r.FrozenSecondHandCount, before, after, false, delivered));
        });
        if (AwaitPairBenefitChildren(f.Id) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(f.Id);
    }
    private bool TryReturnPairBenefitMovement(ProgramSkillFrame f)
    {
        if (f.PendingMovementContinuation is null || f.PairObtain is not { AwaitingMovement: true } &&
            f.ShownPairGift?.Stage is not (ProgramShownPairGiftStage.AwaitingGift or ProgramShownPairGiftStage.AwaitingReward)) return false;
        if (!ValidPairObtain(f) || f.ShownPairGift is not null && !ValidShownPairGift(f) ||
            f.PendingMovementContinuation is not { SubjectSeat: var subject, BeforeCount: 0, CoverageResultBind: null } || subject != f.OwnerSeat)
            throw new InvalidOperationException("Pair benefit movement lost its real payment proof.");
        ReplaceRuntimeTop(f with { PendingMovementContinuation = null });
        AdvanceRuntimeProgram(f.Id); return true;
    }
    private PromptChoice SelectAiPairBenefit(PendingDecision decision, ProgramSkillFrame f)
    {
        if (f.ShownPairGift is not null)
        {
            var hint = new SkillProgramAiHint(0, 0, 0, 1, 0, 0, true, false); var view = CreateSnapshot(f.OwnerSeat);
            return decision.Choices.OrderByDescending(c => _aiBrains[f.OwnerSeat].ScoreProgramTarget(view, c.Targets.Single(), hint)).ThenBy(c => c.Targets.Single()).First();
        }
        // Foreign hands have no Cards/identity. AI ranks only published equipment
        // or its own cards, then deterministically chooses an opaque slot.
        return decision.Choices.OrderByDescending(c => c.Cards is [var id] && c.Parameters.GetValueOrDefault("card-owner-seat") != f.OwnerSeat.ToString(CultureInfo.InvariantCulture)
            ? GetKeepValue(_cardZones.CardsAt(_cardZones.GetLocation(id)).Single(card => card.Id == id), _players[f.OwnerSeat]) : 0)
            .ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
    }
    private sealed partial class ProgramSkillHost : IPairObtainFixedRecipientProgramHost
    {
        public SkillProgramStepOutcome ObtainOneFromEachSelectedTarget(ProgramSkillFrame f, SkillProgramEffect e) => engine.ObtainOneFromEachSelectedTarget(f, e);
        public SkillProgramStepOutcome GiveShownCardToLeastOriginalTarget(ProgramSkillFrame f, SkillProgramEffect e) => engine.GiveShownCardToLeastOriginalTarget(f, e);
        public SkillProgramStepOutcome IssueFixedRecipientBenefit(ProgramSkillFrame f, SkillProgramEffect e) => engine.IssueFixedRecipientBenefit(f, e);
        public SkillProgramStepOutcome SelectIssuedFixedRecipient(ProgramSkillFrame f, SkillProgramEffect e) => engine.SelectIssuedFixedRecipient(f, e);
    }
}
