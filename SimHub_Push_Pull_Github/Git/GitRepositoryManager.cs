using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
            PluginLogger.Warn("No git credentials configured (username/token). Read-only operations may still work for public repos.");
            return null;
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

        private static string ToPublicHttpsIfPossible(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url)) return null;
                var u = url.Trim();
                // git@github.com:owner/repo(.git)
                if (u.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
                {
                    var tail = u.Substring("git@github.com:".Length);
                    if (tail.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) tail = tail.Substring(0, tail.Length - 4);
                    return "https://github.com/" + tail + ".git";
                }
                // https://user[:pass]@github.com/owner/repo(.git) -> strip credentials
                if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    // strip userinfo if any
                    var idxScheme = u.IndexOf("://", StringComparison.Ordinal);
                    if (idxScheme > 0)
                    {
                        var scheme = u.Substring(0, idxScheme + 3); // incl ://
                        var rest = u.Substring(idxScheme + 3);
                        var at = rest.IndexOf('@');
                        if (at >= 0)
                        {
                            rest = rest.Substring(at + 1); // drop credentials
                        }
                        // ensure https
                        if (!scheme.Equals("https://", StringComparison.OrdinalIgnoreCase)) scheme = "https://";
                        // ensure .git suffix
                        if (!rest.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) rest += ".git";
                        return scheme + rest;
                    }
                }
            }
            catch { }
            return null;
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
                    try
                    {
                        Commands.Fetch(repo, r.Name, refSpecs, new FetchOptions
                        {
                            CredentialsProvider = creds
                        }, null);
                        return true;
                    }
                    catch (LibGit2SharpException ex)
                    {
                        // Fallback for public GitHub repos when no credentials are set: convert to anonymous HTTPS and retry
                        if (creds == null)
                        {
                            var anon = ToPublicHttpsIfPossible(r.Url);
                            if (!string.IsNullOrWhiteSpace(anon) && !string.Equals(anon, r.Url, StringComparison.OrdinalIgnoreCase))
                            {
                                PluginLogger.Info($"Fetch failed with URL '{r.Url}'. Retrying anonymously via '{anon}'...");
                                repo.Network.Remotes.Update(r.Name, rr => rr.Url = anon);
                                Commands.Fetch(repo, r.Name, refSpecs, new FetchOptions { CredentialsProvider = null }, null);
                                return true;
                            }
                        }
                        PluginLogger.Error("Fetch failed", ex);
                        return false;
                    }
                }
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

        // Tag support
        public bool TagExists(string tagName)
        {
            try
            {
                using (var repo = new Repository(RepoPath))
                {
                    return repo.Tags[tagName] != null;
                }
            }
            catch (Exception ex)
            {
                PluginLogger.Error("TagExists failed", ex);
                return false;
            }
        }

        public string CreateAnnotatedTag(string tagName, string message)
        {
            try
            {
                using (var repo = new Repository(RepoPath))
                {
                    var head = repo.Head.Tip;
                    if (head == null)
                    {
                        PluginLogger.Warn("Cannot create tag: repository has no commits.");
                        return null;
                    }
                    if (repo.Tags[tagName] != null)
                    {
                        PluginLogger.Warn($"Tag already exists: {tagName}");
                        return tagName;
                    }
                    var sig = GetSignature();
                    var tag = repo.ApplyTag(tagName, head.Sha, sig, message ?? tagName);
                    PluginLogger.Info($"Created tag '{tagName}' on {head.Sha}");
                    return tag.FriendlyName;
                }
            }
            catch (Exception ex)
            {
                PluginLogger.Error("CreateAnnotatedTag failed", ex);
                return null;
            }
        }

        public bool PushTag(string tagName, string remote = "origin")
        {
            try
            {
                using (var repo = new Repository(RepoPath))
                {
                    var remoteObj = repo.Network.Remotes[remote] ?? repo.Network.Remotes.FirstOrDefault();
                    if (remoteObj == null)
                    {
                        PluginLogger.Warn("No remote configured for tag push");
                        return false;
                    }
                    var creds = BuildCredentialsHandler();
                    var refSpec = "refs/tags/" + tagName;
                    repo.Network.Push(remoteObj, refSpec, new PushOptions { CredentialsProvider = creds });
                    PluginLogger.Info($"Pushed tag '{tagName}' to {remoteObj.Name}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                PluginLogger.Error("PushTag failed", ex);
                return false;
            }
        }

        private static Signature GetSignature()
        {
            return new Signature("SimHub", "simhub@example.com", DateTimeOffset.Now);
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
    }
}
