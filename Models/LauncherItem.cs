// File: Models/LauncherItem.cs
using System;

namespace RightClickAppLauncher.Models
{
    public class LauncherItem
    {
        public Guid Id { get; set; }
        public string DisplayName { get; set; }
        public string ExecutablePath { get; set; }
        public string Arguments { get; set; }
        public string IconPath { get; set; }
        public string WorkingDirectory { get; set; }
        public double X { get; set; } // Position on Canvas
        public double Y { get; set; } // Position on Canvas

        public LauncherItem()
        {
            Id = Guid.NewGuid();
            DisplayName = "New Application";
            Arguments = string.Empty;
            IconPath = string.Empty;
            WorkingDirectory = string.Empty;
            X = 10; // Default X position
            Y = 10; // Default Y position
        }
    }
}