using System;
using System.IO;

namespace SimHub_Push_Pull_Github.Git
{
    internal class GitRepositoryManager
    {
        public string RepoPath { get; }

        public GitRepositoryManager(string repoPath)
        {
            if (string.IsNullOrWhiteSpace(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            RepoPath = Path.GetFullPath(repoPath);
        }

        public bool IsRepository()
        {
            return Directory.Exists(Path.Combine(RepoPath, ".git"));
        }

        public bool InitIfNeeded()
        {
            if (IsRepository()) return true;
            var res = GitProcess.Run(RepoPath, "init");
            return res.Success;
        }

        public bool AddAll()
        {
            var res = GitProcess.Run(RepoPath, "add -A");
            return res.Success;
        }

        public bool Commit(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) message = "Update dashboards";
            var res = GitProcess.Run(RepoPath, $"commit -m \"{EscapeMessage(message)}\" --allow-empty");
            return res.Success;
        }

        public bool SetRemote(string name, string url)
        {
            var remove = GitProcess.Run(RepoPath, $"remote remove {name}");
            var add = GitProcess.Run(RepoPath, $"remote add {name} {url}");
            return add.Success;
        }

        public bool Fetch(string remote = "origin")
        {
            var res = GitProcess.Run(RepoPath, $"fetch {remote}");
            return res.Success;
        }

        public bool Pull(string remote = "origin", string branch = "main")
        {
            var res = GitProcess.Run(RepoPath, $"pull {remote} {branch} --ff-only");
            return res.Success;
        }

        public bool Push(string remote = "origin", string branch = "main")
        {
            var res = GitProcess.Run(RepoPath, $"push {remote} {branch}");
            return res.Success;
        }

        public bool CheckoutOrCreateBranch(string branch)
        {
            var checkout = GitProcess.Run(RepoPath, $"checkout {branch}");
            if (checkout.Success) return true;
            var create = GitProcess.Run(RepoPath, $"checkout -b {branch}");
            return create.Success;
        }

        private static string EscapeMessage(string message)
        {
            return message.Replace("\"", "'");
        }
    }
}
