using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool IsAllHandChallengeOp(SkillProgramEffectOp op) => op is
        SkillProgramEffectOp.ChooseTrickOrAllHandDiscard or SkillProgramEffectOp.AllHandDiscardOrDifferenceDamageFromSelected;
    private static bool IsAllHandChallengeKind(ProgramSequentialDiscardKind kind) => kind is
        ProgramSequentialDiscardKind.CategoryOrAllHand or ProgramSequentialDiscardKind.SelectedStartAllHand;
    private static bool AllHandChallengeRing(ProgramSequentialDiscardDraft d) => d.Kind == ProgramSequentialDiscardKind.SelectedStartAllHand;
    private int AllHandChallengeDamage(ProgramSkillFrame f, int target) => Math.Abs(GetHand(_players[f.OwnerSeat]).Count - GetHand(_players[target]).Count);
    private bool AllHandChallengeQuotaAvailable(int owner, string skill, string binding) =>
        _roundNumber >= 1 && !CompleteProgramEventHistory().OfType<ProgramAllHandChallengeQuotaPaidEvent>().Any(e =>
            e.Source.OwnerSeat == owner && e.Source.SkillId == skill && e.Source.BindingId == binding && _roundNumber - e.RoundNumber < 2);

    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCondition, 5703, SkillProgramEffectOp.AllHandDiscardOrDifferenceDamageFromSelected)]
    private bool AllowsAllHandChallengeQuota(ProgramActivationLegalityContext gate) =>
        AllHandChallengeQuotaAvailable(gate.OwnerSeat, gate.SkillId, gate.Activation.Id);

    private SkillProgramStepOutcome RunAllHandChallenge(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SequentialDiscard is null)
        {
            if (!SequentialDiscardCanContinue(f) || f.SelectedTargetSeats is not [var start] || !_players[start].IsAlive)
                return SkillProgramStepOutcome.Continue;
            var ring = effect.Op == SkillProgramEffectOp.AllHandDiscardOrDifferenceDamageFromSelected;
            if (ring && !AllHandChallengeQuotaAvailable(f.OwnerSeat, f.SkillId, f.ActivationId!))
                throw new InvalidOperationException("The two-round all-hand challenge allowance has already paid.");
            var d = new ProgramSequentialDiscardDraft(ring ? ProgramSequentialDiscardKind.SelectedStartAllHand : ProgramSequentialDiscardKind.CategoryOrAllHand,
                f.InstructionIndex - 1, SequentialDiscardSource(f), f.GameplayHash, _turnNumber, _currentSeat,
                start, SequentialDiscardOrder(f.OwnerSeat, start, ring), 0, start, 0, 0, null,
                ProgramSequentialDiscardStage.BranchChoice, [], []);
            ReplaceRuntimeTop(f = f with { SequentialDiscard = d, ReexecuteParticipantInstruction = true });
            AdvanceEventRulesAndQueueFact(new ProgramSequentialDiscardStartedEvent(f.Id, d.Kind, d.Source, d.GameplayHash,
                d.ActualTurnNumber, d.ActualTurnOwnerSeat, start));
            if (ring) AdvanceEventRulesAndQueueFact(new ProgramAllHandChallengeQuotaPaidEvent(f.Id, d.Source, d.GameplayHash,
                _roundNumber, _turnNumber, _currentSeat));
        }
        else ReplaceRuntimeTop(f = f with { ReexecuteParticipantInstruction = true });
        if (!ValidAllHandChallenge(f)) throw new InvalidOperationException("An all-hand challenge lost its original frame or paid child.");
        var current = f.SequentialDiscard!;
        if (current.Stage is ProgramSequentialDiscardStage.AwaitingMovement or ProgramSequentialDiscardStage.AwaitingDamage)
        {
            if (f.PendingMovementContinuation is not null || f.AttackAttempt is not null)
                throw new InvalidOperationException("An all-hand challenge resumed before its paid child returned.");
            if (!AllHandChallengeRing(current)) return FinishSequentialDiscard(f, 0);
            current = current with { Cursor = current.Cursor + 1, Stage = ProgramSequentialDiscardStage.BranchChoice,
                PrimaryBranch = null, Payment = null, Remaining = 0 };
            ReplaceRuntimeTop(f = f with { SequentialDiscard = current });
        }
        if (!SequentialDiscardCanContinue(f))
        { CancelProgramBindingAndCleanup(f, "弃牌来源或原实例失效，已付结算保留。"); return SkillProgramStepOutcome.AwaitChild; }
        if (AllHandChallengeRing(current))
        {
            var cursor = current.Cursor;
            while (cursor < current.Order.Count && !_players[current.Order[cursor]].IsAlive) cursor++;
            if (cursor == current.Order.Count) return FinishSequentialDiscard(f with { SequentialDiscard = current with { Cursor = cursor } }, 0);
            current = current with { Cursor = cursor, ChooserSeat = current.Order[cursor] };
            ReplaceRuntimeTop(f = f with { SequentialDiscard = current });
        }
        else if (!_players[current.ChooserSeat].IsAlive) return FinishSequentialDiscard(f, 0);
        return PublishAllHandChallenge(f, effect);
    }

    private SkillProgramStepOutcome PublishAllHandChallenge(ProgramSkillFrame f, SkillProgramEffect effect)
    {
        var d = f.SequentialDiscard!;
        var available = SequentialDiscardCards(f, d.ChooserSeat, effect).ToArray();
        var hand = GetHand(_players[d.ChooserSeat]);
        var choices = new List<PromptChoice>();
        void Add(string branch, string label, IReadOnlyList<int>? cards = null) => choices.Add(new(
            new ChoiceId($"all-hand-challenge.{f.Id}.{d.Cursor}.{d.Stage}.{branch}.{cards?.FirstOrDefault() ?? 0}"), label,
            cards ?? [], cards is null ? [] : [d.ChooserSeat], new Dictionary<string, string>
            { ["program-action"] = "sequential-discard", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture),
                ["cursor"] = d.Cursor.ToString(CultureInfo.InvariantCulture), ["stage"] = d.Stage.ToString(), ["branch"] = branch }));
        if (d.Stage == ProgramSequentialDiscardStage.BranchChoice)
        {
            if (!AllHandChallengeRing(d) && available.Any(c => GetProgramCardCategory(c.Card.Kind) == SkillProgramCardCategory.Trick))
                Add("primary", "弃置一张锦囊牌。");
            // All is one current-hand batch. A protected hand entity makes that option unavailable.
            if (available.Length == hand.Count) Add("all-hand", $"弃置所有手牌（{hand.Count}张）。");
            if (AllHandChallengeRing(d)) Add("damage", $"受到 {AllHandChallengeDamage(f, d.ChooserSeat)} 点火焰伤害。");
        }
        else if (d.Stage == ProgramSequentialDiscardStage.CardChoice)
            foreach (var item in available.Where(c => GetProgramCardCategory(c.Card.Kind) == SkillProgramCardCategory.Trick))
                Add("card", $"弃置【{item.Card.DisplayName}】。", [item.Card.Id]);
        else throw new InvalidOperationException("An all-hand challenge cannot expose a paid child as a choice.");
        if (choices.Count == 0) return FinishSequentialDiscard(f, 0);
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, d.ChooserSeat, $"【{skill.Name}】请选择一项。",
            choices.SelectMany(c => c.Cards).ToArray(), [d.ChooserSeat], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = d.ChooserSeat, Choices = Array.AsReadOnly(choices.ToArray()),
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[d.ChooserSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveAllHandChallengeChoice(ProgramSkillFrame f, PromptChoice choice)
    {
        var d = f.SequentialDiscard!; var effect = SequentialDiscardEffect(f);
        if (!ValidAllHandChallenge(f) || _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p ||
            p.PlayerSeat != d.ChooserSeat || !p.Choices.Any(c => c.Id == choice.Id) ||
            choice.Parameters.GetValueOrDefault("frame-id") != f.Id.ToString(CultureInfo.InvariantCulture) ||
            choice.Parameters.GetValueOrDefault("cursor") != d.Cursor.ToString(CultureInfo.InvariantCulture) ||
            choice.Parameters.GetValueOrDefault("stage") != d.Stage.ToString())
            throw new InvalidOperationException("An all-hand challenge choice belongs to its original private owner.");
        if (!SequentialDiscardCanContinue(f) || !_players[d.ChooserSeat].IsAlive)
        { ClearPendingDecision(); CancelProgramBindingAndCleanup(f, "弃牌参与者或原实例失效。"); return; }
        var branch = choice.Parameters.GetValueOrDefault("branch");
        var available = SequentialDiscardCards(f, d.ChooserSeat, effect).ToArray();
        if (d.Stage == ProgramSequentialDiscardStage.CardChoice)
        {
            if (branch != "card" || choice.Cards is not [var id] || choice.Targets is not [var chooser] || chooser != d.ChooserSeat ||
                !available.Any(c => c.Card.Id == id && GetProgramCardCategory(c.Card.Kind) == SkillProgramCardCategory.Trick))
                throw new InvalidOperationException("The all-hand challenge requires its selected real hand trick.");
            ClearPendingDecision(); PaySequentialDiscard(f, [id], [CardLocation.Hand(d.ChooserSeat)]); return;
        }
        if (d.Stage != ProgramSequentialDiscardStage.BranchChoice || choice.Cards.Count != 0 || choice.Targets.Count != 0)
            throw new InvalidOperationException("An all-hand challenge branch cannot directly supply a private payment.");
        if (branch == "damage" && AllHandChallengeRing(d))
        {
            var amount = AllHandChallengeDamage(f, d.ChooserSeat);
            ClearPendingDecision(); ReplaceRuntimeTop(f = f with { SequentialDiscard = d with { Stage = ProgramSequentialDiscardStage.AwaitingDamage } });
            AdvanceEventRulesAndQueueFact(new ProgramSequentialDiscardDamageChosenEvent(f.Id, d.Cursor, f.OwnerSeat, d.ChooserSeat, amount, DamageNature.Fire));
            if (amount > 0) BeginProgramSkillDamage(f, d.ChooserSeat, amount, nature: DamageNature.Fire);
            else AdvanceRuntimeProgram(f.Id);
            return;
        }
        if (branch == "primary" && !AllHandChallengeRing(d) && available.Any(c => GetProgramCardCategory(c.Card.Kind) == SkillProgramCardCategory.Trick))
        {
            ClearPendingDecision(); ReplaceRuntimeTop(f = f with { SequentialDiscard = d with { PrimaryBranch = true, Remaining = 1, Stage = ProgramSequentialDiscardStage.CardChoice } });
            AdvanceEventRulesAndQueueFact(new ProgramSequentialDiscardBranchEvent(f.Id, d.Cursor, d.ChooserSeat, true, 1));
            PublishAllHandChallenge(f, effect); return;
        }
        if (branch != "all-hand" || available.Length != GetHand(_players[d.ChooserSeat]).Count)
            throw new InvalidOperationException("Every current hand entity must be discardable for the all-hand branch.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { SequentialDiscard = d with { PrimaryBranch = false } });
        AdvanceEventRulesAndQueueFact(new ProgramSequentialDiscardBranchEvent(f.Id, d.Cursor, d.ChooserSeat, false, available.Length));
        if (available.Length != 0)
        { PaySequentialDiscard(f, available.Select(c => c.Card.Id).ToArray(), available.Select(c => c.Location).ToArray()); return; }
        // Empty hand is a valid zero-card choice; there is no fictitious move or damage child.
        if (!AllHandChallengeRing(d)) { FinishSequentialDiscard(f, 0); AdvanceRuntimeProgram(f.Id); return; }
        ReplaceRuntimeTop(f = f with { SequentialDiscard = f.SequentialDiscard! with { Cursor = d.Cursor + 1,
            ChooserSeat = d.Cursor + 1 < d.Order.Count ? d.Order[d.Cursor + 1] : d.ChooserSeat, PrimaryBranch = null } });
        if (d.Cursor + 1 == d.Order.Count) { FinishSequentialDiscard(f, 0); AdvanceRuntimeProgram(f.Id); return; }
        if (RunAllHandChallenge(f, effect) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(f.Id);
    }

    private PromptChoice SelectAiAllHandChallenge(PendingDecision p, ProgramSkillFrame f, SkillProgramEffect effect)
    {
        var d = f.SequentialDiscard!;
        if (d.Stage == ProgramSequentialDiscardStage.CardChoice)
            return p.Choices.OrderBy(c => GetKeepValue(GetHand(_players[d.ChooserSeat]).Single(card => card.Id == c.Cards[0]), _players[d.ChooserSeat]))
                .ThenBy(c => c.Cards[0]).First();
        if (!AllHandChallengeRing(d)) return p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "primary") ?? p.Choices.First();
        var discard = p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "all-hand");
        var cost = SequentialDiscardCards(f, d.ChooserSeat, effect).Sum(c => GetKeepValue(c.Card, _players[d.ChooserSeat]));
        return discard is not null && cost <= AllHandChallengeDamage(f, d.ChooserSeat) * 20d
            ? discard : p.Choices.Single(c => c.Parameters.GetValueOrDefault("branch") == "damage");
    }
}
