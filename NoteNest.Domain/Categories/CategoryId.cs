using System;
using System.Collections.Generic;
using NoteNest.Domain.Common;

namespace NoteNest.Domain.Categories
{
    public class CategoryId : ValueObject
    {
        public string Value { get; }

        private CategoryId(string value)
        {
            Value = value;
        }

        public static CategoryId Create() => new(Guid.NewGuid().ToString());
        public static CategoryId From(string value) => new(value);

        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Value;
        }

        public override string ToString() => Value;
    }
}
