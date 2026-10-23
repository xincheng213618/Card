using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string HappyGjHuishi = "happy:huishi", HappyGjTianyi = "happy:tianyi", HappyGjGift = "happy:huishi-gift", HappyGjZuoxing = "happy:zuoxing";
    [ProgramCapability] private sealed class HappyGuoJiaHost(GameEngine e) : IHappyGuoJiaHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => e.BeginHappyGuoJia(f, op); }
    private CardConversionSource HappyGjSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 40900, SkillProgramEffectOp.ResolveHappyGuoJiaHuishi)]
    private bool CanHappyGjHuishi(CharacterState owner) => owner.HasHpValue && owner.MaxHp < 10;
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 40901, SkillProgramEffectOp.ResolveHappyGuoJiaHuishiGrant)]
    private bool CanHappyGjGift(CharacterState owner) => owner.HasHpValue && _players.Any(p => HappyGjGiftTarget(owner, p.Seat));
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 40902, SkillProgramEffectOp.ResolveHappyGuoJiaZuoxing)]
    private bool CanHappyGjZuoxing(CharacterState owner) => HappyGjBenefactors(owner, GetRuntimeSkillInstanceId(owner, HappyGjZuoxing)).Any() && HappyGjPlans(owner.Seat).Count > 0;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.Collection, 40900, SkillProgramEffectOp.ResolveHappyGuoJiaTianyi)]
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 40900, SkillProgramEffectOp.ResolveHappyGuoJiaTianyi)]
    private bool CanAwakenHappyGj(ProgramTriggerLegalityContext g) => HasShenGuoJiaAwakeningCondition(g.OwnerSeat, g.SkillId, g.Candidate.BindingId) ||
        _players.Where(p => p.IsAlive).All(p => ProgramEventHistory<DamageAppliedEvent>().Any(d => d.TargetSeat == p.Seat && d.Amount > 0));
    private bool HappyGjGiftTarget(CharacterState owner, int seat) => IsValidPlayerSeat(seat) && _players[seat].IsAlive &&
        (ShenGuoJiaUnawakened(seat).Count > 0 ? owner.MaxHp >= _players.Count(p => p.IsAlive) : owner.MaxHp > 2);
    private IEnumerable<int> HappyGjBenefactors(CharacterState owner, string instance) => ProgramEventHistory<HappyGuoJiaBenefactorIssuedEvent>()
        .Where(e => e.Recipient == owner.Seat && e.GrantedInstance == instance && IsValidPlayerSeat(e.Source.OwnerSeat) && _players[e.Source.OwnerSeat] is { IsAlive: true, HasHpValue: true, MaxHp: > 1 } &&
            owner.SkillGrants.Grants.Any(g =>
                g.IsEnabled && g.SkillId == HappyGjZuoxing && g.SkillInstanceId == instance && g.SourceId == "acquired:" + e.GrantSource))
        .Select(e => e.Source.OwnerSeat).Distinct();
    private IReadOnlyList<ShenGuoJiaPlan> HappyGjPlans(int owner)
    {
        var starts = ProgramEventHistory<ShenGuoJiaStartedEvent>().Where(e => e.Source is { SkillId: HappyGjZuoxing } &&
            e.Source.OwnerSeat == owner && e.Turn == _turnNumber && e.TurnOwner == _turnProgression.OwnerSeat).Select(e => e.FrameId).ToHashSet();
        var used = ProgramEventHistory<ShenGuoJiaNativeIssuedEvent>().Where(e => starts.Contains(e.FrameId)).Select(e => e.Kind).ToHashSet();
        return Array.AsReadOnly(ShenGuoJiaPlans(owner).Where(p => !used.Contains(p.Kind)).ToArray());
    }
    private SkillProgramStepOutcome BeginHappyGuoJia(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (op == SkillProgramEffectOp.ResolveHappyGuoJiaTianyi)
        {
            if (f.SkillId != HappyGjTianyi || f.TriggerId != "all-living-damaged" || f.SelectedTargetSeats is not [var seat] || !_players[seat].IsAlive)
                throw new InvalidOperationException("Happy Tianyi lost its real awakening recipient.");
            var grant = $"happy-guo-jia:{f.OwnerSeat}:{f.SkillInstanceId}:{f.Id}";
            AcquireRuntimeSkills(_players[seat], grant, [HappyGjZuoxing]);
            AdvanceEventRulesAndQueueFact(new HappyGuoJiaBenefactorIssuedEvent(f.Id, HappyGjSource(f), seat, GetRuntimeSkillInstanceId(_players[seat], HappyGjZuoxing), grant));
            AdvanceEventRulesAndQueueFact(new SkillAwakenedEvent(f.OwnerSeat, f.SkillId, _players[f.OwnerSeat].MaxHp, Array.AsReadOnly(new[] { HappyGjZuoxing })));
            return SkillProgramStepOutcome.Continue;
        }
        if (f.HappyGuoJia is not null || f.TriggerId is not null || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            f.InstructionIndex != (op == SkillProgramEffectOp.ResolveHappyGuoJiaHuishi ? 2 : 1))
            throw new InvalidOperationException("Happy Guo Jia requires its original unpaid zero-input activation.");
        var stage = op switch
        {
            SkillProgramEffectOp.ResolveHappyGuoJiaHuishi when f.SkillId == HappyGjHuishi => HappyGuoJiaStage.GrowChildren,
            SkillProgramEffectOp.ResolveHappyGuoJiaHuishiGrant when f.SkillId == HappyGjGift => HappyGuoJiaStage.Target,
            SkillProgramEffectOp.ResolveHappyGuoJiaZuoxing when f.SkillId == HappyGjZuoxing => HappyGuoJiaStage.UseChoice,
            _ => throw new InvalidOperationException("Happy Guo Jia lost its exact operation source.")
        };
        var r = new HappyGuoJiaReceipt(HappyGjSource(f), f.GameplayHash, op, _turnNumber, _turnProgression.OwnerSeat, stage);
        if (op == SkillProgramEffectOp.ResolveHappyGuoJiaHuishi)
        {
            var cards = GetProgramCardSet(f, "huishi-five").CardIds;
            r = r with { Cards = cards, SuitCount = cards.Select(id => GetAttackCard(id).Suit).Where(s => s != Suit.None).Distinct().Count() };
        }
        ReplaceRuntimeTop(f = f with { HappyGuoJia = r });
        AdvanceEventRulesAndQueueFact(new ShenGuoJiaStartedEvent(f.Id, r.Source, f.GameplayHash, op, r.Turn, r.TurnOwner, 0, -1));
        if (op == SkillProgramEffectOp.ResolveHappyGuoJiaHuishi)
        {
            f = HappyGjChildren(f, HappyGuoJiaStage.GrowChildren);
            PayHappyGjMaximum(f, f.OwnerSeat, Math.Max(0, Math.Min(r.SuitCount, 10 - _players[f.OwnerSeat].MaxHp)), "happy-huishi-grow");
            ContinueHappyGuoJia(f.Id);
        }
        else PublishHappyGj(f);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private PromptChoice HappyGjChoice(ProgramSkillFrame f, string branch, string label, IReadOnlyList<int>? targets = null, string? value = null) =>
        new(new($"happy-guo-jia.{f.Id}.{f.HappyGuoJia!.Stage}.{branch}.{value}"), label, [], targets ?? [], new Dictionary<string, string>
        { ["program-action"] = "happy-guo-jia", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch, ["value"] = value ?? "" });
    private IReadOnlyList<PromptChoice> HappyGjChoices(ProgramSkillFrame f)
    {
        var r = f.HappyGuoJia!; var choices = new List<PromptChoice>();
        if (r.Stage == HappyGuoJiaStage.Recipient)
        {
            foreach (var p in _players.Where(p => p.IsAlive)) choices.Add(HappyGjChoice(f, "recipient", $"将全部展示牌交给{p.Name}", [p.Seat], p.Seat.ToString(CultureInfo.InvariantCulture)));
            choices.Add(HappyGjChoice(f, "decline", "不交给角色，将展示牌置入弃牌堆"));
        }
        if (r.Stage == HappyGuoJiaStage.Target)
            foreach (var p in _players.Where(p => HappyGjGiftTarget(_players[f.OwnerSeat], p.Seat))) choices.Add(HappyGjChoice(f, "target", $"对{p.Name}发动辉逝", [p.Seat], p.Seat.ToString(CultureInfo.InvariantCulture)));
        if (r.Stage == HappyGuoJiaStage.Awakening)
            foreach (var s in ShenGuoJiaUnawakened(r.Target)) choices.Add(HappyGjChoice(f, "awakening", $"令{_players[r.Target].Name}满足【{_contentRegistry.GetSkill(s.SkillId).Name}】觉醒条件", [r.Target], s.SkillId));
        if (r.Stage == HappyGuoJiaStage.UseChoice)
        {
            var plans = HappyGjPlans(f.OwnerSeat);
            foreach (var benefactor in HappyGjBenefactors(_players[f.OwnerSeat], f.SkillInstanceId))
                for (var i = 0; i < plans.Count; i++) choices.Add(HappyGjChoice(f, "use", $"令{_players[benefactor].Name}减1点体力上限：{CardCatalog.Get(plans[i].Kind).DisplayName}（{string.Join('、', plans[i].Targets.Select(s => _players[s].Name))}）", plans[i].Targets, $"{benefactor}:{i}"));
            choices.Add(HappyGjChoice(f, "cancel", "取消发动"));
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishHappyGj(ProgramSkillFrame f)
    {
        var choices = HappyGjChoices(f);
        if (choices.Count == 0) { FinishHappyGj(f); return; }
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        PublishParticipantHandChoice(f, f.OwnerSeat, choices, $"【{_contentRegistry.GetSkill(f.SkillId).Name}】请选择。", f.OwnerSeat);
    }
    [ProgramAnswer("happy-guo-jia")]
    private void AnswerHappyGuoJia(PromptChoice c)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HappyGuoJia: { } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !ValidHappyGj(f) || !AssistedChoicesEqual([c], HappyGjChoices(f).Where(x => x.Id == c.Id).ToArray()))
            throw new InvalidOperationException("Happy Guo Jia requires its exact current owner menu.");
        ClearPendingDecision(); var branch = c.Parameters["branch"];
        if (r.Stage == HappyGuoJiaStage.Recipient)
        { MoveHappyGjRevealed(f, branch == "decline" ? -1 : c.Targets.Single()); return; }
        if (r.Stage == HappyGuoJiaStage.Target)
        {
            var target = c.Targets.Single();
            if (!HappyGjGiftTarget(_players[f.OwnerSeat], target)) throw new InvalidOperationException("Happy Hui Shi lost its actual branch eligibility.");
            var awaken = ShenGuoJiaUnawakened(target).Count > 0;
            ReplaceRuntimeTop(f = f with { HappyGuoJia = r with { Target = target, Stage = awaken ? HappyGuoJiaStage.Awakening : HappyGuoJiaStage.Drawing } });
            if (awaken) PublishHappyGj(f);
            else BeginOperationProgramDraw(f, target, 4, new("skill-program.happy:huishi-gift.draw"));
            return;
        }
        if (r.Stage == HappyGuoJiaStage.Awakening)
        {
            var skill = ShenGuoJiaUnawakened(r.Target).Single(s => s.SkillId == c.Parameters["value"]);
            AdvanceEventRulesAndQueueFact(new ShenGuoJiaAwakeningOverrideEvent(f.Id, r.Source, r.Target, skill.SkillId, skill.SkillInstanceId, skill.Program.GameplayHash));
            f = HappyGjChildren(f, HappyGuoJiaStage.CostChildren); PayHappyGjMaximum(f, f.OwnerSeat, -2, "happy-huishi-gift"); ContinueHappyGuoJia(f.Id); return;
        }
        if (r.Stage != HappyGuoJiaStage.UseChoice) throw new InvalidOperationException("Happy Guo Jia's choice was already paid.");
        if (branch == "cancel") { FinishHappyGj(f); return; }
        var parts = c.Parameters["value"].Split(':');
        var benefactor = int.Parse(parts[0], CultureInfo.InvariantCulture); var plan = HappyGjPlans(f.OwnerSeat)[int.Parse(parts[1], CultureInfo.InvariantCulture)];
        ReplaceRuntimeTop(f = f with { HappyGuoJia = r with { Plan = plan, Benefactor = benefactor } });
        f = HappyGjChildren(f, HappyGuoJiaStage.CostChildren); PayHappyGjMaximum(f, benefactor, -1, "happy-zuoxing"); ContinueHappyGuoJia(f.Id);
    }
    private ProgramSkillFrame HappyGjChildren(ProgramSkillFrame f, HappyGuoJiaStage stage)
    {
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { HappyGuoJia = f.HappyGuoJia! with { Stage = stage, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); return f;
    }
    private void PayHappyGjMaximum(ProgramSkillFrame f, int seat, int amount, string purpose)
    {
        var p = _players[seat]; var before = p.MaxHp; var hp = p.Hp;
        var after = HasWushuangTactic(seat, 2020) ? 7 : Math.Max(0, checked(before + amount)); p.MaxHp = after; p.Hp = Math.Min(hp, after);
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { HappyGuoJia = f.HappyGuoJia! with { MaximumBefore = before, MaximumAfter = after } });
        AdvanceEventRulesAndQueueFact(new MaximumHpChangedEvent(seat, after - before, after, f.SkillId) { HpBeforeClamp = hp });
        AdvanceEventRulesAndQueueFact(new ShenGuoJiaMaximumPaidEvent(f.Id, ProgramEventHistory<ShenGuoJiaMaximumPaidEvent>().Count(e => e.FrameId == f.Id), seat, before, after, purpose));
        if (after == 0 && p.IsAlive) BeginPlayerDeath(f.Id, p, null, null, null, causingProgramSkillFrameId: f.Id);
    }
    private void MoveHappyGjRevealed(ProgramSkillFrame f, int target)
    {
        var ids = f.HappyGuoJia!.Cards.Where(id => _cardZones.GetLocation(id) == CardLocation.Processing).ToArray();
        ReplaceRuntimeTop(f = f with { HappyGuoJia = f.HappyGuoJia with { Target = target } });
        f = HappyGjChildren(f, target < 0 ? HappyGuoJiaStage.CleanupChildren : HappyGuoJiaStage.GainChildren);
        MoveProgramCardsFromMultipleSources(ids, target < 0 ? CardLocation.DiscardPile : CardLocation.Hand(target),
            new(target < 0 ? "skill-program.happy:huishi.cleanup" : "skill-program.happy:huishi.give"),
            target < 0 ? null : NativeGiveCompletionRecords(f.Id, f.OwnerSeat, target, ids, _movementSequence, (_, _) => { }));
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { HappyGuoJia = f.HappyGuoJia! with { After = _movementSequence } }); ContinueHappyGuoJia(f.Id);
    }
    private void ContinueHappyGuoJia(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { HappyGuoJia: { } r } f || f.Id != id) return;
        if (r.Stage is HappyGuoJiaStage.Drawing or HappyGuoJiaStage.IssuingUse or HappyGuoJiaStage.NativeUse or HappyGuoJiaStage.ReturnedUse) return;
        if (DrainShenGuoJia(f)) return;
        f = GetActiveProgramFrame(id); r = f.HappyGuoJia!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (r.Stage == HappyGuoJiaStage.GrowChildren)
        {
            if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None || !r.Cards.Any(c => _cardZones.GetLocation(c) == CardLocation.Processing)) { MoveHappyGjRevealed(f, -1); return; }
            ReplaceRuntimeTop(f = f with { HappyGuoJia = r with { Stage = HappyGuoJiaStage.Recipient } }); PublishHappyGj(f); return;
        }
        if (r.Stage == HappyGuoJiaStage.GainChildren && _players[f.OwnerSeat].IsAlive && IsValidPlayerSeat(r.Target) && _players[r.Target].IsAlive &&
            GetHand(_players[r.Target]).Count == _players.Where(p => p.IsAlive).Max(p => GetHand(p).Count))
        { f = HappyGjChildren(f, HappyGuoJiaStage.CostChildren); PayHappyGjMaximum(f, f.OwnerSeat, -1, "happy-huishi-most-hand"); ContinueHappyGuoJia(id); return; }
        if (r.Stage == HappyGuoJiaStage.DrawChildren)
        { f = HappyGjChildren(f, HappyGuoJiaStage.CostChildren); PayHappyGjMaximum(f, f.OwnerSeat, -2, "happy-huishi-gift"); ContinueHappyGuoJia(id); return; }
        if (r.Stage == HappyGuoJiaStage.CostChildren && r.Operation == SkillProgramEffectOp.ResolveHappyGuoJiaZuoxing && _players[f.OwnerSeat].IsAlive && _winner == Winner.None)
        { IssueHappyGuoJiaUse(f); return; }
        if (r.Stage is HappyGuoJiaStage.GainChildren or HappyGuoJiaStage.CleanupChildren or HappyGuoJiaStage.CostChildren) { FinishHappyGj(f); return; }
        PublishHappyGj(f);
    }
    private void FinishHappyGj(ProgramSkillFrame f)
    {
        AdvanceEventRulesAndQueueFact(new ShenGuoJiaCompletedEvent(f.Id));
        ReplaceRuntimeTop(f = f with { HappyGuoJia = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 40900)]
    private bool ResumeHappyGuoJia(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HappyGuoJia: not null } f || f.Id != id) return false; ContinueHappyGuoJia(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 40900)]
    private bool ReturnHappyGjMovement(ProgramSkillFrame f) => f.HappyGuoJia is not null && ResumeHappyGuoJia(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 40900)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 40900)]
    private bool ContinuePaidHappyGj(ProgramSkillFrame f) => f.HappyGuoJia is not null && ValidHappyGj(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveHappyGuoJiaHuishi, SkillProgramEffectOp.ResolveHappyGuoJiaHuishiGrant, SkillProgramEffectOp.ResolveHappyGuoJiaZuoxing)]
    private PromptChoice HappyGjAi(PendingDecision p, ProgramSkillFrame f) => p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") is "recipient" or "target" && c.Targets.Contains(f.OwnerSeat)) ?? p.Choices[0];
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveHappyGuoJiaHuishiGrant)]
    private void ReturnHappyGjDraw(ProgramSkillFrame supplied, NativeDrawOutcome outcome)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.HappyGuoJia!;
        if (!ValidHappyGj(f) || r.Stage != HappyGuoJiaStage.Drawing || f.NativeDraw is not null || outcome.RecipientSeat != r.Target || outcome.RequestedCount != 4 || outcome.SequenceAfter != _movementSequence)
            throw new InvalidOperationException("Happy Hui Shi lost its exact native four-card return.");
        ReplaceRuntimeTop(f = f with { HappyGuoJia = r with { Stage = HappyGuoJiaStage.DrawChildren, DrawOutcome = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); ContinueHappyGuoJia(f.Id);
    }
}
