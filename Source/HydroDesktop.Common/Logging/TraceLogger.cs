using System;
using System.Diagnostics;
using System.Text;

namespace HydroDesktop.Common.Logging
{
    /// <summary>
    /// Simple logger, which uses Trace class.
    /// </summary>
    class TraceLogger : IExtraLog
    {
        public TraceLogger(ILogInitializer initializer)
        {
            SkipFrames = 3;
            Destination = initializer.Destination;
        }

        public void Info(string message)
        {
            LogMessage(message, "INFO");
        }

        public void Warn(string message)
        {
            LogMessage(message, "WARN");
        }

        public void Error(string message, Exception exception = null)
        {
            LogMessage(message, "ERROR", exception);
            // Flush to see message in log immediately after error.
            Trace.Flush();
        }

        public string Destination { get; private set; }

        private void LogMessage(string message, string category, Exception exception = null)
        {
            if (!IsEnabled()) return;

            var sb = new StringBuilder();
            sb.AppendFormat("[{0}] [{1}] [{2}]", DateTime.Now, category, GetCallingMethod());
            sb.AppendLine();
            sb.Append(message);
            if (exception != null)
            {
                sb.AppendLine();
                sb.AppendLine(exception.Message);
                sb.AppendLine("Inner: " + (exception.InnerException != null? exception.InnerException.ToString() : "NULL"));
                sb.Append("Stack trace: " + exception.StackTrace);
            }
            Trace.WriteLine(sb.ToString());
        }

        private static bool IsEnabled()
        {
#if TRACE
            return true;
#else
            return false;
#endif
        }

        private string GetCallingMethod()
        {
            if (!IsEnabled()) return string.Empty;
            var frame = new StackFrame(SkipFrames);
            var method = frame.GetMethod();
            return method.DeclaringType != null ? method.DeclaringType.FullName + "." + method.Name : method.Name;
        }

        #region Implementation of IExtraLog

        public int SkipFrames { get; set; }

        #endregion
    }
}