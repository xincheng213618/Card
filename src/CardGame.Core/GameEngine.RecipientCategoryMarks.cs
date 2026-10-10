using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed record RecipientCategoryMarkKey(int HolderSeat, string SourceSkillId, string StateId);
    // Durable gameplay tokens only. Every unfinished choice/payment belongs to its ProgramSkillFrame.
    private readonly Dictionary<RecipientCategoryMarkKey, RecipientCategoryMarkToken> _recipientCategoryMarks = new();
    private bool _recipientCategoryMarkLedgerStarted;
    private bool TracksRecipientCategoryMarks => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GiveHandAndGrantCategoryMark);
    private static RecipientCategoryMarkKind RecipientMarkKind(SkillProgramEffectOp op) => op switch {
        SkillProgramEffectOp.SpendCategoryMarkForDyingRecovery => RecipientCategoryMarkKind.Rescue,
        SkillProgramEffectOp.SpendCategoryMarkForAreaDiscard => RecipientCategoryMarkKind.Discard,
        SkillProgramEffectOp.SpendCategoryMarkForSlashTargets => RecipientCategoryMarkKind.ExtraTargets,
        _ => throw new InvalidOperationException("A gift has no predetermined category mark.") };
    private static PlayerMarkerKind RecipientPublicMarker(RecipientCategoryMarkKind kind) => kind switch {
        RecipientCategoryMarkKind.Rescue => PlayerMarkerKind.CategoryLei,
        RecipientCategoryMarkKind.Discard => PlayerMarkerKind.CategoryFu,
        _ => PlayerMarkerKind.CategorySong };
    private static CardConversionSource RecipientMarkSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static string RecipientMarkReason(ProgramSkillFrame f, string suffix) => $"skill-program.{f.SkillId}.recipient-category.{suffix}";
    private long RecipientMarkSequence => _cardMovements.LastOrDefault()?.Sequence ?? 0;
    private static Dictionary<string, string> RecipientMarkParameters(ProgramSkillFrame f, string action) => new()
    { ["program-action"] = "recipient-category-mark", ["mark-action"] = action, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) };

    private IReadOnlyList<ProgramTriggerCandidate> IncludeRecipientCategoryGiftObservers(CharacterState drawOwner, IReadOnlyList<ProgramTriggerCandidate> existing)
    {
        if (!TracksRecipientCategoryMarks) return existing;
        var added = _players.Where(p => p.IsAlive && p.Seat != drawOwner.Seat &&
            GetSkillBindingShard(p).GetInstanceTriggers(SkillProgramTriggerWindow.DrawPhaseEnded).Any(binding =>
                ProgramInstructionResolver.Default.Features(binding.Trigger).HasOperation(SkillProgramEffectOp.GiveHandAndGrantCategoryMark)))
            .SelectMany(p => CollectEligibleProgramTriggerCandidates(p, SkillProgramTriggerWindow.DrawPhaseEnded, CaptureProgramTriggerFacts(p)))
            .Where(c => GetProgramTrigger(c).Effects.Any(e => e.Op == SkillProgramEffectOp.GiveHandAndGrantCategoryMark));
        return Array.AsReadOnly(existing.Concat(added).Distinct().OrderBy(c => (c.OwnerSeat - drawOwner.Seat + _playerCount) % _playerCount)
            .ThenByDescending(c => c.Priority).ThenBy(c => c.SkillId, StringComparer.Ordinal).ThenBy(c => c.BindingId, StringComparer.Ordinal).ToArray());
    }
    private bool CanOfferRecipientCategoryMarkProgram(ProgramTriggerCandidate c, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects is not [var e] || !RecipientCategoryMarksComposition.IsOperation(e.Op)) return true;
        if (context.OwnerSeat != c.OwnerSeat || !IsValidPlayerSeat(c.OwnerSeat) || !_players[c.OwnerSeat].IsAlive) return false;
        if (e.Op == SkillProgramEffectOp.GiveHandAndGrantCategoryMark)
            return context.Window == SkillProgramTriggerWindow.DrawPhaseEnded && context.SourceSeat is { } holder && context.TargetSeat == holder &&
                holder != c.OwnerSeat && IsValidPlayerSeat(holder) && _players[holder].IsAlive && GetHand(_players[holder]).Count > 0 &&
                !_recipientCategoryMarks.ContainsKey(new(holder, c.SkillId, e.StateId!));
        if (!_recipientCategoryMarks.TryGetValue(new(c.OwnerSeat, e.SourceBind!, e.StateId!), out var token) ||
            token.Kind != RecipientMarkKind(e.Op) || token.DerivedSkillId != c.SkillId) return false;
        return e.Op switch {
            SkillProgramEffectOp.SpendCategoryMarkForDyingRecovery => context.Window == SkillProgramTriggerWindow.DyingEntering && context.TargetSeat == c.OwnerSeat && _players[c.OwnerSeat].Hp <= 0,
            SkillProgramEffectOp.SpendCategoryMarkForAreaDiscard => context.Window == SkillProgramTriggerWindow.PlayPhaseStarting && context.SourceSeat == c.OwnerSeat && context.TargetSeat == c.OwnerSeat && _currentSeat == c.OwnerSeat,
            _ => TryGetRecipientCategorySlashUse(c.OwnerSeat, context, out _, out _) };
    }
    private SkillProgramStepOutcome ExecuteRecipientCategoryMark(ProgramSkillFrame supplied, SkillProgramEffect e)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.RecipientCategoryMark is not null || f.InstructionIndex != 1 || f.TriggerId is null || f.WindowContext is not { } context ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            !CanOfferRecipientCategoryMarkProgram(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0), GetProgramTrigger(f), context) ||
            !ExactRecipientCategoryParent(f)) throw new InvalidOperationException("A category mark operation lost its exact native opportunity.");
        var gift = e.Op == SkillProgramEffectOp.GiveHandAndGrantCategoryMark;
        var holder = gift ? context.TargetSeat!.Value : f.OwnerSeat;
        var token = gift ? null : _recipientCategoryMarks[new(holder, e.SourceBind!, e.StateId!)];
        var r = new ProgramRecipientCategoryMarkReceipt { Source = RecipientMarkSource(f), GameplayHash = f.GameplayHash,
            SourceSkillId = gift ? f.SkillId : e.SourceBind!, StateId = e.StateId!, Operation = e.Op, HolderSeat = holder,
            ParentWindowId = context.ParentFrameId, Token = token, Stage = gift ? RecipientCategoryMarkStage.ChoosingGift :
                e.Op == SkillProgramEffectOp.SpendCategoryMarkForAreaDiscard ? RecipientCategoryMarkStage.ChoosingTarget :
                e.Op == SkillProgramEffectOp.SpendCategoryMarkForSlashTargets ? RecipientCategoryMarkStage.ChoosingSlashTargets : RecipientCategoryMarkStage.RecoveryChildren,
            Materials = gift ? GetHand(_players[holder]).Select((card, index) => new RecipientCategoryMarkMaterial(card.Id, CardLocation.Hand(holder), index, false)).ToArray() : [],
            CandidateSeats = e.Op == SkillProgramEffectOp.SpendCategoryMarkForAreaDiscard ? _players.Where(p => p.IsAlive).Select(p => p.Seat).ToArray() : [] };
        if (e.Op == SkillProgramEffectOp.SpendCategoryMarkForDyingRecovery)
        { ExactDyingOwnedCardEntry(f, out var dying, out var entry); r = r with { DyingFrameId = dying.Id, OriginalDyingCursor = CaptureDyingSuitsOriginalCursor(dying, entry) }; }
        if (e.Op == SkillProgramEffectOp.SpendCategoryMarkForSlashTargets)
        {
            TryGetRecipientCategorySlashUse(f.OwnerSeat, context, out var use, out _);
            r = r with { CardUseFrameId = use.Id, ActionId = use.Action!.ActionId, BaseTargetSeats = use.TargetSeats,
                CandidateSeats = _players.Where(p => CanBeRecipientCategorySlashTarget(use, p)).Select(p => p.Seat).ToArray() };
        }
        ReplaceRuntimeTop(f = f with { RecipientCategoryMark = r });
        _recipientCategoryMarkLedgerStarted = true;
        AdvanceEventRulesAndQueueFact(new RecipientCategoryMarkStartedEvent(f.Id, r.Source, r.GameplayHash, r.SourceSkillId, r.StateId, r.Operation, holder, r.ParentWindowId, token?.TokenId));
        if (r.OriginalDyingCursor is { } original)
            AdvanceEventRulesAndQueueFact(new RecipientCategoryMarkDyingCursorIssuedEvent(f.Id, original.DyingFrameId, DyingSuitsCursorHash(original)));
        if (!gift)
        {
            _recipientCategoryMarks.Remove(new(holder, r.SourceSkillId, r.StateId));
            ChangeRecipientCategoryMarker(f, holder, token!.Kind, -1);
            ReplaceRuntimeTop(f = f with { RecipientCategoryMark = r = r with { Consumed = true } });
            AdvanceEventRulesAndQueueFact(new RecipientCategoryMarkConsumedEvent(f.Id, token.TokenId, holder, r.SourceSkillId, r.StateId, token.Kind));
        }
        if (e.Op == SkillProgramEffectOp.SpendCategoryMarkForDyingRecovery)
        {
            var amount = Math.Max(0, 1 - _players[holder].Hp);
            ReplaceRuntimeTop(f = f with { RecipientCategoryMark = r with { RecoveryIssued = true, RecoveryAmount = amount }, PendingMovementContinuation = new(holder, 0, null) });
            AdvanceEventRulesAndQueueFact(new RecipientCategoryMarkRecoveryIssuedEvent(f.Id, holder, amount));
            new ProgramSkillHost(this).Recover(f.Id, holder, holder, amount, null, null);
            AdvanceRuntimeProgram(f.Id); return SkillProgramStepOutcome.AwaitChild;
        }
        PublishRecipientCategoryMarkChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private void ChangeRecipientCategoryMarker(ProgramSkillFrame f, int holder, RecipientCategoryMarkKind kind, int delta)
    {
        var p = _players[holder]; var marker = RecipientPublicMarker(kind); var key = (marker, holder);
        var owned = checked(p.MarkerSourceCounts.GetValueOrDefault(key) + delta); var total = checked(p.Markers.GetValueOrDefault(marker) + delta);
        if (owned < 0 || total < 0) throw new InvalidOperationException("A category token cannot consume an absent public marker.");
        if (owned == 0) p.MarkerSourceCounts.Remove(key); else p.MarkerSourceCounts[key] = owned;
        if (total == 0) p.Markers.Remove(marker); else p.Markers[marker] = total;
        AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(f.Id, holder, marker, delta, total, holder, RecipientMarkReason(f, "marker")));
    }
    private IReadOnlyList<RecipientCategoryMarkMaterial> RecipientCategoryAreaMaterials(int chooser, int target) =>
        Array.AsReadOnly(new[] { CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment }.SelectMany(zone =>
            _cardZones.CardsAt(new(zone, target)).Select((card, slot) => (card, slot, from: new CardLocation(zone, target))))
            .Where(x => !IsForeignEquipmentDiscardPrevented(chooser, x.card, x.from, OwnedCardMoveIntent.Discard) &&
                !IsSelfHandCategoryDiscardForbidden(chooser, x.card, x.from, OwnedCardMoveIntent.Discard))
            .Select(x => new RecipientCategoryMarkMaterial(x.card.Id, x.from, x.slot, x.from.Zone == CardZoneKind.Hand && chooser != target)).ToArray());
    private bool RecipientCategoryMaterialStillLegal(ProgramSkillFrame f, RecipientCategoryMarkMaterial m)
    {
        if (_cardZones.GetLocation(m.CardId) != m.From || m.From.OwnerSeat is not { } holder || !_players[holder].IsAlive) return false;
        var card = GetAdvancedCard(m.CardId);
        return !IsForeignEquipmentDiscardPrevented(f.OwnerSeat, card, m.From, OwnedCardMoveIntent.Discard) &&
            !IsSelfHandCategoryDiscardForbidden(f.OwnerSeat, card, m.From, OwnedCardMoveIntent.Discard);
    }
    private IReadOnlyList<PromptChoice> RecipientCategoryMarkChoices(ProgramSkillFrame f)
    {
        var r = f.RecipientCategoryMark!; var choices = new List<PromptChoice>();
        PromptChoice Finish(string stage) => new(new($"category-mark.{f.Id}.{stage}.finish"), "完成选择", [], [], RecipientMarkParameters(f, "finish"));
        if (r.Stage == RecipientCategoryMarkStage.ChoosingGift)
            foreach (var m in r.Materials)
                choices.Add(new(new($"category-mark.{f.Id}.gift.{m.CardId}"), PublicPileCardLabel(GetAdvancedCard(m.CardId)), [m.CardId], [], RecipientMarkParameters(f, "gift")));
        else if (r.Stage == RecipientCategoryMarkStage.ChoosingTarget)
            foreach (var seat in r.CandidateSeats)
                choices.Add(new(new($"category-mark.{f.Id}.discard-target.{seat}"), _players[seat].Name, [], [seat], RecipientMarkParameters(f, "discard-target")));
        else if (r.Stage == RecipientCategoryMarkStage.ChoosingCards)
        {
            foreach (var m in r.Materials.Where(m => !r.SelectedCardIds.Contains(m.CardId)))
            {
                var parameters = RecipientMarkParameters(f, "discard-card"); parameters["source-zone"] = m.From.Zone.ToString();
                parameters["slot-index"] = m.SlotIndex.ToString(CultureInfo.InvariantCulture); parameters["card-owner-seat"] = r.TargetSeat!.Value.ToString(CultureInfo.InvariantCulture);
                choices.Add(new(new($"category-mark.{f.Id}.discard.{m.From.Zone}.{m.SlotIndex}"), m.Hidden ? $"第 {m.SlotIndex + 1} 张手牌" : PublicPileCardLabel(GetAdvancedCard(m.CardId)),
                    m.Hidden ? [] : [m.CardId], [], parameters));
            }
            choices.Add(Finish("discard"));
        }
        else if (r.Stage == RecipientCategoryMarkStage.ChoosingSlashTargets)
        {
            foreach (var seat in r.CandidateSeats.Where(s => !r.AddedTargetSeats.Contains(s)))
                choices.Add(new(new($"category-mark.{f.Id}.slash-target.{seat}"), _players[seat].Name, [], [seat], RecipientMarkParameters(f, "slash-target")));
            choices.Add(Finish("slash"));
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishRecipientCategoryMarkChoice(ProgramSkillFrame f)
    {
        var r = f.RecipientCategoryMark!; var chooser = r.Stage == RecipientCategoryMarkStage.ChoosingGift ? r.HolderSeat : f.OwnerSeat;
        var choices = RecipientCategoryMarkChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, r.Stage switch {
            RecipientCategoryMarkStage.ChoosingGift => "正面朝上交出一张手牌，并获得其类别对应的标记。",
            RecipientCategoryMarkStage.ChoosingTarget => "选择一名角色，弃置其区域里至多两张牌。",
            RecipientCategoryMarkStage.ChoosingCards => "选择至多两张牌；他人暗手牌仅显示牌位。",
            _ => "为此杀多指定至多两个合法目标。" }, Array.AsReadOnly(choices.SelectMany(c => c.Cards).Distinct().ToArray()),
            Array.AsReadOnly(choices.SelectMany(c => c.Targets).Distinct().ToArray()), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = chooser, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private void ResolveRecipientCategoryMarkChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Category mark choice lost its owning frame.");
        AssertRecipientCategoryMark(f);
        if (_pendingDecision is not { } prompt || !IsRecipientCategoryMarkChoice(f, prompt) ||
            !RecipientCategoryMarkChoices(f).Any(c => SameNameHandChoicesEqual(c, choice)))
            throw new InvalidOperationException("A category mark choice changed its exact published actor, material or target.");
        var r = f.RecipientCategoryMark!;
        ClearPendingDecision();
        if (r.Stage == RecipientCategoryMarkStage.ChoosingGift)
        { PayRecipientCategoryGift(f, choice.Cards.Single()); return; }
        if (r.Stage == RecipientCategoryMarkStage.ChoosingTarget)
        {
            var target = choice.Targets.Single();
            if (!_players[target].IsAlive) throw new InvalidOperationException("Category discard target is no longer living.");
            ReplaceRuntimeTop(f = f with { RecipientCategoryMark = r with { TargetSeat = target, Materials = RecipientCategoryAreaMaterials(f.OwnerSeat, target), Stage = RecipientCategoryMarkStage.ChoosingCards } });
            PublishRecipientCategoryMarkChoice(f); return;
        }
        if (r.Stage == RecipientCategoryMarkStage.ChoosingCards)
        {
            if (choice.Parameters["mark-action"] != "finish")
            {
                var m = r.Materials.Single(m => m.From.Zone.ToString() == choice.Parameters["source-zone"] && m.SlotIndex.ToString(CultureInfo.InvariantCulture) == choice.Parameters["slot-index"]);
                if (!RecipientCategoryMaterialStillLegal(f, m)) throw new InvalidOperationException("The frozen discard entity is no longer a legal payment.");
                ReplaceRuntimeTop(f = f with { RecipientCategoryMark = r = r with { SelectedCardIds = r.SelectedCardIds.Append(m.CardId).ToArray() } });
                if (r.SelectedCardIds.Count < 2) { PublishRecipientCategoryMarkChoice(f); return; }
            }
            PayRecipientCategoryAreaDiscard(f); return;
        }
        if (choice.Parameters["mark-action"] != "finish")
        {
            var target = choice.Targets.Single();
            if (LifecycleCardUse(r.CardUseFrameId!.Value) is not { } use || !CanBeRecipientCategorySlashTarget(use, _players[target]))
                throw new InvalidOperationException("The frozen extra Slash target is no longer legal.");
            ReplaceRuntimeTop(f = f with { RecipientCategoryMark = r = r with { AddedTargetSeats = r.AddedTargetSeats.Append(target).ToArray() } });
            if (r.AddedTargetSeats.Count < 2) { PublishRecipientCategoryMarkChoice(f); return; }
        }
        CommitRecipientCategorySlashTargets(f);
    }
    private void PayRecipientCategoryGift(ProgramSkillFrame f, int id)
    {
        var r = f.RecipientCategoryMark!;
        if (!_players[f.OwnerSeat].IsAlive || !_players[r.HolderSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            _recipientCategoryMarks.ContainsKey(new(r.HolderSeat, r.SourceSkillId, r.StateId)) || _cardZones.GetLocation(id) != CardLocation.Hand(r.HolderSeat))
        { CancelProgramBindingAndCleanup(f, "赠牌前来源、手牌或无标记条件已失效。"); return; }
        var card = GetAdvancedCard(id); var category = GetProgramCardCategory(card.Kind);
        var kind = category switch { SkillProgramCardCategory.Trick => RecipientCategoryMarkKind.Rescue,
            SkillProgramCardCategory.Equipment => RecipientCategoryMarkKind.Discard, _ => RecipientCategoryMarkKind.ExtraTargets };
        var e = GetProgramTrigger(f).Effects.Single(); var before = RecipientMarkSequence;
        AdvanceEventRulesAndQueueFact(new ProgramCardsRevealedEvent(f.Id, f.SkillId, GetProgramBindingId(f), r.HolderSeat, "category-mark-gift", Array.AsReadOnly(new[] { ToSnapshot(card) })));
        ReplaceRuntimeTop(f = f with { RecipientCategoryMark = r with { Stage = RecipientCategoryMarkStage.GiftChildren, SelectedCardIds = [id], SequenceBefore = before, SequenceAfter = before },
            PendingMovementContinuation = new(r.HolderSeat, 0, null) });
        MoveProgramCardsFromMultipleSources([id], CardLocation.Hand(f.OwnerSeat), new(RecipientMarkReason(f, "gift")), (batch, records) =>
        {
            var token = new RecipientCategoryMarkToken(f.Id, r.HolderSeat, r.SourceSkillId, r.StateId, kind, e.SkillIds[(int)kind], r.Source, r.GameplayHash, records.Single().Sequence);
            _recipientCategoryMarks.Add(new(r.HolderSeat, r.SourceSkillId, r.StateId), token);
            var active = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(active = active with { RecipientCategoryMark = active.RecipientCategoryMark! with { Token = token, BatchId = batch, SequenceAfter = token.GiftSequence } });
            ChangeRecipientCategoryMarker(active, r.HolderSeat, kind, 1);
            AdvanceEventRulesAndQueueFact(new RecipientCategoryMarkMovementPaidEvent(f.Id, r.HolderSeat, 1, before, token.GiftSequence, batch));
            AdvanceEventRulesAndQueueFact(new RecipientCategoryMarkGrantedEvent(f.Id, token));
            // The holder permanently owns this grant. Origin identity stays on the token, never on grant qualification.
            AcquireRuntimeSkills(_players[r.HolderSeat], $"recipient-category:{r.HolderSeat}:{r.SourceSkillId}:{r.StateId}", [token.DerivedSkillId]);
        });
        AdvanceRuntimeProgram(f.Id);
    }
    private void PayRecipientCategoryAreaDiscard(ProgramSkillFrame f)
    {
        var r = f.RecipientCategoryMark!;
        if (r.SelectedCardIds.Count == 0) { CompleteRecipientCategoryMark(f); return; }
        if (r.SelectedCardIds.Any(id => !RecipientCategoryMaterialStillLegal(f, r.Materials.Single(m => m.CardId == id))))
            throw new InvalidOperationException("Category discard cannot substitute a stale selected entity.");
        var before = RecipientMarkSequence;
        ReplaceRuntimeTop(f = f with { RecipientCategoryMark = r with { Stage = RecipientCategoryMarkStage.DiscardChildren, SequenceBefore = before, SequenceAfter = before }, PendingMovementContinuation = new(r.TargetSeat!.Value, 0, null) });
        MoveProgramCardsFromMultipleSources(r.SelectedCardIds, CardLocation.DiscardPile, new(RecipientMarkReason(f, "discard")), (batch, records) =>
        {
            var active = GetActiveProgramFrame(f.Id); var after = records.Max(m => m.Sequence);
            ReplaceRuntimeTop(active with { RecipientCategoryMark = active.RecipientCategoryMark! with { SequenceAfter = after, BatchId = batch } });
            AdvanceEventRulesAndQueueFact(new RecipientCategoryMarkMovementPaidEvent(f.Id, r.TargetSeat!.Value, records.Count, before, after, batch));
        });
        AdvanceRuntimeProgram(f.Id);
    }
    private bool ResumeRecipientCategoryMark(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { RecipientCategoryMark: { } r } f || f.Id != id) return false;
        AssertRecipientCategoryMark(f);
        if (r.Stage is RecipientCategoryMarkStage.ChoosingGift or RecipientCategoryMarkStage.ChoosingTarget or RecipientCategoryMarkStage.ChoosingCards or RecipientCategoryMarkStage.ChoosingSlashTargets)
        { if (_pendingDecision is null) PublishRecipientCategoryMarkChoice(f); return true; }
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return true;
        if (r.Stage == RecipientCategoryMarkStage.RecoveryChildren)
        {
            var before = RecipientMarkSequence;
            ReplaceRuntimeTop(f = f with { RecipientCategoryMark = r with { Stage = RecipientCategoryMarkStage.DrawChildren, DrawIssued = true, DrawBefore = before, DrawAfter = before }, PendingMovementContinuation = new(r.HolderSeat, 0, null) });
            var actual = _players[r.HolderSeat].IsAlive ? DrawCards(_players[r.HolderSeat], 1, true, new(RecipientMarkReason(f, "draw")), r.Source).Count : 0;
            var active = GetActiveProgramFrame(id); var after = RecipientMarkSequence;
            ReplaceRuntimeTop(active with { RecipientCategoryMark = active.RecipientCategoryMark! with { DrawActual = actual, DrawAfter = after } });
            AdvanceEventRulesAndQueueFact(new RecipientCategoryMarkDrawIssuedEvent(id, r.HolderSeat, actual, before, after));
            AdvanceRuntimeProgram(id); return true;
        }
        CompleteRecipientCategoryMark(GetActiveProgramFrame(id)); return true;
    }
    private void CompleteRecipientCategoryMark(ProgramSkillFrame f)
    {
        var r = f.RecipientCategoryMark!;
        ReplaceRuntimeTop(f = f with { RecipientCategoryMark = r with { Stage = RecipientCategoryMarkStage.Complete }, PendingMovementContinuation = null });
        AdvanceEventRulesAndQueueFact(new RecipientCategoryMarkCompletedEvent(f.Id, r.Operation, r.Consumed)); FinishProgramSkill(f, true);
    }
    private bool ReturnRecipientCategoryMarkMovement(ProgramSkillFrame f)
    {
        if (f.RecipientCategoryMark is not { Stage: RecipientCategoryMarkStage.GiftChildren or RecipientCategoryMarkStage.DiscardChildren or RecipientCategoryMarkStage.RecoveryChildren or RecipientCategoryMarkStage.DrawChildren }) return false;
        AssertRecipientCategoryMark(f); AdvanceRuntimeProgram(f.Id); return true;
    }
    private bool CanContinueRecipientCategoryMark(ProgramSkillFrame f) => f.RecipientCategoryMark is { } r &&
        (r.Consumed || r.Stage == RecipientCategoryMarkStage.GiftChildren) && ValidRecipientCategoryMarkReceipt(f);
    private bool IsRecipientCategoryMarkMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) =>
        f.RecipientCategoryMark is { } r && effect?.Op == r.Operation && f.PendingMovementContinuation == p && p.BeforeCount == 0 && p.CoverageResultBind is null &&
        p.SubjectSeat == (r.Stage == RecipientCategoryMarkStage.DiscardChildren ? r.TargetSeat : r.HolderSeat) && ValidRecipientCategoryMarkReceipt(f);
    private PromptChoice SelectAiRecipientCategoryMarkChoice(PendingDecision p, ProgramSkillFrame f) =>
        p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("mark-action") != "finish" &&
            (c.Targets.Count == 0 || !AreProgramDistributionAllies(_players[f.OwnerSeat], _players[c.Targets[0]]))) ?? p.Choices[0];
    private sealed partial class ProgramSkillHost : IRecipientCategoryMarksProgramHost
    {
        public SkillProgramStepOutcome ExecuteRecipientCategoryMark(ProgramSkillFrame f, SkillProgramEffect e) => engine.ExecuteRecipientCategoryMark(f, e);
        public bool CanContinueRecipientCategoryMark(ProgramSkillFrame f) => engine.CanContinueRecipientCategoryMark(f);
    }
}
