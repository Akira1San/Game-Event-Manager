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
        /// <summary>When set, this event only fires while the sheet's CurrentState matches.</summary>
        public string RequiredState = "";
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

    public class BagRemoveNode : StatementNode
    {
        public string Thing = "";
    }

    public class EquipNode : StatementNode
    {
        public string Thing = "";
    }

    public class DropNode : StatementNode
    {
        public string Thing = "";
    }

    public class SetTextNode : StatementNode
    {
        public string Target = "";
        public ValueNode Value = new NumberValue();
    }

    /// <summary><c>set button &lt;thing&gt; enabled/disabled</c>.</summary>
    public class SetButtonEnabledNode : StatementNode
    {
        public string Thing = "";
        public bool Enabled = true;
    }

    /// <summary><c>set image of &lt;thing&gt; to &lt;sprite&gt;</c> — swaps the Image control's brush.</summary>
    public class SetImageBrushNode : StatementNode
    {
        public string Thing = "";
        public string Sprite = "";
    }

    /// <summary><c>set color of &lt;thing&gt; to &lt;color&gt;</c> — tints a control (e.g. an Image).</summary>
    public class SetControlColorNode : StatementNode
    {
        public string Thing = "";
        public string Color = "";
    }

    /// <summary><c>set progress of &lt;thing&gt; to &lt;value&gt;</c> — fills a ProgressBar (0-1 or 0-100).</summary>
    public class SetProgressNode : StatementNode
    {
        public string Thing = "";
        public ValueNode Value = new NumberValue();
    }

    /// <summary><c>set slider &lt;thing&gt; to &lt;value&gt;</c> — moves a Slider (0-1 or 0-100).</summary>
    public class SetSliderNode : StatementNode
    {
        public string Thing = "";
        public ValueNode Value = new NumberValue();
    }

    public class RawActionNode : StatementNode
    {
        public List<Token> Words = new List<Token>();
    }

    /// <summary><c>play step sound "&lt;material&gt;"</c>: looks up the sheet's material→clip map,
    /// plays the mapped clip once per cooldown interval.</summary>
    public class PlayStepSoundNode : StatementNode
    {
        public string Material = "";
    }

    public class SetStateNode : StatementNode
    {
        public string State = "";
    }

    /// <summary><c>set my control group to &lt;n&gt;</c>: assigns this unit (or the current
    /// selection, from a global sheet) to control group 1–0.</summary>
    public class SetControlGroupNode : StatementNode
    {
        public int Group = -1;
    }
}
