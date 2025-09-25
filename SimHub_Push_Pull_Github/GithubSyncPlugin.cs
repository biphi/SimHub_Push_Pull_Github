using SimHub.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SimHub_Push_Pull_Github
{
    public partial class GithubSyncPlugin : IPlugin
    {
        private static Version _assemblyVersion;
        private static string _computedVersion;
        private static string _repoOwner = "mzluzifer"; // falls Fork bitte anpassen
        private static string _repoName = "SimHub_Push_Pull_Github";

        public string Name => $"GitHub Dashboard Sync v{_computedVersion}";
        public string Author => "GitHub Copilot";
        public PluginManager PluginManager { get; set; }

        private static void ComputeBaseVersion()
        {
            if (_assemblyVersion != null) return;
            try
            {
                var asm = typeof(GithubSyncPlugin).Assembly;
                _assemblyVersion = asm.GetName().Version ?? new Version(1, 0, 0, 0);
            }
            catch { _assemblyVersion = new Version(1, 0, 0, 0); }
        }

        private DashboardSyncService _sync;
        private string _dashboardsPath;
        private PluginSettings _settings;

        private static readonly Regex VersionPattern = new Regex("^v?(\\d+\\.\\d+\\.\\d+(\\.\\d+)?)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static string TryGetTagVersionFromEnvironment()
        {
            foreach (var key in new[] { "GITHUB_REF_NAME", "SIMHUB_RELEASE_TAG" })
            {
                var val = Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrWhiteSpace(val) && VersionPattern.IsMatch(val.Trim()))
                {
                    var t = val.Trim();
                    if (t.StartsWith("v")) t = t.Substring(1);
                    return t;
                }
            }
            return null;
        }

        private static string TryGetTagVersionFromSideFile()
        {
            try
            {
                var asmPath = typeof(GithubSyncPlugin).Assembly.Location;
                var dir = Path.GetDirectoryName(asmPath);
                if (string.IsNullOrEmpty(dir)) return null;
                var vf = Path.Combine(dir, "plugin.version");
                if (!File.Exists(vf)) return null;
                var line = File.ReadAllLines(vf).FirstOrDefault()?.Trim();
                if (string.IsNullOrWhiteSpace(line)) return null;
                if (VersionPattern.IsMatch(line))
                {
                    if (line.StartsWith("v")) line = line.Substring(1);
                    return line;
                }
            }
            catch { }
            return null;
        }

        private static string TryGetTagVersionFromInformational()
        {
            try
            {
                var attr = typeof(GithubSyncPlugin).Assembly
                    .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                    .OfType<AssemblyInformationalVersionAttribute>()
                    .FirstOrDefault();
                var info = attr?.InformationalVersion;
                if (string.IsNullOrWhiteSpace(info)) return null;
                // extract first version-like token
                var m = VersionPattern.Match(info.Trim());
                if (m.Success)
                {
                    var v = m.Groups[1].Value;
                    if (v.StartsWith("v")) v = v.Substring(1);
                    return v;
                }
            }
            catch { }
            return null;
        }

        private static string ResolveEffectiveVersion()
        {
            // Priority: Env > side file > informational > assembly
            var tagEnv = TryGetTagVersionFromEnvironment();
            if (!string.IsNullOrWhiteSpace(tagEnv)) return tagEnv;
            var side = TryGetTagVersionFromSideFile();
            if (!string.IsNullOrWhiteSpace(side)) return side;
            var info = TryGetTagVersionFromInformational();
            if (!string.IsNullOrWhiteSpace(info)) return info;
            return _assemblyVersion.ToString();
        }

        public void Init(PluginManager pluginManager)
        {
            ComputeBaseVersion();
            PluginManager = pluginManager;
            NativeResolver.RegisterAssemblyResolver();
            NativeResolver.EnsureLibGit2SharpNativeOnPath();
            _settings = PluginSettings.Load();

            _computedVersion = ResolveEffectiveVersion();

            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var defaultPath = Path.Combine(docs, "SimHub", "Dashboards");
            _dashboardsPath = string.IsNullOrWhiteSpace(_settings.DashboardsPath) ? defaultPath : _settings.DashboardsPath;
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimHub", "Plugins", "GitHubDashboardSync");
            PluginLogger.Initialize(logDir);
            PluginLogger.Info($"Initializing plugin version v{_computedVersion}. Dashboards path: {_dashboardsPath}");
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
            TryRegisterAction(pluginManager, "Git Pull", "Pull from remote origin", (Action)GitPull);
            TryRegisterAction(pluginManager, "Git Push", "Push to remote origin", (Action)GitPush);
            TryRegisterAction(pluginManager, "Git Commit All", "Commit all changes", (Action)GitCommitAll);
            TryRegisterAction(pluginManager, "Git Auto Tag", "Create and push automatic version tag", (Action)GitTagAuto);

            // Fire and forget update check
            Task.Run(CheckForUpdateAsync);
        }

        private async Task CheckForUpdateAsync()
        {
            try
            {
                var current = _computedVersion;
                using (var http = new HttpClient())
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("SimHubGithubSync/" + current);
                    var url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases/latest";
                    var json = await http.GetStringAsync(url).ConfigureAwait(false);
                    // crude parse for tag_name
                    var tagMatch = Regex.Match(json, "\\\"tag_name\\\"\\s*:\\s*\\\"(?<tag>[^\\\"]+)\\\"", RegexOptions.IgnoreCase);
                    if (tagMatch.Success)
                    {
                        var tag = tagMatch.Groups["tag"].Value.Trim();
                        if (tag.StartsWith("v")) tag = tag.Substring(1);
                        if (VersionPattern.IsMatch(tag))
                        {
                            if (TryParseVersion(tag, out var latest) && TryParseVersion(current, out var cur) && latest > cur)
                            {
                                PluginLogger.Warn($"A newer plugin version v{latest} is available (current v{cur}).");
                            }
                            else
                            {
                                PluginLogger.Info($"You are on the latest version (v{current}).");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLogger.Debug("Update check failed: " + ex.Message);
            }
        }

        private static bool TryParseVersion(string s, out Version v)
        {
            try { v = new Version(s); return true; } catch { v = null; return false; }
        }

        private void WarnIfProgramFiles(string path)
        {
            try
            {
                var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                var pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!string.IsNullOrWhiteSpace(path) && (path.StartsWith(pf, StringComparison.OrdinalIgnoreCase) || path.StartsWith(pf64, StringComparison.OrdinalIgnoreCase)))
                {
                    PluginLogger.Warn($"The selected dashboards path is under Program Files: '{path}'. Write access may be restricted. Consider copying dashboards to '{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SimHub", "Dashboards")}' and selecting that folder in settings.");
                }
            }
            catch { }
        }

        public void End(PluginManager pluginManager) { PluginLogger.Info("Plugin End called"); }
        public void GitPull() { var branch = string.IsNullOrWhiteSpace(_settings?.Branch) ? "master" : _settings.Branch; PluginLogger.Info($"Pull requested for origin/{branch}"); var ok = _sync?.Pull("origin", branch) ?? false; PluginLogger.Info($"Pull result: {ok}"); }
        public void GitPush() { var branch = string.IsNullOrWhiteSpace(_settings?.Branch) ? "master" : _settings.Branch; PluginLogger.Info($"Push requested for origin/{branch}"); var ok = _sync?.Push("origin", branch) ?? false; PluginLogger.Info($"Push result: {ok}"); }
        public void GitCommitAll() { var msg = $"Update from SimHub at {DateTime.Now:yyyy-MM-dd HH:mm:ss}"; PluginLogger.Info($"Commit requested: '{msg}'"); bool ok; var selected = _settings?.SelectedDashboards ?? new List<string>(); if (selected.Count > 0) { PluginLogger.Info($"Committing selected dashboards: {string.Join(", ", selected)}"); ok = _sync?.CommitSelected(msg, selected) ?? false; } else { ok = _sync?.CommitAll(msg) ?? false; } PluginLogger.Info($"Commit result: {ok}"); }
        public void GitTagAuto() { try { PluginLogger.Info("Auto tag requested"); var tag = _sync?.CreateTagAndPush("v"); if (string.IsNullOrEmpty(tag)) PluginLogger.Warn("Auto tag creation failed"); else PluginLogger.Info($"Created and pushed tag {tag}"); } catch (Exception ex) { PluginLogger.Error("GitTagAuto failed", ex); } }

        public void SaveSettings(string remoteUrl, string branch, bool autoPullOnStart) => SaveSettings(remoteUrl, branch, autoPullOnStart, null, null, null, null);
        public void SaveSettings(string remoteUrl, string branch, bool autoPullOnStart, string dashboardsPath, IEnumerable<string> selectedDashboards, string gitUser, string gitToken)
        {
            PluginLogger.Info($"SaveSettings called. remoteUrl='{remoteUrl}', branch='{branch}', autoPullOnStart={autoPullOnStart}, dashboardsPath='{dashboardsPath}'");
            if (_settings == null) _settings = new PluginSettings();
            _settings.RemoteUrl = remoteUrl ?? string.Empty;
            _settings.Branch = string.IsNullOrWhiteSpace(branch) ? "master" : branch.Trim();
            _settings.AutoPullOnStart = autoPullOnStart;
            if (!string.IsNullOrWhiteSpace(dashboardsPath)) { _settings.DashboardsPath = dashboardsPath.Trim(); _dashboardsPath = _settings.DashboardsPath; WarnIfProgramFiles(_dashboardsPath); _sync = new DashboardSyncService(_dashboardsPath); _sync.EnsureGitInitialized(); }
            if (selectedDashboards != null) { _settings.SelectedDashboards = selectedDashboards.ToList(); }
            if (gitUser != null) _settings.GitUsername = gitUser; if (gitToken != null) _settings.GitToken = gitToken; _settings.Save();
            try
            {
                if (!string.IsNullOrWhiteSpace(_settings.RemoteUrl)) { var setRemote = _sync?.SetRemote("origin", _settings.RemoteUrl) ?? false; PluginLogger.Info($"After save: Set remote origin: {setRemote}"); }
                var checkout = _sync?.CheckoutOrCreateBranch(_settings.Branch) ?? false; PluginLogger.Info($"After save: Checkout/Create branch '{_settings.Branch}': {checkout}");
            }
            catch (Exception ex) { PluginLogger.Error("Error while applying settings", ex); }
        }

        internal IEnumerable<string> GetSelectedDashboardFiles()
        {
            try
            {
                var path = _dashboardsPath;
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return new string[0];
                if (_settings?.SelectedDashboards == null || _settings.SelectedDashboards.Count == 0) return Directory.GetFiles(path, "*.json");
                return _settings.SelectedDashboards.Select(name => Path.Combine(path, name)).Where(File.Exists).ToArray();
            }
            catch { return new string[0]; }
        }

        private void TryRegisterAction(PluginManager pm, string name, string description, Action action)
        {
            if (pm == null || action == null) return; var pmType = pm.GetType(); object actionsObj = null; foreach (var propName in new[] { "ActionsManager", "ActionManager", "Actions" }) { var prop = pmType.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance); if (prop != null) { actionsObj = prop.GetValue(pm); if (actionsObj != null) break; } }
            if (actionsObj == null) actionsObj = pm; var actType = actionsObj.GetType(); foreach (var methodName in new[] { "RegisterAction", "AddAction" }) { var methods = actType.GetMethods(BindingFlags.Public | BindingFlags.Instance); foreach (var m in methods) { if (m.Name != methodName) continue; var ps = m.GetParameters(); if (ps.Length >= 2 && ps[ps.Length - 1].ParameterType == typeof(Action)) { try { object[] args; if (ps.Length == 2) args = new object[] { name, action }; else if (ps.Length == 3) args = new object[] { name, description, action }; else if (ps.Length >= 4) args = new object[] { name, description, action, null }; else continue; m.Invoke(actionsObj, args); PluginLogger.Info($"Action registered: {name}"); return; } catch (Exception ex) { PluginLogger.Error($"Failed to register action: {name}", ex); } } } }
        }
    }
}
