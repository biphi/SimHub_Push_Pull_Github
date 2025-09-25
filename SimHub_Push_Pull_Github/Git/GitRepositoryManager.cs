using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;

namespace SimHub_Push_Pull_Github.Git
{
    internal class GitRepositoryManager
    {
        public string RepoPath { get; }

        public GitRepositoryManager(string repoPath)
        {
            if (string.IsNullOrWhiteSpace(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            RepoPath = Path.GetFullPath(repoPath);
            Directory.CreateDirectory(RepoPath);
            PluginLogger.Info($"GitRepositoryManager (LibGit2Sharp) for '{RepoPath}' PATH={Environment.GetEnvironmentVariable("PATH")}");
        }

        private (string user, string token) GetCredentials()
        {
            try
            {
                var settings = PluginSettings.Load();
                if (!string.IsNullOrWhiteSpace(settings.GitUsername) || !string.IsNullOrWhiteSpace(settings.GitToken))
                {
                    return (settings.GitUsername ?? string.Empty, settings.GitToken ?? string.Empty);
                }
            }
            catch { }
            var user = Environment.GetEnvironmentVariable("SIMHUB_GIT_USERNAME") ?? string.Empty;
            var token = Environment.GetEnvironmentVariable("SIMHUB_GIT_TOKEN") ?? string.Empty;
            return (user, token);
        }

        private CredentialsHandler BuildCredentialsHandler()
        {
            var (user, token) = GetCredentials();
            if (!string.IsNullOrWhiteSpace(token))
            {
                var u = string.IsNullOrWhiteSpace(user) ? "git" : user; // GitHub accepts any non-empty user when using PAT
                PluginLogger.Info($"Using git credentials: user='{u}', tokenLen={(token ?? string.Empty).Length}");
                return (_url, _user, _types) => new UsernamePasswordCredentials { Username = u, Password = token };
            }
            PluginLogger.Warn("No git credentials configured (username/token). Push may fail.");
            return null;
        }

        private bool HasWriteAccess()
        {
            try
            {
                var testFile = Path.Combine(RepoPath, $".write_test_{Guid.NewGuid():N}.tmp");
                Directory.CreateDirectory(RepoPath);
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                return true;
            }
            catch (Exception ex)
            {
                PluginLogger.Warn($"No write access to '{RepoPath}': {ex.Message}");
                return false;
            }
        }

        private static bool IsUnderGitDirectory(string fullPath, string repoRoot)
        {
            var gitDir = Path.Combine(repoRoot, ".git");
            return fullPath.StartsWith(gitDir, StringComparison.OrdinalIgnoreCase);
        }

        private static string ToRelative(string fullPath, string repoRoot)
        {
            return fullPath.Substring(repoRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool HasChanges(Repository repo)
        {
            var status = repo.RetrieveStatus(new StatusOptions());
            return status.IsDirty;
        }

        public bool IsRepository()
        {
            var ok = Repository.IsValid(RepoPath);
            PluginLogger.Debug($"IsRepository: {ok}");
            return ok;
        }

        public bool InitIfNeeded()
        {
            try
            {
                if (!HasWriteAccess())
                {
                    PluginLogger.Error($"Cannot initialize git repository. Path not writable: '{RepoPath}'");
                    return false;
                }
                if (!IsRepository())
                {
                    Repository.Init(RepoPath);
                    PluginLogger.Info("Repository.Init executed");
                }
                using (var repo = new Repository(RepoPath))
                {
                    PluginLogger.Info($"LibGit2Sharp loaded. Version: {GlobalSettings.Version}");
                }
                return true;
            }
            catch (Exception ex)
            {
                PluginLogger.Error("InitIfNeeded failed", ex);
                return false;
            }
        }

        public bool AddAll()
        {
            try
            {
                using (var repo = new Repository(RepoPath))
                {
                    var files = Directory.EnumerateFiles(RepoPath, "*", SearchOption.AllDirectories)
                        .Where(f => !IsUnderGitDirectory(f, RepoPath))
                        .Select(f => ToRelative(f, RepoPath))
                        .ToArray();
                    foreach (var rel in files)
                    {
                        Commands.Stage(repo, rel);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                PluginLogger.Error("AddAll failed", ex);
                return false;
            }
        }

        public bool AddPaths(IEnumerable<string> relativePaths)
        {
            try
            {
                if (relativePaths == null) return AddAll();
                var paths = relativePaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
                if (paths.Length == 0) return AddAll();
                using (var repo = new Repository(RepoPath))
                {
                    foreach (var p in paths)
                    {
                        var full = Path.Combine(RepoPath, p);
                        if (Directory.Exists(full))
                        {
                            var files = Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
                                .Where(f => !IsUnderGitDirectory(f, RepoPath))
                                .Select(f => ToRelative(f, RepoPath));
                            foreach (var rel in files) Commands.Stage(repo, rel);
                        }
                        else
                        {
                            Commands.Stage(repo, p);
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                PluginLogger.Error("AddPaths failed", ex);
                return false;
            }
        }

        public bool EnsureInitialCommit()
        {
            try
            {
                using (var repo = new Repository(RepoPath))
                {
                    if (repo.Head.Tip != null) return true;
                    var sig = GetSignature();
                    repo.Commit("Initial commit", sig, sig, new CommitOptions { AllowEmptyCommit = true });
                }
                return true;
            }
            catch (Exception ex)
            {
                PluginLogger.Error("EnsureInitialCommit failed", ex);
                return false;
            }
        }

        public bool Commit(string message)
        {
            try
            {
                using (var repo = new Repository(RepoPath))
                {
                    if (!HasChanges(repo))
                    {
                        PluginLogger.Info("No changes to commit.");
                        return false;
                    }
                    var sig = GetSignature();
                    repo.Commit(string.IsNullOrWhiteSpace(message) ? "Update dashboards" : message, sig, sig);
                }
                return true;
            }
            catch (Exception ex)
            {
                PluginLogger.Error("Commit failed", ex);
                return false;
            }
        }

        public bool SetRemote(string name, string url)
        {
            try
            {
                using (var repo = new Repository(RepoPath))
                {
                    var existing = repo.Network.Remotes[name];
                    if (existing != null)
                    {
                        repo.Network.Remotes.Update(name, r => r.Url = url);
                    }
                    else
                    {
                        repo.Network.Remotes.Add(name, url);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                PluginLogger.Error("SetRemote failed", ex);
                return false;
            }
        }

        public bool Fetch(string remote = "origin")
        {
            try
            {
                using (var repo = new Repository(RepoPath))
                {
                    var r = repo.Network.Remotes[remote];
                    if (r == null) return false;
                    var refSpecs = r.FetchRefSpecs.Select(x => x.Specification);
                    var creds = BuildCredentialsHandler();
                    Commands.Fetch(repo, r.Name, refSpecs, new FetchOptions
                    {
                        CredentialsProvider = creds
                    }, null);
                }
                return true;
            }
            catch (Exception ex)
            {
                PluginLogger.Error("Fetch failed", ex);
                return false;
            }
        }

        public bool Pull(string remote = "origin", string branch = "master")
        {
            try
            {
                using (var repo = new Repository(RepoPath))
                {
                    if (!Fetch(remote)) return false;

                    var remoteBranch = repo.Branches[$"{remote}/{branch}"];
                    if (remoteBranch == null)
                    {
                        PluginLogger.Warn($"Remote branch not found: {remote}/{branch}");
                        return false;
                    }

                    var local = repo.Branches[branch] ?? repo.CreateBranch(branch, remoteBranch.Tip);
                    Commands.Checkout(repo, local);
                    repo.Branches.Update(local, b => b.TrackedBranch = remoteBranch.CanonicalName);

                    var sig = GetSignature();
                    try
                    {
                        // Prefer fast-forward
                        var ffResult = repo.Merge(remoteBranch, sig, new MergeOptions { FastForwardStrategy = FastForwardStrategy.FastForwardOnly });
                        return ffResult.Status == MergeStatus.UpToDate || ffResult.Status == MergeStatus.FastForward;
                    }
                    catch (NonFastForwardException)
                    {
                        // Fallback: allow a merge commit if necessary
                        var result = repo.Merge(remoteBranch, sig, new MergeOptions { FastForwardStrategy = FastForwardStrategy.Default });
                        if (result.Status == MergeStatus.Conflicts)
                        {
                            PluginLogger.Warn("Pull resulted in conflicts. Manual resolution required.");
                            return false;
                        }
                        return result.Status == MergeStatus.UpToDate || result.Status == MergeStatus.FastForward || result.Status == MergeStatus.NonFastForward;
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLogger.Error("Pull failed", ex);
                return false;
            }
        }

        public bool Push(string remote = "origin", string branch = "master")
        {
            try
            {
                using (var repo = new Repository(RepoPath))
                {
                    var local = repo.Branches[branch];
                    if (local == null)
                    {
                        PluginLogger.Warn($"Local branch not found: {branch}");
                        return false;
                    }

                    var remoteObj = repo.Network.Remotes[remote] ?? repo.Network.Remotes.FirstOrDefault();
                    if (remoteObj == null)
                    {
                        PluginLogger.Warn("No remote configured");
                        return false;
                    }

                    if (string.IsNullOrEmpty(local.UpstreamBranchCanonicalName))
                    {
                        repo.Branches.Update(local, b => b.TrackedBranch = $"refs/remotes/{remote}/{branch}");
                    }

                    if (!EnsureInitialCommit()) return false;

                    var creds = BuildCredentialsHandler();
                    var pushRefSpec = $"refs/heads/{branch}:refs/heads/{branch}";
                    repo.Network.Push(remoteObj, pushRefSpec, new PushOptions
                    {
                        CredentialsProvider = creds
                    });
                }
                return true;
            }
            catch (NonFastForwardException ex)
            {
                // Auto-recover: try fast-forward pull, then retry push once
                PluginLogger.Warn("Push failed: non fast-forward. Attempting pull then retry push.");
                PluginLogger.Error("Push failed", ex);
                try
                {
                    if (!Pull(remote, branch))
                    {
                        PluginLogger.Warn("Pull attempt after non fast-forward failed.");
                        return false;
                    }
                    using (var repo = new Repository(RepoPath))
                    {
                        var local = repo.Branches[branch];
                        if (local == null)
                        {
                            PluginLogger.Warn($"Local branch not found after pull: {branch}");
                            return false;
                        }
                        var remoteObj = repo.Network.Remotes[remote] ?? repo.Network.Remotes.FirstOrDefault();
                        if (remoteObj == null)
                        {
                            PluginLogger.Warn("No remote configured after pull");
                            return false;
                        }
                        if (string.IsNullOrEmpty(local.UpstreamBranchCanonicalName))
                        {
                            repo.Branches.Update(local, b => b.TrackedBranch = $"refs/remotes/{remote}/{branch}");
                        }
                        var creds = BuildCredentialsHandler();
                        var pushRefSpec = $"refs/heads/{branch}:refs/heads/{branch}";
                        repo.Network.Push(remoteObj, pushRefSpec, new PushOptions
                        {
                            CredentialsProvider = creds
                        });
                        return true;
                    }
                }
                catch (NonFastForwardException retryEx)
                {
                    PluginLogger.Warn("Retry push failed: non fast-forward. Manual intervention required (rebase/merge).");
                    PluginLogger.Error("Retry push failed", retryEx);
                    return false;
                }
                catch (Exception retryEx)
                {
                    PluginLogger.Error("Retry push failed", retryEx);
                    return false;
                }
            }
            catch (Exception ex)
            {
                PluginLogger.Error("Push failed", ex);
                return false;
            }
        }

        public bool CheckoutOrCreateBranch(string branch)
        {
            try
            {
                using (var repo = new Repository(RepoPath))
                {
                    var existing = repo.Branches[branch];
                    if (existing != null)
                    {
                        Commands.Checkout(repo, existing);
                        return true;
                    }

                    if (repo.Head.Tip != null)
                    {
                        var created = repo.CreateBranch(branch, repo.Head.Tip);
                        Commands.Checkout(repo, created);
                        return true;
                    }

                    // Unborn repository: point HEAD to desired branch and create initial empty commit
                    var targetRef = $"refs/heads/{branch}";
                    repo.Refs.UpdateTarget("HEAD", targetRef);
                    var sig = GetSignature();
                    repo.Commit("Initial commit", sig, sig, new CommitOptions { AllowEmptyCommit = true });
                    var checkout = repo.Branches[branch] ?? repo.Head;
                    Commands.Checkout(repo, checkout);
                    return true;
                }
            }
            catch (Exception ex)
            {
                PluginLogger.Error("CheckoutOrCreateBranch failed", ex);
                return false;
            }
        }

        private static Signature GetSignature()
        {
            return new Signature("SimHub", "simhub@example.com", DateTimeOffset.Now);
        }
    }
}
