using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;

namespace RSTGameTranslation
{
    /// <summary>
    /// "Start with Windows". RST always runs as administrator, and Windows skips elevated programs listed
    /// under the registry Run key at sign-in, so a Task Scheduler task with a logon trigger is used instead.
    /// The task itself is the setting: it exists when start with Windows is on.
    /// </summary>
    public static class StartupManager
    {
        // Passed by the task, so the app knows it was started at sign-in
        public const string AutostartArgument = "--autostart";

        private static readonly XNamespace TaskNs = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        // One task per Windows user: each starts RST for themselves
        private static string TaskName
        {
            get
            {
                var name = new StringBuilder("RSTGameTranslation_");
                foreach (char c in Environment.UserName)
                    name.Append(char.IsLetterOrDigit(c) ? c : '_');
                return name.ToString();
            }
        }

        private static string ExecutablePath => Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;

        public static bool IsEnabled() => GetRegisteredCommand() != null;

        /// <summary>Creates or replaces the task; null on success, otherwise the error.</summary>
        public static string? Enable()
        {
            string xmlPath = Path.Combine(Path.GetTempPath(), $"{TaskName}.xml");
            try
            {
                // schtasks reads task XML as UTF-16
                File.WriteAllText(xmlPath, BuildTaskXml(), Encoding.Unicode);
                var (exitCode, output) = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F");
                return exitCode == 0 ? null : output;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            finally
            {
                try { File.Delete(xmlPath); } catch { }
            }
        }

        /// <summary>Removes the task; null on success, otherwise the error.</summary>
        public static string? Disable()
        {
            try
            {
                if (!IsEnabled())
                    return null;
                var (exitCode, output) = RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
                return exitCode == 0 ? null : output;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>
        /// When the RST folder was moved or copied elsewhere, point the task at this copy of rst.exe,
        /// otherwise sign-in would start the old copy or nothing.
        /// </summary>
        public static void RepairIfMoved()
        {
            try
            {
                string? command = GetRegisteredCommand();
                if (command == null)
                    return;
                if (!string.Equals(command, ExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Start with Windows pointed at '{command}'; updating it to {ExecutablePath}");
                    string? error = Enable();
                    if (error != null)
                        Console.WriteLine($"Could not update the start with Windows task: {error}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not check the start with Windows task: {ex.Message}");
            }
        }

        // The program the task starts ("" when it cannot be read), or null when there is no task
        private static string? GetRegisteredCommand()
        {
            try
            {
                var (exitCode, output) = RunSchtasks($"/Query /TN \"{TaskName}\" /XML");
                if (exitCode != 0)
                    return null;
                XElement? command = XDocument.Parse(output).Root?
                    .Element(TaskNs + "Actions")?.Element(TaskNs + "Exec")?.Element(TaskNs + "Command");
                return command?.Value.Trim().Trim('"') ?? string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not read the start with Windows task: {ex.Message}");
                return string.Empty;
            }
        }

        private static string BuildTaskXml()
        {
            string exe = ExecutablePath;
            string userSid = WindowsIdentity.GetCurrent().User!.Value;
            XNamespace ns = TaskNs;

            var task = new XElement(ns + "Task", new XAttribute("version", "1.2"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Description", "Starts RST Game Translation when you sign in to Windows")),
                new XElement(ns + "Triggers",
                    new XElement(ns + "LogonTrigger",
                        new XElement(ns + "Enabled", "true"),
                        new XElement(ns + "UserId", userSid),
                        // Let the desktop and tray finish loading first
                        new XElement(ns + "Delay", "PT10S"))),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal", new XAttribute("id", "Author"),
                        new XElement(ns + "UserId", userSid),
                        new XElement(ns + "LogonType", "InteractiveToken"),
                        // Admin rights without a UAC prompt at every sign-in
                        new XElement(ns + "RunLevel", "HighestAvailable"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(ns + "AllowHardTerminate", "true"),
                    new XElement(ns + "StartWhenAvailable", "false"),
                    new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(ns + "IdleSettings",
                        new XElement(ns + "StopOnIdleEnd", "false"),
                        new XElement(ns + "RestartOnIdle", "false")),
                    new XElement(ns + "AllowStartOnDemand", "true"),
                    new XElement(ns + "Enabled", "true"),
                    new XElement(ns + "Hidden", "false"),
                    new XElement(ns + "RunOnlyIfIdle", "false"),
                    // The default stops the task (and RST with it) after 3 days
                    new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                    // The default (7) runs the program below normal priority
                    new XElement(ns + "Priority", "4")),
                new XElement(ns + "Actions", new XAttribute("Context", "Author"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", $"\"{exe}\""),
                        new XElement(ns + "Arguments", AutostartArgument),
                        new XElement(ns + "WorkingDirectory", Path.GetDirectoryName(exe)))));

            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" + Environment.NewLine + task;
        }

        private static (int exitCode, string output) RunSchtasks(string arguments)
        {
            var info = new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(info)!;
            var stderr = process.StandardError.ReadToEndAsync();
            string stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            string error = stderr.Result.Trim();
            return (process.ExitCode, process.ExitCode == 0 ? stdout : (error.Length > 0 ? error : stdout.Trim()));
        }
    }
}
