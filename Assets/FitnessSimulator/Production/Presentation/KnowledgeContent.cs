using System;
using System.IO;
using System.Linq;
using FitnessSimulator.Game.Configuration;
using UnityEngine;
namespace FitnessSimulator.Game.Presentation
{
    [Serializable] public sealed class KnowledgeDocument { public string Version; public KnowledgeCard[] Cards; }
    [Serializable] public sealed class KnowledgeCard
    {
        public string Id, Version, Category, Title, Body, SourceOrganization, SourceDocument, SourceUrl, SourceDate, VerifiedAt, ScopeNote, GameDifference, Trigger;
        public int DisplayOrder;
    }
    public static class KnowledgeContent
    {
        public static void ValidateContent()
        {
            var d=ContentRuntime.Current.Get<KnowledgeDocument>("knowledge");
            if(string.IsNullOrWhiteSpace(d.Version)||d.Cards==null||d.Cards.Any(c=>c==null)||d.Cards.Select(c=>c.Id).Distinct().Count()!=d.Cards.Length) throw new ContentException("knowledge","Cards","Invalid card catalog");
            foreach(var c in d.Cards) {
                if(new[]{c.Id,c.Version,c.Title,c.Body,c.SourceOrganization,c.SourceDocument,c.SourceDate,c.VerifiedAt,c.ScopeNote,c.GameDifference}.Any(string.IsNullOrWhiteSpace)||c.Category!="reality"||!Uri.TryCreate(c.SourceUrl,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!new[]{"ordinary-complete","day-end","body-exit"}.Contains(c.Trigger)) throw new ContentException("knowledge",c.Id,"Invalid reality card provenance/trigger");
            }
        }
    }
    [Serializable] public sealed class PresentationPreferences
    {
        public AudioPreferences Audio;
        public bool CardsEnabled=true;
        public string[] Presented=Array.Empty<string>(), DisabledCards=Array.Empty<string>();
    }
    public static class PresentationPreferenceStore
    {
        public static PresentationPreferences Load(string path,AudioPreferences defaults)
        {
            if(!File.Exists(path))return new PresentationPreferences{Audio=defaults};
            var p=JsonUtility.FromJson<PresentationPreferences>(File.ReadAllText(path));
            if(p==null||p.Audio==null||!AudioContent.Volume(p.Audio.MusicVolume)||!AudioContent.Volume(p.Audio.SfxVolume)||p.Presented==null||p.DisabledCards==null)throw new InvalidDataException("Invalid presentation preferences: "+path);
            return p;
        }
        public static void Save(string path,PresentationPreferences value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp=path+".pending";File.WriteAllText(temp,JsonUtility.ToJson(value));
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        }
    }
}
