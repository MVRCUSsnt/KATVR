using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace Merkulov_Bp_Version_2.KatVrLogger
{
    public partial class MainWindow : Window
    {
        private ActivityLogger logger;
        private DispatcherTimer updateTimer;
        private List<SessionStat> allStats = new List<SessionStat>();
        private List<SessionStat> filteredStats = new List<SessionStat>();
        private string period = "Day";

        public MainWindow()
        {
            InitializeComponent();
            
            logger = new ActivityLogger();
             
            txtWeight.Text = logger.WeightKg.ToString("0");
            txtHeight.Text = logger.HeightCm.ToString("0");
            txtAge.Text = logger.Age.ToString();
            cbGender.SelectedIndex = logger.IsMale ? 0 : 1;
            
            updateTimer = new DispatcherTimer();
            updateTimer.Interval = TimeSpan.FromMilliseconds(500);
            updateTimer.Tick += UpdateUi;
            updateTimer.Start();

            // Загрузка статистики
            cbMetric.SelectedIndex = 0;
            cbPeriod.SelectedIndex = 0;
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "kat_summary_v2.csv");
            if (File.Exists(path))
            {
                allStats = SessionStat.LoadAll(path);
            }

            FilterStatsAndUpdateUI();
        }

        // ----------- ЛОГГЕР -------------
        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            logger.StartLogging();
            btnStart.IsEnabled = false;
            btnStop.IsEnabled = true;
            ellipseStatus.Fill = new SolidColorBrush(Colors.LimeGreen);
            lblStatus.Content = "Logger started. Data is being recorded..";
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            logger.StopLogging();
            btnStart.IsEnabled = true;
            btnStop.IsEnabled = false;
            ellipseStatus.Fill = new SolidColorBrush(Colors.Red);
            lblStatus.Content = "Logger stopped. Data has been saved.";
            logger.RecalcCaloriesByHR();
            logger.UpdateLastSessionCaloriesByHR(logger.CaloriesByHR);
            ReloadStatsAndPlot();
        }

        private void ReloadStatsAndPlot()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "kat_summary_v2.csv");
            if (File.Exists(path))
            {
                allStats = SessionStat.LoadAll(path);
                FilterStatsAndUpdateUI();
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (btnStop.IsEnabled)
                logger.StopLogging();
            base.OnClosing(e);
        }
        
        private void BtnApplyUserParams_Click(object sender, RoutedEventArgs e)
        {
            if (float.TryParse(txtWeight.Text, out float weight)) logger.WeightKg = weight;
            if (float.TryParse(txtHeight.Text, out float height)) logger.HeightCm = height;
            if (int.TryParse(txtAge.Text, out int age)) logger.Age = age;
            logger.IsMale = (cbGender.SelectedIndex == 0); // 0 = Male, 1 = Female
            lblStatus.Content = "User parameters updated!";
        }

        private void UpdateUi(object sender, EventArgs e)
        {
            txtSessionDate.Text = $"Date and Time: {logger.SessionStartTime}";
            txtCalories.Text = $"Calories (BMR): {logger.TotalCalories:F2}";
            txtCaloriesByHR.Text = logger.CaloriesByHR.HasValue
                ? $"Calories (by HR): {logger.CaloriesByHR.Value:F2}"
                : "Calories (by HR): —";
            txtDuration.Text = $"Duration: {logger.ReadableDuration} ({logger.ElapsedTime:F2} sec)";
            txtAvgSpeed.Text = $"Average Speed: {logger.AvgSpeedKmh:F2} km/h";
            txtDistance.Text = $"Distance: {logger.DistanceTraveled:F2} m";
            txtSteps.Text = $"Steps: {logger.StepCount}";
            txtJumpCount.Text = $"Jumps this session: {logger.JumpCount}";
        }

        private void BtnRecalcCalories_Click(object sender, RoutedEventArgs e)
        {
            if (float.TryParse(txtAvgHR.Text, out float hr) && hr > 0)
            {
                logger.AverageHR = hr;
                logger.RecalcCaloriesByHR();
                logger.UpdateLastSessionCaloriesByHR(logger.CaloriesByHR);
                txtCaloriesByHR.Text = $"Calories (by HR): {logger.CaloriesByHR.Value:F2}";
                ReloadStatsAndPlot();
            }
            else
            {
                logger.AverageHR = null;
                logger.RecalcCaloriesByHR();
                logger.UpdateLastSessionCaloriesByHR(null);
                txtCaloriesByHR.Text = "Calories (by HR): —";
            }
        }

        // ----------- ФИЛЬТР ПО ПЕРИОДУ -------------
        private void cbPeriod_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            period = ((System.Windows.Controls.ComboBoxItem)cbPeriod.SelectedItem).Content as string ?? "Day";
            FilterStatsAndUpdateUI();
        }

        private void FilterStatsAndUpdateUI()
        {
            DateTime now = DateTime.Now;
            switch (period)
            {
                case "Day":
                    filteredStats = allStats.Where(s => s.Date.Date == now.Date).ToList();
                    break;
                case "Week":
                    var weekStart = now.AddDays(-7);
                    filteredStats = allStats.Where(s => s.Date >= weekStart && s.Date <= now).ToList();
                    break;
                case "Month":
                    var monthStart = now.AddMonths(-1);
                    filteredStats = allStats.Where(s => s.Date >= monthStart && s.Date <= now).ToList();
                    break;
                case "Year":
                    var yearStart = now.AddYears(-1);
                    filteredStats = allStats.Where(s => s.Date >= yearStart && s.Date <= now).ToList();
                    break;
                case "All time":
                default:
                    filteredStats = allStats.ToList();
                    break;
            }
            dgStats.ItemsSource = null;
            dgStats.ItemsSource = filteredStats;
            UpdatePlot(filteredStats);

            // Вывод суммы итогов
            double totalCalories = filteredStats.Sum(s => s.Calories);
            double totalCaloriesHR = filteredStats.Sum(s => s.CaloriesByHR ?? 0);
            double totalDistance = filteredStats.Sum(s => s.Distance);
            int totalSteps = filteredStats.Sum(s => s.Steps);
            int totalJumps = filteredStats.Sum(s => s.Jumps);
            txtPeriodSummary.Text = $"Total for period: Calories (BMR): {totalCalories:F2}, Calories (by HR): {totalCaloriesHR:F2}, " +
                                   $"Distance: {totalDistance:F1} m, Steps: {totalSteps}, Jumps: {totalJumps}";
        }

        // ----------- СТАТИСТИКА -------------
        private void cbMetric_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdatePlot(filteredStats.Count > 0 ? filteredStats : allStats);
        }

        private void UpdatePlot(List<SessionStat> source)
        {
            var model = new PlotModel { Background = OxyColors.Transparent, TextColor = OxyColors.White };
            var xLabels = source.Count > 0 ? source.ConvertAll(s => s.Date.ToString("dd.MM HH:mm")) : new List<string>();

            var catAxis = new CategoryAxis
            {
                Position = AxisPosition.Bottom,
                ItemsSource = xLabels,
                Angle = 40,
                GapWidth = 0.5,
                TextColor = OxyColors.White,
                TicklineColor = OxyColors.White
            };
            model.Axes.Add(catAxis);

            var valAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                TextColor = OxyColors.White,
                AxislineColor = OxyColors.White,
                AxislineStyle = LineStyle.Solid,
                AxislineThickness = 1.5,
                MajorGridlineStyle = LineStyle.Dash,
                MinorGridlineStyle = LineStyle.Dot,
            };

            string selected = ((cbMetric.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string) ?? "Calories (BMR)";
            LineSeries series = new LineSeries
            {
                Color = OxyColors.Cyan,
                MarkerType = MarkerType.Circle,
                MarkerSize = 5,
                MarkerStroke = OxyColors.LightCyan,
                StrokeThickness = 2
            };

            model.Axes.Add(valAxis);

            for (int i = 0; i < source.Count; i++)
            {
                double value = 0;
                switch (selected)
                {
                    case "Calories (BMR)":
                        value = source[i].Calories;
                        model.Title = "Session Dynamics: Calories (BMR)";
                        valAxis.Title = "kcal";
                        break;
                    case "Calories (by HR)":
                        value = source[i].CaloriesByHR ?? double.NaN;
                        model.Title = "Session Dynamics: Calories by HR";
                        valAxis.Title = "kcal";
                        break;
                    case "Average Speed":
                        value = source[i].AvgSpeed;
                        model.Title = "Dynamics: Average Speed";
                        valAxis.Title = "km/h";
                        break;
                    case "Steps":
                        value = source[i].Steps;
                        model.Title = "Dynamics: Step Count";
                        valAxis.Title = "Steps";
                        break;
                    case "Jumps":
                        value = source[i].Jumps;
                        model.Title = "Dynamics: Jumps";
                        valAxis.Title = "Jumps";
                        break;
                }
                if (!double.IsNaN(value))
                    series.Points.Add(new DataPoint(i, value));
            }
            model.Series.Add(series);
            plotView.Model = model;
        }
        
        private void btnReset_Click(object sender, RoutedEventArgs e)
        {
            logger.ResetSession();
            
            txtSessionDate.Text = $"Date and Time: {logger.SessionStartTime}";
            txtCalories.Text = $"Calories (BMR): 0.00";
            txtCaloriesByHR.Text = "Calories (by HR): —";
            txtDuration.Text = $"Duration: 00:00 (0.00 sec)";
            txtAvgSpeed.Text = $"Average Speed: 0.00 km/h";
            txtDistance.Text = $"Distance: 0.00 m";
            txtSteps.Text = $"Steps: 0";
            txtJumpCount.Text = $"Jumps this session: 0";
            txtAvgHR.Text = ""; 
            lblStatus.Content = "Session reset. Data cleared on screen.";
        }


        // ---------- ВСПОМОГАТЕЛЬНЫЙ КЛАСС ДЛЯ СТАТИСТИКИ ----------
        public class SessionStat
        {
            public DateTime Date { get; set; }
            public double Calories { get; set; }
            public double? CaloriesByHR { get; set; }
            public string Duration { get; set; }
            public double AvgSpeed { get; set; }
            public double Distance { get; set; }
            public int Steps { get; set; }
            public int Jumps { get; set; }

            public static List<SessionStat> LoadAll(string csvPath)
            {
                var list = new List<SessionStat>();
                var lines = File.ReadAllLines(csvPath);
                if (lines.Length < 2) return list;
                for (int i = 1; i < lines.Length; i++)
                {
                    var s = lines[i].Split(';');
                    // Если не хватает колонок — добиваем пустыми
                    if (s.Length < 10)
                    {
                        var temp = s.ToList();
                        while (temp.Count < 10)
                            temp.Add("");
                        s = temp.ToArray();
                    }

                    var stat = new SessionStat();
                    DateTime dt;
                    if (DateTime.TryParse(s[0], out dt))
                        stat.Date = dt;
                    else
                        stat.Date = DateTime.MinValue;

                    double d;
                    stat.Calories = double.TryParse(s[1], NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? d : 0;
                    double durSec = double.TryParse(s[2], NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? d : 0;
                    string minSec = s[3];
                    // если поле пустое — ставим "00:00"
                    if (string.IsNullOrWhiteSpace(minSec))
                        minSec = "00:00";
                    stat.Duration = $"{minSec} ({durSec:0.##} sec)";
                    stat.AvgSpeed = double.TryParse(s[4], NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? d : 0;
                    stat.Distance = double.TryParse(s[5], NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? d : 0;
                    stat.Steps = int.TryParse(s[6], out int steps) ? steps : 0;
                    // StrideDistance(m) можно игнорировать, если не используешь, либо добавить как дополнительное поле

                    // JumpCount
                    int jumps = 0;
                    int.TryParse(s[8], out jumps); // s[8] — jumpCount, даже если пусто
                    stat.Jumps = jumps;

                    // CaloriesByHR
                    stat.CaloriesByHR = double.TryParse(s[9], NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? d : (double?)null;

                    list.Add(stat);
                }
                return list;
            }

        }
    }
}
