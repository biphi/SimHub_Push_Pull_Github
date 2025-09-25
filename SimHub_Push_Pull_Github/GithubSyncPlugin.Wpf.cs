#if SIMHUB_WPF
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media; // Added for ImageSource
using Microsoft.Win32;
using SimHub.Plugins;
using System.Diagnostics;
using System.Windows.Documents; // For Hyperlink
using System.Windows.Navigation; // For RequestNavigateEventArgs

namespace SimHub_Push_Pull_Github
{
    // WPF settings UI for SimHub (split to avoid hard dependency when not needed)
    public partial class GithubSyncPlugin : IWPFSettingsV2
    {
        public string LeftMenuTitle => Name;

        // IWPFSettingsV2.PictureIcon
        public ImageSource PictureIcon => null; // Or return a proper icon

        public Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            return new SettingsControl(this, _settings);
        }

        private class SettingsControl : UserControl
        {
            private readonly GithubSyncPlugin _plugin;
            private readonly PluginSettings _settings;
            private TextBox _pathBox;
            private ListBox _dashboardsList;
            private ListBox _remoteList;
            private TextBox _logText;
            private CheckBox _autoScrollCheck;
            private FileSystemWatcher _logWatcher;
            private TextBox _localFilterBox;
            private TextBox _remoteFilterBox;

            private class DashboardItem
            {
                public string Name { get; set; }
                public DateTime? LastWriteUtc { get; set; }
                public bool IsChecked { get; set; }
                public string GetDisplay()
                {
                    var part = LastWriteUtc.HasValue ? LastWriteUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "n/a";
                    return $"{Name} — {part}";
                }
                public override string ToString() => GetDisplay();
            }

            public SettingsControl(GithubSyncPlugin plugin, PluginSettings settings)
            {
                _plugin = plugin;
                _settings = settings ?? new PluginSettings();

                // Root TabControl
                var tabs = new TabControl { Margin = new Thickness(8) };

                // ================= Git Tab =================
                var gitStack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 4) };
                var gitGroup = new GroupBox { Header = "Git Settings", Margin = new Thickness(0, 0, 0, 8) };
                var gitPanel = new StackPanel { Orientation = Orientation.Vertical };
                var urlLabel = new TextBlock { Text = "Remote URL" };
                var urlBox = new TextBox { Text = _settings.RemoteUrl ?? string.Empty, MinWidth = 400, ToolTip = "HTTPS URL of the Git repository" };
                var branchLabel = new TextBlock { Text = "Branch" };
                var branchBox = new TextBox { Text = _settings.Branch ?? "master", MinWidth = 160, ToolTip = "Branch name (e.g., main)" };
                var autoPull = new CheckBox { Content = "Auto pull on start", IsChecked = _settings.AutoPullOnStart, ToolTip = "Automatically pull on plugin start" };
                var credPanel = new StackPanel { Orientation = Orientation.Horizontal };
                var userLabel = new TextBlock { Text = "Git Username", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                var userBox = new TextBox { Text = _settings.GitUsername ?? string.Empty, MinWidth = 200 };
                var tokenLabel = new TextBlock { Text = "Git Token/Password", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
                var tokenBox = new PasswordBox { Password = _settings.GitToken ?? string.Empty, MinWidth = 200, ToolTip = "Personal access token (GitHub) or password" };
                credPanel.Children.Add(userLabel);
                credPanel.Children.Add(userBox);
                credPanel.Children.Add(tokenLabel);
                credPanel.Children.Add(tokenBox);
                var saveBtn = new Button { Content = "Save Settings", Margin = new Thickness(0, 6, 0, 0) };
                saveBtn.Click += (s, e) =>
                {
                    var selected = _dashboardsList?.Items.Cast<object>()
                        .Select(i => i as CheckBox)
                        .Where(cb => cb != null && cb.IsChecked == true)
                        .Select(cb => (cb.Tag as DashboardItem)?.Name)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .ToArray() ?? new string[0];
                    _plugin.SaveSettings(urlBox.Text, branchBox.Text, autoPull.IsChecked == true, _pathBox?.Text, selected, userBox.Text, tokenBox.Password);
                    LoadDashboardsList();
                };
                gitPanel.Children.Add(urlLabel);
                gitPanel.Children.Add(urlBox);
                gitPanel.Children.Add(branchLabel);
                gitPanel.Children.Add(branchBox);
                gitPanel.Children.Add(autoPull);
                gitPanel.Children.Add(credPanel);
                gitPanel.Children.Add(saveBtn);
                gitGroup.Content = gitPanel;
                gitStack.Children.Add(gitGroup);
                var gitTab = new TabItem { Header = "Git" };
                gitTab.Content = new ScrollViewer { Content = gitStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                tabs.Items.Add(gitTab);

                // ================= Dashboards Tab =================
                var dashStack = new StackPanel { Orientation = Orientation.Vertical };

                // Local dashboards group
                var localGroup = new GroupBox { Header = "Local Dashboards", Margin = new Thickness(0, 0, 0, 8) };
                var localPanel = new StackPanel { Orientation = Orientation.Vertical };
                localPanel.Children.Add(new TextBlock { Text = "Dashboards path" });
                var pathPanel = new StackPanel { Orientation = Orientation.Horizontal };
                _pathBox = new TextBox { Text = string.IsNullOrWhiteSpace(_settings.DashboardsPath) ? GetDefaultDashboardsPath() : _settings.DashboardsPath, MinWidth = 400 };
                var browseBtn = new Button { Content = "Browse...", Margin = new Thickness(6, 0, 0, 0) };
                browseBtn.Click += (s, e) =>
                {
                    var ofd = new OpenFileDialog
                    {
                        CheckFileExists = true,
                        Multiselect = false,
                        Title = "Select folder (pick any file inside the desired folder)",
                        InitialDirectory = Directory.Exists(_pathBox.Text) ? _pathBox.Text : GetDefaultDashboardsPath(),
                        Filter = "All files (*.*)|*.*"
                    };
                    if (ofd.ShowDialog() == true)
                    {
                        var dir = Path.GetDirectoryName(ofd.FileName);
                        if (!string.IsNullOrWhiteSpace(dir))
                        {
                            _pathBox.Text = dir;
                            LoadDashboardsList();
                        }
                    }
                };
                var refreshPathBtn = new Button { Content = "Refresh", Margin = new Thickness(6, 0, 0, 0), ToolTip = "Reload local folder" };
                refreshPathBtn.Click += (s, e) => LoadDashboardsList();
                var openPathBtn = new Button { Content = "Open Folder", Margin = new Thickness(6, 0, 0, 0) };
                openPathBtn.Click += (s, e) =>
                {
                    try { if (Directory.Exists(_pathBox.Text)) Process.Start("explorer.exe", _pathBox.Text); } catch { }
                };
                pathPanel.Children.Add(_pathBox);
                pathPanel.Children.Add(browseBtn);
                pathPanel.Children.Add(refreshPathBtn);
                pathPanel.Children.Add(openPathBtn);
                localPanel.Children.Add(pathPanel);

                var localHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                var selectAllLocal = new Button { Content = "Select All" };
                selectAllLocal.Click += (s, e) => SelectAllLocal(true);
                var selectNoneLocal = new Button { Content = "Select None", Margin = new Thickness(6, 0, 0, 0) };
                selectNoneLocal.Click += (s, e) => SelectAllLocal(false);
                _localFilterBox = new TextBox { MinWidth = 200, Margin = new Thickness(12, 0, 0, 0), ToolTip = "Filter (name contains)" };
                _localFilterBox.TextChanged += (s, e) => LoadDashboardsList();
                localHeader.Children.Add(selectAllLocal);
                localHeader.Children.Add(selectNoneLocal);
                localHeader.Children.Add(new TextBlock { Text = "Filter:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) });
                localHeader.Children.Add(_localFilterBox);
                localPanel.Children.Add(localHeader);

                _dashboardsList = new ListBox { MinHeight = 120, MinWidth = 400, Height = 220 };
                _dashboardsList.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
                localPanel.Children.Add(_dashboardsList);
                localGroup.Content = localPanel;
                dashStack.Children.Add(localGroup);

                // Remote dashboards group
                var remoteGroup = new GroupBox { Header = "Remote Dashboards (GitHub)", Margin = new Thickness(0, 0, 0, 8) };
                var remotePanel = new StackPanel { Orientation = Orientation.Vertical };
                var remoteHeader = new StackPanel { Orientation = Orientation.Horizontal };
                var refreshRemote = new Button { Content = "Load Remote" };
                refreshRemote.Click += (s, e) => LoadRemoteList(urlBox.Text, branchBox.Text);
                var selectAllRemote = new Button { Content = "Select All", Margin = new Thickness(6, 0, 0, 0) };
                selectAllRemote.Click += (s, e) => SelectAllRemote(true);
                var selectNoneRemote = new Button { Content = "Select None", Margin = new Thickness(6, 0, 0, 0) };
                selectNoneRemote.Click += (s, e) => SelectAllRemote(false);
                _remoteFilterBox = new TextBox { MinWidth = 200, Margin = new Thickness(12, 0, 0, 0), ToolTip = "Filter (name contains)" };
                _remoteFilterBox.TextChanged += (s, e) => LoadRemoteList(urlBox.Text, branchBox.Text);
                remoteHeader.Children.Add(refreshRemote);
                remoteHeader.Children.Add(selectAllRemote);
                remoteHeader.Children.Add(selectNoneRemote);
                remoteHeader.Children.Add(new TextBlock { Text = "Filter:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) });
                remoteHeader.Children.Add(_remoteFilterBox);
                remotePanel.Children.Add(remoteHeader);

                _remoteList = new ListBox { MinHeight = 120, MinWidth = 400, Height = 220 };
                _remoteList.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
                remotePanel.Children.Add(_remoteList);
                remoteGroup.Content = remotePanel;
                dashStack.Children.Add(remoteGroup);

                // Actions group
                var actionsGroup = new GroupBox { Header = "Actions", Margin = new Thickness(0, 0, 0, 8) };
                var buttonsPanel = new StackPanel { Orientation = Orientation.Horizontal };
                var pullBtn = new Button { Content = "Pull" };
                var pushBtn = new Button { Content = "Push", Margin = new Thickness(6, 0, 0, 0) };
                var commitBtn = new Button { Content = "Commit All", Margin = new Thickness(6, 0, 0, 0) };
                var downloadBtn = new Button { Content = "Download Selected", Margin = new Thickness(6, 0, 0, 0) };
                pullBtn.Click += (s, e) => _plugin.GitPull();
                pushBtn.Click += (s, e) => _plugin.GitPush();
                commitBtn.Click += (s, e) => _plugin.GitCommitAll();
                downloadBtn.Click += (s, e) => DownloadSelected(urlBox.Text, branchBox.Text);
                buttonsPanel.Children.Add(pullBtn);
                buttonsPanel.Children.Add(pushBtn);
                buttonsPanel.Children.Add(commitBtn);
                buttonsPanel.Children.Add(downloadBtn);
                actionsGroup.Content = buttonsPanel;
                dashStack.Children.Add(actionsGroup);

                var dashTab = new TabItem { Header = "Dashboards" };
                dashTab.Content = new ScrollViewer { Content = dashStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                tabs.Items.Add(dashTab);

                // ================= Logs Tab =================
                var logsStack = new StackPanel { Orientation = Orientation.Vertical };
                var logsGroup = new GroupBox { Header = "Logs" };
                var logsPanel = new StackPanel { Orientation = Orientation.Vertical };
                var logsHeader = new StackPanel { Orientation = Orientation.Horizontal };
                var refreshLogs = new Button { Content = "Refresh" };
                refreshLogs.Click += (s, e) => LoadLogs();
                var clearLogs = new Button { Content = "Clear", Margin = new Thickness(6, 0, 0, 0), ToolTip = "Delete log file" };
                clearLogs.Click += (s, e) => ClearLogs();
                _autoScrollCheck = new CheckBox { Content = "Auto-scroll", IsChecked = true, Margin = new Thickness(8, 0, 0, 0) };
                logsHeader.Children.Add(refreshLogs);
                logsHeader.Children.Add(clearLogs);
                logsHeader.Children.Add(_autoScrollCheck);
                logsPanel.Children.Add(logsHeader);
                _logText = new TextBox
                {
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.NoWrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MinWidth = 600,
                    Height = 240
                };
                logsPanel.Children.Add(_logText);
                logsGroup.Content = logsPanel;
                logsStack.Children.Add(logsGroup);
                var logsTab = new TabItem { Header = "Logs" };
                logsTab.Content = new ScrollViewer { Content = logsStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                tabs.Items.Add(logsTab);

                // ================= Support Tab =================
                var supportStack = new StackPanel { Orientation = Orientation.Vertical };
                var supportHeader = new TextBlock { Text = "Support & Kontakt", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) };
                supportStack.Children.Add(supportHeader);
                var info = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6), Text = "Trete dem Discord bei, um Fragen zu stellen, Feedback zu geben oder Probleme zu melden." };
                supportStack.Children.Add(info);
                var linkText = new TextBlock();
                linkText.Inlines.Add(new Run("Discord: "));
                var discordLink = new Hyperlink(new Run("https://discord.gg/WSy8G4UhjC")) { NavigateUri = new Uri("https://discord.gg/WSy8G4UhjC") };
                discordLink.RequestNavigate += (s, e) =>
                {
                    try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
                    e.Handled = true;
                };
                linkText.Inlines.Add(discordLink);
                supportStack.Children.Add(linkText);
                var supportTab = new TabItem { Header = "Support" };
                supportTab.Content = new ScrollViewer { Content = supportStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                tabs.Items.Add(supportTab);

                this.Content = tabs;

                // Initial load
                LoadDashboardsList();
                LoadRemoteList(urlBox.Text, branchBox.Text);
                LoadLogs();
                StartLogWatcher();

                // Dispose watcher on unload
                this.Unloaded += (s, e) => StopLogWatcher();
            }

            private void SelectAllLocal(bool check)
            {
                try
                {
                    foreach (var obj in _dashboardsList.Items.Cast<object>())
                    {
                        var cb = obj as CheckBox;
                        if (cb != null) cb.IsChecked = check;
                    }
                }
                catch { }
            }

            private void SelectAllRemote(bool check)
            {
                try
                {
                    foreach (var obj in _remoteList.Items.Cast<object>())
                    {
                        var cb = obj as CheckBox;
                        if (cb != null) cb.IsChecked = check;
                    }
                }
                catch { }
            }

            private void DownloadSelected(string remoteUrl, string branch)
            {
                try
                {
                    var cacheRoot = Path.Combine(Path.GetTempPath(), "SimHub_GitCache");
                    var cacheDir = Path.Combine(cacheRoot, "repo");
                    Directory.CreateDirectory(cacheRoot);
                    var mgr = new Git.GitRepositoryManager(cacheDir);

                    if (!mgr.IsRepository())
                    {
                        mgr.InitIfNeeded();
                        mgr.SetRemote("origin", remoteUrl);
                        mgr.Fetch("origin");
                        mgr.CheckoutOrCreateBranch(string.IsNullOrWhiteSpace(branch) ? "master" : branch);
                    }
                    else
                    {
                        mgr.Fetch("origin");
                        mgr.CheckoutOrCreateBranch(string.IsNullOrWhiteSpace(branch) ? "master" : branch);
                    }

                    var selected = _remoteList.Items.Cast<object>()
                        .Select(i => i as CheckBox)
                        .Where(cb => cb != null && cb.IsChecked == true)
                        .Select(cb => (cb.Tag as DashboardItem)?.Name)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .ToArray();
                    var destRoot = _pathBox.Text;
                    foreach (var name in selected)
                    {
                        var src = Path.Combine(cacheDir, name);
                        var dst = Path.Combine(destRoot, name);

                        if (!Directory.Exists(src))
                        {
                            PluginLogger.Warn($"Remote dashboard not found in cache: '{name}' (src='{src}')");
                            continue;
                        }

                        PluginLogger.Info($"Updating dashboard: '{name}' (src='{src}', dst='{dst}')");

                        if (Directory.Exists(dst))
                        {
                            try
                            {
                                Directory.Delete(dst, true);
                                PluginLogger.Debug($"Deleted target folder: '{dst}'");
                            }
                            catch (Exception delEx)
                            {
                                PluginLogger.Warn($"Could not delete target folder: '{dst}': {delEx.Message}");
                            }
                        }
                        try
                        {
                            CopyDirectory(src, dst);
                            PluginLogger.Info($"Dashboard updated: '{name}'");
                        }
                        catch (Exception copyEx)
                        {
                            PluginLogger.Error($"Error updating dashboard '{name}'", copyEx);
                        }
                    }
                    _plugin.GitCommitAll();
                    LoadDashboardsList();
                    LoadLogs();
                }
                catch (Exception ex)
                {
                    PluginLogger.Error("DownloadSelected failed", ex);
                }
            }

            private static void CopyDirectory(string sourceDir, string destDir)
            {
                Directory.CreateDirectory(destDir);
                foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
                {
                    var rel = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var target = Path.Combine(destDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(file, target, true);
                }
            }

            private string GetDefaultDashboardsPath()
            {
                var dashTemplates = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "SimHub", "DashTemplates");
                if (Directory.Exists(dashTemplates)) return dashTemplates;
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                return System.IO.Path.Combine(docs, "SimHub", "Dashboards");
            }

            private bool MatchesFilter(string name, string filter)
            {
                if (string.IsNullOrWhiteSpace(filter)) return true;
                try { return name?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0; } catch { return true; }
            }

            private void LoadDashboardsList()
            {
                try
                {
                    var filter = _localFilterBox?.Text;
                    var previousChecked = _dashboardsList?.Items.Cast<object>()
                        .Select(i => i as CheckBox)
                        .Where(cb => cb != null && cb.IsChecked == true)
                        .Select(cb => (cb.Tag as DashboardItem)?.Name)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .ToList() ?? new System.Collections.Generic.List<string>();

                    _dashboardsList.Items.Clear();
                    var path = _pathBox.Text;
                    if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
                    var dirs = Directory.GetDirectories(path)
                        .Select(Path.GetFileName)
                        .OrderBy(n => n)
                        .ToArray();

                    foreach (var d in dirs)
                    {
                        if (!MatchesFilter(d, filter)) continue;
                        var full = Path.Combine(path, d);
                        var item = new DashboardItem
                        {
                            Name = d,
                            LastWriteUtc = GetLatestWriteTimeUtc(full),
                        };
                        item.IsChecked = (_settings?.SelectedDashboards?.Contains(d, StringComparer.OrdinalIgnoreCase) ?? false)
                 || previousChecked.Any(x => string.Equals(x, d, StringComparison.OrdinalIgnoreCase));

                        var cb = new CheckBox { Content = item.GetDisplay(), IsChecked = item.IsChecked, Tag = item, Margin = new Thickness(0, 2, 0, 2) };
                        _dashboardsList.Items.Add(cb);
                    }
                }
                catch (Exception ex)
                {
                    PluginLogger.Error("LoadDashboardsList failed", ex);
                }
            }

            private void LoadRemoteList(string remoteUrl, string branch)
            {
                try
                {
                    var filter = _remoteFilterBox?.Text;
                    var previousChecked = _remoteList?.Items.Cast<object>()
                        .Select(i => i as CheckBox)
                        .Where(cb => cb != null && cb.IsChecked == true)
                        .Select(cb => (cb.Tag as DashboardItem)?.Name)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .ToList() ?? new System.Collections.Generic.List<string>();

                    _remoteList.Items.Clear();
                    if (string.IsNullOrWhiteSpace(remoteUrl)) return;

                    var itemsWithDates = _plugin._sync?.ListRemoteDashboardsWithDates(remoteUrl, branch);
                    if (itemsWithDates != null)
                    {
                        foreach (var it in itemsWithDates)
                        {
                            if (!MatchesFilter(it.Name, filter)) continue;
                            var item = new DashboardItem { Name = it.Name, LastWriteUtc = it.LastWriteUtc };
                            item.IsChecked = previousChecked.Contains(it.Name, StringComparer.OrdinalIgnoreCase);
                            var cb = new CheckBox { Content = item.GetDisplay(), IsChecked = item.IsChecked, Tag = item, Margin = new Thickness(0, 2, 0, 2) };
                            _remoteList.Items.Add(cb);
                        }
                    }
                    else
                    {
                        var items = _plugin._sync?.ListRemoteDashboards(remoteUrl, branch) ?? Enumerable.Empty<string>();
                        foreach (var it in items)
                        {
                            if (!MatchesFilter(it, filter)) continue;
                            var item = new DashboardItem { Name = it, LastWriteUtc = null };
                            item.IsChecked = previousChecked.Contains(it, StringComparer.OrdinalIgnoreCase);
                            var cb = new CheckBox { Content = item.GetDisplay(), IsChecked = item.IsChecked, Tag = item, Margin = new Thickness(0, 2, 0, 2) };
                            _remoteList.Items.Add(cb);
                        }
                    }
                }
                catch (Exception ex)
                {
                    PluginLogger.Error("LoadRemoteList failed", ex);
                }
            }

            private string GetLogDirectory()
            {
                return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimHub", "Plugins", "GitHubDashboardSync");
            }

            private string GetLogFile()
            {
                return System.IO.Path.Combine(GetLogDirectory(), "plugin.log");
            }

            private void StartLogWatcher()
            {
                try
                {
                    var dir = GetLogDirectory();
                    Directory.CreateDirectory(dir);
                    _logWatcher = new FileSystemWatcher(dir, "plugin.log")
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
                    };
                    _logWatcher.Changed += OnLogFileChanged;
                    _logWatcher.Created += OnLogFileChanged;
                    _logWatcher.Renamed += OnLogFileChanged;
                    _logWatcher.EnableRaisingEvents = true;
                }
                catch (Exception ex)
                {
                    PluginLogger.Error("StartLogWatcher failed", ex);
                }
            }

            private void StopLogWatcher()
            {
                try
                {
                    if (_logWatcher != null)
                    {
                        _logWatcher.EnableRaisingEvents = false;
                        _logWatcher.Changed -= OnLogFileChanged;
                        _logWatcher.Created -= OnLogFileChanged;
                        _logWatcher.Renamed -= OnLogFileChanged;
                        _logWatcher.Dispose();
                        _logWatcher = null;
                    }
                }
                catch { }
            }

            private void OnLogFileChanged(object sender, FileSystemEventArgs e)
            {
                try
                {
                    // marshal to UI thread
                    Dispatcher.BeginInvoke(new Action(() => LoadLogs()));
                }
                catch { }
            }

            private void LoadLogs()
            {
                try
                {
                    var logFile = GetLogFile();
                    bool wasAtEnd = IsScrolledToEnd();
                    if (File.Exists(logFile))
                    {
                        _logText.Text = SafeReadAllText(logFile, 4000);
                        if (_autoScrollCheck?.IsChecked == true || wasAtEnd)
                        {
                            _logText.ScrollToEnd();
                        }
                    }
                    else
                    {
                        _logText.Text = "No logs yet.";
                    }
                }
                catch (Exception ex)
                {
                    _logText.Text = "Error loading logs: " + ex.Message;
                }
            }

            private void ClearLogs()
            {
                try
                {
                    StopLogWatcher();
                    var logFile = GetLogFile();
                    if (File.Exists(logFile))
                    {
                        try { File.Delete(logFile); } catch { File.WriteAllText(logFile, string.Empty); }
                    }
                    _logText.Text = "Logs cleared.";
                }
                catch (Exception ex)
                {
                    PluginLogger.Error("ClearLogs failed", ex);
                }
                finally
                {
                    StartLogWatcher();
                }
            }

            private bool IsScrolledToEnd()
            {
                try
                {
                    if (_logText == null) return true;
                    int lastVisible = _logText.GetLastVisibleLineIndex();
                    int lastLine = _logText.LineCount - 1;
                    return lastLine <= 0 || lastVisible >= lastLine;
                }
                catch { return true; }
            }

            private static string SafeReadAllText(string filePath, int maxLines)
            {
                try
                {
                    using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs))
                    {
                        var content = sr.ReadToEnd();
                        if (maxLines <= 0) return content;
                        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                        if (lines.Length <= maxLines) return content;
                        var tail = string.Join(Environment.NewLine, lines.Skip(lines.Length - maxLines));
                        return tail;
                    }
                }
                catch
                {
                    // fallback to regular read
                    return File.ReadAllText(filePath);
                }
            }

            private static DateTime? GetLatestWriteTimeUtc(string directory)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return null;
                    var dirInfoUtc = Directory.GetLastWriteTimeUtc(directory);
                    var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories);
                    DateTime latest = dirInfoUtc;
                    foreach (var f in files)
                    {
                        try
                        {
                            var t = File.GetLastWriteTimeUtc(f);
                            if (t > latest) latest = t;
                        }
                        catch { }
                    }
                    return latest;
                }
                catch { return null; }
            }
        }
    }
}
#endif
