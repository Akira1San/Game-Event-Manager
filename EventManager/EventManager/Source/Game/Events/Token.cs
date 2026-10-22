namespace Game
{
    public enum TokenType
    {
        Word,
        Number,
        Str,
        Indent,
        Dedent,
        NewLine,
        EndOfFile,
    }

    public class Token
    {
        public TokenType Type;
        public string Text = "";
        public double Number;
        public int Line;
        public int IndentLevel;

        public override string ToString()
        {
            switch (Type)
            {
                case TokenType.Word: return $"Word:{Text}";
                case TokenType.Number: return $"Number:{Number}";
                case TokenType.Str: return $"Str:{Text}";
                case TokenType.Indent: return $"Indent:{IndentLevel}";
                case TokenType.Dedent: return "Dedent";
                case TokenType.NewLine: return "NewLine";
                case TokenType.EndOfFile: return "EndOfFile";
                default: return Type.ToString();
            }
        }
    }
}
