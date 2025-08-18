using System;

namespace NoteNest.UI
{
    public static class FeatureFlags
    {
        public static bool UseNewArchitecture
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("NOTE_NEST_USE_NEW_ARCH");
                if (bool.TryParse(env, out var val)) return val;
                return true; // default ON
            }
        }
    }
}


