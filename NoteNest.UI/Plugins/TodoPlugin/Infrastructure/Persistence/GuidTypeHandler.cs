using System;
using System.Data;
using Dapper;
using Serilog;

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
            // DIAGNOSTIC: Log every invocation to both Debug and Serilog
            var valueType = value?.GetType().Name ?? "null";
            var valueStr = value?.ToString() ?? "null";
            
            if (value == null || value is DBNull)
            {
                Log.Debug("[GuidTypeHandler] Parse: value=null/DBNull → returning null");
                return null;
            }

            if (value is Guid guid)
            {
                Log.Debug("[GuidTypeHandler] Parse: already Guid={Guid} → returning as-is", guid);
                return guid;
            }

            if (value is string str && !string.IsNullOrWhiteSpace(str))
            {
                if (Guid.TryParse(str, out var parsed))
                {
                    Log.Debug("[GuidTypeHandler] Parse: '{String}' → {Parsed} ✅", str.Substring(0, Math.Min(8, str.Length)), parsed);
                    return parsed;
                }
                else
                {
                    Log.Warning("[GuidTypeHandler] Parse: '{String}' → TryParse FAILED → NULL", str);
                }
            }
            else
            {
                Log.Warning("[GuidTypeHandler] Parse: type={ValueType}, whitespace={IsWhitespace} → NULL", 
                    valueType, string.IsNullOrWhiteSpace(valueStr));
            }

            return null;
        }

        public override void SetValue(IDbDataParameter parameter, Guid? value)
        {
            parameter.Value = value?.ToString() ?? (object)DBNull.Value;
        }
    }
}

