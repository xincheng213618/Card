using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string NamedAmbushRecordReason = "skill-program.named-ambush.record";
    private const string NamedAmbushRemoveReason = "skill-program.named-ambush.remove";
    private bool HasNamedAmbushCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.NullifyMatchingHandUse);
    private static CardKind AmbushCardName(CardKind kind) => kind is CardKind.FireSlash or CardKind.ThunderSlash ? CardKind.Slash : kind;
    private static CardConversionSource AmbushSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static CardLocation AmbushLocation(CardConversionSource source) => new(CardZoneKind.PrivatePersistentPile, source.OwnerSeat,
        $"named-ambush:{source.SkillId}:{source.SkillInstanceId}");
    private NamedAmbushDeposit[] LiveAmbushDeposits(int owner, string? skill = null, string? instance = null)
    {
        if (!HasNamedAmbushCapability || !_cardZones.HasPrivatePersistentCards(owner)) return [];
        var facts = CompleteProgramEventHistory().ToArray();
        var removed = facts.OfType<NamedAmbushRemovedEvent>().Select(e => (e.Deposit.DepositId, e.Deposit.Source, e.Deposit.CardId)).ToHashSet();
        return facts.OfType<NamedAmbushDepositedEvent>().Select(e => e.Deposit)
            .Where(d => d.Source.OwnerSeat == owner && (skill is null || d.Source.SkillId == skill) &&
                (instance is null || d.Source.SkillInstanceId == instance) && !removed.Contains((d.DepositId, d.Source, d.CardId)) &&
                _cardZones.GetLocation(d.CardId) == d.Location).ToArray();
    }
    private bool TryGetAmbushUse(ProgramSkillWindowContext context, out NamedAmbushUseIdentity identity, out CardActionContext action)
    {
        identity = null!; action = null!;
        if (context.Window != SkillProgramTriggerWindow.CardUseCommitted ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } window ||
            GetCardActionWindow(window) != SkillProgramTriggerWindow.CardUseCommitted || window.Action.PhysicalCards.Count == 0) return false;
        action = window.Action;
        var response = action.Type == CardActionType.Response;
        var actorSeat = action.ActorSeat;
        if (action.ActorSeat != action.ProviderSeat || action.PhysicalCards.Any(c => c.From != CardLocation.Hand(actorSeat)) ||
            action.PhysicalCards.Select(c => c.CardId).Distinct().Count() != action.PhysicalCards.Count ||
            CardCatalog.Get(action.EffectiveKind).CategoryName == "装备牌" ||
            response && (window.CompletedResponseReturn is not { IsCommitted: true } typed || !IsCompletedResponseUse(action, typed.Kind)) ||
            !response && (LifecycleCardUse(window.ParentFrameId) is not { Action: { } actual } use || actual.ActionId != action.ActionId ||
                actual.ActorSeat != action.ActorSeat || actual.ProviderSeat != action.ProviderSeat || use.CardKind != action.EffectiveKind)) return false;
        identity = new(window.Id, window.ParentFrameId, action.ActionId, action.ActorSeat, action.ProviderSeat, action.EffectiveKind, response);
        return true;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 260)]
    private bool CanOfferNamedAmbush(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.RecordNamedPrivateAmbush))
            return _currentSeat == candidate.OwnerSeat && GetHand(_players[candidate.OwnerSeat]).Count > 0;
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.NullifyMatchingHandUse)) return true;
        return _currentSeat != candidate.OwnerSeat && TryGetAmbushUse(context, out var identity, out _) &&
            identity.ActorSeat != candidate.OwnerSeat && LiveAmbushDeposits(candidate.OwnerSeat, candidate.SkillId, candidate.SkillInstanceId)
                .Any(d => d.RecordedKind == AmbushCardName(identity.EffectiveKind));
    }
    private SkillProgramStepOutcome BeginNamedAmbush(SkillProgramEffect effect, ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.NamedAmbush is not null || f.WindowContext is not { } context || f.TriggerId is null)
            throw new InvalidOperationException("Named ambush requires one unpaid owning trigger.");
        var removing = effect.Op == SkillProgramEffectOp.NullifyMatchingHandUse;
        NamedAmbushUseIdentity? use = null;
        CardActionContext? originalAction = null;
        if (removing)
        {
            if (_currentSeat == f.OwnerSeat || !TryGetAmbushUse(context, out var identity, out var action) || identity.ActorSeat == f.OwnerSeat)
                throw new InvalidOperationException("Named ambush lost its off-turn actual hand use.");
            use = identity;
            originalAction = action;
        }
        var receipt = new NamedAmbushReceipt { Source = AmbushSource(f), GameplayHash = f.GameplayHash, InstructionIndex = f.InstructionIndex,
            WindowFrameId = context.ParentFrameId, Removing = removing, Use = use, OriginalAction = originalAction,
            Stage = removing ? NamedAmbushStage.SelectDeposit : NamedAmbushStage.SelectCard };
        ReplaceRuntimeTop(f = f with { NamedAmbush = receipt });
        AdvanceEventRulesAndQueueFact(new NamedAmbushOfferedEvent(f.Id, receipt));
        PublishNamedAmbushChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> NamedAmbushChoices(ProgramSkillFrame f)
    {
        var r = f.NamedAmbush!;
        PromptChoice Choice(string branch, string label, IReadOnlyList<int>? cards = null, CardKind? name = null) => new(
            new($"named-ambush.{f.Id}.{r.Stage}.{branch}.{string.Join('.', cards ?? [])}.{name}"), label, cards ?? [], [],
            new Dictionary<string, string> { ["program-action"] = "named-ambush", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture),
                ["branch"] = branch, ["recorded-kind"] = name?.ToString() ?? "" });
        var choices = new List<PromptChoice>();
        if (r.Stage == NamedAmbushStage.SelectCard)
            choices.AddRange(GetHand(_players[f.OwnerSeat]).Select(c => Choice("card", $"暗置【{c.DisplayName}】", [c.Id])));
        else if (r.Stage == NamedAmbushStage.SelectName && r.SelectedCardId is { } selected && _cardZones.GetLocation(selected) == CardLocation.Hand(f.OwnerSeat))
            choices.AddRange(Enum.GetValues<CardKind>().Select(AmbushCardName).Distinct().Where(k => CardCatalog.Get(k).CategoryName != "装备牌")
                .Select(k => Choice("name", $"记录【{CardCatalog.Get(k).DisplayName}】", name: k)));
        else if (r.Stage == NamedAmbushStage.SelectDeposit && r.Use is { } use)
            choices.AddRange(LiveAmbushDeposits(f.OwnerSeat, f.SkillId, f.SkillInstanceId).Where(d => d.RecordedKind == AmbushCardName(use.EffectiveKind))
                .Select(d => Choice("remove", $"移去伏兵【{GetAttackCard(d.CardId).DisplayName}】，令【{CardCatalog.Get(use.EffectiveKind).DisplayName}】无效", [d.CardId])));
        choices.Add(Choice("decline", "取消")); return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishNamedAmbushChoice(ProgramSkillFrame f)
    {
        var choices = NamedAmbushChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f = f with { NamedAmbush = f.NamedAmbush! with { Choices = choices } });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, $"【{skill.Name}】请选择。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("named-ambush")]
    private void ResolveNamedAmbushChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Ambush lost owning frame.");
        RequireNamedAmbushReceipt(f); var r = f.NamedAmbush!;
        if (!AssistedChoicesEqual([choice], r.Choices.Where(c => c.Id == choice.Id).ToArray()) ||
            !AssistedChoicesEqual([choice], NamedAmbushChoices(f).Where(c => c.Id == choice.Id).ToArray()) ||
            r.Stage == NamedAmbushStage.MovementChildren) throw new InvalidOperationException("Ambush choice changed after publication.");
        ClearPendingDecision();
        if (choice.Parameters["branch"] == "decline") { CompleteNamedAmbush(f, false); return; }
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
        { CompleteNamedAmbush(f, false); return; }
        if (r.Stage == NamedAmbushStage.SelectCard)
        {
            ReplaceRuntimeTop(f = f with { NamedAmbush = r with { SelectedCardId = choice.Cards.Single(), Stage = NamedAmbushStage.SelectName, Choices = [] } });
            PublishNamedAmbushChoice(f); return;
        }
        NamedAmbushDeposit deposit; CardLocation from, to;
        var before = _movementSequence;
        if (r.Removing)
        {
            if (r.Use is not { } identity || !TryGetAmbushUse(f.WindowContext!, out var live, out _) || live != identity || _currentSeat == f.OwnerSeat)
                throw new InvalidOperationException("Ambush payment lost actual committed hand use.");
            deposit = LiveAmbushDeposits(f.OwnerSeat, f.SkillId, f.SkillInstanceId).Single(d => d.CardId == choice.Cards.Single() && d.RecordedKind == AmbushCardName(identity.EffectiveKind));
            from = deposit.Location; to = CardLocation.DiscardPile;
        }
        else
        {
            if (r.SelectedCardId is not { } cardId || _cardZones.GetLocation(cardId) != CardLocation.Hand(f.OwnerSeat) ||
                !Enum.TryParse<CardKind>(choice.Parameters["recorded-kind"], out var name)) throw new InvalidOperationException("Ambush deposit lost exact hand card or name.");
            from = CardLocation.Hand(f.OwnerSeat); to = AmbushLocation(r.Source); _cardZones.EnsurePrivatePersistentPile(to);
            deposit = new(f.Id, r.Source, f.GameplayHash, cardId, GetHand(_players[f.OwnerSeat]).Single(c => c.Id == cardId).Kind,
                name, to, before, before);
        }
        ReplaceRuntimeTop(f = f with { NamedAmbush = r with { Deposit = deposit, MovementBefore = before, Stage = NamedAmbushStage.MovementChildren, Choices = [] },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveCard(GetAttackCard(deposit.CardId), from, to, new(r.Removing ? NamedAmbushRemoveReason : NamedAmbushRecordReason));
        var after = _movementSequence; if (!r.Removing) deposit = deposit with { SequenceAfter = after };
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { NamedAmbush = f.NamedAmbush! with { Deposit = deposit, MovementAfter = after } });
        if (r.Removing) AdvanceEventRulesAndQueueFact(new NamedAmbushRemovedEvent(f.Id, deposit, r.Use, before, after, "used"));
        else AdvanceEventRulesAndQueueFact(new NamedAmbushDepositedEvent(deposit));
        if (!DrainNamedHandChoiceChildren(f)) ReturnNamedAmbushMovement(f);
    }
    private bool DrainNamedHandChoiceChildren(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramContinuation(ProgramContinuationKind.Movement, 20700)]
    private bool ReturnNamedAmbushMovement(ProgramSkillFrame f)
    {
        if (f.NamedAmbush is null) return false;
        RequireNamedAmbushReceipt(f);
        if (f.NamedAmbush.Stage != NamedAmbushStage.MovementChildren || f.PendingMovementContinuation is null)
            throw new InvalidOperationException("Ambush movement lost typed return.");
        if (DrainNamedHandChoiceChildren(f)) return true;
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (f.NamedAmbush.Removing && _winner == Winner.None) InvalidateNamedAmbushUse(f);
        CompleteNamedAmbush(GetActiveProgramFrame(f.Id), true); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 20700)]
    private bool ResumeNamedAmbush(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { NamedAmbush: not null } f || f.Id != id) return false;
        RequireNamedAmbushReceipt(f);
        if (f.PendingMovementContinuation is not null) ReturnNamedAmbushMovement(f);
        return true;
    }
    private void CompleteNamedAmbush(ProgramSkillFrame f, bool completed)
    { ReplaceRuntimeTop(f = f with { NamedAmbush = null, PendingMovementContinuation = null }); FinishProgramSkill(f, completed); }
    private void RequireNamedAmbushReceipt(ProgramSkillFrame f)
    {
        if (f.NamedAmbush is not { } r || f.TriggerId is null || r.Source != AmbushSource(f) || r.GameplayHash != f.GameplayHash ||
            r.InstructionIndex != f.InstructionIndex || f.WindowContext?.ParentFrameId != r.WindowFrameId ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            GetPausedPrivateOfferEffect(f)?.Op != (r.Removing ? SkillProgramEffectOp.NullifyMatchingHandUse : SkillProgramEffectOp.RecordNamedPrivateAmbush) ||
            CompleteProgramEventHistory().OfType<NamedAmbushOfferedEvent>().Count(e => e.ProgramFrameId == f.Id && e.Receipt.Source == r.Source &&
                e.Receipt.InstructionIndex == r.InstructionIndex && e.Receipt.WindowFrameId == r.WindowFrameId && e.Receipt.Use == r.Use &&
                (r.OriginalAction is null ? e.Receipt.OriginalAction is null : e.Receipt.OriginalAction is { } issued && SameOverflowAction(issued, r.OriginalAction))) != 1 ||
            r.Removing && (r.Use is null || r.OriginalAction is not { } original ||
                !TryGetAmbushUse(f.WindowContext!, out var use, out var actual) || use != r.Use || !SameOverflowAction(original, actual)))
            throw new InvalidOperationException("Ambush lost exact instruction, source, window or current hand-use identity.");
        if (r.Stage != NamedAmbushStage.MovementChildren) return;
        if (r.Deposit is not { } d || r.MovementAfter <= r.MovementBefore ||
            _cardMovements.Count(m => m.CardId == d.CardId && m.Sequence > r.MovementBefore && m.Sequence <= r.MovementAfter &&
                m.From == (r.Removing ? d.Location : CardLocation.Hand(f.OwnerSeat)) && m.To == (r.Removing ? CardLocation.DiscardPile : d.Location) &&
                m.Reason.Value == (r.Removing ? NamedAmbushRemoveReason : NamedAmbushRecordReason)) != 1)
            throw new InvalidOperationException("Ambush lost its one real paid movement.");
    }
    private bool CanContinueNamedAmbush(ProgramSkillFrame f)
    {
        if (f.NamedAmbush is not { Stage: NamedAmbushStage.MovementChildren }) return false;
        RequireNamedAmbushReceipt(f); return true;
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 70)]
    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 80)]
    private void CleanupLostNamedAmbushPiles()
    {
        if (!HasNamedAmbushCapability || !_cardZones.HasPrivatePersistentCards()) return;
        foreach (var d in _players.SelectMany(p => LiveAmbushDeposits(p.Seat)).Where(d => !_players[d.Source.OwnerSeat].IsAlive ||
            !_players[d.Source.OwnerSeat].SkillGrants.Grants.Any(g => g.SkillId == d.Source.SkillId && g.SkillInstanceId == d.Source.SkillInstanceId)).ToArray())
        {
            // Moving an earlier entity can recursively settle another deposit.
            // Its consumed identity and actual zone remain authoritative.
            if (_cardZones.GetLocation(d.CardId) != d.Location || !LiveAmbushDeposits(d.Source.OwnerSeat, d.Source.SkillId, d.Source.SkillInstanceId).Any(live => live.DepositId == d.DepositId)) continue;
            var before = _movementSequence; MoveCard(GetAttackCard(d.CardId), d.Location, CardLocation.DiscardPile, new("skill-program.named-ambush.source-lost"));
            AdvanceEventRulesAndQueueFact(new NamedAmbushRemovedEvent(0, d, null, before, _movementSequence, "source-lost"));
        }
    }
    private IReadOnlyList<NamedAmbushPileSnapshot>? ProjectNamedAmbushPiles(int owner, int viewer, bool revealAll)
    {
        var deposits = LiveAmbushDeposits(owner); if (deposits.Length == 0) return null;
        return Array.AsReadOnly(deposits.GroupBy(d => (d.Source.SkillId, d.Source.SkillInstanceId)).Select(g => new NamedAmbushPileSnapshot(g.Key.SkillId,
            "伏兵", g.Count(), revealAll || owner == viewer ? Array.AsReadOnly(g.Select(d => new NamedAmbushPrivateEntrySnapshot(ToSnapshot(GetAttackCard(d.CardId)), d.RecordedKind)).ToArray()) : null)).ToArray());
    }
    [ProgramAiChoice(SkillProgramEffectOp.RecordNamedPrivateAmbush, SkillProgramEffectOp.NullifyMatchingHandUse)]
    private PromptChoice SelectAiNamedAmbush(PendingDecision decision, ProgramSkillFrame f)
    {
        if (f.NamedAmbush!.Stage == NamedAmbushStage.SelectName)
            return decision.Choices.FirstOrDefault(c => c.Parameters["recorded-kind"] == CardKind.Slash.ToString()) ?? decision.Choices[0];
        return decision.Choices.Where(c => c.Cards.Count == 1).OrderBy(c => GetKeepValue(GetAttackCard(c.Cards[0]), _players[f.OwnerSeat])).FirstOrDefault() ?? decision.Choices[0];
    }
    [ProgramCapability]
    private sealed class NamedAmbushCapability(GameEngine engine) : INamedAmbushHost
    { public SkillProgramStepOutcome ExecuteNamedAmbush(SkillProgramEffect e, ProgramSkillFrame f) => engine.BeginNamedAmbush(e, f); }
}
