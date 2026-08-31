using System.Collections.Generic;

namespace Clockify;

public class AvailableOptions
{
    public List<string> WorkspaceNames { get; set; } = [];
    public List<string> ProjectNames { get; set; } = [];
    public List<string> TaskNames { get; set; } = [];
}
