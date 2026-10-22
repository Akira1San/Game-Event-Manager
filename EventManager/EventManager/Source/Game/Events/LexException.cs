using System;

namespace Game
{
    public class LexException : Exception
    {
        public int Line;

        public LexException(string message, int line)
            : base($"Line {line}: {message}")
        {
            Line = line;
        }
    }
}
