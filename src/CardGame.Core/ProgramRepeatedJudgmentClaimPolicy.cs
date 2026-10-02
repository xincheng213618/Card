namespace CardGame.Core;

public enum SkillProgramClaimHandLimitExemption { ActualTurn }
public sealed record ProgramRepeatedJudgmentFinalOutcome(long JudgmentFrameId, int CardId, CardKind CardKind, Suit FinalSuit, bool Matched);
public sealed record ProgramRepeatedJudgmentClaimTurn(int TurnNumber, int TurnOwnerSeat);
public sealed record ProgramRepeatedJudgmentClaimReceipt(int InstructionIndex, long JudgmentFrameId, int CardId, int MovementSequence,
    CardLocation From, CardLocation To, int TurnNumber, int TurnOwnerSeat, long GrantSequence);
public sealed record TurnHandLimitExemptCardGrant(long GrantSequence, int TurnNumber, int TurnSeat,
    long ParentFrameId, int EffectIndex, long JudgmentFrameId, CardUseEffectSource Source,
    int BeneficiarySeat, int CardId, int MovementSequence);
public sealed record TurnHandLimitExemptCardGrantedEvent(TurnHandLimitExemptCardGrant Grant) : IGameEvent;

internal interface IRepeatedJudgmentClaimPolicyProgramHost
{
    SkillProgramStepOutcome RepeatJudgmentWithClaimPolicy(ProgramSkillFrame frame, string reason, string bind,
        IReadOnlyList<Suit> suits, SkillProgramClaimHandLimitExemption policy);
}

internal sealed partial class TurnCardUseEffectStore
{
    private readonly List<TurnHandLimitExemptCardGrant> _handLimitExemptCards = [];
    internal IReadOnlyList<TurnHandLimitExemptCardGrant> HandLimitExemptCards => Array.AsReadOnly(_handLimitExemptCards.ToArray());
    internal TurnHandLimitExemptCardGrant GrantHandLimitExemptCard(int turn, int turnSeat, long parent, int effect,
        long judgment, CardUseEffectSource source, int beneficiary, int card, int movement)
    {
        var existing = _handLimitExemptCards.SingleOrDefault(g => g.ParentFrameId == parent && g.EffectIndex == effect &&
            g.JudgmentFrameId == judgment && g.CardId == card);
        if (existing is not null)
        {
            if (existing.TurnNumber != turn || existing.TurnSeat != turnSeat || existing.Source != source ||
                existing.BeneficiarySeat != beneficiary || existing.MovementSequence != movement)
                throw new InvalidOperationException("An exact-card exemption grant changed its actual receipt.");
            return existing;
        }
        var grant = new TurnHandLimitExemptCardGrant(++_grantSequence, turn, turnSeat, parent, effect, judgment,
            source, beneficiary, card, movement);
        _handLimitExemptCards.Add(grant);
        return grant;
    }
    internal IReadOnlySet<int> GetHandLimitExemptCardIds(int turn, int turnSeat, int beneficiary) =>
        _handLimitExemptCards.Where(g => g.TurnNumber == turn && g.TurnSeat == turnSeat && g.BeneficiarySeat == beneficiary)
            .Select(g => g.CardId).ToHashSet();
    private IEnumerable<long> ExpiringHandLimitExemptCards(int turn, int seat) =>
        _handLimitExemptCards.Where(g => g.TurnNumber == turn && g.TurnSeat == seat).Select(g => g.GrantSequence);
    private void ExpireHandLimitExemptCards(HashSet<long> expired) => _handLimitExemptCards.RemoveAll(g => expired.Contains(g.GrantSequence));
}
