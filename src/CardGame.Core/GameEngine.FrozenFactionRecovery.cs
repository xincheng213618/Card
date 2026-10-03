namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IFrozenFactionRecoveryProgramHost
    {
        public void FreezeLivingFactionRecovery(ProgramSkillFrame f) => engine.FreezeProgramLivingFactionRecovery(f);
        public void DrawToFrozenFactionCount(ProgramSkillFrame f) => engine.DrawProgramToFrozenFactionCount(f);
        public void TurnOverIfFrozenFactionCountExceedsGameDamage(ProgramSkillFrame f) => engine.TurnOverProgramFrozenFactionRecovery(f);
    }
    private void FreezeProgramLivingFactionRecovery(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.FrozenFactionRecovery is not null || f.InstructionIndex != 1 ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.SelfDyingResponse } context ||
            ActiveDying is not { } dying || context.ParentFrameId != dying.Id || context.TargetSeat != f.OwnerSeat ||
            dying.VictimSeat != f.OwnerSeat || dying.ResponderSeat != f.OwnerSeat || _players[f.OwnerSeat].Hp > 0)
            throw new InvalidOperationException("Frozen faction recovery lost its exact fresh self-dying producer.");
        var x = GetLivingFactionCount();
        var points = CompleteProgramEventHistory().OfType<DamageAppliedEvent>()
            .Where(e => !e.SourceLess && e.SourceSeat == f.OwnerSeat && e.Amount > 0).Sum(e => e.Amount);
        ReplaceRuntimeTop(f with { FrozenFactionRecovery = new(f.InstructionIndex, dying.Id, x, points) });
        AdvanceEventRulesAndQueueFact(new FrozenFactionRecoveryCapturedEvent(f.Id,
            new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId), dying.Id, x, points));
        RecoverProgramTargetTo(f.Id, f.OwnerSeat, f.OwnerSeat, SkillProgramNumberExpression.IntegerConstant, x, true);
    }
    private void DrawProgramToFrozenFactionCount(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        var receipt = f.FrozenFactionRecovery ?? throw new InvalidOperationException("Frozen faction draw lost its completed recovery producer.");
        if (f.InstructionIndex != receipt.InstructionIndex + 1 || receipt.DrawIssued)
            throw new InvalidOperationException("The frozen faction hand target can be issued only once after recovery children.");
        ReplaceRuntimeTop(f with { FrozenFactionRecovery = receipt with { DrawIssued = true } });
        var count = _players[f.OwnerSeat].IsAlive && _winner == Winner.None ? Math.Max(0, receipt.FactionCount - GetHand(_players[f.OwnerSeat]).Count) : 0;
        AdvanceEventRulesAndQueueFact(new FrozenFactionHandDrawIssuedEvent(f.Id, f.OwnerSeat, receipt.FactionCount, count));
        if (count > 0) DrawProgramCards(f.Id, f.OwnerSeat, count, null, null,
            SkillProgramCardSetVisibility.Private, new("program.frozen-faction-recovery.draw"));
    }
    private void TurnOverProgramFrozenFactionRecovery(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        var receipt = f.FrozenFactionRecovery ?? throw new InvalidOperationException("Frozen faction face policy lost its paid hand target.");
        if (f.InstructionIndex != receipt.InstructionIndex + 2 || !receipt.DrawIssued || receipt.FaceIssued)
            throw new InvalidOperationException("The frozen faction comparison cannot overtake or repeat its draw children.");
        ReplaceRuntimeTop(f with { FrozenFactionRecovery = receipt with { FaceIssued = true } });
        if (_players[f.OwnerSeat].IsAlive && _winner == Winner.None && receipt.FactionCount > receipt.GameDamagePoints)
            SetProgramTargetFaceState(f.Id, f.OwnerSeat, f.OwnerSeat, !_players[f.OwnerSeat].IsFaceDown);
    }
    private void AssertFrozenFactionRecovery(ProgramSkillFrame f)
    {
        if (f.FrozenFactionRecovery is not { } r) return;
        if (r.InstructionIndex != 1 || r.FactionCount < 1 || r.FactionCount > _players.Count || r.GameDamagePoints < 0 ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.SelfDyingResponse } context || context.ParentFrameId != r.DyingFrameId ||
            f.InstructionIndex is < 1 or > 3 || r.DrawIssued != (f.InstructionIndex >= 2) || r.FaceIssued != (f.InstructionIndex >= 3) ||
            !CompleteProgramEventHistory().OfType<FrozenFactionRecoveryCapturedEvent>().Any(e => e.FrameId == f.Id &&
                e.Source == new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) &&
                e.DyingFrameId == r.DyingFrameId && e.FactionCount == r.FactionCount && e.GameDamagePoints == r.GameDamagePoints))
            throw new InvalidOperationException("A frozen faction recovery lost its exact immutable amount/history receipt.");
    }
}
