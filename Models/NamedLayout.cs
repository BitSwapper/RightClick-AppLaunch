// File: Models/NamedLayout.cs
using System;

namespace RightClickAppLauncher.Models
{
    public class NamedLayout
    {
        public Guid Id { get; set; } // Unique ID for this saved layout entry
        public string Name { get; set; }
        public string LayoutJson { get; set; } // JSON string of List<LauncherItem>
        public DateTime SavedDate { get; set; }
        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }

        // Parameterless constructor for deserialization
        public NamedLayout()
        {
            Id = Guid.NewGuid();
            SavedDate = DateTime.UtcNow;
        }
    }
}