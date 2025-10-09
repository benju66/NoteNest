using System;
using System.Data;
using Dapper;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence
{
    /// <summary>
    /// Dapper type handler for converting SQLite TEXT to Guid
    /// Handles the persistence bug where TEXT columns can't be auto-mapped to Guid
    /// </summary>
    public class GuidTypeHandler : SqlMapper.TypeHandler<Guid>
    {
        public override Guid Parse(object value)
        {
            if (value == null || value is DBNull)
                return Guid.Empty;

            if (value is Guid guid)
                return guid;

            if (value is string str && !string.IsNullOrWhiteSpace(str))
            {
                if (Guid.TryParse(str, out var parsed))
                    return parsed;
            }

            return Guid.Empty;
        }

        public override void SetValue(IDbDataParameter parameter, Guid value)
        {
            parameter.Value = value.ToString();
        }
    }

    /// <summary>
    /// Dapper type handler for nullable Guid
    /// </summary>
    public class NullableGuidTypeHandler : SqlMapper.TypeHandler<Guid?>
    {
        public override Guid? Parse(object value)
        {
            if (value == null || value is DBNull)
                return null;

            if (value is Guid guid)
                return guid;

            if (value is string str && !string.IsNullOrWhiteSpace(str))
            {
                if (Guid.TryParse(str, out var parsed))
                    return parsed;
            }

            return null;
        }

        public override void SetValue(IDbDataParameter parameter, Guid? value)
        {
            parameter.Value = value?.ToString() ?? (object)DBNull.Value;
        }
    }
}

