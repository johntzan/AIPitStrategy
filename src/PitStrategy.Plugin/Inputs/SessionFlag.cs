using System;

namespace PitStrategy.Core.Inputs
{
    [Flags]
    public enum SessionFlag
    {
        None = 0,
        Green = 1 << 0,
        Yellow = 1 << 1,
        FullCourseYellow = 1 << 2,
        Red = 1 << 3,
        White = 1 << 4,
        Checkered = 1 << 5,
        Blue = 1 << 6,
    }
}
