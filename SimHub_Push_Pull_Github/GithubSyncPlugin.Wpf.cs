#if SIMHUB_WPF
using Microsoft.Win32;
using SimHub.Plugins;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects; // Für Schatteneffekt
namespace SimHub_Push_Pull_Github
{
    public partial class GithubSyncPlugin : IWPFSettingsV2
    {
        public string LeftMenuTitle => _settings?.RemoteUrl != null ? $"Git Sync" : typeof(GithubSyncPlugin).Name;
        public ImageSource PictureIcon => null;
        public Control GetWPFSettingsControl(PluginManager pluginManager) => new SettingsControl(this, _settings);

        private class SettingsControl : UserControl
        {
            private readonly GithubSyncPlugin _plugin;
            private readonly PluginSettings _settings;
            // UI + State Felder (Original wiederhergestellt)
            private TextBox _pathBox; private ListView _dashboardsList; private ListView _remoteList; private TextBox _logText; private CheckBox _autoScrollCheck; private FileSystemWatcher _logWatcher; private TextBox _localFilterBox; private TextBox _remoteFilterBox; private bool _initialDashboardsLoaded; private readonly ObservableCollection<DashboardRow> _localRows = new ObservableCollection<DashboardRow>(); private readonly ObservableCollection<DashboardRow> _remoteRows = new ObservableCollection<DashboardRow>(); private bool _isBusy; private TextBlock _statusText; private Panel _actionsButtonsPanel; private ProgressBar _progressBar; private TextBlock _remoteRepoLinkContainer; private Hyperlink _remoteRepoHyperlink;
            // Version UI
            private TextBlock _versionText; private Button _updateButton;

            // Farbpalette
            private static readonly Color PrimaryColor = Color.FromRgb(0x1E, 0x88, 0xE5); // Blau
            private static readonly Color PrimaryHover = Color.FromRgb(0x42, 0xA5, 0xF5);
            private static readonly Color DangerColor = Color.FromRgb(0xD3, 0x2F, 0x2F); // Rot
            private static readonly Color DangerHover = Color.FromRgb(0xE5, 0x57, 0x57);
            private static readonly Color AccentColor = Color.FromRgb(0x43, 0xA0, 0x47); // Grün
            private static readonly Color AccentHover = Color.FromRgb(0x66, 0xBB, 0x6A);
            private static readonly Color NeutralColor = Color.FromRgb(0x45, 0x45, 0x45);
            private static readonly Color NeutralHover = Color.FromRgb(0x60, 0x60, 0x60);

            private class DashboardRow : INotifyPropertyChanged { private bool _isChecked; public string Name { get; set; } public DateTime? LastWriteUtc { get; set; } public bool IsChecked { get { return _isChecked; } set { if (_isChecked != value) { _isChecked = value; OnPropertyChanged("IsChecked"); } } } public string DateDisplay { get { return LastWriteUtc.HasValue ? LastWriteUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "n/a"; } } public event PropertyChangedEventHandler PropertyChanged; private void OnPropertyChanged(string p) { var h = PropertyChanged; if (h != null) { try { h(this, new PropertyChangedEventArgs(p)); } catch { } } } }

            public SettingsControl(GithubSyncPlugin plugin, PluginSettings settings)
            {
                _plugin = plugin; _settings = settings ?? new PluginSettings();
                _plugin.VersionInfoChanged += OnVersionInfoChanged;
                BuildUi();
                UpdateVersionUi();
            }

            private void OnVersionInfoChanged() { try { Dispatcher.Invoke(UpdateVersionUi); } catch { } }
            private void UpdateVersionUi() { if (_versionText != null) _versionText.Text = "Version: v" + (_plugin?.CurrentVersion ?? "?"); if (_updateButton != null) { _updateButton.Visibility = (_plugin?.UpdateAvailable == true) ? Visibility.Visible : Visibility.Collapsed; _updateButton.ToolTip = _plugin?.UpdateAvailable == true ? ($"New version v{_plugin.LatestRemoteVersion} available") : string.Empty; } }
            private Button CreateUpdateButton() { var b = new Button { Content = "Update", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2), Background = new SolidColorBrush(DangerColor), Foreground = Brushes.White, Cursor = Cursors.Hand, Visibility = Visibility.Collapsed }; b.Click += (s, e) => { try { var url = _plugin?.LatestReleaseUrl ?? $"https://github.com/mzluzifer/SimHub_Push_Pull_Github/releases"; Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }; return b; }

            // Button Styling Helpers (Original)
            private Button CreateStyledButton(string text, RoutedEventHandler onClick, Color baseColor, Color hoverColor, string tooltip = null, bool bold = false, bool danger = false, double minWidth = 100)
            { var btn = new Button { Content = text, Margin = new Thickness(6, 4, 0, 4), Padding = new Thickness(14, 6, 14, 6), Background = new SolidColorBrush(baseColor), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Colors.WhiteSmoke), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, MinWidth = minWidth, ToolTip = tooltip, Effect = new DropShadowEffect { BlurRadius = 4, Direction = 270, ShadowDepth = 2, Opacity = 0.35, Color = Colors.Black } }; btn.Click += onClick; btn.MouseEnter += (s, e) => { ((Button)s).Background = new SolidColorBrush(hoverColor); }; btn.MouseLeave += (s, e) => { ((Button)s).Background = new SolidColorBrush(baseColor); }; btn.IsEnabledChanged += (s, e) => { var b = (Button)s; if (b.IsEnabled) { b.Opacity = 1.0; b.Background = new SolidColorBrush(baseColor); } else { b.Opacity = .55; b.Background = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90)); } }; return btn; }
            private Button Primary(string text, RoutedEventHandler h, string tip = null) => CreateStyledButton(text, h, PrimaryColor, PrimaryHover, tip, true);
            private Button Accent(string text, RoutedEventHandler h, string tip = null) => CreateStyledButton(text, h, AccentColor, AccentHover, tip, true);
            private Button Danger(string text, RoutedEventHandler h, string tip = null) => CreateStyledButton(text, h, DangerColor, DangerHover, tip, true);
            private Button Neutral(string text, RoutedEventHandler h, string tip = null) => CreateStyledButton(text, h, NeutralColor, NeutralHover, tip, false, false, 80);

            private void BuildUi()
            {
                var root = new DockPanel { LastChildFill = true };

                var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6, 8, 0) };
                _versionText = new TextBlock { FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
                _updateButton = CreateUpdateButton();
                headerPanel.Children.Add(_versionText); headerPanel.Children.Add(_updateButton);
                DockPanel.SetDock(headerPanel, Dock.Top);
                root.Children.Add(headerPanel);

                var tabs = new TabControl { Margin = new Thickness(8) };

                // === Original UI (leicht gekürzt) ===
                var gitStack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 4) };
                var gitGroup = new GroupBox { Header = "Git Settings", Margin = new Thickness(0, 0, 0, 8) };
                var gitPanel = new StackPanel { Orientation = Orientation.Vertical };
                var urlLabel = new TextBlock { Text = "Remote URL" };
                var urlBox = new TextBox { Text = _settings.RemoteUrl ?? string.Empty, MinWidth = 420 };
                var branchLabel = new TextBlock { Text = "Branch" };
                var branchBox = new TextBox { Text = _settings.Branch ?? "master", MinWidth = 180 };
                var autoPull = new CheckBox { Content = "Auto pull on start", IsChecked = _settings.AutoPullOnStart, Margin = new Thickness(0, 4, 0, 0) };
                var credPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                var userLabel = new TextBlock { Text = "Git Username", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                var userBox = new TextBox { Text = _settings.GitUsername ?? string.Empty, MinWidth = 180 };
                var tokenLabel = new TextBlock { Text = "Git Token/Password", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
                var tokenBox = new PasswordBox { Password = _settings.GitToken ?? string.Empty, MinWidth = 180 };
                credPanel.Children.Add(userLabel); credPanel.Children.Add(userBox); credPanel.Children.Add(tokenLabel); credPanel.Children.Add(tokenBox);
                var saveBtn = Primary("Save Settings", async (s, e) => { var selected = _localRows.Where(r => r.IsChecked).Select(r => r.Name).ToArray(); var url = urlBox.Text; var branch = branchBox.Text; var ap = autoPull.IsChecked == true; var path = _pathBox != null ? _pathBox.Text : null; var user = userBox.Text; var token = tokenBox.Password; await RunBackground("Saving settings", delegate { _plugin.SaveSettings(url, branch, ap, path, selected, user, token); }, false); UpdateRemoteRepoHyperlink(url); await ReloadListsAsync(url, branch); }, "Speichert URL, Branch und Zugangsdaten");
                gitPanel.Children.Add(urlLabel); gitPanel.Children.Add(urlBox); gitPanel.Children.Add(branchLabel); gitPanel.Children.Add(branchBox); gitPanel.Children.Add(autoPull); gitPanel.Children.Add(credPanel); gitPanel.Children.Add(saveBtn); gitGroup.Content = gitPanel; gitStack.Children.Add(gitGroup); var gitTab = new TabItem { Header = "Git-Settings", Content = new ScrollViewer { Content = gitStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };

                var dashStack = new StackPanel { Orientation = Orientation.Vertical };
                var localGroup = new GroupBox { Header = "Local Dashboards", Margin = new Thickness(0, 0, 0, 8) };
                var localPanel = new StackPanel { Orientation = Orientation.Vertical };
                localPanel.Children.Add(new TextBlock { Text = "Dashboards path" });
                var pathPanel = new StackPanel { Orientation = Orientation.Horizontal };
                _pathBox = new TextBox { Text = string.IsNullOrWhiteSpace(_settings.DashboardsPath) ? GetDefaultDashboardsPath() : _settings.DashboardsPath, MinWidth = 420 };
                var browseBtn = Neutral("Browse", async (s, e) => { if (!TrySelectFolderViaReflection()) { var ofd = new OpenFileDialog { CheckFileExists = true, Multiselect = false, Title = "Select any file inside the desired folder (fallback)", InitialDirectory = Directory.Exists(_pathBox.Text) ? _pathBox.Text : GetDefaultDashboardsPath(), Filter = "All files (*.*)|*.*" }; if (ofd.ShowDialog() == true) { var dir = Path.GetDirectoryName(ofd.FileName); if (!string.IsNullOrWhiteSpace(dir)) { _pathBox.Text = dir; _initialDashboardsLoaded = false; await RunBackground("Loading dashboards", LoadDashboardsListInternal, false); } } } }, "Ordner wählen");
                var openPathBtn = Neutral("Open", (s, e) => { try { if (Directory.Exists(_pathBox.Text)) Process.Start("explorer.exe", _pathBox.Text); } catch { } }, "Ordner im Explorer öffnen");
                pathPanel.Children.Add(_pathBox); pathPanel.Children.Add(browseBtn); pathPanel.Children.Add(openPathBtn); localPanel.Children.Add(pathPanel);
                var localHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                var refreshLocal = Primary("Refresh", async (s, e) => await RunBackground("Refreshing local", LoadDashboardsListInternal, false), "Lokale Liste neu laden");
                var selectAllLocal = Accent("All", (s, e) => SelectAllLocal(true), "Alle auswählen");
                var selectNoneLocal = Neutral("None", (s, e) => SelectAllLocal(false), "Keine auswählen");
                _localFilterBox = new TextBox { MinWidth = 200, Margin = new Thickness(12, 4, 0, 4), ToolTip = "Filter (name contains)", VerticalAlignment = VerticalAlignment.Center };
                _localFilterBox.TextChanged += async (s, e) => await RunBackground("Filtering", LoadDashboardsListInternal, false);
                localHeader.Children.Add(refreshLocal); localHeader.Children.Add(selectAllLocal); localHeader.Children.Add(selectNoneLocal); localHeader.Children.Add(new TextBlock { Text = "Filter:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) }); localHeader.Children.Add(_localFilterBox);
                localPanel.Children.Add(localHeader);
                _dashboardsList = CreateListView(); _dashboardsList.ItemsSource = _localRows; localPanel.Children.Add(_dashboardsList); localGroup.Content = localPanel; dashStack.Children.Add(localGroup);

                var remoteGroup = new GroupBox { Header = "Remote Dashboards (GitHub)", Margin = new Thickness(0, 0, 0, 8) }; var remotePanel = new StackPanel { Orientation = Orientation.Vertical };
                _remoteRepoLinkContainer = new TextBlock { Margin = new Thickness(0, 0, 0, 4) }; _remoteRepoHyperlink = new Hyperlink(new Run("<none>")); _remoteRepoHyperlink.RequestNavigate += (s, e) => { try { if (_remoteRepoHyperlink.NavigateUri != null) Process.Start(new ProcessStartInfo(_remoteRepoHyperlink.NavigateUri.AbsoluteUri) { UseShellExecute = true }); } catch { } e.Handled = true; }; _remoteRepoLinkContainer.Inlines.Add(new Run("Repository: ")); _remoteRepoLinkContainer.Inlines.Add(_remoteRepoHyperlink); remotePanel.Children.Add(_remoteRepoLinkContainer);
                var remoteHeader = new StackPanel { Orientation = Orientation.Horizontal };
                var refreshRemote = Primary("Refresh", async (s, e) => { var u = urlBox.Text; var b = branchBox.Text; await RunBackground("Refreshing remote", delegate { LoadRemoteListInternal(u, b); }, false); }, "Remote repos neu laden");
                var selectAllRemote = Accent("All", (s, e) => SelectAllRemote(true), "Alle auswählen");
                var selectNoneRemote = Neutral("None", (s, e) => SelectAllRemote(false), "Keine auswählen");
                _remoteFilterBox = new TextBox { MinWidth = 200, Margin = new Thickness(12, 4, 0, 4), ToolTip = "Filter (name contains)", VerticalAlignment = VerticalAlignment.Center };
                _remoteFilterBox.TextChanged += async (s, e) => { var u = urlBox.Text; var b = branchBox.Text; await RunBackground("Filtering remote", delegate { LoadRemoteListInternal(u, b); }, false); };
                remoteHeader.Children.Add(refreshRemote); remoteHeader.Children.Add(selectAllRemote); remoteHeader.Children.Add(selectNoneRemote); remoteHeader.Children.Add(new TextBlock { Text = "Filter:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) }); remoteHeader.Children.Add(_remoteFilterBox);
                remotePanel.Children.Add(remoteHeader); _remoteList = CreateListView(); _remoteList.ItemsSource = _remoteRows; remotePanel.Children.Add(_remoteList); remoteGroup.Content = remotePanel; dashStack.Children.Add(remoteGroup);
                urlBox.TextChanged += (s, e) => UpdateRemoteRepoHyperlink(urlBox.Text); UpdateRemoteRepoHyperlink(urlBox.Text);

                var actionsGroup = new GroupBox { Header = "Actions", Margin = new Thickness(0, 0, 0, 8) }; var buttonsPanel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) }; _actionsButtonsPanel = buttonsPanel; var pullBtn = Primary("Pull", async (s, e) => await RunBackground("Pull", _plugin.GitPull, false)); var pushBtn = Accent("Push", async (s, e) => await RunBackground("Push", _plugin.GitPush, false)); var commitBtn = Neutral("Commit", async (s, e) => await RunBackground("Commit", _plugin.GitCommitAll, false)); var downloadBtn = Primary("Download", async (s, e) => { var u = urlBox.Text; var b = branchBox.Text; await RunBackground("Download", delegate { DownloadSelectedInternal(u, b); }, false); }); buttonsPanel.Children.Add(pullBtn); buttonsPanel.Children.Add(pushBtn); buttonsPanel.Children.Add(commitBtn); buttonsPanel.Children.Add(downloadBtn); actionsGroup.Content = buttonsPanel; dashStack.Children.Add(actionsGroup);

                var statusPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) }; _statusText = new TextBlock { Text = "Ready", VerticalAlignment = VerticalAlignment.Center }; _progressBar = new ProgressBar { Width = 140, Height = 16, Margin = new Thickness(12, 0, 0, 0), Visibility = Visibility.Collapsed, IsIndeterminate = false }; statusPanel.Children.Add(_statusText); statusPanel.Children.Add(_progressBar); dashStack.Children.Add(statusPanel); var dashTab = new TabItem { Header = "Dashboards", Content = new ScrollViewer { Content = dashStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };

                var logsStack = new StackPanel { Orientation = Orientation.Vertical }; var logsGroup = new GroupBox { Header = "Logs" }; var logsPanel = new StackPanel { Orientation = Orientation.Vertical }; var logsHeader = new StackPanel { Orientation = Orientation.Horizontal }; var refreshLogs = Neutral("Refresh", (s, e) => LoadLogs(), "Logs neu laden"); var clearLogs = Danger("Clear", (s, e) => ClearLogs(), "Logdatei löschen"); _autoScrollCheck = new CheckBox { Content = "Auto-scroll", IsChecked = true, Margin = new Thickness(12, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center }; logsHeader.Children.Add(refreshLogs); logsHeader.Children.Add(clearLogs); logsHeader.Children.Add(_autoScrollCheck); logsPanel.Children.Add(logsHeader); _logText = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Visible, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, MinWidth = 620, Height = 520, FontFamily = new FontFamily("Consolas"), FontSize = 12 }; logsPanel.Children.Add(_logText); logsGroup.Content = logsPanel; logsStack.Children.Add(logsGroup); var logsTab = new TabItem { Header = "Logs", Content = new ScrollViewer { Content = logsStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };

                var supportStack = new StackPanel { Orientation = Orientation.Vertical }; supportStack.Children.Add(new TextBlock { Text = "Support & Contact", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) }); supportStack.Children.Add(new TextBlock { Text = "Join my Discord! If you have questions or comments let me know. Thanks in advance!", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) }); var linkText = new TextBlock(); linkText.Inlines.Add(new Run("Discord: ")); var discordLink = new Hyperlink(new Run("https://discord.gg/WSy8G4UhjC")) { NavigateUri = new Uri("https://discord.gg/WSy8G4UhjC") }; discordLink.RequestNavigate += (s, e) => { try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { } e.Handled = true; }; linkText.Inlines.Add(discordLink); supportStack.Children.Add(linkText); var linkTreeText = new TextBlock { Margin = new Thickness(0, 4, 0, 0) }; linkTreeText.Inlines.Add(new Run("Linktree: ")); var linkTree = new Hyperlink(new Run("https://linktr.ee/mzluzifer")) { NavigateUri = new Uri("https://linktr.ee/mzluzifer") }; linkTree.RequestNavigate += (s, e) => { try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { } e.Handled = true; }; linkTreeText.Inlines.Add(linkTree); supportStack.Children.Add(linkTreeText); supportStack.Children.Add(new TextBlock { Margin = new Thickness(0, 10, 0, 0), Text = "Thank you for your support! Greeting MZLuzifer ??", TextWrapping = TextWrapping.Wrap }); var supportTab = new TabItem { Header = "Support", Content = new ScrollViewer { Content = supportStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };

                tabs.Items.Add(dashTab); tabs.Items.Add(gitTab); tabs.Items.Add(logsTab); tabs.Items.Add(supportTab);
                root.Children.Add(tabs);
                Content = root;

                var initUrl = urlBox.Text; var initBranch = branchBox.Text; _ = RunBackground("Initial load", delegate { LoadDashboardsListInternal(); LoadRemoteListInternal(initUrl, initBranch); LoadLogs(); }, false); StartLogWatcher(); Unloaded += (s, e) => StopLogWatcher();
            }

            // Restliche Methoden unverändert (siehe ursprüngliche Version) – bereits unterhalb vorhanden.
            private void UpdateRemoteRepoHyperlink(string remoteUrl) { try { if (_remoteRepoHyperlink == null) return; if (string.IsNullOrWhiteSpace(remoteUrl)) { _remoteRepoHyperlink.NavigateUri = null; _remoteRepoHyperlink.Inlines.Clear(); _remoteRepoHyperlink.Inlines.Add(new Run("<none>")); return; } var url = NormalizeRemoteUrlToWeb(remoteUrl); _remoteRepoHyperlink.NavigateUri = new Uri(url); _remoteRepoHyperlink.Inlines.Clear(); _remoteRepoHyperlink.Inlines.Add(new Run(url)); } catch { } }
            private string NormalizeRemoteUrlToWeb(string remote) { try { if (string.IsNullOrWhiteSpace(remote)) return remote; var r = remote.Trim(); if (r.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase)) { r = r.Substring("git@github.com:".Length); if (r.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) r = r.Substring(0, r.Length - 4); return "https://github.com/" + r; } if (r.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || r.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) { if (r.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) r = r.Substring(0, r.Length - 4); return r; } if (r.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) r = r.Substring(0, r.Length - 4); return r; } catch { return remote; } }
            private void SetBusy(bool busy, string message) { _isBusy = busy; if (_statusText != null) _statusText.Text = message; if (_actionsButtonsPanel != null) foreach (UIElement child in _actionsButtonsPanel.Children) child.IsEnabled = !busy; if (_progressBar != null) { _progressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed; _progressBar.IsIndeterminate = busy; } }
            private async Task RunBackground(string label, Action action, bool reloadAfter) { if (_isBusy) return; SetBusy(true, label + " ..."); await Task.Run(delegate { try { action(); } catch (Exception ex) { PluginLogger.Error(label + " failed", ex); } }); if (reloadAfter) { try { LoadDashboardsListInternal(); } catch { } } SetBusy(false, "Ready"); }
            private async Task ReloadListsAsync(string remoteUrl, string branch) { await Task.Run(delegate { LoadDashboardsListInternal(); LoadRemoteListInternal(remoteUrl, branch); }); }
            private void LoadDashboardsListInternal() { string filter = null; string path = null; Application.Current.Dispatcher.Invoke(delegate { filter = _localFilterBox != null ? _localFilterBox.Text : null; path = _pathBox != null ? _pathBox.Text : null; }); var previousChecked = _localRows.Where(r => r.IsChecked).Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase); if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) { Application.Current.Dispatcher.Invoke(delegate { _localRows.Clear(); }); return; } string[] dirs; try { dirs = Directory.GetDirectories(path); } catch { return; } var newRows = dirs.Select(Path.GetFileName).Where(n => MatchesFilter(n, filter)).OrderBy(n => n).Select(n => new DashboardRow { Name = n, LastWriteUtc = GetLatestWriteTimeUtc(Path.Combine(path, n)), IsChecked = !_initialDashboardsLoaded ? (_settings.SelectedDashboards != null && _settings.SelectedDashboards.Contains(n, StringComparer.OrdinalIgnoreCase)) : ((_settings.SelectedDashboards != null && _settings.SelectedDashboards.Contains(n, StringComparer.OrdinalIgnoreCase)) || previousChecked.Contains(n)) }).ToList(); Application.Current.Dispatcher.Invoke(delegate { _localRows.Clear(); foreach (var r in newRows) _localRows.Add(r); if (!_initialDashboardsLoaded) _initialDashboardsLoaded = true; }); }
            private void LoadRemoteListInternal(string remoteUrl, string branch) { string filter = null; Application.Current.Dispatcher.Invoke(delegate { filter = _remoteFilterBox != null ? _remoteFilterBox.Text : null; }); var prev = _remoteRows.Where(r => r.IsChecked).Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase); if (string.IsNullOrWhiteSpace(remoteUrl)) { Application.Current.Dispatcher.Invoke(delegate { _remoteRows.Clear(); }); return; } var rows = new System.Collections.Generic.List<DashboardRow>(); try { var withDates = _plugin._sync != null ? _plugin._sync.ListRemoteDashboardsWithDates(remoteUrl, branch) : null; if (withDates != null) { foreach (var it in withDates) { if (!MatchesFilter(it.Name, filter)) continue; rows.Add(new DashboardRow { Name = it.Name, LastWriteUtc = it.LastWriteUtc, IsChecked = prev.Contains(it.Name) }); } } else { var plain = _plugin._sync != null ? _plugin._sync.ListRemoteDashboards(remoteUrl, branch) : Enumerable.Empty<string>(); foreach (var it in plain) { if (!MatchesFilter(it, filter)) continue; rows.Add(new DashboardRow { Name = it, LastWriteUtc = null, IsChecked = prev.Contains(it) }); } } } catch (Exception ex) { PluginLogger.Error("LoadRemoteList failed", ex); } Application.Current.Dispatcher.Invoke(delegate { _remoteRows.Clear(); foreach (var r in rows) _remoteRows.Add(r); }); }
            private void SelectAllLocal(bool check) { foreach (var r in _localRows) r.IsChecked = check; }
            private void SelectAllRemote(bool check) { foreach (var r in _remoteRows) r.IsChecked = check; }
            private void DownloadSelectedInternal(string remoteUrl, string branch) { string[] selected = new string[0]; string destRoot = string.Empty; Application.Current.Dispatcher.Invoke(delegate { selected = _remoteRows.Where(r => r.IsChecked).Select(r => r.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToArray(); destRoot = _pathBox.Text; }); var cacheRoot = Path.Combine(Path.GetTempPath(), "SimHub_GitCache"); var cacheDir = Path.Combine(cacheRoot, "repo"); Directory.CreateDirectory(cacheRoot); var mgr = new Git.GitRepositoryManager(cacheDir); if (!mgr.IsRepository()) { mgr.InitIfNeeded(); mgr.SetRemote("origin", remoteUrl); mgr.Fetch("origin"); mgr.CheckoutOrCreateBranch(string.IsNullOrWhiteSpace(branch) ? "master" : branch); } else { mgr.Fetch("origin"); mgr.CheckoutOrCreateBranch(string.IsNullOrWhiteSpace(branch) ? "master" : branch); } foreach (var name in selected) { var src = Path.Combine(cacheDir, name); var dst = Path.Combine(destRoot, name); if (!Directory.Exists(src)) { PluginLogger.Warn("Remote dashboard not found in cache: '" + name + "'"); continue; } PluginLogger.Info("Updating dashboard: '" + name + "'"); if (Directory.Exists(dst)) { try { Directory.Delete(dst, true); } catch (Exception delEx) { PluginLogger.Warn("Could not delete target folder: '" + dst + "': " + delEx.Message); } } try { CopyDirectory(src, dst); PluginLogger.Info("Dashboard updated: '" + name + "'"); } catch (Exception copyEx) { PluginLogger.Error("Error updating dashboard '" + name + "'", copyEx); } } _plugin.GitCommitAll(); LoadDashboardsListInternal(); LoadLogs(); }
            private bool TrySelectFolderViaReflection() { try { var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "System.Windows.Forms") ?? Assembly.Load("System.Windows.Forms"); var dlgType = asm.GetType("System.Windows.Forms.FolderBrowserDialog"); if (dlgType == null) return false; using (IDisposable dlg = (IDisposable)Activator.CreateInstance(dlgType)) { dlgType.GetProperty("Description").SetValue(dlg, "Select dashboards folder", null); dlgType.GetProperty("SelectedPath").SetValue(dlg, Directory.Exists(_pathBox.Text) ? _pathBox.Text : GetDefaultDashboardsPath(), null); dlgType.GetProperty("ShowNewFolderButton").SetValue(dlg, true, null); var result = dlgType.GetMethod("ShowDialog", Type.EmptyTypes).Invoke(dlg, null); if (result != null && result.ToString() == "OK") { var sel = dlgType.GetProperty("SelectedPath").GetValue(dlg, null) as string; if (!string.IsNullOrWhiteSpace(sel)) { _pathBox.Text = sel; _initialDashboardsLoaded = false; LoadDashboardsListInternal(); return true; } } } } catch (Exception ex) { PluginLogger.Error("Folder selection via reflection failed", ex); } return false; }
            private string GetLogDirectory() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimHub", "Plugins", "GitHubDashboardSync"); }
            private string GetLogFile() { return Path.Combine(GetLogDirectory(), "plugin.log"); }
            private void StartLogWatcher() { try { var dir = GetLogDirectory(); Directory.CreateDirectory(dir); _logWatcher = new FileSystemWatcher(dir, "plugin.log") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName }; _logWatcher.Changed += OnLogFileChanged; _logWatcher.Created += OnLogFileChanged; _logWatcher.Renamed += OnLogFileChanged; _logWatcher.EnableRaisingEvents = true; } catch (Exception ex) { PluginLogger.Error("StartLogWatcher failed", ex); } }
            private void StopLogWatcher() { try { if (_logWatcher != null) { _logWatcher.EnableRaisingEvents = false; _logWatcher.Changed -= OnLogFileChanged; _logWatcher.Created -= OnLogFileChanged; _logWatcher.Renamed -= OnLogFileChanged; _logWatcher.Dispose(); _logWatcher = null; } } catch { } }
            private void OnLogFileChanged(object sender, FileSystemEventArgs e) { try { Dispatcher.BeginInvoke(new Action(LoadLogs)); } catch { } }
            private bool IsScrolledToEnd() { if (!Dispatcher.CheckAccess()) { bool result = true; Application.Current.Dispatcher.Invoke(() => result = IsScrolledToEndUI()); return result; } return IsScrolledToEndUI(); }
            private bool IsScrolledToEndUI() { try { if (_logText == null) return true; int lastVisible = _logText.GetLastVisibleLineIndex(); int lastLine = _logText.LineCount - 1; return lastLine <= 0 || lastVisible >= lastLine; } catch { return true; } }
            private static string SafeReadAllText(string filePath, int maxLines) { try { using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (var sr = new StreamReader(fs)) { var content = sr.ReadToEnd(); if (maxLines <= 0) return content; var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None); if (lines.Length <= maxLines) return content; return string.Join(Environment.NewLine, lines.Skip(lines.Length - maxLines)); } } catch { return File.ReadAllText(filePath); } }
            private static DateTime? GetLatestWriteTimeUtc(string directory) { try { if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return null; var dirTime = Directory.GetLastWriteTimeUtc(directory); DateTime latest = dirTime; foreach (var f in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)) { try { var t = File.GetLastWriteTimeUtc(f); if (t > latest) latest = t; } catch { } } return latest; } catch { return null; } }
            private string GetDefaultDashboardsPath() { var dashTemplates = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "SimHub", "DashTemplates"); if (Directory.Exists(dashTemplates)) return dashTemplates; var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); return Path.Combine(docs, "SimHub", "Dashboards"); }
            private bool MatchesFilter(string name, string filter) { if (string.IsNullOrWhiteSpace(filter)) return true; try { return name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0; } catch { return true; } }
            private static void CopyDirectory(string sourceDir, string destDir) { Directory.CreateDirectory(destDir); foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories)) { var rel = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); var target = Path.Combine(destDir, rel); Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(file, target, true); } }
            private ListView CreateListView() { var lv = new ListView { MinHeight = 120, MinWidth = 400, Height = 300 }; var gv = new GridView(); var nameTemplate = new DataTemplate(); var spFactory = new FrameworkElementFactory(typeof(StackPanel)); spFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal); var cbFactory = new FrameworkElementFactory(typeof(CheckBox)); cbFactory.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding("IsChecked") { Mode = System.Windows.Data.BindingMode.TwoWay }); cbFactory.SetBinding(CheckBox.ContentProperty, new System.Windows.Data.Binding("Name")); spFactory.AppendChild(cbFactory); nameTemplate.VisualTree = spFactory; var col1 = new GridViewColumn { Header = "Name", Width = 260, CellTemplate = nameTemplate }; var col2 = new GridViewColumn { Header = "Last Modified", Width = 140, DisplayMemberBinding = new System.Windows.Data.Binding("DateDisplay") }; gv.Columns.Add(col1); gv.Columns.Add(col2); lv.View = gv; lv.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto); return lv; }
            private void LoadLogs() { try { string logFile = GetLogFile(); bool wasAtEnd = false; Application.Current.Dispatcher.Invoke(() => { wasAtEnd = IsScrolledToEndUI(); }); string text = File.Exists(logFile) ? SafeReadAllText(logFile, 4000) : "No logs yet."; Application.Current.Dispatcher.Invoke(() => { _logText.Text = text; if (_autoScrollCheck?.IsChecked == true || wasAtEnd) _logText.ScrollToEnd(); }); } catch (Exception ex) { try { Application.Current.Dispatcher.Invoke(() => _logText.Text = "Error loading logs: " + ex.Message); } catch { } } }
            private void ClearLogs()
            {
                try
                {
                    StopLogWatcher();
                    var logFile = GetLogFile();
                    if (File.Exists(logFile))
                    {
                        try { File.Delete(logFile); }
                        catch
                        {
                            try { File.WriteAllText(logFile, string.Empty); } catch { }
                        }
                    }
                    Application.Current.Dispatcher.Invoke(() => _logText.Text = "Logs cleared.");
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
        }
    }
}
#endif
