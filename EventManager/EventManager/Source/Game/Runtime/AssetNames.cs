using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// Turns paths and file names into plain asset keys, so events can name a clip
    /// (<c>play sound "hurt.wav"</c>) without knowing its folder or extension.
    /// Pure string work only — no Flax types live here.
    /// </summary>
    public static class AssetNames
    {
        private static readonly string[] AudioExtensions =
        {
            ".wav", ".mp3", ".ogg", ".flac", ".aiff", ".aif", ".wma", ".m4a",
        };

        /// <summary>
        /// Normalizes "sounds/hurt.wav.flax", "Content\hurt.wav" or "Hurt.WAV" to "hurt".
        /// </summary>
        public static string Key(string pathOrName)
        {
            if (string.IsNullOrWhiteSpace(pathOrName))
                return "";

            string name = pathOrName.Trim().Replace('\\', '/');
            int slash = name.LastIndexOf('/');
            if (slash >= 0)
                name = name.Substring(slash + 1);

            if (name.EndsWith(".flax", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - ".flax".Length);

            foreach (string extension in AudioExtensions)
            {
                if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - extension.Length);
                    break;
                }
            }

            return name.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Yields the key plus every space-boundary suffix, so a verbose imported name still
        /// answers to the action name at its end: "bge dragon 2.07 animation walk" → "walk",
        /// "animation walk", …; "bge dragon 2.07 animation idle pose" → "idle pose", ….
        /// </summary>
        public static IEnumerable<string> Suffixes(string key)
        {
            if (string.IsNullOrEmpty(key))
                yield break;

            yield return key;
            int space = key.IndexOf(' ');
            while (space >= 0 && space + 1 < key.Length)
            {
                yield return key.Substring(space + 1);
                space = key.IndexOf(' ', space + 1);
            }
        }
    }
}
