using System.Globalization;
namespace CardGame.Core;
[ProgramRuntimeModule, ProgramCapability]
internal sealed class CaoXianRuntime(ICaoXianFacts facts, ICaoXianNative native, IProgramRuntimeFrames frames, IProgramRuntimeEvents events, IProgramRuntimePrompts prompts, IProgramOperationDraws draws) : ICaoXianRuntime
{
    internal const string Lingxi = "ol:lingxi", Zhifou = "ol:zhifou", Action = "cao-xian-anniversary";
    internal static CardConversionSource Source(ProgramSkillFrame f) => new(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId);
    private CaoXianZhifouPaidEvent[] Paid(int owner) => facts.History<CaoXianZhifouPaidEvent>().Where(e => e.Source.OwnerSeat == owner && e.ActualTurn == facts.Turn).ToArray();
    private int[] Options(int owner) => Enumerable.Range(1, 3).Except(facts.History<CaoXianOptionEvent>().Where(e => e.Owner == owner && e.ActualTurn == facts.Turn).Select(e => e.Option)).ToArray();
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 37300, SkillProgramEffectOp.StoreCaoXianLingxi, SkillProgramEffectOp.ResolveCaoXianZhifou, SkillProgramEffectOp.AdjustCaoXianLingxi)]
    private bool Qualifies(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w) => t.Effects.Single().Op switch
    {
        SkillProgramEffectOp.StoreCaoXianLingxi => facts.Owned(c.OwnerSeat, false, false).Count > 0 && facts.MaxHp(c.OwnerSeat) > 0,
        SkillProgramEffectOp.ResolveCaoXianZhifou => facts.Completed(w, c.OwnerSeat) is not null && Options(c.OwnerSeat).Length > 0 && facts.Piles(c.OwnerSeat, c.SkillInstanceId).Any(p => facts.PileCards(p).Count >= Paid(c.OwnerSeat).Length + 1),
        _ => facts.RemovedPile(w, c.SkillInstanceId) is not null
    };
    public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = frames.ActiveProgram(supplied.Id);
        if (f.CaoXian is not null || f.InstructionIndex != 1 || f.TriggerId != f.ActivationId || f.WindowContext is not { } w || !facts.Parent(f) || !Qualifies(new(f.OwnerSeat, f.SkillId, f.ActivationId, f.SkillInstanceId, f.GameplayHash, 0), facts.Program(f.SkillId).Triggers.Single(t => t.Id == f.TriggerId), w)) throw new InvalidOperationException("Cao Xian requires her exact actual phase, native completed Use or original wings-loss batch.");
        var use = operation == SkillProgramEffectOp.ResolveCaoXianZhifou ? facts.Completed(w, f.OwnerSeat) : null;
        var r = new CaoXianReceipt(Source(f), f.GameplayHash, operation, w.ParentFrameId, facts.Turn, operation == SkillProgramEffectOp.ResolveCaoXianZhifou ? Paid(f.OwnerSeat).Length + 1 : 0, use?.UseId ?? 0, use?.ActionId ?? 0) { Before = facts.Sequence, After = facts.Sequence, Stage = operation == SkillProgramEffectOp.StoreCaoXianLingxi ? CaoXianStage.StoreCards : CaoXianStage.Pile };
        frames.ReplaceTop(f = f with { CaoXian = r }); events.AdvanceFact(new CaoXianStartedEvent(f.Id, r.Source, r.GameplayHash, operation, r.ParentId, r.ActualTurn, r.X, r.CompletedUseId, r.ActionId));
        if (operation == SkillProgramEffectOp.StoreCaoXianLingxi) frames.ReplaceTop(f with { CaoXian = r with { Pile = native.EnsurePile(f), Required = facts.MaxHp(f.OwnerSeat) } });
        else if (operation == SkillProgramEffectOp.AdjustCaoXianLingxi) BeginAdjustment(f, facts.RemovedPile(w, f.SkillInstanceId)!);
        Continue(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    internal bool Valid(ProgramSkillFrame f) => f.CaoXian is { } r && f.TriggerId == f.ActivationId && f.InstructionIndex == 1 && r.Source == Source(f) && r.GameplayHash == f.GameplayHash && facts.Program(f.SkillId).GameplayHash == f.GameplayHash && r.ParentId == f.WindowContext?.ParentFrameId && r.ActualTurn == facts.Turn && facts.Parent(f) && r.Before >= 0 && r.After >= r.Before && r.After <= facts.Sequence && r.X is >= 0 and <= 3 && Enum.IsDefined(r.Stage) && r.Cards.Select(c => c.Id).Distinct().Count() == r.Cards.Count && facts.History<CaoXianStartedEvent>().Count(e => e == new CaoXianStartedEvent(f.Id, r.Source, r.GameplayHash, r.Operation, r.ParentId, r.ActualTurn, r.X, r.CompletedUseId, r.ActionId)) == 1;
    private void BeginAdjustment(ProgramSkillFrame f, PublicPersistentPileSource pile)
    {
        var suits = facts.PileCards(pile).Select(c => c.Suit).Where(s => s != Suit.None).Distinct().Count(); var target = suits * 2; var r = f.CaoXian!;
        events.AdvanceFact(new CaoXianAdjustedEvent(f.Id, f.OwnerSeat, suits, target));
        if (facts.HandCount(f.OwnerSeat) < target)
        {
            var amount = target - facts.HandCount(f.OwnerSeat); frames.ReplaceTop(f = f with { CaoXian = r with { Pile = pile, Stage = CaoXianStage.Drawing, Target = f.OwnerSeat, Required = amount } }); draws.BeginOperationProgramDraw(f, f.OwnerSeat, amount, new("skill-program.ol:lingxi.adjust-draw")); return;
        }
        frames.ReplaceTop(f with { CaoXian = r with { Pile = pile, Stage = CaoXianStage.AdjustCards, Target = f.OwnerSeat, Required = Math.Min(facts.HandCount(f.OwnerSeat) - target, facts.Owned(f.OwnerSeat, true, true).Count) } });
    }
    private int Chooser(ProgramSkillFrame f) => f.CaoXian is { Stage: CaoXianStage.EffectCards, Target: >= 0 } r ? r.Target : f.OwnerSeat;
    private IReadOnlyList<CaoXianCard> AvailableCards(ProgramSkillFrame f)
    {
        var r = f.CaoXian!;
        var all = r.Stage switch { CaoXianStage.RemoveCards => facts.PileCards(r.Pile!), CaoXianStage.AdjustCards => facts.Owned(f.OwnerSeat, true, true), CaoXianStage.EffectCards => facts.Owned(r.Target, false, r.Option == 2), _ => facts.Owned(f.OwnerSeat, false, false) };
        return all.Where(c => !r.Cards.Any(p => p.Id == c.Id)).ToArray();
    }
    private PromptChoice Choice(ProgramSkillFrame f, string branch, string label, int? card = null, int? target = null, int option = 0) => new(new($"cao-xian.{f.Id}.{f.CaoXian!.Stage}.{f.CaoXian.Cards.Count}.{branch}.{card}.{target}.{option}"), label, card is { } id ? [id] : [], target is { } t ? [t] : [], new Dictionary<string, string> { ["program-action"] = Action, ["branch"] = branch, ["option"] = option.ToString(CultureInfo.InvariantCulture) });
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        var r = f.CaoXian!;
        if (r.Stage == CaoXianStage.Pile) return facts.Piles(f.OwnerSeat, f.SkillInstanceId).Where(p => facts.PileCards(p).Count >= r.X).Select((p, i) => Choice(f, "pile", "使用翼：" + string.Join("、", facts.PileCards(p).Select(c => c.Label)), option: i)).ToArray();
        if (r.Stage == CaoXianStage.OptionTarget) return Options(f.OwnerSeat).SelectMany(o => facts.Living().Select(t => Choice(f, "option", "令" + facts.Name(t) + (o == 1 ? "将一张牌置入翼" : o == 2 ? "弃置两张牌" : "失去1点体力"), target: t, option: o))).ToArray();
        var choices = AvailableCards(f).Select(c => Choice(f, "card", (r.Stage == CaoXianStage.RemoveCards ? "移去翼：" : "选择：") + c.Label, card: c.Id)).ToList();
        if (r.Stage == CaoXianStage.StoreCards || r.Stage == CaoXianStage.RemoveCards && r.Cards.Count >= r.X) choices.Add(Choice(f, "finish", r.Stage == CaoXianStage.StoreCards && r.Cards.Count == 0 ? "不置入翼" : "完成选择"));
        return choices;
    }
    private void Publish(ProgramSkillFrame f)
    {
        var choices = Choices(f); var chooser = Chooser(f); frames.ReplaceTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        prompts.Set(new(DecisionKind.ProgramTrigger, chooser, f.CaoXian!.Stage == CaoXianStage.RemoveCards ? $"知否：本次至少移去{f.CaoXian.X}张真实翼。" : "灵犀/知否：按当前真实实体执行。", choices.SelectMany(c => c.Cards).ToArray(), choices.SelectMany(c => c.Targets).ToArray(), f.OwnerSeat) { PromptId = prompts.NextId(), Choices = Array.AsReadOnly(choices.ToArray()), IsPrivate = f.CaoXian.Stage is not (CaoXianStage.RemoveCards or CaoXianStage.Pile or CaoXianStage.OptionTarget) }, facts.Human(chooser)); native.Publish();
    }
    [ProgramAnswer(Action)] private void Answer(PromptChoice supplied)
    {
        if (frames.Top is not ProgramSkillFrame { CaoXian: { } r } f || !Valid(f) || prompts.Current?.PlayerSeat != Chooser(f) || Choices(f).SingleOrDefault(c => c.Id == supplied.Id) is not { } exact || !exact.Cards.SequenceEqual(supplied.Cards) || !exact.Targets.SequenceEqual(supplied.Targets) || exact.Parameters.Count != supplied.Parameters.Count || exact.Parameters.Any(p => supplied.Parameters.GetValueOrDefault(p.Key) != p.Value)) throw new InvalidOperationException("Cao Xian requires the original published chooser, exact current card and owning source.");
        prompts.Clear(); frames.ReplaceTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect }); var branch = supplied.Parameters["branch"];
        if (r.Stage == CaoXianStage.Pile) { var pile = facts.Piles(f.OwnerSeat, f.SkillInstanceId).Where(p => facts.PileCards(p).Count >= r.X).ElementAt(int.Parse(supplied.Parameters["option"], CultureInfo.InvariantCulture)); frames.ReplaceTop(f with { CaoXian = r with { Pile = pile, Stage = CaoXianStage.RemoveCards } }); Continue(f.Id); return; }
        if (r.Stage == CaoXianStage.OptionTarget)
        {
            var option = int.Parse(supplied.Parameters["option"], CultureInfo.InvariantCulture); var target = supplied.Targets.Single(); events.AdvanceFact(new CaoXianOptionEvent(f.Id, f.OwnerSeat, r.ActualTurn, option, target));
            frames.ReplaceTop(f = f with { CaoXian = r with { Option = option, Target = target, Cards = [], Stage = CaoXianStage.EffectCards, Required = Math.Min(option == 1 ? 1 : 2, facts.Owned(target, false, option == 2).Count) } });
            if (option == 3) { frames.ReplaceTop(f = f with { CaoXian = f.CaoXian! with { Stage = CaoXianStage.EffectChildren, Before = facts.Sequence, After = facts.Sequence } }); events.AdvanceFact(new CaoXianHpIssuedEvent(f.Id, target)); native.LoseHp(f); }
            Continue(f.Id); return;
        }
        if (branch == "finish") { if (r.Cards.Count == 0) { Complete(f); return; } StartMove(f, r.Stage == CaoXianStage.RemoveCards ? CaoXianStage.RemoveChildren : CaoXianStage.EffectChildren); return; }
        var card = AvailableCards(f).Single(c => c.Id == supplied.Cards.Single()); frames.ReplaceTop(f with { CaoXian = r with { Cards = r.Cards.Append(card).ToArray() } }); Continue(f.Id);
    }
    private void StartMove(ProgramSkillFrame f, CaoXianStage stage)
    {
        var r = f.CaoXian!; frames.ReplaceTop(f = f with { CaoXian = r with { Stage = stage, Before = facts.Sequence, After = facts.Sequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); native.Move(f); Continue(f.Id);
    }
    internal void MovesPaid(long id, IReadOnlyList<CardMovementRecord> moves)
    {
        var f = frames.ActiveProgram(id); var r = f.CaoXian!;
        if (!Valid(f) || moves.Count != r.Cards.Count || moves.Any(m => !r.Cards.Any(c => c.Id == m.CardId && c.Kind == m.CardKind && c.From == m.From) || m.Sequence <= r.Before)) throw new InvalidOperationException("Cao Xian lost the exact successful native physical payment invoice.");
        frames.ReplaceTop(f = f with { CaoXian = r with { After = moves.Count == 0 ? r.Before : moves.Max(m => m.Sequence) } });
        foreach (var m in moves) events.AdvanceFact(new CaoXianMovedEvent(f.Id, r.Stage, m.CardId, m.From, m.To, m.Sequence));
        if (r.Stage == CaoXianStage.RemoveChildren)
        {
            if (r.Pile is null || moves.Count < r.X || moves.Any(m => m.From != r.Pile.Location || m.To != CardLocation.OutsideGame)) throw new InvalidOperationException("Zhifou requires genuine wing-pile-to-Outside physical records.");
            events.AdvanceFact(new CaoXianZhifouPaidEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurn, r.X, r.CompletedUseId, r.ActionId));
            foreach (var m in moves) events.AdvanceFact(new CaoXianZhifouRemovedEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurn, r.CompletedUseId, r.ActionId, r.Pile, m.CardId, m.CardKind, m.Sequence));
        }
    }
    internal void Continue(long id)
    {
        while (prompts.Current is null && frames.Top is ProgramSkillFrame { CaoXian: { } r } f && f.Id == id)
        {
            if (!Valid(f)) throw new InvalidOperationException("Cao Xian lost her exact native parent, physical wing source or completed-use receipt.");
            if (r.Stage == CaoXianStage.Drawing) return;
            if (r.Stage is CaoXianStage.RemoveChildren or CaoXianStage.EffectChildren or CaoXianStage.AdjustChildren or CaoXianStage.DrawChildren)
            {
                if (native.Drain(f)) return; frames.ReplaceTop(f = f with { PendingMovementContinuation = null });
                if (r.Stage != CaoXianStage.RemoveChildren) { Complete(f); return; }
                if (!facts.Alive(f.OwnerSeat) || facts.Finished) { Complete(f); return; }
                frames.ReplaceTop(f with { CaoXian = r with { Stage = CaoXianStage.OptionTarget, Cards = [] } }); continue;
            }
            if (!facts.Alive(f.OwnerSeat) || facts.Finished) { Complete(f); return; }
            if (r.Stage == CaoXianStage.Pile)
            {
                var piles = facts.Piles(f.OwnerSeat, f.SkillInstanceId).Where(p => facts.PileCards(p).Count >= r.X).ToArray(); if (piles.Length == 0) { Complete(f); return; }
                if (piles is [var pile]) { frames.ReplaceTop(f with { CaoXian = r with { Pile = pile, Stage = CaoXianStage.RemoveCards } }); continue; }
            }
            if (r.Stage is CaoXianStage.StoreCards or CaoXianStage.EffectCards or CaoXianStage.AdjustCards)
            {
                if (r.Cards.Count >= r.Required || AvailableCards(f).Count == 0)
                {
                    if (r.Cards.Count == 0) { Complete(f); return; }
                    StartMove(f, r.Stage == CaoXianStage.AdjustCards ? CaoXianStage.AdjustChildren : CaoXianStage.EffectChildren); return;
                }
            }
            if (r.Stage == CaoXianStage.RemoveCards && AvailableCards(f).Count == 0) { if (r.Cards.Count < r.X) { Complete(f); return; } StartMove(f, CaoXianStage.RemoveChildren); return; }
            Publish(f); return;
        }
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.AdjustCaoXianLingxi)] private void DrawReturned(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (!Valid(f) || f.CaoXian is not { Stage: CaoXianStage.Drawing, Draw: null } r || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != r.Required) throw new InvalidOperationException("Lingxi requires its original exact native Hand-adjustment draw return.");
        frames.ReplaceTop(f with { CaoXian = r with { Stage = CaoXianStage.DrawChildren, Draw = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); events.AdvanceFact(new CaoXianDrawReturnedEvent(f.Id, r.Required, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.AttemptFrameId)); Continue(f.Id);
    }
    private void Complete(ProgramSkillFrame f) { events.AdvanceFact(new CaoXianCompletedEvent(f.Id)); frames.ReplaceTop(f = f with { CaoXian = null, PendingMovementContinuation = null }); native.Finish(f); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 37300)] private bool Resume(long id) { if (frames.Top is not ProgramSkillFrame { CaoXian: not null } f || f.Id != id) return false; Continue(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 37300)] private bool Movement(ProgramSkillFrame f) => f.CaoXian is not null && Resume(f.Id);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 37300)] private void Invariant(ProgramSkillFrame f) { if (f.CaoXian is not null && !Valid(f)) throw new InvalidOperationException("Invalid Cao Xian owning-frame continuation."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 37300)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 37300)] private bool PaidContinuation(ProgramSkillFrame f) => f.CaoXian is not null && Valid(f);
    [ProgramAiChoice(SkillProgramEffectOp.StoreCaoXianLingxi, SkillProgramEffectOp.ResolveCaoXianZhifou, SkillProgramEffectOp.AdjustCaoXianLingxi)] private PromptChoice Ai(PendingDecision p, ProgramSkillFrame f)
    {
        var r = f.CaoXian!;
        if (r.Stage == CaoXianStage.OptionTarget) return p.Choices.OrderBy(c => c.Targets is [var target] && !facts.Friendly(p.PlayerSeat, target) ? 0 : 1).ThenBy(c => c.Parameters["option"] == "3" ? 0 : 1).First();
        if (r.Stage == CaoXianStage.RemoveCards && r.Cards.Count >= r.X) return p.Choices.Single(c => c.Parameters["branch"] == "finish");
        return p.Choices.OrderBy(c => c.Cards is [var card] ? facts.Keep(p.PlayerSeat, card) : int.MaxValue).First();
    }
}
