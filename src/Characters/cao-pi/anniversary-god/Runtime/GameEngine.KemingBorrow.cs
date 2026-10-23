using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<KemingBorrowOption> KemingPrintedBorrowOptions(ContentGeneralDefinition general) => Array.AsReadOnly(general.SkillIds
        .SelectMany(id => _contentRegistry.Skills.GetValueOrDefault(id)?.Program is { } p ? p.Triggers
            .Where(t => t.Window == SkillProgramTriggerWindow.AfterDamageApplied && t.Subject == SkillProgramTriggerSubject.Owner)
            .Select(t => new KemingBorrowOption(general.Id, id, t.Id, p.GameplayHash)) : [])
        .OrderBy(o => o.SkillId, StringComparer.Ordinal).ThenBy(o => o.BindingId, StringComparer.Ordinal).ToArray());
    private IReadOnlyList<KemingBorrowOption> KemingSourceBorrowOptions(int source) => Array.AsReadOnly(KemingPrintedBorrowOptions(_contentRegistry.Generals[_players[source].General.Id])
        .Concat(_players[source].SecondaryGeneral is { } second ? KemingPrintedBorrowOptions(_contentRegistry.Generals[second.Id]) : []).DistinctBy(o => (o.SkillId, o.BindingId)).ToArray());
    private IReadOnlyList<KemingBorrowOption> KemingBorrowOptions(int borrower, int source)
    {
        var printed = KemingSourceBorrowOptions(source);
        if (printed.Count > 0) return printed;
        return Array.AsReadOnly(_contentRegistry.Generals.Values.Where(g => g.CharacterId?.StartsWith("character:cao-", StringComparison.Ordinal) == true &&
                g.RulesetId is null or "sanguosha-anniversary").SelectMany(KemingPrintedBorrowOptions)
            .DistinctBy(o => (o.SkillId, o.BindingId)).OrderBy(o => o.GeneralId, StringComparer.Ordinal).ThenBy(o => o.SkillId, StringComparer.Ordinal).ThenBy(o => o.BindingId, StringComparer.Ordinal).ToArray());
    }
    private IReadOnlyList<PromptChoice> KemingBorrowChoices(ProgramSkillFrame f)
    {
        if (f.ShenCaoPi is not { Stage: ShenCaoPiStage.BorrowChoice } r) return [];
        return Array.AsReadOnly(r.Options.Select((o, index) => (o, index)).Where(x => r.ForcedBorrow is null || x.o == r.ForcedBorrow).Select(x => new PromptChoice(
            new($"keming:{f.Id}:{x.index}"), "触发【" + _contentRegistry.GetSkill(x.o.SkillId).Name + "】", [], [],
            new Dictionary<string, string> { ["program-action"] = "keming-borrow", ["option"] = x.index.ToString(CultureInfo.InvariantCulture) })).ToArray());
    }
    private void PublishKemingBorrow(ProgramSkillFrame f)
    {
        var choices = KemingBorrowChoices(f);
        if (choices.Count == 0) { FinishShenCaoPi(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "刻名：触发本次受伤时机的技能。", [], [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = false, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("keming-borrow")] private void AnswerKemingBorrow(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenCaoPi: { Stage: ShenCaoPiStage.BorrowChoice } r } f ||
            !KemingBorrowChoices(f).Any(c => c.Id == selected.Id && c.Cards.SequenceEqual(selected.Cards) && c.Targets.SequenceEqual(selected.Targets) && c.Parameters.OrderBy(x => x.Key).SequenceEqual(selected.Parameters.OrderBy(x => x.Key))))
            throw new InvalidOperationException("Keming lost its exact original borrowed-skill choice.");
        AssertShenCaoPi(f);
        var option = r.Options[int.Parse(selected.Parameters["option"], CultureInfo.InvariantCulture)];
        if (f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, SourceSeat: { } source, TargetSeat: { } target, DamageFrameId: { } damageId } context || target != f.OwnerSeat || source == target ||
            _resolutionStack.Count < 2 || _resolutionStack[^2] is not DamageTriggerWindowFrame window || window.Id != r.ParentId || window.ParentFrameId != damageId ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count || !MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex].ToProgramCandidate()) ||
            _contentRegistry.GetSkill(option.SkillId).Program is not { } program || program.GameplayHash != option.GameplayHash)
            throw new InvalidOperationException("Keming cannot move its borrowed trigger to another native damage segment.");
        var trigger = program.Triggers.Single(t => t.Id == option.BindingId);
        var count = trigger.DamageOccurrence == SkillProgramDamageOccurrence.PerDamagePoint ? context.Amount : 1;
        if (count < 1) throw new InvalidOperationException("Keming lost its original positive damage occurrence count.");
        var instance = $"keming:{f.OwnerSeat}:{f.SkillInstanceId}:{option.SkillId}";
        var receipts = Enumerable.Range(0, count).Select(i =>
        {
            var candidate = new DamageTriggerCandidate(f.OwnerSeat, $"keming:{f.Id}:{option.SkillId}:{option.BindingId}:{i}", trigger.Priority, trigger.Optional,
                option.SkillId, option.BindingId, instance, option.GameplayHash, i);
            return new KemingBorrowReceipt(f.Id, r.Source, r.GameplayHash, damageId, f.OwnerSeat, option, candidate);
        }).Where(r => !window.Candidates.Any(c => c.OwnerSeat == f.OwnerSeat && c.ProgramId == option.SkillId && c.ProgramTriggerId == option.BindingId && c.OccurrenceIndex == r.Candidate.OccurrenceIndex)).ToArray();
        var candidates = window.Candidates.Take(window.CandidateIndex + 1).Concat(receipts.Select(r => r.Candidate)).Concat(window.Candidates.Skip(window.CandidateIndex + 1)).ToArray();
        ReplaceRuntimeFrame(window.Id, window with { Candidates = Array.AsReadOnly(candidates), KemingBorrows = (window.KemingBorrows ?? []).Concat(receipts).ToArray() });
        foreach (var receipt in receipts) AdvanceEventRulesAndQueueFact(new KemingBorrowIssuedEvent(window.Id, receipt));
        ClearPendingDecision(); FinishShenCaoPi(GetActiveProgramFrame(f.Id));
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveKemingDamagedSkill)] private PromptChoice ChooseKemingBorrowAi(PendingDecision p, ProgramSkillFrame f) => p.Choices[0];
    private KemingBorrowReceipt? ActiveKemingBorrow(int owner, string skill, string? instance = null)
    {
        if (!HasShenCaoPiCapability) return null;
        foreach (var window in _resolutionStack.OfType<DamageTriggerWindowFrame>().Reverse())
        {
            if (window.KemingBorrows is not { } borrows || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count) continue;
            var candidate = window.Candidates[window.CandidateIndex];
            if (candidate.OwnerSeat != owner || candidate.ProgramId != skill || instance is not null && candidate.SkillInstanceId != instance) continue;
            var receipt = borrows.SingleOrDefault(r => r.Candidate == candidate);
            if (receipt is null || receipt.BorrowerSeat != owner || receipt.Selected.SkillId != skill || receipt.Selected.GameplayHash != candidate.GameplayHash ||
                receipt.DamageFrameId != window.ParentFrameId || window.TargetSeat != owner || receipt.Source.OwnerSeat != owner ||
                candidate.SkillInstanceId != $"keming:{owner}:{receipt.Source.SkillInstanceId}:{skill}" ||
                _contentRegistry.GetSkill(skill).Program?.GameplayHash != candidate.GameplayHash ||
                ProgramEventHistory<KemingBorrowIssuedEvent>().Count(e => e.WindowId == window.Id && e.Receipt == receipt) != 1) continue;
            return receipt;
        }
        return null;
    }
    private bool HasKemingBorrowedSkillInstance(CharacterState owner, string skill, string instance) => ActiveKemingBorrow(owner.Seat, skill, instance) is not null;
    private bool HasKemingBorrowedSkill(CharacterState owner, string skill) => ActiveKemingBorrow(owner.Seat, skill) is not null;
    private string? KemingBorrowedSkillInstance(CharacterState owner, string skill) => ActiveKemingBorrow(owner.Seat, skill)?.Candidate.SkillInstanceId;
}
