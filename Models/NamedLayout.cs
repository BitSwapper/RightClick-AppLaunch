// File: Models/NamedLayout.cs
using System;

namespace RightClickAppLauncher.Models
{
    public class NamedLayout
    {
        public Guid Id { get; set; } // Unique ID for this saved layout entry
        public string Name { get; set; }
        public string LayoutJson { get; set; }
        public DateTime SavedDate { get; set; }
        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }

        public double IconSize { get; set; }
        public double IconSpacing { get; set; }

        public NamedLayout()
        {
            Id = Guid.NewGuid();
            SavedDate = DateTime.UtcNow;
            IconSize = 20;
            IconSpacing = 10;
        }
    }
}