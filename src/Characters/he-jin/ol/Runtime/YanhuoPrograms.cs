using System.Text.Json.Serialization;

namespace CardGame.Core;
public enum HeJinYanhuoStage { ChoosingCard, DiscardChildren }
public sealed record HeJinYanhuoSelection(int CardId, CardLocation From);
public sealed record HeJinYanhuoReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long DeathWindowId, int CandidateIndex, ResolutionFrameStep WindowStep, ProgramTriggerCandidate Candidate,
    OwnedDeathCursor OriginalDeath, OwnedDeadDyingCursor? OriginalDying, OwnedOriginalDamageCursor? OriginalDamage,
    long? OriginalAttackOwnerFrameId, int ActualTurn, int TurnOwnerSeat, int KillerSeat, int Limit,
    HeJinYanhuoStage Stage = HeJinYanhuoStage.ChoosingCard, int Discarded = 0, long Before = 0, long After = 0)
{
    private readonly IReadOnlyList<HeJinYanhuoSelection> _selectedCards = Array.AsReadOnly(Array.Empty<HeJinYanhuoSelection>());
    public IReadOnlyList<HeJinYanhuoSelection> SelectedCards { get => _selectedCards; init => _selectedCards = Array.AsReadOnly(value.ToArray()); }
}
public sealed record HeJinYanhuoDeathCapturedEvent(long DeathFrameId, int OwnerSeat, int HandCount, int EquipmentCount) : IGameEvent;
public sealed record HeJinYanhuoStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long DeathFrameId, int KillerSeat, int Limit, string ReturnHash) : IGameEvent;
public sealed record HeJinYanhuoDiscardPaidEvent(long FrameId, int Index, int KillerSeat, int CardId,
    CardLocation From, long Before, long After) : IGameEvent;
public sealed record HeJinYanhuoCompletedEvent(long FrameId, int Discarded) : IGameEvent;
internal sealed class ResolveYanhuoKillerDiscardDescriptor() : HeJinDescriptor(SkillProgramEffectOp.ResolveYanhuoKillerDiscard)
{
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.OwnerDied)];

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => true;
}
