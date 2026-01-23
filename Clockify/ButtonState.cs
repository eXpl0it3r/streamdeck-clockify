using System;

namespace Clockify;

public class ButtonState
{
    public uint Ticks { get; set; } = 5;
    public DateTime? LastStart { get; set; }
}