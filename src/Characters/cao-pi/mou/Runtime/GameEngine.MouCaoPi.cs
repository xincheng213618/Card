using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MouCaoPiDrawReason = "skill-program.mou-cao-pi.xingshang.draw";
    private static CardConversionSource MouCaoPiSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_cao_pi_mou_MouCaoPi.MouCaoPiSource(f);
    private int Song(int seat) => _players[seat].Markers.GetValueOrDefault(PlayerMarkerKind.CaoPiSong);

    [ProgramCapability]
    private sealed class MouCaoPiCapability(GameEngine engine) : IMouCaoPiHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginMouCaoPi(f, op); }

    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 28000,
        SkillProgramEffectOp.MouCaoPiXingshang, SkillProgramEffectOp.MouCaoPiFangzhu, SkillProgramEffectOp.MouCaoPiRemoveWeiSkills)]
    private bool CanActivateMouCaoPi(ProgramActivationLegalityContext c) => c.Owner.IsAlive && _phase == TurnPhase.Play && _currentSeat == c.OwnerSeat &&
        (c.SkillId == "mou:songwei" ? c.Owner.Role == Role.Lord && _players.Any(p => p.IsAlive && p.Seat != c.OwnerSeat && GetEffectiveFactionId(p) == "wei" && MouCaoPiGeneralGrants(p).Any()) :
        HasMouXingshang(c.Owner) && Song(c.OwnerSeat) >= 2 && (c.SkillId != "mou:fangzhu" || _players.Any(p => p.IsAlive && p.Seat != c.OwnerSeat)));

    private SkillProgramStepOutcome BeginMouCaoPi(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MouCaoPi is not null || f.InstructionIndex != 1 || !MouCaoPiComposition.Handles(op))
            throw new InvalidOperationException("Mou Cao Pi requires his exact owning instruction.");
        if (op is SkillProgramEffectOp.MouCaoPiGainSong or SkillProgramEffectOp.MouCaoPiSongwei)
        {
            var gain = op == SkillProgramEffectOp.MouCaoPiGainSong ? 2 : 2 * _players.Count(p => p.Seat != f.OwnerSeat && GetEffectiveFactionId(p) == "wei");
            SetStrategicMarker(f.OwnerSeat, PlayerMarkerKind.CaoPiSong, f.OwnerSeat, Math.Min(9, Song(f.OwnerSeat) + gain), f);
            return SkillProgramStepOutcome.Continue;
        }
        ReplaceRuntimeTop(f = f with { MouCaoPi = new(MouCaoPiSource(f), f.GameplayHash, op, _turnNumber, _cardUseDebitPhaseInstanceId, MouCaoPiStage.Choosing) });
        PublishMouCaoPi(f);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private IEnumerable<SkillGrant> MouCaoPiGeneralGrants(CharacterState p) => p.SkillGrants.Grants.Where(g => g.IsEnabled && !g.SourceId.StartsWith("equipment:", StringComparison.Ordinal));

    private IReadOnlyList<PromptChoice> MouCaoPiChoices(ProgramSkillFrame f)
    {
        if (f.MouCaoPi is not { Stage: MouCaoPiStage.Choosing } r || !_players[f.OwnerSeat].IsAlive) return [];
        var choices = new List<PromptChoice>();
        void Add(string option, string label, int seat, int cost)
        {
            if (cost > Song(f.OwnerSeat)) return;
            choices.Add(new(new($"mou-cao-pi.{f.Id}.{option}.{seat}"), label, [], [seat], new Dictionary<string, string>
            { ["program-action"] = "mou-cao-pi", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["option"] = option, ["cost"] = cost.ToString(CultureInfo.InvariantCulture) }));
        }
        foreach (var p in _players.Where(p => p.IsAlive))
        {
            if (r.Op == SkillProgramEffectOp.MouCaoPiXingshang && HasMouXingshang(_players[f.OwnerSeat]))
            {
                Add("draw", $"2颂：令{p.Name}摸三张牌", p.Seat, 2);
                Add("reset", $"2颂：令{p.Name}复原武将牌", p.Seat, 2);
                if (p.HasHpValue && p.MaxHp <= 9) Add("grow", $"3颂：令{p.Name}回复1点体力、增加1点体力上限并恢复随机废除装备栏", p.Seat, 3);
            }
            else if (r.Op == SkillProgramEffectOp.MouCaoPiFangzhu && p.Seat != f.OwnerSeat && HasMouXingshang(_players[f.OwnerSeat]))
            {
                Add("basic", $"2颂：{p.Name}直到其下回合结束仅能使用基本手牌", p.Seat, 2);
                Add("response", $"4颂：{p.Name}直到其下回合结束不能响应其他角色使用的牌", p.Seat, 4);
                Add("skills", $"6颂：{p.Name}直到其下回合结束武将技能失效", p.Seat, 6);
                Add("trick", $"6颂：{p.Name}直到其下回合结束仅能使用锦囊手牌", p.Seat, 6);
                Add("turn-over", $"8颂：令{p.Name}翻面", p.Seat, 8);
                Add("equipment", $"8颂：{p.Name}直到其下回合结束仅能使用装备手牌", p.Seat, 8);
            }
            else if (r.Op == SkillProgramEffectOp.MouCaoPiRemoveWeiSkills && p.Seat != f.OwnerSeat && GetEffectiveFactionId(p) == "wei" && MouCaoPiGeneralGrants(p).Any())
                Add("remove", $"颂威：令{p.Name}永久失去所有武将技能（每局限一次）", p.Seat, 0);
        }
        if (r.Op == SkillProgramEffectOp.MouCaoPiXingshang && HasMouXingshang(_players[f.OwnerSeat]) && HasPrintedMouXingshang(_players[f.OwnerSeat]))
            foreach (var dead in _players.Where(p => !p.IsAlive && MouCaoPiPrintedSkills(p).Length > 0))
                Add("inherit", $"4颂：追思{dead.General.Name}，获得其非主公技并失去行殇、放逐、颂威", dead.Seat, 4);
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishMouCaoPi(ProgramSkillFrame f)
    {
        var choices = MouCaoPiChoices(f);
        if (choices.Count == 0) { FinishMouCaoPi(f); return; }
        PublishParticipantHandChoice(f, f.OwnerSeat, choices, $"{_contentRegistry.GetSkill(f.SkillId).Name}：当前有{Song(f.OwnerSeat)}个颂，选择操作与目标。", f.OwnerSeat);
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mou-cao-pi")]
    private void AnswerMouCaoPi(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouCaoPi: { Stage: MouCaoPiStage.Choosing } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([choice], MouCaoPiChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Mou Cao Pi lost the exact live menu and original private chooser.");
        AssertMouCaoPi(f); ClearPendingDecision();
        var option = choice.Parameters["option"]; var cost = int.Parse(choice.Parameters["cost"], CultureInfo.InvariantCulture);
        var target = choice.Targets.Single(); var before = Song(f.OwnerSeat);
        ReplaceRuntimeTop(f = f with { MouCaoPi = r with { TargetSeat = target, Option = option, Cost = cost, MarkerBefore = before, MarkerAfter = before - cost,
            Stage = option == "draw" ? MouCaoPiStage.DrawRequest : option == "grow" ? MouCaoPiStage.RecoveryChildren : MouCaoPiStage.StateChildren,
            MovementBefore = _movementSequence, MovementAfter = _movementSequence }, PendingMovementContinuation = new(target, 0, null) });
        SetStrategicMarker(f.OwnerSeat, PlayerMarkerKind.CaoPiSong, f.OwnerSeat, before - cost, f);
        AdvanceEventRulesAndQueueFact(new MouCaoPiPaidEvent(f.Id, f.OwnerSeat, target, option, cost, before, before - cost));
        var recipient = _players[target];
        if (option == "draw")
        {
            BeginOperationProgramDraw(f, target, 3, new(MouCaoPiDrawReason)); return;
        }
        else if (option == "grow") new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, target, 1, null, null);
        else if (option == "reset") { SetProgramTargetFaceState(f.Id, f.OwnerSeat, target, false); SetProgramChainedState(f, false, target); }
        else if (option == "turn-over") TurnOverProgramTarget(f.Id, f.OwnerSeat, target);
        else if (option == "remove") LoseMouCaoPiSkills(recipient, f.OwnerSeat, null);
        else if (option == "inherit")
        {
            AcquireRuntimeSkills(_players[f.OwnerSeat], "mou:xingshang", MouCaoPiPrintedSkills(recipient));
            LoseMouCaoPiSkills(_players[f.OwnerSeat], f.OwnerSeat, ["mou:xingshang", "mou:fangzhu", "mou:songwei"]);
            AdvanceEventRulesAndQueueFact(new MouCaoPiInheritedEvent(f.Id, f.OwnerSeat, target, recipient.General.Id));
        }
        else
        {
            var kind = option switch { "basic" => MouCaoPiControlKind.BasicHandOnly, "response" => MouCaoPiControlKind.CannotRespond,
                "skills" => MouCaoPiControlKind.SkillsInvalid, "trick" => MouCaoPiControlKind.TrickHandOnly, "equipment" => MouCaoPiControlKind.EquipmentHandOnly,
                _ => throw new InvalidOperationException("Unknown Fangzhu option.") };
            var control = new MouCaoPiControl(f.Id, f.OwnerSeat, target, _turnNumber, kind);
            _mouCaoPiControls.Add(control);
            _currentTurnSkillSuppressionRevision++;
            AdvanceEventRulesAndQueueFact(new MouCaoPiControlGrantedEvent(control));
        }
        AddLog("SkillTriggered", $"{_players[f.OwnerSeat].Name} 发动【{_contentRegistry.GetSkill(f.SkillId).Name}】：{choice.Label}。", f.OwnerSeat, target);
        ContinueMouCaoPi(f.Id);
    }
    private void LoseMouCaoPiSkills(CharacterState p, int source, IReadOnlyList<string>? ids)
    {
        var lost = MouCaoPiGeneralGrants(p).Where(g => ids is null || ids.Contains(g.SkillId, StringComparer.Ordinal)).ToArray();
        foreach (var g in lost) p.SkillGrants.SetEnabled(g.GrantId, false);
        AdvanceEventRulesAndQueueFact(new CharacterSkillsLostEvent(p.Seat, source, Array.AsReadOnly(lost.Select(g => g.SkillId).Distinct(StringComparer.Ordinal).ToArray())));
    }
    private bool DrainMouCaoPi(ProgramSkillFrame f) => TryBeginAdvancedSkillsChanged(f.Id) || TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 550)]
    private bool IsMouCaoPiMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation continuation) =>
        f.MouCaoPi is { Stage: not MouCaoPiStage.Choosing } r && effect?.Op == r.Op && continuation == f.PendingMovementContinuation &&
        continuation.SubjectSeat == r.TargetSeat && continuation.BeforeCount == 0 && continuation.CoverageResultBind is null &&
        ProgramEventHistory<MouCaoPiPaidEvent>().Any(e => e.FrameId == f.Id && e.TargetSeat == r.TargetSeat && e.Option == r.Option && e.Cost == r.Cost);
    private void ContinueMouCaoPi(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouCaoPi: { } r } f || f.Id != id) return;
        AssertMouCaoPi(f);
        if (f.NativeDraw is not null) return;
        if (r.Stage == MouCaoPiStage.Choosing) { if (_pendingDecision is null) PublishMouCaoPi(f); return; }
        if (DrainMouCaoPi(f)) return;
        f = GetActiveProgramFrame(id); r = f.MouCaoPi!;
        var target = _players[r.TargetSeat];
        if (_winner != Winner.None || !target.IsAlive && r.Option != "inherit") { FinishMouCaoPi(f); return; }
        if (r.Stage == MouCaoPiStage.RecoveryChildren)
        {
            ReplaceRuntimeTop(f = f with { MouCaoPi = r with { Stage = MouCaoPiStage.MaximumChildren } });
            target.MaxHp = checked(target.MaxHp + 1);
            AdvanceEventRulesAndQueueFact(new MaximumHpChangedEvent(target.Seat, 1, target.MaxHp, f.SkillId));
            ContinueMouCaoPi(id); return;
        }
        if (r.Stage == MouCaoPiStage.MaximumChildren)
        {
            ReplaceRuntimeTop(f = f with { MouCaoPi = r with { Stage = MouCaoPiStage.StateChildren } });
            var slots = Enum.GetValues<EquipmentSlot>().Where(s => target.EquipmentSlotCapacity(s) == 0).ToArray();
            if (slots.Length > 0) RestoreEquipmentSlot(target, slots[_random.Next(slots.Length)]);
            ContinueMouCaoPi(id); return;
        }
        FinishMouCaoPi(f);
    }
    private void FinishMouCaoPi(ProgramSkillFrame f)
    {
        var r = f.MouCaoPi!; var applied = r.Stage != MouCaoPiStage.Choosing;
        AdvanceEventRulesAndQueueFact(new MouCaoPiBenefitCompletedEvent(f.Id, f.OwnerSeat, r.TargetSeat, r.Option));
        ReplaceRuntimeTop(f = f with { MouCaoPi = null, PendingMovementContinuation = null }); FinishProgramSkill(f, applied);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 28000)] private bool ResumeMouCaoPi(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouCaoPi: not null } f || f.Id != id) return false; ContinueMouCaoPi(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 28000)] private bool ReturnMouCaoPi(ProgramSkillFrame f)
    { if (f.MouCaoPi is null) return false; ContinueMouCaoPi(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28000)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28000)]
    private bool ContinuePaidMouCaoPi(ProgramSkillFrame f) => f.MouCaoPi is { Stage: not MouCaoPiStage.Choosing };
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 28000)]
    private void AssertMouCaoPi(ProgramSkillFrame f)
    {
        if (f.MouCaoPi is not { } r) return;
        var expected = f.SkillId switch { "mou:xingshang" => SkillProgramEffectOp.MouCaoPiXingshang,
            "mou:fangzhu" => SkillProgramEffectOp.MouCaoPiFangzhu, "mou:songwei" => SkillProgramEffectOp.MouCaoPiRemoveWeiSkills,
            _ => (SkillProgramEffectOp)(-1) };
        if (r.Source != MouCaoPiSource(f) || r.GameplayHash != f.GameplayHash || f.InstructionIndex != 1 ||
            r.ActualTurn != _turnNumber || r.PhaseInstance != _cardUseDebitPhaseInstanceId || r.Op != expected || !Enum.IsDefined(r.Stage) ||
            f.TriggerId is not null || f.WindowContext is not null || f.ActivationId != "menu" ||
            r.MovementBefore < 0 || r.MovementAfter < r.MovementBefore || r.MovementAfter > _movementSequence || r.ActualDraw is < 0 or > 3)
            throw new InvalidOperationException("Mou Cao Pi lost the original source, phase or owning instruction.");
        if (r.Stage != MouCaoPiStage.Choosing && (!IsValidPlayerSeat(r.TargetSeat) || r.MarkerBefore > 9 || r.MarkerAfter != r.MarkerBefore - r.Cost || r.MarkerAfter < 0 ||
            ProgramEventHistory<MouCaoPiPaidEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var paid] ||
            paid != new MouCaoPiPaidEvent(f.Id, f.OwnerSeat, r.TargetSeat, r.Option, r.Cost, r.MarkerBefore, r.MarkerAfter)))
            throw new InvalidOperationException("Mou Cao Pi cannot resume an unproved or twice-paid benefit.");
    }
    [ProgramAiChoice(SkillProgramEffectOp.MouCaoPiXingshang, SkillProgramEffectOp.MouCaoPiFangzhu, SkillProgramEffectOp.MouCaoPiRemoveWeiSkills)]
    private PromptChoice ChooseAiMouCaoPi(PendingDecision p, ProgramSkillFrame f)
    {
        var view = CreateSnapshot(f.OwnerSeat);
        double Score(PromptChoice c)
        {
            var target = _players[c.Targets.Single()]; var op = c.Parameters["option"];
            var support = _aiBrains[f.OwnerSeat].ScoreProgramTarget(view, target.Seat, new(1, 0, 0, 0, 0, 0, false, false));
            var hostility = _aiBrains[f.OwnerSeat].ScoreProgramTarget(view, target.Seat, new(0, 0, 0, 0, 0, 1, false, false));
            return op switch { "draw" => support * 3, "grow" => support * (target.Hp < target.MaxHp ? 5 : 2),
                "reset" => support * (target.IsFaceDown ? 5 : target.IsChained ? 1 : 0) - 100,
                "inherit" => -500, "remove" or "skills" => hostility * Math.Max(1, MouCaoPiGeneralGrants(target).Count()) * 3,
                "turn-over" => (target.IsFaceDown ? support : hostility) * 2, "response" => hostility * 2,
                _ => hostility * 1.5 };
        }
        return p.Choices.OrderByDescending(Score).ThenBy(c => int.Parse(c.Parameters["cost"], CultureInfo.InvariantCulture)).First();
    }
}
