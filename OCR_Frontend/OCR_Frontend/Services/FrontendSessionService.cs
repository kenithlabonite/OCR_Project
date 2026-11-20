using System;

namespace OCR_Frontend.Services
{
    /// <summary>
    /// Tracks the lifetime and run instance of the frontend system.
    /// A new instance ID is generated every time the frontend starts up.
    /// If the frontend process stops or restarts, the instance ID changes,
    /// invalidating sessions from previous runs.
    /// </summary>
    public static class FrontendSessionService
    {
        public static readonly string FrontendInstanceId = Guid.NewGuid().ToString("N");
        public static readonly DateTime FrontendStartTimeUtc = DateTime.UtcNow;
    }
}
