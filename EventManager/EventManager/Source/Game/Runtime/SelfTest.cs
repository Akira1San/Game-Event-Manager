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

        public static void Register(CaseRecord c) => Cases.Add(c);

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
