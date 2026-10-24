using System.Globalization;
namespace CardGame.Core;
[ProgramRuntimeModule, ProgramCapability]
internal sealed class MouZhangXiuAnniversaryRuntime(IMouZhangXiuAnniversaryFacts facts, IMouZhangXiuAnniversaryNative native, IProgramRuntimeFrames frames, IProgramRuntimeEvents events, IProgramRuntimePrompts prompts, IProgramOperationDraws draws) : IMouZhangXiuAnniversaryRuntime
{
    internal const string Fuxi = "mou:fuxi", Haoyi = "mou:haoyi", Action = "mou-zhang-xiu-anniversary";
    internal const string GiveReason = "skill-program.mou:fuxi.give", DiscardReason = "skill-program.mou:fuxi.discard", DrawReason = "skill-program.mou:fuxi.draw", ClaimReason = "skill-program.mou:haoyi.claim", DistributionReason = "skill-program.mou:haoyi.distribute";
    internal static CardConversionSource Source(ProgramSkillFrame f) => new(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId);
    // Current physical card names classify discard entities; past converted
    // faces do not turn their printed materials into new damage-card names.
    internal static bool DamageCard(CardKind k) => k is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.ColdSlash or CardKind.Duel or CardKind.FireAttack or CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.UnexpectedAssault or CardKind.AgainstCurrent or CardKind.WumiaoFlood or CardKind.Lightning;
    private IReadOnlyList<MouZhangXiuAnniversaryCard> Claimable() => facts.Entities(facts.History<MouZhangXiuAnniversaryDiscardEvent>().Where(e => e.Turn == facts.Turn).Select(e => e.CardId).Distinct()).Where(c => c.From == CardLocation.DiscardPile && DamageCard(c.Kind) && !facts.History<MouZhangXiuAnniversaryDamageEvent>().Any(e => e.Turn == facts.Turn && e.CardId == c.Id && e.Amount > 0)).ToArray();
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 36600, SkillProgramEffectOp.ResolveMouZhangXiuFuxi, SkillProgramEffectOp.ResolveMouZhangXiuHaoyi)]
    private bool Qualifies(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w)
    {
        if (t.Effects.Single().Op == SkillProgramEffectOp.ResolveMouZhangXiuHaoyi) return Claimable().Count > 0;
        var target = facts.PlayActor(w);
        return target >= 0 && target != c.OwnerSeat && facts.Alive(target) && facts.Largest(target) && (facts.Cards(c.OwnerSeat, c.OwnerSeat, false).Count > 0 || facts.Cards(c.OwnerSeat, target, true).Count > 0);
    }
    public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = frames.ActiveProgram(supplied.Id);
        if (f.MouZhangXiuAnniversary is not null || f.InstructionIndex != 1 || !facts.Parent(f)) throw new InvalidOperationException("Mou Zhang Xiu requires his actual owning native phase window.");
        var fuxi = op == SkillProgramEffectOp.ResolveMouZhangXiuFuxi;
        var target = fuxi ? facts.PlayActor(f.WindowContext!) : -1;
        if (fuxi && (target == f.OwnerSeat || target < 0 || !facts.Largest(target))) throw new InvalidOperationException("Fuxi requires the actual other Play actor with a largest current Hand.");
        var r = new MouZhangXiuAnniversaryReceipt(Source(f), f.GameplayHash, op, f.WindowContext!.ParentFrameId, facts.Turn) { Stage = fuxi ? MouZhangXiuAnniversaryStage.Branch : MouZhangXiuAnniversaryStage.ObtainChildren, Target = target, Before = facts.Sequence, After = facts.Sequence, Cards = fuxi ? [] : Claimable() };
        frames.ReplaceTop(f = f with { MouZhangXiuAnniversary = r }); events.AdvanceFact(new MouZhangXiuAnniversaryStartedEvent(f.Id, r.Source, r.GameplayHash, op, r.ParentId, r.Turn));
        if (!fuxi) StartMove(f, r); else Continue(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    internal bool Valid(ProgramSkillFrame f) => f.MouZhangXiuAnniversary is { } r && r.Source == Source(f) && r.GameplayHash == f.GameplayHash && facts.Program(f.SkillId).GameplayHash == f.GameplayHash && facts.Parent(f) && f.InstructionIndex == 1 && r.ParentId == f.WindowContext?.ParentFrameId && r.Turn == facts.Turn && Enum.IsDefined(r.Stage) && r.Index is >= 0 and <= 1 && r.Count == 1 && r.Before >= 0 && r.After >= r.Before && r.After <= facts.Sequence && r.Cards.Select(c => c.Id).Distinct().Count() == r.Cards.Count && r.Gained.Distinct().Count() == r.Gained.Count && r.Given.All(r.Gained.Contains) && facts.History<MouZhangXiuAnniversaryStartedEvent>().Count(e => e == new MouZhangXiuAnniversaryStartedEvent(f.Id, r.Source, r.GameplayHash, r.Operation, r.ParentId, r.Turn)) == 1;
    private IReadOnlyList<MouZhangXiuAnniversaryCard> Distributable(ProgramSkillFrame f) => facts.Entities(f.MouZhangXiuAnniversary!.Gained.Except(f.MouZhangXiuAnniversary.Given)).Where(c => c.From == CardLocation.Hand(f.OwnerSeat)).ToArray();
    private PromptChoice Choice(ProgramSkillFrame f, string branch, string text, int? card = null, int? target = null) => new(new($"mou-zhang-xiu-anniversary.{f.Id}.{f.MouZhangXiuAnniversary!.Stage}.{branch}.{card}.{target}"), text, card is { } id ? [id] : [], target is { } seat ? [seat] : [], new Dictionary<string, string> { ["program-action"] = Action, ["branch"] = branch });
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        var r = f.MouZhangXiuAnniversary!;
        if (r.Stage == MouZhangXiuAnniversaryStage.Branch)
        {
            var options = new List<PromptChoice>();
            if (facts.Alive(r.Target) && facts.Cards(f.OwnerSeat, f.OwnerSeat, false).Count > 0) options.Add(Choice(f, "give", "交给其一张牌，然后摸两张牌"));
            if (facts.Alive(r.Target) && facts.Cards(f.OwnerSeat, r.Target, true).Count > 0) options.Add(Choice(f, "discard", "弃置其一张牌，然后视为对其使用杀"));
            return options;
        }
        if (r.Stage == MouZhangXiuAnniversaryStage.GiveCard) return facts.Cards(f.OwnerSeat, f.OwnerSeat, false).Select(c => Choice(f, "card", "交给其" + c.Label, c.Id)).ToArray();
        if (r.Stage == MouZhangXiuAnniversaryStage.DiscardCard)
        {
            var cards = facts.Cards(f.OwnerSeat, r.Target, true); var options = new List<PromptChoice>();
            if (cards.Any(c => c.From.Zone == CardZoneKind.Hand)) options.Add(Choice(f, "hand", "弃置其一张随机手牌"));
            options.AddRange(cards.Where(c => c.From.Zone == CardZoneKind.Equipment).Select(c => Choice(f, "card", "弃置" + c.Label, c.Id))); return options;
        }
        if (r.Stage == MouZhangXiuAnniversaryStage.DistributionCard) return Distributable(f).Select(c => Choice(f, "card", "分配" + c.Label, c.Id)).Append(Choice(f, "stop", "保留剩余牌，结束分配")).ToArray();
        if (r.Stage == MouZhangXiuAnniversaryStage.DistributionTarget) return facts.Others(f.OwnerSeat).Select(t => Choice(f, "recipient", "交给" + facts.Name(t), target: t)).Append(Choice(f, "back", "重新选择分配牌")).ToArray();
        return [];
    }
    private void Publish(ProgramSkillFrame f)
    {
        var choices = Choices(f); if (choices.Count == 0) { Complete(f); return; }
        frames.ReplaceTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        prompts.Set(new PendingDecision(DecisionKind.ProgramTrigger, f.OwnerSeat, f.SkillId == Fuxi ? "附袭：选择执行的选项及其真实牌。" : "豪义：可将本次实际获得的牌分配给其他角色。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat) { PromptId = prompts.NextId(), IsPrivate = true, Choices = Array.AsReadOnly(choices.ToArray()) }, facts.Human(f.OwnerSeat)); native.PublishState();
    }
    [ProgramAnswer(Action)] private void Answer(PromptChoice supplied)
    {
        if (frames.Top is not ProgramSkillFrame { MouZhangXiuAnniversary: { } r } f || !Valid(f) || prompts.Current?.PlayerSeat != f.OwnerSeat || Choices(f).SingleOrDefault(c => c.Id == supplied.Id) is not { } canonical || !canonical.Cards.SequenceEqual(supplied.Cards) || !canonical.Targets.SequenceEqual(supplied.Targets) || canonical.Parameters.Count != supplied.Parameters.Count || canonical.Parameters.Any(p => supplied.Parameters.GetValueOrDefault(p.Key) != p.Value)) throw new InvalidOperationException("Mou Zhang Xiu requires the current exact private material or recipient choice.");
        prompts.Clear(); frames.ReplaceTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect }); var branch = supplied.Parameters["branch"];
        if (branch == "stop") { Complete(f); return; }
        if (r.Stage == MouZhangXiuAnniversaryStage.Branch) { frames.ReplaceTop(f with { MouZhangXiuAnniversary = r with { Stage = branch == "give" ? MouZhangXiuAnniversaryStage.GiveCard : MouZhangXiuAnniversaryStage.DiscardCard } }); Continue(f.Id); return; }
        if (r.Stage is MouZhangXiuAnniversaryStage.GiveCard or MouZhangXiuAnniversaryStage.DiscardCard)
        {
            var discard = r.Stage == MouZhangXiuAnniversaryStage.DiscardCard; var cards = facts.Cards(f.OwnerSeat, discard ? r.Target : f.OwnerSeat, discard);
            var card = branch == "hand" ? native.RandomHand(f, cards.Where(c => c.From.Zone == CardZoneKind.Hand).ToArray()) : cards.Single(c => c.Id == supplied.Cards.Single());
            StartMove(f, r with { Stage = discard ? MouZhangXiuAnniversaryStage.DiscardChildren : MouZhangXiuAnniversaryStage.GiveChildren, Cards = [card] }); return;
        }
        if (r.Stage == MouZhangXiuAnniversaryStage.DistributionCard) { frames.ReplaceTop(f with { MouZhangXiuAnniversary = r with { Stage = MouZhangXiuAnniversaryStage.DistributionTarget, Selected = supplied.Cards.Single() } }); Continue(f.Id); return; }
        if (branch == "back") { frames.ReplaceTop(f with { MouZhangXiuAnniversary = r with { Stage = MouZhangXiuAnniversaryStage.DistributionCard, Selected = 0 } }); Continue(f.Id); return; }
        var selected = Distributable(f).Single(c => c.Id == r.Selected);
        StartMove(f, r with { Stage = MouZhangXiuAnniversaryStage.DistributionChildren, Recipient = supplied.Targets.Single(), Cards = [selected] });
    }
    private void StartMove(ProgramSkillFrame f, MouZhangXiuAnniversaryReceipt r)
    {
        frames.ReplaceTop(f = f with { MouZhangXiuAnniversary = r with { Before = facts.Sequence, After = facts.Sequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (r.Cards.Count > 0) native.Move(f); Continue(f.Id);
    }
    internal void MovesPaid(long id, IReadOnlyList<CardMovementRecord> moves)
    {
        var f = frames.ActiveProgram(id); var r = f.MouZhangXiuAnniversary!;
        if (!Valid(f) || moves.Count != r.Cards.Count || moves.Any(m => !r.Cards.Any(c => c.Id == m.CardId && c.Kind == m.CardKind && c.From == m.From) || m.Sequence <= r.Before)) throw new InvalidOperationException("Mou Zhang Xiu lost his exact one-time native movement receipt.");
        var gained = r.Stage == MouZhangXiuAnniversaryStage.ObtainChildren ? moves.Where(m => m.To == CardLocation.Hand(f.OwnerSeat)).Select(m => m.CardId).ToArray() : r.Gained;
        var given = r.Stage == MouZhangXiuAnniversaryStage.DistributionChildren ? r.Given.Concat(moves.Select(m => m.CardId)).Distinct().ToArray() : r.Given;
        frames.Replace(id, f with { MouZhangXiuAnniversary = r with { After = moves.Max(m => m.Sequence), Gained = gained, Given = given } });
        foreach (var m in moves) events.AdvanceFact(new MouZhangXiuAnniversaryMovedEvent(f.Id, r.Stage, m.CardId, m.From, m.To, m.Sequence));
    }
    internal void Continue(long id)
    {
        while (prompts.Current is null && frames.Top is ProgramSkillFrame { MouZhangXiuAnniversary: { } r } f && f.Id == id)
        {
            if (!Valid(f)) throw new InvalidOperationException("Mou Zhang Xiu lost his original native phase, paid movement or typed return.");
            if (facts.Finished) { Complete(f); return; }
            if (r.Stage is MouZhangXiuAnniversaryStage.Branch or MouZhangXiuAnniversaryStage.GiveCard or MouZhangXiuAnniversaryStage.DiscardCard or MouZhangXiuAnniversaryStage.DistributionCard or MouZhangXiuAnniversaryStage.DistributionTarget)
            {
                if (!facts.Alive(f.OwnerSeat) || r.Stage == MouZhangXiuAnniversaryStage.DistributionCard && (Distributable(f).Count == 0 || facts.Others(f.OwnerSeat).Count == 0)) { Complete(f); return; }
                if (r.Stage == MouZhangXiuAnniversaryStage.DistributionTarget && !Distributable(f).Any(c => c.Id == r.Selected)) { frames.ReplaceTop(f with { MouZhangXiuAnniversary = r with { Stage = MouZhangXiuAnniversaryStage.DistributionCard, Selected = 0 } }); continue; }
                Publish(f); return;
            }
            if (r.Stage is MouZhangXiuAnniversaryStage.NativeSlash or MouZhangXiuAnniversaryStage.Drawing) return;
            if (native.Drain(f)) return;
            frames.ReplaceTop(f = f with { PendingMovementContinuation = null });
            if (r.Stage == MouZhangXiuAnniversaryStage.GiveChildren)
            {
                if (!facts.Alive(f.OwnerSeat)) { Complete(f); return; }
                frames.ReplaceTop(f = f with { MouZhangXiuAnniversary = r with { Stage = MouZhangXiuAnniversaryStage.Drawing } }); draws.BeginOperationProgramDraw(f, f.OwnerSeat, 2, new(DrawReason)); return;
            }
            if (r.Stage is MouZhangXiuAnniversaryStage.ObtainChildren or MouZhangXiuAnniversaryStage.DistributionChildren) { frames.ReplaceTop(f with { MouZhangXiuAnniversary = r with { Stage = MouZhangXiuAnniversaryStage.DistributionCard, Selected = 0 } }); continue; }
            if (r.Stage == MouZhangXiuAnniversaryStage.DrawChildren) { Complete(f); return; }
            if (r.Stage is MouZhangXiuAnniversaryStage.DiscardChildren or MouZhangXiuAnniversaryStage.SlashChildren)
            {
                if (r.Index >= r.Count || !facts.Alive(f.OwnerSeat) || !facts.Alive(r.Target)) { Complete(f); return; }
                frames.ReplaceTop(f = f with { MouZhangXiuAnniversary = r with { Stage = MouZhangXiuAnniversaryStage.SlashIssuing } });
                if (native.CanSlash(f)) native.Slash(f); else Complete(f); return;
            }
            throw new InvalidOperationException("Mou Zhang Xiu has no legal native continuation for this cursor.");
        }
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouZhangXiuFuxi)] private void DrawReturned(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (!Valid(f) || f.MouZhangXiuAnniversary is not { Stage: MouZhangXiuAnniversaryStage.Drawing, Draw: null } r || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != 2) throw new InvalidOperationException("Fuxi requires its original native two-card draw return.");
        frames.ReplaceTop(f = f with { MouZhangXiuAnniversary = r with { Stage = MouZhangXiuAnniversaryStage.DrawChildren, Draw = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); events.AdvanceFact(new MouZhangXiuAnniversaryDrawReturnedEvent(f.Id, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.AttemptFrameId)); Continue(f.Id);
    }
    private void Complete(ProgramSkillFrame f) { events.AdvanceFact(new MouZhangXiuAnniversaryCompletedEvent(f.Id)); frames.ReplaceTop(f = f with { MouZhangXiuAnniversary = null, PendingMovementContinuation = null }); native.Finish(f); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 36600)] private bool Resume(long id) { if (frames.Top is not ProgramSkillFrame { MouZhangXiuAnniversary: not null } f || f.Id != id) return false; Continue(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 36600)] private bool Movement(ProgramSkillFrame f) => f.MouZhangXiuAnniversary is not null && Resume(f.Id);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 36600)] private void Invariant(ProgramSkillFrame f) { if (f.MouZhangXiuAnniversary is not null && !Valid(f)) throw new InvalidOperationException("Invalid Mou Zhang Xiu native receipt."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 36600)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 36600)] private bool Paid(ProgramSkillFrame f) => f.MouZhangXiuAnniversary is not null && Valid(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouZhangXiuFuxi, SkillProgramEffectOp.ResolveMouZhangXiuHaoyi)] private PromptChoice Ai(PendingDecision p, ProgramSkillFrame f)
    {
        var r = f.MouZhangXiuAnniversary!;
        if (r.Stage == MouZhangXiuAnniversaryStage.DistributionTarget) return p.Choices.FirstOrDefault(c => c.Targets is [var t] && facts.Friendly(f.OwnerSeat, t)) ?? p.Choices.Last();
        if (r.Stage == MouZhangXiuAnniversaryStage.DistributionCard && !facts.Others(f.OwnerSeat).Any(t => facts.Friendly(f.OwnerSeat, t))) return p.Choices.Last();
        if (r.Stage == MouZhangXiuAnniversaryStage.Branch) return p.Choices.FirstOrDefault(c => c.Parameters["branch"] == (facts.Friendly(f.OwnerSeat, r.Target) ? "give" : "discard")) ?? p.Choices.First();
        return p.Choices.OrderBy(c => c.Cards is [var card] ? facts.Keep(f.OwnerSeat, card) : int.MaxValue).First();
    }
}
