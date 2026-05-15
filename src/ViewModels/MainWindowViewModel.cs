using System;
using System.Collections.Generic;
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
    private readonly SftpService _sftpServiceForFilename;

    private const int MaxHistoryEntries = 64;

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

    [ObservableProperty]
    private int _historyPosition = -1;

    [ObservableProperty]
    private int _historyCount;

    [ObservableProperty]
    private bool _isViewingHistory;

    [ObservableProperty]
    private string _historyPositionText = "";

    [ObservableProperty]
    private bool _imageOnlyMode;

    [ObservableProperty]
    private bool _canNavigateBack;

    [ObservableProperty]
    private bool _canNavigateForward;

    private List<HistoryEntry> _historyEntries = new();
    private string? _latestFilename;

    public MainWindowViewModel()
    {
        _sshConfigService = new SshConfigService();
        _clipboardMonitor = new ClipboardMonitor();
        _sftpService = new SftpService();
        _sftpServiceForFilename = new SftpService();

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

    public void PauseClipboardMonitoring()
    {
        _clipboardMonitor.StopMonitoring();
    }

    public void ResumeClipboardMonitoring()
    {
        _clipboardMonitor.StartMonitoring(250);
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

            string filename;
            byte[] data;

            if (IsViewingHistory && HistoryPosition >= 0 && HistoryPosition < _historyEntries.Count)
            {
                var entry = _historyEntries[HistoryPosition];
                filename = entry.Filename;
                data = entry.ContentType == HistoryContentType.Image
                    ? entry.ImageData!
                    : System.Text.Encoding.UTF8.GetBytes(entry.Text ?? "");
            }
            else
            {
                (filename, data) = _sftpServiceForFilename.GenerateFilenameAndData(CurrentContent);
                _latestFilename = filename;
            }

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

    public void SaveToHistory(ClipboardContent content)
    {
        if (content.Type == ClipboardContentType.Text && string.IsNullOrWhiteSpace(content.Text))
            return;

        if (_historyEntries.Count > 0)
        {
            var lastEntry = _historyEntries[0];
            if (lastEntry.ContentType == HistoryContentType.Image && content.Type == ClipboardContentType.Image)
            {
                if (lastEntry.ImageData != null && content.ImageData != null &&
                    lastEntry.ImageData.Length == content.ImageData.Length)
                {
                    var sameImage = true;
                    for (int i = 0; i < lastEntry.ImageData.Length; i++)
                    {
                        if (lastEntry.ImageData[i] != content.ImageData[i])
                        {
                            sameImage = false;
                            break;
                        }
                    }
                    if (sameImage) return;
                }
            }
            else if (lastEntry.ContentType == HistoryContentType.Text && content.Type == ClipboardContentType.Text)
            {
                if (lastEntry.Text == content.Text) return;
            }
        }

        var (filename, _) = _sftpServiceForFilename.GenerateFilenameAndData(content);
        _latestFilename = filename;

        var entry = HistoryEntry.FromClipboardContent(content, filename);
        _historyEntries.Insert(0, entry);

        if (_historyEntries.Count > MaxHistoryEntries)
        {
            _historyEntries.RemoveAt(_historyEntries.Count - 1);
        }

        HistoryCount = _historyEntries.Count;
        HistoryPosition = -1;
        IsViewingHistory = false;
        UpdateHistoryPositionText();
        UpdateNavigationButtonStates();
    }

    [RelayCommand]
    private void NavigateBack()
    {
        if (HistoryCount == 0)
            return;

        if (HistoryPosition < 0)
        {
            HistoryPosition = HistoryCount - 1;
        }
        else if (HistoryPosition >= HistoryCount - 1)
        {
            return;
        }
        else
        {
            HistoryPosition++;
        }

        IsViewingHistory = true;
        UpdateHistoryPositionText();
        UpdateNavigationButtonStates();
        LoadHistoryEntry();
    }

    [RelayCommand]
    private void NavigateForward()
    {
        if (HistoryPosition <= 0)
        {
            GoToLatest();
            return;
        }

        HistoryPosition--;
        IsViewingHistory = true;
        UpdateHistoryPositionText();
        UpdateNavigationButtonStates();
        LoadHistoryEntry();
    }

    [RelayCommand]
    private void GoToLatest()
    {
        HistoryPosition = -1;
        IsViewingHistory = false;
        UpdateHistoryPositionText();
        UpdateNavigationButtonStates();
        if (_historyEntries.Count > 0)
        {
            var entry = _historyEntries[0];
            CurrentContent = entry.ToClipboardContent();
            CurrentImageData = entry.ImageData;
            CurrentFilename = entry.Filename;
        }
    }

    private void LoadHistoryEntry()
    {
        var index = HistoryPosition;
        if (index < 0 || index >= _historyEntries.Count)
            return;

        var entry = _historyEntries[index];
        CurrentContent = entry.ToClipboardContent();
        CurrentImageData = entry.ImageData;
        CurrentFilename = entry.Filename;
    }

    private void UpdateHistoryPositionText()
    {
        if (!IsViewingHistory || HistoryCount == 0)
        {
            HistoryPositionText = "";
            return;
        }

        var displayPosition = HistoryCount - HistoryPosition;
        HistoryPositionText = $"← {displayPosition} of {HistoryCount} →";
    }

    private void UpdateNavigationButtonStates()
    {
        CanNavigateBack = HistoryCount > 1 && HistoryPosition < HistoryCount - 1;
        CanNavigateForward = IsViewingHistory && HistoryPosition > 0;
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

            if (!IsViewingHistory)
            {
                SaveToHistory(content);
            }
        });
    }

    public (string filename, byte[] data) GetTransferData()
    {
        if (IsViewingHistory && HistoryPosition >= 0 && HistoryPosition < _historyEntries.Count)
        {
            var entry = _historyEntries[HistoryPosition];
            var filename = entry.Filename;
            var data = entry.ContentType == HistoryContentType.Image
                ? entry.ImageData!
                : System.Text.Encoding.UTF8.GetBytes(entry.Text ?? "");
            return (filename, data);
        }
        else
        {
            return _sftpServiceForFilename.GenerateFilenameAndData(CurrentContent!);
        }
    }

    public string GetLatestFilename()
    {
        return _latestFilename ?? "";
    }
}
