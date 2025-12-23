using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

#if WINDOWS
using Windows.Storage; // ApplicationData
#endif

namespace WeightCalorieMAUI
{
    /// <summary>
    /// Provides data storage and retrieval functionality for weight and calorie records.
    /// </summary>
    public class DataService
    {
        private readonly string _filePath;
        private readonly string _exportFolderPath;
        private readonly string _importFolderPath;
        private readonly string _archiveFolderPath;
        private readonly string _backupFolderPath;

        private const string ExportFolderName = "WeightCalorieMAUI";
        private const string DatabaseFileName = "WeightCalorie.txt";

        /// <summary>
        /// Initializes a new instance of the <see cref="DataService"/> class.
        /// </summary>
        public DataService()
        {
            // Ensure we use a persistent path on Windows packaged builds (LocalState),
            // and migrate any legacy DB that may have been written to MAUI AppDataDirectory (which can map to LocalCache).
            MigrateLegacyDatabaseIfNeeded();

            _filePath = GetDatabaseFilePath();

            var basePath = GetOneDriveBasePath();
            _exportFolderPath = Path.Combine(basePath, "Exports");
            _importFolderPath = Path.Combine(basePath, "Imports");
            _archiveFolderPath = Path.Combine(basePath, "Archive");

            // Keep backups in the same persistent root as the live DB (LocalState on Windows packaged apps).
            _backupFolderPath = Path.Combine(GetPersistentAppDataRoot(), "Backups");

            EnsureDirectoriesExist();
        }

        /// <summary>
        /// Returns the persistent app data root.
        /// Windows packaged apps: ...\AppData\Local\Packages\<PackageFamilyName>\LocalState\
        /// Other platforms: MAUI AppDataDirectory.
        /// </summary>
        private static string GetPersistentAppDataRoot()
        {
#if WINDOWS
            // LocalFolder maps to LocalState for packaged apps (MSIX) and is persistent.
            return ApplicationData.Current.LocalFolder.Path;
#else
            return FileSystem.AppDataDirectory;
#endif
        }

        /// <summary>
        /// Gets the database file path in the persistent app data root.
        /// Ensures that the directory exists before returning the path.
        /// </summary>
        private static string GetDatabaseFilePath()
        {
            string directoryPath = GetPersistentAppDataRoot();
            Directory.CreateDirectory(directoryPath);
            return Path.Combine(directoryPath, DatabaseFileName);
        }

        /// <summary>
        /// If a previous version stored the database under MAUI AppDataDirectory (which may map to LocalCache on Windows),
        /// migrate it into the persistent LocalState location (ApplicationData.Current.LocalFolder).
        /// This is safe and prevents "missing database" surprises after you change storage paths.
        /// </summary>
        private void MigrateLegacyDatabaseIfNeeded()
        {
#if WINDOWS
            try
            {
                string targetRoot = ApplicationData.Current.LocalFolder.Path; // LocalState
                Directory.CreateDirectory(targetRoot);

                string targetPath = Path.Combine(targetRoot, DatabaseFileName);

                // Legacy MAUI path (can map to LocalCache on some machines)
                string legacyRoot = FileSystem.AppDataDirectory;
                string legacyPath = Path.Combine(legacyRoot, DatabaseFileName);

                if (!File.Exists(targetPath) && File.Exists(legacyPath))
                {
                    File.Copy(legacyPath, targetPath, overwrite: true);

                    // If you prefer to keep the legacy copy as a safety net, comment out the delete.
                    File.Delete(legacyPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Migration check failed: {ex.Message}");
                // Non-fatal: app can still operate and create a new DB if needed.
            }
#endif
        }

        /// <summary>
        /// Gets the OneDrive base path in the user's Documents folder.
        /// </summary>
        /// <returns>The full path to the OneDrive folder structure.</returns>
        private string GetOneDriveBasePath()
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documentsPath, "AppData", ExportFolderName);
        }

        /// <summary>
        /// Ensures all required directories exist.
        /// </summary>
        private void EnsureDirectoriesExist()
        {
            Directory.CreateDirectory(_exportFolderPath);
            Directory.CreateDirectory(_importFolderPath);
            Directory.CreateDirectory(_archiveFolderPath);
            Directory.CreateDirectory(_backupFolderPath);
        }

        /// <summary>
        /// Exports the current database to OneDrive using atomic write operations.
        /// Creates a versioned export with .tmp -> .txt -> .ready pattern.
        /// </summary>
        public async Task ExportDatabaseAsync()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return; // Nothing to export
                }

                // Create timestamped filename
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
                string baseFileName = $"DB_{timestamp}";
                string tempFilePath = Path.Combine(_exportFolderPath, $"{baseFileName}.tmp");
                string finalFilePath = Path.Combine(_exportFolderPath, $"{baseFileName}.txt");
                string readyFilePath = Path.Combine(_exportFolderPath, $"{baseFileName}.ready");
                string latestFilePath = Path.Combine(_exportFolderPath, "LATEST.txt");

                // Phase 1: Write to .tmp file
                await Task.Run(() => File.Copy(_filePath, tempFilePath, overwrite: true));

                // Phase 2: Rename .tmp to .txt
                await Task.Run(() =>
                {
                    if (File.Exists(finalFilePath))
                    {
                        File.Delete(finalFilePath);
                    }
                    File.Move(tempFilePath, finalFilePath);
                });

                // Phase 3: Create .ready marker file (signals complete export)
                await Task.Run(() => File.WriteAllText(readyFilePath, timestamp));

                // Phase 4: Update LATEST.txt pointer
                await Task.Run(() => File.WriteAllText(latestFilePath, $"{baseFileName}.txt"));

                System.Diagnostics.Debug.WriteLine($"Export complete: {baseFileName}.txt");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Export failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Imports the latest database from OneDrive, backing up the current local database first.
        /// Uses .ready marker to ensure complete files only.
        /// </summary>
        /// <returns>True if import was successful.</returns>
        public async Task<bool> ImportDatabaseAsync()
        {
            try
            {
                // Determine which file to import
                string? fileToImport = await GetLatestReadyExportAsync();

                if (string.IsNullOrEmpty(fileToImport))
                {
                    System.Diagnostics.Debug.WriteLine("No ready export found to import.");
                    return false;
                }

                string sourceFilePath = Path.Combine(_exportFolderPath, fileToImport);

                // Wait for file to be stable (OneDrive sync safety)
                if (!await WaitForFileStabilityAsync(sourceFilePath))
                {
                    System.Diagnostics.Debug.WriteLine("File not stable after 60 seconds.");
                    return false;
                }

                // Backup current local database if it exists
                if (File.Exists(_filePath))
                {
                    string backupFileName = $"LocalBackup_{DateTime.Now:yyyy-MM-dd_HHmmss}.txt";
                    string backupFilePath = Path.Combine(_backupFolderPath, backupFileName);
                    await Task.Run(() => File.Copy(_filePath, backupFilePath, overwrite: true));
                }

                // Safe replacement using .importing.tmp pattern (store temp in persistent app data root)
                string persistentRoot = GetPersistentAppDataRoot();
                Directory.CreateDirectory(persistentRoot);

                string tempImportPath = Path.Combine(persistentRoot, "WeightCalorie.importing.tmp");

                await Task.Run(() =>
                {
                    // Copy import file to temp
                    File.Copy(sourceFilePath, tempImportPath, overwrite: true);

                    // Replace live database
                    if (File.Exists(_filePath))
                    {
                        string oldFilePath = Path.Combine(persistentRoot, "WeightCalorie.old");
                        if (File.Exists(oldFilePath))
                        {
                            File.Delete(oldFilePath);
                        }
                        File.Move(_filePath, oldFilePath);
                    }

                    File.Move(tempImportPath, _filePath);
                });

                System.Diagnostics.Debug.WriteLine($"Import successful: {fileToImport}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Import failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Finds the latest ready export file from OneDrive Exports folder.
        /// Prefers LATEST.txt pointer if valid; otherwise scans for newest .ready file.
        /// </summary>
        private async Task<string?> GetLatestReadyExportAsync()
        {
            // Option A: Use LATEST.txt pointer
            string latestPointer = Path.Combine(_exportFolderPath, "LATEST.txt");

            if (File.Exists(latestPointer))
            {
                string latestFileName = (await Task.Run(() => File.ReadAllText(latestPointer))).Trim();
                string readyFilePath = Path.Combine(_exportFolderPath, latestFileName.Replace(".txt", ".ready"));

                if (File.Exists(readyFilePath) && File.Exists(Path.Combine(_exportFolderPath, latestFileName)))
                {
                    return latestFileName;
                }
            }

            // Option B: Scan for newest .ready file
            var readyFiles = await Task.Run(() =>
                Directory.GetFiles(_exportFolderPath, "DB_*.ready")
                    .OrderByDescending(f => f)
                    .ToList()
            );

            if (readyFiles.Any())
            {
                string readyFile = readyFiles.First();
                string txtFile = readyFile.Replace(".ready", ".txt");

                if (File.Exists(txtFile))
                {
                    return Path.GetFileName(txtFile);
                }
            }

            return null;
        }

        /// <summary>
        /// Waits for a file to become stable (size unchanged) to ensure OneDrive sync is complete.
        /// </summary>
        /// <param name="filePath">The file path to monitor.</param>
        /// <returns>True if file is stable, false if timeout after 60 seconds.</returns>
        private async Task<bool> WaitForFileStabilityAsync(string filePath)
        {
            int maxRetries = 30; // 30 retries * 2 seconds = 60 seconds
            int retryCount = 0;

            while (retryCount < maxRetries)
            {
                if (!File.Exists(filePath))
                {
                    await Task.Delay(2000);
                    retryCount++;
                    continue;
                }

                long size1 = await Task.Run(() => new FileInfo(filePath).Length);
                await Task.Delay(500);
                long size2 = await Task.Run(() => new FileInfo(filePath).Length);

                if (size1 == size2 && size1 > 0)
                {
                    return true; // File is stable
                }

                await Task.Delay(2000);
                retryCount++;
            }

            return false;
        }

        /// <summary>
        /// Loads weight and calorie records asynchronously from the storage file.
        /// </summary>
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
                    if (line == null) continue;

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
        public void DeleteRecord(string date)
        {
            var allLines = File.ReadAllLines(_filePath).ToList();
            allLines.RemoveAll(line => line.StartsWith(date));
            File.WriteAllLines(_filePath, allLines);
        }
    }
}
