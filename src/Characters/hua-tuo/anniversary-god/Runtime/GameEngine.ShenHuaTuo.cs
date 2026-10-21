using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static CardConversionSource ShenHtSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private Card ShenHtCard(int id) => _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(c => c.Id == id);
    [ProgramCapability]
    private sealed class ShenHuaTuoCapability(GameEngine e) : IShenHuaTuoHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => e.BeginShenHuaTuo(f, op); }
    private SkillProgramStepOutcome BeginShenHuaTuo(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.ShenHuaTuo is not null || f.TriggerId is not null || f.SelectedTargetSeats.Count != 1)
            throw new InvalidOperationException("Shen Hua Tuo requires its accepted original active occurrence.");
        var target = f.SelectedTargetSeats.Single();
        var r = new ShenHuaTuoReceipt(ShenHtSource(f), f.GameplayHash, f.InstructionIndex, target,
            op == SkillProgramEffectOp.ResolveShenHuaTuoLuxin ? ShenHuaTuoStage.LuxinChoice : ShenHuaTuoStage.RestoreChildren,
            Count: Math.Min(5, Math.Max(0, _roundNumber)));
        if (op == SkillProgramEffectOp.ResolveShenHuaTuoLuxin)
        {
            if (f.SelectedCardIds is not [var card] || f.SelectedCardPaymentResult is not { Completed: true, Operation: SkillProgramEffectOp.GiveSelected } ||
                f.SelectedCardPayment is not { MovementCommitted: true, CardIds: [var paidCard] } || paidCard != card)
                throw new InvalidOperationException("Luxin requires its already-completed real GiveSelected payment.");
            r = r with { GiftId = card, GiftKind = ProgramBasicCardName(ShenHtCard(card).Kind) };
        }
        ReplaceRuntimeTop(f = f with { ShenHuaTuo = r });
        if (op == SkillProgramEffectOp.ResolveShenHuaTuoHuandao && _players[target].IsAlive)
        {
            SetProgramTargetFaceState(f.Id, f.OwnerSeat, target, false);
            SetProgramChainedState(f, false, target);
        }
        ContinueShenHuaTuo(f.Id); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> ShenHtChoices(ProgramSkillFrame f)
    {
        var r = f.ShenHuaTuo!;
        PromptChoice Choice(string key, string label) => new(new("shen-hua-tuo." + f.Id + "." + key), label, [], [r.TargetSeat],
            new Dictionary<string, string> { ["program-action"] = "shen-hua-tuo", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["option"] = key });
        if (r.Stage == ShenHuaTuoStage.LuxinChoice)
            return [Choice("draw", "令其摸" + r.Count + "张牌"), Choice("discard", "令其随机弃置" + r.Count + "张手牌")];
        if (r.Stage == ShenHuaTuoStage.AcceptSkill)
            return ShenHtGeneralCandidates(r.TargetSeat).Length == 0 ? [] : [Choice("accept", "获得随机一名同名武将的随机一个技能，然后失去一个其他技能"), Choice("decline", "不获得技能")];
        if (r.Stage == ShenHuaTuoStage.LoseSkill)
            return ShenHtLossSkillIds(r).Select(s => Choice("lose:" + s, "失去【" + _contentRegistry.GetSkill(s).Name + "】")).ToArray();
        return [];
    }
    private void PublishShenHt(ProgramSkillFrame f)
    {
        var r = f.ShenHuaTuo!; var skill = _contentRegistry.GetSkill(f.SkillId); var choices = ShenHtChoices(f);
        var chooser = r.Stage == ShenHuaTuoStage.LuxinChoice ? f.OwnerSeat : r.TargetSeat;
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, skill.Name + "：请选择。", [], [r.TargetSeat], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("shen-hua-tuo")]
    private void AnswerShenHt(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenHuaTuo: { } r } f ||
            _pendingDecision?.PlayerSeat != (r.Stage == ShenHuaTuoStage.LuxinChoice ? f.OwnerSeat : r.TargetSeat) ||
            !ShenHtChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Shen Hua Tuo requires its exact original chooser and current option.");
        AssertShenHt(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var key = choice.Parameters["option"];
        if (r.Stage == ShenHuaTuoStage.LuxinChoice)
        {
            if (key == "draw")
            {
                ReplaceRuntimeTop(f = f with { ShenHuaTuo = r with { Stage = ShenHuaTuoStage.Drawing, HpDelta = 1 } });
                BeginOperationProgramDraw(f, r.TargetSeat, r.Count, new("skill.shen-hua-tuo.luxin.draw")); return;
            }
            var pool = GetHand(_players[r.TargetSeat]).ToList();
            var chosen = new List<int>();
            while (pool.Count > 0 && chosen.Count < r.Count)
            { var i = _random.Next(pool.Count); chosen.Add(pool[i].Id); pool.RemoveAt(i); }
            var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { ShenHuaTuo = r with { Stage = ShenHuaTuoStage.DiscardChildren, HpDelta = -1,
                DiscardedIds = chosen, SequenceBefore = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            if (chosen.Count > 0) MoveProgramLogicalDiscard(f, r.TargetSeat, chosen, new("skill.shen-hua-tuo.luxin.random-discard"));
            f = GetActiveProgramFrame(f.Id);
            var actual = _cardMovements.Where(m => m.Sequence > before && m.From == CardLocation.Hand(r.TargetSeat) &&
                GetProgramDiscardSource(m) == CardLocation.Hand(r.TargetSeat)).Select(m => m.CardId).Distinct().ToArray();
            ReplaceRuntimeTop(f = f with { ShenHuaTuo = f.ShenHuaTuo! with { SequenceAfter = _movementSequence, DiscardedIds = actual } });
            ArmShenHtLuxin(f, actual); ContinueShenHuaTuo(f.Id); return;
        }
        if (r.Stage == ShenHuaTuoStage.AcceptSkill)
        {
            if (key == "decline") { FinishShenHt(f); return; }
            AcquireShenHtRandomSkill(f); return;
        }
        if (r.Stage == ShenHuaTuoStage.LoseSkill)
        {
            RevokeShenHtSkill(f, key[5..]); return;
        }
        throw new InvalidOperationException("Shen Hua Tuo cannot repay an issued child.");
    }
    private void ArmShenHtLuxin(ProgramSkillFrame f, IReadOnlyList<int> ids)
    {
        var r = f.ShenHuaTuo!;
        if (ids.Any(id => ProgramBasicCardName(ShenHtCard(id).Kind) == r.GiftKind))
            AdvanceEventRulesAndQueueFact(new ShenHuaTuoLuxinArmedEvent(f.Id, f.OwnerSeat, r.TargetSeat, r.HpDelta));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveShenHuaTuoLuxin)]
    private void ReturnShenHtDraw(ProgramSkillFrame supplied, NativeDrawOutcome outcome)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.ShenHuaTuo!;
        if (r.Stage != ShenHuaTuoStage.Drawing || outcome.RecipientSeat != r.TargetSeat || outcome.RequestedCount != r.Count || f.NativeDraw is not null)
            throw new InvalidOperationException("Luxin lost its once-issued exact real draw return.");
        ReplaceRuntimeTop(f = f with { ShenHuaTuo = r with { Stage = ShenHuaTuoStage.DrawChildren, DrawOutcome = outcome,
            SequenceBefore = outcome.SequenceBefore, SequenceAfter = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        ArmShenHtLuxin(f, outcome.ActualDrawIds); ContinueShenHuaTuo(f.Id);
    }
    private string[] ShenHtGeneralCandidates(int seat)
    {
        var target = _players[seat];
        var families = new[] { target.General.Id, target.SecondaryGeneral?.Id }.OfType<string>()
            .Select(id => ShenHtFamily(_contentRegistry.Generals[id])).ToHashSet(StringComparer.Ordinal);
        return _contentRegistry.Generals.Values.Where(g => !g.Id.StartsWith("standard:", StringComparison.Ordinal) &&
                families.Contains(ShenHtFamily(g)) && g.SkillIds.Count > 0 && g.SkillIds.All(s => _contentRegistry.Skills.ContainsKey(s)))
            .Select(g => g.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
    private static string ShenHtFamily(ContentGeneralDefinition g)
    {
        var key = g.Id[(g.Id.IndexOf(':') + 1)..];
        if (g.FactionId == "god" && key.StartsWith("shen-", StringComparison.Ordinal)) key = key[5..];
        return (g.CharacterId ?? "character:" + key) switch {
            "character:gao-da-yi-hao" => "character:zhao-yun", "character:fu-huanghou" => "character:fu-huang-hou",
            "character:wolong-zhuge-liang" => "character:zhuge-liang", var family => family };
    }
    private void AcquireShenHtRandomSkill(ProgramSkillFrame f)
    {
        var r = f.ShenHuaTuo!; var target = _players[r.TargetSeat]; var owned = OwnedAndRestorableRandomSkillIds(target);
        var generals = ShenHtGeneralCandidates(r.TargetSeat);
        if (generals.Length == 0) { FinishShenHt(f); return; }
        var before = _random.State; var general = generals[_random.Next(generals.Length)]; var afterGeneral = _random.State;
        var skills = _contentRegistry.Generals[general].SkillIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var selected = skills[_random.Next(skills.Length)]; var afterSkill = _random.State;
        var sourceId = "acquired:shen-hua-tuo.huandao:" + f.Id + ":" + target.Seat; var added = !owned.Contains(selected);
        var grantId = added ? sourceId + ":" + selected : null;
        ReplaceRuntimeTop(f = f with { ShenHuaTuo = r with { Stage = ShenHuaTuoStage.SkillChildren, GeneralCandidates = generals,
            SkillCandidates = skills, SelectedGeneralId = general, SelectedSkillId = selected, RandomBefore = before,
            RandomAfterGeneral = afterGeneral, RandomAfterSkill = afterSkill, GrantedGrantId = grantId, GrantAdded = added } });
        AdvanceEventRulesAndQueueFact(new ShenHuaTuoRandomGeneralEvent(f.Id, target.Seat, general));
        AdvanceEventRulesAndQueueFact(new ShenHuaTuoRandomSkillEvent(f.Id, target.Seat, general, selected));
        if (!added)
        {
            AdvanceEventRulesAndQueueFact(new ShenHuaTuoSkillUnchangedEvent(f.Id, target.Seat, general, selected));
            ContinueShenHuaTuo(f.Id); return;
        }
        foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
        if (!target.SkillGrants.Grant(new(grantId!, selected, grantId!, sourceId))) throw new InvalidOperationException("Huandao cannot grant twice.");
        RegisterTaggedConversionSkill(target, selected);
        AdvanceEventRulesAndQueueFact(new ShenHuaTuoSkillGrantedEvent(f.Id, target.Seat, general, selected, grantId!));
        AdvanceEventRulesAndQueueFact(new SkillsAcquiredEvent(target.Seat, f.SkillId, Array.AsReadOnly(new[] { selected })));
        ContinueShenHuaTuo(f.Id);
    }
    private IEnumerable<SkillGrant> ShenHtRevocableGrants(int seat, string id) => OwnedAndRestorableRoundSkillGrants(_players[seat], id)
        .Where(g => !g.IsModeRule && !g.SourceId.StartsWith("equipment:", StringComparison.Ordinal))
        .DistinctBy(g => (g.GrantId, g.SkillInstanceId, g.SourceId));
    private string[] ShenHtLossSkillIds(ShenHuaTuoReceipt r) => OwnedAndRestorableRandomSkillIds(_players[r.TargetSeat])
        .Where(s => s != r.SelectedSkillId && ShenHtRevocableGrants(r.TargetSeat, s).Any()).Order(StringComparer.Ordinal).ToArray();
    private void RevokeShenHtSkill(ProgramSkillFrame f, string skill)
    {
        var r = f.ShenHuaTuo!; var target = _players[r.TargetSeat]; var grants = ShenHtRevocableGrants(target.Seat, skill).ToArray();
        if (grants.Length == 0 || skill == r.SelectedSkillId) throw new InvalidOperationException("Huandao lost its chosen other skill.");
        foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
        foreach (var g in grants)
        {
            if (target.SkillGrants.Grants.Any(live => live.GrantId == g.GrantId)) target.SkillGrants.RemoveGrant(g.GrantId);
            AdvanceEventRulesAndQueueFact(new ShenHuaTuoSkillRevokedEvent(f.Id, target.Seat, skill, g.GrantId, g.SkillInstanceId, g.SourceId));
        }
        ReplaceRuntimeTop(f = f with { ShenHuaTuo = r with { Stage = ShenHuaTuoStage.SkillChildren } });
        ContinueShenHuaTuo(f.Id);
    }
    private void ContinueShenHuaTuo(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenHuaTuo: { } r } f || f.Id != id || _pendingDecision is not null) return;
        AssertShenHt(f);
        if (r.Stage == ShenHuaTuoStage.Drawing) return;
        if (r.Stage is ShenHuaTuoStage.DrawChildren or ShenHuaTuoStage.DiscardChildren or ShenHuaTuoStage.RestoreChildren or ShenHuaTuoStage.SkillChildren)
        {
            if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
                TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return;
            f = GetActiveProgramFrame(id); r = f.ShenHuaTuo!;
            if (r.Stage == ShenHuaTuoStage.RestoreChildren)
                ReplaceRuntimeTop(f = f with { ShenHuaTuo = r with { Stage = ShenHuaTuoStage.AcceptSkill } });
            else if (r.Stage == ShenHuaTuoStage.SkillChildren && r.GrantAdded && r.SelectedSkillId is not null &&
                     !ProgramEventHistory<ShenHuaTuoSkillRevokedEvent>().Any(e => e.FrameId == id))
                ReplaceRuntimeTop(f = f with { ShenHuaTuo = r with { Stage = ShenHuaTuoStage.LoseSkill } });
            else { FinishShenHt(f); return; }
        }
        if (!_players[f.ShenHuaTuo!.TargetSeat].IsAlive || _winner != Winner.None || ShenHtChoices(f).Count == 0) { FinishShenHt(f); return; }
        PublishShenHt(f);
    }
    private void FinishShenHt(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f with { ShenHuaTuo = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(f.Id); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 38700)]
    private bool ResumeShenHt(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenHuaTuo: not null } f || f.Id != id) return false; ContinueShenHuaTuo(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 38700)]
    private bool ReturnShenHtMovement(ProgramSkillFrame f)
    { if (f.ShenHuaTuo is null) return false; ContinueShenHuaTuo(f.Id); return true; }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 38700)]
    private bool ShenHtMovementProof(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        f.ShenHuaTuo is { Stage: ShenHuaTuoStage.DrawChildren or ShenHuaTuoStage.DiscardChildren } && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 38700)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 38700)]
    private bool ContinueIssuedShenHt(ProgramSkillFrame f) => f.ShenHuaTuo is not null;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 38700)]
    private void AssertShenHt(ProgramSkillFrame f)
    {
        if (f.ShenHuaTuo is not { } r) return;
        if (r.Source != ShenHtSource(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex ||
            !f.SelectedTargetSeats.SequenceEqual([r.TargetSeat]) || !Enum.IsDefined(r.Stage) || r.Count is < 0 or > 5)
            throw new InvalidOperationException("Shen Hua Tuo lost its accepted source, original frame or recipient.");
        if (r.SelectedGeneralId is not null)
        {
            var random = new DeterministicRandom(unchecked((int)r.RandomBefore));
            if (r.GeneralCandidates.Count == 0 || r.SkillCandidates.Count == 0 || r.SelectedGeneralId != r.GeneralCandidates[random.Next(r.GeneralCandidates.Count)] ||
                random.State != r.RandomAfterGeneral || r.SelectedSkillId != r.SkillCandidates[random.Next(r.SkillCandidates.Count)] || random.State != r.RandomAfterSkill ||
                (r.GrantAdded ? !ProgramEventHistory<ShenHuaTuoSkillGrantedEvent>().Any(e => e.FrameId == f.Id && e.TargetSeat == r.TargetSeat && e.GeneralId == r.SelectedGeneralId && e.SkillId == r.SelectedSkillId && e.GrantId == r.GrantedGrantId) :
                    !ProgramEventHistory<ShenHuaTuoSkillUnchangedEvent>().Any(e => e.FrameId == f.Id && e.TargetSeat == r.TargetSeat && e.GeneralId == r.SelectedGeneralId && e.SkillId == r.SelectedSkillId)))
                throw new InvalidOperationException("Huandao lost its frozen two-stage random samples or once-issued grant.");
        }
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveShenHuaTuoLuxin)]
    private PromptChoice ChooseShenHtLuxin(PendingDecision p, ProgramSkillFrame f) => p.Choices.First();
    [ProgramAiChoice(SkillProgramEffectOp.ResolveShenHuaTuoHuandao)]
    private PromptChoice ChooseShenHtHuandao(PendingDecision p, ProgramSkillFrame f) => p.Choices.First();
}
