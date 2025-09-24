#if SIMHUB_WPF
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media; // Hinzugefügt für ImageSource
using SimHub.Plugins;

namespace SimHub_Push_Pull_Github
{
    // WPF settings UI for SimHub (split to avoid hard dependency when not needed)
    public partial class GithubSyncPlugin : IWPFSettingsV2
    {
        public string LeftMenuTitle => Name;

        // Implementierung von IWPFSettingsV2.PictureIcon
        public ImageSource PictureIcon => null; // Oder ein passendes Icon zurückgeben

        public Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            return new SettingsControl(this, _settings);
        }

        private class SettingsControl : UserControl
        {
            private readonly GithubSyncPlugin _plugin;
            private readonly PluginSettings _settings;

            public SettingsControl(GithubSyncPlugin plugin, PluginSettings settings)
            {
                _plugin = plugin;
                _settings = settings ?? new PluginSettings();

                var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8) };

                var urlLabel = new TextBlock { Text = "Remote URL" };
                var urlBox = new TextBox { Text = _settings.RemoteUrl ?? string.Empty, MinWidth = 320 };
                panel.Children.Add(urlLabel);
                panel.Children.Add(urlBox);

                var branchLabel = new TextBlock { Text = "Branch" };
                var branchBox = new TextBox { Text = _settings.Branch ?? "main", MinWidth = 160 };
                panel.Children.Add(branchLabel);
                panel.Children.Add(branchBox);

                var autoPull = new CheckBox { Content = "Auto pull on start", IsChecked = _settings.AutoPullOnStart };
                panel.Children.Add(autoPull);

                var saveBtn = new Button { Content = "Save Settings" };
                saveBtn.Click += (s, e) =>
                {
                    _plugin.SaveSettings(urlBox.Text, branchBox.Text, autoPull.IsChecked == true);
                };
                panel.Children.Add(saveBtn);

                var buttonsPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                var pullBtn = new Button { Content = "Pull" };
                var pushBtn = new Button { Content = "Push" };
                var commitBtn = new Button { Content = "Commit All" };
                pullBtn.Click += (s, e) => _plugin.GitPull();
                pushBtn.Click += (s, e) => _plugin.GitPush();
                commitBtn.Click += (s, e) => _plugin.GitCommitAll();
                buttonsPanel.Children.Add(pullBtn);
                buttonsPanel.Children.Add(pushBtn);
                buttonsPanel.Children.Add(commitBtn);
                panel.Children.Add(buttonsPanel);

                this.Content = panel;
            }
        }
    }
}
#endif
