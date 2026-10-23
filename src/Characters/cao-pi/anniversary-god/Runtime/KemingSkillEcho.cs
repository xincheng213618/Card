using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record KemingEchoEntitlement(long IssueFrameId, CardConversionSource Source, string GameplayHash);
public sealed record KemingProducedName(CardKind Name, bool IsDraw, long ProducerId, long Sequence);
public sealed record KemingSourceCapture(long InvocationId, long ParentFrameId, int ActorSeat, string SkillKey,
    string OccurrenceKey, string SkillInstanceId, ActualDiscardRecoveryPhaseKey Phase, bool Closed = false)
{
    private IReadOnlyList<KemingEchoEntitlement> _entitlements = Array.AsReadOnly(Array.Empty<KemingEchoEntitlement>());
    private IReadOnlyList<KemingProducedName> _names = Array.AsReadOnly(Array.Empty<KemingProducedName>());
    public IReadOnlyList<KemingEchoEntitlement> Entitlements { get => _entitlements; init => _entitlements = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<KemingProducedName> Names { get => _names; init => _names = Array.AsReadOnly(value.ToArray()); }
}
public enum KemingSkillEchoStage { Choosing, Drawing, Children, Finished }
public enum KemingEchoReturnKind { ProgramCompletion, CardWindow, CixiongCompletion }
public sealed record KemingEchoReturn(KemingEchoReturnKind Kind, bool SourceCompleted = true, bool Activated = false,
    bool TargetDiscarded = false, int? DiscardedCardId = null, int SourceDrawCount = 0);
[JsonDerivedType(typeof(KemingSkillEchoFrame), "keming-skill-echo")]
public abstract partial record ResolutionFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public KemingSourceCapture? KemingCapture { get; init; } }
public sealed record KemingSkillEchoFrame(long Id, long ParentFrameId, KemingSourceCapture Capture, KemingEchoReturn Return,
    int Cursor = 0, KemingSkillEchoStage Stage = KemingSkillEchoStage.Choosing,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect, NativeDrawOutcome? Outcome = null)
    : ResolutionFrame(Id, ResolutionFrameKind.KemingSkillEcho, Step);
public sealed partial record CardsMovedTriggerWindowFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? ResumeKemingEchoFrameId { get; init; } }
public sealed record KemingEchoReservedEvent(long IssueFrameId, long InvocationId, long ParentFrameId, int ActorSeat,
    string SkillKey, string OccurrenceKey) : IGameEvent;
public sealed record KemingEchoStartedEvent(long FrameId, long ParentFrameId, long InvocationId, int Count) : IGameEvent;
public sealed record KemingEchoChoiceEvent(long FrameId, int Cursor, int OwnerSeat, bool Accepted) : IGameEvent;
public sealed record KemingEchoDrawPaidEvent(long FrameId, int Cursor, int OwnerSeat, int Requested, int Actual,
    long Before, long After, bool Replaced) : IGameEvent;
public sealed record KemingEchoFinishedEvent(long FrameId, long ParentFrameId, long InvocationId) : IGameEvent;
public sealed record KemingSourceCardUsedEvent(long InvocationId, long SourceFrameId, long ActionId) : IGameEvent;

public sealed partial class GameEngine
{
    private bool HasKemingInvocationObservers => HasShenCaoPiCapability && (_resolutionStack.Any(f => f.KemingCapture is { Closed: false }) ||
        ProgramEventHistory<KemingEchoArmedEvent>().Any(e => !ProgramEventHistory<KemingEchoReservedEvent>().Any(r => r.IssueFrameId == e.FrameId)));
    private void CaptureKemingInvocation(long invocationId, long parentFrameId, int actorSeat, string skillKey, string occurrenceKey, string skillInstanceId)
    {
        if (!HasKemingInvocationObservers || parentFrameId <= 0 || CurrentActualDiscardRecoveryPhase() is not { } phase || phase.ActorSeat != actorSeat ||
            phase.Kind is not (ActualDiscardRecoveryPhaseKind.Preparation or ActualDiscardRecoveryPhaseKind.Play or ActualDiscardRecoveryPhaseKind.Ending) ||
            _resolutionStack.LastOrDefault() is not { } parent || parent.Id != parentFrameId) return;
        var issues = ProgramEventHistory<KemingEchoArmedEvent>().Where(e => e.TargetSeat == actorSeat && invocationId > e.InvocationCutoff &&
            !ProgramEventHistory<KemingEchoReservedEvent>().Any(r => r.IssueFrameId == e.FrameId)).OrderBy(e => RelativeTableOrder(_currentSeat, e.Source.OwnerSeat)).ThenBy(e => e.FrameId).ToArray();
        if (issues.Length == 0) return;
        var capture = new KemingSourceCapture(invocationId, parentFrameId, actorSeat, skillKey, occurrenceKey, skillInstanceId, phase)
        { Entitlements = issues.Select(e => new KemingEchoEntitlement(e.FrameId, e.Source, e.GameplayHash)).ToArray() };
        if (parent is ProgramCardTriggerWindowFrame cardWindow && cardWindow.Action.Type == CardActionType.Use && cardWindow.Action.ActorSeat == actorSeat)
            capture = capture with { Names = [new(ProgramBasicCardName(cardWindow.Action.EffectiveKind), false, cardWindow.Action.ActionId, 0)] };
        ReplaceRuntimeTop(parent with { KemingCapture = capture });
        foreach (var issue in issues) AdvanceEventRulesAndQueueFact(new KemingEchoReservedEvent(issue.FrameId, invocationId, parentFrameId, actorSeat, skillKey, occurrenceKey));
        foreach (var name in capture.Names) AdvanceEventRulesAndQueueFact(new KemingSourceCardUsedEvent(invocationId, parentFrameId, name.ProducerId));
    }
    private void ObserveKemingProducedFacts(IGameEvent payload)
    {
        if (payload is NativeDrawInvocationRecordedEvent draw)
        {
            var index = _resolutionStack.FindIndex(f => f.Id == draw.ParentFrameId);
            if (index >= 0 && _resolutionStack[index] is { KemingCapture: { Closed: false } capture } parent && capture.ActorSeat == draw.OwnerSeat)
            {
                var names = _cardMovements.Where(m => m.Sequence > draw.SequenceBefore && m.Sequence <= draw.SequenceAfter && m.To == CardLocation.Hand(draw.OwnerSeat) &&
                        m.From == CardLocation.DrawPile && m.Reason == draw.Reason).OrderBy(m => m.Sequence)
                    .Select(m => new KemingProducedName(ProgramBasicCardName(m.CardKind), true, draw.InvocationId, m.Sequence));
                ReplaceRuntimeFrame(parent.Id, parent with { KemingCapture = capture with { Names = capture.Names.Concat(names).Distinct().Take(5).ToArray() } });
            }
        }
        CardActionContext? action = payload switch
        {
            CardUseDeclaredEvent declared => LifecycleCardUse(declared.ResolutionId)?.Action,
            CardActionAcceptedEvent accepted when accepted.Action.Type == CardActionType.Response &&
                IsCompletedResponseUse(accepted.Action, accepted.Action.EffectiveKind == CardKind.Dodge ? ProgramCardContinuation.Dodge : ProgramCardContinuation.NullificationResponse) => accepted.Action,
            _ => null
        };
        if (action is null) return;
        foreach (var parent in _resolutionStack.Where(f => f.KemingCapture is { Closed: false } c && c.ActorSeat == action.ActorSeat).ToArray())
        {
            var capture = parent.KemingCapture!;
            if (capture.Names.Any(n => !n.IsDraw && n.ProducerId == action.ActionId) || parent is ProgramCardTriggerWindowFrame) continue;
            if (parent is ProgramSkillFrame)
            {
                var sourceIndex = _resolutionStack.FindIndex(f => f.Id == parent.Id);
                var useIndex = _resolutionStack.FindIndex(f => f is CardUseFrame use && use.Action?.ActionId == action.ActionId);
                if (action.Type != CardActionType.Use || useIndex <= sourceIndex || _resolutionStack.Skip(sourceIndex + 1).Take(useIndex - sourceIndex - 1).Any(f => f is ProgramSkillFrame or SkillInvocationFrame)) continue;
            }
            else if (parent is CardUseFrame use)
            {
                var native = ProgramEventHistory<NativeEquipmentInvocationIssuedEvent>().LastOrDefault(e => e.ParentFrameId == parent.Id && e.OccurrenceKey == capture.OccurrenceKey);
                if (native is null || action.Type == CardActionType.Use && native.Kind != NativeEquipmentInvocationKind.Qinglong ||
                    action.Type == CardActionType.Response && (native.Kind is not (NativeEquipmentInvocationKind.Bagua or NativeEquipmentInvocationKind.FactionBagua) || action.EffectiveKind != CardKind.Dodge || action.ParentActionId != use.Action?.ActionId)) continue;
            }
            else continue;
            ReplaceRuntimeFrame(parent.Id, parent with { KemingCapture = capture with { Names = capture.Names.Append(new(ProgramBasicCardName(action.EffectiveKind), false, action.ActionId, 0)).Take(5).ToArray() } });
            AdvanceEventRulesAndQueueFact(new KemingSourceCardUsedEvent(capture.InvocationId, parent.Id, action.ActionId));
        }
    }
    private bool HasKemingCompletedCardEffect(CardActionContext action, SkillProgramTriggerWindow window) => HasShenCaoPiCapability &&
        window is SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardResponseAccepted && _resolutionStack.Any(f =>
            f.KemingCapture is { Closed: false } c && c.Names.Any(n => !n.IsDraw && n.ProducerId == action.ActionId));
    private bool TryCompleteKemingProgram(ProgramSkillFrame f, bool completed) => f.KemingCapture is { Closed: false } capture &&
        BeginKemingEcho(f, capture, new(KemingEchoReturnKind.ProgramCompletion, completed));
    private bool TryCompleteKemingConverted(ProgramCardTriggerWindowFrame supplied)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramCardTriggerWindowFrame;
        if (f is null || f.Id != supplied.Id) return false;
        if (f.KemingCapture is { Closed: false } own) return BeginKemingEcho(f, own, new(KemingEchoReturnKind.CardWindow));
        var source = _resolutionStack.LastOrDefault(parent => parent.KemingCapture is { Closed: false } capture && capture.Names.Any(n => !n.IsDraw && n.ProducerId == f.Action.ActionId));
        return source?.KemingCapture is { } native && source is not ProgramSkillFrame && BeginKemingEcho(f, native, new(KemingEchoReturnKind.CardWindow));
    }
    private bool TryCompleteKemingCixiong(CixiongDoubleSwordsHandle pending, bool activated, bool targetDiscarded, int? discardedCardId, int sourceDrawCount)
    {
        if (LifecycleCardUse(pending.Attack.ResolutionId) is not { KemingCapture: { Closed: false } capture } source || capture.SkillKey != CardCatalog.Get(CardKind.CixiongDoubleSwords).DisplayName) return false;
        return BeginKemingEcho(source, capture, new(KemingEchoReturnKind.CixiongCompletion, Activated: activated, TargetDiscarded: targetDiscarded, DiscardedCardId: discardedCardId, SourceDrawCount: sourceDrawCount));
    }
    private bool BeginKemingEcho(ResolutionFrame parent, KemingSourceCapture capture, KemingEchoReturn returned)
    {
        var source = _resolutionStack.Single(f => f.Id == capture.ParentFrameId);
        ReplaceRuntimeFrame(source.Id, source with { KemingCapture = capture with { Closed = true } });
        if (capture.Names.Count == 0 || !capture.Entitlements.Any(e => _players[e.Source.OwnerSeat].IsAlive) || _winner != Winner.None) return false;
        if (_resolutionStack.LastOrDefault()?.Id != parent.Id) throw new InvalidOperationException("Keming's effect completion must retain the actual live source host.");
        if (returned.Kind == KemingEchoReturnKind.CixiongCompletion) ClearPendingDecision();
        else if (_pendingDecision is not null) throw new InvalidOperationException("Keming cannot overwrite another accepted source decision.");
        var frame = new KemingSkillEchoFrame(++_resolutionSequence, parent.Id, capture, returned);
        PushRuntimeFrame(frame); AdvanceEventRulesAndQueueFact(new KemingEchoStartedEvent(frame.Id, parent.Id, capture.InvocationId, capture.Names.Count));
        AdvanceRuntimeFrame(frame.Id); return true;
    }
    private IReadOnlyList<PromptChoice> KemingEchoChoices(KemingSkillEchoFrame f) => Array.AsReadOnly(new[] { true, false }.Select(accept => new PromptChoice(
        new($"keming-echo:{f.Id}:{f.Cursor}:{accept}"), accept ? $"摸至多{f.Capture.Names.Count}张同名牌" : "不发动刻名", [], [],
        new Dictionary<string, string> { ["program-action"] = "keming-echo", ["accept"] = accept ? "yes" : "no" })).ToArray());
    private void PublishKemingEcho(KemingSkillEchoFrame f)
    {
        var owner = f.Capture.Entitlements[f.Cursor].Source.OwnerSeat; var skill = _contentRegistry.GetSkill(ShenCaoPiComposition.Keming);
        _pendingDecision = new(DecisionKind.ProgramTrigger, owner, "刻名：是否摸等量同名牌？", [], [], owner)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = KemingEchoChoices(f), SkillPrompt = new(skill.Id, skill.Name, skill.Name, skill.Description) };
        _status = _players[owner].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("keming-echo")] private void AnswerKemingEcho(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not KemingSkillEchoFrame { Stage: KemingSkillEchoStage.Choosing } f ||
            !KemingEchoChoices(f).Any(c => c.Id == choice.Id && c.Cards.SequenceEqual(choice.Cards) && c.Targets.SequenceEqual(choice.Targets) && c.Parameters.OrderBy(x => x.Key).SequenceEqual(choice.Parameters.OrderBy(x => x.Key))))
            throw new InvalidOperationException("Keming rejects a stale original source-effect return.");
        AssertKemingEcho(f); var owner = f.Capture.Entitlements[f.Cursor].Source.OwnerSeat; var accept = choice.Parameters["accept"] == "yes"; ClearPendingDecision();
        AdvanceEventRulesAndQueueFact(new KemingEchoChoiceEvent(f.Id, f.Cursor, owner, accept));
        if (!accept) ReplaceRuntimeTop(f with { Cursor = f.Cursor + 1 });
        else ReplaceRuntimeTop(f with { Stage = KemingSkillEchoStage.Drawing,
            NativeDraw = new(f.Cursor, f.Capture.Names.Count, null, SkillProgramCardSetVisibility.Private, new("skill-program.anniversary:keming.same-name-draw"))
            { ReturnKind = NativeDrawAttemptReturnKind.KemingSkillEcho, Recipients = [owner] } });
        AdvanceRuntimeFrame(f.Id);
    }
    [NativeFrameAutomaticStep(NativeFrameAutomaticPhase.AfterOpening, 39700, NativeFrameAutomaticPending.RequireNoneBeforeFrame, NativeFrameAutomaticPublish.AfterAdvance)]
    [NativeFrameRoute(typeof(KemingSkillEchoFrame), 39700)] private void ContinueKemingSkillEcho(KemingSkillEchoFrame supplied)
    {
        var id = supplied.Id;
        while (_resolutionStack.LastOrDefault() is KemingSkillEchoFrame f && f.Id == id && _pendingDecision is null)
        {
            AssertKemingEcho(f);
            if (f.Stage == KemingSkillEchoStage.Drawing)
            {
                var paid = f.NativeDraw ?? throw new InvalidOperationException("Keming lost its frozen same-name draw request.");
                if (paid.ActiveAttemptId is not null && !paid.AttemptReturned) throw new InvalidOperationException("Keming must await its exact native draw request.");
                if (!paid.AttemptReturned && TryBeginNativeDrawAttempt(f)) return;
                f = (KemingSkillEchoFrame)_resolutionStack[^1]; paid = f.NativeDraw!; var owner = paid.Recipients.Single(); var before = _movementSequence;
                var ids = new List<int>(); var draft = CaptureNativeDrawInvocation(owner, paid.RequestedCount, paid.Reason, f.Capture.Entitlements[f.Cursor].Source);
                if (paid.Replacement is null && _players[owner].IsAlive && _winner == Winner.None)
                foreach (var name in f.Capture.Names)
                {
                    if (!EnsureDrawPile()) break;
                    var card = _cardZones.CardsAt(CardLocation.DrawPile).Reverse().FirstOrDefault(c => ProgramBasicCardName(c.Kind) == name.Name);
                    if (card is null) continue;
                    MoveCard(card, CardLocation.DrawPile, CardLocation.Hand(owner), paid.Reason); ids.Add(card.Id);
                }
                CompleteNativeDrawInvocation(draft, ids);
                var outcome = new NativeDrawOutcome(owner, paid.RequestedCount, paid.ActiveAttemptId, before, _movementSequence, paid.Replacement) { ActualDrawIds = ids };
                ReplaceRuntimeTop(f with { Stage = KemingSkillEchoStage.Children, NativeDraw = null, Outcome = outcome });
                AdvanceEventRulesAndQueueFact(new KemingEchoDrawPaidEvent(f.Id, f.Cursor, owner, paid.RequestedCount, ids.Count, before, _movementSequence, paid.Replacement is not null));
                continue;
            }
            if (f.Stage == KemingSkillEchoStage.Children)
            {
                if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.KemingEcho) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.KemingEcho) ||
                    TryBeginHpChangedProgramWindow(id, PostEventContinuation.KemingEcho) || TryBeginCardsMovedProgramWindow(id)) return;
                f = (KemingSkillEchoFrame)_resolutionStack[^1]; ReplaceRuntimeTop(f with { Stage = KemingSkillEchoStage.Choosing, Cursor = f.Cursor + 1, Outcome = null }); continue;
            }
            if (f.Cursor < f.Capture.Entitlements.Count && _winner == Winner.None)
            {
                if (!_players[f.Capture.Entitlements[f.Cursor].Source.OwnerSeat].IsAlive) { ReplaceRuntimeTop(f with { Cursor = f.Cursor + 1 }); continue; }
                PublishKemingEcho(f); return;
            }
            AdvanceEventRulesAndQueueFact(new KemingEchoFinishedEvent(f.Id, f.ParentFrameId, f.Capture.InvocationId));
            PopResolutionFrame(f.Id, ResolutionFrameKind.KemingSkillEcho);
            if (_resolutionStack.LastOrDefault() is not { } parent || parent.Id != f.ParentFrameId) throw new InvalidOperationException("Keming lost its typed completed-source parent.");
            if (f.Return.Kind == KemingEchoReturnKind.ProgramCompletion) FinishProgramSkill((ProgramSkillFrame)parent, f.Return.SourceCompleted);
            else if (f.Return.Kind == KemingEchoReturnKind.CardWindow) AdvanceRuntimeFrame(parent.Id);
            else
            {
                var pending = ActiveCixiongDoubleSwords ?? throw new InvalidOperationException("Keming lost its completed Cixiong source.");
                CompleteCixiongDoubleSwords(pending, f.Return.Activated, f.Return.TargetDiscarded, f.Return.DiscardedCardId, f.Return.SourceDrawCount);
            }
            return;
        }
    }
    private bool IsKemingEchoMovement(KemingSkillEchoFrame f, CardMovementBatchContext b) => f.Stage == KemingSkillEchoStage.Children && f.Outcome is { } o &&
        b.ParentFrameId == f.Id && b.AwaitingProgramFrameId is null && b.Movements.All(m => m.Sequence > o.SequenceBefore && m.Sequence <= o.SequenceAfter) &&
        b.Movements.Any(m => m.To == CardLocation.Hand(o.RecipientSeat));
    private void ReturnKemingEchoMovement(CardsMovedTriggerWindowFrame window)
    {
        if (_resolutionStack.LastOrDefault() is not KemingSkillEchoFrame f || window.ResumeKemingEchoFrameId != f.Id || !IsKemingEchoMovement(f, window.Batch))
            throw new InvalidOperationException("Keming's draw observer lost its exact native producer return.");
        AdvanceRuntimeFrame(f.Id);
    }
    private static bool SameKemingCapture(KemingSourceCapture a, KemingSourceCapture b) => a.InvocationId == b.InvocationId && a.ParentFrameId == b.ParentFrameId && a.ActorSeat == b.ActorSeat &&
        a.SkillKey == b.SkillKey && a.OccurrenceKey == b.OccurrenceKey && a.SkillInstanceId == b.SkillInstanceId && a.Phase == b.Phase && a.Entitlements.SequenceEqual(b.Entitlements) && a.Names.SequenceEqual(b.Names);
    private void AssertKemingEcho(KemingSkillEchoFrame f)
    {
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (index < 1 || _resolutionStack[index - 1].Id != f.ParentFrameId || _resolutionStack.SingleOrDefault(x => x.Id == f.Capture.ParentFrameId)?.KemingCapture is not { Closed: true } source || !SameKemingCapture(source, f.Capture) ||
            f.Capture.Names is not System.Collections.IList { IsReadOnly: true } || f.Capture.Entitlements is not System.Collections.IList { IsReadOnly: true } ||
            f.Capture.Names.Count is < 1 or > 5 || f.Cursor < 0 || f.Cursor > f.Capture.Entitlements.Count || !Enum.IsDefined(f.Stage) || !Enum.IsDefined(f.Return.Kind) ||
            f.Capture.Entitlements.Any(e => ProgramEventHistory<KemingEchoReservedEvent>().Count(r => r.IssueFrameId == e.IssueFrameId && r.InvocationId == f.Capture.InvocationId && r.ParentFrameId == f.Capture.ParentFrameId && r.ActorSeat == f.Capture.ActorSeat && r.SkillKey == f.Capture.SkillKey && r.OccurrenceKey == f.Capture.OccurrenceKey) != 1) ||
            ProgramEventHistory<KemingEchoStartedEvent>().Count(e => e.FrameId == f.Id && e.ParentFrameId == f.ParentFrameId && e.InvocationId == f.Capture.InvocationId && e.Count == f.Capture.Names.Count) != 1)
            throw new InvalidOperationException("Keming's private completed-effect receipt lost its exact invocation, source, phase or entitlement.");
        AssertKemingSourceReceipt(f.Capture);
        var parent = _resolutionStack[index - 1];
        if (!KemingSkillEchoFrameRidesOn(f, parent) || f.Stage == KemingSkillEchoStage.Finished ||
            f.Stage is KemingSkillEchoStage.Drawing or KemingSkillEchoStage.Children && f.Cursor >= f.Capture.Entitlements.Count)
            throw new InvalidOperationException("Keming's completed source changed its typed native return or payment stage.");
        if (f.Stage == KemingSkillEchoStage.Drawing)
        {
            var owner = f.Capture.Entitlements[f.Cursor].Source.OwnerSeat;
            if (f.Outcome is not null || f.NativeDraw is not { } draw || draw.ReturnKind != NativeDrawAttemptReturnKind.KemingSkillEcho ||
                draw.InstructionIndex != f.Cursor || draw.RequestedCount != f.Capture.Names.Count || !draw.Recipients.SequenceEqual([owner]) || draw.RecipientIndex != 0 ||
                draw.Reason.Value != "skill-program.anniversary:keming.same-name-draw" ||
                ProgramEventHistory<KemingEchoChoiceEvent>().Count(e => e.FrameId == f.Id && e.Cursor == f.Cursor && e.OwnerSeat == owner && e.Accepted) != 1 ||
                ProgramEventHistory<KemingEchoDrawPaidEvent>().Any(e => e.FrameId == f.Id && e.Cursor == f.Cursor))
                throw new InvalidOperationException("Keming's accepted matching-name draw changed its exact unpaid native request.");
        }
        else if (f.NativeDraw is not null) throw new InvalidOperationException("Keming retained an unrelated native draw producer.");
        if (f.Stage == KemingSkillEchoStage.Children)
        {
            if (f.Outcome is not { } o || o.RecipientSeat != f.Capture.Entitlements[f.Cursor].Source.OwnerSeat || o.RequestedCount != f.Capture.Names.Count ||
                o.ActualDrawIds is not System.Collections.IList { IsReadOnly: true } || o.ActualDrawIds.Distinct().Count() != o.ActualDrawIds.Count ||
                ProgramEventHistory<KemingEchoDrawPaidEvent>().Count(e => e.FrameId == f.Id && e.Cursor == f.Cursor && e.OwnerSeat == o.RecipientSeat &&
                    e.Requested == o.RequestedCount && e.Actual == o.ActualDrawIds.Count && e.Before == o.SequenceBefore && e.After == o.SequenceAfter && e.Replaced == (o.Replacement is not null)) != 1 ||
                !_cardMovements.Where(m => m.Sequence > o.SequenceBefore && m.Sequence <= o.SequenceAfter && m.From == CardLocation.DrawPile &&
                    m.To == CardLocation.Hand(o.RecipientSeat) && m.Reason.Value == "skill-program.anniversary:keming.same-name-draw").OrderBy(m => m.Sequence).Select(m => m.CardId).SequenceEqual(o.ActualDrawIds))
                throw new InvalidOperationException("Keming's paid return changed its actual matching deck entities or original native movement range.");
        }
        else if (f.Outcome is not null) throw new InvalidOperationException("Keming retained a draw outcome outside its exact children stage.");
    }
    private void AssertKemingSourceReceipt(KemingSourceCapture c)
    {
        if (!IsRecordedActualDiscardRecoveryPhase(c.Phase) || c.Phase.ActorSeat != c.ActorSeat ||
            c.Phase.Kind is not (ActualDiscardRecoveryPhaseKind.Preparation or ActualDiscardRecoveryPhaseKind.Play or ActualDiscardRecoveryPhaseKind.Ending) ||
            ProgramEventHistory<SkillInvocationStartedEvent>().Count(e => e.FrameId == c.InvocationId && e.ParentFrameId == c.ParentFrameId && e.ActorSeat == c.ActorSeat &&
                e.SkillKey == c.SkillKey && e.OccurrenceKey == c.OccurrenceKey && e.SkillInstanceId == c.SkillInstanceId && e.TurnNumber == c.Phase.TurnNumber && e.TurnOwnerSeat == c.Phase.ActualTurnOwnerSeat) != 1 ||
            ProgramEventHistory<SkillInvocationCompletedEvent>().Count(e => e.FrameId == c.InvocationId && e.ParentFrameId == c.ParentFrameId && e.OccurrenceKey == c.OccurrenceKey) != 1 ||
            c.Entitlements.Distinct().Count() != c.Entitlements.Count || c.Names.Distinct().Count() != c.Names.Count ||
            c.Entitlements.Any(ent => ProgramEventHistory<KemingEchoArmedEvent>().Count(e => e.FrameId == ent.IssueFrameId && e.Source == ent.Source && e.GameplayHash == ent.GameplayHash &&
                e.TargetSeat == c.ActorSeat && e.InvocationCutoff < c.InvocationId) != 1))
            throw new InvalidOperationException("Keming's receipt is not the recorded actual next completed skill invocation.");
        foreach (var name in c.Names)
        {
            if (name.IsDraw)
            {
                var draws = ProgramEventHistory<NativeDrawInvocationRecordedEvent>().Where(e => e.InvocationId == name.ProducerId && e.ParentFrameId == c.ParentFrameId &&
                    e.OwnerSeat == c.ActorSeat && name.Sequence > e.SequenceBefore && name.Sequence <= e.SequenceAfter).ToArray();
                if (draws.Length != 1 || !_cardMovements.Any(m => m.Sequence == name.Sequence && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(c.ActorSeat) &&
                    m.Reason == draws[0].Reason && ProgramBasicCardName(m.CardKind) == name.Name))
                    throw new InvalidOperationException("Keming's private drawn name must match its original native draw invoice and exact entity movement.");
            }
            else if (name.Sequence != 0 || ProgramEventHistory<KemingSourceCardUsedEvent>().Count(e => e.InvocationId == c.InvocationId && e.SourceFrameId == c.ParentFrameId && e.ActionId == name.ProducerId) != 1 ||
                !ProgramEventHistory<CardActionAcceptedEvent>().Any(e => e.Action.ActionId == name.ProducerId && e.Action.ActorSeat == c.ActorSeat && ProgramBasicCardName(e.Action.EffectiveKind) == name.Name &&
                    (e.Action.Type == CardActionType.Use || IsCompletedResponseUse(e.Action, e.Action.EffectiveKind == CardKind.Dodge ? ProgramCardContinuation.Dodge : ProgramCardContinuation.NullificationResponse))))
                throw new InvalidOperationException("Keming's public used name must match the exact accepted native action observed by this source invocation.");
        }
    }
    private void AssertKemingEchoFrames() { foreach (var f in _resolutionStack.OfType<KemingSkillEchoFrame>()) AssertKemingEcho(f); }
    private bool HasKemingEchoPrompt() => _resolutionStack.LastOrDefault() is KemingSkillEchoFrame { Stage: KemingSkillEchoStage.Choosing } f && _pendingDecision is { Kind: DecisionKind.ProgramTrigger } p &&
        f.Cursor < f.Capture.Entitlements.Count && p.PlayerSeat == f.Capture.Entitlements[f.Cursor].Source.OwnerSeat && p.Choices.Select(c => c.Id).SequenceEqual(KemingEchoChoices(f).Select(c => c.Id));
    private bool KemingSkillEchoFrameRidesOn(ResolutionFrame child, ResolutionFrame parent) => child switch
    {
        KemingSkillEchoFrame f => f.ParentFrameId == parent.Id && KemingEchoTypedParentMatches(f, parent),
        CardsMovedTriggerWindowFrame moved when parent is KemingSkillEchoFrame f => moved.ResumeKemingEchoFrameId == f.Id && IsKemingEchoMovement(f, moved.Batch),
        HpChangedTriggerWindowFrame hp when parent is KemingSkillEchoFrame f => hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.KemingEcho && hp.Change.ParentFrameId == f.Id,
        ProgramLifecycleTriggerWindowFrame state when parent is KemingSkillEchoFrame f => state.ResumeProgramFrameId == f.Id && state.CharacterStateContinuation == CharacterStateContinuation.KemingEcho,
        RecoveryReplacementFrame recovery when parent is KemingSkillEchoFrame f => f.Stage == KemingSkillEchoStage.Children && recovery.ParentFrameId == f.Id &&
            recovery.Return.Continuation == PostEventContinuation.KemingEcho && recovery.Return.ResumeFrameId == f.Id,
        _ => NativeDrawAttemptRidesOn(child, parent)
    };
    private bool KemingEchoTypedParentMatches(KemingSkillEchoFrame f, ResolutionFrame parent) => f.Return.Kind switch
    {
        KemingEchoReturnKind.ProgramCompletion => parent is ProgramSkillFrame p && f.Capture.ParentFrameId == p.Id && f.Capture.SkillKey == p.SkillId &&
            f.Capture.SkillInstanceId == p.SkillInstanceId && f.Capture.OccurrenceKey == "program:" + GetProgramBindingId(p) &&
            !f.Return.Activated && !f.Return.TargetDiscarded && f.Return.DiscardedCardId is null && f.Return.SourceDrawCount == 0,
        KemingEchoReturnKind.CardWindow => parent is ProgramCardTriggerWindowFrame w && f.Capture.ActorSeat == w.Action.ActorSeat &&
            f.Capture.Names.Any(n => !n.IsDraw && n.ProducerId == w.Action.ActionId) && !f.Return.Activated && !f.Return.TargetDiscarded &&
            f.Return.DiscardedCardId is null && f.Return.SourceDrawCount == 0,
        KemingEchoReturnKind.CixiongCompletion => parent is CardUseFrame use && f.Capture.ParentFrameId == use.Id &&
            f.Capture.SkillKey == CardCatalog.Get(CardKind.CixiongDoubleSwords).DisplayName && f.Return.Activated && !f.Return.TargetDiscarded &&
            f.Return.DiscardedCardId is null && f.Return.SourceDrawCount == 1 && ActiveCixiongDoubleSwords is { Stage: CixiongDoubleSwordsStage.TargetChoice } pending &&
            pending.Attack.ResolutionId == use.Id && ProgramEventHistory<NativeEquipmentInvocationIssuedEvent>().Any(e => e.ParentFrameId == use.Id &&
                e.Kind == NativeEquipmentInvocationKind.Cixiong && e.OccurrenceKey == f.Capture.OccurrenceKey),
        _ => false
    };
    private bool HasKemingCixiongContinuation(CixiongDoubleSwordsHandle pending)
    {
        if (!HasShenCaoPiCapability || pending.Stage != CixiongDoubleSwordsStage.TargetChoice || !SameAttackOwner(ActiveCardAttack, pending.Attack)) return false;
        var index = _resolutionStack.FindIndex(f => f.Id == pending.Attack.ResolutionId);
        if (index < 0 || index + 1 >= _resolutionStack.Count || _resolutionStack[index] is not CardUseFrame { Step: ResolutionFrameStep.Declared, KemingCapture: { Closed: true } source } root ||
            root.CardAttack is not { Active: true, CixiongDoubleSwordsResolved: false } attack || root.SourceSeat != pending.Attack.SourceSeat || attack.TargetSeat != pending.Attack.TargetSeat ||
            _resolutionStack[index + 1] is not KemingSkillEchoFrame { Return.Kind: KemingEchoReturnKind.CixiongCompletion } echo || !SameKemingCapture(source, echo.Capture) ||
            !KemingSkillEchoFrameRidesOn(echo, root) || echo.Capture.Names is not [{ IsDraw: true } paid] ||
            !ProgramEventHistory<NativeDrawInvocationRecordedEvent>().Any(e => e.InvocationId == paid.ProducerId && e.ParentFrameId == root.Id && e.OwnerSeat == attack.SourceSeat &&
                e.Requested == 1 && e.ActualCount == 1 && e.Reason == CardMoveReasons.CixiongDraw && paid.Sequence > e.SequenceBefore && paid.Sequence <= e.SequenceAfter) ||
            ProgramEventHistory<NativeEquipmentInvocationReturnedEvent>().Count(e => e.InvocationId == source.InvocationId && e.ParentFrameId == root.Id &&
                e.AttackId == root.Id && e.Kind == NativeEquipmentInvocationKind.Cixiong && e.OccurrenceKey == source.OccurrenceKey && !e.Canceled) != 1 ||
            ProgramEventHistory<CixiongDoubleSwordsResolvedEvent>().Any(e => e.ResolutionId == root.Id) ||
            !HasObserverSuffix(index, static (engine, child) => engine.KemingCixiongObserverEdge(child),
                static (engine, child, dying) => engine.IsSuspendedOrPaidRescueObserverSuffix(child, dying))) return false;
        AssertKemingEcho(echo); return true;
    }
    private bool KemingCixiongObserverEdge(int index)
    {
        var parent = _resolutionStack[index - 1]; var child = _resolutionStack[index];
        if (parent is KemingSkillEchoFrame || child is KemingSkillEchoFrame) return KemingSkillEchoFrameRidesOn(child, parent);
        return NativeEquipmentObserverEdge(index);
    }
}
