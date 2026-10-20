namespace CardGame.Core;

// 丰积 evidence: one scalar fact per resolved option of the owner's round
// ledger; the redistribution target is public once committed.
public enum FengjiOptionKind { Draw = 1, Slash = 2 }
public sealed record ProgramFengjiOptionChosenEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int RoundNumber, FengjiOptionKind Option, bool Accepted, int RecipientSeat) : IGameEvent;

// 旋回 evidence: the round effects swap is public and the skill disables until
// a character dies; the disable state derives from this fact versus deaths.
public sealed record ProgramXuanhuiEffectsSwappedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int RoundNumber, int SwappedOptionCount) : IGameEvent;

public sealed partial class GameEngine
{

    // Round-scoped 丰积 adjustments for one seat and one public numeric query.
    // Every owner's option facts participate: accepted options swap the
    // owner↔recipient attribution per the option owner's 旋回 swap parity;
    // a declined option's +1 stays with its owner (it has no counterpart).
    internal int GetFengjiRoundAdjustment(int seat, SkillRuleQuery query)
    {
        if (_roundNumber == 0) return 0;
        var facts = CompleteProgramEventHistory();
        var options = facts.OfType<ProgramFengjiOptionChosenEvent>()
            .Where(e => e.RoundNumber == _roundNumber).ToArray();
        if (options.Length == 0) return 0;
        var swapsByOwner = facts.OfType<ProgramXuanhuiEffectsSwappedEvent>()
            .Where(e => e.RoundNumber == _roundNumber)
            .GroupBy(e => e.OwnerSeat)
            .ToDictionary(group => group.Key, group => group.Count());
        var total = 0;
        foreach (var option in options)
        {
            if (query == SkillRuleQuery.DrawCount && option.Option != FengjiOptionKind.Draw ||
                query == SkillRuleQuery.SlashLimit && option.Option != FengjiOptionKind.Slash)
                continue;
            var swapCount = swapsByOwner.GetValueOrDefault(option.OwnerSeat);
            if (option.Accepted)
            {
                if (option.OwnerSeat == seat)
                    total += swapCount % 2 == 0 ? -1 : 2;
                else if (option.RecipientSeat == seat)
                    total += swapCount % 2 == 0 ? 2 : -1;
            }
            else if (option.OwnerSeat == seat)
            {
                total += 1;
            }
        }
        return total;
    }


    [ProgramCapability]
    private sealed class ChenDengProgramCapability(GameEngine engine) : IChenDengProgramHost
    {
        public SkillProgramStepOutcome FengjiRoundChoice(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.FengjiProgramRoundChoice(frame, effect);
        public SkillProgramStepOutcome XuanhuiSwapEffects(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.XuanhuiProgramSwapEffects(frame, effect);
    }
}
