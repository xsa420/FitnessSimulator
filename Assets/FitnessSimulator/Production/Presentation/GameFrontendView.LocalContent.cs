using System;
using System.IO;
using System.Linq;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Configuration;
using UnityEngine;
namespace FitnessSimulator.Game.Presentation
{
    public sealed partial class GameFrontendView
    {
        private AudioPresentation audioPresentation;
        private PresentationPreferences localPreferences;
        private string preferencesPath, audioHash, audioScreen, lastInputIdentity;
        private int lastValid, lastFailed;
        private string queuedKnowledge;
        public string PreferenceFileOverride { get; set; }
        private bool LocalReading => modalKind=="knowledge"||modalKind=="knowledge-card";
        private ContentPackage ReadUiContent() => (frontend as IFrontendContentProvider)?.ReadContent() ?? ContentRuntime.Current;
        private void InitializeLocalPresentation()
        {
            preferencesPath=PreferenceFileOverride??Path.Combine(UnityEngine.Application.persistentDataPath,"presentation-preferences.json");
            using(ContentRuntime.Push(ReadUiContent()))localPreferences=PresentationPreferenceStore.Load(preferencesPath,AudioContent.Defaults());
            audioPresentation=gameObject.AddComponent<AudioPresentation>();
        }
        private void RenderLocalPresentation()
        {
            var package=ReadUiContent();
            if(audioHash!=package.Hash){using(ContentRuntime.Push(package))audioPresentation.Configure(localPreferences.Audio);audioHash=package.Hash;}
            string screen=view.ScreenId??"";
            audioPresentation.SetMusic(screen.StartsWith("competition")?"competition":"daily");
            if(audioScreen!=screen){
                if(screen.StartsWith("ending-"))audioPresentation.PlayCue("ending");
                if((view.Actions??Array.Empty<FrontendOption>()).Any(x=>x.Id=="settle"))audioPresentation.PlayCue("result");
                if((view.Actions??Array.Empty<FrontendOption>()).Any(x=>x.Id=="next-day"))QueueKnowledge("day-end");
                audioScreen=screen;
            }
            var input=view.TrainingInput;
            string identity=view.RunId+"/"+screen;
            if(input!=null){
                if(lastInputIdentity==identity){if(input.EffectiveReps>lastValid)audioPresentation.PlayCue("training_valid");if(input.FailedAttempts>lastFailed)audioPresentation.PlayCue("training_failed");}
                lastValid=input.EffectiveReps;lastFailed=input.FailedAttempts;lastInputIdentity=identity;
            }else lastInputIdentity=null;
            if(queuedKnowledge!=null&&modalKind==null&&view.CanOpenAuxiliary&&input==null&&view.BenchPress==null){var id=queuedKnowledge;queuedKnowledge=null;ShowKnowledgeCard(id,true);}
        }
        private KnowledgeDocument Knowledge => ReadUiContent().Get<KnowledgeDocument>("knowledge");
        private void QueueKnowledge(string trigger)
        {
            if(!localPreferences.CardsEnabled||queuedKnowledge!=null)return;
            var card=Knowledge.Cards.OrderBy(x=>x.DisplayOrder).FirstOrDefault(x=>x.Trigger==trigger&&!localPreferences.DisabledCards.Contains(x.Id)&&!localPreferences.Presented.Contains(x.Id+"/"+x.Version));
            if(card!=null)queuedKnowledge=card.Id;
        }
        private void PersistPreferences(){PresentationPreferenceStore.Save(preferencesPath,localPreferences);using(ContentRuntime.Push(ReadUiContent()))audioPresentation.Configure(localPreferences.Audio);}
        private void ShowKnowledgeDirectory()
        {
            if(view==null||view.TrainingInput!=null&&!view.TrainingInput.IsComplete||view.BenchPress!=null&&!view.BenchPress.IsComplete)return;
            OpenModal("knowledge","现实知识与游戏规则分开阅读。目录可随时重读已关闭的卡片；阅读不提交游戏操作。");
            foreach(var card in Knowledge.Cards.OrderBy(x=>x.DisplayOrder)){string id=card.Id;Button(card.Title,modalContent,()=>ShowKnowledgeCard(id,false));}
            Button("知识卡自动提示："+(localPreferences.CardsEnabled?"开":"关"),modalContent,()=>{localPreferences.CardsEnabled=!localPreferences.CardsEnabled;PersistPreferences();ShowKnowledgeDirectory();});
            Button("音乐："+(localPreferences.Audio.MusicEnabled?"开":"关"),modalContent,()=>{localPreferences.Audio.MusicEnabled=!localPreferences.Audio.MusicEnabled;PersistPreferences();ShowKnowledgeDirectory();});
            Button("音效："+(localPreferences.Audio.SfxEnabled?"开":"关"),modalContent,()=>{localPreferences.Audio.SfxEnabled=!localPreferences.Audio.SfxEnabled;PersistPreferences();ShowKnowledgeDirectory();});
            foreach(float volume in new[]{0f,.25f,.5f,1f}){float v=volume;Button("音乐音量 "+v.ToString("P0"),modalContent,()=>{localPreferences.Audio.MusicVolume=v;PersistPreferences();});Button("音效音量 "+v.ToString("P0"),modalContent,()=>{localPreferences.Audio.SfxVolume=v;PersistPreferences();});}
            Button("返回游戏",ModalButtons(),CloseModal);
        }
        private void ShowKnowledgeCard(string id,bool automatic)
        {
            var c=Knowledge.Cards.Single(x=>x.Id==id);
            OpenModal("knowledge-card","现实知识｜"+c.Title+"\n\n"+c.Body+"\n\n适用范围："+c.ScopeNote+"\n来源："+c.SourceOrganization+" · "+c.SourceDocument+"\n"+c.SourceUrl+"\n来源日期："+c.SourceDate+"　核验："+c.VerifiedAt+"\n\n与游戏不同："+c.GameDifference);
            if(automatic){localPreferences.Presented=localPreferences.Presented.Concat(new[]{c.Id+"/"+c.Version}).Distinct().ToArray();PersistPreferences();}
            Button("不再自动提示此卡",modalContent,()=>{localPreferences.DisabledCards=localPreferences.DisabledCards.Concat(new[]{id}).Distinct().ToArray();PersistPreferences();CloseModal();});
            var row=ModalButtons();Button("知识目录",row,ShowKnowledgeDirectory);Button("关闭",row,CloseModal);
        }
    }
}
