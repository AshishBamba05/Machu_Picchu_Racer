using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public static class Parse
{
    public static List<Vector3> LoadTrack(string filePath, int maxPoints)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Track file not found.", filePath);
        }

        var points = new List<Vector3>();
        var lines = File.ReadAllLines(filePath);

        for (var index = 0; index < lines.Length; index++)
        {
            var trimmedLine = lines[index].Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                continue;
            }

            var parts = trimmedLine.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
            {
                throw new FormatException($"Line {index + 1} must contain exactly three coordinates.");
            }

            points.Add(new Vector3(
                ParseFloat(parts[0], index),
                ParseFloat(parts[1], index),
                ParseFloat(parts[2], index)));

            if (points.Count > maxPoints)
            {
                throw new InvalidOperationException($"Track exceeds the maximum of {maxPoints} checkpoints.");
            }
        }

        if (points.Count < 2)
        {
            throw new InvalidOperationException("Track files must contain at least two checkpoints.");
        }

        return points;
    }

    private static float ParseFloat(string value, int lineIndex)
    {
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue))
        {
            throw new FormatException($"Invalid floating-point value '{value}' on line {lineIndex + 1}.");
        }

        return parsedValue;
    }
}
