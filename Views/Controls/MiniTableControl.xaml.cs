using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace Finly.Views.Controls
{
    public partial class MiniTableControl : UserControl
    {
        public MiniTableControl()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(
                nameof(ItemsSource),
                typeof(IEnumerable),
                typeof(MiniTableControl),
                new PropertyMetadata(null, OnItemsSourceChanged));

        public IEnumerable? ItemsSource
        {
            get => (IEnumerable?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public static readonly DependencyProperty HeaderTemplateProperty =
            DependencyProperty.Register(
                nameof(HeaderTemplate),
                typeof(DataTemplate),
                typeof(MiniTableControl),
                new PropertyMetadata(null));

        public DataTemplate? HeaderTemplate
        {
            get => (DataTemplate?)GetValue(HeaderTemplateProperty);
            set => SetValue(HeaderTemplateProperty, value);
        }

        public static readonly DependencyProperty RowTemplateProperty =
            DependencyProperty.Register(
                nameof(RowTemplate),
                typeof(DataTemplate),
                typeof(MiniTableControl),
                new PropertyMetadata(null));

        public DataTemplate? RowTemplate
        {
            get => (DataTemplate?)GetValue(RowTemplateProperty);
            set => SetValue(RowTemplateProperty, value);
        }

        // Selection
        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(
                nameof(SelectedItem),
                typeof(object),
                typeof(MiniTableControl),
                new PropertyMetadata(null));

        public object? SelectedItem
        {
            get => (object?)GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        public static readonly DependencyProperty SelectedIndexProperty =
            DependencyProperty.Register(
                nameof(SelectedIndex),
                typeof(int),
                typeof(MiniTableControl),
                new PropertyMetadata(-1));

        public int SelectedIndex
        {
            get => (int)GetValue(SelectedIndexProperty);
            set => SetValue(SelectedIndexProperty, value);
        }

        public static readonly DependencyProperty SelectionModeProperty =
            DependencyProperty.Register(
                nameof(SelectionMode),
                typeof(SelectionMode),
                typeof(MiniTableControl),
                new PropertyMetadata(SelectionMode.Single));

        public SelectionMode SelectionMode
        {
            get => (SelectionMode)GetValue(SelectionModeProperty);
            set => SetValue(SelectionModeProperty, value);
        }

        // Scrollbars
        public static readonly DependencyProperty HorizontalScrollBarVisibilityProperty =
            DependencyProperty.Register(
                nameof(HorizontalScrollBarVisibility),
                typeof(ScrollBarVisibility),
                typeof(MiniTableControl),
                new PropertyMetadata(ScrollBarVisibility.Disabled));

        public ScrollBarVisibility HorizontalScrollBarVisibility
        {
            get => (ScrollBarVisibility)GetValue(HorizontalScrollBarVisibilityProperty);
            set => SetValue(HorizontalScrollBarVisibilityProperty, value);
        }

        public static readonly DependencyProperty VerticalScrollBarVisibilityProperty =
            DependencyProperty.Register(
                nameof(VerticalScrollBarVisibility),
                typeof(ScrollBarVisibility),
                typeof(MiniTableControl),
                new PropertyMetadata(ScrollBarVisibility.Auto));

        public ScrollBarVisibility VerticalScrollBarVisibility
        {
            get => (ScrollBarVisibility)GetValue(VerticalScrollBarVisibilityProperty);
            set => SetValue(VerticalScrollBarVisibilityProperty, value);
        }

        // ItemsCount (do empty-state)
        public static readonly DependencyProperty ItemsCountProperty =
            DependencyProperty.Register(
                nameof(ItemsCount),
                typeof(int),
                typeof(MiniTableControl),
                new PropertyMetadata(0));

        public int ItemsCount
        {
            get => (int)GetValue(ItemsCountProperty);
            private set => SetValue(ItemsCountProperty, value);
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (MiniTableControl)d;

            if (e.OldValue is INotifyCollectionChanged oldNcc)
                oldNcc.CollectionChanged -= control.OnItemsCollectionChanged;

            if (e.NewValue is INotifyCollectionChanged newNcc)
                newNcc.CollectionChanged += control.OnItemsCollectionChanged;

            control.RecountItems();
        }

        private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RecountItems();
        }

        private void RecountItems()
        {
            try
            {
                if (ItemsSource == null)
                {
                    ItemsCount = 0;
                    return;
                }

                // Jeśli to ICollection, to liczymy szybko
                if (ItemsSource is ICollection col)
                {
                    ItemsCount = col.Count;
                    return;
                }

                // Fallback: policz enumerując (nieidealne, ale działa)
                int count = 0;
                foreach (var _ in ItemsSource) count++;
                ItemsCount = count;
            }
            catch
            {
                ItemsCount = 0;
            }
        }
    }
}
