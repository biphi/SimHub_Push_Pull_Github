using System;
using System.IO;
using System.Reflection;
using SimHub.Plugins;

namespace SimHub_Push_Pull_Github
{
    // Core plugin (no WPF dependencies). UI is in partial class guarded by SIMHUB_WPF.
    public partial class GithubSyncPlugin : IPlugin
    {
        private DashboardSyncService _sync;
        private string _dashboardsPath;
        private PluginSettings _settings;

        public string Name => "GitHub Dashboard Sync";
        public string Author => "GitHub Copilot";
        public PluginManager PluginManager { get; set; }

        public void Init(PluginManager pluginManager)
        {
            PluginManager = pluginManager;

            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _dashboardsPath = Path.Combine(docs, "SimHub", "Dashboards");
            _sync = new DashboardSyncService(_dashboardsPath);
            _sync.EnsureGitInitialized();

            _settings = PluginSettings.Load();
            if (!string.IsNullOrWhiteSpace(_settings.RemoteUrl))
            {
                _sync.SetRemote("origin", _settings.RemoteUrl);
            }
            var branch = string.IsNullOrWhiteSpace(_settings.Branch) ? "main" : _settings.Branch;
            _sync.CheckoutOrCreateBranch(branch);

            if (_settings.AutoPullOnStart)
            {
                _sync.Pull("origin", branch);
            }

            // Register actions for buttons/hotkeys (reflection to be resilient across SimHub versions)
            TryRegisterAction(pluginManager, "Git Pull", "Pull from remote origin", (Action)GitPull);
            TryRegisterAction(pluginManager, "Git Push", "Push to remote origin", (Action)GitPush);
            TryRegisterAction(pluginManager, "Git Commit All", "Commit all changes", (Action)GitCommitAll);
        }

        public void End(PluginManager pluginManager)
        {
            // no-op
        }

        // Public actions
        public void GitPull()
        {
            var branch = string.IsNullOrWhiteSpace(_settings?.Branch) ? "main" : _settings.Branch;
            _sync?.Pull("origin", branch);
        }

        public void GitPush()
        {
            var branch = string.IsNullOrWhiteSpace(_settings?.Branch) ? "main" : _settings.Branch;
            _sync?.Push("origin", branch);
        }

        public void GitCommitAll()
        {
            _sync?.CommitAll($"Update from SimHub at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        public void SaveSettings(string remoteUrl, string branch, bool autoPullOnStart)
        {
            if (_settings == null) _settings = new PluginSettings();
            _settings.RemoteUrl = remoteUrl ?? string.Empty;
            _settings.Branch = string.IsNullOrWhiteSpace(branch) ? "main" : branch.Trim();
            _settings.AutoPullOnStart = autoPullOnStart;
            _settings.Save();

            if (!string.IsNullOrWhiteSpace(_settings.RemoteUrl))
            {
                _sync?.SetRemote("origin", _settings.RemoteUrl);
            }
            _sync?.CheckoutOrCreateBranch(_settings.Branch);
        }

        // Reflection-based action registration (covers ActionManager / ActionsManager differences)
        private void TryRegisterAction(PluginManager pm, string name, string description, Action action)
        {
            if (pm == null || action == null) return;
            var pmType = pm.GetType();

            object actionsObj = null;
            foreach (var propName in new[] { "ActionsManager", "ActionManager", "Actions" })
            {
                var prop = pmType.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                {
                    actionsObj = prop.GetValue(pm);
                    if (actionsObj != null) break;
                }
            }
            if (actionsObj == null) actionsObj = pm;

            var actType = actionsObj.GetType();
            foreach (var methodName in new[] { "RegisterAction", "AddAction" })
            {
                var methods = actType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                foreach (var m in methods)
                {
                    if (m.Name != methodName) continue;
                    var ps = m.GetParameters();
                    if (ps.Length >= 2 && ps[ps.Length - 1].ParameterType == typeof(Action))
                    {
                        try
                        {
                            object[] args;
                            if (ps.Length == 2)
                                args = new object[] { name, action };
                            else if (ps.Length == 3)
                                args = new object[] { name, description, action };
                            else if (ps.Length >= 4)
                                args = new object[] { name, description, action, null };
                            else
                                continue;

                            m.Invoke(actionsObj, args);
                            return;
                        }
                        catch { }
                    }
                }
            }
        }
    }
}
