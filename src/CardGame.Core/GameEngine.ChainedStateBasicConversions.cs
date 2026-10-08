using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ChainedStateBasicMarker = "chained-state-basic";
    private sealed record ChainedStateBasicNeed(ChainedStateBasicIntent Intent, long? ParentFrameId,
        long? RequestFrameId, long? ParentActionId, int Cursor, int? FixedTargetSeat);

    private bool CanPayChainedStateBasic(CharacterState actor, bool desired) => actor.IsAlive && _winner == Winner.None &&
        actor.IsChained != desired && ResolveEnteringChain(actor, desired || HasCardPolicy(actor, SkillProgramCardPolicyKind.ForceChained)) == desired;

    private IEnumerable<(CardConversionSource Source, SkillProgramViewAs Rule, string Hash)> ChainedStateBasicRules(CharacterState actor,
        CardKind? output = null, bool dodge = false)
    {
        foreach (var instance in GetSkillBindingShard(actor).ProgramInstances)
        foreach (var rule in instance.Program.ViewAs.Where(r => r.ChainedStateCost.HasValue))
            if ((output is null || rule.OutputKind == output) && (dodge ? rule.ForResponse && rule.OutputKind == CardKind.Dodge : rule.ForPlay && IsSlashCard(rule.OutputKind)) &&
                rule.UseOnly && rule.InputCount == 0 && rule.Condition.Evaluate(CreateSkillContext(actor)) &&
                CanPayChainedStateBasic(actor, rule.ChainedStateCost!.Value) && !IsCardUseForbidden(actor.Seat, rule.OutputKind, CardActionType.Use))
                yield return (new(instance.SkillId, rule.Id, actor.Seat, instance.SkillInstanceId), rule, instance.Program.GameplayHash);
    }

    private IReadOnlyList<LegalAction> ChainedStateBasicNativePlayActions(CharacterState actor, CardConversionSource source, CardKind kind)
    {
        if (!actor.IsAlive || _winner != Winner.None || _phase != TurnPhase.Play || _currentSeat != actor.Seat ||
            !IsSlashCard(kind) || IsCardUseForbidden(actor.Seat, kind, CardActionType.Use)) return [];
        var card = new Card(0, kind, Suit.None, 0);
        var targets = GetFangtianOrderedSlashTargets(actor, card, source, effectiveKind: kind);
        var actions = targets.Select(t => new LegalAction(LegalActionKind.Slash, 0, t.Seat,
            DescribeConversion(source, $"解除连环状态，视为对 {t.Name} 使用【{card.DisplayName}】"), kind) { ConversionSource = source }).ToList();
        AddProgramTargetCountSlashActions(actions, actor, card, targets, card.DisplayName, kind, source, null);
        return Array.AsReadOnly(actions.Select(a => a with { ChainedStateBasicUse = false }).ToArray());
    }
    private IReadOnlyList<LegalAction> ChainedStateBasicPlayActions(CharacterState actor)
    {
        if (_resolutionStack.Count != 0 || _phase != TurnPhase.Play || _currentSeat != actor.Seat) return [];
        return Array.AsReadOnly(ChainedStateBasicRules(actor).SelectMany(r => ChainedStateBasicNativePlayActions(actor, r.Source, r.Rule.OutputKind)).ToArray());
    }
    private bool TrySubmitChainedStateBasicPlay(PlayCardCommand command, out CommandResult result)
    {
        result = null!;
        if (command.CardId != 0 || command.ConversionSource is not { } source || ViewAsRule(source)?.ChainedStateCost is null) return false;
        var actor = _players[command.ActorSeat];
        var action = ChainedStateBasicPlayActions(actor).SingleOrDefault(a => a.ConversionSource == source && a.PlayedCardKind == command.PlayedCardKind &&
            a.TargetCardId == command.TargetCardId && a.TargetSeats.SequenceEqual(command.TargetSeats ?? []));
        if (action is null || command.AdditionalConversionSources is { Count: > 0 } || _pendingDecision is not { Kind: DecisionKind.PlayCard } original)
        { result = Reject(CommandErrorCode.IllegalAction, "The chained-state use changed its published cost, source, name or native targets."); return true; }
        result = Accept(() =>
        {
            BeginChainedStateBasic(actor, source, action.PlayedCardKind!.Value, action.TargetSeats, original,
                new(ChainedStateBasicIntent.Play, null, null, null, 0, action.TargetSeat));
            AdvanceRulesAndPublishState(); if (_options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary();
        }); return true;
    }
    private bool TryExecuteChainedStateBasicPlay(CharacterState actor, LegalAction action)
    {
        if (action.CardId != 0 || action.ConversionSource is not { } source || ViewAsRule(source)?.ChainedStateCost is null) return false;
        if (!ChainedStateBasicPlayActions(actor).Any(a => a.ConversionSource == source && a.PlayedCardKind == action.PlayedCardKind && a.TargetSeats.SequenceEqual(action.TargetSeats)))
            throw new InvalidOperationException("An AI chained-state use changed its actual legal action.");
        var original = _pendingDecision ?? new PendingDecision(DecisionKind.PlayCard, actor.Seat, "已发布合法用牌动作", [], []) { PromptId = CreatePromptId(), Revision = Revision };
        BeginChainedStateBasic(actor, source, action.PlayedCardKind!.Value, action.TargetSeats, original,
            new(ChainedStateBasicIntent.Play, null, null, null, 0, action.TargetSeat)); return true;
    }

    private ChainedStateBasicNeed? ChainedStateBasicContext(PendingDecision prompt, ResolutionFrame? suppliedTop = null)
    {
        var top = suppliedTop ?? _resolutionStack.LastOrDefault();
        if (top is null || top is ChainedStateBasicFrame or DrawFundedDistinctBasicFrame or RequestedDeckBasicFrame) return null;
        var ownerId = top is ResponseWindowFrame response ? response.ParentFrameId : top.Id;
        var parentAction = LifecycleCardUse(ownerId)?.Action?.ActionId;
        if (prompt.Kind == DecisionKind.RespondDodge && ActiveFactionDefense is null && ActiveGroupCard is null && top is ResponseWindowFrame &&
            ActiveCardAttack is { } attack && attack.TargetSeat == prompt.PlayerSeat && ownerId == attack.ResolutionId && IsProgramResponseCardUse(_players[prompt.PlayerSeat], CardKind.Dodge))
            return new(ChainedStateBasicIntent.OwnSlashDodge, ownerId, top.Id, parentAction, attack.SuccessfulDodgeResponses, attack.SourceSeat);
        if (prompt.Kind == DecisionKind.RespondSlash && ActiveBorrowedSword is { AwaitingSlashChoice: true, ActiveAttack: null } borrowed &&
            borrowed.ResolutionId == ownerId && borrowed.WeaponOwnerSeat == prompt.PlayerSeat && GetWeapon(_players[prompt.PlayerSeat]) is not null)
            return new(ChainedStateBasicIntent.BorrowedSword, ownerId, top.Id, parentAction, 0, borrowed.SlashTargetSeat);
        if (prompt.Kind == DecisionKind.QinglongCrescentBlade && ActiveQinglongCrescentBlade is { } blade && blade.Attack.ResolutionId == ownerId &&
            blade.Attack.SourceSeat == prompt.PlayerSeat && SameAttackOwner(ActiveCardAttack, blade.Attack))
            return new(ChainedStateBasicIntent.Qinglong, ownerId, top.Id, parentAction, 0, blade.Attack.TargetSeat);
        if (prompt.Kind != DecisionKind.ProgramTrigger || top is not ProgramSkillFrame program) return null;
        var paused = ProgramInstructionResolver.Default.Resolve(program, _contentRegistry.GetSkill(program.SkillId).Program!).GetPausedInstruction(program.InstructionIndex).Effect;
        if (paused.Op == SkillProgramEffectOp.RequestSlashByTarget && program.SelectedTargetSeats.SequenceEqual([prompt.PlayerSeat]) &&
            prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "request-slash-decline"))
            return new(ChainedStateBasicIntent.ProgramSlash, program.Id, program.Id, null, program.InstructionIndex, program.OwnerSeat);
        if (paused.Op == SkillProgramEffectOp.RequestSlashByNearest && prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "request-slash-nearest-decline"))
            return new(ChainedStateBasicIntent.ProgramNearestSlash, program.Id, program.Id, null, program.InstructionIndex, null);
        if (paused.Op == SkillProgramEffectOp.RequestSlashAgainstChosenTarget && program.AssistedSlashRequest is { TargetSeat: { } target } assisted && assisted.ActorSeat == prompt.PlayerSeat)
            return new(ChainedStateBasicIntent.AssistedSlash, program.Id, program.Id, null, program.InstructionIndex, target);
        if (paused.Op == SkillProgramEffectOp.RequestLegalSlashByNearest && program.NearestLegalSlashRequest is { AwaitingFaction: false } nearest && nearest.ActorSeat == prompt.PlayerSeat)
            return new(ChainedStateBasicIntent.NearestLegalSlash, program.Id, program.Id, null, program.InstructionIndex, null);
        return null;
    }
    private bool ChainedStateBasicNativeResponseLegal(CharacterState actor, ChainedStateBasicNeed need, CardKind kind, int? targetSeat)
    {
        if (!actor.IsAlive || _winner != Winner.None || IsCardUseForbidden(actor.Seat, kind, CardActionType.Use)) return false;
        if (need.Intent == ChainedStateBasicIntent.OwnSlashDodge)
            return kind == CardKind.Dodge && ActiveFactionDefense is null && ActiveGroupCard is null && IsProgramResponseCardUse(actor, kind) &&
                ActiveCardAttack is { } attack && attack.ResolutionId == need.ParentFrameId && attack.SuccessfulDodgeResponses == need.Cursor && attack.SourceSeat == targetSeat;
        if (!IsSlashCard(kind) || targetSeat is not { } seat || !IsValidPlayerSeat(seat) || !_players[seat].IsAlive ||
            HasBeneficiarySuitShield(actor.Seat, seat, Suit.None) || IsCardTargetProhibited(_players[seat], kind, Suit.None)) return false;
        if (need.Intent == ChainedStateBasicIntent.BorrowedSword)
            return ActiveBorrowedSword is { AwaitingSlashChoice: true, ActiveAttack: null } b && b.ResolutionId == need.ParentFrameId && b.WeaponOwnerSeat == actor.Seat &&
                b.SlashTargetSeat == seat && GetWeapon(actor) is not null && IsLegalBorrowedSwordSlashTarget(actor, _players[seat], kind, Suit.None, false, null, 0, []);
        if (need.Intent == ChainedStateBasicIntent.Qinglong)
            return ActiveQinglongCrescentBlade is { } q && q.Attack.ResolutionId == need.ParentFrameId && q.Attack.SourceSeat == actor.Seat &&
                q.Attack.TargetSeat == seat && SameAttackOwner(ActiveCardAttack, q.Attack) && CanUseQinglongCrescentBladeTarget(actor, _players[seat], kind, null, false, Suit.None, 0);
        if (_resolutionStack.SingleOrDefault(f => f.Id == need.ParentFrameId) is not ProgramSkillFrame program || program.InstructionIndex != need.Cursor ||
            !_players[program.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[program.OwnerSeat], program.SkillId, program.SkillInstanceId)) return false;
        if (need.FixedTargetSeat is { } fixedTarget && fixedTarget != seat ||
            need.Intent is ChainedStateBasicIntent.ProgramNearestSlash or ChainedStateBasicIntent.NearestLegalSlash && !GetNearestLivingCharacterSeats(actor.Seat).Contains(seat)) return false;
        return CanUseSlashTarget(actor, _players[seat], new(0, kind, Suit.None, 0), effectiveKind: kind, noEffectiveRank: true, physicalCardIds: []);
    }
    private IReadOnlyList<PromptChoice> ChainedStateBasicResponseChoices(PendingDecision prompt, ChainedStateBasicNeed need)
    {
        var actor = _players[prompt.PlayerSeat]; var choices = new List<PromptChoice>();
        foreach (var (source, rule, _) in ChainedStateBasicRules(actor, dodge: need.Intent == ChainedStateBasicIntent.OwnSlashDodge))
        foreach (var target in (need.FixedTargetSeat is { } fixedTarget ? new[] { fixedTarget } : prompt.ValidTargetSeats.ToArray())
                     .Where(t => ChainedStateBasicNativeResponseLegal(actor, need, rule.OutputKind, t)))
        {
            var p = new Dictionary<string, string> { [ChainedStateBasicMarker] = need.Intent.ToString(), ["output-kind"] = rule.OutputKind.ToString(),
                ["original-frame-id"] = need.ParentFrameId!.Value.ToString(CultureInfo.InvariantCulture), ["native-target-seat"] = target.ToString(CultureInfo.InvariantCulture) };
            AddConversionParameters(p, source);
            choices.Add(new(new($"chained-state.{need.Intent}.{need.ParentFrameId}.{source.BindingId}.{source.SkillInstanceId}.{target}"),
                $"发动【{ProgramConversionName(source)}】，{(rule.ChainedStateCost == true ? "进入" : "解除")}连环状态，视为使用【{CardCatalog.Get(rule.OutputKind).DisplayName}】。", [],
                need.Intent == ChainedStateBasicIntent.OwnSlashDodge ? [] : [target], p));
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private PendingDecision? AddChainedStateBasicChoices(PendingDecision? prompt)
    {
        if (prompt is null || !_started || prompt.Choices.Any(IsChainedStateBasicAnswer) || ChainedStateBasicContext(prompt) is not { } need) return prompt;
        var choices = ChainedStateBasicResponseChoices(prompt, need);
        return choices.Count == 0 ? prompt : prompt with { Choices = Array.AsReadOnly(prompt.Choices.Concat(choices).ToArray()) };
    }
    private IReadOnlyList<PromptChoice> ChainedStateBasicDodgeChoices(CharacterState actor)
    {
        if (ActiveCardAttack is not { } attack || LifecycleCardUse(attack.ResolutionId) is not { } use ||
            ActiveFactionDefense is not null || ActiveGroupCard is not null || !IsProgramResponseCardUse(actor, CardKind.Dodge)) return [];
        var prompt = new PendingDecision(DecisionKind.RespondDodge, actor.Seat, "真实杀防御", [], [], attack.SourceSeat);
        return ChainedStateBasicResponseChoices(prompt, new(ChainedStateBasicIntent.OwnSlashDodge, use.Id,
            _resolutionStack.OfType<ResponseWindowFrame>().LastOrDefault(r => r.ParentFrameId == use.Id)?.Id,
            use.Action?.ActionId, attack.SuccessfulDodgeResponses, attack.SourceSeat));
    }
    private bool HasChainedStateBasicDodge(CharacterState actor) => ChainedStateBasicDodgeChoices(actor).Count > 0;
    private IEnumerable<(CardConversionSource Source, SkillProgramViewAs Rule, string Hash)> ChainedStateBasicForcedSlashRules(CharacterState actor, CharacterState target, bool qinglong) =>
        ChainedStateBasicRules(actor).Where(r => !HasBeneficiarySuitShield(actor.Seat, target.Seat, Suit.None) && !IsCardTargetProhibited(target, r.Rule.OutputKind, Suit.None) &&
            (qinglong ? CanUseQinglongCrescentBladeTarget(actor, target, r.Rule.OutputKind, null, false, Suit.None, 0) :
                IsLegalBorrowedSwordSlashTarget(actor, target, r.Rule.OutputKind, Suit.None, false, null, 0, [])));
    private IReadOnlyList<PromptChoice> ChainedStateBasicForcedSlashChoices(CharacterState actor, CharacterState target, long originalId, bool qinglong)
    {
        var need = new ChainedStateBasicNeed(qinglong ? ChainedStateBasicIntent.Qinglong : ChainedStateBasicIntent.BorrowedSword,
            originalId, _resolutionStack.OfType<ResponseWindowFrame>().LastOrDefault(r => r.ParentFrameId == originalId)?.Id ?? originalId,
            LifecycleCardUse(originalId)?.Action?.ActionId, 0, target.Seat);
        return ChainedStateBasicResponseChoices(new(qinglong ? DecisionKind.QinglongCrescentBlade : DecisionKind.RespondSlash, actor.Seat, "原生杀使用请求", [], [target.Seat]), need);
    }
    private bool HasChainedStateBasicProgramSlash(CharacterState actor, IEnumerable<int> targets) => ChainedStateBasicRules(actor).Any(r =>
        targets.Any(t => IsValidPlayerSeat(t) && _players[t].IsAlive && !HasBeneficiarySuitShield(actor.Seat, t, Suit.None) &&
            CanUseSlashTarget(actor, _players[t], new(0, r.Rule.OutputKind, Suit.None, 0), effectiveKind: r.Rule.OutputKind, noEffectiveRank: true, physicalCardIds: [])));
    private static bool IsChainedStateBasicAnswer(PromptChoice choice) => choice.Parameters.ContainsKey(ChainedStateBasicMarker);
    private bool IsChainedStateBasicAnswer(ChoiceId id) => _pendingDecision?.Choices.Any(c => c.Id == id && IsChainedStateBasicAnswer(c)) == true;
    private static PendingDecision ChainedStateBasicNativeDecision(PendingDecision prompt) => prompt with
    { Choices = Array.AsReadOnly(prompt.Choices.Where(c => !IsChainedStateBasicAnswer(c)).ToArray()) };
    private CommandResult SubmitChainedStateBasicAnswer(int actorSeat, PromptId promptId, ChoiceId choiceId)
    {
        if (!_started) return Reject(CommandErrorCode.NotStarted, "游戏尚未开始。");
        if (_pendingDecision is not { } prompt || prompt.PlayerSeat != actorSeat || prompt.PromptId != promptId)
            return Reject(CommandErrorCode.InvalidPrompt, "连环状态费用必须回答当前精确提示。");
        var choice = prompt.Choices.SingleOrDefault(c => c.Id == choiceId && IsChainedStateBasicAnswer(c));
        return choice is null ? Reject(CommandErrorCode.InvalidChoice, "该连环转换选择未发布。") : SubmitChainedStateBasicAnswer(actorSeat, prompt, choice);
    }
    private CommandResult SubmitChainedStateBasicAnswer(int actorSeat, PendingDecision prompt, PromptChoice choice)
    {
        if (_winner != Winner.None || !_players[actorSeat].IsHuman || prompt.PlayerSeat != actorSeat || _pendingDecision?.PromptId != prompt.PromptId ||
            !prompt.Choices.Any(c => AssistedChoicesEqual([c], [choice])) || choice.Cards.Count != 0 ||
            !Enum.TryParse<CardKind>(choice.Parameters.GetValueOrDefault("output-kind"), out var kind) ||
            !int.TryParse(choice.Parameters.GetValueOrDefault("native-target-seat"), out var target) ||
            ChainedStateBasicContext(prompt) is not { } need || !ChainedStateBasicResponseChoices(prompt, need).Any(c => AssistedChoicesEqual([c], [choice])))
            return Reject(CommandErrorCode.IllegalAction, "The chained-state answer changed its human actor, source, real cost or original native need.");
        return Accept(() =>
        {
            BeginChainedStateBasic(_players[actorSeat], RequireConversionSource(choice), kind,
                need.Intent == ChainedStateBasicIntent.OwnSlashDodge ? [target] : choice.Targets, prompt, need);
            AdvanceRulesAndPublishState(); if (_options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary();
        });
    }

    private void BeginChainedStateBasic(CharacterState actor, CardConversionSource source, CardKind kind, IReadOnlyList<int> targets,
        PendingDecision original, ChainedStateBasicNeed need)
    {
        var accepted = ChainedStateBasicRules(actor, kind, need.Intent == ChainedStateBasicIntent.OwnSlashDodge).SingleOrDefault(r => r.Source == source);
        if (accepted.Rule is null || original.PlayerSeat != actor.Seat || _resolutionStack.OfType<ChainedStateBasicFrame>().Any(f => f.Payment.OriginalPromptId == original.PromptId))
            throw new InvalidOperationException("A chained-state cost cannot accept an unavailable source or repay an original need.");
        var id = ++_resolutionSequence;
        var p = new ChainedStateBasicPayment(id, source, accepted.Hash, actor.IsChained, accepted.Rule.ChainedStateCost!.Value,
            _turnNumber, _turnProgression.OwnerSeat, need.Intent, kind, need.ParentFrameId, need.RequestFrameId, need.ParentActionId,
            targets.Count == 1 ? targets[0] : need.FixedTargetSeat, need.Cursor, original.PromptId, original.Revision);
        ClearPendingDecision(); PushRuntimeFrame(new ChainedStateBasicFrame(id, p, original, targets));
        AdvanceEventRulesAndQueueFact(new ChainedStateBasicStartedEvent(p));
        // Persist the paid stage before any native state-change observer can run.
        ReplaceRuntimeTop(((ChainedStateBasicFrame)_resolutionStack.Last()) with { Stage = ChainedStateBasicStage.StateChildren });
        actor.IsChained = ResolveEnteringChain(actor, p.DesiredChained || HasCardPolicy(actor, SkillProgramCardPolicyKind.ForceChained));
        if (actor.IsChained != p.DesiredChained || actor.IsChained == p.WasChained)
            throw new InvalidOperationException("A chained-state cost must actually change to its accepted destination.");
        if (!p.WasChained && p.DesiredChained) RecordCharacterStateChange(actor.Seat, SkillProgramTriggerWindow.CharacterEnteredChain, id);
        var change = CompleteProgramEventHistory().OfType<CharacterStateChangedEvent>().SingleOrDefault(e => e.Change.ParentFrameId == id);
        p = p with { CharacterStateChangeId = change?.Change.Id };
        ReplaceRuntimeTop(((ChainedStateBasicFrame)_resolutionStack.Last()) with { Payment = p });
        AdvanceEventRulesAndQueueFact(new ChainedStateBasicPaidEvent(p)); ContinueChainedStateBasic(id);
    }
    private void ContinueChainedStateBasic(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ChainedStateBasicFrame frame || frame.Id != id || _pendingDecision is not null) return;
        if (frame.Stage != ChainedStateBasicStage.StateChildren || frame.ActiveChildFrameId is not null || !ValidChainedStateBasicPayment(frame.Payment))
            throw new InvalidOperationException("A chained-state cost resumed before its exact paid state child.");
        if (TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.ChainedStateBasic)) return;
        var legal = ChainedStateBasicOriginalNeedCurrent(frame);
        PopResolutionFrame(id, ResolutionFrameKind.ChainedStateBasic);
        if (!legal) { CancelChainedStateBasic(frame); return; }
        ExecuteChainedStateBasicPaid(frame);
    }
    private void BeginChainedStateBasicChild(long parentId, long childId)
    {
        if (_resolutionStack.LastOrDefault() is not ChainedStateBasicFrame frame || frame.Id != parentId || frame.ActiveChildFrameId is not null ||
            frame.Payment.CharacterStateChangeId != childId || !ValidChainedStateBasicPayment(frame.Payment))
            throw new InvalidOperationException("A chained-state observer lost its exact once-paid owner.");
        ReplaceRuntimeTop(frame with { ActiveChildFrameId = childId });
    }
    private void ResumeChainedStateBasicState(ProgramLifecycleTriggerWindowFrame child)
    {
        if (_resolutionStack.LastOrDefault() is not ChainedStateBasicFrame parent || parent.ActiveChildFrameId != child.Id ||
            child.ResumeProgramFrameId != parent.Id || child.CharacterStateContinuation != CharacterStateContinuation.ChainedStateBasic ||
            child.Window != SkillProgramTriggerWindow.CharacterEnteredChain || child.OwnerSeat != parent.Payment.Source.OwnerSeat ||
            parent.Payment.CharacterStateChangeId != child.Id || !ValidChainedStateBasicPayment(parent.Payment))
            throw new InvalidOperationException("A chained-state child returned to another payment or native need.");
        ReplaceRuntimeTop(parent with { ActiveChildFrameId = null }); ContinueChainedStateBasic(parent.Id);
    }
    private bool ValidChainedStateBasicPayment(ChainedStateBasicPayment p)
    {
        if (p.PaymentFrameId <= 0 || !IsValidPlayerSeat(p.Source.OwnerSeat) || string.IsNullOrWhiteSpace(p.Source.SkillInstanceId) ||
            p.WasChained == p.DesiredChained || p.DesiredChained != (p.EffectiveKind == CardKind.Dodge) || p.Cursor < 0 ||
            !Enum.IsDefined(p.Intent) || _contentRegistry.Skills.GetValueOrDefault(p.Source.SkillId)?.Program is not { } definition || definition.GameplayHash != p.GameplayHash ||
            definition.ViewAs.SingleOrDefault(v => v.Id == p.Source.BindingId) is not { InputCount: 0, UseOnly: true, ChainedStateCost: { } desired } rule ||
            desired != p.DesiredChained || rule.OutputKind != p.EffectiveKind ||
            (p.Intent == ChainedStateBasicIntent.OwnSlashDodge) != p.DesiredChained) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<ChainedStateBasicStartedEvent>().Where(e => e.Payment.PaymentFrameId == p.PaymentFrameId).ToArray() is not [var started] ||
            started.Payment != (p with { CharacterStateChangeId = null }) ||
            history.OfType<ChainedStateBasicPaidEvent>().Count(e => e.Payment == p) != 1) return false;
        var changes = history.OfType<CharacterStateChangedEvent>().Where(e => e.Change.ParentFrameId == p.PaymentFrameId).ToArray();
        return p.CharacterStateChangeId is { } changeId ? p.DesiredChained && changes is [var changed] && changed.Change.Id == changeId &&
            changed.Change.TargetSeat == p.Source.OwnerSeat && changed.Change.Window == SkillProgramTriggerWindow.CharacterEnteredChain : changes.Length == 0;
    }
    private bool ChainedStateBasicOriginalNeedCurrent(ChainedStateBasicFrame frame)
    {
        var p = frame.Payment; var actor = _players[p.Source.OwnerSeat];
        if (!actor.IsAlive || _winner != Winner.None || p.ActualTurnNumber != _turnNumber || p.ActualTurnOwnerSeat != _turnProgression.OwnerSeat) return false;
        if (p.Intent == ChainedStateBasicIntent.Play)
            return _resolutionStack.Count == 1 && ChainedStateBasicNativePlayActions(actor, p.Source, p.EffectiveKind).Any(a => a.TargetSeats.SequenceEqual(frame.TargetSeats));
        var request = _resolutionStack.SingleOrDefault(f => f.Id == p.RequestFrameId);
        if (request is null || _resolutionStack.Count < 2 || _resolutionStack[^2].Id != request.Id ||
            ChainedStateBasicContext(frame.OriginalDecision, request) is not { } current || current.Intent != p.Intent || current.ParentFrameId != p.ParentFrameId ||
            current.RequestFrameId != p.RequestFrameId || current.ParentActionId != p.ParentActionId || current.Cursor != p.Cursor) return false;
        if (request is ProgramSkillFrame program)
        {
            var effect = ProgramInstructionResolver.Default.Resolve(program, _contentRegistry.GetSkill(program.SkillId).Program!).GetPausedInstruction(program.InstructionIndex).Effect;
            if (!IsChainedStateBasicProgramSelection(program, effect, frame)) return false;
        }
        return ChainedStateBasicNativeResponseLegal(actor, current, p.EffectiveKind, p.TargetSeat);
    }
    private void CancelChainedStateBasic(ChainedStateBasicFrame frame)
    {
        var p = frame.Payment; AdvanceEventRulesAndQueueFact(new ChainedStateBasicCancelledEvent(p)); ClearPendingDecision(); _status = EngineStatus.Running;
        if (_winner != Winner.None) return;
        if (p.Intent == ChainedStateBasicIntent.BorrowedSword && ActiveBorrowedSword is { ActiveAttack: null } b && b.ResolutionId == p.ParentFrameId)
        { CompleteBorrowedSwordWithoutSlash(b, transferWeapon: _players[b.WeaponOwnerSeat].IsAlive && _players[b.SlashTargetSeat].IsAlive); return; }
        if (p.Intent == ChainedStateBasicIntent.Qinglong && ActiveQinglongCrescentBlade is { } q && q.Attack.ResolutionId == p.ParentFrameId)
        { _pendingDecision = ChainedStateBasicNativeDecision(frame.OriginalDecision); ResolveQinglongCrescentBladeChoice(frame.OriginalDecision.Choices.Single(c => c.Parameters.GetValueOrDefault("action") == "qinglong-skip")); return; }
        if (p.Intent == ChainedStateBasicIntent.OwnSlashDodge && ActiveCardAttack is { } attack && attack.ResolutionId == p.ParentFrameId)
        {
            if (_resolutionStack.LastOrDefault() is ResponseWindowFrame response && response.ParentFrameId == attack.ResolutionId) PopResponseWindow(attack.ResolutionId);
            SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            if (!_players[attack.TargetSeat].IsAlive || !ApplyAttackDamage(attack)) CompleteAttack(attack); return;
        }
        if (p.Intent is ChainedStateBasicIntent.ProgramSlash or ChainedStateBasicIntent.ProgramNearestSlash or ChainedStateBasicIntent.AssistedSlash or ChainedStateBasicIntent.NearestLegalSlash &&
            _resolutionStack.LastOrDefault() is ProgramSkillFrame program && program.Id == p.ParentFrameId && program.InstructionIndex == p.Cursor)
            CancelProgramBindingAndCleanup(program, "原生用牌请求已失效；已支付的连环状态费用不重复支付。");
    }

    private bool TryAdvanceChainedStateBasicAi()
    {
        if (_pendingDecision is not { } prompt || _players[prompt.PlayerSeat].IsHuman || ChainedStateBasicContext(prompt) is not { } need) return false;
        var choice = prompt.Choices.FirstOrDefault(c => IsChainedStateBasicAnswer(c) && ChainedStateBasicResponseChoices(prompt, need).Any(v => AssistedChoicesEqual([v], [c])));
        if (choice is null || !Enum.TryParse<CardKind>(choice.Parameters.GetValueOrDefault("output-kind"), out var kind) ||
            !int.TryParse(choice.Parameters.GetValueOrDefault("native-target-seat"), out var target)) return false;
        var actor = _players[prompt.PlayerSeat];
        if (need.Intent == ChainedStateBasicIntent.OwnSlashDodge && ActiveCardAttack is { } attack)
        { var decision = _aiBrains[actor.Seat].ChooseDodgeResponse(CreateSnapshot(actor.Seat), attack.SourceSeat, ++_thoughtSequence, attack.IgnoresArmor, true); AddThought(decision.Item3); if (!decision.Item1) return false; }
        else if (need.Intent == ChainedStateBasicIntent.BorrowedSword && ActiveBorrowedSword is { } borrowed)
        {
            // The mature evaluator recognizes its native Slash-choice marker. Adapt only
            // this actor's already-published, revalidated option for evaluation; the
            // original prompt and the exact choice used for payment remain unchanged.
            var view = CreateSnapshot(actor.Seat);
            var parameters = choice.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            parameters["response"] = "borrowed-sword-slash";
            var evaluationPrompt = prompt with
            {
                Choices = Array.AsReadOnly(prompt.Choices.Select(candidate => candidate.Id == choice.Id
                    ? candidate with { Parameters = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(parameters) }
                    : candidate).ToArray())
            };
            var decision = _aiBrains[actor.Seat].ChooseBorrowedSwordResponse(view with { PendingDecision = evaluationPrompt }, borrowed.SourceSeat, target, ++_thoughtSequence);
            AddThought(decision.Thought); if (!decision.UseSlash) return false;
        }
        else if (need.Intent == ChainedStateBasicIntent.Qinglong)
        { var decision = _aiBrains[actor.Seat].ChooseChainedStateBasicTargetUse(CreateSnapshot(actor.Seat), target, ++_thoughtSequence); AddThought(decision.Thought); if (!decision.UseSlash) return false; }
        BeginChainedStateBasic(actor, RequireConversionSource(choice), kind, need.Intent == ChainedStateBasicIntent.OwnSlashDodge ? [target] : choice.Targets, prompt, need);
        AdvanceRulesAndPublishState(); return true;
    }
}

public sealed partial class SimpleAiBrain
{
    private (double Score, string Reason) ScoreChainedStateBasicAction(GameSnapshot view, PlayerSnapshot self, Role role, LegalAction action)
    {
        if (action.CardId != 0 || action.ChainedStateBasicUse != false || action.ConversionSource is null || action.PlayedCardKind is not { } kind ||
            !IsSlashCard(kind) || action.TargetSeats.Count == 0) return (-1000, "连环费用转换缺少真实合法用牌方向。");
        var pressure = action.TargetSeats.Select(s => view.Players.Single(p => p.Seat == s)).Sum(t => GetHostility(view, role, t) + (t.Hp <= 1 ? 28 : 0));
        return (CardCatalog.Get(kind).AiPlayValue + pressure + 2, "按公开目标与体力评估实际解除连环后的杀，不读取他人暗牌。");
    }
    internal (bool UseSlash, AiThoughtRecord Thought) ChooseChainedStateBasicTargetUse(GameSnapshot view, int targetSeat, int sequence)
    {
        var self = view.Players.Single(p => p.Seat == Seat); var target = view.Players.Single(p => p.Seat == targetSeat);
        var score = GetHostility(view, self.Role ?? Role.Renegade, target) * 1.2 + (target.Hp <= 1 ? 45 : 0) + 2;
        var use = score > 0; var text = use ? "解除连环状态继续使用杀。" : "保留当前连环状态，放弃追杀。";
        return (use, new(sequence, view.TurnNumber, Seat, text,
            [new(new LegalAction(LegalActionKind.Slash, 0, targetSeat, text, CardKind.Slash), score, "只依据公开关系与目标体力。")], text));
    }
}
