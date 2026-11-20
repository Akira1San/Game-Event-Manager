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
        /// <summary><c>otherwise</c>/<c>else</c>: runs when the condition line above it was false.</summary>
        public bool IsOtherwise;
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

    /// <summary><c>deduct &lt;n&gt; from &lt;resource&gt;</c>: subtracts, clamping at 0.</summary>
    public class DeductNode : StatementNode
    {
        public ValueNode Amount = new NumberValue();
        public VariableValue Target = new VariableValue();
    }

    /// <summary>What a cursor word does (Task 48 Part E).</summary>
    public enum CursorOp
    {
        /// <summary><c>set cursor "Attack"</c> / <c>set cursor to default</c>.</summary>
        Set,
        /// <summary><c>push cursor "Attack"</c>.</summary>
        Push,
        /// <summary><c>pop cursor</c>.</summary>
        Pop,
        /// <summary><c>hide cursor</c>.</summary>
        Hide,
        /// <summary><c>show cursor</c>.</summary>
        Show,
    }

    /// <summary>One cursor word (Task 48 Part E): set, push, pop, hide or show.</summary>
    public class CursorNode : StatementNode
    {
        public CursorOp Op;
        public string Name = "";
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
        /// <summary>Whether writing the text also un-hides its control. A bind sets this false: it
        /// writes a value, so a sheet that <c>hide</c>s a label keeps it hidden while the value
        /// behind it keeps changing. An explicit <c>set text of</c> keeps the reveal, so `hide
        /// GameText` cannot brick `show text`.</summary>
        public bool Reveal = true;
    }

    /// <summary><c>type text "&lt;text&gt;" into &lt;thing&gt; speed &lt;n&gt;</c> — reveals the
    /// words a character at a time (Task 35 Part A). The timing lives in the runtime's
    /// <see cref="Typewriter"/>; only the finished reveal raises `when text finishes`.</summary>
    public class TypeTextNode : StatementNode
    {
        public string Target = "";
        public ValueNode Value = new TextValue();
        /// <summary>Characters per second; 0 or less shows the whole line at once.</summary>
        public ValueNode Speed = new NumberValue();
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

    /// <summary><c>set portrait of &lt;thing&gt; to &lt;sprite&gt;</c> — swaps the portrait Image's
    /// brush through the same sprite resolver as <c>set image</c> / <c>play sound</c>
    /// (Task 35 Part B). The speaker is its own word, so a name never lands in the art.</summary>
    public class SetPortraitNode : StatementNode
    {
        public string Thing = "";
        public string Sprite = "";
    }

    /// <summary><c>set speaker to &lt;name&gt;</c> — records the speaker in the <c>Speaker</c>
    /// global and writes the scene's own <c>SpeakerLabel</c> (Task 35 Part B).</summary>
    public class SetSpeakerNode : StatementNode
    {
        public ValueNode Value = new TextValue();
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
        /// <summary>See <see cref="SetTextNode.Reveal"/>: a bound bar fills but stays hidden.</summary>
        public bool Reveal = true;
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

    /// <summary><c>set build progress to &lt;n&gt;</c>: sets the acting building's
    /// construction progress (0–100) on <see cref="ConstructionManager"/>. The
    /// optional <c>of &lt;building&gt;</c> form targets a named actor.</summary>
    public class SetBuildProgressNode : StatementNode
    {
        public string Thing = "";
        public ValueNode Value = new NumberValue();
    }

    /// <summary><c>add &lt;n&gt; to build progress</c>: advances the acting building's
    /// construction progress, clamping at 0. The optional <c>of &lt;building&gt;</c>
    /// form targets a named actor.</summary>
    public class AddBuildProgressNode : StatementNode
    {
        public string Thing = "";
        public ValueNode Amount = new NumberValue();
    }

    /// <summary><c>set build time to &lt;n&gt; seconds</c>: changes the acting building's
    /// current training time and the default for later orders. The optional
    /// <c>of &lt;building&gt;</c> form targets a named actor.</summary>
    public class SetBuildTimeNode : StatementNode
    {
        public string Thing = "";
        public ValueNode Value = new NumberValue();
    }

    /// <summary><c>set weapon to &lt;name&gt;</c> / <c>weapon &lt;name&gt;</c> — records the
    /// actor's current weapon in <c>my Weapon</c> (Task 16 Part C).</summary>
    public class SetWeaponNode : StatementNode
    {
        public ValueNode Value = new TextValue();
    }

    /// <summary><c>set ammo &lt;weapon&gt; to &lt;n&gt;</c> — writes that weapon's ammo count on
    /// the acting actor, clamped at 0. <see cref="WeaponVariable"/> reads the name from a
    /// variable instead (<c>set ammo my Weapon to 30</c>).</summary>
    public class SetAmmoNode : StatementNode
    {
        public string Weapon = "";
        public string WeaponVariable = "";
        public ValueNode Value = new NumberValue();
    }

    /// <summary><c>add &lt;n&gt; to ammo &lt;weapon&gt;</c> — the shot cost, e.g.
    /// <c>add -1 to ammo Pistol</c>; clamps at 0 so a trigger-happy sheet can't go negative.</summary>
    public class AddAmmoNode : StatementNode
    {
        public string Weapon = "";
        public string WeaponVariable = "";
        public ValueNode Amount = new NumberValue();
    }

    /// <summary><c>reload</c> — starts the current weapon's reload timer; <c>reloading</c> is
    /// true until it finishes and <c>when reload completes</c> fires once.</summary>
    public class ReloadNode : StatementNode
    {
        public string Weapon = "";
        public string WeaponVariable = "";
    }

    /// <summary><c>add 1 to kills &lt;enemyType&gt;</c> — counts a kill for that type and for
    /// <c>Kills Total</c> (Task 16 Part G). <see cref="TypeVariable"/> reads the type from the
    /// actor instead (<c>add 1 to kills my Type</c>), so one enemy sheet serves every type.</summary>
    public class AddKillNode : StatementNode
    {
        public ValueNode Amount = new NumberValue();
        public string EnemyType = "";
        public string TypeVariable = "";
    }
}
