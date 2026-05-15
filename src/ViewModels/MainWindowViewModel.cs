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

    private const int MaxEntries = 64;

    [ObservableProperty]
    private ObservableCollection<SshHost> _hosts = new();

    [ObservableProperty]
    private SshHost? _selectedHost;

    [ObservableProperty]
    private bool _isTransferring;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private int _currentPosition = -1;

    [ObservableProperty]
    private string _historyPositionText = "";

    [ObservableProperty]
    private bool _imageOnlyMode;

    [ObservableProperty]
    private bool _canNavigateBack;

    [ObservableProperty]
    private bool _canNavigateForward;

    [ObservableProperty]
    private ClipboardEntry? _currentEntry;

    private readonly List<ClipboardEntry> _entries = new();

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

    public void AddEntry(ClipboardContent content)
    {
        if (content.Type == ClipboardContentType.Text && string.IsNullOrWhiteSpace(content.Text))
            return;

        if (IsKnownFilename(content))
            return;

        var (filename, _) = _sftpServiceForFilename.GenerateFilenameAndData(content);

        var entry = ClipboardEntry.FromContent(content, filename);
        _entries.Insert(0, entry);

        if (_entries.Count > MaxEntries)
        {
            _entries.RemoveAt(_entries.Count - 1);
        }

        CurrentPosition = -1;
        CurrentEntry = _entries[0];
        UpdateNavigationState();
    }

    private bool IsKnownFilename(ClipboardContent content)
    {
        if (content.Type != ClipboardContentType.Text || content.Text == null)
            return false;

        var text = content.Text;
        foreach (var entry in _entries)
        {
            if (text == entry.LocalFilename ||
                text == entry.RemoteFilename ||
                text == Path.GetFileName(entry.LocalFilename) ||
                text == Path.GetFileName(entry.RemoteFilename))
            {
                return true;
            }
        }
        return false;
    }

    [RelayCommand]
    public void NavigateBack()
    {
        if (_entries.Count == 0)
            return;

        if (CurrentPosition < 0)
        {
            CurrentPosition = _entries.Count - 1;
        }
        else if (CurrentPosition >= _entries.Count - 1)
        {
            return;
        }
        else
        {
            CurrentPosition++;
        }

        CurrentEntry = _entries[CurrentPosition];
        UpdateNavigationState();
    }

    [RelayCommand]
    public void NavigateForward()
    {
        if (_entries.Count == 0)
            return;

        if (CurrentPosition <= 0)
        {
            GoToLatest();
            return;
        }

        CurrentPosition--;
        CurrentEntry = _entries[CurrentPosition];
        UpdateNavigationState();
    }

    [RelayCommand]
    public void GoToLatest()
    {
        if (_entries.Count == 0)
        {
            CurrentPosition = -1;
            CurrentEntry = null;
        }
        else
        {
            CurrentPosition = -1;
            CurrentEntry = _entries[0];
        }
        UpdateNavigationState();
    }

    private void UpdateNavigationState()
    {
        var count = _entries.Count;
        CanNavigateBack = count > 1 && CurrentPosition < count - 1;
        CanNavigateForward = CurrentPosition > 0;

        if (count == 0)
        {
            HistoryPositionText = "";
        }
        else if (CurrentPosition < 0)
        {
            HistoryPositionText = "";
        }
        else
        {
            var displayPosition = count - CurrentPosition;
            HistoryPositionText = $"← {displayPosition} of {count} →";
        }
    }

    public ClipboardEntry? GetCurrentEntry()
    {
        if (CurrentPosition >= 0 && CurrentPosition < _entries.Count)
        {
            return _entries[CurrentPosition];
        }
        return _entries.Count > 0 ? _entries[0] : null;
    }

    public (string filename, byte[] data) GetPersistData()
    {
        var entry = GetCurrentEntry();
        if (entry == null)
            throw new InvalidOperationException("No entry to persist");

        byte[] data;
        if (entry.Content.Type == ClipboardContentType.Image && entry.Content.ImageData != null)
        {
            data = entry.Content.ImageData;
        }
        else if (entry.Content.Text != null)
        {
            data = System.Text.Encoding.UTF8.GetBytes(entry.Content.Text);
        }
        else
        {
            throw new InvalidOperationException("No data to persist");
        }

        return (Path.GetFileName(entry.RemoteFilename), data);
    }

    private void OnClipboardChanged(object? sender, ClipboardContent content)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            if (ImageOnlyMode && content.Type == ClipboardContentType.Text)
                return;

            StatusMessage = content.Type == ClipboardContentType.Image
                ? "Image detected in clipboard"
                : "Text detected in clipboard";

            if (CurrentPosition < 0)
            {
                AddEntry(content);
            }
        });
    }
}