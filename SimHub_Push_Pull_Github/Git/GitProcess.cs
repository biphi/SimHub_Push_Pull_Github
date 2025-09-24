using System;
using System.Diagnostics;
using System.Text;

namespace SimHub_Push_Pull_Github.Git
{
    internal class GitCommandResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }
        public bool Success { get { return ExitCode == 0; } }
    }

    internal static class GitProcess
    {
        public static bool IsGitAvailable()
        {
            try
            {
                var result = Run(null, "--version", 5000);
                return result.Success && result.Output.IndexOf("git version", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        public static GitCommandResult Run(string workingDirectory, string arguments, int timeoutMs = 60000)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (var process = new Process { StartInfo = psi })
            {
                var output = new StringBuilder();
                var error = new StringBuilder();

                process.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) error.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(); } catch { /* ignore */ }
                    return new GitCommandResult { ExitCode = -1, Output = output.ToString(), Error = "git command timed out" };
                }

                return new GitCommandResult
                {
                    ExitCode = process.ExitCode,
                    Output = output.ToString(),
                    Error = error.ToString()
                };
            }
        }
    }
}
