using System;
using System.Collections.Generic;

namespace Game
{
    public enum CaseState
    {
        Idle,
        Running,
        Passed,
        Failed,
        Skipped,
    }

    /// <summary>One self-test case: a `when` block that contains an `expect`/`pass` (Task 51).
    /// The label defaults to the block's order when `test "<label>"` is omitted.</summary>
    public class CaseRecord
    {
        public string Label = "";
        public int Line;
        public CaseState State = CaseState.Idle;
        public string FailureReason = "";
        /// <summary>True once the case's event body has started running.</summary>
        public bool Entered;
        /// <summary>Text of the gate that kept the case from running (shown for SKIP).</summary>
        public string GateText = "";
        /// <summary>Index of the last statement executed inside the case body.</summary>
        public int StatementIndex;
        /// <summary>Text of the last statement executed, for timeout reports.</summary>
        public string LastStatement = "";
        /// <summary>Warnings raised while this case was running.</summary>
        public List<string> Warnings = new List<string>();
        /// <summary>Time the case first entered Running.</summary>
        public double StartedAt;
    }

    public static class SelfTest
    {
        public static readonly List<CaseRecord> Cases = new List<CaseRecord>();
        public static int TestPassed;
        public static int TestFailed;
        public static double TimeoutSeconds = 5.0;
        /// <summary>How many times `when self test ends` has been raised (once total).</summary>
        public static int SelfTestEndsRaises;

        /// <summary>Labels this run cannot evaluate (Task 51 Part D). The headless tier lists the
        /// `(editor only)` cases here, and the runner reports them SKIP without running their
        /// bodies, so an excluded case is visible in the report instead of silently passing. A
        /// Play leaves this empty and everything runs.</summary>
        public static readonly HashSet<string> ExcludedLabels = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>True when the case's label was handed to <see cref="ExcludedLabels"/>.</summary>
        public static bool IsExcluded(string label)
        {
            return !string.IsNullOrEmpty(label) && ExcludedLabels.Contains(label);
        }

        public static void Register(CaseRecord c) => Cases.Add(c);

        /// <summary>How many cases settled as skipped in this round.</summary>
        public static int SkippedCount()
        {
            int skipped = 0;
            foreach (var c in Cases)
                if (c.State == CaseState.Skipped)
                    skipped++;
            return skipped;
        }

        /// <summary>Starts a fresh self-test round. The state above is static, so the editor's
        /// second Play would otherwise report the first run's cases and never raise
        /// `when self test ends` again (Task 51 Part C). The manager calls this once when a
        /// scene boots, before any sheet registers, so an ordinary play is unaffected.</summary>
        public static void Reset()
        {
            Cases.Clear();
            TestPassed = 0;
            TestFailed = 0;
            TimeoutSeconds = 5.0;
            SelfTestEndsRaises = 0;
            ExcludedLabels.Clear();
        }

        public static void Pass(CaseRecord c)
        {
            if (c.State != CaseState.Passed && c.State != CaseState.Failed)
            {
                c.State = CaseState.Passed;
                TestPassed++;
            }
        }

        public static void Fail(CaseRecord c, string reason)
        {
            if (c.State != CaseState.Passed && c.State != CaseState.Failed)
            {
                c.State = CaseState.Failed;
                var report = new System.Text.StringBuilder(reason);
                if (c.Warnings.Count > 0)
                    report.Append(" | warnings: ").Append(string.Join("; ", c.Warnings));
                c.FailureReason = report.ToString();
                TestFailed++;
            }
        }
    }
}
