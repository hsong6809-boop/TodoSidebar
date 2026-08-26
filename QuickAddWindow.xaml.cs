using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TodoSidebar.Controls;
using TodoSidebar.Models;
using TodoSidebar.Services;

namespace TodoSidebar
{
    /// <summary>
    /// v5.4 全局快速添加浮窗（Spotlight 式）：
    /// 任意应用中 Ctrl+Alt+Space 呼出，输入自然语言回车即落库。
    /// 复用 V5.1 NaturalLanguageParser；成功后短暂显示 ✓ 并自动关闭；
    /// 失焦自动关闭（正在显示错误提示时例外）。
    /// </summary>
    public partial class QuickAddWindow : Window
    {
        /// <summary>当前浮窗实例（避免热键重复开窗）。</summary>
        private static QuickAddWindow? _current;

        public static void Toggle()
        {
            if (_current != null)
            {
                try { _current.Close(); } catch { }
                return;
            }

            var win = new QuickAddWindow();
            _current = win;
            win.Closed += (_, _) => _current = null;
            win.Show();
            win.Activate();
            win.InputBox.Focus();
        }

        private QuickAddWindow()
        {
            InitializeComponent();
            PositionTopCenter();
        }

        private void PositionTopCenter()
        {
            var work = SystemParameters.WorkArea;
            Left = work.Left + (work.Width - Width) / 2;
            Top = work.Top + Math.Max(48, work.Height * 0.18);
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            // 错误提示展示期间不因失焦关闭，给用户阅读时间
            if (_errorUntil > DateTime.Now) return;
            Close();
        }

        private DateTime _errorUntil = DateTime.MinValue;

        private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            StatusText.Text = "";
            var p = NaturalLanguageParser.Parse(InputBox.Text);
            var parts = new List<string>();
            if (p.DueDate.HasValue)
                parts.Add(DescribeDate(p.DueDate.Value));
            if (p.Priority.HasValue)
                parts.Add(p.Priority.Value switch
                {
                    TaskPriority.High => "高优先级",
                    TaskPriority.Low => "低优先级",
                    _ => "中优先级"
                });
            parts.AddRange(p.Tags.Select(t => "#" + t));

            PreviewText.Text = string.Join("  ·  ", parts);
            LeadIcon.Glyph = p.HasDue ? Icons.Clock : Icons.CheckList;
        }

        private static string DescribeDate(DateTime d)
        {
            var today = DateTime.Today;
            var day = d.Date == today ? "今天"
                    : d.Date == today.AddDays(1) ? "明天"
                    : d.ToString("MM月dd日");
            return d.TimeOfDay == TimeSpan.Zero ? day : $"{day} {d:HH:mm}";
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Submit();
            }
        }

        private void Submit()
        {
            var raw = InputBox.Text.Trim();
            if (raw.Length == 0) return;

            try
            {
                var parsed = NaturalLanguageParser.Parse(raw);
                var title = parsed.Title.Length > 0 ? parsed.Title : raw;
                var deadline = parsed.DueDate;
                var type = deadline.HasValue ? TaskType.Deadline : TaskType.Daily;

                var task = new TaskItem
                {
                    Title = title,
                    Type = type,
                    Priority = parsed.Priority ?? TaskPriority.Medium,
                    Deadline = deadline
                };
                task.Id = DatabaseService.Instance.InsertTask(task);

                if (parsed.Tags.Count > 0)
                {
                    task.Tags = string.Join(",", parsed.Tags);
                    DatabaseService.Instance.UpdateTask(task);
                }

                // v5.5 行为统计：浮窗使用 + NLP 贡献判定
                try { DatabaseService.Instance.IncrementSettingCounter("QuickAddUsedCount"); } catch { }
                bool contributed = parsed.Title.Length > 0 && parsed.Title != raw
                    || parsed.DueDate.HasValue || parsed.Priority.HasValue || parsed.Tags.Count > 0;
                if (contributed)
                {
                    try { DatabaseService.Instance.IncrementSettingCounter("NlpUsedCount"); } catch { }
                }

                // 通知主界面刷新（浮窗可能在任何页面状态下触发）
                var vm = App.SharedViewModel;
                if (vm != null)
                {
                    vm.LoadData();
                }

                StatusText.Text = "✓ 已添加";
                InputBox.Clear();
                PreviewText.Text = "";

                // 短暂展示成功后自动关闭
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
                timer.Tick += (s, args) => { timer.Stop(); Close(); };
                timer.Start();
            }
            catch (Exception ex)
            {
                _errorUntil = DateTime.Now.AddSeconds(3);
                StatusText.Text = "";
                PreviewText.Text = $"添加失败：{ex.Message}";
            }
        }
    }
}
