using System;
using System.Collections.Generic;
using NoteNest.Domain.Common;

namespace NoteNest.Domain.Todos
{
    /// <summary>
    /// Value object representing a Todo identifier.
    /// Moved to Domain layer for proper Clean Architecture (Infrastructure can reference Domain).
    /// </summary>
    public class TodoId : ValueObject
    {
        public Guid Value { get; }

        private TodoId(Guid value)
        {
            Value = value;
        }

        public static TodoId Create() => new(Guid.NewGuid());
        public static TodoId From(Guid value) => new(value);

        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Value;
        }

        public override string ToString() => Value.ToString();
    }
}

