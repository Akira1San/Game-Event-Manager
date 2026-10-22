using System;

namespace Game
{
    public class ParseException : Exception
    {
        public int Line;

        public ParseException(string message, int line)
            : base($"Line {line}: {message}")
        {
            Line = line;
        }
    }
}
