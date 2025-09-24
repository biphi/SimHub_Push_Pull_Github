using System;
using System.IO;

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
            if (!Git.GitProcess.IsGitAvailable())
            {
                return false;
            }
            return _git.InitIfNeeded();
        }

        public bool CommitAll(string message)
        {
            if (!EnsureGitInitialized()) return false;
            if (!_git.AddAll()) return false;
            return _git.Commit(message);
        }

        public bool Pull(string remote = "origin", string branch = "main")
        {
            if (!EnsureGitInitialized()) return false;
            return _git.Pull(remote, branch);
        }

        public bool Push(string remote = "origin", string branch = "main")
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
