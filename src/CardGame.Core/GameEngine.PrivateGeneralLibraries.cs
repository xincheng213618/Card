namespace CardGame.Core;

public sealed partial class GameEngine
{
    private readonly Dictionary<PrivateGeneralLibrarySource,PrivateGeneralLibrary> _privateGeneralLibraries=[];
    private long _privateGeneralLibraryRevision;
    private bool HasPrivateGeneralLibraryCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.InitializePrivateGeneralLibrary);
    private sealed partial class ProgramSkillHost:IPrivateGeneralLibraryProgramHost
    {public SkillProgramStepOutcome ExecutePrivateGeneralLibrary(SkillProgramEffect e,ProgramSkillFrame f)=>engine.ExecutePrivateGeneralLibrary(e,f);}

    private bool LibraryBaseSourceLive(PrivateGeneralLibrarySource source)
    {
        if(!IsValidPlayerSeat(source.OwnerSeat))return false;
        var owner=_players[source.OwnerSeat];
        if(!owner.IsAlive)return false;
        var grant=owner.SkillGrants.Grants.FirstOrDefault(g=>g.GrantId==source.CapabilityGrantId&&g.SkillId==source.CapabilitySkillId&&g.SkillInstanceId==source.CapabilitySkillInstanceId);
        if(grant is null||!grant.IsEnabled||grant.GeneralLibraryProjection is not null)return false;
        if(grant.SourceId==CharacterState.PrimarySkillSource&&IsNationalWarMode&&(!owner.GeneralSelected||!owner.GeneralRevealed))return false;
        if(grant.SourceId==CharacterState.SecondarySkillSource&&(!IsNationalWarMode||!owner.SecondaryGeneralSelected||!owner.SecondaryGeneralRevealed))return false;
        return grant.LordProjection is null||LibraryBaseLordProjectionLive(owner,grant);
    }
    // A finite two-layer qualification: base source eligibility precedes HP suppression.
    // Raw borrowed suppressors from a base-live library participate without recursively
    // requesting the same binding shard; derived grants never create another source library.
    private bool LibrarySourceQualified(PrivateGeneralLibrarySource source)
    {
        if(!LibraryBaseSourceLive(source))return false;
        var owner=_players[source.OwnerSeat];
        var suppressors=PrivateGeneralLibrarySuppressionInputs(owner);
        var cap=owner.SkillGrants.Grants.Single(g=>g.GrantId==source.CapabilityGrantId);
        return suppressors.Count==0||suppressors.Contains(cap.SkillId)||cap.SourceId.StartsWith("equipment:",StringComparison.Ordinal);
    }
    private bool LibraryBaseGrantLive(CharacterState owner,SkillGrant grant)=>owner.IsAlive&&grant.IsEnabled&&
        (grant.SourceId!=CharacterState.PrimarySkillSource||!IsNationalWarMode||owner.GeneralSelected&&owner.GeneralRevealed)&&
        (grant.SourceId!=CharacterState.SecondarySkillSource||IsNationalWarMode&&owner.SecondaryGeneralSelected&&owner.SecondaryGeneralRevealed)&&
        (grant.SourceId is not(CharacterState.PrimarySkillSource or CharacterState.SecondarySkillSource)||!_contentRegistry.GetSkill(grant.SkillId).Tags.HasFlag(SkillTag.Lord)||owner.Role==Role.Lord||IsPrintedLordGrantQualified(owner,grant));
    // Lord projection suppressors also use base relations, never a final binding shard.
    private bool LibraryBaseLordProjectionLive(CharacterState owner,SkillGrant grant)
    {
        if(grant.LordProjection is not {} p)return true;
        if(!owner.IsAlive||IsTeamMode||IsNationalWarMode||!IsValidPlayerSeat(p.LordSeat)||p.LordSeat==owner.Seat)return false;
        var lord=_players[p.LordSeat];if(!lord.IsAlive||lord.Role!=Role.Lord)return false;
        return owner.SkillGrants.Grants.Any(g=>g.GrantId==p.CapabilityGrantId&&g.SkillInstanceId==p.CapabilitySkillInstanceId&&g.LordProjection is null&&LibraryBaseGrantLive(owner,g)&&
            _contentRegistry.GetSkill(g.SkillId).Program?.LordSkillProjection==true&&
            (g.GeneralLibraryProjection is null||g.GeneralLibraryProjection is {} a&&LibraryProjectionRelationLive(g,a)&&LibraryBaseSourceLive(a.LibrarySource)))&&
            lord.SkillGrants.Grants.Any(g=>g.GrantId==p.LordGrantId&&g.SkillInstanceId==p.LordSkillInstanceId&&g.SkillId==grant.SkillId&&g.LordProjection is null&&g.GeneralLibraryProjection is null&&LibraryBaseGrantLive(lord,g)&&_contentRegistry.GetSkill(g.SkillId).Tags.HasFlag(SkillTag.Lord));
    }
    private IReadOnlySet<string> PrivateGeneralLibrarySuppressionInputs(CharacterState owner)=>owner.SkillGrants.Grants.Where(g=>g.IsEnabled&&
        (g.SourceId!=CharacterState.PrimarySkillSource||!IsNationalWarMode||owner.GeneralSelected&&owner.GeneralRevealed)&&
        (g.SourceId!=CharacterState.SecondarySkillSource||IsNationalWarMode&&owner.SecondaryGeneralSelected&&owner.SecondaryGeneralRevealed)&&
        (g.SourceId is not(CharacterState.PrimarySkillSource or CharacterState.SecondarySkillSource)||!_contentRegistry.GetSkill(g.SkillId).Tags.HasFlag(SkillTag.Lord)||owner.Role==Role.Lord||IsPrintedLordGrantQualified(owner,g))&&
        (g.LordProjection is null||LibraryBaseLordProjectionLive(owner,g))&&
        (g.GeneralLibraryProjection is null||g.GeneralLibraryProjection is {} p&&LibraryProjectionRelationLive(g,p)&&LibraryBaseSourceLive(p.LibrarySource))&&
        _contentRegistry.GetSkill(g.SkillId).SuppressionRule is {} rule&&owner.Hp==rule.OwnerHpEquals).Select(g=>g.SkillId).ToHashSet(StringComparer.Ordinal);
    private bool LibraryProjectionRelationLive(SkillGrant grant,GeneralLibraryProjectionSource relation)=>
        _privateGeneralLibraries.GetValueOrDefault(relation.LibrarySource) is {} library&&library.Initialized&&
        library.RevealedGeneralId==relation.GeneralId&&library.DeclaredSkillId==relation.SkillId&&grant.SkillId==relation.SkillId;
    private bool IsGeneralLibraryGrantQualified(CharacterState owner,SkillGrant grant)=>IsCurrentTurnSkillGrantQualified(owner,grant)&&(grant.GeneralLibraryProjection is not {} source||
        source.LibrarySource.OwnerSeat==owner.Seat&&LibraryProjectionRelationLive(grant,source)&&LibrarySourceQualified(source.LibrarySource));
    private PrivateGeneralLibrary? EffectiveGeneralLibrary(CharacterState owner)=>_privateGeneralLibraries.Values
        .Where(l=>l.Source.OwnerSeat==owner.Seat&&l.RevealedGeneralId is not null&&LibrarySourceQualified(l.Source))
        .OrderBy(l=>l.Source.CapabilitySkillInstanceId,StringComparer.Ordinal).ThenBy(l=>l.Source.CapabilityGrantId,StringComparer.Ordinal).FirstOrDefault();
    private GeneralGender? GetPrivateGeneralLibraryGender(CharacterState owner)=>EffectiveGeneralLibrary(owner) is {} l?_contentRegistry.Generals[l.RevealedGeneralId!].Gender:null;
    private string? GetPrivateGeneralLibraryFaction(CharacterState owner)=>EffectiveGeneralLibrary(owner) is {} l?l.SelectedFactionId??_contentRegistry.Generals[l.RevealedGeneralId!].FactionId:null;
    private bool _synchronizingPrivateGeneralLibraries;
    private void SynchronizePrivateGeneralLibraries()
    {
        if(!HasPrivateGeneralLibraryCapability||_synchronizingPrivateGeneralLibraries)return;
        _synchronizingPrivateGeneralLibraries=true;
        try {
        foreach(var library in _privateGeneralLibraries.Values.ToArray())
        {
            var owner=_players[library.Source.OwnerSeat];
            if(owner.IsAlive&&owner.SkillGrants.Grants.Any(g=>g.GrantId==library.Source.CapabilityGrantId&&g.SkillInstanceId==library.Source.CapabilitySkillInstanceId&&g.SkillId==library.Source.CapabilitySkillId&&g.GeneralLibraryProjection is null))continue;
            _privateGeneralLibraries.Remove(library.Source);_privateGeneralLibraryRevision++;
            foreach(var grant in owner.SkillGrants.Grants.Where(g=>g.GeneralLibraryProjection?.LibrarySource==library.Source).ToArray())owner.SkillGrants.RemoveGrant(grant.GrantId);
            AdvanceEventRulesAndQueueFact(new PrivateGeneralLibraryRemovedEvent(library.Source));
        }
        } finally {_synchronizingPrivateGeneralLibraries=false;}
    }
    private long _combinedProjectionStamp;private (long Lord,long Library,string Owners) _combinedProjectionDependencies;
    private long CaptureCombinedProjectionDependencyStamp()
    {
        var lord=CaptureLordProjectionDependencyStamp();if(!HasPrivateGeneralLibraryCapability)return lord;
        var owners=string.Join(';',_players.Select(p=>$"{p.SkillGrants.Revision}:{p.Hp}:{p.IsAlive}:{p.GeneralSelected}:{p.GeneralRevealed}:{p.SecondaryGeneralSelected}:{p.SecondaryGeneralRevealed}"));
        var current=(lord,_privateGeneralLibraryRevision,owners);if(current!=_combinedProjectionDependencies){_combinedProjectionDependencies=current;_combinedProjectionStamp++;}return _combinedProjectionStamp;
    }
    private PrivateGeneralLibrarySource? ResolvePrivateGeneralLibrarySource(ProgramSkillFrame f,SkillProgramEffect e)
    {
        var grants=_players[f.OwnerSeat].SkillGrants.Grants;
        var cap=grants.Where(g=>g.IsEnabled&&g.SkillId==f.SkillId&&g.SkillInstanceId==f.SkillInstanceId&&g.GeneralLibraryProjection is null).OrderBy(g=>g.GrantId,StringComparer.Ordinal).FirstOrDefault();
        if(cap is null)return null;
        if(e.Op==SkillProgramEffectOp.AcquirePrivateGeneralAvatar)
        {
            var skill=e.SkillIds.Single();cap=grants.Where(g=>g.IsEnabled&&g.SkillId==skill&&g.GeneralLibraryProjection is null&&(g.SkillInstanceId==f.SkillInstanceId||g.SourceId==cap.SourceId))
                .OrderByDescending(g=>g.SkillInstanceId==f.SkillInstanceId).ThenBy(g=>g.SkillInstanceId,StringComparer.Ordinal).ThenBy(g=>g.GrantId,StringComparer.Ordinal).FirstOrDefault();
        }
        return cap is null?null:new(f.OwnerSeat,cap.SkillId,cap.GrantId,cap.SkillInstanceId);
    }
    private IEnumerable<string> PrivateGeneralCandidates()
    {
        var excluded=_players.Where(p=>p.IsAlive).SelectMany(p=>new[]{p.GeneralSelected?p.General.Id:null,p.SecondaryGeneralSelected?p.SecondaryGeneral?.Id:null}).OfType<string>()
            .Concat(_privateGeneralLibraries.Values.SelectMany(l=>l.GeneralIds)).ToHashSet(StringComparer.Ordinal);
        return _generalPool.Select(g=>g.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Where(id=>!excluded.Contains(id));
    }
    private PrivateGeneralLibrary DrawPrivateGeneralAvatars(PrivateGeneralLibrary library,int amount)
    {
        var ids=library.GeneralIds.ToList();
        for(var i=0;i<amount;i++)
        {
            var candidates=PrivateGeneralCandidates().Where(id=>!ids.Contains(id,StringComparer.Ordinal)).ToArray();if(candidates.Length==0)break;
            ids.Add(candidates[_random.Next(candidates.Length)]);
        }
        library=library with{GeneralIds=Array.AsReadOnly(ids.ToArray()),Initialized=true,Revision=library.Revision+1};
        _privateGeneralLibraries[library.Source]=library;_privateGeneralLibraryRevision++;
        AdvanceEventRulesAndQueueFact(new PrivateGeneralLibraryCountChangedEvent(library.Source,ids.Count));return library;
    }
    private IReadOnlyList<string> PrivateGeneralDeclarableSkills(string generalId,PrivateGeneralLibraryPolicy policy)
    {
        var tags=policy.ExcludedSkillTags.Aggregate(SkillTag.None,(a,b)=>a|b);
        return Array.AsReadOnly(_contentRegistry.Generals[generalId].SkillIds.Where(id=>_contentRegistry.Skills.TryGetValue(id,out var skill)&&skill.ImplementationStatus==SkillImplementationStatus.Complete&&(skill.Tags&tags)==0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }
    private SkillProgramStepOutcome ExecutePrivateGeneralLibrary(SkillProgramEffect e,ProgramSkillFrame f)
    {
        var source=ResolvePrivateGeneralLibrarySource(f,e);if(source is null||!LibrarySourceQualified(source))return SkillProgramStepOutcome.Continue;
        var library=_privateGeneralLibraries.GetValueOrDefault(source);
        if(e.Op==SkillProgramEffectOp.InitializePrivateGeneralLibrary)
        {
            if(f.WindowContext?.Window!=SkillProgramTriggerWindow.GameStarting)throw new InvalidOperationException("Initial private library draw requires GameStarting.");
            library??=new(source,false,[]);if(!library.Initialized)library=DrawPrivateGeneralAvatars(library,e.Amount);
        }
        else if(library is null)return SkillProgramStepOutcome.Continue;
        if(e.Op==SkillProgramEffectOp.AcquirePrivateGeneralAvatar){DrawPrivateGeneralAvatars(library!,e.Amount);return SkillProgramStepOutcome.Continue;}
        ReplaceRuntimeTop(f=f with{GeneralLibraryDraft=new(GeneralLibraryStep.SelectGeneral,source,e.Op==SkillProgramEffectOp.InitializePrivateGeneralLibrary)});
        if(PrivateGeneralLibraryChoices(f).Count==0){ReplaceRuntimeTop(f with{GeneralLibraryDraft=null});return SkillProgramStepOutcome.Continue;}
        PublishPrivateGeneralLibraryChoice(f);return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> PrivateGeneralLibraryChoices(ProgramSkillFrame f)
    {
        var d=f.GeneralLibraryDraft!;var library=_privateGeneralLibraries.GetValueOrDefault(d.Source);if(library is null||!LibrarySourceQualified(d.Source))return [];
        var e=ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
        PromptChoice Choice(string value,string label)=>new(new($"general-library.{f.Id}.{d.Step}.{value}"),label,[],[],new Dictionary<string,string>{{"program-action","private-general-library"},{"frame-id",f.Id.ToString()},{"value",value}});
        return d.Step switch
        {
            GeneralLibraryStep.SelectGeneral=>library.GeneralIds.Where(id=>PrivateGeneralDeclarableSkills(id,e.GeneralLibraryPolicy!).Count>0).Order(StringComparer.Ordinal).Select(id=>Choice(id,"展示【"+_contentRegistry.Generals[id].Name+"】")).ToArray(),
            GeneralLibraryStep.SelectSkill=>PrivateGeneralDeclarableSkills(d.SelectedGeneralId!,e.GeneralLibraryPolicy!).Select(id=>Choice(id,"获得【"+_contentRegistry.Skills[id].Name+"】")).ToArray(),
            GeneralLibraryStep.SelectFaction=>new[]{"wei","shu","wu","qun"}.Select(id=>Choice(id,"选择势力 "+id)).ToArray(),
            _=>throw new InvalidOperationException("Invalid private library selection stage.")
        };
    }
    private void PublishPrivateGeneralLibraryChoice(ProgramSkillFrame f)
    {
        var choices=PrivateGeneralLibraryChoices(f);if(choices.Count==0){FinishPrivateGeneralLibrarySelection(f);return;}
        var skill=_contentRegistry.GetSkill(f.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,skill.Description,[],[],f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=true,Choices=Array.AsReadOnly(choices.ToArray()),SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    private void ResolvePrivateGeneralLibraryChoice(PromptChoice choice)
    {
        var f=_resolutionStack.LastOrDefault() as ProgramSkillFrame??throw new InvalidOperationException("Private library selection lost its owning frame.");
        if(choice.Parameters.GetValueOrDefault("frame-id")!=f.Id.ToString()||!PrivateGeneralLibraryChoices(f).Any(c=>c.Id==choice.Id))throw new InvalidOperationException("Stale private library choice.");
        ClearPendingDecision();var d=f.GeneralLibraryDraft!;var value=choice.Parameters["value"];
        if(d.Step==GeneralLibraryStep.SelectGeneral){ReplaceRuntimeTop(f=f with{GeneralLibraryDraft=d with{Step=GeneralLibraryStep.SelectSkill,SelectedGeneralId=value}});PublishPrivateGeneralLibraryChoice(f);return;}
        if(d.Step==GeneralLibraryStep.SelectSkill)
        {
            d=d with{SelectedSkillId=value};
            if(_contentRegistry.Generals[d.SelectedGeneralId!].FactionId=="god"){ReplaceRuntimeTop(f=f with{GeneralLibraryDraft=d with{Step=GeneralLibraryStep.SelectFaction}});PublishPrivateGeneralLibraryChoice(f);return;}
        }
        else d=d with{SelectedFactionId=value};
        var library=_privateGeneralLibraries[d.Source];var owner=_players[f.OwnerSeat];
        var id=$"general-library:{f.OwnerSeat}:{d.Source.CapabilityGrantId.Length}:{d.Source.CapabilityGrantId}:{d.Source.CapabilitySkillInstanceId.Length}:{d.Source.CapabilitySkillInstanceId}:{d.SelectedGeneralId}:{d.SelectedSkillId}";
        foreach(var old in owner.SkillGrants.Grants.Where(g=>g.GeneralLibraryProjection?.LibrarySource==d.Source&&g.GrantId!=id).ToArray())owner.SkillGrants.RemoveGrant(old.GrantId);
        if(!owner.SkillGrants.Grants.Any(g=>g.GrantId==id))owner.SkillGrants.Grant(new(id,d.SelectedSkillId!,id,"general-library:"+d.Source.CapabilityGrantId,GeneralLibraryProjection:new(d.Source,d.SelectedGeneralId!,d.SelectedSkillId!)));
        RegisterTaggedConversionSkill(owner,d.SelectedSkillId!);
        library=library with{RevealedGeneralId=d.SelectedGeneralId,DeclaredSkillId=d.SelectedSkillId,SelectedFactionId=d.SelectedFactionId,Revision=library.Revision+1};
        _privateGeneralLibraries[d.Source]=library;_privateGeneralLibraryRevision++;
        var general=_contentRegistry.Generals[d.SelectedGeneralId!];
        AdvanceEventRulesAndQueueFact(new PrivateGeneralLibraryDeclaredEvent(d.Source,d.SelectedGeneralId!,d.SelectedSkillId!,general.Gender,d.SelectedFactionId??general.FactionId));FinishPrivateGeneralLibrarySelection(f);
    }
    private void FinishPrivateGeneralLibrarySelection(ProgramSkillFrame f){ReplaceRuntimeTop(GetActiveProgramFrame(f.Id) with{GeneralLibraryDraft=null});AdvanceRuntimeProgram(f.Id);}
    private bool ResumePrivateGeneralLibrarySelection(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{GeneralLibraryDraft:{}} f||f.Id!=id)return false;
        if(_winner!=Winner.None||PrivateGeneralLibraryChoices(f).Count==0){FinishPrivateGeneralLibrarySelection(f);return true;}
        if(_pendingDecision is null)PublishPrivateGeneralLibraryChoice(f);return true;
    }
    private IReadOnlyList<PrivateGeneralLibrarySnapshot>? ProjectPrivateGeneralLibraries(CharacterState owner,int viewerSeat,bool revealAll)
    {
        var libraries=_privateGeneralLibraries.Values.Where(l=>l.Source.OwnerSeat==owner.Seat).OrderBy(l=>l.Source.CapabilitySkillInstanceId,StringComparer.Ordinal).ThenBy(l=>l.Source.CapabilityGrantId,StringComparer.Ordinal).Select(l=>
        {
            var live=l.RevealedGeneralId is not null&&LibrarySourceQualified(l.Source);var general=live?_contentRegistry.Generals[l.RevealedGeneralId!]:null;
            return new PrivateGeneralLibrarySnapshot(l.Source,l.GeneralIds.Count,l.RevealedGeneralId,l.DeclaredSkillId,general?.Gender,live?l.SelectedFactionId??general?.FactionId:null,
                viewerSeat==owner.Seat||revealAll?Array.AsReadOnly(l.GeneralIds.ToArray()):null);
        }).ToArray();return libraries.Length==0?null:Array.AsReadOnly(libraries);
    }
}
