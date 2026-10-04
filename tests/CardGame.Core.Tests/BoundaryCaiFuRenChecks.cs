using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryCaiFuRenChecks
{
    private const string Donation = "boundary:xianzhou-current", Listen = "boundary:qieting-current";
    private const string Driver = "fixture:cfr-driver", Movement = "fixture:cfr-movement", Hp = "fixture:cfr-hp", Entry = "fixture:cfr-entry";
    private const string Mode = "identity:classic-cfr-fixture";
    private const string Given = "program.all-equipment-donation.give";
    public static void WholeEquipmentDonationActualXRecoveryAndColdChildren()
    {
        var (g, r) = Create(); Play(g); Use(g, "gear", [0]); Play(g); Use(g, "hurt-two"); Play(g);
        var recipient = Enumerable.Range(1, 3).Single(s => g.CreateSnapshot(s).Players[s].Role == Role.Loyalist);
        var equipped = g.CreateSnapshot(0).Players[0].Equipment.Select(c => c.Id).ToArray();
        Require(equipped.Length == 1, "The small fixed fixture owns one actual Silver Lion equipment entity.");
        Donate(g, recipient); Reach(g, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == Hp);
        var paid = E<EquipmentDonationPaidEvent>(g).Single();
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Donation);
        Require(root.EquipmentDonation is { Stage: EquipmentDonationStage.PaidMovement, ActualDeliveredCount: 1 } receipt &&
            receipt.PaidCardIds.SequenceEqual(equipped) && paid.PaidCount == 1 && paid.ActualDeliveredCount == 1 &&
            !g.CreateSnapshot(0).Players[0].Equipment.Any() &&
            g.CreateSnapshot(recipient).Players[recipient].Hand.Any(c => c.Id == equipped[0]),
            "The entire actual equipment payment and delivered X are frozen before Silver Lion's real HP child and recipient choice.");
        Reject(g); g = Restore(g, r); Continue(g);
        Until(g, () => E<EquipmentDonationBenefitIssuedEvent>(g).Any(e => e.ProgramFrameId == paid.ProgramFrameId && e.Benefit == "recover"));
        if (P(g) is { PlayerSeat: 0, SkillPrompt.SkillId: Hp }) { g = Restore(g, r); Continue(g); }
        Play(g);
        Require(g.State.Players[0].Hp == g.State.Players[0].MaxHp &&
            E<EquipmentDonationBenefitIssuedEvent>(g).Count(e => e.ProgramFrameId == paid.ProgramFrameId && e.Benefit == "recover") == 1 &&
            equipped.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Equipment(0) &&
                m.To == CardLocation.Processing && m.Reason.Value == Given) == 1) &&
            !g.GetLegalActions().Any(a => a.ProgramSkillId == Donation),
            "A native Loyalist recipient issues exactly one X recovery; restored children never repay equipment or reopen the consumed limited opportunity.");
        g = Restore(g, r);
    }
    public static void DonationRecipientRangeDamageSourceLossAndGainDying()
    {
        var (g, r) = Create(twoKinds: true); Play(g);
        // At most 20 copies of either kind exist. Normal setup/draw removes at most 18,
        // so the bounded real equipment producer can find both without seed search.
        for (var i = 0; i < 21 && g.CreateSnapshot(0).Players[0].Equipment.Count < 2; i++) { Use(g, "gear", [0]); Play(g); }
        var recipient = Enumerable.Range(1, 3).First(s => g.CreateSnapshot(s).Players[s].Role == Role.Rebel);
        for (var i = 0; i < 21 && g.GetAttackRange(recipient) < 3; i++) { Use(g, "gear", [recipient]); Play(g); }
        Require(g.CreateSnapshot(0).Players[0].Equipment.Count == 2 && g.GetAttackRange(recipient) >= 3,
            "The same fixed fixture prepares two actual equipment entities and the recipient's actual weapon range through normal producers.");
        Donate(g, recipient); Until(g, () => E<EquipmentDonationPaidEvent>(g).Any()); g = Restore(g, r);
        Until(g, () => E<ProgramSkillResolvedEvent>(g).Any(e => e.SkillId == Donation)); Play(g);
        var paid = E<EquipmentDonationPaidEvent>(g).Single(); var damage = E<EquipmentDonationBenefitIssuedEvent>(g).Where(e => e.Benefit == "damage").ToArray();
        Require(paid.ActualDeliveredCount == 2 && damage.Length is >= 1 and <= 2 && damage.Select(e => e.TargetSeat).Distinct().Count() == damage.Length &&
            damage.All(e => e.RecipientSeat == recipient && e.TargetSeat != recipient) &&
            E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == recipient && e.Amount > 0 && damage.Any(d => d.TargetSeat == e.TargetSeat)) &&
            E<DamageAppliedEvent>(g).Where(e => e.SourceSeat == recipient).All(e => damage.Any(d => d.TargetSeat == e.TargetSeat)),
            "The real native recipient chooses distinct current-range targets up to actual X; each original damage source is the recipient, not the donor.");
        g = Restore(g, r);

        var (lost, lr) = Create(sourceLoss: true); Play(lost); Use(lost, "gear", [0]); Play(lost); Donate(lost, 1);
        Reach(lost, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == Movement); lost = Restore(lost, lr); Continue(lost); Play(lost);
        var lostPaid = E<EquipmentDonationPaidEvent>(lost).Single();
        Require(!lost.CreateSnapshot(0).Players[0].Skills!.Any(s => s.Id == Donation) &&
            E<EquipmentDonationBenefitIssuedEvent>(lost).Count() == 0 && lostPaid.ActualDeliveredCount == 1 &&
            E<EquipmentDonationEntityPaidEvent>(lost).All(e => lost.CardMovements.Count(m => m.CardId == e.CardId &&
                m.From == CardLocation.Equipment(0) && m.Reason.Value == Given) == 1),
            "Real source loss after payment cancels the unissued benefit, while actual entity movement and the limited opportunity stay paid once.");

        var (dying, dr) = Create(gainDying: true); Play(dying); Use(dying, "gear", [0]); Play(dying); Donate(dying, 1);
        Reach(dying, p => p.SkillPrompt?.SkillId == Entry);
        Require(dying.ResolutionStack.OfType<DyingFrame>().Any(d => d.VictimSeat == P(dying)!.PlayerSeat) &&
            E<EquipmentDonationPaidEvent>(dying).Single().ActualDeliveredCount == 1,
            "A genuine recipient gain observer can enter Dying after payment; the original donation owns the whole child chain.");
        dying = Restore(dying, dr); Until(dying, () => E<ProgramSkillResolvedEvent>(dying).Any(e => e.SkillId == Donation));
        Require(E<EquipmentDonationPaidEvent>(dying).Count() == 1 && E<EquipmentDonationEntityPaidEvent>(dying).Count() == 1,
            "The restored gain/Dying/rescue-or-death chain returns without repaying the donor entity or limited opportunity.");
    }
    public static void OriginalActualTurnTiersDistinctEquipmentAndDrawOptions()
    {
        var (g, r) = Create(); Play(g); Use(g, "gear", [1]); Play(g);
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Reach(g, p => ActivateChoice(p, Listen)); Reject(g); g = Restore(g, r); Activate(g, Listen);
        Reach(g, p => p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "actual-ended-equipment"));
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Listen);
        var qualification = root.ActualEndedEquipment!.Qualification;
        Require(qualification is { TurnOwnerSeat: 1, MaximumDistinctOptions: 2, UsedOnAnother: false, DamagedAnother: false } &&
            root.WindowContext!.ActualEndedEquipment == qualification,
            "The original other actual turn had only self equipment/Peach uses and no foreign damage; two distinct options freeze at its exact TurnEnded.");
        var card = P(g)!.Choices.First(c => c.Parameters.GetValueOrDefault("option") == "equipment").Cards.Single();
        Answer(g, c => c.Parameters.GetValueOrDefault("option") == "equipment" && c.Cards.SequenceEqual([card]));
        Reach(g, p => p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "actual-ended-equipment"));
        Require(P(g)!.Choices.All(c => c.Parameters.GetValueOrDefault("option") == "draw") &&
            g.CreateSnapshot(0).Players[0].Equipment.Any(c => c.Id == card),
            "The chosen current equipment is actually placed; the issued equipment option cannot repeat.");
        g = Restore(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("option") == "draw");
        Until(g, () => E<ProgramBindingResolvedEvent>(g).Any(e => e.SkillId == Listen && e.Completed));
        var options = E<ActualEndedTurnEquipmentOptionIssuedEvent>(g).Where(e => e.ProgramFrameId == root.Id).ToArray();
        Require(options.Select(e => e.Option).SequenceEqual(["equipment", "draw"]) && options.All(e => e.TurnNumber == qualification.TurnNumber && e.TurnOwnerSeat == 1) &&
            options.Single(e => e.Option == "draw").ActualDrawCount == 1 &&
            g.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Equipment(1) && m.To == CardLocation.Equipment(0)) == 1,
            "The original completed turn issues one actual placement and one actual draw, in chosen order, through restored typed returns.");
        g = Restore(g, r);
        var (decline, cr) = Create(); Play(decline); Accept(decline, new EndPlayPhaseCommand(0, decline.Revision, P(decline)!.PromptId));
        Reach(decline, p => ActivateChoice(p, Listen)); var before = decline.CardMovements.Count; decline = Restore(decline, cr);
        Answer(decline, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        Require(!E<ActualEndedTurnEquipmentOptionIssuedEvent>(decline).Any() && decline.CardMovements.Count == before,
            "Declining the optional original-turn window does not place equipment or draw a card.");
    }
    public static void ActualUseResponseExclusionLegacyNativeAndStrictComposition()
    {
        var (g, r) = Create(slashDeck: true); Play(g); Use(g, "enemy-duel", [1]); Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.RespondSlash);
        Answer(g, c => c.Parameters.GetValueOrDefault("response") == "slash");
        var response = E<CardActionAcceptedEvent>(g).Last(e => e.Action.ActorSeat == 0 && e.Action.Type == CardActionType.Response).Action;
        Require(!E<ActualTurnForeignUseTargetEvent>(g).Any(e => e.CardActionId == response.ActionId),
            "A genuine ordinary Slash response pays its entity but is not an actual use on another character.");
        g = Restore(g, r); Play(g);

        var (legacy, lr) = Create(slashDeck: true, legacy: true); Play(legacy); Accept(legacy, new EndPlayPhaseCommand(0, legacy.Revision, P(legacy)!.PromptId));
        Until(legacy, () => E<ActualTurnForeignUseTargetEvent>(legacy).Any(e => e.CardActionId is null && e.LegacyProducerProgramId is not null));
        var fact = E<ActualTurnForeignUseTargetEvent>(legacy).First(e => e.CardActionId is null && e.LegacyProducerProgramId is not null);
        Require(fact.EffectiveKind == CardKind.Slash && fact.ActorSeat == fact.ProviderSeat && fact.TargetSeat != fact.ActorSeat &&
            E<CardUseDeclaredEvent>(legacy).Any(e => e.ResolutionId == fact.CardUseFrameId && e.CardId == 0),
            "Registered Shensu retains a real declared action-null virtual Slash and exact typed producer rather than an invented accepted action.");
        legacy = Restore(legacy, lr);

        var (native, _) = Create(native: true); var prior = E<ActualEndedTurnEquipmentOptionIssuedEvent>(native).Any();
        for (var i = 0; i < 140 && !prior && native.State.Status != EngineStatus.Completed; i++)
        { Accept(native, new AdvanceOneStepCommand(native.Revision)); prior = E<ActualEndedTurnEquipmentOptionIssuedEvent>(native).Any(); }
        Require(prior && native.AcceptedCommands.All(c => c is StartGameCommand or AdvanceOneStepCommand),
            "Native optional activation and distinct equipment/draw choices use the normal AI path with public inputs and actual movement; no prompt is answered on AI's behalf.");
        var assembly = typeof(StandardClassicGeneralPackage).Assembly;
        string Read(string suffix) { using var stream = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.boundary-cai-fu-ren." + suffix)!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
        var rules = Read("rules.json"); var presentation = Read("presentation.json");
        foreach (var invalid in new[] { rules.Replace("\"otherLiving\", \"optional\": true", "\"own\", \"optional\": true"),
            rules.Replace("\"minTargets\": 1", "\"minTargets\": 0"), rules.Replace("\"afterTurnEnded\"", "\"turnEnding\"") })
        { var rejected = false; try { SkillProgramCatalog.Load(invalid, presentation); } catch (InvalidOperationException) { rejected = true; }
            Require(invalid != rules && rejected, "New full donation/actual-ended options reject wrong recipient counts and unbacked scopes/windows without widening classic contracts."); }
    }

    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0,4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool ActivateChoice(PendingDecision p, string id) => p.SkillPrompt?.SkillId == id && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Donate(GameEngine g, int target) => Accept(g, new UseProgramSkillCommand(0, Donation, "all-equipment-donation", [], [target], g.Revision, P(g)!.PromptId));
    private static void Activate(GameEngine g, string id) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == id);
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice,bool> predicate) { var p = P(g)!; Require(p.PlayerSeat == 0, "Only the actual human's prompt may be answered."); Accept(g, new AnswerPromptCommand(0, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0 } && p.SkillPrompt?.SkillId is Hp or Movement or Entry) Continue(g);
        else if (p is { PlayerSeat: 0 } && ActivateChoice(p, Listen)) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass")) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Reach(GameEngine g, Func<PendingDecision,bool> predicate) { for(var i=0;i<220;i++) { if(P(g) is { } p && predicate(p))return; Advance(g); } throw new InvalidOperationException("Fixed actual commands did not reach the expected child."); }
    private static void Until(GameEngine g, Func<bool> predicate) { for(var i=0;i<220;i++) { if(predicate())return; Advance(g); } throw new InvalidOperationException("Fixed actual commands did not finish the typed return."); }
    private static void Accept(GameEngine g, GameCommand command) { var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted,result.Error?.Message??"Rejected real command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames=JsonSerializer.Serialize(g.ResolutionStack), Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(), g.CardMovements, Commands=CommandJson.Serialize(g.AcceptedCommands), Zones=g.CreateCardZoneDiagnostics() });
    private static GameEngine Restore(GameEngine g, ContentRegistry r) { var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r); Require(State(g)==State(restored),"Four private views, exact scalar actual turn, paid equipment entities and typed children survive true JSON restore."); return restored; }
    private static void Reject(GameEngine g) { var p=P(g)!; var before=State(g); Require(!g.Submit(new AnswerPromptCommand(0,p.PromptId,new ChoiceId("not-published"),g.Revision)).Accepted&&before==State(g),"Rejected input preserves real payments, all views and the original actual-turn qualification."); }
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
    private static (GameEngine,ContentRegistry) Create(bool twoKinds=false,bool sourceLoss=false,bool gainDying=false,bool slashDeck=false,bool legacy=false,bool native=false)
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(twoKinds,sourceLoss,gainDying,slashDeck,legacy,native));
        var g=GameEngine.CreateStandard(new GameOptions { Seed=31,PlayerCount=4,ModeId=Mode,HumanSeat=native?-1:0,HumanRole=native?null:Role.Lord,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=4 },registry);
        Accept(g,new StartGameCommand()); if(!native){Reach(g,p=>p.Kind==DecisionKind.SelectGeneral&&p.PlayerSeat==0);Accept(g,new SelectGeneralCommand(0,"fixture:cfr-owner",g.Revision,P(g)!.PromptId));} return(g,registry);
    }
    private sealed class Fixture(bool twoKinds,bool sourceLoss,bool gainDying,bool slashDeck,bool legacy,bool native):IGameContentPackage
    {
        public PackageManifest Manifest {get;}=new("fixture-boundary-cai-fu-ren",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var loss=sourceLoss?",{\"op\":\"loseOwnerSkillsAndGrant\",\"target\":\"owner\",\"skillIds\":[\""+Donation+"\"],\"sourceBind\":\"standard:none\"}":"";
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
            {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":70,"priority":0}],"activations":[
            {"id":"gear","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
            {"id":"hurt-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":2}]},
            {"id":"enemy-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":1,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]}]},
            {"id":"{{Hp}}","revision":1,"triggers":[{"id":"hp","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"{{Movement}}","revision":1,"triggers":[{"id":"move","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["{{Given}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}{{loss}}]}]},
            {"id":"{{Entry}}","revision":1,"triggers":[{"id":"entry","window":"dyingEntering","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"fixture:cfr-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perSourceOwner","movementReasons":["{{Given}}"],"optional":false,"effects":[{"op":"loseHp","target":"owner","amount":6}]}]}]}
            """;
            var labels=new Dictionary<string,object>{[Driver]=new{name="真实装备驱动",description="实际装备及对方用牌"},[Hp]=new{name="实际HP子窗",description="真实回复",optionLabels=new Dictionary<string,string>{["continue"]="继续"}},[Movement]=new{name="真实付款观察",description="真实原批及失源",optionLabels=new Dictionary<string,string>{["continue"]="继续"}},[Entry]=new{name="实际濒死入口",description="实际收牌濒死",optionLabels=new Dictionary<string,string>{["continue"]="继续"}},["fixture:cfr-gain"]=new{name="真实收牌损失",description="实际gain"}};
            var c=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=3,skills=labels}));
            foreach(var id in c.Programs.Keys)b.AddSkill(new(id,id,"实际通用能力"){Program=c.Programs[id],ProgramPresentation=c.Presentations[id]});
            foreach(var owner in new[]{true,false})b.AddSkill(new(owner?"fixture:cfr-owner-weight":"fixture:cfr-other-weight","固定公开选将","正式角色权重")
            {SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>(role==Role.Lord)==owner?10000d:-10000d)});
            var extras=new List<string>{Listen,"fixture:cfr-owner-weight"};if(!native)extras.Add(Driver);if(!native)extras.Add(Hp);if(sourceLoss)extras.Add(Movement);
            b.AddGeneral(new("fixture:cfr-owner","当前蔡夫人来源","supporter",Donation,"qun",3,extras));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:cfr-other-{i}","其他角色","supporter","fixture:cfr-other-weight","wei",6,
                (gainDying?new[]{"fixture:cfr-gain",Entry}:Array.Empty<string>()).Concat(legacy?new[]{"boundary:shensu"}:Array.Empty<string>()).ToArray()));
            var physical=Enumerable.Range(0,120).Select(i=>new ContentDeckPhysicalCard(i<40?(twoKinds&&i%2==1?"classic:fangtian-halberd":"classic:silver-lion"):slashDeck?"standard:slash":"standard:peach",Suit.Club,7)).ToArray();
            b.AddDeck(new("fixture:cfr-deck","小固定实际实体",4,0,[]){PhysicalCards=physical});
            b.AddMode(new(Mode,"实际全装备及原回合",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2},"fixture:cfr-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:cfr-owner","fixture:cfr-other-1","fixture:cfr-other-2","fixture:cfr-other-3"]));
        }
    }
}
