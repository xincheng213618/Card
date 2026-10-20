using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record WuTuGuHanyongGrant(long ProgramFrameId,long UseId,long ActionId,CardConversionSource Source,string GameplayHash,int ActualTurn,int Round,int HpAtUse,int ProviderSeat,CardKind Kind);
public sealed record WuTuGuHanyongUse
{
    private IReadOnlyList<WuTuGuHanyongGrant> _grants=Array.AsReadOnly(Array.Empty<WuTuGuHanyongGrant>());
    public IReadOnlyList<WuTuGuHanyongGrant> Grants{get=>_grants;init=>_grants=Array.AsReadOnly(value.ToArray());}
}
public sealed record WuTuGuHanyongQualifiedEvent(long UseId,long ActionId,CardConversionSource Source,string GameplayHash,int ActualTurn,int Round,int HpAtUse,int ProviderSeat,CardKind Kind):IGameEvent;
public sealed record WuTuGuHanyongArmedEvent(WuTuGuHanyongGrant Grant):IGameEvent;
internal sealed class ArmHanyongNativeGroupDamageDescriptor():WuTuGuDescriptor(SkillProgramEffectOp.ArmHanyongNativeGroupDamage){}
