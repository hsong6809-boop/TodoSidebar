using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TodoSidebar.Controls
{
    public class TagControl : Control
    {
        public static readonly DependencyProperty TagsProperty =
            DependencyProperty.Register("Tags", typeof(ObservableCollection<string>), typeof(TagControl),
                new PropertyMetadata(new ObservableCollection<string>()));

        public static readonly DependencyProperty TagBrushProperty =
            DependencyProperty.Register("TagBrush", typeof(Brush), typeof(TagControl),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(91, 95, 233))));

        public static readonly DependencyProperty CanAddProperty =
            DependencyProperty.Register("CanAdd", typeof(bool), typeof(TagControl),
                new PropertyMetadata(true));

        public static readonly DependencyProperty CanRemoveProperty =
            DependencyProperty.Register("CanRemove", typeof(bool), typeof(TagControl),
                new PropertyMetadata(true));

        public ObservableCollection<string> Tags
        {
            get => (ObservableCollection<string>)GetValue(TagsProperty);
            set => SetValue(TagsProperty, value);
        }

        public Brush TagBrush
        {
            get => (Brush)GetValue(TagBrushProperty);
            set => SetValue(TagBrushProperty, value);
        }

        public bool CanAdd
        {
            get => (bool)GetValue(CanAddProperty);
            set => SetValue(CanAddProperty, value);
        }

        public bool CanRemove
        {
            get => (bool)GetValue(CanRemoveProperty);
            set => SetValue(CanRemoveProperty, value);
        }

        public event EventHandler<string>? TagAdded;
        public event EventHandler<string>? TagRemoved;

        static TagControl()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(TagControl),
                new FrameworkPropertyMetadata(typeof(TagControl)));
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            var addTagButton = GetTemplateChild("AddTagButton") as Button;
            if (addTagButton != null)
            {
                addTagButton.Click += AddTagButton_Click;
            }

            var tagInput = GetTemplateChild("TagInput") as TextBox;
            if (tagInput != null)
            {
                tagInput.KeyDown += TagInput_KeyDown;
            }

            UpdateTagsDisplay();
        }

        private void AddTagButton_Click(object sender, RoutedEventArgs e)
        {
            var tagInput = GetTemplateChild("TagInput") as TextBox;
            if (tagInput != null && !string.IsNullOrWhiteSpace(tagInput.Text))
            {
                AddTag(tagInput.Text.Trim());
                tagInput.Text = "";
            }
        }

        private void TagInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var tagInput = sender as TextBox;
                if (tagInput != null && !string.IsNullOrWhiteSpace(tagInput.Text))
                {
                    AddTag(tagInput.Text.Trim());
                    tagInput.Text = "";
                }
            }
        }

        public void AddTag(string tag)
        {
            if (!Tags.Contains(tag))
            {
                Tags.Add(tag);
                TagAdded?.Invoke(this, tag);
                UpdateTagsDisplay();
            }
        }

        public void RemoveTag(string tag)
        {
            if (Tags.Remove(tag))
            {
                TagRemoved?.Invoke(this, tag);
                UpdateTagsDisplay();
            }
        }

        private void UpdateTagsDisplay()
        {
            var tagsPanel = GetTemplateChild("TagsPanel") as ItemsControl;
            if (tagsPanel == null) return;

            tagsPanel.Items.Clear();

            foreach (var tag in Tags)
            {
                var tagBorder = new Border
                {
                    Background = TagBrush,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 0, 4, 4)
                };

                var stackPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal
                };

                var tagText = new TextBlock
                {
                    Text = tag,
                    Foreground = Brushes.White,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                };

                stackPanel.Children.Add(tagText);

                if (CanRemove)
                {
                    var removeButton = new Button
                    {
                        Content = "×",
                        Background = Brushes.Transparent,
                        Foreground = Brushes.White,
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(4, 0, 0, 0),
                        FontSize = 12,
                        Cursor = Cursors.Hand,
                        Tag = tag
                    };
                    removeButton.Click += RemoveButton_Click;
                    stackPanel.Children.Add(removeButton);
                }

                tagBorder.Child = stackPanel;
                tagsPanel.Items.Add(tagBorder);
            }
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tag)
            {
                RemoveTag(tag);
            }
        }
    }

    // 标签字符串转换器
    public static class TagHelper
    {
        public static ObservableCollection<string> FromString(string? tags)
        {
            var result = new ObservableCollection<string>();
            if (!string.IsNullOrWhiteSpace(tags))
            {
                foreach (var tag in tags.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    result.Add(tag.Trim());
                }
            }
            return result;
        }

        public static string ToString(ObservableCollection<string> tags)
        {
            return string.Join(",", tags);
        }
    }
}
