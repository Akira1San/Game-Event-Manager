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
    }
}
