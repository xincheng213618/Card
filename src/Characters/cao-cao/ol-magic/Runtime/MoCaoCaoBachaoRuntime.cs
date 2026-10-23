using System.Globalization;
namespace CardGame.Core;

[ProgramRuntimeModule, ProgramCapability]
internal sealed partial class MoCaoCaoBachaoRuntime(IMoCaoCaoFacts facts, IMoCaoCaoOperations operations,
    IProgramRuntimeFrames frames, IProgramRuntimeEvents events, IProgramRuntimePrompts prompts) : IMoCaoCaoBachaoHost
{
    internal const string Action = "mo-cao-cao", GiftReason = "skill-program.ol:mo-bachao.gift", BaibiReason = "equipment.baibi-dao.obtain";
    private static CardConversionSource Source(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId);
    public SkillProgramStepOutcome Start(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = frames.ActiveProgram(supplied.Id);
        if (!facts.IsExactStart(f, operation)) throw new InvalidOperationException("Magic Cao Cao requires the exact native owner/window and original source.");
        if (operation == SkillProgramEffectOp.ResolveMoCaoCaoBaibi)
        {
            var claim = facts.History<MoCaoCaoBaibiIssuedEvent>().Select(e => e.Claim).SingleOrDefault(c => c.DamageWindowId == f.WindowContext!.ParentFrameId && c.InstanceId == f.SkillInstanceId && c.ActorSeat == f.OwnerSeat);
            if (claim is null || facts.WeaponAuthorization(f.OwnerSeat, CardKind.BaiBiDao) is null)
                throw new InvalidOperationException("Baipi requires a real native weapon-ability authorization, never a Fuzai skill grant.");
            frames.ReplaceTop(f = f with { MoCaoCaoBaibi = new(claim) });
        }
        else
        {
            if (facts.Phase is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != f.OwnerSeat)
                throw new InvalidOperationException("Bachao starts only at the owner's actual Play boundary.");
            var seats = facts.Seats.Where(s => s != f.OwnerSeat && facts.Participant(s).Alive)
                .OrderBy(s => DamageTriggerOrdering.GetRelativeSeatOrder(f.OwnerSeat, s, facts.Seats.Count, facts.Seats)).ToArray();
            frames.ReplaceTop(f = f with { MoCaoCaoBachao = new(Source(f), f.GameplayHash, f.WindowContext!.ParentFrameId, phase)
                { Participants = seats, Stage = MoCaoCaoBachaoStage.Commit, Accepted = true } });
            events.AdvanceFact(new MoCaoCaoBachaoStartedEvent(f.Id, Source(f), f.GameplayHash, f.MoCaoCaoBachao!.WindowId, phase));
        }
        Continue(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    internal bool Valid(ProgramSkillFrame f)
    {
        if (facts.Program(f.SkillId)?.GameplayHash != f.GameplayHash || f.ActivationId != f.TriggerId || f.InstructionIndex != 1) return false;
        if (f.MoCaoCaoBaibi is { } b) return f.SkillId == MoCaoCaoComposition.Baibi && b.Claim.ActorSeat == f.OwnerSeat &&
            b.Claim.InstanceId == f.SkillInstanceId && b.Claim.GameplayHash == f.GameplayHash && f.WindowContext?.ParentFrameId == b.Claim.DamageWindowId &&
            facts.History<MoCaoCaoBaibiIssuedEvent>().Count(e => e.Claim == b.Claim) == 1;
        return f.MoCaoCaoBachao is { } r && r.Source == Source(f) && r.GameplayHash == f.GameplayHash && r.Accepted &&
            f.WindowContext?.ParentFrameId == r.WindowId && r.Cursor >= 0 && r.Cursor <= r.Participants.Count &&
            r.Participants.Distinct().Count() == r.Participants.Count && r.Votes.Count <= r.Participants.Count &&
            facts.History<MoCaoCaoBachaoStartedEvent>().Count(e => e == new MoCaoCaoBachaoStartedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, r.Phase)) == 1;
    }
    private int Chooser(ProgramSkillFrame f) => f.MoCaoCaoBachao is { Stage: MoCaoCaoBachaoStage.Commit } r ? r.Participants[r.Cursor] : f.OwnerSeat;
    private PromptChoice Option(ProgramSkillFrame f, string branch, string text, int? target = null) => new(
        new ChoiceId($"mo-cao-cao.{f.Id}.{f.MoCaoCaoBachao?.Stage}.{f.MoCaoCaoBachao?.Cursor}.{branch}.{target}"), text, [], target is { } seat ? [seat] : [],
        new Dictionary<string,string> { ["program-action"] = Action, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
    internal IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        if (f.MoCaoCaoBaibi is { Paid: false } b)
            return WrapPayments(f, f.OwnerSeat, b.Claim.TargetSeat, true).Append(Option(f,"decline","不获得手牌")).ToArray();
        if (f.MoCaoCaoBachao is not { } r) return [];
        if (r.Stage == MoCaoCaoBachaoStage.Commit)
            return WrapPayments(f, r.Participants[r.Cursor], r.Participants[r.Cursor], false).Append(Option(f,"decline","不交牌")).ToArray();
        if (r.Stage == MoCaoCaoBachaoStage.Target)
            return facts.Seats.Where(s => facts.Participant(s).Alive && !r.ProtectedSeats.Contains(s))
                .Select(s => Option(f,"target",s == f.OwnerSeat ? "对自己造成1点伤害，当前手牌本阶段不限次与距离并入魔" : $"对座位{s + 1}造成1点伤害",s))
                .Append(Option(f,"decline","不造成伤害")).ToArray();
        return [];
    }
    private IEnumerable<PromptChoice> WrapPayments(ProgramSkillFrame f, int chooser, int subject, bool handOnly) =>
        facts.PaymentChoices(f, chooser, subject, handOnly).Select(c => c with
        { Parameters = new Dictionary<string,string>(c.Parameters) { ["program-action"] = Action, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = "card" } });
    private void Publish(ProgramSkillFrame f)
    {
        var choices = Choices(f); var chooser = Chooser(f);
        if (choices.Count == 0) { Complete(f); return; }
        frames.ReplaceTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        var skill = facts.Skill(f.SkillId);
        prompts.Set(new PendingDecision(DecisionKind.ProgramTrigger, chooser,
            f.MoCaoCaoBaibi is not null ? "百辟刀：可以获得目标的一张手牌。" : f.MoCaoCaoBachao!.Stage == MoCaoCaoBachaoStage.Commit ? "霸朝：分别密选；全部提交后再结算交牌。" : "霸朝：选择伤害对象，或不造成伤害。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        {
            PromptId = prompts.NextId(), IsPrivate = true, TargetSeat = chooser,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description),
            Choices = Array.AsReadOnly(choices.ToArray())
        }, facts.Participant(chooser).Human);
        operations.PublishState();
    }
    [ProgramAnswer(Action)]
    private void Answer(PromptChoice selected)
    {
        if (frames.Top is not ProgramSkillFrame f || !Valid(f) || prompts.Current?.PlayerSeat != Chooser(f) ||
            Choices(f).FirstOrDefault(c => c.Id == selected.Id) is not { } canonical || !canonical.Cards.SequenceEqual(selected.Cards) ||
            !canonical.Targets.SequenceEqual(selected.Targets) || canonical.Parameters.Count != selected.Parameters.Count || canonical.Parameters.Any(p => selected.Parameters.GetValueOrDefault(p.Key) != p.Value))
            throw new InvalidOperationException("Magic Cao Cao requires the exact current participant's private native choice.");
        prompts.Clear(); frames.ReplaceTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = selected.Parameters["branch"];
        if (f.MoCaoCaoBaibi is { } b)
        {
            if (branch == "decline") { Complete(f); return; }
            var card = facts.ResolveHandCard(f, f.OwnerSeat, selected); var before = facts.MovementSequence;
            frames.ReplaceTop(f = f with { MoCaoCaoBaibi = b with { CardId = card, Paid = true, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat,0,null) });
            var moved = operations.Transfer(f, b.Claim.TargetSeat, card, BaibiReason);
            if (frames.Find(f.Id) is ProgramSkillFrame after)
            {
                frames.Replace(f.Id, after with { MoCaoCaoBaibi = after.MoCaoCaoBaibi! with { After = moved.After } });
                events.AdvanceFact(new MoCaoCaoBaibiObtainedEvent(f.Id,b.Claim.DamageWindowId,f.OwnerSeat,b.Claim.TargetSeat,moved.Before,moved.After,moved.Arrived));
            }
        }
        else if (f.MoCaoCaoBachao is { } r)
        {
            if (r.Stage == MoCaoCaoBachaoStage.Commit)
            {
                var card = branch == "card" ? facts.ResolveHandCard(f, Chooser(f), selected) : 0;
                var vote = new MoCaoCaoVote(r.Participants[r.Cursor],card,card > 0 ? facts.Kind(card) : null);
                frames.ReplaceTop(f = f with { MoCaoCaoBachao = r with { Votes = r.Votes.Append(vote).ToArray(), Cursor = r.Cursor + 1 } });
            }
            else
            {
                if (branch == "decline") { Complete(f); return; }
                var target = selected.Targets.Single();
                frames.ReplaceTop(f = f with { MoCaoCaoBachao = r with { Stage = MoCaoCaoBachaoStage.Damage, TargetSeat = target } });
                events.AdvanceFact(new MoCaoCaoBachaoDamageEvent(f.Id,f.OwnerSeat,target));
                operations.Damage(f,target);
            }
        }
        Continue(f.Id);
    }
    internal void Continue(long id)
    {
        if (prompts.Current is not null || frames.Top is not ProgramSkillFrame f || f.Id != id) return;
        if (!Valid(f)) throw new InvalidOperationException("Magic Cao Cao lost its owning receipt or native source.");
        if (f.MoCaoCaoBaibi is { } b)
        {
            if (!b.Paid) { Publish(f); return; }
            if (operations.Drain(f)) return;
            Complete(frames.ActiveProgram(id)); return;
        }
        while (frames.Top is ProgramSkillFrame { MoCaoCaoBachao: { } r } current && current.Id == id && prompts.Current is null)
        {
            f = current;
            if (facts.Finished) { Complete(f); return; }
            if (r.Stage == MoCaoCaoBachaoStage.Commit)
            {
                if (r.Cursor < r.Participants.Count)
                {
                    if (facts.Participant(r.Participants[r.Cursor]).Alive) { Publish(f); return; }
                    frames.ReplaceTop(f with { MoCaoCaoBachao = r with { Votes = r.Votes.Append(new(r.Participants[r.Cursor],0,null)).ToArray(), Cursor = r.Cursor + 1 } }); continue;
                }
                events.AdvanceFact(new MoCaoCaoBachaoCommittedEvent(f.Id,r.Votes.Count));
                frames.ReplaceTop(f with { MoCaoCaoBachao = r with { Stage = MoCaoCaoBachaoStage.Pay, Cursor = 0 } }); continue;
            }
            if (r.Stage == MoCaoCaoBachaoStage.PaymentChildren)
            {
                if (operations.Drain(f)) return;
                frames.ReplaceTop(f with { MoCaoCaoBachao = r with { Stage = MoCaoCaoBachaoStage.Pay, Cursor = r.Cursor + 1, PaidCardId = 0 }, PendingMovementContinuation = null }); continue;
            }
            if (r.Stage == MoCaoCaoBachaoStage.Pay)
            {
                if (r.Cursor == r.Votes.Count) { frames.ReplaceTop(f with { MoCaoCaoBachao = r with { Stage = MoCaoCaoBachaoStage.Target } }); continue; }
                var vote = r.Votes[r.Cursor]; var location = vote.CardId > 0 ? facts.Location(vote.CardId) : CardLocation.OutsideGame;
                if (vote.CardId <= 0 || !facts.Participant(vote.Seat).Alive || !facts.Participant(f.OwnerSeat).Alive || location.OwnerSeat != vote.Seat || location.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))
                { frames.ReplaceTop(f with { MoCaoCaoBachao = r with { Cursor = r.Cursor + 1 } }); continue; }
                var before = facts.MovementSequence;
                frames.ReplaceTop(f = f with { MoCaoCaoBachao = r with { Stage = MoCaoCaoBachaoStage.PaymentChildren, PaidCardId = vote.CardId, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat,0,null) });
                var moved = operations.Transfer(f,vote.Seat,vote.CardId,GiftReason);
                var nonBasic = CardCatalog.Get(vote.PrintedKind!.Value).CategoryName != "基本牌";
                if (frames.Find(f.Id) is ProgramSkillFrame after)
                    frames.Replace(f.Id, after with { MoCaoCaoBachao = after.MoCaoCaoBachao! with { After = moved.After,
                        ProtectedSeats = moved.Arrived && nonBasic ? after.MoCaoCaoBachao!.ProtectedSeats.Append(vote.Seat).ToArray() : after.MoCaoCaoBachao!.ProtectedSeats } });
                events.AdvanceFact(new MoCaoCaoBachaoGiftEvent(f.Id,vote.Seat,nonBasic,moved.Before,moved.After,moved.Arrived));
                if (frames.Top?.Id != id) return;
                continue;
            }
            if (r.Stage == MoCaoCaoBachaoStage.Target) { Publish(f); return; }
            if (r.Stage == MoCaoCaoBachaoStage.Damage)
            {
                if (f.AttackAttempt is not null) return;
                if (operations.Drain(f)) return;
                frames.ReplaceTop(f with { MoCaoCaoBachao = r with { Stage = MoCaoCaoBachaoStage.DamageChildren } }); continue;
            }
            if (r.TargetSeat == f.OwnerSeat && facts.Participant(f.OwnerSeat).Alive)
            {
                foreach (var card in facts.Hand(f.OwnerSeat)) events.AdvanceFact(new MoCaoCaoHandPrivilegedEvent(f.Id,r.Source,r.GameplayHash,r.Phase,card));
                events.AdvanceFact(new MoCaoCaoBachaoSelfIssuedEvent(f.Id,r.Source,r.GameplayHash,r.Phase));
                operations.EnterRumo(f);
            }
            Complete(f); return;
        }
    }
    private void Complete(ProgramSkillFrame f)
    {
        events.AdvanceFact(new MoCaoCaoCompletedEvent(f.Id,f.SkillId));
        frames.ReplaceTop(f = f with { MoCaoCaoBachao = null, MoCaoCaoBaibi = null, PendingMovementContinuation = null }); operations.Finish(f);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,33800)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,33800)]
    private bool AcceptedContinuation(ProgramSkillFrame f) =>
        (f.MoCaoCaoBachao is not null || f.MoCaoCaoBaibi is not null) ? Valid(f) : RetainIssuedBaibi(f);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,33800)]
    private void AssertReceipt(ProgramSkillFrame f)
    {
        if((f.MoCaoCaoBachao is not null || f.MoCaoCaoBaibi is not null) && !Valid(f))
            throw new InvalidOperationException("Magic Cao Cao lost the exact source, signed window or native payment cursor.");
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 33800)]
    private bool Resume(long id)
    {
        if (frames.Top is not ProgramSkillFrame f || f.Id != id || f.MoCaoCaoBachao is null && f.MoCaoCaoBaibi is null) return false;
        Continue(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 33800)]
    private bool Movement(ProgramSkillFrame f) => (f.MoCaoCaoBachao is not null || f.MoCaoCaoBaibi is not null) && Resume(f.Id);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMoCaoCaoBachao, SkillProgramEffectOp.ResolveMoCaoCaoBaibi)]
    private PromptChoice Ai(PendingDecision pending, ProgramSkillFrame f)
    {
        if (f.MoCaoCaoBaibi is not null) return pending.Choices.First();
        var r = f.MoCaoCaoBachao!;
        if (r.Stage == MoCaoCaoBachaoStage.Commit)
        {
            var cards = pending.Choices.Where(c => c.Parameters["branch"] == "card").ToArray();
            if (cards.Length == 0 || !facts.Friendly(pending.PlayerSeat,f.OwnerSeat)) return pending.Choices.Last();
            return cards.OrderBy(c => facts.KeepValue(pending.PlayerSeat,facts.ResolveHandCard(f,pending.PlayerSeat,c))).First();
        }
        return pending.Choices.FirstOrDefault(c => c.Targets is [var seat] && seat != f.OwnerSeat && !facts.Friendly(f.OwnerSeat,seat)) ?? pending.Choices.Last();
    }
    [ProgramPipelineHook(ProgramPipelinePhase.RumoOrigin, 33800)]
    internal bool PaidRumoOrigin(long id, CardConversionSource source, string hash) => source.SkillId == MoCaoCaoComposition.Bachao &&
        facts.History<MoCaoCaoBachaoSelfIssuedEvent>().Count(e => e.FrameId == id && e.Source == source && e.GameplayHash == hash) == 1 &&
        facts.History<MoCaoCaoBachaoStartedEvent>().Count(e => e.FrameId == id && e.Source == source && e.GameplayHash == hash) == 1 &&
        facts.History<MoCaoCaoBachaoDamageEvent>().Count(e => e.FrameId == id && e.ActorSeat == source.OwnerSeat && e.TargetSeat == source.OwnerSeat) == 1;
    [ProgramPipelineHook(ProgramPipelinePhase.RumoEntry, 33800)]
    internal bool PaidRumoEntry(ProgramSkillFrame f) => Valid(f) && f.MoCaoCaoBachao is { Stage: MoCaoCaoBachaoStage.DamageChildren, TargetSeat: { } target } r &&
        target == f.OwnerSeat && PaidRumoOrigin(f.Id,r.Source,r.GameplayHash);
}
