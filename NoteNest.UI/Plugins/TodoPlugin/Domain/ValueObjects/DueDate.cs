using System;
using System.Collections.Generic;
using NoteNest.Domain.Common;

namespace NoteNest.UI.Plugins.TodoPlugin.Domain.ValueObjects
{
    public class DueDate : ValueObject
    {
        public DateTime Value { get; }

        private DueDate(DateTime value)
        {
            Value = value.Date; // Store only the date part
        }

        public static Result<DueDate> Create(DateTime date)
        {
            return Result.Ok(new DueDate(date));
        }

        public bool IsOverdue()
        {
            return Value.Date < DateTime.UtcNow.Date;
        }

        public bool IsToday()
        {
            return Value.Date == DateTime.UtcNow.Date;
        }

        public bool IsTomorrow()
        {
            return Value.Date == DateTime.UtcNow.Date.AddDays(1);
        }

        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Value;
        }

        public override string ToString() => Value.ToShortDateString();
    }
}

