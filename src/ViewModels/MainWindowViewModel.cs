using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClipboardToSsh.Models;
using ClipboardToSsh.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipboardToSsh.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly SshConfigService _sshConfigService;
    private readonly ClipboardMonitor _clipboardMonitor;
    private readonly SftpService _sftpService;

    [ObservableProperty]
    private ObservableCollection<SshHost> _hosts = new();

    [ObservableProperty]
    private SshHost? _selectedHost;

    [ObservableProperty]
    private ClipboardContent? _currentContent;

    [ObservableProperty]
    private bool _isTransferring;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _currentFilename = "";

    [ObservableProperty]
    private byte[]? _currentImageData;

    public MainWindowViewModel()
    {
        _sshConfigService = new SshConfigService();
        _clipboardMonitor = new ClipboardMonitor();
        _sftpService = new SftpService();

        _clipboardMonitor.ClipboardChanged += OnClipboardChanged;
        LoadHosts();
    }

    public MainWindowViewModel(Avalonia.Input.Platform.IClipboard clipboard) : this()
    {
        _clipboardMonitor = new ClipboardMonitor(clipboard);
        _clipboardMonitor.ClipboardChanged += OnClipboardChanged;
    }

    public void StartClipboardMonitoring()
    {
        _clipboardMonitor.StartMonitoring(250);
    }

    public void StopClipboardMonitoring()
    {
        _clipboardMonitor.StopMonitoring();
    }

    private void LoadHosts()
    {
        var sshConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ssh",
            "config"
        );

        var hosts = _sshConfigService.ParseHosts(sshConfigPath);
        Hosts.Clear();
        foreach (var host in hosts)
        {
            Hosts.Add(host);
        }

        if (Hosts.Count > 0)
        {
            SelectedHost = Hosts[0];
        }
    }

    [RelayCommand]
    private void RefreshHosts()
    {
        LoadHosts();
        StatusMessage = $"Loaded {Hosts.Count} hosts from SSH config";
    }

    [RelayCommand]
    private async Task TransferAsync()
    {
        if (SelectedHost == null || CurrentContent == null)
            return;

        if (IsTransferring)
            return;

        try
        {
            IsTransferring = true;
            StatusMessage = "Transferring...";

            var (filename, data) = _sftpService.GenerateFilenameAndData(CurrentContent);
            CurrentFilename = filename;

            var remotePath = await _sftpService.UploadAsync(SelectedHost, filename, data);

            StatusMessage = $"Uploaded to {remotePath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsTransferring = false;
        }
    }

    private void OnClipboardChanged(object? sender, ClipboardContent content)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            CurrentContent = content;
            CurrentImageData = content.ImageData;
            StatusMessage = content.Type == ClipboardContentType.Image
                ? "Image detected in clipboard"
                : "Text detected in clipboard";
        });
    }
}
