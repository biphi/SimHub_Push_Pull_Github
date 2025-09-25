using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using LibGit2Sharp;

namespace SimHub_Push_Pull_Github
{
    // This service abstracts where SimHub stores dashboards and provides sync helpers.
    public class DashboardSyncService
    {
        public string DashboardsPath { get; }
        private readonly Git.GitRepositoryManager _git;

        public DashboardSyncService(string dashboardsPath)
        {
            if (string.IsNullOrWhiteSpace(dashboardsPath)) throw new ArgumentNullException(nameof(dashboardsPath));
            DashboardsPath = Path.GetFullPath(dashboardsPath);
            Directory.CreateDirectory(DashboardsPath);
            _git = new Git.GitRepositoryManager(DashboardsPath);
        }

        public bool EnsureGitInitialized()
        {
            // LibGit2Sharp does not require system git; just initialize repository if needed
            return _git.InitIfNeeded();
        }

        public IEnumerable<string> ListLocalDashboards()
        {
            if (!Directory.Exists(DashboardsPath)) return Enumerable.Empty<string>();
            return Directory.GetDirectories(DashboardsPath).Select(Path.GetFileName).OrderBy(n => n).ToArray();
        }

        public sealed class DashboardInfo
        {
            public string Name { get; set; }
            public DateTime? LastWriteUtc { get; set; }
        }

        public IEnumerable<string> ListRemoteDashboards(string remoteUrl, string branch)
        {
            try
            {
                var cacheRoot = Path.Combine(Path.GetTempPath(), "SimHub_GitCache");
                Directory.CreateDirectory(cacheRoot);
                var cacheDir = Path.Combine(cacheRoot, "repo");
                if (Directory.Exists(cacheDir) && !Directory.Exists(Path.Combine(cacheDir, ".git")))
                {
                    try { Directory.Delete(cacheDir, true); } catch { }
                }
                var mgr = new Git.GitRepositoryManager(cacheDir);
                // Init Repo, Remote setzen, fetchen und Arbeitsverzeichnis auf Remote-Branch setzen
                if (!mgr.InitIfNeeded())
                {
                    return Enumerable.Empty<string>();
                }
                var b = string.IsNullOrWhiteSpace(branch) ? "master" : branch;
                mgr.SetRemote("origin", remoteUrl);
                mgr.Fetch("origin");
                EnsureWorkingCopyMatchesRemote(cacheDir, b);

                // List top-level directories in cache
                if (!Directory.Exists(cacheDir)) return Enumerable.Empty<string>();
                return Directory.GetDirectories(cacheDir)
                    .Select(Path.GetFileName)
                    .Where(n => !string.Equals(n, ".git", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(n => n)
                    .ToArray();
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }

        public IEnumerable<DashboardInfo> ListRemoteDashboardsWithDates(string remoteUrl, string branch)
        {
            try
            {
                var cacheRoot = Path.Combine(Path.GetTempPath(), "SimHub_GitCache");
                Directory.CreateDirectory(cacheRoot);
                var cacheDir = Path.Combine(cacheRoot, "repo");
                if (Directory.Exists(cacheDir) && !Directory.Exists(Path.Combine(cacheDir, ".git")))
                {
                    try { Directory.Delete(cacheDir, true); } catch { }
                }
                var mgr = new Git.GitRepositoryManager(cacheDir);
                if (!mgr.InitIfNeeded()) return Enumerable.Empty<DashboardInfo>();
                var b = string.IsNullOrWhiteSpace(branch) ? "master" : branch;
                mgr.SetRemote("origin", remoteUrl);
                mgr.Fetch("origin");
                EnsureWorkingCopyMatchesRemote(cacheDir, b);

                if (!Directory.Exists(cacheDir)) return Enumerable.Empty<DashboardInfo>();
                var names = Directory.GetDirectories(cacheDir)
                    .Select(Path.GetFileName)
                    .Where(n => !string.Equals(n, ".git", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(n => n)
                    .ToArray();

                var list = new List<DashboardInfo>();
                using (var repo = new Repository(cacheDir))
                {
                    foreach (var name in names)
                    {
                        DateTime? last = GetLastCommitTimeUtcForPath(repo, name);
                        list.Add(new DashboardInfo { Name = name, LastWriteUtc = last });
                    }
                }
                return list;
            }
            catch
            {
                return Enumerable.Empty<DashboardInfo>();
            }
        }

        private static void EnsureWorkingCopyMatchesRemote(string repoPath, string branch)
        {
            using (var repo = new Repository(repoPath))
            {
                var remoteBranch = repo.Branches[$"origin/{branch}"];
                if (remoteBranch == null) return;

                var local = repo.Branches[branch] ?? repo.CreateBranch(branch, remoteBranch.Tip);
                repo.Branches.Update(local, b => b.TrackedBranch = remoteBranch.CanonicalName);
                Commands.Checkout(repo, local);
                // Stelle sicher, dass der Arbeitsbaum exakt dem Remote-Stand entspricht
                repo.Reset(ResetMode.Hard, remoteBranch.Tip);
            }
        }

        private static DateTime? GetLastCommitTimeUtcForPath(Repository repo, string topLevelFolder)
        {
            try
            {
                if (repo == null || string.IsNullOrWhiteSpace(topLevelFolder)) return null;
                var rel = topLevelFolder.Replace('\\', '/').Trim('/');

                // 1) Schnellweg: direkte Historie für Pfad abfragen
                try
                {
                    var entry = repo.Commits.QueryBy(rel).FirstOrDefault();
                    if (entry != null)
                    {
                        return entry.Commit.Committer.When.UtcDateTime;
                    }
                }
                catch { }

                // 2) Fallback: über Diffs je Commit prüfen
                var prefix = rel + "/";
                foreach (var commit in repo.Commits)
                {
                    try
                    {
                        var parent = commit.Parents.FirstOrDefault();
                        var parentTree = parent?.Tree;
                        var changes = repo.Diff.Compare<TreeChanges>(parentTree, commit.Tree);
                        if (changes.Any(ch =>
                            (!string.IsNullOrEmpty(ch.Path) && ch.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(ch.OldPath) && ch.OldPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))))
                        {
                            return commit.Committer.When.UtcDateTime;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        public bool CommitAll(string message)
        {
            if (!EnsureGitInitialized()) return false;
            if (!_git.AddAll()) return false;
            return _git.Commit(message);
        }

        public bool CommitSelected(string message, IEnumerable<string> dashboardNames)
        {
            if (!EnsureGitInitialized()) return false;
            var relPaths = ToRelativeDashboardDirs(dashboardNames);
            if (!_git.AddPaths(relPaths)) return false;
            return _git.Commit(message);
        }

        private IEnumerable<string> ToRelativeDashboardDirs(IEnumerable<string> dashboardNames)
        {
            if (dashboardNames == null) return new string[0];
            var names = dashboardNames.Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();
            if (names.Length == 0) return new string[0];
            // Each dashboard is a directory directly under DashboardsPath
            return names.Select(n => n.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).ToArray();
        }

        public bool Pull(string remote = "origin", string branch = "master")
        {
            if (!EnsureGitInitialized()) return false;
            return _git.Pull(remote, branch);
        }

        public bool Push(string remote = "origin", string branch = "master")
        {
            if (!EnsureGitInitialized()) return false;
            return _git.Push(remote, branch);
        }

        public bool SetRemote(string name, string url)
        {
            if (!EnsureGitInitialized()) return false;
            return _git.SetRemote(name, url);
        }

        public bool CheckoutOrCreateBranch(string branch)
        {
            if (!EnsureGitInitialized()) return false;
            return _git.CheckoutOrCreateBranch(branch);
        }
    }
}
