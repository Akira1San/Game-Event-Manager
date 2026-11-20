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
    }

    public static class SelfTest
    {
        public static readonly List<CaseRecord> Cases = new List<CaseRecord>();
        public static int TestPassed;
        public static int TestFailed;
        public static double TimeoutSeconds = 5.0;

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
                c.FailureReason = reason;
                TestFailed++;
            }
        }
    }
}
