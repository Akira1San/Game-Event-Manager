using System;
using System.Collections.Generic;
using System.IO;

namespace Game
{
    public class EventFileResult
    {
        public string Name;
        public string Path;
        public ProgramFile File;
        public string Error = "";
    }

    public static class EventFiles
    {
        public static string LocateGameFiles(string startDir)
        {
            string current = startDir;
            while (!string.IsNullOrEmpty(current))
            {
                string candidate = Path.Combine(current, "Source", "Game", "GameFiles");
                if (Directory.Exists(candidate) && Directory.GetFiles(candidate, "*.events").Length > 0)
                    return candidate;
                string loose = Path.Combine(current, "GameFiles");
                if (Directory.Exists(loose) && Directory.GetFiles(loose, "*.events").Length > 0)
                    return loose;
                string parent = Directory.GetParent(current)?.FullName;
                if (parent == current)
                    break;
                current = parent;
            }
            return null;
        }

        /// <summary>Closest existing file name to what the user wrote, or null when nothing is near
        /// (same near-miss rule the parser uses: distance at most 2 — "Playr" suggests "Player").</summary>
        public static string SuggestFileName(string wanted, System.Collections.Generic.IEnumerable<string> knownNames)
        {
            if (string.IsNullOrEmpty(wanted) || knownNames == null)
                return null;
            string best = null;
            int bestDistance = int.MaxValue;
            foreach (string name in knownNames)
            {
                int distance = WordDistance.Between(wanted, name);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = name;
                }
            }
            return bestDistance <= 2 ? best : null;
        }

        public static List<EventFileResult> LoadAll(string folder)
        {
            var results = new List<EventFileResult>();
            if (folder == null || !Directory.Exists(folder))
                return results;
            string[] files = Directory.GetFiles(folder, "*.events");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                string fullPath = files[i];
                string name = Path.GetFileNameWithoutExtension(fullPath);
                var result = new EventFileResult { Name = name, Path = fullPath };
                try
                {
                    string source = File.ReadAllText(fullPath);
                    var parser = new EventParser();
                    result.File = parser.Parse(source);
                }
                catch (ParseException error)
                {
                    result.File = new ProgramFile();
                    result.Error = error.Message;
                }
                catch (LexException error)
                {
                    result.File = new ProgramFile();
                    result.Error = error.Message;
                }
                results.Add(result);
            }
            return results;
        }

        /// <summary>
        /// Parses one named file without loading the rest, so a data file that other sheets are
        /// parsed against (the resource list) can be read first. Returns null when the file does
        /// not exist; a broken file comes back with <see cref="EventFileResult.Error"/> set, like
        /// <see cref="LoadAll"/>.
        /// </summary>
        public static EventFileResult LoadOne(string folder, string name)
        {
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(name) || !Directory.Exists(folder))
                return null;
            string path = Path.Combine(folder, name + ".events");
            if (!File.Exists(path))
                return null;
            var result = new EventFileResult { Name = name, Path = path };
            try
            {
                result.File = new EventParser().Parse(File.ReadAllText(path));
            }
            catch (ParseException error)
            {
                result.File = new ProgramFile();
                result.Error = error.Message;
            }
            catch (LexException error)
            {
                result.File = new ProgramFile();
                result.Error = error.Message;
            }
            return result;
        }
    }
}
