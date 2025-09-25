using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SimHub_Push_Pull_Github
{
    internal static class NativeResolver
    {
        private static bool _resolverRegistered;

        private static string Normalize(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return string.Empty;
            return p.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public static void RegisterAssemblyResolver()
        {
            if (_resolverRegistered) return;
            try
            {
                AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
                _resolverRegistered = true;
                PluginLogger.Info("AssemblyResolve for LibGit2Sharp registered.");
            }
            catch (Exception ex)
            {
                PluginLogger.Warn("RegisterAssemblyResolver failed: " + ex.Message);
            }
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                var name = new AssemblyName(args.Name).Name;
                if (!string.Equals(name, "LibGit2Sharp", StringComparison.OrdinalIgnoreCase)) return null;

                var asmPath = typeof(GithubSyncPlugin).Assembly.Location;
                var asmDir = Path.GetDirectoryName(asmPath);
                if (string.IsNullOrEmpty(asmDir)) return null;

                foreach (var candidate in new[]
                {
                    Path.Combine(asmDir, "LibGit2Sharp.dll"),
                    Path.Combine(asmDir, "libs", "LibGit2Sharp.dll")
                })
                {
                    if (File.Exists(candidate))
                    {
                        PluginLogger.Info($"Resolving LibGit2Sharp from '{candidate}'");
                        return Assembly.LoadFrom(candidate);
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                PluginLogger.Warn("OnAssemblyResolve failed: " + ex.Message);
                return null;
            }
        }

        public static void EnsureLibGit2SharpNativeOnPath()
        {
            try
            {
                var asmPath = typeof(GithubSyncPlugin).Assembly.Location;
                var asmDir = Path.GetDirectoryName(asmPath);
                if (string.IsNullOrEmpty(asmDir)) return;

                var arch = Environment.Is64BitProcess ? "win-x64" : "win-x86";
                var candidates = new[]
                {
                    asmDir,
                    Path.Combine(asmDir, "runtimes", arch, "native"),
                    Path.Combine(asmDir, "runtimes", "win-x64", "native"),
                    Path.Combine(asmDir, "runtimes", "win-x86", "native"),
                };

                var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                var parts = currentPath.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(Normalize)
                                        .ToList();
                var changed = false;
                foreach (var dir in candidates)
                {
                    if (Directory.Exists(dir))
                    {
                        var n = Normalize(dir);
                        if (!parts.Any(p => string.Equals(p, n, StringComparison.OrdinalIgnoreCase)))
                        {
                            parts.Insert(0, n);
                            changed = true;
                        }
                    }
                }
                if (changed)
                {
                    var newPath = string.Join(";", parts);
                    Environment.SetEnvironmentVariable("PATH", newPath);
                    PluginLogger.Info("Added libgit2 native paths to PATH for LibGit2Sharp.");
                }
            }
            catch (Exception ex)
            {
                PluginLogger.Warn("EnsureLibGit2SharpNativeOnPath failed: " + ex.Message);
            }
        }
    }
}
