using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record MouGongsunZanHuMaterial(int CardId,CardKind Kind,CardLocation From,CardConversionSource Source,string GameplayHash,int Sequence=0);
public sealed record MouGongsunZanHuPayment(long ParentFrameId,int ActorSeat,int ProviderSeat,int? RequesterSeat,int Turn,long Before)
{
    public long? ActionId{get;init;}
    private IReadOnlyList<MouGongsunZanHuMaterial> _materials=[];
    public IReadOnlyList<MouGongsunZanHuMaterial> Materials{get=>_materials;init=>_materials=Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<CardConversionSource> _conversions=[];
    public IReadOnlyList<CardConversionSource> Conversions{get=>_conversions;init=>_conversions=Array.AsReadOnly(value.ToArray());}
}
public abstract partial record ResolutionFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouGongsunZanHuPayment? MouGongsunZanHuPayment{get;init;}}
public sealed record MouGongsunZanHuMaterialIssuedEvent(long ParentFrameId,int Index,int CardId,CardKind Kind,CardLocation From,CardConversionSource Source,string GameplayHash,int ActorSeat,int ProviderSeat,int? RequesterSeat,int Turn,long Before):IGameEvent;
public sealed record MouGongsunZanHuMaterialPaidEvent(long ParentFrameId,int Index,int CardId,int Sequence):IGameEvent;
public sealed record MouGongsunZanHuActionBoundEvent(long ParentFrameId,int CardId,int Sequence,long ActionId):IGameEvent;
