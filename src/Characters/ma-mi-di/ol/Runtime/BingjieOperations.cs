using System.Text.Json.Serialization;
namespace CardGame.Core;
internal sealed class PayBingjieTurnMaximumHpDescriptor() : MaMiDiDescriptor(SkillProgramEffectOp.PayBingjieTurnMaximumHp, SkillProgramTriggerWindow.PlayPhaseStarting) { }
internal sealed class ResolveBingjieIssuedTargetDiscardsDescriptor() : MaMiDiDescriptor(SkillProgramEffectOp.ResolveBingjieIssuedTargetDiscards, SkillProgramTriggerWindow.CardUseTargetsFinalized) { }
public sealed record BingjieGrantedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int Turn, int TurnOwner, long PlayWindowId) : IGameEvent;
public sealed record BingjieUseBoundEvent(long UseId, long ActionId, int ActorSeat, long GrantFrameId, int Turn, int TurnOwner) : IGameEvent;
public sealed record MaMiDiBingjieUseReceipt(long UseId, long ActionId, int ActorSeat, CardKind Kind, int Turn, int TurnOwner, bool Issued = false)
{
    private readonly IReadOnlyList<BingjieGrantedEvent> _grants = Array.AsReadOnly(Array.Empty<BingjieGrantedEvent>());
    public IReadOnlyList<BingjieGrantedEvent> Grants { get => _grants; init => _grants = Array.AsReadOnly(value.ToArray()); }
}
