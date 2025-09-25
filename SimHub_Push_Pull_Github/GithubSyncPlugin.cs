using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using SimHub.Plugins;

namespace SimHub_Push_Pull_Github
{
    public partial class GithubSyncPlugin : IPlugin
    {
        public string Name => $"GitHub Dashboard Sync v{GetVersionString()}";
        public string Author => "GitHub Copilot";
        public PluginManager PluginManager { get; set; }

        private static string GetVersionString()
        {
            try
            {
                var asm = typeof(GithubSyncPlugin).Assembly;
                var v = asm.GetName().Version;
                var ver = v != null ? v.ToString() : "1.0.0.0";
                var path = asm.Location;
                string suffix = string.Empty;
                try
                {
                    var ts = File.GetLastWriteTime(path);
                    suffix = $"+{ts:yyyyMMdd.HHmm}";
                }
                catch { }
                return ver + suffix;
            }
            catch
            {
                return "1.0.0.0";
            }
        }

        private DashboardSyncService _sync;
        private string _dashboardsPath;
        private PluginSettings _settings;

        public void Init(PluginManager pluginManager)
        {
            PluginManager = pluginManager;

            // Resolve LibGit2Sharp managed and native dependencies
            NativeResolver.RegisterAssemblyResolver();
            NativeResolver.EnsureLibGit2SharpNativeOnPath();

            _settings = PluginSettings.Load();
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var defaultPath = Path.Combine(docs, "SimHub", "Dashboards");
            _dashboardsPath = string.IsNullOrWhiteSpace(_settings.DashboardsPath) ? defaultPath : _settings.DashboardsPath;

            // Initialize logging to %AppData%\SimHub\Plugins\GitHubDashboardSync
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimHub", "Plugins", "GitHubDashboardSync");
            PluginLogger.Initialize(logDir);
            PluginLogger.Info($"Initializing plugin. Dashboards path: {_dashboardsPath}");

            WarnIfProgramFiles(_dashboardsPath);

            _sync = new DashboardSyncService(_dashboardsPath);
            var gitInit = _sync.EnsureGitInitialized();
            PluginLogger.Info($"Git initialized: {gitInit}");

            PluginLogger.Info($"Settings loaded. RemoteUrl={(string.IsNullOrEmpty(_settings.RemoteUrl) ? "<empty>" : _settings.RemoteUrl)}, Branch={_settings.Branch}, AutoPullOnStart={_settings.AutoPullOnStart}");

            try
            {
                if (!string.IsNullOrWhiteSpace(_settings.RemoteUrl))
                {
                    var setRemote = _sync.SetRemote("origin", _settings.RemoteUrl);
                    PluginLogger.Info($"Set remote origin: {setRemote}");
                }

                var branch = string.IsNullOrWhiteSpace(_settings.Branch) ? "master" : _settings.Branch;
                var checkedOut = _sync.CheckoutOrCreateBranch(branch);
                PluginLogger.Info($"Checkout/Create branch '{branch}': {checkedOut}");

                if (_settings.AutoPullOnStart)
                {
                    var pulled = _sync.Pull("origin", branch);
                    PluginLogger.Info($"Auto pull on start from origin/{branch}: {pulled}");
                }
            }
            catch (Exception ex)
            {
                PluginLogger.Error("Error during initialization", ex);
            }

            // Register actions for buttons/hotkeys (reflection to be resilient across SimHub versions)
            TryRegisterAction(pluginManager, "Git Pull", "Pull from remote origin", (Action)GitPull);
            TryRegisterAction(pluginManager, "Git Push", "Push to remote origin", (Action)GitPush);
            TryRegisterAction(pluginManager, "Git Commit All", "Commit all changes", (Action)GitCommitAll);
        }

        private void WarnIfProgramFiles(string path)
        {
            try
            {
                var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                var pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!string.IsNullOrWhiteSpace(path) &&
                    (path.StartsWith(pf, StringComparison.OrdinalIgnoreCase) || path.StartsWith(pf64, StringComparison.OrdinalIgnoreCase)))
                {
                    PluginLogger.Warn($"The selected dashboards path is under Program Files: '{path}'. Write access may be restricted. Consider copying dashboards to '{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SimHub", "Dashboards")}' and selecting that folder in settings.");
                }
            }
            catch { }
        }

        public void End(PluginManager pluginManager)
        {
            PluginLogger.Info("Plugin End called");
        }

        // Public actions
        public void GitPull()
        {
            var branch = string.IsNullOrWhiteSpace(_settings?.Branch) ? "master" : _settings.Branch;
            PluginLogger.Info($"Pull requested for origin/{branch}");
            var ok = _sync?.Pull("origin", branch) ?? false;
            PluginLogger.Info($"Pull result: {ok}");
        }

        public void GitPush()
        {
            var branch = string.IsNullOrWhiteSpace(_settings?.Branch) ? "master" : _settings.Branch;
            PluginLogger.Info($"Push requested for origin/{branch}");
            var ok = _sync?.Push("origin", branch) ?? false;
            PluginLogger.Info($"Push result: {ok}");
        }

        public void GitCommitAll()
        {
            var msg = $"Update from SimHub at {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            PluginLogger.Info($"Commit requested: '{msg}'");
            bool ok;
            var selected = _settings?.SelectedDashboards ?? new List<string>();
            if (selected.Count > 0)
            {
                PluginLogger.Info($"Committing selected dashboards: {string.Join(", ", selected)}");
                ok = _sync?.CommitSelected(msg, selected) ?? false;
            }
            else
            {
                ok = _sync?.CommitAll(msg) ?? false;
            }
            PluginLogger.Info($"Commit result: {ok}");
        }

        public void SaveSettings(string remoteUrl, string branch, bool autoPullOnStart)
        {
            SaveSettings(remoteUrl, branch, autoPullOnStart, null, null, null, null);
        }

        public void SaveSettings(string remoteUrl, string branch, bool autoPullOnStart, string dashboardsPath, IEnumerable<string> selectedDashboards, string gitUser, string gitToken)
        {
            PluginLogger.Info($"SaveSettings called. remoteUrl='{remoteUrl}', branch='{branch}', autoPullOnStart={autoPullOnStart}, dashboardsPath='{dashboardsPath}'");
            if (_settings == null) _settings = new PluginSettings();
            _settings.RemoteUrl = remoteUrl ?? string.Empty;
            _settings.Branch = string.IsNullOrWhiteSpace(branch) ? "master" : branch.Trim();
            _settings.AutoPullOnStart = autoPullOnStart;
            if (!string.IsNullOrWhiteSpace(dashboardsPath))
            {
                _settings.DashboardsPath = dashboardsPath.Trim();
                _dashboardsPath = _settings.DashboardsPath;
                WarnIfProgramFiles(_dashboardsPath);
                _sync = new DashboardSyncService(_dashboardsPath);
                _sync.EnsureGitInitialized();
            }
            if (selectedDashboards != null)
            {
                _settings.SelectedDashboards = selectedDashboards.ToList();
            }
            if (gitUser != null) _settings.GitUsername = gitUser;
            if (gitToken != null) _settings.GitToken = gitToken;
            _settings.Save();

            try
            {
                if (!string.IsNullOrWhiteSpace(_settings.RemoteUrl))
                {
                    var setRemote = _sync?.SetRemote("origin", _settings.RemoteUrl) ?? false;
                    PluginLogger.Info($"After save: Set remote origin: {setRemote}");
                }

                var checkout = _sync?.CheckoutOrCreateBranch(_settings.Branch) ?? false;
                PluginLogger.Info($"After save: Checkout/Create branch '{_settings.Branch}': {checkout}");
            }
            catch (Exception ex)
            {
                PluginLogger.Error("Error while applying settings", ex);
            }
        }

        // For future use: Apply selection filter (not yet wired to git operations as repo is whole-folder)
        internal IEnumerable<string> GetSelectedDashboardFiles()
        {
            try
            {
                var path = _dashboardsPath;
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return new string[0];
                if (_settings?.SelectedDashboards == null || _settings.SelectedDashboards.Count == 0)
                {
                    return Directory.GetFiles(path, "*.json");
                }
                return _settings.SelectedDashboards
                    .Select(name => Path.Combine(path, name))
                    .Where(File.Exists)
                    .ToArray();
            }
            catch
            {
                return new string[0];
            }
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
                            PluginLogger.Info($"Action registered: {name}");
                            return;
                        }
                        catch (Exception ex)
                        {
                            PluginLogger.Error($"Failed to register action: {name}", ex);
                        }
                    }
                }
            }
        }
    }
}
