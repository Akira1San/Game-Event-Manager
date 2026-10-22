using System.Collections.Generic;

namespace Game
{
    public class ProgramFile
    {
        public List<EventNode> Events = new List<EventNode>();
    }

    public class EventNode
    {
        public int Line;
        public List<Token> Trigger = new List<Token>();
        public List<BodyLineNode> Body = new List<BodyLineNode>();
    }

    public class BodyLineNode
    {
        public int Line;
        public List<Token> Words = new List<Token>();
        public bool IsCondition;
        public StatementNode Statement;
    }

    public abstract class StatementNode
    {
        public int Line;
    }

    public class SetNode : StatementNode
    {
        public VariableValue Target = new VariableValue();
        public ValueNode Value = new NumberValue();
        public char Op = '\0';
        public ValueNode Right;
    }

    public class AddNode : StatementNode
    {
        public ValueNode Amount = new NumberValue();
        public VariableValue Target = new VariableValue();
    }

    public class SetTextNode : StatementNode
    {
        public string Target = "";
        public ValueNode Value = new NumberValue();
    }

    public class RawActionNode : StatementNode
    {
        public List<Token> Words = new List<Token>();
    }
}
