using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// <c>include "&lt;file&gt;"</c> (controller reuse): splices another GameFiles <c>.events</c> file's
    /// events into the including program, at the position of the include line. Nothing is copied at
    /// runtime — the blocks simply run on the including sheet, so `my` still means that actor and a
    /// library can hold the shared input wiring (see <c>GameFiles/FPS_Controls.events</c>) that any
    /// player sheet pulls in with one line.
    ///
    /// Resolution runs after every file has parsed (<see cref="EventFiles.LoadAll"/>), which is what
    /// lets a library name be resolved case-insensitively without the parser knowing the folder.
    /// Each name is included once per sheet, so writing the same include twice cannot double-fire a
    /// block, and a cycle is reported instead of recursing forever.
    ///
    /// Flax-free and file-system-free, so the rules are unit tested.
    /// </summary>
    public static class ProgramIncludes
    {
        /// <summary>
        /// How many files one sheet may pull in through a chain of includes: a sheet's own include is
        /// the first file, a file that includes another is the second, and the ninth is refused. A
        /// chain that long is almost certainly a mistake, and refusing it keeps a bad edit from
        /// turning into a very long recursion when the names never repeat.
        /// </summary>
        public const int MaxDepth = 8;

        /// <summary>
        /// Replaces every placeholder <see cref="EventNode.IncludeName"/> with that file's events.
        /// <paramref name="resolve"/> returns null for a name that has no file; <paramref name="suggest"/>
        /// (optional) offers the closest real name in the error. Returns false and sets
        /// <paramref name="error"/> when a file is missing, a loop is found or the chain is too deep.
        /// </summary>
        public static bool Expand(ProgramFile program, Func<string, ProgramFile> resolve,
            out string error, Func<string, string> suggest = null)
        {
            error = "";
            var events = new List<EventNode>();
            string problem = ExpandInto(program, resolve, suggest, events,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase), new List<string>(), 0);
            program.Events = events;
            if (problem != null)
            {
                error = problem;
                return false;
            }
            return true;
        }

        private static string ExpandInto(ProgramFile program, Func<string, ProgramFile> resolve,
            Func<string, string> suggest, List<EventNode> into, HashSet<string> seen,
            List<string> chain, int depth)
        {
            if (program == null)
                return "";
            foreach (EventNode node in program.Events)
            {
                if (string.IsNullOrEmpty(node.IncludeName))
                {
                    into.Add(node);
                    continue;
                }
                string name = node.IncludeName;
                if (depth >= MaxDepth)
                    return $"Line {node.Line}: `include` nests more than {MaxDepth} files deep — a chain that long is almost certainly a mistake, so put the shared blocks in the file that both sides include rather than passing them along";
                // The cycle check comes first: a file that includes one already on the chain is a
                // loop even when its name was also included earlier somewhere else.
                foreach (string open in chain)
                {
                    if (string.Equals(open, name, StringComparison.OrdinalIgnoreCase))
                        return $"Line {node.Line}: `include \"{name}\"` is already being included, so these two files pull each other in forever — break the loop by moving the shared blocks into a third file";
                }
                if (seen.Contains(name))
                    continue;
                ProgramFile included = resolve == null ? null : resolve(name);
                if (included == null)
                {
                    string near = suggest == null ? null : suggest(name);
                    string hint = string.IsNullOrEmpty(near) ? "" : $" (did you mean \"{near}\"?)";
                    return $"Line {node.Line}: I can't find an event file called \"{name}\" to include{hint} — `include` names a .events file in GameFiles, without the extension";
                }
                seen.Add(name);
                chain.Add(name);
                string problem = ExpandInto(included, resolve, suggest, into, seen, chain, depth + 1);
                chain.RemoveAt(chain.Count - 1);
                if (problem != null)
                    return problem;
            }
            return null;
        }
    }
}
