using System;
using System.Collections.Generic;
using System.Linq;

namespace Clockify;

public static class LookupMapping
{
    public static List<string> ToSortedNames<T>(IEnumerable<T> items, Func<T, string> nameSelector)
    {
        if (items is null)
        {
            return [];
        }

        return items
            .Select(nameSelector)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
