using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// Which UI controls carry a tooltip's text. Flax shows a tooltip from the control the pointer
    /// is over (<c>Control.OnMouseMove</c> asks that control's own <c>ShowTooltip</c>, which is
    /// false for an empty <c>TooltipText</c>) and only the container it sits in is offered the
    /// event as well — a deeper label, image or button is not. So writing the text on the actor's
    /// root control alone would show nothing while the pointer is over the slot's own art. The text
    /// goes on the whole subtree instead, and the control the cursor actually hits answers.
    /// Engine-free so the walk itself can be checked without Flax (Task 34 Part D follow-up).
    /// </summary>
    public static class UiTooltipTargets
    {
        /// <summary>
        /// <paramref name="root"/> and every descendant, parents before children, so a caller can
        /// stamp the same text on each. A null root has no targets.
        /// </summary>
        public static List<T> Collect<T>(T root, Func<T, IReadOnlyList<T>> children) where T : class
        {
            var found = new List<T>();
            if (root == null)
                return found;
            if (children == null)
            {
                found.Add(root);
                return found;
            }
            var stack = new Stack<T>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                T node = stack.Pop();
                if (node == null)
                    continue;
                found.Add(node);
                IReadOnlyList<T> childNodes = children(node);
                if (childNodes == null)
                    continue;
                // Pushed in reverse so the stack pops them in authored order.
                for (int i = childNodes.Count - 1; i >= 0; i--)
                    stack.Push(childNodes[i]);
            }
            return found;
        }
    }
}
