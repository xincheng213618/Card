using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class BoundaryLuXunChecks
{
    public static void OwnedHePaymentCountsActualHandBatch()
    {
        var(g,r)=Start("mixed");var equip=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip);
        Accept(g,new PlayCardCommand(0,equip.CardId!.Value,equip.TargetSeats,g.Revision,P(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        var x=g.CreateSnapshot(0).Players[0].Hand!.Count;
        Accept(g,new UseProgramSkillCommand(0,"fixture:mixed-loss","discard",[],[],g.Revision,P(g)!.PromptId));
        Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:mixed-loss"&&p.Choices.Any(c=>c.Cards.Count>0));
        for(var i=0;i<x+1;i++)Answer(g,c=>c.Cards.Count==1);
        Reach(g,p=>p.SkillPrompt?.SkillId=="boundary:lianying");
        var movement=g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Last();
        Require(movement.Batch.Movements.Count==x&&movement.Contexts is {} contexts && contexts[movement.CandidateIndex].Facts?.MovedCardCount==x,"Real HE selection emits an exact Hand source batch with frozen X, independent of its later equipment source batch.");Replay(g,r);
        Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,p=>p.SkillPrompt?.SkillId=="boundary:lianying"&&p.Choices.Any(c=>c.Targets.Count>0));
        Require(P(g)!.Choices.All(c=>c.Targets.Count<=x),"Target bound uses matched Hand X rather than whole mixed batch size.");Replay(g,r);Conserve(g);
    }
    private static (GameEngine,ContentRegistry) Start(string mode="duel")
    {
        var definitions=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage());
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(definitions,mode));
        Require(!r.Skills.Values.Any(s=>s.Program?.Triggers.Any(t=>t.Window==SkillProgramTriggerWindow.AfterTurnEnded)==true),"Fixture tests hold prelude without an AfterTurnEnded program.");
        var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-lu-xun-check",UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},r);
        Accept(g,new StartGameCommand());Reach(g,p=>p.Kind==DecisionKind.SelectGeneral);
        Accept(g,new SelectGeneralCommand(0,"fixture:lu-xun",g.Revision,P(g)!.PromptId));
        Reach(g,p=>p.Kind==DecisionKind.PlayCard && p.PlayerSeat==0);
        return(g,r);
    }
    private static PendingDecision? P(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(c);Require(result.Accepted,result.Error?.Message ?? "Command rejected.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> select){var p=P(g)!;Accept(g,new AnswerPromptCommand(0,p.PromptId,p.Choices.First(select).Id,g.Revision));}
    private static void Step(GameEngine g)
    {
        var p=P(g);
        if(p is {PlayerSeat:0,Kind:DecisionKind.ProgramTrigger}) Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
        else if(p is {PlayerSeat:0,Kind:DecisionKind.PlayCard}) Accept(g,new EndPlayPhaseCommand(0,g.Revision,p.PromptId));
        else if(p is {PlayerSeat:0,Kind:DecisionKind.Nullification or DecisionKind.RespondSlash or DecisionKind.RespondDodge})
            Accept(g,new AnswerPromptCommand(0,p.PromptId,p.Choices.First(c=>c.Cards.Count==0).Id,g.Revision));
        else Accept(g,new AdvanceOneStepCommand(g.Revision));
    }
    private static void Reach(GameEngine g,Func<PendingDecision,bool> predicate)
    {for(var i=0;i<180;i++){if(P(g) is {} p&&predicate(p))return;Step(g);}throw new InvalidOperationException("Boundary fixture did not reach requested prompt: "+P(g)?.Prompt);}
    private static void Replay(GameEngine g,ContentRegistry r)
    {var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(Enumerable.Range(0,4).All(v=>SnapshotJson.Serialize(g.CreateSnapshot(v))==SnapshotJson.Serialize(restored.CreateSnapshot(v)))&&g.CardMovements.SequenceEqual(restored.CardMovements)&&JsonSerializer.Serialize(g.ResolutionStack)==JsonSerializer.Serialize(restored.ResolutionStack),"Four viewer and owning typed stack checkpoint replay.");}
    private static void Conserve(GameEngine g)=>Require(g.CreateCardZoneDiagnostics().Select(c=>c.CardId).Distinct().Count()==80,"Physical entities conserved.");
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private sealed class Fixture(ContentRegistry definitions,string mode):IGameContentPackage
    {
        public PackageManifest Manifest {get;}=new("fixture-lu-xun",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            foreach(var id in new[]{"boundary:qianxun","boundary:lianying"}) b.AddSkill(definitions.GetSkill(id));
            b.AddCard(new("fixture:duel","决斗","锦囊牌","固定真实锦囊",LegacyKind:CardKind.Duel));
            var gain=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:hold-gain","revision":1,"triggers":[{"id":"returned-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.private-turn-hold.return"],"optional":false,"effects":[{"op":"draw","target":"owner","amount":1}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:hold-gain":{"name":"返还获得子链","description":"返还后真摸一"}}}""");
            b.AddSkill(new("fixture:hold-gain","返还获得子链","返还后真摸一"){Program=gain.Programs["fixture:hold-gain"]});
            b.AddCard(new("fixture:delayed","闪电","锦囊牌","固定延时",LegacyKind:CardKind.Lightning));
            b.AddCard(new("fixture:draw-two","无中生有","锦囊牌","固定普通",LegacyKind:CardKind.DrawTwo));
            b.AddCard(new("fixture:borrowed","借刀杀人","锦囊牌","固定借刀",LegacyKind:CardKind.BorrowedSword));
            b.AddCard(new("fixture:weapon","诸葛连弩","装备牌","固定武器",LegacyKind:CardKind.Crossbow));
            b.AddCard(new("fixture:chain","铁索连环","锦囊牌","固定多目标",LegacyKind:CardKind.IronChain));
            if(mode=="mixed")
            {
                var mixed=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:mixed-loss","revision":1,"activations":[{"id":"discard","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":1,"condition":{"kind":"always"},"effects":[{"op":"selectOwnedCards","target":"owner","amount":6,"zones":["hand","equipment"],"resultBind":"chosen"},{"op":"moveBoundCards","target":"owner","sourceBind":"chosen","destination":"discardPile"}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:mixed-loss":{"name":"混合付款","description":"真实HE批次"}}}""");
                b.AddSkill(new("fixture:mixed-loss","混合付款","真实HE批次"){Program=mixed.Programs["fixture:mixed-loss"]});
            }
            if(mode=="nullification")b.AddSkill(definitions.GetSkill("classic:kanpo"));
            if(mode.StartsWith("death",StringComparison.Ordinal))
            {
                var effects="[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}]";
                var c=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:hold-child","revision":1,"triggers":[{"id":"paid-child","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.private-turn-hold.pay"],"optional":false,"priority":100,"effects":{{effects}}}]}]}""","""{"schemaVersion":3,"skills":{"fixture:hold-child":{"name":"真实付款子链","description":"真实付款子链"}}}""");
                b.AddSkill(new("fixture:hold-child","真实付款子链","真实付款子链"){Program=c.Programs["fixture:hold-child"]});
            }
            b.AddGeneral(new("fixture:lu-xun","界陆逊机制","supporter","boundary:qianxun","wu",mode=="death"?3:20,new[]{"boundary:lianying","fixture:hold-gain"}.Concat(mode=="mixed"?["fixture:mixed-loss"]:mode=="nullification"?["classic:kanpo"]:mode.StartsWith("death",StringComparison.Ordinal)?["fixture:hold-child"]:Array.Empty<string>()).ToArray()));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:other-{i}","其他"+i,"supporter","standard:none","qun",4,[]));
            b.AddDeck(new("fixture:lu-xun-deck","固定实体",4,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(i=>new ContentDeckPhysicalCard((mode=="borrowed"||mode=="mixed")?(i%2==0?"fixture:weapon":"fixture:borrowed"):mode=="lightning"?"fixture:delayed":mode=="draw-two"?"fixture:draw-two":mode=="iron-chain"?"fixture:chain":"fixture:duel",(Suit)(i%4),i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-lu-xun-check","界陆逊机制",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2},"fixture:lu-xun-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:lu-xun","fixture:other-1","fixture:other-2","fixture:other-3"]));
        }
    }
}
