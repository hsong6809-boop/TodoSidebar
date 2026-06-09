using System;
using Supabase.Postgrest.Models;
using Supabase.Postgrest.Attributes;

namespace TodoSidebar.Models
{
    /// <summary>
    /// 同步任务模型（对应 Supabase 数据库表）
    /// </summary>
    [Table("tasks")]
    public class SyncTask : BaseModel
    {
        [PrimaryKey("id")]
        public Guid Id { get; set; }
        
        [Column("user_id")]
        public string? UserId { get; set; }
        
        [Column("title")]
        public string Title { get; set; } = string.Empty;
        
        [Column("type")]
        public int Type { get; set; } // 0=Daily, 1=Deadline
        
        [Column("priority")]
        public int Priority { get; set; } = 1; // 0=Low, 1=Med, 2=High
        
        [Column("is_completed")]
        public bool IsCompleted { get; set; }
        
        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        [Column("deadline")]
        public DateTime? Deadline { get; set; }
        
        [Column("completed_at")]
        public DateTime? CompletedAt { get; set; }
        
        [Column("description")]
        public string? Description { get; set; }
        
        [Column("tags")]
        public string? Tags { get; set; }
        
        [Column("sort_order")]
        public int SortOrder { get; set; }
        
        [Column("subtasks_json")]
        public string? SubtasksJson { get; set; }
        
        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        
        [Column("is_deleted")]
        public bool IsDeleted { get; set; }
    }
    
    /// <summary>
    /// 本地同步队列项
    /// </summary>
    public class SyncQueueItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TaskId { get; set; }
        public string Operation { get; set; } = string.Empty; // "create", "update", "delete"
        public string? TaskData { get; set; } // JSON 格式的任务数据
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int RetryCount { get; set; }
        public string? LastError { get; set; }
    }
    
    /// <summary>
    /// 同步状态
    /// </summary>
    public enum SyncStatus
    {
        Idle,
        Syncing,
        Error,
        Offline
    }
    
    /// <summary>
    /// 同步结果
    /// </summary>
    public class SyncResult
    {
        public bool Success { get; set; }
        public int Uploaded { get; set; }
        public int Downloaded { get; set; }
        public int Conflicts { get; set; }
        public string? Error { get; set; }
    }
}
