using System;
using System.Linq;
using System.Text.RegularExpressions;
using FitnessSimulator.Game.Configuration;
using UnityEngine;

namespace FitnessSimulator.Game.Presentation
{
    [Serializable] public sealed class AudioDocument
    {
        public string Version;
        public bool DefaultMusicEnabled, DefaultSfxEnabled;
        public float DefaultMusicVolume, DefaultSfxVolume;
        public AudioEntry[] Entries;
        public AudioCue[] Cues;
    }
    [Serializable] public sealed class AudioEntry
    {
        public string Id, AssetRef, Kind, Source, SourceRecord, Sha256;
        public bool Loop;
        public float Gain;
    }
    [Serializable] public sealed class AudioCue { public string Cue, AudioId; }
    [Serializable] public sealed class AudioPreferences
    {
        public bool MusicEnabled, SfxEnabled;
        public float MusicVolume, SfxVolume;
    }
    public static class AudioContent
    {
        public static AudioDocument Read() => ContentRuntime.Current.Get<AudioDocument>("audio");
        public static AudioPreferences Defaults()
        {
            var d = Read();
            return new AudioPreferences { MusicEnabled = d.DefaultMusicEnabled, SfxEnabled = d.DefaultSfxEnabled, MusicVolume = d.DefaultMusicVolume, SfxVolume = d.DefaultSfxVolume };
        }
        public static void ValidateContent()
        {
            var d = Read();
            if (string.IsNullOrWhiteSpace(d.Version) || d.Entries == null || d.Cues == null || !Volume(d.DefaultMusicVolume) || !Volume(d.DefaultSfxVolume)) throw Error("Defaults", "Invalid audio defaults");
            if (d.Entries.Any(x => x == null || string.IsNullOrWhiteSpace(x.Id)) || d.Entries.Select(x => x.Id).Distinct().Count() != d.Entries.Length) throw Error("Entries", "Duplicate/missing audio ID");
            foreach (var e in d.Entries)
            {
                if ((e.Kind != "music" && e.Kind != "sfx") || e.Loop != (e.Kind == "music") || !Volume(e.Gain) || string.IsNullOrWhiteSpace(e.AssetRef) || e.AssetRef.Contains("..") || string.IsNullOrWhiteSpace(e.Source) || string.IsNullOrWhiteSpace(e.SourceRecord) || !Regex.IsMatch(e.Sha256 ?? "", "^[A-Fa-f0-9]{64}$")) throw Error("Entries." + e.Id, "Invalid audio metadata");
                if (!Resources.Load<AudioClip>(e.AssetRef)) throw Error("Entries." + e.Id + ".AssetRef", "Missing AudioClip: " + e.AssetRef);
            }
            if (d.Cues.Any(x => x == null || string.IsNullOrWhiteSpace(x.Cue) || !d.Entries.Any(e => e.Id == x.AudioId)) || d.Cues.Select(x => x.Cue).Distinct().Count() != d.Cues.Length) throw Error("Cues", "Duplicate/missing cue or unknown audio reference");
        }
        internal static bool Volume(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && v >= 0 && v <= 1;
        internal static ContentException Error(string path, string message) => new ContentException("audio", path, message);
    }
    // The application owns persistence. This component only renders supplied preferences/cues.
    public sealed class AudioPresentation : MonoBehaviour
    {
        private AudioSource music, sfx;
        private AudioDocument document;
        private AudioPreferences preferences;
        private string contentHash, musicCue;
        private bool suspended;

        public void Configure(AudioPreferences value)
        {
            if (value == null || !AudioContent.Volume(value.MusicVolume) || !AudioContent.Volume(value.SfxVolume)) throw new ArgumentException("Invalid audio preferences");
            if (!music) { music = gameObject.AddComponent<AudioSource>(); music.playOnAwake = false; music.spatialBlend = 0; music.loop = true; }
            if (!sfx) { sfx = gameObject.AddComponent<AudioSource>(); sfx.playOnAwake = false; sfx.spatialBlend = 0; }
            preferences = new AudioPreferences { MusicEnabled = value.MusicEnabled, SfxEnabled = value.SfxEnabled, MusicVolume = value.MusicVolume, SfxVolume = value.SfxVolume };
            string hash = ContentRuntime.Current.Hash;
            if (document == null || contentHash != hash) { music.Stop(); sfx.Stop(); document = AudioContent.Read(); contentHash = hash; music.clip = null; }
            if (!preferences.SfxEnabled) sfx.Stop();
            RefreshMusic();
        }
        public void SetMusic(string cue)
        {
            EnsureConfigured();
            if (musicCue != cue) { music.Stop(); music.clip = null; musicCue = cue; }
            RefreshMusic();
        }
        public void PlayCue(string cue)
        {
            EnsureConfigured();
            var entry = Entry(cue, "sfx");
            if (suspended || !preferences.SfxEnabled) return;
            sfx.PlayOneShot(Clip(entry), preferences.SfxVolume * entry.Gain);
        }
        public void SetSuspended(bool value)
        {
            suspended = value;
            if (suspended) { if (music) music.Pause(); if (sfx) sfx.Stop(); }
            else if (preferences != null) RefreshMusic();
        }
        private void RefreshMusic()
        {
            if (preferences == null || string.IsNullOrEmpty(musicCue)) return;
            var entry = Entry(musicCue, "music");
            music.volume = preferences.MusicVolume * entry.Gain;
            if (!preferences.MusicEnabled || suspended) { music.Pause(); return; }
            if (!music.clip) { music.clip = Clip(entry); music.Play(); }
            else if (!music.isPlaying) { music.UnPause(); if (!music.isPlaying) music.Play(); }
        }
        private AudioEntry Entry(string cue, string kind)
        {
            var mapping = document.Cues.SingleOrDefault(x => x.Cue == cue) ?? throw AudioContent.Error("Cues." + cue, "Unknown cue");
            var entry = document.Entries.SingleOrDefault(x => x.Id == mapping.AudioId && x.Kind == kind);
            return entry ?? throw AudioContent.Error("Cues." + cue, "Wrong audio kind");
        }
        private static AudioClip Clip(AudioEntry entry) => Resources.Load<AudioClip>(entry.AssetRef) ?? throw AudioContent.Error("Entries." + entry.Id, "Missing AudioClip");
        private void EnsureConfigured() { if (preferences == null) Configure(AudioContent.Defaults()); }
        private void OnDisable() { if (music) music.Stop(); if (sfx) sfx.Stop(); }
    }
}
