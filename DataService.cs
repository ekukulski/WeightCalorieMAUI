using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace WeightCalorieMAUI
{
    /// <summary>
    /// Provides data storage and retrieval functionality for weight and calorie records.
    /// </summary>
    public class DataService
    {
        private readonly string _filePath;

        /// <summary>
        /// Initializes a new instance of the <see cref="DataService"/> class.
        /// </summary>
        public DataService()
        {
            _filePath = GetFilePath();
        }

        /// <summary>
        /// Gets the file path for storing weight and calorie records.
        /// Ensures that the directory exists before returning the path.
        /// </summary>
        /// <returns>The full file path of the data storage file.</returns>
        private string GetFilePath()
        {
            string localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string directoryPath = Path.Combine(localAppDataPath, "WeightCalorieMAUI");
            string filePath = Path.Combine(directoryPath, "WeightCalorie.txt");

            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            return filePath;
        }

        /// <summary>
        /// Loads weight and calorie records asynchronously from the storage file.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation. The task result contains a list of <see cref="WeightCalorieData"/>.</returns>
        public async Task<List<WeightCalorieData>> LoadDataAsync()
        {
            var dataList = new List<WeightCalorieData>();

            if (!File.Exists(_filePath))
            {
                return dataList;
            }

            using (StreamReader sr = new StreamReader(_filePath))
            {
                while (!sr.EndOfStream)
                {
                    var line = await sr.ReadLineAsync();
                    var parts = line.Split(',');

                    if (parts.Length == 3)
                    {
                        dataList.Add(new WeightCalorieData { Date = parts[0], Weight = parts[1], Calorie = parts[2] });
                    }
                }
            }

            return dataList;
        }

        /// <summary>
        /// Saves a new weight and calorie record to the storage file.
        /// </summary>
        /// <param name="data">The weight and calorie data to save.</param>
        public void SaveRecord(WeightCalorieData data)
        {
            using (StreamWriter sw = new StreamWriter(_filePath, append: true))
            {
                sw.WriteLine($"{data.Date},{data.Weight},{data.Calorie}");
            }
        }

        /// <summary>
        /// Updates an existing record in the storage file with new weight and calorie values.
        /// </summary>
        /// <param name="date">The date of the record to update.</param>
        /// <param name="newWeight">The new weight value.</param>
        /// <param name="newCalories">The new calorie value.</param>
        public void UpdateRecord(string date, string newWeight, string newCalories)
        {
            var allLines = File.ReadAllLines(_filePath).ToList();

            for (int i = 0; i < allLines.Count; i++)
            {
                if (allLines[i].StartsWith(date + ","))
                {
                    allLines[i] = $"{date},{newWeight},{newCalories}";
                    break;
                }
            }

            File.WriteAllLines(_filePath, allLines);
        }

        /// <summary>
        /// Deletes a record from the storage file based on the date.
        /// </summary>
        /// <param name="date">The date of the record to delete.</param>
        public void DeleteRecord(string date)
        {
            var allLines = File.ReadAllLines(_filePath).ToList();
            allLines.RemoveAll(line => line.StartsWith(date));
            File.WriteAllLines(_filePath, allLines);
        }
    }
}
