namespace V8.Net
{
    using System;

    public static class Exceptions
    {
        /// <summary>
        /// A simple utility method which formats and returns an exception error object.
        /// The stack trace is also included. The inner exceptions are also recursed and added.
        /// </summary>
        /// <param name="ex">The exception object with error message to format and return.</param>
        public static string GetFullErrorMessage(this Exception ex)
        {
            return GetFullErrorMessage(ex, true);
        }

        /// <summary>
        /// A simple utility method which formats and returns an exception error object.
        /// The stack trace is also included. The inner exceptions are also recursed and added.
        /// </summary>
        /// <param name="ex">The exception object with error message to format and return.</param>
        public static string GetFullErrorMessage(this Exception ex, bool includeStackTrace)
        {
            return _GetFullErrorMessage(ex, "", includeStackTrace);
        }

        private static string _GetFullErrorMessage(this Exception ex, string margin)
        {
            // ReSharper disable once IntroduceOptionalParameters.Local
            return _GetFullErrorMessage(ex, margin, true);
        }

        static string _GetFullErrorMessage(this Exception ex, string margin, bool includeStackTrace)
        {
            var msg = margin + "=> Message: " + ex.Message + Environment.NewLine + Environment.NewLine + margin;
            if (includeStackTrace) msg += "=> Stack Trace: " + ex.StackTrace;
            if (ex.InnerException != null)
                msg += Environment.NewLine + Environment.NewLine + "***Inner Exception ***" + Environment.NewLine + _GetFullErrorMessage(ex.InnerException, margin + "==");
            return msg;
        }
    }
}
