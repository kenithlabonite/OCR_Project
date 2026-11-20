using System;

namespace Project_OLMS.Services
{
    /// <summary>
    /// Tracks the lifetime and run instance of the backend system.
    /// A new instance ID is generated every time the backend starts up.
    /// If the backend process stops or restarts, the instance ID changes,
    /// invalidating tokens created during previous runs.
    /// </summary>
    public static class SystemSessionService
    {
        public static readonly string SystemInstanceId = Guid.NewGuid().ToString("N");
        public static readonly DateTime SystemStartTimeUtc = DateTime.UtcNow;
    }
}
