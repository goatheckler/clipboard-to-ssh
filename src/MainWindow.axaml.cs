using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using ClipboardToSsh.Models;
using ClipboardToSsh.Services;
using ClipboardToSsh.ViewModels;

namespace ClipboardToSsh;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;
    private ClipboardMonitor? _clipboardMonitor;
    private string _lastCopiedFilename = "";

    public MainWindow()
    {
        InitializeComponent();
        AfterInitialize();
    }

    private void AfterInitialize()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard != null)
        {
            _viewModel = new MainWindowViewModel(topLevel.Clipboard);
            DataContext = _viewModel;

            _clipboardMonitor = new ClipboardMonitor(topLevel.Clipboard);
            _clipboardMonitor.ClipboardChanged += OnClipboardChanged;
            _clipboardMonitor.StartMonitoring(250);

            LoadHosts();
        }
        _isInitialized = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _clipboardMonitor?.StopMonitoring();
    }

    private void LoadHosts()
    {
        var sshConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ssh",
            "config"
        );

        var sshConfigService = new SshConfigService();
        var hosts = sshConfigService.ParseHosts(sshConfigPath);

        HostComboBox.ItemsSource = hosts;
        if (hosts.Count > 0)
        {
            HostComboBox.SelectedItem = hosts[0];
        }
    }

    private void RefreshHosts_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        LoadHosts();
        var count = HostComboBox.ItemsSource?.Cast<object>().Count() ?? 0;
        StatusText.Text = $"Loaded {count} hosts";
    }

    private void PollingToggle_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (PollingToggle.IsChecked == true)
        {
            _clipboardMonitor?.StopMonitoring();
            PollingToggle.Content = "Paused";
            StatusText.Text = "Clipboard polling paused";
        }
        else
        {
            _clipboardMonitor?.StartMonitoring(250);
            PollingToggle.Content = "Monitoring";
            StatusText.Text = "Clipboard polling active";
            _viewModel?.GoToLatestCommand.Execute(null);
            UpdateDisplayForCurrentEntry();
        }
    }

    private void ImageOnlyToggle_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ImageOnlyToggle.IsChecked == true)
        {
            ContentText.IsVisible = false;
            ContentImage.IsVisible = false;
            ContentText.Text = "";
            _viewModel!.ImageOnlyMode = true;
            ImageOnlyToggle.Content = "Images Only";
            StatusText.Text = "Image-only mode enabled";
        }
        else
        {
            _viewModel!.ImageOnlyMode = false;
            ImageOnlyToggle.Content = "All Content";
            StatusText.Text = "Image-only mode disabled";
            _clipboardMonitor?.StopMonitoring();
            _clipboardMonitor?.StartMonitoring(250);
        }
    }

    private void HistoryBack_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        _viewModel.NavigateBackCommand.Execute(null);
        UpdateDisplayForCurrentEntry();

        if (_viewModel.CurrentPosition >= 0)
        {
            _clipboardMonitor?.StopMonitoring();
            PollingToggle.IsChecked = true;
            PollingToggle.Content = "Paused";
        }
    }

    private void HistoryForward_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        _viewModel.NavigateForwardCommand.Execute(null);
        UpdateDisplayForCurrentEntry();

        if (_viewModel.CurrentPosition < 0)
        {
            _clipboardMonitor?.StartMonitoring(250);
            PollingToggle.IsChecked = false;
            PollingToggle.Content = "Monitoring";
        }
    }

    private void UpdateDisplayForCurrentEntry()
    {
        if (_viewModel == null) return;

        HistoryPositionText.Text = _viewModel.HistoryPositionText;
        UpdateHistoryButtonStates();

        var entry = _viewModel.GetCurrentEntry();
        if (entry == null) return;

        if (entry.Content.Type == ClipboardContentType.Text)
        {
            ContentText.Text = entry.Content.Text;
            ContentText.IsVisible = true;
            ContentImage.IsVisible = false;
        }
        else
        {
            ContentText.IsVisible = false;
            ContentImage.IsVisible = true;
            if (entry.Content.ImageData != null)
            {
                using var ms = new MemoryStream(entry.Content.ImageData);
                ContentImage.Source = new Avalonia.Media.Imaging.Bitmap(ms);
            }
        }

        UpdateFilenameDisplay();
    }

    private void UpdateFilenameDisplay()
    {
        bool isLocal = PathTypeComboBox.SelectedIndex == 1;
        var entry = _viewModel?.GetCurrentEntry();

        if (entry == null)
        {
            FilenameText.Text = "";
            return;
        }

        if (isLocal)
        {
            FilenameText.Text = entry.LocalFilename;
        }
        else
        {
            FilenameText.Text = entry.RemoteFilename;
        }
    }

    private void PathTypeComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized)
            return;

        UpdateFilenameDisplay();

        bool isLocal = PathTypeComboBox.SelectedIndex == 1;
        PersistButton.Content = isLocal ? "Save Local" : "Transfer";
    }

    private void OnClipboardChanged(object? sender, ClipboardContent content)
    {
        Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
        {
            if (ImageOnlyToggle?.IsChecked == true && content.Type == ClipboardContentType.Text)
            {
                return;
            }

            if (content.Type == ClipboardContentType.Text)
            {
                var text = content.Text ?? "";
                var lastCopy = _lastCopiedFilename ?? "";
                if (!string.IsNullOrEmpty(lastCopy) &&
                    (text == lastCopy ||
                     text.EndsWith("/" + lastCopy) ||
                     text.EndsWith("\\" + lastCopy)))
                {
                    _lastCopiedFilename = "";
                    return;
                }
            }

            _viewModel?.AddEntry(content);
            UpdateDisplayForCurrentEntry();
        });
    }

    private async void PersistButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel?.GetCurrentEntry() == null)
        {
            StatusText.Text = "No content to persist";
            return;
        }

        bool isLocal = PathTypeComboBox.SelectedIndex == 1;
        if (isLocal)
        {
            await SaveLocalAsync();
        }
        else
        {
            await TransferAsync();
        }
    }

    private async Task TransferAsync()
    {
        if (HostComboBox.SelectedItem is not SshHost selectedHost)
        {
            StatusText.Text = "Please select a host";
            return;
        }

        try
        {
            StatusText.Text = "Transferring...";
            PersistButton.IsEnabled = false;

            var (filename, data) = _viewModel!.GetPersistData();
            var remotePath = await new SftpService().UploadAsync(selectedHost, filename, data);

            StatusText.Text = $"Uploaded to {remotePath}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            PersistButton.IsEnabled = true;
        }
    }

    private async Task SaveLocalAsync()
    {
        try
        {
            StatusText.Text = "Saving locally...";
            PersistButton.IsEnabled = false;

            var entry = _viewModel!.GetCurrentEntry();
            if (entry == null)
            {
                StatusText.Text = "No content to save";
                return;
            }

            if (entry.Content.ImageData != null)
            {
                File.WriteAllBytes(entry.LocalFilename, entry.Content.ImageData);
            }
            else if (entry.Content.Text != null)
            {
                File.WriteAllText(entry.LocalFilename, entry.Content.Text);
            }

            StatusText.Text = $"Saved to {entry.LocalFilename}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            PersistButton.IsEnabled = true;
        }
    }

    private async void CopyButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var entry = _viewModel?.GetCurrentEntry();
        if (entry == null)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        var clipboard = topLevel?.Clipboard;
        if (clipboard == null)
        {
            StatusText.Text = "Clipboard unavailable";
            return;
        }

        bool isLocal = PathTypeComboBox.SelectedIndex == 1;
        var pathToCopy = isLocal ? entry.LocalFilename : entry.RemoteFilename;
        var filenameToTrack = isLocal
            ? Path.GetFileName(entry.LocalFilename)
            : Path.GetFileName(entry.RemoteFilename);

        for (int i = 0; i < 3; i++)
        {
            try
            {
                await clipboard.SetTextAsync(pathToCopy);
                _lastCopiedFilename = filenameToTrack;
                StatusText.Text = "Path copied";
                return;
            }
            catch
            {
                await Task.Delay(50);
            }
        }

        StatusText.Text = "Copy failed - try again";
    }

    private void UpdateHistoryButtonStates()
    {
        if (_viewModel == null) return;

        HistoryBackButton.IsEnabled = _viewModel.CanNavigateBack;
        HistoryForwardButton.IsEnabled = _viewModel.CanNavigateForward;
    }

    private bool _isInitialized = false;

    private void InitializeAfterLoad()
    {
        _isInitialized = true;
    }
}