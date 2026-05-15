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
    private string _currentRemotePath = "";

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
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
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
        }
    }

    private void ImageOnlyToggle_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ImageOnlyToggle.IsChecked == true)
        {
            ContentText.IsVisible = false;
            ContentImage.IsVisible = false;
            ContentText.Text = "";
            _viewModel!.CurrentContent = null;
            StatusText.Text = "Image-only mode enabled";
        }
        else
        {
            StatusText.Text = "Image-only mode disabled";
            _clipboardMonitor?.StopMonitoring();
            _clipboardMonitor?.StartMonitoring(250);
        }
    }

    private async void Transfer_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (HostComboBox.SelectedItem is not SshHost selectedHost)
        {
            StatusText.Text = "Please select a host";
            return;
        }

        if (_viewModel?.CurrentContent == null)
        {
            StatusText.Text = "No content in clipboard";
            return;
        }

        try
        {
            StatusText.Text = "Transferring...";
            TransferButton.IsEnabled = false;

            if (string.IsNullOrEmpty(_currentRemotePath))
            {
                var sftpService = new SftpService();
                var (fname, fdata) = sftpService.GenerateFilenameAndData(_viewModel.CurrentContent);
                _currentRemotePath = $"/tmp/{fname}";
                FilenameText.Text = _currentRemotePath;
                await sftpService.UploadAsync(selectedHost, fname, fdata);
            }
            else
            {
                var remoteFilename = Path.GetFileName(_currentRemotePath);
                var fileData = _viewModel!.CurrentContent!.ImageData ??
                              System.Text.Encoding.UTF8.GetBytes(_viewModel.CurrentContent.Text ?? "");
                await new SftpService().UploadAsync(selectedHost, remoteFilename, fileData);
            }

            StatusText.Text = $"Uploaded to {_currentRemotePath}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            TransferButton.IsEnabled = true;
        }
    }

    private async void CopyFilename_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentRemotePath))
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        var clipboard = topLevel?.Clipboard;

        if (clipboard == null)
        {
            StatusText.Text = "Clipboard unavailable";
            return;
        }

        for (int i = 0; i < 3; i++)
        {
            try
            {
                await clipboard.SetTextAsync(_currentRemotePath);
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

    private void OnClipboardChanged(object? sender, ClipboardContent content)
    {
        Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
        {
            if (ImageOnlyToggle.IsChecked == true && content.Type == ClipboardContentType.Text)
            {
                return;
            }

            _viewModel!.CurrentContent = content;

            if (content.Type == ClipboardContentType.Text)
            {
                ContentText.Text = content.Text;
                ContentText.IsVisible = true;
                ContentImage.IsVisible = false;
            }
            else if (content.Type == ClipboardContentType.Image && content.ImageData != null)
            {
                ContentText.IsVisible = false;
                ContentImage.IsVisible = true;
                using var ms = new MemoryStream(content.ImageData);
                ContentImage.Source = new Avalonia.Media.Imaging.Bitmap(ms);
            }

            var sftpService = new SftpService();
            var (filename, _) = sftpService.GenerateFilenameAndData(content);
            _currentRemotePath = $"/tmp/{filename}";
            FilenameText.Text = _currentRemotePath;

            StatusText.Text = content.Type == ClipboardContentType.Image
                ? "Image detected in clipboard"
                : "Text detected in clipboard";
        });
    }

    private void SaveLocal_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel?.CurrentContent == null)
        {
            StatusText.Text = "No content in clipboard";
            return;
        }

        try
        {
            var filename = Path.GetFileName(_currentRemotePath);
            var localPath = Path.Combine("/tmp", filename);

            if (_viewModel.CurrentContent.ImageData != null)
            {
                File.WriteAllBytes(localPath, _viewModel.CurrentContent.ImageData);
            }
            else if (_viewModel.CurrentContent.Text != null)
            {
                File.WriteAllText(localPath, _viewModel.CurrentContent.Text);
            }

            StatusText.Text = $"Saved to {localPath}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }
}
