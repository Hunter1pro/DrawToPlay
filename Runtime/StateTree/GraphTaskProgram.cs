using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE PROGRAM MODEL'S OWN FACTS (M30.6) — how many pins each instruction has, in the assembly
    /// that defines the instructions.
    ///
    /// This was knowledge a BAKER held, which was fine while there was one baker. A second
    /// authoring surface makes it a contract: two bakers disagreeing about whether a Branch has
    /// one exec pin or two would produce programs that run differently from graphs that look the
    /// same, and the disagreement would be invisible until a wire went missing at runtime. The
    /// interpreter reads these slots; the interpreter's assembly is where their shape belongs.
    ///
    /// Frozen alongside <see cref="GraphTaskNodeKind"/> — a new kind appends, and its pins are
    /// declared here in the same commit.
    /// </summary>
    public static class GraphTaskProgram
    {
        /// <summary>How many EXEC out-pins this instruction has: where control goes next, per
        /// outcome. Zero means it ends a chain or is a value.</summary>
        public static int ExecPins(GraphTaskNodeKind kind)
        {
            switch (kind)
            {
                case GraphTaskNodeKind.Branch:
                case GraphTaskNodeKind.DoTask:
                    return 2;
                case GraphTaskNodeKind.SetBlackboardFloat:
                case GraphTaskNodeKind.SetBlackboardString:
                case GraphTaskNodeKind.SetOutputFloat:
                case GraphTaskNodeKind.SetOutputString:
                case GraphTaskNodeKind.SetOutputBool:
                case GraphTaskNodeKind.Wait:
                case GraphTaskNodeKind.FireCue:
                    return 1;
                default:
                    return 0;
            }
        }

        /// <summary>How many DATA in-pins it reads: which instruction produces each value it
        /// needs. Zero means it needs nothing wired.</summary>
        public static int DataPins(GraphTaskNodeKind kind)
        {
            switch (kind)
            {
                case GraphTaskNodeKind.CompareFloat:
                case GraphTaskNodeKind.BoolAnd:
                case GraphTaskNodeKind.BoolOr:
                    return 2;
                case GraphTaskNodeKind.Branch:
                case GraphTaskNodeKind.SetBlackboardFloat:
                case GraphTaskNodeKind.SetBlackboardString:
                case GraphTaskNodeKind.SetOutputFloat:
                case GraphTaskNodeKind.SetOutputString:
                case GraphTaskNodeKind.SetOutputBool:
                case GraphTaskNodeKind.Wait:
                case GraphTaskNodeKind.BoolNot:
                case GraphTaskNodeKind.GetTaskOutputFloat:
                case GraphTaskNodeKind.GetTaskOutputString:
                case GraphTaskNodeKind.GetTaskOutputBool:
                    return 1;
                default:
                    return 0;
            }
        }

        /// <summary>Does this instruction PRODUCE a value another one can read? The question a
        /// wire asks before it is allowed to exist.</summary>
        public static bool IsValue(GraphTaskNodeKind kind)
        {
            switch (kind)
            {
                case GraphTaskNodeKind.ConstFloat:
                case GraphTaskNodeKind.ConstString:
                case GraphTaskNodeKind.ConstBool:
                case GraphTaskNodeKind.GetBlackboardFloat:
                case GraphTaskNodeKind.GetBlackboardString:
                case GraphTaskNodeKind.HasBlackboardKey:
                case GraphTaskNodeKind.EvaluateCondition:
                case GraphTaskNodeKind.CompareFloat:
                case GraphTaskNodeKind.BoolAnd:
                case GraphTaskNodeKind.BoolOr:
                case GraphTaskNodeKind.BoolNot:
                case GraphTaskNodeKind.ExitStatus:
                case GraphTaskNodeKind.GetParamFloat:
                case GraphTaskNodeKind.GetParamString:
                case GraphTaskNodeKind.GetParamBool:
                case GraphTaskNodeKind.GetTaskOutputFloat:
                case GraphTaskNodeKind.GetTaskOutputString:
                case GraphTaskNodeKind.GetTaskOutputBool:
                case GraphTaskNodeKind.RegistryEntry:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>A blank instruction of this kind, with every pin unwired — the shape both
        /// bakers start from, so neither can forget a slot.</summary>
        public static GraphTaskNode NewInstruction(GraphTaskNodeKind kind)
        {
            return new GraphTaskNode
            {
                kind = kind,
                stringValue = string.Empty,
                stringValue2 = string.Empty,
                exec = Unwired(ExecPins(kind)),
                data = Unwired(DataPins(kind))
            };
        }

        /// <summary>-1 is "nothing here", everywhere in the program.</summary>
        public static int[] Unwired(int count)
        {
            var slots = new int[count];
            for (int i = 0; i < count; i++)
                slots[i] = -1;
            return slots;
        }
    }
}
