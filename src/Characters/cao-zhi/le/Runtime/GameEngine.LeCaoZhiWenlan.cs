using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string WenlanSkill = "le:wenlan";
    private const string WenlanUseBinding = "after-pair-use";
    private const string WenlanPlayedBinding = "after-pair-played";
    private const string WenlanObtainReason = "skill-program.le-cao-zhi.wenlan.obtain";
    private IReadOnlyList<int> WenlanHandLikeIds(int owner) => GetHand(_players[owner])
        .Concat(GetWoodenOxGrain(_players[owner])).Select(card => card.Id).ToArray();
    private bool IsWenlanHandLike(int owner, int cardId) =>
        IsOwnedPlayableLocation(_players[owner], _cardZones.GetLocation(cardId));
    private bool HasLeCaoZhiWenlanCapability => _contentRegistry.ProgramDependencies
        .HasTriggerOperation(SkillProgramEffectOp.ResolveLeCaoZhiWenlan);
    private static WenlanSource WenlanFrameSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_cao_zhi_le_LeCaoZhiWenlan.WenlanFrameSource(f);

    private WenlanSource? CurrentWenlanSource(int owner, SkillProgramTriggerWindow window)
    {
        if (!HasLeCaoZhiWenlanCapability || !IsValidPlayerSeat(owner) || !_players[owner].IsAlive) return null;
        var binding = GetSkillBindingShard(_players[owner]).GetInstanceTriggers(window)
            .Where(b => b.SkillId == WenlanSkill && LeCaoZhiComposition.IsWenlanProgram(b.Program))
            .OrderBy(b => b.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault();
        return binding is null ? null : new(new(binding.SkillId, binding.Trigger.Id, owner, binding.SkillInstanceId), binding.Program.GameplayHash);
    }
    private WenlanActionCapture? CaptureWenlanMaterials(int owner, IReadOnlyList<int> ids,
        SkillProgramTriggerWindow window)
    {
        if (CurrentWenlanSource(owner, window) is not { } source) return null;
        return new(source, ids.Count, ids is [var id] && id > 0 ? CaptureLeCaoZhiFuEntity(id) : null);
    }
    private WenlanActionAcceptedEvent[] WenlanEntries(int owner)
    {
        var retired = ProgramEventHistory<WenlanActionReassignedEvent>().Where(e => e.RetiredEntryId is not null)
            .Select(e => e.RetiredEntryId!.Value).ToHashSet();
        var entries = ProgramEventHistory<WenlanActionAcceptedEvent>().Where(e => e.OwnerSeat == owner && !retired.Contains(e.EntryId)).ToArray();
        if (!ProgramEventHistory<WenlanActionReassignedEvent>().Any(e => e.PreviousActorSeat == owner || e.ActorSeat == owner))
            return entries.OrderBy(e => e.Ordinal).ToArray();
        // Issued pairs never move or roll back. Only still-unclaimed entries
        // form the remaining tail when a real native actor replacement occurs.
        var frozen = new Dictionary<long, int>();
        foreach (var q in ProgramEventHistory<WenlanStartedEvent>().Where(e => e.Qualification.Source.Source.OwnerSeat == owner).Select(e => e.Qualification))
        {
            frozen.Add(q.FirstEntryId, q.SecondOrdinal - 1);
            frozen.Add(q.SecondEntryId, q.SecondOrdinal);
        }
        var next = frozen.Count == 0 ? 0 : frozen.Values.Max();
        return entries.Where(e => frozen.ContainsKey(e.EntryId)).Select(e => e with { Ordinal = frozen[e.EntryId] })
            .Concat(entries.Where(e => !frozen.ContainsKey(e.EntryId)).OrderBy(e => e.EntryId).Select(e => e with { Ordinal = ++next }))
            .OrderBy(e => e.Ordinal).ToArray();
    }
    private int NextWenlanOrdinal(int owner) => WenlanEntries(owner).Select(e => e.Ordinal).DefaultIfEmpty(0).Max() + 1;
    private bool IsWenlanEntryClaimed(long id) => ProgramEventHistory<WenlanStartedEvent>()
        .Any(e => e.Qualification.FirstEntryId == id || e.Qualification.SecondEntryId == id);
    private long WenlanBoundActionId(WenlanActionAcceptedEvent entry) => entry.ActionId != 0 ? entry.ActionId :
        ProgramEventHistory<WenlanLegacyActionBoundEvent>().SingleOrDefault(e => e.EntryId == entry.EntryId)?.ActionId ?? 0;
    private void RecordWenlanAccepted(CardActionContext action, long nativeId, WenlanActionCapture? capture)
    {
        var owner = action.Type == CardActionType.Response ? action.ProviderSeat : action.ActorSeat;
        if (capture?.Source is not { } source || source.Source.OwnerSeat != owner ||
            capture.MaterialCount != action.PhysicalCards.Count) return;
        if (WenlanEntries(owner).Any(e => WenlanBoundActionId(e) == action.ActionId)) return;
        AdvanceEventRulesAndQueueFact(new WenlanActionAcceptedEvent(++_resolutionSequence, owner, NextWenlanOrdinal(owner),
            action.ActionId, nativeId, action.Type, action.ProviderSeat, action.EffectiveKind, capture));
    }

    // The native frame retains this pre-payment invoice; event history contains
    // completed scalar facts, never a use-ID pending object.
    private void PrepareLeCaoZhiWenlanResponse(CharacterState provider, CardKind kind,
        IReadOnlyList<Card> cards)
    {
        var ids = cards.Select(c => c.Id).ToArray();
        if (CaptureWenlanMaterials(provider.Seat, ids, SkillProgramTriggerWindow.CardResponseCompleted) is not { } capture) return;
        var parent = _resolutionStack.LastOrDefault() ?? throw new InvalidOperationException("Wenlan response needs its actual payment owner.");
        if (parent.LeCaoZhiWenlanResponse is not null)
            throw new InvalidOperationException("Wenlan cannot prepare another unpaid response invoice.");
        var p = new WenlanResponseCapture(++_resolutionSequence, parent.Id, provider.Seat, kind, _movementSequence, capture)
        { Materials = cards.Select(c => new CardActionCost(c.Id, c.Kind, FindOwnedCardLocation(provider, c))).ToArray() };
        ReplaceRuntimeFrame(parent.Id, parent with { LeCaoZhiWenlanResponse = p });
        AdvanceEventRulesAndQueueFact(new WenlanResponsePreparedEvent(p.CaptureId, p.NativeParentId, provider.Seat, kind, p.Before, capture));
        for (var i = 0; i < p.Materials.Count; i++)
            AdvanceEventRulesAndQueueFact(new WenlanResponseMaterialEvent(p.CaptureId, i, p.Materials[i]));
    }
    private WenlanActionCapture? BindLeCaoZhiWenlanResponse(CardActionContext action, out long parentId)
    {
        parentId = 0;
        var matches = _resolutionStack.Where(f => f.LeCaoZhiWenlanResponse is { } p && p.ProviderSeat == action.ProviderSeat &&
            p.EffectiveKind == action.EffectiveKind && p.Materials.Select(c => c.CardId).SequenceEqual(action.PhysicalCards.Select(c => c.CardId))).ToArray();
        if (matches.Length == 0) return CaptureWenlanMaterials(action.ProviderSeat,
            action.PhysicalCards.Select(c => c.CardId).ToArray(), SkillProgramTriggerWindow.CardResponseCompleted);
        if (matches is not [var parent] || parent.LeCaoZhiWenlanResponse is not { } payment || parent.Id != payment.NativeParentId ||
            payment.Materials.Count != action.PhysicalCards.Count ||
            !payment.Materials.Zip(action.PhysicalCards).All(pair => pair.First.CardId == pair.Second.CardId &&
                pair.First.CardKind == pair.Second.CardKind && pair.First.From == pair.Second.From) ||
            ProgramEventHistory<WenlanResponsePreparedEvent>().Count(e => e == new WenlanResponsePreparedEvent(payment.CaptureId,
                parent.Id, payment.ProviderSeat, payment.EffectiveKind, payment.Before, payment.Capture)) != 1 ||
            !ProgramEventHistory<WenlanResponseMaterialEvent>().Where(e => e.CaptureId == payment.CaptureId).OrderBy(e => e.Index)
                .Select(e => e.Material).SequenceEqual(payment.Materials) ||
            ProgramEventHistory<WenlanResponseBoundEvent>().Any(e => e.CaptureId == payment.CaptureId) ||
            payment.Materials.Any(c => !_cardMovements.Any(m => m.Sequence > payment.Before && m.CardId == c.CardId &&
                m.CardKind == c.CardKind && m.From == c.From && m.Reason is var reason &&
                (reason == CardMoveReasons.Respond || reason == CardMoveReasons.Nullification))))
            throw new InvalidOperationException("Wenlan requires its exact original, once-paid response materials.");
        parentId = parent.Id;
        ReplaceRuntimeFrame(parent.Id, parent with { LeCaoZhiWenlanResponse = null });
        AdvanceEventRulesAndQueueFact(new WenlanResponseBoundEvent(payment.CaptureId, action.ActionId));
        return payment.Capture;
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 29081)]
    private void ObserveLeCaoZhiWenlanAcceptance(IGameEvent payload)
    {
        if (!HasLeCaoZhiWenlanCapability) return;
        if (payload is CardUseDeclaredEvent declared && LifecycleCardUse(declared.ResolutionId) is { } use)
        {
            IReadOnlyList<int> ids = use.Action is { } declaredAction
                ? declaredAction.PhysicalCards.Select(c => c.CardId).ToArray() : use.PhysicalCardIds ?? [];
            // Capture material names even before a later native actor replacement
            // grants the actual user a Wenlan source. The nullable source alone
            // decides admission; no earlier unowned action is counted.
            var capture = new WenlanActionCapture(CurrentWenlanSource(use.SourceSeat, SkillProgramTriggerWindow.CardUseCompleted),
                ids.Count, ids is [var id] && id > 0 ? CaptureLeCaoZhiFuEntity(id) : null);
            ReplaceRuntimeFrame(use.Id, use with { LeCaoZhiWenlanUse = capture });
            // The declaration is the native accepted Use boundary. Its payment
            // may open child Uses before targets are finally accepted later.
            if (use.Action is { } declaredUse) RecordWenlanAccepted(declaredUse, use.Id, capture);
            // Exact legacy zero-material declarations acquire their Action only
            // at native completion. Preserve this earlier accepted order now.
            if (capture.Source is not null && use.Action is null && use.CardId == 0 && ids.Count == 0 && IsActualTurnLegacyVirtualUse(use) &&
                !WenlanEntries(use.SourceSeat).Any(e => e.NativeFrameId == use.Id && e.ActionId == 0))
                AdvanceEventRulesAndQueueFact(new WenlanActionAcceptedEvent(++_resolutionSequence, use.SourceSeat,
                    NextWenlanOrdinal(use.SourceSeat), 0, use.Id, CardActionType.Use, use.SourceSeat, use.CardKind, capture));
            return;
        }
        if (payload is ProgramCardUseActorReplacedEvent replaced)
        { ReassignWenlanNativeActor(replaced); return; }
        if (payload is not CardActionAcceptedEvent accepted) return;
        var action = accepted.Action;
        if (action.Type == CardActionType.Response)
        {
            var capture = BindLeCaoZhiWenlanResponse(action, out var parent);
            RecordWenlanAccepted(action, parent, capture); return;
        }
        if (LifecycleCardUseForWenlan(action.ActionId) is not { } native) return;
        var legacy = WenlanEntries(action.ActorSeat).SingleOrDefault(e => e.NativeFrameId == native.Id && e.ActionId == 0);
        if (legacy is not null)
        {
            if (!ProgramEventHistory<WenlanLegacyActionBoundEvent>().Any(e => e.EntryId == legacy.EntryId))
                AdvanceEventRulesAndQueueFact(new WenlanLegacyActionBoundEvent(legacy.EntryId, action.ActionId));
            return;
        }
        RecordWenlanAccepted(action, native.Id, native.LeCaoZhiWenlanUse);
    }
    private void ReassignWenlanNativeActor(ProgramCardUseActorReplacedEvent changed)
    {
        if (LifecycleCardUse(changed.CardUseFrameId) is not { Action: { Type: CardActionType.Use } action, LeCaoZhiWenlanUse: { } original } use) return;
        if (use.SourceSeat != changed.ActorSeat || action.ActorSeat != changed.ActorSeat || action.ProviderSeat != changed.ProviderSeat ||
            _resolutionStack.SingleOrDefault(f => f.Id == changed.FrameId) is not ProgramSkillFrame producer || producer.SkillId != changed.SkillId ||
            producer.OwnerSeat != changed.PreviousActorSeat || changed.OwnerSeat != changed.PreviousActorSeat ||
            producer.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } context } ||
            context.ParentCardUseFrameId != use.Id || context.CardActionId != action.ActionId ||
            GetPausedPrivateOfferEffect(producer)?.Op != SkillProgramEffectOp.ReplaceCurrentCardUseActor ||
            !producer.SelectedTargetSeats.SequenceEqual([changed.ActorSeat]))
            throw new InvalidOperationException("Wenlan actor reassignment requires the exact native role-change instruction and paid action.");
        var capture = original with { Source = CurrentWenlanSource(changed.ActorSeat, SkillProgramTriggerWindow.CardUseCompleted) };
        ReplaceRuntimeFrame(use.Id, use with { LeCaoZhiWenlanUse = capture });
        var retired = ProgramEventHistory<WenlanActionReassignedEvent>().Where(e => e.RetiredEntryId is not null).Select(e => e.RetiredEntryId!.Value).ToHashSet();
        var all = ProgramEventHistory<WenlanActionAcceptedEvent>().Where(e => WenlanBoundActionId(e) == action.ActionId).ToArray();
        // A later mutation cannot undo a reward already issued while this Use
        // was an earlier member of another completed pair.
        if (all.Any(e => IsWenlanEntryClaimed(e.EntryId))) return;
        var prior = all.SingleOrDefault(e => !retired.Contains(e.EntryId));
        if (prior is not null && prior.OwnerSeat != changed.PreviousActorSeat)
            throw new InvalidOperationException("Wenlan cannot skip the native actor replacement chain.");
        var newId = capture.Source is null ? (long?)null : ++_resolutionSequence;
        AdvanceEventRulesAndQueueFact(new WenlanActionReassignedEvent(use.Id, action.ActionId, changed.PreviousActorSeat,
            changed.ActorSeat, prior?.EntryId, newId));
        if (newId is { } entryId)
            AdvanceEventRulesAndQueueFact(new WenlanActionAcceptedEvent(entryId, changed.ActorSeat, NextWenlanOrdinal(changed.ActorSeat),
                action.ActionId, use.Id, action.Type, action.ProviderSeat, action.EffectiveKind, capture));
    }
    private CardUseFrame? LifecycleCardUseForWenlan(long actionId) =>
        _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(f => f.Action?.ActionId == actionId);
    private bool IsWenlanTrueUse(CardActionContext action) => action.Type == CardActionType.Use ||
        IsCompletedResponseUse(action, ProgramCompletedResponseKind.Nullification) ||
        IsCompletedResponseUse(action, ProgramCompletedResponseKind.Dodge) &&
        _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(use => use.Action?.ActionId == action.ParentActionId)
            is { CardAttack: { EffectiveCardKind: { } kind } attack } && IsSlashCard(kind) && attack.TargetSeat == action.ActorSeat;
    private void CaptureLeCaoZhiWenlanQualification(CardActionContext action, SkillProgramTriggerWindow window)
    {
        if (!HasLeCaoZhiWenlanCapability || window is not (SkillProgramTriggerWindow.CardUseCompleted or SkillProgramTriggerWindow.CardResponseCompleted)) return;
        if (window == SkillProgramTriggerWindow.CardUseCompleted ? !IsWenlanTrueUse(action) : IsWenlanTrueUse(action)) return;
        var owner = window == SkillProgramTriggerWindow.CardResponseCompleted ? action.ProviderSeat : action.ActorSeat;
        var entries = WenlanEntries(owner);
        var current = entries.SingleOrDefault(e => WenlanBoundActionId(e) == action.ActionId);
        if (current is null || current.Ordinal % 2 != 0 || entries.SingleOrDefault(e => e.Ordinal == current.Ordinal - 1) is not { } first ||
            CurrentWenlanSource(owner, window) is not { } source ||
            ProgramEventHistory<WenlanStartedEvent>().Any(e => e.Qualification.SecondEntryId == current.EntryId)) return;
        var q = new WenlanPairQualification(source, first.EntryId, current.EntryId, action.ActionId, window) { SecondOrdinal = current.Ordinal };
        if (!ProgramEventHistory<WenlanPairQualifiedEvent>().Any(e => e.Qualification == q))
            AdvanceEventRulesAndQueueFact(new WenlanPairQualifiedEvent(q));
    }
    private WenlanPairQualification? WenlanQualificationFor(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        c.SkillId != WenlanSkill ? null : context.CardUse is not { } use ? WenlanDirectQualificationFor(c, context) :
        ProgramEventHistory<WenlanPairQualifiedEvent>().Select(e => e.Qualification).SingleOrDefault(q => q.ActionId == use.CardActionId &&
            q.Source == new WenlanSource(new(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId), c.GameplayHash) && q.Window == context.Window);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 29081, SkillProgramEffectOp.ResolveLeCaoZhiWenlan)]
    private bool CanOfferLeCaoZhiWenlan(ProgramTriggerLegalityContext gate) => gate.Owner.IsAlive &&
        WenlanQualificationFor(gate.Candidate, gate.Context) is { } q &&
        !ProgramEventHistory<WenlanStartedEvent>().Any(e => e.Qualification.SecondEntryId == q.SecondEntryId);
    private static CardKind[] WenlanNames(WenlanActionAcceptedEvent entry) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_cao_zhi_le_LeCaoZhiWenlan.WenlanNames(entry);
    private WenlanActionAcceptedEvent WenlanEntry(long id)
    { var raw = ProgramEventHistory<WenlanActionAcceptedEvent>().Single(e => e.EntryId == id); return WenlanEntries(raw.OwnerSeat).Single(e => e.EntryId == id); }
    private bool WenlanWindowMatches(ProgramCardTriggerWindowFrame window, WenlanPairQualification q) =>
        q.DirectDuelEntryId is null && GetCardActionWindow(window) == q.Window && window.Action.ActionId == q.ActionId &&
        (q.Window == SkillProgramTriggerWindow.CardUseCompleted
            ? window.Action.ActorSeat == q.Source.Source.OwnerSeat && IsWenlanTrueUse(window.Action) &&
              (window.Action.Type != CardActionType.Response || window.CompletedResponseReturn is { IsCommitted: false } && IsCompletedResponseParentConsistent(window))
            : window.ResponseCompletion is { CostsDrained: true } response && response.CompletionActorSeat == q.Source.Source.OwnerSeat &&
              ValidResponseCompletionWindow(window) && !IsWenlanTrueUse(window.Action));

    private SkillProgramStepOutcome BeginLeCaoZhiWenlan(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.LeCaoZhiWenlan is not null || f.SkillId != WenlanSkill || f.TriggerId is not (WenlanUseBinding or WenlanPlayedBinding) ||
            f.ActivationId != f.TriggerId || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            f.WindowContext is not { } context ||
            WenlanQualificationFor(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex), context) is not { } q ||
            ProgramEventHistory<WenlanStartedEvent>().Any(e => e.Qualification.SecondEntryId == q.SecondEntryId))
            throw new InvalidOperationException("Wenlan requires its exact accepted pair and the second action's completed native window.");
        long windowId; long nativeParentId; WenlanNativeParentKind nativeKind;
        if (TryGetWenlanDirectWindow(f, out var direct))
        { windowId = direct.Id; nativeParentId = direct.ParentFrameId; nativeKind = WenlanNativeParentKind.DirectDuel; }
        else if (_resolutionStack.SingleOrDefault(item => item.Id == context.ParentFrameId) is ProgramCardTriggerWindowFrame window && WenlanWindowMatches(window, q) &&
            DesignatedExtraTargetCandidateMatches(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex), context, window))
        { windowId = window.Id; nativeParentId = window.ParentFrameId; nativeKind = WenlanNativeParentKind.CardAction; }
        else throw new InvalidOperationException("Wenlan cannot borrow another native action or direct-Duel parent.");
        var first = WenlanEntry(q.FirstEntryId); var second = WenlanEntry(q.SecondEntryId);
        var one = WenlanNames(first); var two = WenlanNames(second);
        var common = one.Intersect(two).Any();
        var r = new WenlanReceipt(q, windowId, nativeParentId, common ? WenlanStage.Obtaining : WenlanStage.SelectingHand, common)
        { Before = _movementSequence, After = _movementSequence, NativeParentKind = nativeKind };
        ReplaceRuntimeTop(f = f with { LeCaoZhiWenlan = r });
        AdvanceEventRulesAndQueueFact(new WenlanStartedEvent(f.Id, q, windowId, nativeParentId, common));
        if (common) ObtainWenlanNamedCards(f, one.Concat(two).Distinct().Order().ToArray());
        else
        {
            var hand = WenlanHandLikeIds(f.OwnerSeat);
            ReplaceRuntimeTop(f = f with { LeCaoZhiWenlan = r with { HandIds = hand } });
            for (var i = 0; i < hand.Count; i++) AdvanceEventRulesAndQueueFact(new WenlanHandCandidateEvent(f.Id, i, hand[i]));
            if (hand.Count == 0) FinishWenlan(f); else PublishWenlanHandChoices(f);
        }
        return SkillProgramStepOutcome.AwaitChild;
    }
    private IReadOnlyList<PromptChoice> WenlanHandChoices(ProgramSkillFrame f)
    {
        var r = f.LeCaoZhiWenlan!;
        var choices = r.HandIds.Select(id => new PromptChoice(new($"le-cao-zhi.wenlan.{f.Id}.toggle.{id}"),
            $"{(r.SelectedIds.Contains(id) ? "取消选择" : "选择")}【{GetAdvancedCard(id).DisplayName}】", [id], [],
            new Dictionary<string, string> { ["program-action"] = "le-cao-zhi-wenlan-hand", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["verb"] = "toggle" })).ToList();
        choices.Add(new(new($"le-cao-zhi.wenlan.{f.Id}.confirm"), $"确定（已选{r.SelectedIds.Count}张）", [], [],
            new Dictionary<string, string> { ["program-action"] = "le-cao-zhi-wenlan-hand", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["verb"] = "confirm" }));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishWenlanHandChoices(ProgramSkillFrame f)
    {
        var r = f.LeCaoZhiWenlan!; var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "文澜：选择任意张手牌，普通牌标记为赋，赋牌替换其额外牌名；可以不选。",
            r.HandIds, [], f.OwnerSeat) { PromptId = CreatePromptId(), IsPrivate = true, Choices = WenlanHandChoices(f),
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanCardSelection : EngineStatus.Running;
    }
    [ProgramAnswer("le-cao-zhi-wenlan-hand")]
    private void AnswerWenlanHand(PromptChoice choice)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id); AssertLeCaoZhiWenlan(f);
        if (f.LeCaoZhiWenlan is not { Stage: WenlanStage.SelectingHand } r ||
            !AssistedChoicesEqual([choice], WenlanHandChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Wenlan only accepts its currently published private Hand choice.");
        ClearPendingDecision();
        if (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
        { FinishWenlan(f); return; }
        if (choice.Parameters.GetValueOrDefault("verb") == "toggle")
        {
            var id = choice.Cards.Single();
            if (!IsWenlanHandLike(f.OwnerSeat, id)) throw new InvalidOperationException("Wenlan selected Hand entity moved.");
            var selected = r.SelectedIds.Contains(id) ? r.SelectedIds.Where(c => c != id).ToArray() : r.SelectedIds.Append(id).Order().ToArray();
            ReplaceRuntimeTop(f = f with { LeCaoZhiWenlan = r with { SelectedIds = selected } });
            AdvanceEventRulesAndQueueFact(new WenlanHandToggledEvent(f.Id, id, selected.Contains(id))); PublishWenlanHandChoices(f); return;
        }
        if (choice.Parameters.GetValueOrDefault("verb") != "confirm" || choice.Cards.Count != 0 ||
            r.SelectedIds.Any(id => !IsWenlanHandLike(f.OwnerSeat, id)))
            throw new InvalidOperationException("Wenlan confirmation must retain all actual selected Hand entities.");
        ReplaceRuntimeTop(f = f with { LeCaoZhiWenlan = r with { Stage = WenlanStage.MarkingHand } });
        AdvanceEventRulesAndQueueFact(new WenlanHandConfirmedEvent(f.Id, r.SelectedIds.Count));
        MarkWenlanFuEntities(f, r.SelectedIds, FuMarkOrigin.WenlanHandRewrite);
        FinishWenlan(GetActiveProgramFrame(f.Id));
    }
    private void ObtainWenlanNamedCards(ProgramSkillFrame f, IReadOnlyList<CardKind> names)
    {
        var plan = names.Select(kind => new WenlanNamedObtain(kind, DeckNamedCard(kind)?.Id)).ToArray();
        var ids = plan.Where(p => p.CardId is not null).Select(p => p.CardId!.Value).ToArray();
        var r = f.LeCaoZhiWenlan!;
        ReplaceRuntimeTop(f = f with { LeCaoZhiWenlan = r with { ObtainPlan = plan, Before = _movementSequence, After = _movementSequence },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        for (var i = 0; i < plan.Length; i++)
            AdvanceEventRulesAndQueueFact(new WenlanNamedObtainPlannedEvent(f.Id, i, plan[i].Kind, plan[i].CardId));
        if (ids.Length > 0) MoveCards(ids.Select(GetAdvancedCard).ToArray(), CardLocation.DrawPile, CardLocation.Hand(f.OwnerSeat), new(WenlanObtainReason), move =>
        {
            var current = GetActiveProgramFrame(f.Id); var receipt = current.LeCaoZhiWenlan!;
            ReplaceRuntimeTop(current with { LeCaoZhiWenlan = receipt with { ObtainedIds = receipt.ObtainedIds.Append(move.CardId).ToArray(), After = move.Sequence } });
            AdvanceEventRulesAndQueueFact(new WenlanEntityObtainedEvent(f.Id, move.CardId, move.CardKind, move.Sequence));
        });
        f = GetActiveProgramFrame(f.Id); r = f.LeCaoZhiWenlan!;
        ReplaceRuntimeTop(f = f with { LeCaoZhiWenlan = r with { Stage = WenlanStage.ObtainChildren, After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new WenlanObtainReturnedEvent(f.Id, r.ObtainedIds.Count, r.Before, _movementSequence));
        MarkWenlanFuEntities(f, r.ObtainedIds, FuMarkOrigin.WenlanObtain);
        ContinueLeCaoZhiWenlan(f.Id);
    }
    private bool IsExactWenlanFuMarking(ProgramSkillFrame f, IReadOnlyList<int> ids, FuMarkOrigin origin)
    {
        if (!WenlanRootMatches(f) || ids.Distinct().Count() != ids.Count || ids.Any(id => id <= 0)) return false;
        var r = f.LeCaoZhiWenlan!;
        if (origin == FuMarkOrigin.WenlanHandRewrite) return r.Stage == WenlanStage.MarkingHand && ids.SequenceEqual(r.SelectedIds) &&
            ids.All(id => r.HandIds.Contains(id) && IsWenlanHandLike(f.OwnerSeat, id)) &&
            ProgramEventHistory<WenlanHandConfirmedEvent>().Where(e => e.FrameId == f.Id).ToArray() is [var confirmed] && confirmed.Count == ids.Count;
        return origin == FuMarkOrigin.WenlanObtain && r.Stage == WenlanStage.ObtainChildren && ids.SequenceEqual(r.ObtainedIds) &&
            ids.All(id => _cardZones.GetLocation(id) == CardLocation.Hand(f.OwnerSeat)) &&
            ProgramEventHistory<WenlanObtainReturnedEvent>().Where(e => e.FrameId == f.Id).ToArray() is [var returned] &&
            returned.Count == ids.Count && returned.Before == r.Before && returned.After == r.After &&
            ProgramEventHistory<WenlanEntityObtainedEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Sequence).Select(e => e.CardId).SequenceEqual(ids) &&
            ids.All(id => _cardMovements.Count(m => m.Sequence > r.Before && m.Sequence <= r.After && m.CardId == id &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == WenlanObtainReason &&
                r.ObtainPlan.Any(p => p.CardId == id && p.Kind == m.CardKind)) == 1);
    }
    private void FinishWenlan(ProgramSkillFrame f)
    {
        var r = f.LeCaoZhiWenlan!;
        if (ProgramEventHistory<WenlanCompletedEvent>().Any(e => e.FrameId == f.Id)) throw new InvalidOperationException("Wenlan pair cannot complete twice.");
        AdvanceEventRulesAndQueueFact(new WenlanCompletedEvent(f.Id, f.OwnerSeat, r.Qualification.FirstEntryId, r.Qualification.SecondEntryId,
            r.CommonName, r.CommonName ? r.ObtainedIds.Count : r.SelectedIds.Count));
        ReplaceRuntimeTop(f with { LeCaoZhiWenlan = null, PendingMovementContinuation = null });
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(f.Id), "文澜已发行的标记或实体获得及其子结算已结清。");
        else AdvanceRuntimeProgram(f.Id);
    }
    private void ContinueLeCaoZhiWenlan(long id)
    {
        var f = GetActiveProgramFrame(id); AssertLeCaoZhiWenlan(f);
        if (f.LeCaoZhiWenlan is { Stage: WenlanStage.SelectingHand })
        { if (_pendingDecision is null) PublishWenlanHandChoices(f); return; }
        if (f.PendingMovementContinuation is not null && TryDrainFireTargetMovement(f)) return;
        FinishWenlan(GetActiveProgramFrame(id));
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 29081)]
    private bool ResumeLeCaoZhiWenlan(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.LeCaoZhiWenlan is null) return false; ContinueLeCaoZhiWenlan(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 29081)]
    private bool ReturnLeCaoZhiWenlanMovement(ProgramSkillFrame f)
    { if (f.LeCaoZhiWenlan is null || f.PendingMovementContinuation is null) return false; ContinueLeCaoZhiWenlan(f.Id); return true; }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 310)]
    private bool IsWenlanMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        f.LeCaoZhiWenlan is { Stage: WenlanStage.ObtainChildren } && effect?.Op == SkillProgramEffectOp.ResolveLeCaoZhiWenlan &&
        pending == f.PendingMovementContinuation && pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29081)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 29081)]
    private bool PermitPaidLeCaoZhiWenlan(ProgramSkillFrame f)
    { if (f.LeCaoZhiWenlan is not { Stage: WenlanStage.Obtaining or WenlanStage.ObtainChildren or WenlanStage.MarkingHand }) return false; AssertLeCaoZhiWenlan(f); return true; }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveLeCaoZhiWenlan)]
    private PromptChoice SelectAiWenlanHand(PendingDecision decision, ProgramSkillFrame f) => decision.Choices.FirstOrDefault(c =>
        c.Cards is [var id] && !f.LeCaoZhiWenlan!.SelectedIds.Contains(id) && CaptureLeCaoZhiFuEntity(id) is null) ??
        decision.Choices.Single(c => c.Parameters.GetValueOrDefault("verb") == "confirm");

    private bool WenlanRootMatches(ProgramSkillFrame f)
    {
        if (f.LeCaoZhiWenlan is not { } r || f.SkillId != WenlanSkill || f.TriggerId is not (WenlanUseBinding or WenlanPlayedBinding) ||
            f.ActivationId != f.TriggerId || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { } context ||
            _contentRegistry.GetSkill(f.SkillId).Program is not { } program || program.GameplayHash != f.GameplayHash || !LeCaoZhiComposition.IsWenlanProgram(program) ||
            r.Qualification.SecondOrdinal < 2 || r.Qualification.SecondOrdinal % 2 != 0 ||
            r.Qualification.Source != WenlanFrameSource(f) || context.ParentFrameId != r.WindowId || !WenlanNativeParentMatches(f, r, context)) return false;
        var first = WenlanEntry(r.Qualification.FirstEntryId); var second = WenlanEntry(r.Qualification.SecondEntryId);
        var index = _resolutionStack.FindIndex(item => item.Id == f.Id);
        return index > 0 && _resolutionStack[index - 1].Id == r.WindowId && first.OwnerSeat == f.OwnerSeat && second.OwnerSeat == f.OwnerSeat &&
            second.Ordinal % 2 == 0 && first.Ordinal + 1 == second.Ordinal &&
            (r.NativeParentKind == WenlanNativeParentKind.DirectDuel ? r.Qualification.DirectDuelEntryId == second.EntryId : WenlanBoundActionId(second) == r.Qualification.ActionId) &&
            r.CommonName == WenlanNames(first).Intersect(WenlanNames(second)).Any() &&
            ProgramEventHistory<WenlanPairQualifiedEvent>().Count(e => e.Qualification == r.Qualification) == 1 &&
            ProgramEventHistory<WenlanStartedEvent>().Count(e => e == new WenlanStartedEvent(f.Id, r.Qualification, r.WindowId, r.NativeParentId, r.CommonName)) == 1 &&
            ProgramEventHistory<WenlanStartedEvent>().Count(e => e.Qualification.SecondEntryId == second.EntryId) == 1;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 29081)]
    private void AssertLeCaoZhiWenlan(ProgramSkillFrame f)
    {
        if (f.LeCaoZhiWenlan is not { } r) return;
        if (!WenlanRootMatches(f) || !Enum.IsDefined(r.Stage) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            r.HandIds.Distinct().Count() != r.HandIds.Count || r.SelectedIds.Distinct().Count() != r.SelectedIds.Count || r.SelectedIds.Any(id => !r.HandIds.Contains(id)))
            throw new InvalidOperationException("Wenlan lost its exact accepted pair, completed native parent or frame-owned selection.");
        if (r.Stage == WenlanStage.SelectingHand && (r.CommonName || r.ObtainPlan.Count != 0 || r.ObtainedIds.Count != 0 ||
            r.HandIds.Any(id => !IsWenlanHandLike(f.OwnerSeat, id)) ||
            _resolutionStack.LastOrDefault()?.Id == f.Id && (_pendingDecision is not { IsPrivate: true, Kind: DecisionKind.ProgramTrigger } p ||
                p.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual(p.Choices, WenlanHandChoices(f)))))
            throw new InvalidOperationException("Wenlan's private selection must retain its actual own Hand and canonical choices.");
        if (r.Stage == WenlanStage.ObtainChildren && (!r.CommonName || r.ObtainedIds.Distinct().Count() != r.ObtainedIds.Count ||
            !ProgramEventHistory<WenlanNamedObtainPlannedEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index)
                .Select(e => new WenlanNamedObtain(e.Kind, e.CardId)).SequenceEqual(r.ObtainPlan) ||
            ProgramEventHistory<WenlanObtainReturnedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var returned] ||
            returned.Count != r.ObtainedIds.Count || returned.Before != r.Before || returned.After != r.After ||
            !ProgramEventHistory<WenlanEntityObtainedEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Sequence).Select(e => e.CardId).SequenceEqual(r.ObtainedIds) ||
            r.ObtainedIds.Any(id => _cardMovements.Count(m => m.CardId == id && WenlanOperationMovement(f, m)) != 1)))
            throw new InvalidOperationException("Wenlan cannot invent, duplicate or repay its actual named deck obtains.");
    }
    private bool WenlanOperationMovement(ProgramSkillFrame f, CardMovementRecord m) => f.LeCaoZhiWenlan is { Stage: WenlanStage.ObtainChildren } r &&
        _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After && r.ObtainedIds.Contains(m.CardId) &&
        m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == WenlanObtainReason &&
        r.ObtainPlan.Any(p => p.CardId == m.CardId && p.Kind == m.CardKind);
    private bool WenlanFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!WenlanRootMatches(f)) return false;
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state) return state.ResumeProgramFrameId == f.Id && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
            ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id && e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
        if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        return child is CardsMovedTriggerWindowFrame moved && ProgramMovementReturnMatches(f, moved) &&
            moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => WenlanOperationMovement(f, m));
    }
    private bool WenlanObserverSuffix(int index) => HasObserverSuffix(index, static (g, child) => g.IsMovementObserverEdge(child) || g.IsSameNameHandObserverEdge(child) ||
        g.IsYanPuDyingExitEdge(child) || g.TypedDamagePostEventEdge(g._resolutionStack[child - 1], g._resolutionStack[child]) ||
        g._resolutionStack[child - 1] is ProgramSkillFrame f && (g.WenlanFirstChild(f, g._resolutionStack[child]) || f.Zenrun is not null && g.ZenrunFirstChild(f, g._resolutionStack[child])) ||
        NativeDrawAttemptRidesOn(g._resolutionStack[child], g._resolutionStack[child - 1]) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[child], g._resolutionStack[child - 1]),
        static (g, child, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(child, dying));
    private ProgramSkillFrame? WenlanObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.WenlanFirstChild(f, child), static (g, index) => g.WenlanObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1260)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1370)]
    private bool HasWenlanDamageObserver(long id) => WenlanObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1400)]
    private bool HasWenlanBeforeDamageObserver(long id) => WenlanObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 1230)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 790)]
    private bool IsWenlanDying() => ActiveDying is { } dying && WenlanObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 50)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1800)]
    private bool AllowsWenlanNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || WenlanObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 560)]
    private bool IsWenlanRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.LeCaoZhiWenlan is null || recovery.ParentFrameId == f.Id) return false;
        var parent = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return parent >= 0 && child > parent + 1 && WenlanFirstChild(f, _resolutionStack[parent + 1]) && WenlanObserverSuffix(parent) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 29081)]
    private bool AdvanceWenlanChildren() => _pendingDecision is null && WenlanObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    private bool HasLeCaoZhiWenlanUseContinuation(long nativeId)
    {
        if (_resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.LeCaoZhiWenlan?.NativeParentId == nativeId).ToArray() is not [var f] || !WenlanRootMatches(f)) return false;
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id); var native = _resolutionStack.FindIndex(x => x.Id == nativeId);
        if (native < 0 || i != native + 2 || _resolutionStack[native + 1].Id != f.LeCaoZhiWenlan!.WindowId) return false;
        AssertLeCaoZhiWenlan(f);
        return i == _resolutionStack.Count - 1 ? f.LeCaoZhiWenlan.Stage == WenlanStage.SelectingHand &&
            _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p && p.PlayerSeat == f.OwnerSeat && AssistedChoicesEqual(p.Choices, WenlanHandChoices(f)) ||
            f.LeCaoZhiWenlan.Stage == WenlanStage.ObtainChildren && _pendingDecision is null :
            WenlanFirstChild(f, _resolutionStack[i + 1]) && WenlanObserverSuffix(i);
    }
    private bool HasLeCaoZhiWenlanRescueContinuation(long dyingId)
    {
        if (_resolutionStack.OfType<CardUseFrame>().Where(use => use.DyingResponse?.ResolutionId == dyingId).ToArray() is not [var rescue] ||
            rescue.DyingResponse is not { } response || rescue.Action is not { Type: CardActionType.Use } action ||
            rescue.CardKind is not (CardKind.Peach or CardKind.Alcohol) || action.ActorSeat != response.ResponderSeat || rescue.SourceSeat != response.ResponderSeat ||
            _resolutionStack.SingleOrDefault(x => x.Id == response.ResolutionId) is not DyingFrame dying ||
            !rescue.TargetSeats.SequenceEqual([dying.VictimSeat]) || !HasLeCaoZhiWenlanUseContinuation(rescue.Id)) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == rescue.Id);
        return index > 0 && _resolutionStack[index - 1].Id == dying.Id &&
            _resolutionStack[index + 1] is ProgramCardTriggerWindowFrame { Continuation: ProgramCardContinuation.CompletedCard } w && w.Action.ActionId == action.ActionId &&
            (rescue.CardKind == CardKind.Peach ? response.UsedPeach : response.UsedAlcohol && dying.VictimSeat == rescue.SourceSeat);
    }
    [ProgramCapability]
    private sealed class LeCaoZhiWenlanCapability(GameEngine engine) : ILeCaoZhiWenlanHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f) => engine.BeginLeCaoZhiWenlan(f); }
}
