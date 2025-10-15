using System.Collections.Generic;

namespace Shared.Allowlists
{
    public interface ISqlAllowlist
    {
        IReadOnlyCollection<string> Tables { get; }
        bool IsTableAllowed(string table);
        bool IsColumnAllowed(string table, string column);
        bool IsOperatorAllowed(string op); // e.g. =, <>, >, >=, <, <=, ILIKE
        int DefaultLimit { get; }
        int MaxLimit { get; }
    }
}
