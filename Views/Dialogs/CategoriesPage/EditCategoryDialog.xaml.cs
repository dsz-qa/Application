using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Finly.Views.Dialogs
{
    public partial class EditCategoryDialog : Window
    {
        public CategoryDialogViewModel Category { get; private set; } = new();

        private bool _isEditMode;

        public enum CategoryDialogMode
        {
            Add,
            Edit
        }

        public EditCategoryDialog()
        {
            InitializeComponent();
            DataContext = Category;

            SetMode(CategoryDialogMode.Add, null);

            Loaded += (_, __) =>
            {
                HideInlineError();
                UpdateColorInfo();
            };
        }

        public void SetMode(CategoryDialogMode mode, string? categoryName)
        {
            _isEditMode = mode == CategoryDialogMode.Edit;

            if (HeaderTitleText != null)
                HeaderTitleText.Text = _isEditMode ? "Edytujesz kategorię" : "Dodaj kategorię";

            if (HeaderSubtitleText != null)
            {
                HeaderSubtitleText.Text = _isEditMode
                    ? $"Zmień dane kategorii: {categoryName ?? "(nieznana)"}"
                    : "Uzupełnij dane i zapisz kategorię.";
            }
        }

        public void LoadForAdd()
        {
            Category = new CategoryDialogViewModel();
            DataContext = Category;
            SetMode(CategoryDialogMode.Add, null);
            HideInlineError();
            UpdateColorInfo();
        }

        public void LoadForEdit(int id, string name, string? description, string? colorHex, string? icon)
        {
            Category = new CategoryDialogViewModel
            {
                Id = id,
                Name = name ?? string.Empty,
                Description = description ?? string.Empty,
                ColorHex = colorHex ?? string.Empty,
                Icon = icon
            };
            DataContext = Category;

            SetMode(CategoryDialogMode.Edit, name);
            HideInlineError();
            UpdateColorInfo();
        }


        // ======= TitleBar =======

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ======= Inline validation =======

        private void NameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(NameTextBox.Text))
                HideInlineError();
        }

        private void ShowInlineError(string message, Control? focus = null)
        {
            if (NameErrorText != null)
            {
                NameErrorText.Text = message;
                NameErrorText.Visibility = Visibility.Visible;
            }

            focus?.Focus();
        }

        private void HideInlineError()
        {
            if (NameErrorText != null)
            {
                NameErrorText.Text = string.Empty;
                NameErrorText.Visibility = Visibility.Collapsed;
            }
        }

        // ======= Color =======

        private void ColorPick_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string hex)
            {
                Category.ColorHex = hex;
                UpdateColorInfo();
            }
        }

        private void UpdateColorInfo()
        {
            if (ColorInfoText == null) return;

            if (string.IsNullOrWhiteSpace(Category.ColorHex))
                ColorInfoText.Text = "Wybierz kolor (opcjonalnie).";
            else
                ColorInfoText.Text = $"Wybrany kolor: {Category.ColorHex}";
        }

        // ======= Save =======

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not CategoryDialogViewModel vm)
                return;

            HideInlineError();

            vm.Name = (vm.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(vm.Name))
            {
                ShowInlineError("Podaj nazwę kategorii.", NameTextBox);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class CategoryDialogViewModel
    {
        public int Id { get; set; } // 0 dla Add
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ColorHex { get; set; } = string.Empty;

        // DODAJ TO:
        public string? Icon { get; set; } = null;
    }

}
