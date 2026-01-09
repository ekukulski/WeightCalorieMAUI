using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Maui.Controls;

namespace WeightCalorieMAUI
{
    public partial class ChartPage : ContentPage
    {
        // Bound from XAML
        public ISeries[] Series { get; private set; } = Array.Empty<ISeries>();
        public Axis[] XAxes { get; private set; } = Array.Empty<Axis>();
        public Axis[] YAxes { get; private set; } = Array.Empty<Axis>();

        private readonly IReadOnlyList<WeightCalorieData> _rawData;

        public ChartPage(IReadOnlyList<WeightCalorieData> data)
        {
            InitializeComponent();
            BindingContext = this;

            _rawData = data ?? Array.Empty<WeightCalorieData>();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            try
            {
                // Build chart safely on appearance (avoids constructor-time issues)
                var points = ExtractValidPoints(_rawData);

                if (points.Count == 0)
                {
                    await DisplayAlert("No chart data", "No valid weight/date records were found to chart.", "OK");
                    await SafeGoBackAsync();
                    return;
                }

                BuildChart(points);

                // Refresh bindings (safe even if already bound)
                OnPropertyChanged(nameof(Series));
                OnPropertyChanged(nameof(XAxes));
                OnPropertyChanged(nameof(YAxes));
            }
            catch (Exception ex)
            {
                // Never crash the app because charting failed
                await DisplayAlert("Chart error", ex.Message, "OK");
                await SafeGoBackAsync();
            }
        }

        private static List<(DateTime Date, double Weight)> ExtractValidPoints(IEnumerable<WeightCalorieData> data)
        {
            var results = new List<(DateTime Date, double Weight)>();

            foreach (var d in data ?? Enumerable.Empty<WeightCalorieData>())
            {
                if (d == null) continue;

                if (!TryParseDate(d.Date, out var dt)) continue;
                if (!TryParseDouble(d.Weight, out var weight)) continue;

                // filter out obviously bad values (optional; adjust as you like)
                if (double.IsNaN(weight) || double.IsInfinity(weight)) continue;

                results.Add((dt.Date, weight));
            }

            // sort by date and collapse duplicates (same day) by taking the latest entry
            return results
                .OrderBy(p => p.Date)
                .GroupBy(p => p.Date)
                .Select(g => g.Last())
                .ToList();
        }

        private void BuildChart(List<(DateTime Date, double Weight)> points)
        {
            // LiveCharts maps points by index for a simple category X axis
            var weights = points.Select(p => p.Weight).ToArray();
            var labels = points.Select(p => p.Date.ToString("MM/dd/yyyy")).ToArray();

            var seriesList = new List<ISeries>
            {
                new LineSeries<double>
                {
                    Name = "Weight",
                    Values = weights,
                    GeometrySize = 6
                }
            };

            // Add trendline only when we have at least 2 points
            if (points.Count >= 2)
            {
                var trend = CalculateTrendLine(points);
                if (trend.Length == weights.Length && trend.All(v => !double.IsNaN(v) && !double.IsInfinity(v)))
                {
                    seriesList.Add(new LineSeries<double>
                    {
                        Name = "Trend",
                        Values = trend,
                        GeometrySize = 0 // no points, just a line
                    });
                }
            }

            Series = seriesList.ToArray();

            XAxes = new[]
            {
                new Axis
                {
                    Labels = labels,
                    LabelsRotation = 15
                }
            };

            YAxes = new[]
            {
                new Axis
                {
                    Name = "Weight"
                }
            };
        }

        private static double[] CalculateTrendLine(List<(DateTime Date, double Weight)> points)
        {
            int n = points.Count;
            if (n == 0) return Array.Empty<double>();
            if (n == 1) return new[] { points[0].Weight }; // avoid divide-by-zero

            // Simple linear regression where X is the index (0..n-1)
            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;

            for (int i = 0; i < n; i++)
            {
                double x = i;
                double y = points[i].Weight;

                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumX2 += x * x;
            }

            double denom = (n * sumX2) - (sumX * sumX);
            if (denom == 0)
                return points.Select(p => p.Weight).ToArray();

            double slope = ((n * sumXY) - (sumX * sumY)) / denom;
            double intercept = (sumY - (slope * sumX)) / n;

            return Enumerable.Range(0, n)
                .Select(i => (slope * i) + intercept)
                .ToArray();
        }

        private static bool TryParseDate(string? s, out DateTime dt)
        {
            dt = default;

            if (string.IsNullOrWhiteSpace(s))
                return false;

            // Try common formats first (fast + predictable)
            string[] formats =
            {
                "M/d/yyyy", "MM/dd/yyyy",
                "M/d/yy",   "MM/dd/yy",
                "yyyy-MM-dd",
                "yyyy/M/d", "yyyy/MM/dd"
            };

            if (DateTime.TryParseExact(s.Trim(), formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces, out dt))
                return true;

            // Fallback: current culture / general parse
            if (DateTime.TryParse(s.Trim(), CultureInfo.CurrentCulture,
                    DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces, out dt))
                return true;

            if (DateTime.TryParse(s.Trim(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces, out dt))
                return true;

            return false;
        }

        private static bool TryParseDouble(string? s, out double value)
        {
            value = 0;

            if (string.IsNullOrWhiteSpace(s))
                return false;

            var text = s.Trim();

            // Try current culture first (user input usually matches it)
            if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.CurrentCulture, out value))
                return true;

            // Fallback: invariant
            if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture, out value))
                return true;

            return false;
        }

        private async Task SafeGoBackAsync()
        {
            try
            {
                if (Navigation?.NavigationStack?.Count > 1)
                {
                    await Navigation.PopAsync();
                    return;
                }
            }
            catch
            {
                // ignore and try Shell route below
            }

            try
            {
                if (Shell.Current != null)
                    await Shell.Current.GoToAsync("..");
            }
            catch
            {
                // last resort: do nothing (avoid crash)
            }
        }

        private async void OnReturnClicked(object sender, EventArgs e)
        {
            await SafeGoBackAsync();
        }
    }
}
