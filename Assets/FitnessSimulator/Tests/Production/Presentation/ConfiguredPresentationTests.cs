using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Configuration;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
namespace FitnessSimulator.Game.Tests.Presentation
{
    public sealed class ConfiguredPresentationTests
    {
        private string directory; private GameObject host; private GameFrontendView ui; private Stub stub;
        private sealed class Stub : IGameFrontend,IFrontendContentProvider
        {
            public ContentPackage Package; public int Commands;
            public FrontendView View=new FrontendView{RunId="p2",ScreenId="menu",Revision=1,CanOpenAuxiliary=true,Actions=Array.Empty<FrontendOption>()};
            public ContentPackage ReadContent()=>Package;
            public FrontendView ReadView()=>View;
            public FrontendReply Execute(FrontendAction action){Commands++;return new FrontendReply{Accepted=true};}
            public void Advance(double value){} public void SetSuspended(bool value){}
        }
        [SetUp]public void Setup(){directory=Path.Combine(Path.GetTempPath(),"Fitness-Presentation-P2",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);}
        [TearDown]public void Cleanup(){if(host)UnityEngine.Object.DestroyImmediate(host);}
        private void Start(ContentPackage package)
        {
            stub=new Stub{Package=package};host=new GameObject("ConfigurationPresentation");ui=host.AddComponent<GameFrontendView>();ui.PreferenceFileOverride=Path.Combine(directory,"preferences.json");
            ui.Initialize(stub,AssetDatabase.LoadAssetAtPath<Font>("Assets/FitnessSimulator/Production/Presentation/Fonts/NotoSansCJKsc-Regular.otf"));
        }
        private void Invoke(string method,params object[] args)=>typeof(GameFrontendView).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,args);
        private Button Find(string name)=>host.GetComponentsInChildren<Button>(false).Last(x=>x.name==name);
        [Test]public void ProviderControlsCombinationNamesAndExplicitPrefillWithoutSubmitting()
        {
            var docs=ContentRuntime.Current.Export().Documents.ToDictionary(x=>x.Id,x=>x.Json);
            var plans=ContentJson.Deserialize<PlansDocument>("plans",docs["plans"]);var combo=plans.Combinations.Single(x=>x.Kind==TrainingCombination.ChestBiceps);combo.Name="文件组合名称";combo.PrefillExerciseIds=new[]{"dumbbell_curl"};docs["plans"]=ContentJson.Serialize(plans);
            Start(ContentPackage.FromDocuments(docs));var original=new PlanEntryDraft{EntryId="old",ExerciseId="bench_press"};var slot=new PlanSlotDraft{Combination=TrainingCombination.ChestBiceps,Entries=new[]{original},AnchorId="noon"};
            Invoke("EditPlanSlot",slot);Assert.That(host.GetComponentsInChildren<Text>(false).Any(t=>t.text.Contains("文件组合名称")),Is.True);
            Invoke("ConfirmPrefill",slot);Assert.That(slot.Entries.Single(),Is.SameAs(original));Find("载入预填").onClick.Invoke();
            Assert.That(slot.Entries.Single().ExerciseId,Is.EqualTo("dumbbell_curl"));Assert.That(stub.Commands,Is.Zero);
        }
        [Test]public void KnowledgeDirectoryAndAudioPreferencesPersistOutsideRunWithoutCommands()
        {
            Start(ContentRuntime.Current);Find("知识与声音").onClick.Invoke();Find("音乐：开").onClick.Invoke();Find("知识卡自动提示：开").onClick.Invoke();Find("日常活动也算").onClick.Invoke();
            Assert.That(host.GetComponentsInChildren<Text>(false).Any(t=>t.text.Contains("与游戏不同：")&&t.text.Contains("核验：")),Is.True);
            var p=PresentationPreferenceStore.Load(ui.PreferenceFileOverride,new AudioPreferences());Assert.That(p.Audio.MusicEnabled,Is.False);Assert.That(p.CardsEnabled,Is.False);Assert.That(stub.Commands,Is.Zero);
            Find("知识目录").onClick.Invoke();Assert.That(Find("日常活动也算").interactable,Is.True);Assert.That(host.GetComponents<AudioSource>().Length,Is.EqualTo(2));
        }
        [Test]public void ConfiguredAudioResourcesMatchHashesAndSuspendDoesNotCreateTracks()
        {
            AudioContent.ValidateContent();KnowledgeContent.ValidateContent();
            foreach(var e in AudioContent.Read().Entries){string path="Assets/FitnessSimulator/Production/Resources/"+e.AssetRef+".wav";using(var sha=SHA256.Create())Assert.That(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-",""),Is.EqualTo(e.Sha256).IgnoreCase);var clip=Resources.Load<AudioClip>(e.AssetRef);Assert.That(clip.samples,Is.GreaterThan(0));}
            Start(ContentRuntime.Current);var audio=host.GetComponent<AudioPresentation>();var before=host.GetComponents<AudioSource>().Single(x=>x.loop).clip;audio.SetSuspended(true);audio.SetSuspended(false);audio.SetMusic("daily");Assert.That(host.GetComponents<AudioSource>().Length,Is.EqualTo(2));Assert.That(host.GetComponents<AudioSource>().Single(x=>x.loop).clip,Is.SameAs(before));
        }
    }
}
