using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using TodoSidebar.Services;

namespace TodoSidebar
{
    /// <summary>
    /// 成就图鉴窗口：展示全部徽章，已解锁彩色，未解锁灰色剪影 + 条件。
    /// </summary>
    public partial class AchievementWindow : Window
    {
        public AchievementWindow()
        {
            InitializeComponent();
            LoadBadges();
        }

        private void LoadBadges()
        {
            try
            {
                var defs = AchievementService.Instance.GetDefinitions();
                var unlockedAt = new Dictionary<string, DateTime?>();
                foreach (var def in defs)
                {
                    unlockedAt[def.Id] = DatabaseService.Instance.GetAchievementUnlockedAt(def.Id);
                }

                var items = defs
                    .Select(d => new BadgeItem(d, unlockedAt.TryGetValue(d.Id, out var at) && at.HasValue))
                    .ToList();

                BadgeList.ItemsSource = items;
                var unlockedCount = items.Count(i => i.IsUnlocked);
                SummaryText.Text = $"{unlockedCount}/{items.Count} 已解锁";
                FooterText.Text = unlockedCount == items.Count
                    ? "🎉 全部成就已收集，你是真正的冒险者！"
                    : "坚持完成任务与番茄钟，成就徽章会一一解锁";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AchievementWindow load error: {ex.Message}");
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                DragMove();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Header drag error: {ex.Message}");
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>图鉴条目视图模型</summary>
        private class BadgeItem
        {
            public string Id { get; }
            public string Name { get; }
            public string Description { get; }
            public string Icon { get; }
            public bool IsUnlocked { get; }

            public BadgeItem(AchievementDef def, bool isUnlocked)
            {
                Id = def.Id;
                Name = def.Name;
                Description = def.Description;
                Icon = def.Icon;
                IsUnlocked = isUnlocked;
            }
        }
    }
}
