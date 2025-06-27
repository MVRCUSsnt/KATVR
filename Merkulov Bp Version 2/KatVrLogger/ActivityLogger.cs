using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.InteropServices;

namespace Merkulov_Bp_Version_2.KatVrLogger;

// Структура для маршаллинга extraData (аналог WalkC2ExtraData.extraInfo)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ExtraInfo
{
    [MarshalAs(UnmanagedType.U1)]
    public bool isLeftGround;
    [MarshalAs(UnmanagedType.U1)]
    public bool isRightGround;
    [MarshalAs(UnmanagedType.U1)]
    public bool isLeftStatic;
    [MarshalAs(UnmanagedType.U1)]
    public bool isRightStatic;

    public int motionType;

    public Vector3 skatingSpeed;
    public Vector3 lFootSpeed;
    public Vector3 rFootSpeed;
}

public class ActivityLogger
{
    // Пользовательские параметры
    public float WeightKg { get; set; } = 75f;
    public float HeightCm { get; set; } = 167f;
    public int Age { get; set; } = 22;
    public bool IsMale { get; set; } = true;

    public float SpeedScale { get; set; } = 0.485f;
    public float StrideLength { get; set; } = 0.65f;

    private List<string> logLines = new List<string>();
    private string logFilePath;
    private string summaryFilePath;
    private float logInterval = 0.1f;
    private bool running = false;
    private Thread logThread;

    // Счётчики
    private float distanceTraveled = 0f;
    private int stepCount = 0;
    private float totalCalories = 0f;
    private DateTime sessionStartTime;
    private float elapsedTime = 0f;
    private float lastLogTime = 0f;

    private Vector3 currentPosition = new Vector3();

    // Для детекции прыжков/шагов
    private bool lastLeftGround = false;
    private bool lastRightGround = false;
    private float airborneTimer = 0f;
    private bool wasBothFeetOff = false;
    private string lastEvent = "";
    private int jumpCount = 0;
    public int JumpCount => jumpCount;

    // Для калорий по пульсу
    private float? averageHR = null;  // nullable! Только если введён
    public float? AverageHR
    {
        get => averageHR;
        set => averageHR = (value.HasValue && value.Value > 0) ? value : null;
    }
    private float? caloriesByHR = null;
    public float? CaloriesByHR => caloriesByHR;

    // --- Публичные свойства для UI ---
    public DateTime SessionStartTime => sessionStartTime;
    public float TotalCalories => totalCalories;
    public float ElapsedTime => elapsedTime;
    public float AvgSpeedKmh => (elapsedTime > 0f) ? (distanceTraveled / elapsedTime) * 3.6f : 0f;
    public float DistanceTraveled => distanceTraveled;
    public int StepCount => stepCount;
    public float StrideDistance => stepCount * StrideLength;
    public string ReadableDuration => TimeSpan.FromSeconds(elapsedTime).ToString(@"mm\:ss");
    public string LastEvent => lastEvent;

    public ActivityLogger()
    {
        var logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        if (!Directory.Exists(logsDir))
            Directory.CreateDirectory(logsDir);

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        logFilePath = Path.Combine(logsDir, $"kat_log_v2_current.csv");
        summaryFilePath = Path.Combine(logsDir, "kat_summary_v2.csv");

        logLines.Add("Time(s);PosX;PosY;PosZ;SpeedX;SpeedY;SpeedZ;Yaw;CaloriesBurned;Distance(m);Steps;Event;RawSpeed;ActivityMultiplier;BMR");
    }

    // Маршаллим extraData в структуру ExtraInfo
    public static ExtraInfo ParseExtraInfo(byte[] extraData)
    {
        GCHandle handle = GCHandle.Alloc(extraData, GCHandleType.Pinned);
        try
        {
            return (ExtraInfo)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(ExtraInfo));
        }
        finally
        {
            handle.Free();
        }
    }

    public void StartLogging()
    {
        sessionStartTime = DateTime.Now;
        running = true;
        logThread = new Thread(LogLoop);
        logThread.IsBackground = true;
        logThread.Start();
        Console.WriteLine("Logger started...");
    }

    public void StopLogging()
    {
        running = false;
        logThread?.Join();
        SaveLog();
        RecalcCaloriesByHR(); // пересчёт калорий по пульсу (если введён)
        AppendSessionSummary();
        Console.WriteLine("Logger stopped. Data has been saved.");
        
    }

    private void LogLoop()
    {
        lastLeftGround = false;
        lastRightGround = false;
        airborneTimer = 0f;
        wasBothFeetOff = false;
        lastEvent = "";
        lastLogTime = 0f;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        while (running)
        {
            float now = (float)sw.Elapsed.TotalSeconds;
            if (now - lastLogTime < logInterval)
            {
                Thread.Sleep(10);
                continue;
            }
            lastLogTime = now;

            try
            {
                var data = KatVrSdkInterop.GetWalkStatus(""); // "" = autodetect, иначе serial number
                if (!data.connected) continue;

                // Разбираем extraData!
                var extra = ParseExtraInfo(data.extraData);

                // Скейлинг скорости и позиции
                Vector3 scaledMove = new Vector3
                {
                    x = data.moveSpeed.x * SpeedScale,
                    y = data.moveSpeed.y * SpeedScale,
                    z = data.moveSpeed.z * SpeedScale
                };
                currentPosition.x += scaledMove.x * logInterval;
                currentPosition.y += scaledMove.y * logInterval;
                currentPosition.z += scaledMove.z * logInterval;

                float yaw = data.bodyRotationRaw.y;
                float rawSpeed = data.moveSpeed.Magnitude;
                float scaledSpeed = scaledMove.Magnitude;

                // Event-тег: прыжки, шаги, crouch
                string eventTag = "";

                // Детекция прыжка (аналог Unity)
                bool isJumpingNow = !extra.isLeftGround && !extra.isRightGround;

                if (isJumpingNow)
                {
                    airborneTimer += logInterval;
                    if (!wasBothFeetOff && airborneTimer >= 0.2f)
                    {
                        eventTag = "Jump";
                        wasBothFeetOff = true;
                        jumpCount++;
                    }
                }
                else
                {
                    airborneTimer = 0f;
                    wasBothFeetOff = false;
                }

                // Шагомер (аналог Unity) — только если оба датчика валидны!
                if (extra.isLeftGround && !lastLeftGround && extra.isRightGround) stepCount++;
                if (extra.isRightGround && !lastRightGround && extra.isLeftGround) stepCount++;

                lastLeftGround = extra.isLeftGround;
                lastRightGround = extra.isRightGround;

                lastEvent = eventTag;

                float bmr = CalculateBMR();
                float activityMultiplier = 1.4f;
                if (eventTag == "Jump") activityMultiplier = 2.2f;
                else if (scaledSpeed > 0.3f) activityMultiplier = 2.0f;

                float calories = (bmr / 86400f) * activityMultiplier * logInterval;
                totalCalories += calories;

                float distanceDelta = scaledSpeed * logInterval;
                distanceTraveled += distanceDelta;

                elapsedTime = now;

            }
            catch (Exception ex)
            {
                Console.WriteLine("Error logging: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Вызывать после изменения HR или завершения сессии.
    /// </summary>
    public void RecalcCaloriesByHR()
    {
        if (AverageHR.HasValue && AverageHR.Value > 0)
        {
            float durationMin = elapsedTime / 60f;
            caloriesByHR = CalculateCaloriesByHR(AverageHR.Value, durationMin);
        }
        else
        {
            caloriesByHR = null;
        }
    }

    /// <summary>
    /// Формула подсчёта калорий по пульсу, корректно для мужчин и женщин.
    /// </summary>
    public float CalculateCaloriesByHR(float hr, float durationMinutes)
    {
        double kpm;
        if (IsMale)
        {
            kpm = (-55.0969 + 0.6309 * hr + 0.1988 * WeightKg + 0.2017 * Age) / 4.184;
        }
        else
        {
            kpm = (-20.4022 + 0.4472 * hr - 0.1263 * WeightKg + 0.074 * Age) / 4.184;
        }
        return (float)(kpm * durationMinutes);
    }

    public void SaveLog()
    {
        lock (logLines)
        {
            File.WriteAllLines(logFilePath, logLines);
        }
    }

    public void AppendSessionSummary()
    {
        float avgSpeedMps = (elapsedTime > 0f) ? distanceTraveled / elapsedTime : 0f;
        float avgSpeedKmh = avgSpeedMps * 3.6f;

        string sessionDate = sessionStartTime.ToString("yyyy-MM-dd HH:mm:ss");
        string readableDuration = TimeSpan.FromSeconds(elapsedTime).ToString(@"mm\:ss");

        // В summary добавляем CaloriesByHR — если HR не был введён, пусто
        string calHRstring = caloriesByHR.HasValue
            ? caloriesByHR.Value.ToString("0.00", CultureInfo.InvariantCulture)
            : "";

        string summaryLine = string.Format(CultureInfo.InvariantCulture,
            "{0};{1:0.00};{2:0.00};{3};{4:00.00};{5:0.00};{6};{7:0.00};{8};{9}",
            sessionDate, totalCalories, elapsedTime, readableDuration, avgSpeedKmh, distanceTraveled, stepCount, stepCount * StrideLength, jumpCount, calHRstring);

        bool addHeader = !File.Exists(summaryFilePath);

        using (var writer = new StreamWriter(summaryFilePath, true))
        {
            if (addHeader)
                writer.WriteLine("DateTime;TotalCalories;Duration(s);ReadableDuration;AvgSpeed(km/h);Distance(m);Steps;StrideDistance(m);jumpCount;CaloriesByHR");
            writer.WriteLine(summaryLine);
        }
    }

    /// <summary>
    /// Обновляет последнюю строку kat_summary_v2.csv при пересчёте калорий по пульсу,
    /// всегда добавляет заголовок если его нет.
    /// </summary>
    public void UpdateLastSessionCaloriesByHR(float? newCaloriesByHR)
    {
        if (!File.Exists(summaryFilePath))
        {
            
            using (var writer = new StreamWriter(summaryFilePath, false))
            {
                writer.WriteLine("DateTime;TotalCalories;Duration(s);ReadableDuration;AvgSpeed(km/h);Distance(m);Steps;StrideDistance(m);jumpCount;CaloriesByHR");
            }
            return;
        }
        var lines = File.ReadAllLines(summaryFilePath);
        if (lines.Length < 2) return; // Нет данных

        int lastIndex = lines.Length - 1;
        var cols = lines[lastIndex].Split(';');
        if (cols.Length >= 10)
        {
            cols[9] = newCaloriesByHR.HasValue
                ? newCaloriesByHR.Value.ToString("0.00", CultureInfo.InvariantCulture)
                : "";
            lines[lastIndex] = string.Join(";", cols);
            File.WriteAllLines(summaryFilePath, lines);
        }
    }

    private float CalculateBMR()
    {
        if (IsMale)
            return 88.362f + (13.397f * WeightKg) + (4.799f * HeightCm) - (5.677f * Age);
        else
            return 447.593f + (9.247f * WeightKg) + (3.098f * HeightCm) - (4.330f * Age);
    }
    
    public void ResetSession()
    {
       
        distanceTraveled = 0f;
        stepCount = 0;
        totalCalories = 0f;
        elapsedTime = 0f;
        lastLogTime = 0f;
        currentPosition = new Vector3();
        jumpCount = 0;
        lastLeftGround = false;
        lastRightGround = false;
        airborneTimer = 0f;
        wasBothFeetOff = false;
        lastEvent = "";
        averageHR = null;
        caloriesByHR = null;

        // логически новая сессия
        sessionStartTime = DateTime.Now;
    }

}
