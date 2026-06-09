using System;

namespace TodoSidebar.Models
{
    public class Milestone
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime TargetDate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool IsCompleted { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
