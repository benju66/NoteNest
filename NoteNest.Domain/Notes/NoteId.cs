using System;
using System.Collections.Generic;
using NoteNest.Domain.Common;

namespace NoteNest.Domain.Notes
{
    public class NoteId : ValueObject
    {
        public string Value { get; }

        private NoteId(string value)
        {
            Value = value;
        }

        public static NoteId Create() => new(Guid.NewGuid().ToString());
        public static NoteId From(string value) => new(value);

        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Value;
        }

        public override string ToString() => Value;
    }
}
