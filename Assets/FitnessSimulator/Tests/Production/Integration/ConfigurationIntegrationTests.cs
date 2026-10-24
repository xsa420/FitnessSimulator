using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Configuration;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Integration
{
    public sealed class ConfigurationIntegrationTests
    {
        private string directory;
        [SetUp] public void Setup() { directory=Path.Combine(Path.GetTempPath(),"Fitness-Configuration-Integration",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);TestContext.WriteLine(directory); }
        private static FrontendReply Send(GameSession session,string id,string value=null) { var v=session.ReadView();return session.Execute(new FrontendAction { ActionId=id,StringValue=value,RunId=v.RunId,ExpectedRevision=v.Revision,OperationId=Guid.NewGuid().ToString("N") }); }
        private static void Copy(string source,string target) { Directory.CreateDirectory(target);foreach(string file in Directory.GetFiles(source,"*",SearchOption.AllDirectories)) {string dest=Path.Combine(target,file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(file,dest);} }
        [TestCase("20261023-080423-916/15893ffae6ba41bfb0bdcc583003993d/MorningChampionAfternoonLoss", "ending-segment")]
        public void EarlierSchemaEndingArchiveKeepsFrozenBusinessWhileBindingExplicitAdapter(string relative, string actionId)
        {
            // Genuine earlier schema archives; these are not claimed to be M3R1 release-source-equivalent.
            string source=Path.GetFullPath("TestResults/Automation/"+relative+"/save"), target=Path.Combine(directory,"earlier-ending");
            var files=Directory.GetFiles(source,"*",SearchOption.AllDirectories).ToDictionary(f=>f,File.ReadAllBytes);Copy(source,target);
            var codec=new UnityJsonStateCodec();
            using(var session=GameSession.OpenAutomation(target,1)) {
                var before=session.InspectSnapshot();Assert.That(before,Is.Not.Null,session.Diagnostic);
                File.WriteAllText(Path.Combine(directory,"ending-before.json"),codec.Encode(before));
                Assert.That(Send(session,"continue-game").Accepted,Is.True);
                var option=session.ReadView().Actions.Single(a=>a.Id==actionId&&a.Enabled);
                Assert.That(Send(session,option.Id,option.StringValue).Accepted,Is.True);
                var after=session.InspectSnapshot();
                foreach(var f in before.Fragments.Where(f=>f.Owner!="progression")) Assert.That(after.FindFragment(f.Owner,f.Scope).Json,Is.EqualTo(f.Json),f.Owner+"/"+f.Scope);
                Assert.That(after.FindFragment("content-migration","shared").Json,Does.Contain("SourceEndingContinuation"));
                File.WriteAllText(Path.Combine(directory,"ending-after.json"),codec.Encode(after));
            }
            using(var reopened=GameSession.OpenAutomation(target,999))Assert.That(reopened.InspectSnapshot(),Is.Not.Null,reopened.Diagnostic);
            foreach(var file in files)Assert.That(File.ReadAllBytes(file.Key),Is.EqualTo(file.Value));
        }
        [Test] public void EarlierNaturalTerminalArchiveRetainsEarnedTemplateGrants()
        {
            string source=Path.GetFullPath("TestResults/Automation/20261023-091303-404/zero-173aa90e0f724734bf0915ac13cf6ba1/save"), target=Path.Combine(directory,"terminal-grants");Copy(source,target);
            using(var session=GameSession.OpenAutomation(target,1)) {
                var original=session.InspectSnapshot();Assert.That(original,Is.Not.Null,session.Diagnostic);
                var permanent=JsonUtility.FromJson<FitnessSimulator.Game.Progression.ProgressionPermanent>(original.PermanentFragments.Single(f=>f.Owner=="progression").Json);
                Assert.That(permanent.TemplateGrants.Count,Is.GreaterThan(0));
                Assert.That(Send(session,"continue-game").Accepted,Is.True);
                Assert.That(session.ReadView().Actions.Any(a=>a.Id=="ending-continue"&&a.Enabled),Is.False);
                foreach(var grant in permanent.TemplateGrants) Assert.That(session.ReadView().Actions.Any(a=>a.Id=="new-game"&&a.Enabled&&a.StringValue==grant.TemplateId),Is.True,grant.TemplateId);
                Assert.That(new UnityJsonStateCodec().Encode(session.InspectSnapshot()),Is.EqualTo(new UnityJsonStateCodec().Encode(original)));
            }
        }
        [TestCase(false)] [TestCase(true)]
        public void ExplicitOldSchemaTrainingFixturePreservesRequestAndLockedResult(bool playing)
        {
            // Structural recovery fixture from an archived, explicitly injected-domain test snapshot.
            // Clearing its unrelated ending and log visibility makes an isolated store, not a historical save claim.
            var codec=new UnityJsonStateCodec();
            var root=codec.Decode<RootEnvelope>(File.ReadAllText("TestResults/Automation/20261023-052934-678/e08ae26e46fe450dac00e68f61d69aca/g06-result-ready-ending.json"));
            root.Generation=0;root.Revision=0;root.LastCommitId=null;root.LogPrefixes.Clear();root.RecentReceipts.Clear();root.PermanentFragments.Clear();
            root.SetFragment("progression","player",1,0,codec.Encode(new FitnessSimulator.Game.Progression.EndingProgression(codec).CreateRun(root.RunId)));
            var training=codec.Decode<TrainingDayState>(root.FindFragment("training","player").Json);
            if(playing) { training.Workout.Phase=FitnessSimulator.Game.Training.WorkoutPhase.Playing;training.Workout.PendingResult=null;root.SetFragment("training","player",1,0,codec.Encode(training));SetFixturePlayCursor(root,codec); }
            string frozen=root.FindFragment("training","player").Json;
            string target=Path.Combine(directory,"old-training-fixture");
            using(var store=new LocalCheckpointStore(target,codec)) {
                var committed=store.Commit(0,root,new OperationReceipt{RunId=root.RunId,OperationId="structural-fixture",SemanticKey="structural-fixture",PayloadHash="explicit-old-schema-not-historical-save"},Array.Empty<LogAppend>());
                Assert.That(committed.Status,Is.EqualTo(CommitStatus.Committed));
            }
            using(var session=GameSession.OpenAutomation(target,1)) {
                Assert.That(session.InspectSnapshot(),Is.Not.Null,session.Diagnostic);
                Assert.That(session.InspectSnapshot().FindFragment("training","player").Json,Is.EqualTo(frozen));
                Assert.That(Send(session,"continue-game").Accepted,Is.True);
                if(!playing)for(int i=0;i<3000&&!session.ReadView().Actions.Any(a=>a.Id=="settle"&&a.Enabled);i++)session.Advance(0);
                if(playing) Assert.That(session.ReadView().TrainingInput,Is.Not.Null);
                else Assert.That(session.ReadView().Actions.Any(a=>a.Id=="settle"&&a.Enabled),Is.True);
                Assert.That(session.InspectSnapshot().FindFragment("training","player").Json,Is.EqualTo(frozen));
                if(!playing) {
                    var view=session.ReadView();var action=new FrontendAction{ActionId="settle",RunId=view.RunId,ExpectedRevision=view.Revision,OperationId="old-result-settle-once"};
                    Assert.That(session.Execute(action).Accepted,Is.True);
                    var historical=codec.Decode<RootEnvelope>(File.ReadAllText("TestResults/Automation/20261023-052934-678/e08ae26e46fe450dac00e68f61d69aca/g06-settled-once.json"));
                    Assert.That(codec.Encode(codec.Decode<BodyState>(session.InspectSnapshot().FindFragment("body","player").Json)),Is.EqualTo(codec.Encode(codec.Decode<BodyState>(historical.FindFragment("body","player").Json))),"Settlement must match the archived old-rule body calculation");
                    Assert.That(codec.Encode(codec.Decode<TrainingDayState>(session.InspectSnapshot().FindFragment("training","player").Json).Progress),Is.EqualTo(codec.Encode(codec.Decode<TrainingDayState>(historical.FindFragment("training","player").Json).Progress)));
                    string settled=codec.Encode(session.InspectSnapshot());Assert.That(session.Execute(action).Accepted,Is.True);Assert.That(codec.Encode(session.InspectSnapshot()),Is.EqualTo(settled));
                    Assert.That(session.InspectSnapshot().FindFragment("content-migration","shared"),Is.Not.Null);
                    File.WriteAllText(Path.Combine(directory,"settled.json"),settled);
                }
            }
            using(var reopened=GameSession.OpenAutomation(target,99)) {Assert.That(reopened.InspectSnapshot(),Is.Not.Null,reopened.Diagnostic);if(!playing)Assert.That(codec.Encode(reopened.InspectSnapshot()),Is.EqualTo(File.ReadAllText(Path.Combine(directory,"settled.json"))));}
        }
        private static void SetFixturePlayCursor(RootEnvelope root,UnityJsonStateCodec codec)
        {
            var flow=codec.Decode<FitnessSimulator.Game.Flow.FlowState>(root.FindFragment("flow","player").Json);
            string play=flow.Tree.Nodes.First(n=>n.Handler=="play").Id;flow.CurrentNodeId=play;
            flow.Cursors.RemoveAll(c=>c.NodeId!=play&&flow.Tree.Nodes.Single(n=>n.Id==c.NodeId).Handler=="settle");
            flow.Cursors.Single(c=>c.NodeId==play).Phase=FitnessSimulator.Game.Flow.NodePhase.Started;
            root.SetFragment("flow","player",1,0,codec.Encode(flow));
        }
        [Test] public void RealSealedM3R1SaveMigratesExplicitlyWithoutChangingCommittedActorState()
        {
            string source=Path.GetFullPath("TestResults/Automation/20261023-105355-155/4154ec454a4a45039cce41e81b800b98/save");
            Assert.That(Directory.Exists(source),Is.True,"Required original M3R1 two-day evidence");
            var originalFiles=Directory.GetFiles(source,"*",SearchOption.AllDirectories).ToDictionary(f=>f,File.ReadAllBytes);
            string target=Path.Combine(directory,"copied-legacy-save");Copy(source,target);
            var codec=new UnityJsonStateCodec(); RootEnvelope original;
            using(var store=new LocalCheckpointStore(target,codec))original=store.Load().Root;
            Assert.That(original.FindFragment("content-package","shared"),Is.Null);
            string before=codec.Encode(original);File.WriteAllText(Path.Combine(directory,"legacy-before.json"),before);
            using(var session=GameSession.OpenAutomation(target,1)) {
                Assert.That(session.InspectSnapshot(),Is.Not.Null,session.Diagnostic);
                Assert.That(codec.Encode(session.InspectSnapshot()),Is.EqualTo(before),"Opening must not relabel or rewrite legacy data");
                Assert.That(Send(session,"continue-game").Accepted,Is.True);
                Assert.That(session.ReadView().Actions.Any(a=>a.Id=="next-day"&&a.Enabled),Is.True);
                Assert.That(Send(session,"next-day").Accepted,Is.True);
                var migrated=session.InspectSnapshot();
                Assert.That(migrated.FindFragment("content-migration","shared").Json,Does.Contain("m3r1-schema2-to-configr1.v1").And.Contain(session.LoadedContentHash));
                Assert.That(migrated.FindFragment("content-package","shared"),Is.Not.Null);
                foreach(var fragment in original.Fragments.Where(f=>new[]{"body","life","plan","training","flow","relationships","inventory","competition"}.Contains(f.Owner)))
                    Assert.That(migrated.FindFragment(fragment.Owner,fragment.Scope).Json,Is.EqualTo(fragment.Json),fragment.Owner+"/"+fragment.Scope);
                Assert.That(migrated.PermanentFragments.Count,Is.EqualTo(original.PermanentFragments.Count));
                foreach(var permanent in original.PermanentFragments)Assert.That(codec.Encode(migrated.PermanentFragments.Single(f=>f.Owner==permanent.Owner&&f.Scope==permanent.Scope)),Is.EqualTo(codec.Encode(permanent)));
                File.WriteAllText(Path.Combine(directory,"migrated.json"),codec.Encode(migrated));
            }
            using(var restored=GameSession.OpenAutomation(target,999)) { Assert.That(restored.InspectSnapshot(),Is.Not.Null,restored.Diagnostic);Assert.That(restored.InspectSnapshot().FindFragment("content-package","shared"),Is.Not.Null); }
            foreach(var file in originalFiles)Assert.That(File.ReadAllBytes(file.Key),Is.EqualTo(file.Value),"Sealed source must remain unchanged");
        }
        [Test] public void DisplayDictionaryAndTemplateMetadataReachActualNewGameView()
        {
            var sources=ContentRuntime.Current.Export().Documents.ToDictionary(d=>d.Id,d=>d.Json);
            var body=ContentJson.Deserialize<BodyContentDefinition>("body",sources["body"]);
            var strength=body.Attributes.Single(a=>a.Id=="strength");strength.Name="配置力量";strength.DisplayDecimals=3;
            sources["body"]=ContentJson.Serialize(body);
            var templates=ContentJson.Deserialize<TemplatesDocument>("templates",sources["templates"]);
            var selected=templates.Templates.Single(t=>t.Id==templates.DefaultId);selected.Description="配置开局说明";selected.Advantage="配置优势";selected.Drawback="配置短板";
            templates.Initializations.Single(i=>i.Id==selected.InitializationId).Body.Regions[0].StrengthMicro.Value=10123456;
            sources["templates"]=ContentJson.Serialize(templates);
            var package=ContentPackage.FromDocuments(sources);GameSession.ValidateContentPackage(package);
            using(var session=GameSession.OpenAutomationContent(Path.Combine(directory,"view"),1,package)) {
                Assert.That(session.ReadView().Actions.First(a=>a.Id=="new-game"&&a.Enabled).Label,Does.Contain("配置开局说明").And.Contain("配置优势").And.Contain("配置短板"));
                Assert.That(Send(session,"new-game",templates.DefaultId).Accepted,Is.True);Assert.That(Send(session,"save-plan",JsonUtility.ToJson(PlanCatalog.CreateBenchPressSlice())).Accepted,Is.True);
                string rows=string.Join("\n",session.ReadView().BodyRows);Assert.That(rows,Does.Contain("配置力量").And.Contain("10.123"));
                Assert.That(((IFrontendContentProvider)session).ReadContent().Hash,Is.EqualTo(package.Hash));
            }
        }
        [Test] public void ConfiguredRelationshipOptionsAndResponseUseApplicationConsumer()
        {
            var sources=ContentRuntime.Current.Export().Documents.ToDictionary(d=>d.Id,d=>d.Json);
            var relationship=ContentJson.Deserialize<FitnessSimulator.Game.Life.Relationships.RelationshipContentDocument>("relationships",sources["relationships"]);
            var dialogue=relationship.Characters.Single(c=>c.CharacterId=="coach").Dialogues.First();dialogue.Text="配置对话";dialogue.MinimumAffection=25;relationship.InviteThreshold=25;relationship.TeachingPrice=37;sources["relationships"]=ContentJson.Serialize(relationship);
            var actors=ContentJson.Deserialize<FitnessSimulator.Game.Actors.ActorContentDocument>("actors",sources["actors"]);actors.Characters.Single(c=>c.Id=="coach").Name="配置教练";sources["actors"]=ContentJson.Serialize(actors);
            var narrative=ContentJson.Deserialize<FitnessSimulator.Game.Narrative.NarrativeDocument>("narrative",sources["narrative"]);var flow=ContentJson.Deserialize<FlowContentDocument>("flow",sources["flow"]);
            string storyId=flow.Events.Single(e=>e.Id==dialogue.ResponseEventId).Nodes.Single().Call.StoryId;
            narrative.Stories.Single(s=>s.Id==storyId).Nodes.Single(n=>n.Kind=="line").Text="配置后继回应";sources["narrative"]=ContentJson.Serialize(narrative);
            var package=ContentPackage.FromDocuments(sources);GameSession.ValidateContentPackage(package);
            using(ContentRuntime.Push(package))using(var session=GameSession.OpenAutomationContent(Path.Combine(directory,"consumer"),1,package)) {
                var codec=new UnityJsonStateCodec();var root=codec.Decode<RootEnvelope>(File.ReadAllText("TestResults/Automation/20261023-052934-678/e08ae26e46fe450dac00e68f61d69aca/g06-result-ready-ending.json"));
                var training=codec.Decode<TrainingDayState>(root.FindFragment("training","player").Json);training.Workout.Phase=FitnessSimulator.Game.Training.WorkoutPhase.PreSet;root.SetFragment("training","player",1,0,codec.Encode(training));
                SetFixturePlayCursor(root,codec);
                root.SetFragment("relationship-rest","player",1,0,"{\"WorkoutId\":\""+training.Workout.WorkoutId+"\",\"NextSetSequence\":"+training.Workout.NextSetSequence+",\"Open\":true}");
                var state=FitnessSimulator.Game.Life.Relationships.RelationshipRules.Initialize(root.RunId,"player");state.Day=training.Workout.Quota.Day;state.Affections.Single(a=>a.CharacterId=="coach").Value=25;root.SetFragment("relationships","player",1,0,codec.Encode(state));
                root.SetFragment("workout-interaction","player",1,0,codec.Encode(new FitnessSimulator.Game.Life.Relationships.WorkoutInteraction{RunId=root.RunId,ActorId="player",WorkoutId=training.Workout.WorkoutId,Day=state.Day,PresentCharacterIds=new[]{"coach"}}));
                var options=new List<FrontendOption>();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                typeof(GameSession).GetMethod("AppendRelationshipOptions",flags).Invoke(session,new object[]{root,options});
                var selected=options.Single(a=>a.Id=="rel-dialogue"&&a.IntValue==dialogue.Option);Assert.That(selected.Label,Does.Contain("配置教练").And.Contain("配置对话"));Assert.That(options.Any(a=>a.Id=="rel-invite"),Is.True);Assert.That(options.Single(a=>a.Id=="rel-teach").Label,Does.Contain("37"));
                object[] args={root,new FrontendAction{ActionId=selected.Id,StringValue=selected.StringValue,IntValue=selected.IntValue},null};
                Assert.That((bool)typeof(GameSession).GetMethod("TryPrepareRelationship",flags).Invoke(session,args),Is.True);
                var outcome=(PreparedOutcome)args[2];Assert.That(outcome.Result,Does.Contain("配置后继回应"));Assert.That(outcome.Appends.Any(a=>a.Kind=="RelationshipResponse"&&a.Json.Contains(dialogue.ResponseEventId)),Is.True);
                Assert.That(codec.Decode<FitnessSimulator.Game.Life.Relationships.WorkoutInteraction>(root.FindFragment("workout-interaction","player").Json).Consumed,Is.True);
            }
        }
        [Test] public void GlobalValidationRejectsMissingChoiceInputsAndDisplayReferences()
        {
            var sources=ContentRuntime.Current.Export().Documents.ToDictionary(d=>d.Id,d=>d.Json);
            var templates=ContentJson.Deserialize<TemplatesDocument>("templates",sources["templates"]);templates.Initializations[0].InventoryInitialRef="missing-initial";sources["templates"]=ContentJson.Serialize(templates);
            Assert.That(Assert.Catch(()=>GameSession.ValidateContentPackage(ContentPackage.FromDocuments(sources))).Message,Does.Contain("templates"));
            sources=ContentRuntime.Current.Export().Documents.ToDictionary(d=>d.Id,d=>d.Json);
            var flow=ContentJson.Deserialize<FlowContentDocument>("flow",sources["flow"]);flow.Events.First(e=>e.Nodes.Any(n=>n.Handler=="narrative")).Nodes[0].Call.ChoiceAccess=new[]{new ConfiguredChoiceAccess {ChoiceId="not-a-choice",Condition="",CostNodeId="",ProbabilitySource=""}};sources["flow"]=ContentJson.Serialize(flow);
            Assert.That(Assert.Catch(()=>GameSession.ValidateContentPackage(ContentPackage.FromDocuments(sources))).Message,Does.Contain("ChoiceAccess"));
        }
    }
}
