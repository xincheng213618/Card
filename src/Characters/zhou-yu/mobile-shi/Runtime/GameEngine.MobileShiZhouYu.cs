using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShiZhouYuChiyun = ShiZhouYuComposition.ChiyunSkill, ShiZhouYuYanhui = ShiZhouYuComposition.YanhuiSkill,
        ShiZhouYuFentao = ShiZhouYuComposition.FentaoSkill, ShiZhouYuXiongzi = ShiZhouYuComposition.XiongziSkill;

    private static CardConversionSource ShiZhouYuSource(ProgramSkillFrame f) =>
        new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool HasShiZhouYuCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveShiZhouYuChiyun);

    // ---- ledger-backed facts ---------------------------------------------------

    private int ShiZhouYuKeptOption(int ownerSeat) =>
        !HasShiZhouYuCapability ? 0 :
        ProgramEventHistory<ShiZhouYuXiongziActivatedEvent>().LastOrDefault(entry => entry.OwnerSeat == ownerSeat &&
            entry.KeptOption is 1 or 2)?.KeptOption ?? 0;

    private bool ShiZhouYuChiyunPhaseSeen(int ownerSeat) =>
        ProgramEventHistory<ShiZhouYuChiyunPhaseSeenEvent>().Any(entry =>
            entry.TurnNumber == _turnNumber && entry.Phase == (int)_phase && entry.OwnerSeat == ownerSeat);

    private int ShiZhouYuYanhuiShownPlayers(int turnNumber) =>
        ProgramEventHistory<ShiZhouYuYanhuiShownEvent>().Where(entry => entry.TurnNumber == turnNumber)
            .Select(entry => entry.HolderSeat).Distinct().Count();

    private int[] ShiZhouYuYanhuiVictims(int turnNumber) =>
        ProgramEventHistory<ShiZhouYuYanhuiShownEvent>().Where(entry => entry.TurnNumber == turnNumber && entry.Discarded)
            .Select(entry => entry.HolderSeat).Distinct().Order().ToArray();

    private bool ShiZhouYuCardShownBefore(int turnNumber, int cardId) =>
        ProgramEventHistory<ShiZhouYuYanhuiShownEvent>().Any(entry => entry.TurnNumber == turnNumber && entry.CardId == cardId);

    private bool ShiZhouYuFentaoPendingRechain(int ownerSeat, int targetSeat) =>
        ProgramEventHistory<ShiZhouYuFentaoChosenEvent>().Any(entry => entry.Option == 2 &&
            entry.Source.OwnerSeat == ownerSeat && entry.TargetSeat == targetSeat && entry.ActualTurn == _turnNumber &&
            !ProgramEventHistory<ShiZhouYuFentaoRechainedEvent>().Any(done =>
                done.Source.OwnerSeat == ownerSeat && done.TargetSeat == targetSeat && done.ActualTurn == _turnNumber));

    // ---- trigger legality -------------------------------------------------------

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 67800,
        SkillProgramEffectOp.ResolveShiZhouYuChiyun, SkillProgramEffectOp.ResolveShiZhouYuYanhuiShow,
        SkillProgramEffectOp.ResolveShiZhouYuYanhuiEnding, SkillProgramEffectOp.ResolveShiZhouYuFentao,
        SkillProgramEffectOp.ResolveShiZhouYuFentaoRechain, SkillProgramEffectOp.ResolveShiZhouYuXiongzi)]
    private bool CanOfferShiZhouYu(ProgramTriggerLegalityContext gate)
    {
        if (_winner != Winner.None || _turnNumber <= 0 || !_players[gate.OwnerSeat].IsAlive) return false;
        if (gate.SkillId == ShiZhouYuChiyun)
            return !ShiZhouYuChiyunPhaseSeen(gate.OwnerSeat) && GetHand(_players[gate.OwnerSeat]).Count > 0 &&
                _players.Any(player => player.IsAlive && player.Seat != gate.OwnerSeat);
        if (gate.SkillId == ShiZhouYuYanhui && gate.Trigger.Id == ShiZhouYuComposition.YanhuiTargetTrigger)
            return gate.Context is { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } use } &&
                use.EventTargetSeat is { } target && target != gate.OwnerSeat && IsValidPlayerSeat(target) &&
                _players[target].IsAlive && GetHand(_players[target]).Count > 0;
        if (gate.SkillId == ShiZhouYuYanhui)
            return ShiZhouYuYanhuiShownPlayers(_turnNumber) > 0;
        if (gate.SkillId == ShiZhouYuFentao && gate.Trigger.Id == ShiZhouYuComposition.FentaoFireTrigger)
            return gate.Context is { Window: SkillProgramTriggerWindow.BeforeDamageApplied,
                    TargetSeat: { } damaged, Amount: > 0 } &&
                damaged != gate.OwnerSeat && IsValidPlayerSeat(damaged) && _players[damaged].IsAlive &&
                _players[damaged].IsChained && CurrentDamageAttempt is { } attack &&
                attack.TargetSeat == damaged && GetDamageNature(attack) == DamageNature.Fire;
        if (gate.SkillId == ShiZhouYuFentao)
            return gate.Context is { Window: SkillProgramTriggerWindow.AfterDamageApplied, TargetSeat: { } settled } &&
                IsValidPlayerSeat(settled) && _players[settled].IsAlive && ShiZhouYuFentaoPendingRechain(gate.OwnerSeat, settled);
        if (gate.SkillId == ShiZhouYuXiongzi)
            return _currentSeat == gate.OwnerSeat && !ProgramEventHistory<ShiZhouYuXiongziActivatedEvent>()
                .Any(entry => entry.OwnerSeat == gate.OwnerSeat);
        return true;
    }

    // 雄姿 mode: the three foundation skills may only trigger during the owner's own turn.
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 67801,
        SkillProgramEffectOp.ResolveShiZhouYuChiyun, SkillProgramEffectOp.ResolveShiZhouYuYanhuiShow,
        SkillProgramEffectOp.ResolveShiZhouYuYanhuiEnding, SkillProgramEffectOp.ResolveShiZhouYuFentao)]
    private bool CanOfferShiZhouYuUnderXiongzi(ProgramTriggerLegalityContext gate)
    {
        var kept = ShiZhouYuKeptOption(gate.OwnerSeat);
        return kept == 0 || _currentSeat == gate.OwnerSeat;
    }

    [ProgramCapability]
    private sealed class ShiZhouYuCapability(GameEngine engine) : IShiZhouYuHost
    {
        public SkillProgramStepOutcome ResolveChiyun(ProgramSkillFrame frame) => engine.BeginShiZhouYu(frame, SkillProgramEffectOp.ResolveShiZhouYuChiyun);
        public SkillProgramStepOutcome ResolveChiyunOption(ProgramSkillFrame frame) => engine.BeginShiZhouYu(frame, SkillProgramEffectOp.ResolveShiZhouYuChiyunOption);
        public SkillProgramStepOutcome ResolveYanhuiShow(ProgramSkillFrame frame) => engine.BeginShiZhouYu(frame, SkillProgramEffectOp.ResolveShiZhouYuYanhuiShow);
        public SkillProgramStepOutcome ResolveYanhuiEnding(ProgramSkillFrame frame) => engine.BeginShiZhouYu(frame, SkillProgramEffectOp.ResolveShiZhouYuYanhuiEnding);
        public SkillProgramStepOutcome ResolveFentao(ProgramSkillFrame frame) => engine.BeginShiZhouYu(frame, SkillProgramEffectOp.ResolveShiZhouYuFentao);
        public SkillProgramStepOutcome ResolveFentaoRechain(ProgramSkillFrame frame) => engine.BeginShiZhouYu(frame, SkillProgramEffectOp.ResolveShiZhouYuFentaoRechain);
        public SkillProgramStepOutcome ResolveXiongzi(ProgramSkillFrame frame) => engine.BeginShiZhouYu(frame, SkillProgramEffectOp.ResolveShiZhouYuXiongzi);
    }

    private SkillProgramStepOutcome BeginShiZhouYu(ProgramSkillFrame input, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.ShiZhouYu is not null && op != SkillProgramEffectOp.ResolveShiZhouYuChiyunOption)
            throw new InvalidOperationException("Shi Zhou Yu restarted a running operation.");
        var r = op switch
        {
            SkillProgramEffectOp.ResolveShiZhouYuChiyun when f.SkillId == ShiZhouYuChiyun && f.TriggerId == ShiZhouYuComposition.ChiyunTrigger =>
                StartShiZhouYuChiyun(f),
            SkillProgramEffectOp.ResolveShiZhouYuChiyunOption when f.SkillId == ShiZhouYuChiyun &&
                f.ShiZhouYu is { Operation: "chiyun-option", Stage: ShiZhouYuComposition.ChiyunStageOption } => f.ShiZhouYu!,
            SkillProgramEffectOp.ResolveShiZhouYuYanhuiShow when f.SkillId == ShiZhouYuYanhui &&
                f.TriggerId == ShiZhouYuComposition.YanhuiTargetTrigger && f.ShiZhouYu is null => StartShiZhouYuYanhuiShow(f),
            SkillProgramEffectOp.ResolveShiZhouYuYanhuiEnding when f.SkillId == ShiZhouYuYanhui &&
                f.TriggerId == ShiZhouYuComposition.YanhuiEndingTrigger && f.ShiZhouYu is null => StartShiZhouYuEnding(f),
            SkillProgramEffectOp.ResolveShiZhouYuFentao when f.SkillId == ShiZhouYuFentao &&
                f.TriggerId == ShiZhouYuComposition.FentaoFireTrigger && f.ShiZhouYu is null => StartShiZhouYuFentao(f),
            SkillProgramEffectOp.ResolveShiZhouYuFentaoRechain when f.SkillId == ShiZhouYuFentao &&
                f.TriggerId == ShiZhouYuComposition.FentaoRechainTrigger && f.ShiZhouYu is null => StartShiZhouYuRechain(f),
            SkillProgramEffectOp.ResolveShiZhouYuXiongzi when f.SkillId == ShiZhouYuXiongzi &&
                f.TriggerId == ShiZhouYuComposition.XiongziTrigger && f.ShiZhouYu is null =>
                new ShiZhouYuReceipt("xiongzi", _turnNumber) { Source = ShiZhouYuSource(f), GameplayHash = f.GameplayHash },
            _ => throw new InvalidOperationException("Shi Zhou Yu instruction has an incompatible owning skill."),
        };
        ReplaceRuntimeTop(f = f with { ShiZhouYu = r });
        ContinueShiZhouYu(f.Id);
        return _pendingDecision is not null ? SkillProgramStepOutcome.AwaitChoice : SkillProgramStepOutcome.AwaitChild;
    }

    private ShiZhouYuReceipt StartShiZhouYuChiyun(ProgramSkillFrame f)
    {
        _ = f.WindowContext ?? throw new InvalidOperationException("Chiyun lost its gained-cards window.");
        AdvanceEventRulesAndQueueFact(new ShiZhouYuChiyunPhaseSeenEvent(_turnNumber, (int)_phase, f.OwnerSeat));
        return new("chiyun", _turnNumber) { Source = ShiZhouYuSource(f), GameplayHash = f.GameplayHash };
    }

    private ShiZhouYuReceipt StartShiZhouYuYanhuiShow(ProgramSkillFrame f)
    {
        var use = f.WindowContext?.CardUse ??
            throw new InvalidOperationException("Yanhui lost its finalized-targets window.");
        var target = use.EventTargetSeat ?? throw new InvalidOperationException("Yanhui lost its targeted character.");
        if (target == f.OwnerSeat || !IsValidPlayerSeat(target) || !_players[target].IsAlive || GetHand(_players[target]).Count == 0)
            throw new InvalidOperationException("Yanhui show lost its living other target with hand cards.");
        return new("yanhui-show", _turnNumber, TargetSeat: target, Stage: ShiZhouYuComposition.YanhuiShowStagePick)
        { Source = ShiZhouYuSource(f), GameplayHash = f.GameplayHash };
    }

    private ShiZhouYuReceipt StartShiZhouYuEnding(ProgramSkillFrame f)
    {
        if (ShiZhouYuYanhuiShownPlayers(_turnNumber) <= 0)
            throw new InvalidOperationException("Yanhui ending requires at least one shown character this turn.");
        return new("yanhui-ending", _turnNumber) { Source = ShiZhouYuSource(f), GameplayHash = f.GameplayHash };
    }

    private ShiZhouYuReceipt StartShiZhouYuFentao(ProgramSkillFrame f)
    {
        var attack = CurrentDamageAttempt ?? throw new InvalidOperationException("Fentao lost its current fire damage.");
        if (f.WindowContext is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied, TargetSeat: { } target, Amount: > 0 } ||
            target != attack.TargetSeat || attack.TargetSeat == f.OwnerSeat || !_players[target].IsChained ||
            GetDamageNature(attack) != DamageNature.Fire)
            throw new InvalidOperationException("Fentao lost its chained fire-damage window.");
        return new("fentao", _turnNumber, TargetSeat: target)
        { Source = ShiZhouYuSource(f), GameplayHash = f.GameplayHash, AttackResolutionId = attack.ResolutionId };
    }

    private ShiZhouYuReceipt StartShiZhouYuRechain(ProgramSkillFrame f)
    {
        if (f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, TargetSeat: { } target } ||
            !ShiZhouYuFentaoPendingRechain(f.OwnerSeat, target))
            throw new InvalidOperationException("Fentao re-chain lost its planned discard outcome.");
        return new("fentao-rechain", _turnNumber, TargetSeat: target, Stage: ShiZhouYuComposition.FentaoStageFinished)
        { Source = ShiZhouYuSource(f), GameplayHash = f.GameplayHash };
    }

    // ---- continuations -----------------------------------------------------------

    private void ContinueShiZhouYu(long id)
    {
        if (_pendingDecision is not null ||
            _resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiZhouYu: { } r } f || f.Id != id) return;
        if (r.Stage is ShiZhouYuComposition.ChiyunStageGiveIssued or ShiZhouYuComposition.FentaoStageDiscardIssued or
            ShiZhouYuComposition.YanhuiResultDiscardIssued)
        {
            if (DrainShiZhouYu(f)) return;
            f = GetActiveProgramFrame(id);
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
            r = f.ShiZhouYu!;
        }
        switch (r.Operation, r.Stage)
        {
            case ("chiyun", ShiZhouYuComposition.ChiyunStageTarget):
            case ("chiyun", ShiZhouYuComposition.ChiyunStageCards):
            case ("chiyun-option", ShiZhouYuComposition.ChiyunStageOption):
            case ("yanhui-ending", ShiZhouYuComposition.YanhuiStageEndingChoice):
            case ("fentao", ShiZhouYuComposition.FentaoStageChoice):
            case ("fentao", ShiZhouYuComposition.FentaoStageDiscardPick):
            case ("xiongzi", ShiZhouYuComposition.XiongziStageChoice):
                PublishShiZhouYu(GetActiveProgramFrame(id));
                return;
            case ("chiyun", ShiZhouYuComposition.ChiyunStageGiveIssued):
                // The give batch settled; hand over to the recipient's option instruction.
                ReplaceRuntimeTop(GetActiveProgramFrame(id) with
                { ShiZhouYu = r with { Operation = "chiyun-option", Stage = ShiZhouYuComposition.ChiyunStageOption } });
                AdvanceRuntimeProgram(id);
                return;
            case ("yanhui-show", ShiZhouYuComposition.YanhuiShowStagePick):
                PublishShiZhouYu(GetActiveProgramFrame(id));
                return;
            case ("yanhui-show", ShiZhouYuComposition.YanhuiResultDiscardIssued):
            case ("fentao", ShiZhouYuComposition.FentaoStageDiscardIssued):
                // The discard batch settled inside a suspended parent window; hand
                // the program back to the runtime so the parent resumes exactly once.
                ReplaceRuntimeTop(GetActiveProgramFrame(id) with { ShiZhouYu = null });
                AdvanceRuntimeProgram(id);
                return;
            case ("chiyun", ShiZhouYuComposition.ChiyunStageResolveDamage):
            case ("yanhui-ending", ShiZhouYuComposition.YanhuiStageEndingDamage):
            case ("fentao", ShiZhouYuComposition.FentaoStageFinished):
            case ("xiongzi", ShiZhouYuComposition.XiongziStageFinished):
                // The issued damage child settled through CompleteProgramAttack,
                // which resumes this frame; the damage was the final effect.
                FinishShiZhouYuOperation(GetActiveProgramFrame(id));
                return;
            case ("fentao-rechain", ShiZhouYuComposition.FentaoStageFinished):
                RunShiZhouYuFentaoRechain(GetActiveProgramFrame(id));
                return;
            default:
                throw new InvalidOperationException("Shi Zhou Yu has an unknown continuation stage.");
        }
    }

    private bool DrainShiZhouYu(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) ||
        TryBeginAdvancedSkillsChanged(f.Id);

    private void FinishShiZhouYuOperation(ProgramSkillFrame f) =>
        FinishProgramSkill(GetActiveProgramFrame(f.Id), true);

    // ---- choice publication -------------------------------------------------------

    private IReadOnlyList<PromptChoice> ShiZhouYuChoices(ProgramSkillFrame f)
    {
        var r = f.ShiZhouYu!; var choices = new List<PromptChoice>();
        void Add(string branch, string label, int? target = null, IReadOnlyList<int>? cards = null) => choices.Add(new(
            new($"shi-zhou-yu.{f.Id}.{r.Stage}.{branch}.{target ?? -1}.{choices.Count}"), label, cards ?? [],
            target is { } seat ? [seat] : [], new Dictionary<string, string>
            {
                ["program-action"] = ShiZhouYuComposition.ProgramAction,
                ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch,
                ["target"] = target?.ToString(CultureInfo.InvariantCulture) ?? "",
            }));
        var kept = ShiZhouYuKeptOption(f.OwnerSeat);
        switch (r.Operation, r.Stage)
        {
            case ("chiyun", ShiZhouYuComposition.ChiyunStageTarget):
            {
                foreach (var seat in _players.Where(player => player.IsAlive && player.Seat != f.OwnerSeat)
                    .Select(player => player.Seat))
                    Add("target", $"将手牌交给 {_players[seat].Name}", seat);
                Add("cancel", "不发动“炽沄”");
                break;
            }
            case ("chiyun", ShiZhouYuComposition.ChiyunStageCards):
            {
                var picked = r.PickedCards;
                foreach (var card in GetHand(_players[f.OwnerSeat]))
                    Add(picked.Contains(card.Id) ? "unpick" : "pick",
                        (picked.Contains(card.Id) ? "取消选择 " : "选择 ") + $"【{card.DisplayName}】", null, [card.Id]);
                if (picked.Count > 0)
                    Add("confirm", $"交给 {_players[r.TargetSeat].Name}（已选 {picked.Count} 张）", null, picked.ToArray());
                Add("back", "重新选择目标");
                break;
            }
            case ("yanhui-show", ShiZhouYuComposition.YanhuiShowStagePick):
            {
                foreach (var card in GetHand(_players[r.TargetSeat]))
                    Add("show", $"观看并展示【{card.DisplayName}】（{card.RankText}）", null, [card.Id]);
                Add("decline", "不展示“焰洄”的手牌");
                break;
            }
            case ("chiyun-option", ShiZhouYuComposition.ChiyunStageOption):
            {
                if (kept != 2)
                    Add("option1", "展示所有与这些牌颜色相同的手牌，然后其受到炽沄的1点火焰伤害", r.TargetSeat);
                if (kept != 1)
                    Add("option2", "令炽沄周瑜摸两张牌，然后你横置", r.TargetSeat);
                break;
            }
            case ("yanhui-ending", ShiZhouYuComposition.YanhuiStageEndingChoice):
            {
                if (kept != 2)
                    foreach (var seat in ShiZhouYuYanhuiVictims(_turnNumber).Where(seat => _players[seat].IsAlive))
                        Add("damage", $"对 {_players[seat].Name} 造成1点火焰伤害", seat);
                var draw = ShiZhouYuYanhuiShownPlayers(_turnNumber);
                if (kept != 1 && draw > 0)
                    Add("draw", $"摸{draw}张牌（本回合展示过牌的角色数）");
                break;
            }
            case ("fentao", ShiZhouYuComposition.FentaoStageChoice):
            {
                if (kept != 2)
                    Add("amplify", "令本次传导的伤害值+1", r.TargetSeat);
                if (kept != 1)
                {
                    var target = _players[r.TargetSeat];
                    Add("discard", $"弃置一半的牌（向上取整 {(GetHand(target).Count + GetEquipment(target).Count + 1) / 2} 张）", r.TargetSeat);
                }
                break;
            }
            case ("fentao", ShiZhouYuComposition.FentaoStageDiscardPick):
            {
                var picked = r.PickedCards;
                var target = _players[r.TargetSeat];
                foreach (var card in GetHand(target).Concat(GetEquipment(target)))
                    Add(picked.Contains(card.Id) ? "unpick" : "pick",
                        (picked.Contains(card.Id) ? "取消弃置 " : "弃置 ") + $"【{card.DisplayName}】", null, [card.Id]);
                var required = (GetHand(target).Count + GetEquipment(target).Count + 1) / 2;
                if (picked.Count == required)
                    Add("confirm", $"确认弃置 {picked.Count} 张", null, picked.ToArray());
                break;
            }
            case ("xiongzi", ShiZhouYuComposition.XiongziStageChoice):
                Add("keep1", "仅保留“炽沄”“焰洄”“焚涛”的全部选项一，且只能于你的回合内发动");
                Add("keep2", "仅保留“炽沄”“焰洄”“焚涛”的全部选项二，且只能于你的回合内发动");
                Add("cancel", "不发动“雄姿”");
                break;
        }
        return Array.AsReadOnly(choices.ToArray());
    }

    private void PublishShiZhouYu(ProgramSkillFrame f)
    {
        var r = f.ShiZhouYu!;
        var choices = ShiZhouYuChoices(f);
        var chooser = r.Operation is "chiyun-option" or "fentao" ? r.TargetSeat : f.OwnerSeat;
        if (choices.Count == 0 || _winner != Winner.None ||
            !_players[f.OwnerSeat].IsAlive || !_players[chooser].IsAlive)
        { FinishShiZhouYuOperation(GetActiveProgramFrame(f.Id)); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser,
            r.Operation switch
            {
                "chiyun" => r.Stage == ShiZhouYuComposition.ChiyunStageTarget
                    ? "炽沄：选择一名其他角色，将任意张手牌交给其。"
                    : "炽沄：选择要交出的手牌。",
                "chiyun-option" => "炽沄：选择一项。",
                "yanhui-show" => "焰洄：观看目标的手牌并展示其中一张。",
                "yanhui-ending" => "焰洄：选择一项。",
                "fentao" => r.Stage == ShiZhouYuComposition.FentaoStageChoice
                    ? "焚涛：选择一项。"
                    : "焚涛：选择要弃置的牌（一半，向上取整）。",
                _ => "雄姿：选择一项。",
            },
            choices.SelectMany(choice => choice.Cards).Distinct().ToArray(),
            choices.SelectMany(choice => choice.Targets).Distinct().ToArray(), chooser)
        {
            PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = chooser, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description)
        };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }

    [ProgramAnswer(ShiZhouYuComposition.ProgramAction)]
    private void AnswerShiZhouYu(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiZhouYu: { } r } f ||
            _pendingDecision is not { } pending ||
            pending.PlayerSeat != (r.Operation is "chiyun-option" or "fentao" ? r.TargetSeat : f.OwnerSeat) ||
            !ShiZhouYuChoices(GetActiveProgramFrame(f.Id)).Any(choice => AssistedChoicesEqual([choice], [selected])))
            throw new InvalidOperationException("Shi Zhou Yu choice lost its current frame, chooser or legal branch.");
        ClearPendingDecision();
        var branch = selected.Parameters["branch"];
        switch (r.Operation, branch)
        {
            case ("chiyun", "target") when r.Stage == ShiZhouYuComposition.ChiyunStageTarget:
                ReplaceRuntimeTop(f = f with { ShiZhouYu = r with { Stage = ShiZhouYuComposition.ChiyunStageCards,
                    TargetSeat = selected.Targets.Single(), PickedCards = Array.Empty<int>() } });
                PublishShiZhouYu(GetActiveProgramFrame(f.Id));
                return;
            case ("chiyun", "cancel") when r.Stage == ShiZhouYuComposition.ChiyunStageTarget:
                FinishShiZhouYuOperation(GetActiveProgramFrame(f.Id));
                return;
            case ("chiyun", "pick") when r.Stage == ShiZhouYuComposition.ChiyunStageCards:
                ReplaceRuntimeTop(f = f with { ShiZhouYu = r with { PickedCards = r.PickedCards.Append(selected.Cards.Single()).ToArray() } });
                PublishShiZhouYu(GetActiveProgramFrame(f.Id));
                return;
            case ("chiyun", "unpick") when r.Stage == ShiZhouYuComposition.ChiyunStageCards:
                ReplaceRuntimeTop(f = f with { ShiZhouYu = r with { PickedCards = r.PickedCards.Where(id => id != selected.Cards.Single()).ToArray() } });
                PublishShiZhouYu(GetActiveProgramFrame(f.Id));
                return;
            case ("chiyun", "back") when r.Stage == ShiZhouYuComposition.ChiyunStageCards:
                ReplaceRuntimeTop(f = f with { ShiZhouYu = r with { Stage = ShiZhouYuComposition.ChiyunStageTarget,
                    TargetSeat = -1, PickedCards = Array.Empty<int>() } });
                PublishShiZhouYu(GetActiveProgramFrame(f.Id));
                return;
            case ("chiyun", "confirm") when r.Stage == ShiZhouYuComposition.ChiyunStageCards:
            {
                var ids = r.PickedCards.ToArray();
                if (ids.Length == 0 || ids.Distinct().Count() != ids.Length ||
                    ids.Any(id => _cardZones.GetLocation(id) is not { Zone: CardZoneKind.Hand, OwnerSeat: var owner } || owner != f.OwnerSeat))
                    throw new InvalidOperationException("Chiyun can only give current own hand cards.");
                AdvanceEventRulesAndQueueFact(new ShiZhouYuChiyunGivenEvent(f.Id, r.Source!, f.GameplayHash, _turnNumber,
                    r.TargetSeat, ids, ids.Select(id => IsRedSuit(EffectiveSuit(_players[f.OwnerSeat],
                        _cardZones.CardsAt(CardLocation.Hand(f.OwnerSeat)).Single(card => card.Id == id)))).ToArray()));
                ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect,
                    ShiZhouYu = r with { Stage = ShiZhouYuComposition.ChiyunStageGiveIssued, PickedCards = ids },
                    PendingMovementContinuation = new(r.TargetSeat, 0, null) });
                MoveProgramCardsFromMultipleSources(ids, CardLocation.Hand(r.TargetSeat),
                    new(ShiZhouYuComposition.ChiyunGiveReason));
                return;
            }
            case ("yanhui-show", "show") when r.Stage == ShiZhouYuComposition.YanhuiShowStagePick:
            {
                var cardId = selected.Cards.Single();
                if (_cardZones.GetLocation(cardId) is not { Zone: CardZoneKind.Hand, OwnerSeat: { } holder } || holder != r.TargetSeat)
                    throw new InvalidOperationException("Yanhui can only show a current hand card of its target.");
                var discard = ShiZhouYuCardShownBefore(_turnNumber, cardId);
                AdvanceEventRulesAndQueueFact(new ShiZhouYuYanhuiShownEvent(_turnNumber, r.TargetSeat, cardId, discard));
                if (!discard)
                { FinishShiZhouYuOperation(GetActiveProgramFrame(f.Id)); return; }
                ReplaceRuntimeTop(f = f with { ShiZhouYu = r with { Stage = ShiZhouYuComposition.YanhuiResultDiscardIssued },
                    PendingMovementContinuation = new(r.TargetSeat, 0, null) });
                MoveProgramCardsFromMultipleSources([cardId], CardLocation.DiscardPile,
                    new(ShiZhouYuComposition.YanhuiDiscardReason), disclosure: CardMovementDisclosure.FaceUp);
                // A settled discard batch with no trigger candidates never returns
                // control on its own; drive the program now (zhang-yan pattern).
                ContinueShiZhouYu(f.Id);
                return;
            }
            case ("yanhui-show", "decline") when r.Stage == ShiZhouYuComposition.YanhuiShowStagePick:
                FinishShiZhouYuOperation(GetActiveProgramFrame(f.Id));
                return;
            case ("chiyun-option", "option1") when r.Stage == ShiZhouYuComposition.ChiyunStageOption:
            {
                var target = r.TargetSeat;
                var givenRed = ProgramEventHistory<ShiZhouYuChiyunGivenEvent>().Last(entry =>
                    entry.FrameId == f.Id && entry.Source == r.Source).GivenRed;
                var revealed = _cardZones.CardsAt(CardLocation.Hand(target))
                    .Where(card => givenRed.Contains(IsRedSuit(EffectiveSuit(_players[target], card))))
                    .Select(card => card.Id).ToArray();
                AdvanceEventRulesAndQueueFact(new ShiZhouYuChiyunOptionEvent(f.Id, r.Source!, f.GameplayHash, _turnNumber,
                    target, 1, revealed));
                ReplaceRuntimeTop(f = f with { ShiZhouYu = r with { Stage = ShiZhouYuComposition.ChiyunStageResolveDamage, Option = 1 } });
                if (revealed.Length > 0) CommitProgramHandReveal(f.Id, target, "chiyun-reveal", revealed);
                BeginProgramSkillDamage(GetActiveProgramFrame(f.Id), target, 1, nature: DamageNature.Fire);
                return;
            }
            case ("chiyun-option", "option2") when r.Stage == ShiZhouYuComposition.ChiyunStageOption:
            {
                var target = r.TargetSeat;
                AdvanceEventRulesAndQueueFact(new ShiZhouYuChiyunOptionEvent(f.Id, r.Source!, f.GameplayHash, _turnNumber,
                    target, 2, Array.Empty<int>()));
                DrawProgramCards(f.Id, f.OwnerSeat, 2, null, null, SkillProgramCardSetVisibility.Private,
                    new(ShiZhouYuComposition.ChiyunDrawReason));
                ReplaceRuntimeTop(f = f with { ShiZhouYu = r with { Stage = ShiZhouYuComposition.ChiyunStageFinished, Option = 2 } });
                SetProgramChainedState(GetActiveProgramFrame(f.Id), true, target);
                FinishShiZhouYuOperation(GetActiveProgramFrame(f.Id));
                return;
            }
            case ("yanhui-ending", "damage") when r.Stage == ShiZhouYuComposition.YanhuiStageEndingChoice:
            {
                var seat = selected.Targets.Single();
                AdvanceEventRulesAndQueueFact(new ShiZhouYuYanhuiEndingChosenEvent(f.Id, r.Source!, f.GameplayHash, _turnNumber,
                    1, seat, 0));
                ReplaceRuntimeTop(f = f with { ShiZhouYu = r with { Stage = ShiZhouYuComposition.YanhuiStageEndingDamage,
                    TargetSeat = seat, Option = 1 } });
                BeginProgramSkillDamage(GetActiveProgramFrame(f.Id), seat, 1, nature: DamageNature.Fire);
                return;
            }
            case ("yanhui-ending", "draw") when r.Stage == ShiZhouYuComposition.YanhuiStageEndingChoice:
            {
                var draw = ShiZhouYuYanhuiShownPlayers(_turnNumber);
                AdvanceEventRulesAndQueueFact(new ShiZhouYuYanhuiEndingChosenEvent(f.Id, r.Source!, f.GameplayHash, _turnNumber,
                    2, null, draw));
                DrawProgramCards(f.Id, f.OwnerSeat, draw, null, null, SkillProgramCardSetVisibility.Private,
                    new(ShiZhouYuComposition.YanhuiDrawReason));
                FinishShiZhouYuOperation(GetActiveProgramFrame(f.Id));
                return;
            }
            case ("fentao", "amplify") when r.Stage == ShiZhouYuComposition.FentaoStageChoice:
                AdvanceEventRulesAndQueueFact(new ShiZhouYuFentaoChosenEvent(f.Id, r.Source!, f.GameplayHash, _turnNumber,
                    r.AttackResolutionId, r.TargetSeat, 1, Array.Empty<int>()));
                FinishShiZhouYuOperation(GetActiveProgramFrame(f.Id));
                return;
            case ("fentao", "discard") when r.Stage == ShiZhouYuComposition.FentaoStageChoice:
                ReplaceRuntimeTop(f = f with { ShiZhouYu = r with { Stage = ShiZhouYuComposition.FentaoStageDiscardPick,
                    PickedCards = Array.Empty<int>() } });
                PublishShiZhouYu(GetActiveProgramFrame(f.Id));
                return;
            case ("fentao", "pick") when r.Stage == ShiZhouYuComposition.FentaoStageDiscardPick:
                ReplaceRuntimeTop(f = f with { ShiZhouYu = r with { PickedCards = r.PickedCards.Append(selected.Cards.Single()).ToArray() } });
                PublishShiZhouYu(GetActiveProgramFrame(f.Id));
                return;
            case ("fentao", "unpick") when r.Stage == ShiZhouYuComposition.FentaoStageDiscardPick:
                ReplaceRuntimeTop(f = f with { ShiZhouYu = r with { PickedCards = r.PickedCards.Where(id => id != selected.Cards.Single()).ToArray() } });
                PublishShiZhouYu(GetActiveProgramFrame(f.Id));
                return;
            case ("fentao", "confirm") when r.Stage == ShiZhouYuComposition.FentaoStageDiscardPick:
            {
                var target = _players[r.TargetSeat];
                var required = (GetHand(target).Count + GetEquipment(target).Count + 1) / 2;
                var ids = r.PickedCards.ToArray();
                if (ids.Length != required || ids.Distinct().Count() != ids.Length ||
                    ids.Any(id => _cardZones.GetLocation(id).OwnerSeat != r.TargetSeat))
                    throw new InvalidOperationException("Fentao must discard exactly half of the current cards.");
                AdvanceEventRulesAndQueueFact(new ShiZhouYuFentaoChosenEvent(f.Id, r.Source!, f.GameplayHash, _turnNumber,
                    r.AttackResolutionId, r.TargetSeat, 2, ids));
                ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect,
                    ShiZhouYu = r with { Stage = ShiZhouYuComposition.FentaoStageDiscardIssued },
                    PendingMovementContinuation = new(r.TargetSeat, 0, null) });
                MoveProgramLogicalDiscard(GetActiveProgramFrame(f.Id), r.TargetSeat, ids,
                    new(ShiZhouYuComposition.FentaoDiscardReason));
                // Same settled-batch rule: drive the program after the discard lands.
                ContinueShiZhouYu(f.Id);
                return;
            }
            case ("xiongzi", "keep1") or ("xiongzi", "keep2") when r.Stage == ShiZhouYuComposition.XiongziStageChoice:
            {
                var kept = branch == "keep1" ? 1 : 2;
                AdvanceEventRulesAndQueueFact(new ShiZhouYuXiongziActivatedEvent(f.Id, r.Source!, f.GameplayHash, _turnNumber,
                    f.OwnerSeat, kept));
                DrawProgramCards(f.Id, f.OwnerSeat, 2, null, null, SkillProgramCardSetVisibility.Private,
                    new(ShiZhouYuComposition.XiongziDrawReason));
                ReplaceRuntimeTop(f = f with { ShiZhouYu = r with { Stage = ShiZhouYuComposition.XiongziStageFinished, Option = kept } });
                FinishShiZhouYuOperation(GetActiveProgramFrame(f.Id));
                return;
            }
            case ("xiongzi", "cancel"):
                FinishShiZhouYuOperation(GetActiveProgramFrame(f.Id));
                return;
            default:
                throw new InvalidOperationException("Unknown Shi Zhou Yu choice branch.");
        }
    }

    // ---- instruction bodies without published choices ------------------------------

    private void RunShiZhouYuFentaoRechain(ProgramSkillFrame f)
    {
        var r = f.ShiZhouYu!;
        var target = r.TargetSeat;
        AdvanceEventRulesAndQueueFact(new ShiZhouYuFentaoRechainedEvent(f.Id, r.Source!, f.GameplayHash, _turnNumber, target));
        if (_players[target].IsAlive)
        {
            SetProgramChainedState(GetActiveProgramFrame(f.Id), true, target);
            AddLog("ChainDamage", $"焚涛令 {_players[target].Name} 于伤害结算后再次横置。", f.OwnerSeat, target);
        }
        FinishShiZhouYuOperation(GetActiveProgramFrame(f.Id));
    }

    // ---- damage amplification --------------------------------------------------------

    // 焚涛 option 1: every chain-propagated segment of the amplified fire damage carries one extra damage.
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 67800)]
    private int ShiZhouYuFentaoAmplification(IDamageAttempt attack) =>
        HasShiZhouYuCapability && attack.IsChainPropagation &&
        ProgramEventHistory<ShiZhouYuFentaoChosenEvent>().Any(entry => entry.Option == 1 &&
            entry.AttackResolutionId == attack.ResolutionId)
            ? 1 : 0;

    // ---- runtime hooks -----------------------------------------------------------------

    [ProgramContinuation(ProgramContinuationKind.Resume, 67800)]
    private bool ResumeShiZhouYu(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiZhouYu: not null } f || f.Id != id) return false;
        ContinueShiZhouYu(id);
        return true;
    }

    [ProgramContinuation(ProgramContinuationKind.Movement, 67800)]
    private bool ReturnShiZhouYuMovement(ProgramSkillFrame f) => f.ShiZhouYu is not null && ResumeShiZhouYu(f.Id);

    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 67800)]
    private bool ShiZhouYuMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) =>
        f.ShiZhouYu is { Stage: ShiZhouYuComposition.ChiyunStageGiveIssued or
            ShiZhouYuComposition.FentaoStageDiscardIssued or ShiZhouYuComposition.YanhuiResultDiscardIssued } r &&
        p.CoverageResultBind is null && p.BeforeCount == 0 && p.SubjectSeat == r.TargetSeat;

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 67800)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 67800)]
    private bool ContinuePaidShiZhouYu(ProgramSkillFrame f) => f.ShiZhouYu is not null;

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 67800)]
    private void AssertShiZhouYu(ProgramSkillFrame f)
    {
        if (f.ShiZhouYu is not { } r) return;
        if (r.ActualTurn <= 0 || r.Source?.OwnerSeat != f.OwnerSeat ||
            r.Operation is not ("chiyun" or "chiyun-option" or "yanhui-show" or
                "yanhui-ending" or "fentao" or "fentao-rechain" or "xiongzi"))
            throw new InvalidOperationException("Shi Zhou Yu lost its original typed operation receipt.");
        if (r.Operation is "yanhui-show" && (f.SkillId != ShiZhouYuYanhui ||
                f.WindowContext?.CardUse is null))
            throw new InvalidOperationException("Yanhui lost its finalized-targets window receipt.");
        if (r.Operation == "fentao" && (f.SkillId != ShiZhouYuFentao ||
                f.WindowContext is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied, TargetSeat: { } damaged } ||
                damaged != r.TargetSeat || r.AttackResolutionId <= 0))
            throw new InvalidOperationException("Fentao lost its chained fire-damage window receipt.");
        if (r.Operation == "chiyun" && (f.SkillId != ShiZhouYuChiyun ||
                f.WindowContext is not { Window: SkillProgramTriggerWindow.CardsGained }))
            throw new InvalidOperationException("Chiyun lost its gained-cards window receipt.");
    }

    // ---- AI choices -----------------------------------------------------------------------

    [ProgramAiChoice(SkillProgramEffectOp.ResolveShiZhouYuChiyun, SkillProgramEffectOp.ResolveShiZhouYuChiyunOption,
        SkillProgramEffectOp.ResolveShiZhouYuYanhuiShow, SkillProgramEffectOp.ResolveShiZhouYuYanhuiEnding,
        SkillProgramEffectOp.ResolveShiZhouYuFentao, SkillProgramEffectOp.ResolveShiZhouYuXiongzi)]
    private PromptChoice ChooseShiZhouYuAi(PendingDecision decision, ProgramSkillFrame f)
    {
        var r = f.ShiZhouYu!;
        if (r.Operation == "chiyun" && r.Stage == ShiZhouYuComposition.ChiyunStageTarget)
            return decision.Choices.FirstOrDefault(choice => choice.Parameters["branch"] == "target" &&
                !AreProgramDistributionAllies(_players[f.OwnerSeat], _players[choice.Targets.Single()])) ??
                decision.Choices.First();
        if (r.Operation == "chiyun" && r.Stage == ShiZhouYuComposition.ChiyunStageCards)
        {
            var confirm = decision.Choices.FirstOrDefault(choice => choice.Parameters["branch"] == "confirm");
            var pick = decision.Choices.Where(choice => choice.Parameters["branch"] == "pick")
                .OrderBy(choice => GetKeepValue(GetHand(_players[f.OwnerSeat]).Single(card => card.Id == choice.Cards[0]),
                    _players[f.OwnerSeat])).ThenBy(choice => choice.Cards[0]).FirstOrDefault();
            return pick ?? confirm ?? decision.Choices.First(choice => choice.Parameters["branch"] == "back");
        }
        if (r.Operation == "chiyun-option")
            return decision.Choices.FirstOrDefault(choice => choice.Parameters["branch"] == "option2") ??
                decision.Choices.First();
        if (r.Operation == "yanhui-show")
            return decision.Choices.Where(choice => choice.Parameters["branch"] == "show")
                .OrderBy(choice => GetKeepValue(GetHand(_players[r.TargetSeat]).Single(card => card.Id == choice.Cards[0]),
                    _players[r.TargetSeat])).ThenBy(choice => choice.Cards[0]).FirstOrDefault() ??
                decision.Choices.First();
        if (r.Operation == "yanhui-ending")
            return decision.Choices.FirstOrDefault(choice => choice.Parameters["branch"] == "damage" &&
                !AreProgramDistributionAllies(_players[f.OwnerSeat], _players[choice.Targets.Single()])) ??
                decision.Choices.First();
        if (r.Operation == "fentao" && r.Stage == ShiZhouYuComposition.FentaoStageChoice)
            return decision.Choices.FirstOrDefault(choice => choice.Parameters["branch"] == "amplify") ??
                decision.Choices.First();
        if (r.Operation == "fentao")
        {
            var confirm = decision.Choices.FirstOrDefault(choice => choice.Parameters["branch"] == "confirm");
            var pick = decision.Choices.Where(choice => choice.Parameters["branch"] == "pick")
                .OrderBy(choice => choice.Cards[0]).FirstOrDefault();
            return confirm ?? pick ?? decision.Choices.First();
        }
        return decision.Choices.FirstOrDefault(choice => choice.Parameters["branch"] == "keep1") ??
            decision.Choices.First();
    }
}
