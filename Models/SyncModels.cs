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

        /// <summary>v5.3 回收站：软删除时间（ISO 文本，展示参考；老客户端忽略）。</summary>
        [Column("deleted_at")]
        public string? DeletedAt { get; set; }
    }

    /// <summary>
    /// 同步用的 XP 流水模型（对应 Supabase xp_log 表）
    /// </summary>
    [Table("xp_log")]
    public class SyncXpLog : BaseModel
    {
        [PrimaryKey("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Column("user_id")]
        public string? UserId { get; set; }

        [Column("source")]
        public string Source { get; set; } = "";

        [Column("amount")]
        public int Amount { get; set; }

        [Column("task_id")]
        public int? TaskId { get; set; }

        [Column("date")]
        public string Date { get; set; } = "";

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 同步用的番茄会话模型（对应 Supabase pomodoro_session 表）
    /// </summary>
    [Table("pomodoro_session")]
    public class SyncPomodoroSession : BaseModel
    {
        [PrimaryKey("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Column("user_id")]
        public string? UserId { get; set; }

        [Column("task_id")]
        public int? TaskId { get; set; }

        [Column("start_time")]
        public DateTime StartTime { get; set; }

        [Column("end_time")]
        public DateTime? EndTime { get; set; }

        [Column("duration_minutes")]
        public int DurationMinutes { get; set; }

        [Column("completed")]
        public bool Completed { get; set; }

        [Column("date")]
        public string Date { get; set; } = "";
    }

    /// <summary>
    /// 同步用的用户成长档案（对应 Supabase user_profile 表）
    /// </summary>
    [Table("user_profile")]
    public class SyncUserProfile : BaseModel
    {
        // S5 修复：不再默认生成新 GUID——Upsert 按主键解析冲突，
        // 每次实例化新 Guid 会让 upsert 退化为无限插入新行。
        // Id 由调用方显式赋值（复用云端已有行的 Id，或首次上传时生成一次）。
        [PrimaryKey("id")]
        public Guid Id { get; set; }

        [Column("user_id")]
        public string? UserId { get; set; }

        [Column("level")]
        public int Level { get; set; } = 1;

        [Column("xp")]
        public int Xp { get; set; }

        [Column("total_xp")]
        public int TotalXp { get; set; }

        [Column("combo_days")]
        public int ComboDays { get; set; }

        [Column("best_combo_days")]
        public int BestComboDays { get; set; }

        [Column("title")]
        public string Title { get; set; } = "初出茅庐";

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
    
    /// <summary>
    /// v5.2 账号中心：账号档案（对应 Supabase account_profile 表）。
    /// user_id 为主键（每账号一行）；uid 为 8 位短账号 ID；
    /// avatar_kind: d1~d8 内置 / custom 自定义（avatar_data 存 base64 PNG）。
    /// </summary>
    [Table("account_profile")]
    public class SyncAccountProfile : BaseModel
    {
        [PrimaryKey("user_id")]
        public string? UserId { get; set; }

        [Column("uid")]
        public string Uid { get; set; } = "";

        [Column("nickname")]
        public string Nickname { get; set; } = "";

        [Column("avatar_kind")]
        public string AvatarKind { get; set; } = "d1";

        [Column("avatar_data")]
        public string? AvatarData { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
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
