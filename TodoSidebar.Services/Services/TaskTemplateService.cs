using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TodoSidebar.Models;

namespace TodoSidebar.Services
{
    public class TaskTemplateService
    {
        private readonly string _templatesPath;
        private List<TaskTemplate> _templates;

        public TaskTemplateService()
        {
            _templatesPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TodoSidebar", "templates.json");
            
            _templates = LoadTemplates();
        }

        public List<TaskTemplate> GetTemplates()
        {
            return _templates;
        }

        public void SaveTemplate(TaskTemplate template)
        {
            template.Id = Guid.NewGuid().ToString();
            template.CreatedAt = DateTime.Now;
            _templates.Add(template);
            SaveTemplates();
        }

        public void DeleteTemplate(string templateId)
        {
            _templates.RemoveAll(t => t.Id == templateId);
            SaveTemplates();
        }

        public TaskItem CreateTaskFromTemplate(TaskTemplate template)
        {
            return new TaskItem
            {
                Title = template.Title,
                Type = template.Type,
                Priority = template.Priority,
                Description = template.Description,
                Tags = template.Tags,
                Deadline = template.DefaultDeadline.HasValue 
                    ? DateTime.Now.AddDays(template.DefaultDeadline.Value) 
                    : null
            };
        }

        private List<TaskTemplate> LoadTemplates()
        {
            try
            {
                if (File.Exists(_templatesPath))
                {
                    var json = File.ReadAllText(_templatesPath);
                    return JsonSerializer.Deserialize<List<TaskTemplate>>(json) ?? new List<TaskTemplate>();
                }
            }
            catch { }

            // 返回默认模板
            return GetDefaultTemplates();
        }

        private void SaveTemplates()
        {
            try
            {
                var dir = Path.GetDirectoryName(_templatesPath);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_templates, options);
                File.WriteAllText(_templatesPath, json);
            }
            catch { }
        }

        private List<TaskTemplate> GetDefaultTemplates()
        {
            return new List<TaskTemplate>
            {
                new TaskTemplate
                {
                    Id = "default_1",
                    Name = "每日阅读",
                    Title = "阅读30分钟",
                    Type = TaskType.Daily,
                    Priority = TaskPriority.Medium,
                    Description = "每天阅读30分钟",
                    Tags = "学习,阅读",
                    Icon = "📚"
                },
                new TaskTemplate
                {
                    Id = "default_2",
                    Name = "运动锻炼",
                    Title = "运动锻炼",
                    Type = TaskType.Daily,
                    Priority = TaskPriority.High,
                    Description = "每天运动30分钟",
                    Tags = "健康,运动",
                    Icon = "🏃"
                },
                new TaskTemplate
                {
                    Id = "default_3",
                    Name = "周报",
                    Title = "提交周报",
                    Type = TaskType.Daily,
                    Priority = TaskPriority.High,
                    Description = "每周五提交周报",
                    Tags = "工作",
                    DefaultWeeklyDays = "5",
                    Icon = "📝"
                },
                new TaskTemplate
                {
                    Id = "default_4",
                    Name = "项目截止",
                    Title = "项目交付",
                    Type = TaskType.Deadline,
                    Priority = TaskPriority.High,
                    Description = "项目截止日期",
                    Tags = "工作,项目",
                    DefaultDeadline = 7,
                    Icon = "🎯"
                }
            };
        }
    }

    public class TaskTemplate
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public TaskType Type { get; set; }
        public TaskPriority Priority { get; set; } = TaskPriority.Medium;
        public string? Description { get; set; }
        public string? Tags { get; set; }
        public string Icon { get; set; } = "📋";
        public int? DefaultDeadline { get; set; } // 天数
        public string DefaultWeeklyDays { get; set; } = "1,2,3,4,5";
        public DateTime CreatedAt { get; set; }
    }
}
