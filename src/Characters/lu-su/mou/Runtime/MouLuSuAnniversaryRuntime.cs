using System.Globalization;
using System.Text.Json;
namespace CardGame.Core;
[ProgramRuntimeModule, ProgramCapability]
internal sealed class MouLuSuAnniversaryRuntime(IMouLuSuAnniversaryFacts facts, IMouLuSuAnniversaryNative native, IProgramRuntimeFrames frames, IProgramRuntimeEvents events, IProgramRuntimePrompts prompts, IProgramOperationDraws draws) : IMouLuSuAnniversaryRuntime
{
    internal const string Mingshi = "mou:mingshi", Mengmou = "mou:mengmou", Action = "mou-lu-su-anniversary";
    private static CardConversionSource Source(ProgramSkillFrame f) => new(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId);
    private bool Used(int owner, bool yin) => facts.History<MouLuSuAnniversaryAcceptedEvent>().Any(e => e.Owner == owner && e.Turn == facts.Turn && e.Yin == yin);
    private MouLuSuAnniversaryDrawTicketEvent? Ticket(int owner, string instance) => facts.Phase() is { } p ? facts.History<MouLuSuAnniversaryDrawTicketEvent>().SingleOrDefault(e => e.Source.OwnerSeat == owner && e.Source.SkillInstanceId == instance && e.Phase == p && !facts.History<MouLuSuAnniversaryDrawConsumedEvent>().Any(c => c.Source == e.Source && c.Phase == p)) : null;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 36100, SkillProgramEffectOp.InitializeMouLuSuMengmou, SkillProgramEffectOp.ScheduleMouLuSuMingshi, SkillProgramEffectOp.ResolveMouLuSuMingshi, SkillProgramEffectOp.ResolveMouLuSuMengmou, SkillProgramEffectOp.RecoverMouLuSuMengmouDamage)]
    private bool Qualifies(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w)
    {
        var op = t.Effects.Last().Op;
        if (op == SkillProgramEffectOp.ResolveMouLuSuMingshi) return Ticket(c.OwnerSeat, c.SkillInstanceId) is not null;
        if (op == SkillProgramEffectOp.ResolveMouLuSuMengmou) return !Used(c.OwnerSeat, facts.Yin(c.OwnerSeat, c.SkillInstanceId)) && facts.Peer(c, w) is { } other && facts.Alive(other);
        if (op == SkillProgramEffectOp.RecoverMouLuSuMengmouDamage) return facts.Damage(c, w) is { Amount: > 0 };
        return true;
    }
    public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = frames.ActiveProgram(supplied.Id);
        if (operation == SkillProgramEffectOp.ScheduleMouLuSuMingshi)
        {
            if (f.WindowContext?.Window != SkillProgramTriggerWindow.DrawPhaseStarting || f.InstructionIndex != 2 || !facts.Parent(f) || facts.Phase() is not { Kind: ActualDiscardRecoveryPhaseKind.Draw } phase || phase.ActorSeat != f.OwnerSeat) throw new InvalidOperationException("Mingshi requires its actual accepted normal-draw plan.");
            events.AdvanceFact(new MouLuSuAnniversaryDrawTicketEvent(Source(f), f.GameplayHash, phase)); return SkillProgramStepOutcome.Continue;
        }
        if (f.MouLuSuAnniversary is not null || f.InstructionIndex != 1 || f.TriggerId != f.ActivationId || f.WindowContext is not { } w || !facts.Parent(f)) throw new InvalidOperationException("Mou Lu Su requires his actual owning native window.");
        var candidate = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.ActivationId, f.SkillInstanceId, f.GameplayHash, 0, w.OccurrenceIndex);
        var yin = f.SkillId == Mengmou && facts.Yin(f.OwnerSeat, f.SkillInstanceId);
        var peer = operation == SkillProgramEffectOp.ResolveMouLuSuMengmou ? facts.Peer(candidate, w) : null;
        var damage = operation == SkillProgramEffectOp.RecoverMouLuSuMengmouDamage ? facts.Damage(candidate, w) : null;
        var other = peer ?? damage?.Actor ?? -1; var count = damage is { } d ? checked(d.Amount * 2) : Math.Max(0, facts.MaxHp(f.OwnerSeat));
        var stage = operation switch { SkillProgramEffectOp.InitializeMouLuSuMengmou => MouLuSuAnniversaryStage.Opening, SkillProgramEffectOp.ResolveMouLuSuMingshi => MouLuSuAnniversaryStage.Show, SkillProgramEffectOp.RecoverMouLuSuMengmouDamage => MouLuSuAnniversaryStage.Recover, _ => MouLuSuAnniversaryStage.Request };
        if (operation == SkillProgramEffectOp.ResolveMouLuSuMingshi)
        {
            var ticket = Ticket(f.OwnerSeat, f.SkillInstanceId) ?? throw new InvalidOperationException("Mingshi lost its accepted actual draw ticket.");
            events.AdvanceFact(new MouLuSuAnniversaryDrawConsumedEvent(ticket.Source, ticket.Phase));
        }
        if (operation == SkillProgramEffectOp.ResolveMouLuSuMengmou && (other < 0 || Used(f.OwnerSeat, yin))) throw new InvalidOperationException("Mengmou requires a real owned-card acquisition and its unused current-turn direction.");
        var r = new MouLuSuAnniversaryReceipt(Source(f), f.GameplayHash, operation, w.ParentFrameId, facts.Turn, other, count, yin) { Stage = stage, Before = facts.Sequence, After = facts.Sequence, UseId = damage?.UseId ?? 0 };
        frames.ReplaceTop(f = f with { MouLuSuAnniversary = r });
        events.AdvanceFact(new MouLuSuAnniversaryStartedEvent(f.Id, r.Source, r.GameplayHash, operation, r.ParentId, r.Turn, r.Other, r.X, r.Yin));
        if (operation == SkillProgramEffectOp.RecoverMouLuSuMengmouDamage) events.AdvanceFact(new MouLuSuAnniversaryDamageRewardIssuedEvent(f.Id,w.DamageFrameId!.Value,damage!.Value.UseId,frames.OfType<CardUseFrame>().Single(u => u.Id == damage.Value.UseId).Action!.ActionId));
        if (operation == SkillProgramEffectOp.ResolveMouLuSuMengmou) { events.AdvanceFact(new MouLuSuAnniversaryAcceptedEvent(f.Id, f.OwnerSeat, other, r.Turn, yin, count)); native.Yin(f, !yin); }
        Continue(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    internal bool Valid(ProgramSkillFrame f) => f.MouLuSuAnniversary is { } r && r.Source == Source(f) && r.GameplayHash == f.GameplayHash && facts.Program(f.SkillId).GameplayHash == f.GameplayHash && r.ParentId == f.WindowContext?.ParentFrameId && frames.ParentOf(f.Id)?.Id == r.ParentId && facts.Parent(f) &&
        r.Turn == facts.Turn && f.InstructionIndex == 1 && Enum.IsDefined(r.Stage) && r.X >= 0 && r.Index >= 0 && r.Index <= r.X && r.Cursor >= 0 && r.Before >= 0 && r.After >= r.Before && r.After <= facts.Sequence &&
        facts.History<MouLuSuAnniversaryStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.Operation == r.Operation && e.ParentId == r.ParentId && e.Turn == r.Turn && (r.Operation == SkillProgramEffectOp.ResolveMouLuSuMingshi || e.Other == r.Other) && e.X == r.X && e.Yin == r.Yin) == 1;
    private int Chooser(ProgramSkillFrame f) => f.MouLuSuAnniversary is { Stage: MouLuSuAnniversaryStage.Gift or MouLuSuAnniversaryStage.Request or MouLuSuAnniversaryStage.DraftCards or MouLuSuAnniversaryStage.DraftTargets } r ? r.Other : f.OwnerSeat;
    private PromptChoice Choice(ProgramSkillFrame f, string branch, string label, int item = 0, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) => new(new($"mou-lu-su-anniversary.{f.Id}.{f.MouLuSuAnniversary!.Stage}.{f.MouLuSuAnniversary.Index}.{f.MouLuSuAnniversary.Cards.Count}.{branch}.{item}"), label, cards ?? [], targets ?? [], new Dictionary<string,string> { ["program-action"] = Action, ["branch"] = branch, ["item"] = item.ToString(CultureInfo.InvariantCulture) });
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        var r = f.MouLuSuAnniversary!; var result = new List<PromptChoice>();
        if (r.Stage == MouLuSuAnniversaryStage.Opening) return [Choice(f, "yang", "以阳状态开始"), Choice(f, "yin", "以阴状态开始")];
        if (r.Stage == MouLuSuAnniversaryStage.Show) return facts.Hand(f.OwnerSeat).Where(c => !r.Cards.Contains(c.Id)).Select(c => Choice(f, "show", "展示【" + c.DisplayName + "】", c.Id, [c.Id])).ToArray();
        if (r.Stage == MouLuSuAnniversaryStage.Recipient) return facts.Living().Where(s => s != f.OwnerSeat).Select(s => Choice(f, "recipient", "令其获得展示牌：" + facts.Name(s), s, targets: [s])).ToArray();
        if (r.Stage == MouLuSuAnniversaryStage.Gift) return r.Cards.Where(c => facts.InHand(f.OwnerSeat, c)).Select(c => Choice(f, "gift", "获得展示的【" + facts.Hand(f.OwnerSeat).Single(x => x.Id == c).DisplayName + "】", c, [c])).ToArray();
        if (r.Stage == MouLuSuAnniversaryStage.Request)
        {
            if (r.Yin) result.AddRange(facts.Plays(f).Select((p,i) => Choice(f, "play", "打出【杀】", i, p.Cards)));
            else result.AddRange(facts.Uses(f).Select((a,i) => Choice(f, "action", a.Description, i, a.CardId is > 0 ? [a.CardId.Value] : [], a.TargetSeats)));
            result.Add(Choice(f, "stop", r.Yin ? $"不再打出，失去{r.X - r.Index}点体力" : "不再使用【杀】")); return result;
        }
        if (r.SelectedAction is { } a && r.Stage is MouLuSuAnniversaryStage.DraftCards or MouLuSuAnniversaryStage.DraftTargets)
        {
            if (r.Cards.Count < a.MaxCardCount) result.AddRange(a.SelectableCardIds.Except(r.Cards).Select(id => Choice(f, "material", "选择使用素材", id, [id])));
            if (r.Targets.Count < a.MaxTargetCount) result.AddRange(a.SelectableTargetSeats.Except(r.Targets).Select(s => Choice(f, "target", "选择目标：" + facts.Name(s), s, targets: [s])));
            if (DraftValid(a, r.Cards, r.Targets)) result.Add(Choice(f, "use", "确认使用【杀】", cards: r.Cards, targets: r.Targets));
            result.Add(Choice(f, "back", "重新选择【杀】"));
        }
        return result;
    }
    internal static bool DraftValid(LegalAction a, IReadOnlyList<int> cards, IReadOnlyList<int> targets) => cards.Count >= a.MinCardCount && cards.Count <= a.MaxCardCount && cards.Distinct().Count() == cards.Count && cards.All(a.SelectableCardIds.Contains) && targets.Count >= a.MinTargetCount && targets.Count <= a.MaxTargetCount && targets.Distinct().Count() == targets.Count && targets.All(a.SelectableTargetSeats.Contains) && (a.SelectableTargetSequences is null || a.SelectableTargetSequences.Any(t => t.SequenceEqual(targets)));
    private void Publish(ProgramSkillFrame f)
    {
        var choices = Choices(f); if (choices.Count == 0) { Complete(f); return; }
        var chooser = Chooser(f); frames.ReplaceTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        prompts.Set(new(DecisionKind.ProgramTrigger, chooser, f.SkillId == Mingshi ? "明势：展示三张手牌，令另一名角色获得其中一张。" : $"盟谋：{(f.MouLuSuAnniversary!.Yin ? "打出" : "使用")}【杀】（{f.MouLuSuAnniversary.Index}/{f.MouLuSuAnniversary.X}）。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat) { PromptId = prompts.NextId(), IsPrivate = true, Choices = Array.AsReadOnly(choices.ToArray()) }, facts.Human(chooser)); native.PublishState();
    }
    [ProgramAnswer(Action)] private void Answer(PromptChoice supplied)
    {
        if (frames.Top is not ProgramSkillFrame { MouLuSuAnniversary: { } r } f || !Valid(f) || prompts.Current?.PlayerSeat != Chooser(f) || !Choices(f).Any(c => c.Id == supplied.Id && c.Cards.SequenceEqual(supplied.Cards) && c.Targets.SequenceEqual(supplied.Targets) && c.Parameters.Count == supplied.Parameters.Count && c.Parameters.All(p => supplied.Parameters.GetValueOrDefault(p.Key) == p.Value))) throw new InvalidOperationException("Mengmou/Mingshi requires the exact current chooser and live published choice.");
        var original = prompts.Current!; prompts.Clear(); frames.ReplaceTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect }); var branch = supplied.Parameters["branch"];
        if (r.Stage == MouLuSuAnniversaryStage.Opening) { native.Yin(f, branch == "yin"); Complete(f); return; }
        if (branch == "show") { frames.ReplaceTop(f with { MouLuSuAnniversary = r with { Cards = r.Cards.Append(supplied.Cards.Single()).ToArray() } }); Continue(f.Id); return; }
        if (branch == "recipient") { frames.ReplaceTop(f with { MouLuSuAnniversary = r with { Other = supplied.Targets.Single(), Stage = MouLuSuAnniversaryStage.Gift } }); Continue(f.Id); return; }
        if (branch == "gift") { frames.ReplaceTop(f = f with { MouLuSuAnniversary = r with { Stage = MouLuSuAnniversaryStage.GiftChildren, Before = facts.Sequence, After = facts.Sequence }, PendingMovementContinuation = new(f.OwnerSeat,0,null) }); native.Gift(f, supplied.Cards.Single()); Continue(f.Id); return; }
        if (branch == "stop") { if (!r.Yin) { Complete(f); return; } frames.ReplaceTop(f with { MouLuSuAnniversary = r with { Stage = MouLuSuAnniversaryStage.MissingHp, Cursor = r.Index } }); Continue(f.Id); return; }
        if (branch == "play") { var plan = facts.Plays(f)[int.Parse(supplied.Parameters["item"], CultureInfo.InvariantCulture)]; frames.ReplaceTop(f = f with { MouLuSuAnniversary = r with { Stage = MouLuSuAnniversaryStage.PlayedAccepted, Played = plan, Before = facts.Sequence, After = facts.Sequence, Response = null, ActionId = 0 } }); native.Play(f); return; }
        if (branch == "action") { var a = facts.Uses(f)[int.Parse(supplied.Parameters["item"], CultureInfo.InvariantCulture)]; frames.ReplaceTop(f = f with { MouLuSuAnniversary = r with { Stage = MouLuSuAnniversaryStage.DraftCards, SelectedAction = a, OriginalDecision = original, Cards = [], Targets = [] } }); if (a.MaxCardCount == 0 && a.MaxTargetCount == 0) Issue(f); else Publish(f); return; }
        if (branch == "material") { frames.ReplaceTop(f with { MouLuSuAnniversary = r with { Cards = r.Cards.Append(supplied.Cards.Single()).ToArray() } }); Publish(frames.ActiveProgram(f.Id)); return; }
        if (branch == "target") { frames.ReplaceTop(f with { MouLuSuAnniversary = r with { Targets = r.Targets.Append(supplied.Targets.Single()).ToArray() } }); Publish(frames.ActiveProgram(f.Id)); return; }
        if (branch == "back") { frames.ReplaceTop(f with { MouLuSuAnniversary = r with { Stage = MouLuSuAnniversaryStage.Request, Cards = [], Targets = [], SelectedAction = null } }); Publish(frames.ActiveProgram(f.Id)); return; }
        if (branch == "use") { Issue(f); return; } throw new InvalidOperationException("Unknown Mou Lu Su native answer.");
    }
    private void Issue(ProgramSkillFrame f)
    {
        var r = f.MouLuSuAnniversary!; if (r.SelectedAction is not { } a || !DraftValid(a,r.Cards,r.Targets) || !facts.Uses(f).Any(c => JsonSerializer.Serialize(c) == JsonSerializer.Serialize(a))) throw new InvalidOperationException("Mengmou lost its selected current native Slash action.");
        frames.ReplaceTop(f = f with { MouLuSuAnniversary = r with { Stage = MouLuSuAnniversaryStage.Issuing, Before = facts.Sequence, After = facts.Sequence, UseId = 0, ActionId = 0, Returned = false } }); native.Use(f);
    }
    internal void Continue(long id)
    {
        while (prompts.Current is null && frames.Top is ProgramSkillFrame { MouLuSuAnniversary: { } r } f && f.Id == id)
        {
            if (!Valid(f)) throw new InvalidOperationException("Mou Lu Su lost his owning native receipt.");
            if (facts.Finished) { Complete(f); return; }
            if (r.Stage == MouLuSuAnniversaryStage.Show && r.Cards.Count >= Math.Min(3, facts.HandCount(f.OwnerSeat))) { frames.ReplaceTop(f = f with { MouLuSuAnniversary = r with { Stage = MouLuSuAnniversaryStage.Recipient } }); native.Reveal(f); continue; }
            if (r.Stage is MouLuSuAnniversaryStage.Opening or MouLuSuAnniversaryStage.Show or MouLuSuAnniversaryStage.Recipient or MouLuSuAnniversaryStage.Gift) { Publish(f); return; }
            if (r.Stage is MouLuSuAnniversaryStage.GiftChildren or MouLuSuAnniversaryStage.DrawChildren or MouLuSuAnniversaryStage.UseChildren or MouLuSuAnniversaryStage.PlayedChildren or MouLuSuAnniversaryStage.PlayedCostChildren or MouLuSuAnniversaryStage.HpChildren or MouLuSuAnniversaryStage.RecoveryChildren)
            {
                if (native.Drain(f)) return; frames.ReplaceTop(f = f with { PendingMovementContinuation = null });
                if (r.Stage == MouLuSuAnniversaryStage.GiftChildren)
                {
                    if (facts.Alive(f.OwnerSeat) && facts.Living().Any(s => facts.HandCount(s) > facts.HandCount(f.OwnerSeat))) { frames.ReplaceTop(f = f with { MouLuSuAnniversary = r with { Stage = MouLuSuAnniversaryStage.Drawing } }); draws.BeginOperationProgramDraw(f,f.OwnerSeat,2,new("skill-program.mou:mingshi.draw")); return; }
                    Complete(f); return;
                }
                if (r.Stage == MouLuSuAnniversaryStage.DrawChildren) { Complete(f); return; }
                if (r.Stage == MouLuSuAnniversaryStage.PlayedCostChildren) { native.CompletePlayed(f); return; }
                var next = r.Stage == MouLuSuAnniversaryStage.HpChildren ? MouLuSuAnniversaryStage.MissingHp : r.Stage == MouLuSuAnniversaryStage.RecoveryChildren ? MouLuSuAnniversaryStage.Recover : MouLuSuAnniversaryStage.Request;
                frames.ReplaceTop(f with { MouLuSuAnniversary = r with { Stage = next, SelectedAction = null, Cards = [], Targets = [], Played = null, Response = null } }); continue;
            }
            if (r.Stage == MouLuSuAnniversaryStage.Request) { if (!facts.Alive(r.Other) || r.Index >= r.X) { Complete(f); return; } Publish(f); return; }
            if (r.Stage == MouLuSuAnniversaryStage.MissingHp)
            {
                if (!facts.Alive(r.Other) || r.Cursor >= r.X) { Complete(f); return; }
                frames.ReplaceTop(f = f with { MouLuSuAnniversary = r with { Stage = MouLuSuAnniversaryStage.HpChildren, Cursor = r.Cursor + 1 }, PendingMovementContinuation = new(f.OwnerSeat,0,null) }); events.AdvanceFact(new MouLuSuAnniversaryHpIssuedEvent(f.Id,r.Cursor,r.Other,false)); native.LoseHp(f,r.Other); continue;
            }
            if (r.Stage == MouLuSuAnniversaryStage.Recover)
            {
                if (r.Cursor >= r.X) { Complete(f); return; } var target = r.Cursor % 2 == 0 ? f.OwnerSeat : r.Other;
                frames.ReplaceTop(f = f with { MouLuSuAnniversary = r with { Stage = MouLuSuAnniversaryStage.RecoveryChildren, Cursor = r.Cursor + 1 }, PendingMovementContinuation = new(f.OwnerSeat,0,null) });
                if (facts.Alive(target)) { events.AdvanceFact(new MouLuSuAnniversaryHpIssuedEvent(f.Id,r.Cursor,target,true)); native.Recover(f,target); } continue;
            }
            return;
        }
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouLuSuMingshi)] private void DrawReturned(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (!Valid(f) || f.MouLuSuAnniversary is not { Stage: MouLuSuAnniversaryStage.Drawing, Draw: null } r || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != 2) throw new InvalidOperationException("Mingshi requires its exact native additional draw return.");
        frames.ReplaceTop(f with { MouLuSuAnniversary = r with { Stage = MouLuSuAnniversaryStage.DrawChildren, Draw = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat,0,null) }); Continue(f.Id);
    }
    private void Complete(ProgramSkillFrame f) { events.AdvanceFact(new MouLuSuAnniversaryCompletedEvent(f.Id)); frames.ReplaceTop(f = f with { MouLuSuAnniversary = null, PendingMovementContinuation = null }); native.Finish(f); }
    [ProgramContinuation(ProgramContinuationKind.Resume,36100)] private bool Resume(long id) { if (frames.Top is not ProgramSkillFrame { MouLuSuAnniversary: not null } f || f.Id != id) return false; Continue(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement,36100)] private bool Movement(ProgramSkillFrame f) => f.MouLuSuAnniversary is not null && Resume(f.Id);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,36100)] private void Invariant(ProgramSkillFrame f) { if (f.MouLuSuAnniversary is not null && !Valid(f)) throw new InvalidOperationException("Invalid Mou Lu Su native receipt."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,36100)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,36100)] private bool Paid(ProgramSkillFrame f) => f.MouLuSuAnniversary is not null && Valid(f) || f.SkillId == Mengmou && f.TriggerId == "requested-slash-damage" && f.InstructionIndex is 0 or 1 && f.WindowContext is { } w && facts.Damage(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,0,w.OccurrenceIndex),w) is not null;
    [ProgramAiChoice(SkillProgramEffectOp.InitializeMouLuSuMengmou,SkillProgramEffectOp.ResolveMouLuSuMingshi,SkillProgramEffectOp.ResolveMouLuSuMengmou)] private PromptChoice Ai(PendingDecision p, ProgramSkillFrame f) => p.Choices.FirstOrDefault(c => c.Parameters["branch"] is "use" or "play" or "action") ?? p.Choices.First();
}
