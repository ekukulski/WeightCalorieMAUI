using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;

namespace WeightCalorieMAUI
{
    /// <summary>
    /// The main page of the WeightCalorieMAUI application.
    /// </summary>
    public partial class MainPage : ContentPage
    {
        private ObservableCollection<WeightCalorieData> _dataItems = new();

        /// <summary>
        /// Gets or sets the collection of weight and calorie records.
        /// </summary>
        public ObservableCollection<WeightCalorieData> DataItems
        {
            get => _dataItems;
            set
            {
                _dataItems = value;
                OnPropertyChanged(nameof(DataItems));
            }
        }

        private readonly DataService _dataService;
        private readonly WeightCalorieManager _manager;
        private WeightCalorieData? _selectedRecord;
        private bool _isEditing = false;
        private bool _isDeleting = false;

        public Command EditRecordCommand { get; }
        public Command DeleteRecordCommand { get; }


        /// <summary>
        /// Initializes a new instance of the <see cref="MainPage"/> class.
        /// </summary>
        public MainPage()
        {
            InitializeComponent();

            EditRecordCommand = new Command(async () => await EditSelectedRecord());
            DeleteRecordCommand = new Command(async () => await DeleteSelectedRecord());

            BindingContext = this;

            _dataService = new DataService();
            _manager = new WeightCalorieManager();

            // Import database on startup, then load data
            MainThread.InvokeOnMainThreadAsync(async () => 
            {
                bool imported = await _dataService.ImportDatabaseAsync();
                if (imported)
                {
                    await DisplayAlert("Import", "Database imported from OneDrive successfully.", "OK");
                }
                await LoadDataAsync();
            });

            // Adjust window size for Windows platform
            if (DeviceInfo.Platform == DevicePlatform.WinUI)
            {
                var window = Application.Current?.Windows[0];
                if (window != null)
                {
                    window.Width = 450;
                    window.Height = 1000;
                }
            }
        }

        /// <summary>
        /// Displays the panel for adding a new record.
        /// </summary>
        private void OnAddRecordClicked(object sender, EventArgs e)
        {
            AddRecordPanel.IsVisible = true;
        }

        /// <summary>
        /// Closes the application.
        /// </summary>
        private async void OnExitClicked(object sender, EventArgs e)
        {
            await _dataService.ExportDatabaseAsync();
            await DisplayAlert("Export", "Database exported to OneDrive. Allow ~30 seconds for OneDrive to sync.", "OK");
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
        
        /// <summary>
        /// Loads the data asynchronously from the data service.
        /// </summary>
        private async Task LoadDataAsync()
        {
            var records = await _dataService.LoadDataAsync();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                DataItems.Clear();
                foreach (var item in records)
                {
                    DataItems.Add(item);
                }
            });

            var weights = records.Select(r => double.Parse(r.Weight)).ToList();
            var calories = records.Select(r => double.Parse(r.Calorie)).ToList();

            _manager.CalculateAverages(weights, calories, AvgWeightLossLabel, AvgCaloriesLabel);
        }

        /// <summary>
        /// Saves a new record to the data service.
        /// </summary>
        private async void OnSaveRecordClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(WeightEntry.Text) || string.IsNullOrWhiteSpace(CaloriesEntry.Text))
            {
                await DisplayAlert("Error", "Please enter both weight and calories.", "OK");
                return;
            }

            var newRecord = new WeightCalorieData
            {
                Date = DatePicker.Date.ToString("yyyy-MM-dd"),
                Weight = WeightEntry.Text,
                Calorie = CaloriesEntry.Text
            };

            _dataService.SaveRecord(newRecord);
            await LoadDataAsync();
            await _dataService.ExportDatabaseAsync(); // Export after saving

            WeightEntry.Text = string.Empty;
            CaloriesEntry.Text = string.Empty;
            AddRecordPanel.IsVisible = false;
        }

        /// <summary>
        /// Cancels the record addition process and hides the panel.
        /// </summary>
        private void OnCancelClicked(object sender, EventArgs e)
        {
            AddRecordPanel.IsVisible = false;
            WeightEntry.Text = string.Empty;
            CaloriesEntry.Text = string.Empty;
        }

        /// <summary>
        /// Enables editing mode and prompts the user to select a record.
        /// </summary>
        private async void OnEditRecordClicked(object sender, EventArgs e)
        {
            await EditSelectedRecord();
        }

        /// <summary>
        /// Enables deletion mode and prompts the user to select a record.
        /// </summary>
        private async void OnDeleteRecordClicked(object sender, EventArgs e)
        {
            await DeleteSelectedRecord();
        }

        /// <summary>
        /// Edits the selected record asynchronously.
        /// </summary>
        private async Task EditSelectedRecord()
        {
            if (_selectedRecord == null) return;

            string? newWeight = await DisplayPromptAsync("Edit Record", "Enter new weight:", initialValue: _selectedRecord.Weight);
            if (string.IsNullOrWhiteSpace(newWeight)) return;

            string? newCalories = await DisplayPromptAsync("Edit Record", "Enter new calories:", initialValue: _selectedRecord.Calorie);
            if (string.IsNullOrWhiteSpace(newCalories)) return;

            _dataService.UpdateRecord(_selectedRecord.Date, newWeight, newCalories);
            await LoadDataAsync();
            await _dataService.ExportDatabaseAsync(); // Export after updating
            _isEditing = false;
        }

        /// <summary>
        /// Handles selection of a record for editing or deletion.
        /// </summary>
        private async void OnRecordSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.Count == 0) return;

            _selectedRecord = e.CurrentSelection.FirstOrDefault() as WeightCalorieData;
            if (_selectedRecord == null) return;

            if (_isEditing)
            {
                _isEditing = false; // Reset flag
                await EditSelectedRecord();
            }
            else if (_isDeleting)
            {
                _isDeleting = false; // Reset flag
                await DeleteSelectedRecord();
            }

            DataView.SelectedItem = null; // Clear selection after action
        }

        /// <summary>
        /// Handles the TextChanged event for an entry field, ensuring that only valid numeric input is accepted.
        /// </summary>
        /// <param name="sender">The source of the event, expected to be an <see cref="Entry"/> control.</param>
        /// <param name="e">The event data containing the new and old text values.</param>
        private void OnEntryTextChanged(object sender, TextChangedEventArgs e)
        {
            Entry entry = (Entry)sender;

            if (entry.Text != null && !IsValidNumericInput(entry.Text))
            {
                entry.Text = e.OldTextValue; // Reset to old value if invalid
            }
        }

        /// <summary>
        /// Validates if the provided text is a numeric input.
        /// </summary>
        /// <param name="text">The input text to validate.</param>
        /// <returns>True if the text is a valid number; otherwise, false.</returns>
        private bool IsValidNumericInput(string text)
        {
            if (string.IsNullOrEmpty(text))
                return true; // Allow empty input

            return decimal.TryParse(text, out _);
        }

        /// <summary>
        /// Deletes the selected record asynchronously.
        /// </summary>
        private async Task DeleteSelectedRecord()
        {
            if (_selectedRecord == null) return;

            bool confirm = await DisplayAlert("Delete", $"Are you sure you want to delete the record for {_selectedRecord.Date}?", "Yes", "No");
            if (!confirm) return;

            _dataService.DeleteRecord(_selectedRecord.Date);
            await LoadDataAsync();
            await _dataService.ExportDatabaseAsync(); // Export after deleting
            _isDeleting = false;
        }
    }
}
