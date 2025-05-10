// File: Models/LauncherItem.cs
using System;
using System.ComponentModel; // Required for INotifyPropertyChanged
using System.Runtime.CompilerServices; // Required for CallerMemberName

namespace RightClickAppLauncher.Models
{
    public class LauncherItem : INotifyPropertyChanged // Implement INotifyPropertyChanged
    {
        public Guid Id { get; set; } // Assuming Id doesn't change, no INPC needed

        private string _displayName;
        public string DisplayName
        {
            get => _displayName;
            set { _displayName = value; OnPropertyChanged(); }
        }

        private string _executablePath;
        public string ExecutablePath
        {
            get => _executablePath;
            set { _executablePath = value; OnPropertyChanged(); }
        }

        private string _arguments;
        public string Arguments
        {
            get => _arguments;
            set { _arguments = value; OnPropertyChanged(); }
        }

        private string _iconPath;
        public string IconPath
        {
            get => _iconPath;
            set { _iconPath = value; OnPropertyChanged(); }
        }

        private string _workingDirectory;
        public string WorkingDirectory
        {
            get => _workingDirectory;
            set { _workingDirectory = value; OnPropertyChanged(); }
        }

        private double _x;
        public double X
        {
            get => _x;
            set { _x = value; OnPropertyChanged(); } // Notify when X changes
        }

        private double _y;
        public double Y
        {
            get => _y;
            set { _y = value; OnPropertyChanged(); } // Notify when Y changes
        }

        public LauncherItem()
        {
            Id = Guid.NewGuid();
            DisplayName = "New Application"; // Initial values
            ExecutablePath = string.Empty;
            Arguments = string.Empty;
            IconPath = string.Empty;
            WorkingDirectory = string.Empty;
            X = 10;
            Y = 10;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}