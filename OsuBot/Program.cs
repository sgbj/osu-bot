using System.Diagnostics;
using System.Runtime.InteropServices;

var path = @"C:\Users\scott\AppData\Local\osu!\Songs\33651 Hatsune Miku - Rubik's Cube\Hatsune Miku - Rubik's Cube (rui) [7x7x7].osu";

var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

var sliderMultiplierLine = lines.First(l => l.StartsWith("SliderMultiplier"));
var sliderMultiplier = double.Parse(sliderMultiplierLine.Split(":")[1]);

var timingPointsStartIndex = Array.FindIndex(lines, 0, l => l.StartsWith("[TimingPoints]"));
var timingPointsEndIndex = Array.FindIndex(lines, timingPointsStartIndex + 1, l => l.StartsWith("["));

var hitObjectsStartIndex = Array.FindIndex(lines, 0, l => l.StartsWith("[HitObjects]"));

var timingPointLines = lines[(timingPointsStartIndex + 1)..timingPointsEndIndex];
var hitObjectLines = lines[(hitObjectsStartIndex + 1)..];

bool zoom = true;
bool isHardRock = false;

var timingPoints = timingPointLines.Select(l => l.Split(',')).Select(split => new TimingPoint
(
    zoom ? (int)(int.Parse(split[0]) / 3.0 * 2.0) : int.Parse(split[0]),
    double.Parse(split[1]),
    int.Parse(split[2]),
    int.Parse(split[3]),
    int.Parse(split[4]),
    int.Parse(split[5]),
    split[6] == "1",
    int.Parse(split[7])
)).ToList();

var hitObjects = hitObjectLines.Select(l => l.Split(',')).Select(split => new HitObject
(
    int.Parse(split[0]),
    int.Parse(split[1]),
    zoom ? (int)(int.Parse(split[2]) / 3.0 * 2.0) : int.Parse(split[2]),
    int.Parse(split[3]),
    int.Parse(split[4]),
    split[5..]
)).ToList();

const int MOUSEEVENTF_LEFTDOWN = 0x0002;
const int MOUSEEVENTF_LEFTUP = 0x0004;
const int VK_CAPSLOCK = 0x14;

Console.WriteLine("Waiting for caps lock");
while (true)
{
    if (GetKeyState(VK_CAPSLOCK) != 0)
    {
        Console.WriteLine("Starting");

        var beatmapWidth = 512d;
        var beatmapHeight = 384d;

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        var msOffset = hitObjects.FirstOrDefault()?.Time ?? 0;

        var first = true;

        foreach (var hitObject in hitObjects)
        {
            var delay = hitObject.Time - (stopwatch.ElapsedMilliseconds + msOffset);

            if (first)
            {
                first = false;
            }
            else if (delay > 0)
            {
                Thread.Sleep((int)delay);
            }

            if (GetKeyState(VK_CAPSLOCK) == 0)
            {
                Console.WriteLine("Stopping");
                break;
            }

            switch (hitObject.HitObjectType)
            {
                case HitObjectType.HitCircle:
                    MoveCursor((hitObject.X, hitObject.Y));
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                    break;

                case HitObjectType.Spinner:
                    var cx = beatmapWidth / 2;
                    var cy = beatmapHeight / 2;
                    var spinnerRadius = 25;
                    var spinnerDelta = 2 * Math.PI / 8;
                    var spinnerEndTime = int.Parse(hitObject.ObjectParams[0]);

                    if (zoom)
                    {
                        spinnerEndTime = (int)(spinnerEndTime / 3.0 * 2.0);
                    }

                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                    for (var spinnerAngle = 0d; (stopwatch.ElapsedMilliseconds + msOffset) <= spinnerEndTime; spinnerAngle += spinnerDelta)
                    {
                        var x = cx + spinnerRadius * Math.Cos(spinnerAngle);
                        var y = cy + spinnerRadius * Math.Sin(spinnerAngle);
                        MoveCursor(((int)x, (int)y));
                        Thread.Sleep(5);
                    }
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                    break;

                case HitObjectType.Slider:
                    MoveCursor((hitObject.X, hitObject.Y));
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);

                    var curveParams = hitObject.ObjectParams[0].Split('|');
                    var slides = int.Parse(hitObject.ObjectParams[1]);
                    var length = double.Parse(hitObject.ObjectParams[2]);

                    var curveType = curveParams[0];
                    var curvePoints = curveParams[1..]
                        .Select(s => s.Split(':'))
                        .Select(s => (X: int.Parse(s[0]), Y: int.Parse(s[1])))
                        .ToList();

                    var lastUninheritedTimingPoint = timingPoints.Last(t => t.Time <= hitObject.Time && t.Uninherited);
                    var lastTimingPoint = timingPoints.Last(t => t.Time <= hitObject.Time);

                    var beatLength = lastUninheritedTimingPoint.BeatLength;

                    var newSliderMultiplier = sliderMultiplier;

                    if (!lastTimingPoint.Uninherited)
                    {
                        newSliderMultiplier -= sliderMultiplier * lastTimingPoint.BeatLength / 100;
                    }

                    var slideTime = length / (newSliderMultiplier * 100) * lastUninheritedTimingPoint.BeatLength + 25;

                    var points = curveType switch
                    {
                        //"L" => BresenhamLine(hitObject.X, hitObject.Y, curvePoints[0].X, curvePoints[0].Y),
                        _ => DeCasteljau(new[] { (hitObject.X, hitObject.Y) }.Concat(curvePoints).ToList(), (int)length)
                    };

                    for (var i = 0; i < slides && points.Any(); i++)
                    {
                        var endTime = hitObject.Time + slideTime * (i + 1);
                        var elapsed = (stopwatch.ElapsedMilliseconds + msOffset);

                        while (elapsed <= endTime)
                        {
                            var index = (int)((slideTime - (endTime - elapsed)) / slideTime * points.Count);

                            if (index < 0 || index >= points.Count)
                            {
                                break;
                            }

                            var point = points[index];
                            MoveCursor(point);

                            elapsed = (stopwatch.ElapsedMilliseconds + msOffset);
                        }

                        points.Reverse();
                    }


                    if (points.Any())
                    {
                        var lastPoint = points.FirstOrDefault();
                        MoveCursor(lastPoint);
                    }

                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                    break;
            }
        }
    }
    Thread.Yield();
}

int MoveCursor((int X, int Y) point)
{
    var beatmapWidth = 512d;
    var beatmapHeight = 384d;

    var screenWidth = 1000d;
    var screenHeight = 700d;

    var screenOffsetLeft = 460d;
    var screenOffsetTop = 190d;

    var y = (int)(screenOffsetTop + point.Y / beatmapHeight * screenHeight);

    if (isHardRock)
    {
        y = (int)(screenHeight - point.Y / beatmapHeight * screenHeight + screenOffsetTop);
    }

    return SetCursorPos((int)(screenOffsetLeft + point.X / beatmapWidth * screenWidth), y);
}

[DllImport("user32")]
static extern int SetCursorPos(int x, int y);

[DllImport("user32.dll")]
static extern void mouse_event(int dwFlags, int dx, int dy, int cButtons, int dwExtraInfo);

[DllImport("user32.dll")]
static extern short GetKeyState(int keyCode);

// https://pomax.github.io/bezierinfo/#decasteljau
static List<(int X, int Y)> DeCasteljau(List<(int X, int Y)> curvePoints, int iterations)
{
    (int X, int Y) DrawCurve(List<(double X, double Y)> points, double t)
    {
        if (points.Count == 1)
        {
            return ((int)points[0].X, (int)points[0].Y);
        }
        else
        {
            var newPoints = new List<(double X, double Y)>();
            for (var i = 0; i < points.Count - 1; i++)
            {
                var x = (1 - t) * points[i].X + t * points[i + 1].X;
                var y = (1 - t) * points[i].Y + t * points[i + 1].Y;
                newPoints.Add((x, y));
            }
            return DrawCurve(newPoints, t);
        }
    }

    var doublePoints = curvePoints.Select(p => ((double)p.X, (double)p.Y)).ToList();
    var points = new List<(int X, int Y)>();
    for (var i = 0d; i < 1; i += 1d / iterations)
    {
        points.Add(DrawCurve(doublePoints, i));
    }
    return points;
}

// https://en.wikipedia.org/wiki/Bresenham%27s_line_algorithm
static List<(int X, int Y)> BresenhamLine(int x0, int y0, int x1, int y1)
{
    var points = new List<(int X, int Y)>();
    var dx = Math.Abs(x1 - x0);
    var sx = x0 < x1 ? 1 : -1;
    var dy = -Math.Abs(y1 - y0);
    var sy = y0 < y1 ? 1 : -1;
    var err = dx + dy;
    while (true)
    {
        points.Add((x0, y0));
        if (x0 == x1 && y0 == y1)
        {
            break;
        }
        var e2 = 2 * err;
        if (e2 >= dy)
        {
            err += dy;
            x0 += sx;
        }
        if (e2 <= dx)
        {
            err += dx;
            y0 += sy;
        }
    }
    return points;
}

record TimingPoint(int Time, double BeatLength, int Meter, int SampleSet, int SampleIndex, int Volume, bool Uninherited, int Effects);

enum HitObjectType
{
    HitCircle,
    Slider,
    Spinner
}

record HitObject(int X, int Y, int Time, int Type, int HitSound, string[] ObjectParams)
{
    public HitObjectType HitObjectType => (Type & 0b1011) switch
    {
        0b1 => HitObjectType.HitCircle,
        0b10 => HitObjectType.Slider,
        0b1000 => HitObjectType.Spinner,
        _ => throw new InvalidOperationException($"Unsupported hit object type: {Type}")
    };
}
