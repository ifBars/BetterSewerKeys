using MelonLoader;

namespace BetterSewerKeys.Utils
{
    /// <summary>
    /// Centralized logging service for the BetterSewerKeys mod
    /// </summary>
    public static class ModLogger
    {
        /// <summary>
        /// Log an informational message
        /// </summary>
        public static void Info(string message)
        {
            MelonLogger.Msg($"[{Constants.MOD_NAME}] {message}");
        }

        /// <summary>
        /// Log a warning message
        /// </summary>
        public static void Warning(string message)
        {
            MelonLogger.Warning($"[{Constants.MOD_NAME}] {message}");
        }

        /// <summary>
        /// Log an error message
        /// </summary>
        public static void Error(string message)
        {
            MelonLogger.Error($"[{Constants.MOD_NAME}] {message}");
        }

        /// <summary>
        /// Log an error with exception details
        /// </summary>
        public static void Error(string message, System.Exception exception)
        {
            MelonLogger.Error($"[{Constants.MOD_NAME}] {message}: {exception.Message}");
            MelonLogger.Error($"Stack trace: {exception.StackTrace}");
        }

        /// <summary>
        /// Log a debug message (only in debug builds)
        /// </summary>
        public static void Debug(string message)
        {
#if DEBUG
            MelonLogger.Msg($"[{Constants.MOD_NAME}] [DEBUG] {message}");
#endif
        }

        /// <summary>
        /// Log mod initialization
        /// </summary>
        public static void LogInitialization()
        {
            Info($"Initializing {Constants.MOD_NAME} v{Constants.MOD_VERSION} by {Constants.MOD_AUTHOR}");
        }

        /// <summary>
        /// Log mod shutdown
        /// </summary>
        public static void LogShutdown()
        {
            Info($"{Constants.MOD_NAME} shutting down");
        }
    }
}
