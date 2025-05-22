using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RightClickAppLauncher.Models;

public class LauncherItem : INotifyPropertyChanged
{
    public Guid Id { get; set; }

    string _displayName;
    public string DisplayName
    {
        get => _displayName;
        set { _displayName = value; OnPropertyChanged(); }
    }

    string _executablePath;
    public string ExecutablePath
    {
        get => _executablePath;
        set { _executablePath = value; OnPropertyChanged(); }
    }

    bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if(_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    string _arguments;
    public string Arguments
    {
        get => _arguments;
        set { _arguments = value; OnPropertyChanged(); }
    }

    string _iconPath;
    public string IconPath
    {
        get => _iconPath;
        set { _iconPath = value; OnPropertyChanged(); }
    }

    string _workingDirectory;
    public string WorkingDirectory
    {
        get => _workingDirectory;
        set { _workingDirectory = value; OnPropertyChanged(); }
    }

    double _x;
    public double X
    {
        get => _x;
        set { _x = value; OnPropertyChanged(); }
    }

    double _y;
    public double Y
    {
        get => _y;
        set { _y = value; OnPropertyChanged(); }
    }

    public LauncherItem()
    {
        Id = Guid.NewGuid();
        DisplayName = "New Application";
        ExecutablePath = string.Empty;
        Arguments = string.Empty;
        IconPath = string.Empty;
        WorkingDirectory = string.Empty;
        X = 10;
        Y = 10;
    }

    public event PropertyChangedEventHandler PropertyChanged;
    public virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}