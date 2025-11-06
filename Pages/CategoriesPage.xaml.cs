using Microsoft.Data.Sqlite;
using System.Data;
using System.Windows.Controls;
namespace Finly.Pages
{
    public partial class CategoriesPage : UserControl
    {
        private readonly int _userId;
        public CategoriesPage() { InitializeComponent(); }
        public CategoriesPage(int userId) : this() { _userId = userId; }
    }


    }
