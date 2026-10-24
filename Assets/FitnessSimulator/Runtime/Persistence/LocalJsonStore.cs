using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace FitnessSimulator.Persistence
{
    public sealed class LocalJsonStore
    {
        private readonly string directory;

        public LocalJsonStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Storage directory is required.");
            this.directory = Path.GetFullPath(directory);
        }

        public void Save<T>(string slot, T data)
        {
            string path = Resolve(slot);
            Directory.CreateDirectory(directory);
            string temporary = path + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(data, true));
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        public bool TryLoad<T>(string slot, out T data) where T : class
        {
            string path = Resolve(slot);
            data = null;
            if (!File.Exists(path)) return false;
            try
            {
                data = JsonUtility.FromJson<T>(File.ReadAllText(path, Encoding.UTF8));
                return data != null;
            }
            catch (ArgumentException) { return false; }
            catch (IOException) { return false; }
        }

        private string Resolve(string slot)
        {
            if (string.IsNullOrWhiteSpace(slot) || slot.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                slot.Contains("/") || slot.Contains("\\") || slot == "." || slot == "..")
                throw new ArgumentException("Slot must be a simple file name.", nameof(slot));
            return Path.Combine(directory, slot + ".json");
        }
    }
}
