using System;
using System.Windows;
using System.Windows.Controls;

namespace NoteNest.UI.Plugins.TodoPlugin.Dialogs
{
    public partial class DatePickerDialog : Window
    {
        public DateTime? SelectedDate { get; private set; }

        public DatePickerDialog(DateTime? currentDate = null)
        {
            InitializeComponent();
            
            if (currentDate.HasValue)
            {
                DateCalendar.SelectedDate = currentDate.Value;
            }
            else
            {
                DateCalendar.SelectedDate = DateTime.Today;
            }
        }

        private void Today_Click(object sender, RoutedEventArgs e)
        {
            SelectedDate = DateTime.Today;
            DialogResult = true;
            Close();
        }

        private void Tomorrow_Click(object sender, RoutedEventArgs e)
        {
            SelectedDate = DateTime.Today.AddDays(1);
            DialogResult = true;
            Close();
        }

        private void ThisWeek_Click(object sender, RoutedEventArgs e)
        {
            // End of this week (Sunday)
            var today = DateTime.Today;
            var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)today.DayOfWeek + 7) % 7;
            if (daysUntilSunday == 0) daysUntilSunday = 7;  // If today is Sunday, go to next Sunday
            SelectedDate = today.AddDays(daysUntilSunday);
            DialogResult = true;
            Close();
        }

        private void NextWeek_Click(object sender, RoutedEventArgs e)
        {
            // Next Monday
            var today = DateTime.Today;
            var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
            if (daysUntilMonday == 0) daysUntilMonday = 7;  // If today is Monday, go to next Monday
            SelectedDate = today.AddDays(daysUntilMonday);
            DialogResult = true;
            Close();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            SelectedDate = null;
            DialogResult = true;
            Close();
        }

        private void Calendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
        {
            // Update selected date when user picks from calendar
            if (DateCalendar.SelectedDate.HasValue)
            {
                SelectedDate = DateCalendar.SelectedDate.Value;
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            SelectedDate = DateCalendar.SelectedDate;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

