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

            var sw = Stopwatch.StartNew();
            PluginLogger.Debug($"EXEC git {arguments} (cwd='{psi.WorkingDirectory}')");

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
                    sw.Stop();
                    PluginLogger.Warn($"git {arguments} timed out after {sw.ElapsedMilliseconds}ms");
                    return new GitCommandResult { ExitCode = -1, Output = output.ToString(), Error = "git command timed out" };
                }

                sw.Stop();
                var result = new GitCommandResult
                {
                    ExitCode = process.ExitCode,
                    Output = output.ToString(),
                    Error = error.ToString()
                };
                PluginLogger.Debug($"EXIT {process.ExitCode} git {arguments} in {sw.ElapsedMilliseconds}ms");
                if (!string.IsNullOrWhiteSpace(result.Error)) PluginLogger.Debug($"STDERR: {result.Error.Trim()}");
                if (!string.IsNullOrWhiteSpace(result.Output)) PluginLogger.Debug($"STDOUT: {result.Output.Trim()}");
                return result;
            }
        }
    }
}
