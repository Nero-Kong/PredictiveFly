using System;
using System.IO;
using System.Text;
using UnityEngine;

public static class PredictiveFlyExperiment2CalibrationStore
{
    public const string ProtocolVersion = "E2_5delay_preference_v1";
    public const string DefaultCalibrationOutputDirectory =
        "Data/PredictiveFlyExperiment2/Calibration";

    [Serializable]
    public sealed class CompletedCalibrationProfile
    {
        public string protocolVersion;
        public bool completed;
        public string participantId;
        public string completedAt;
        public string calibrationCsvFileName;
        public float selectedHorizon0Seconds = -1f;
        public float selectedHorizon250Seconds = -1f;
        public float selectedHorizon500Seconds = -1f;
        public float selectedHorizon750Seconds = -1f;
        public float selectedHorizon1000Seconds = -1f;
        public float minimumSelectableHorizonSeconds;
        public float maximumSelectableHorizonSeconds = 1f;
        public float horizonStepSeconds = 0.1f;
        public float referenceMinTranslationHorizonSeconds;
        public float referenceMaxTranslationHorizonSeconds;
        public float referenceMinYawHorizonSeconds;
        public float referenceMaxYawHorizonSeconds;
        public int predictionMethod;
    }

    public static bool TrySaveCompletedProfile(
        CompletedCalibrationProfile profile,
        string configuredDirectory,
        out string profilePath,
        out string message)
    {
        profilePath = string.Empty;
        if (!TryValidateProfileRecord(profile, profile?.participantId, configuredDirectory, out message))
        {
            return false;
        }

        profilePath = GetProfilePath(profile.participantId, configuredDirectory);
        string stagingPath = profilePath + ".writing";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(profilePath));
            File.WriteAllText(
                stagingPath,
                JsonUtility.ToJson(profile, true),
                new UTF8Encoding(false));
            if (File.Exists(profilePath))
            {
                File.Replace(stagingPath, profilePath, null);
            }
            else
            {
                File.Move(stagingPath, profilePath);
            }

            message = $"Completed calibration profile saved for {profile.participantId}.";
            return true;
        }
        catch (Exception exception)
        {
            TryDeleteFile(stagingPath);
            message = $"Completed calibration profile could not be saved: {exception.Message}";
            return false;
        }
    }

    public static bool TryLoadCompletedProfile(
        string participantId,
        string configuredDirectory,
        out CompletedCalibrationProfile profile,
        out string profilePath,
        out string message)
    {
        profile = null;
        profilePath = GetProfilePath(participantId, configuredDirectory);
        if (string.IsNullOrWhiteSpace(participantId))
        {
            message = "Participant ID is empty. Calibration has not been completed.";
            return false;
        }
        if (!File.Exists(profilePath))
        {
            message = $"Calibration incomplete for {participantId.Trim()}: completed profile not found.";
            return false;
        }

        try
        {
            profile = JsonUtility.FromJson<CompletedCalibrationProfile>(
                File.ReadAllText(profilePath, Encoding.UTF8));
        }
        catch (Exception exception)
        {
            message = $"Calibration incomplete for {participantId.Trim()}: profile could not be read ({exception.Message}).";
            profile = null;
            return false;
        }

        if (!TryValidateProfileRecord(profile, participantId, configuredDirectory, out message))
        {
            profile = null;
            return false;
        }

        message = $"Calibration ready for {participantId.Trim()}.";
        return true;
    }

    public static bool TryInvalidateCompletedProfile(
        string participantId,
        string configuredDirectory,
        out string message)
    {
        string path = GetProfilePath(participantId, configuredDirectory);
        if (!File.Exists(path))
        {
            message = "No previous completed profile existed.";
            return true;
        }

        try
        {
            File.Delete(path);
            message = "Previous completed profile was invalidated before recalibration.";
            return true;
        }
        catch (Exception exception)
        {
            message = $"Previous completed profile could not be invalidated: {exception.Message}";
            return false;
        }
    }

    public static string GetProfilePath(string participantId, string configuredDirectory)
    {
        string directory = ResolveDirectory(configuredDirectory);
        return Path.Combine(
            directory,
            $"{Sanitize(participantId)}_{ProtocolVersion}_completed.json");
    }

    public static string ResolveDirectory(string configuredDirectory)
    {
        string directory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? DefaultCalibrationOutputDirectory
            : configuredDirectory.Trim();
        if (Path.IsPathRooted(directory))
        {
            return Path.GetFullPath(directory);
        }

#if UNITY_EDITOR
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", directory));
#else
        return Path.Combine(Application.persistentDataPath, directory);
#endif
    }

    public static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "NA";
        }

        StringBuilder builder = new StringBuilder(value.Length);
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            builder.Append(Array.IndexOf(invalid, c) >= 0 || char.IsWhiteSpace(c) ? '_' : c);
        }
        return builder.ToString();
    }

    static bool TryValidateProfileRecord(
        CompletedCalibrationProfile profile,
        string requestedParticipantId,
        string configuredDirectory,
        out string message)
    {
        string requested = requestedParticipantId != null ? requestedParticipantId.Trim() : string.Empty;
        if (profile == null)
        {
            message = $"Calibration incomplete for {requested}: profile is empty.";
            return false;
        }
        if (!profile.completed)
        {
            message = $"Calibration incomplete for {requested}: completion flag is missing.";
            return false;
        }
        if (!string.Equals(profile.protocolVersion, ProtocolVersion, StringComparison.Ordinal))
        {
            message = $"Calibration incomplete for {requested}: protocol version does not match {ProtocolVersion}.";
            return false;
        }
        if (!string.Equals(profile.participantId?.Trim(), requested, StringComparison.OrdinalIgnoreCase))
        {
            message = $"Calibration incomplete for {requested}: profile belongs to another participant.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(profile.completedAt))
        {
            message = $"Calibration incomplete for {requested}: completion timestamp is missing.";
            return false;
        }
        if (float.IsNaN(profile.minimumSelectableHorizonSeconds)
            || float.IsInfinity(profile.minimumSelectableHorizonSeconds)
            || float.IsNaN(profile.maximumSelectableHorizonSeconds)
            || float.IsInfinity(profile.maximumSelectableHorizonSeconds)
            || profile.maximumSelectableHorizonSeconds
                <= profile.minimumSelectableHorizonSeconds
            || float.IsNaN(profile.horizonStepSeconds)
            || float.IsInfinity(profile.horizonStepSeconds)
            || profile.horizonStepSeconds <= 0f)
        {
            message = $"Calibration incomplete for {requested}: selection range is invalid.";
            return false;
        }
        if (!IsValidHorizon(profile.selectedHorizon0Seconds, profile)
            || !IsValidHorizon(profile.selectedHorizon250Seconds, profile)
            || !IsValidHorizon(profile.selectedHorizon500Seconds, profile)
            || !IsValidHorizon(profile.selectedHorizon750Seconds, profile)
            || !IsValidHorizon(profile.selectedHorizon1000Seconds, profile))
        {
            message = $"Calibration incomplete for {requested}: one or more selected horizons are invalid.";
            return false;
        }
        if (profile.referenceMaxTranslationHorizonSeconds
                < profile.referenceMinTranslationHorizonSeconds
            || profile.referenceMaxYawHorizonSeconds < profile.referenceMinYawHorizonSeconds
            || profile.referenceMaxTranslationHorizonSeconds <= 0f)
        {
            message = $"Calibration incomplete for {requested}: prediction profile shape is invalid.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(profile.calibrationCsvFileName)
            || !string.Equals(
                Path.GetFileName(profile.calibrationCsvFileName),
                profile.calibrationCsvFileName,
                StringComparison.Ordinal))
        {
            message = $"Calibration incomplete for {requested}: calibration CSV reference is invalid.";
            return false;
        }

        string csvPath = Path.Combine(
            ResolveDirectory(configuredDirectory),
            profile.calibrationCsvFileName);
        if (!File.Exists(csvPath))
        {
            message = $"Calibration incomplete for {requested}: calibration CSV is missing.";
            return false;
        }

        try
        {
            string csv = File.ReadAllText(csvPath, Encoding.UTF8);
            if (!ContainsMatchingCompletionEvent(csv, requested))
            {
                message = $"Calibration incomplete for {requested}: calibration CSV has no matching completion event.";
                return false;
            }
        }
        catch (Exception exception)
        {
            message = $"Calibration incomplete for {requested}: calibration CSV could not be verified ({exception.Message}).";
            return false;
        }

        message = "Calibration profile is complete.";
        return true;
    }

    static bool IsValidHorizon(float value, CompletedCalibrationProfile profile)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return false;
        }
        float minimum = Mathf.Min(
            profile.minimumSelectableHorizonSeconds,
            profile.maximumSelectableHorizonSeconds);
        float maximum = Mathf.Max(
            profile.minimumSelectableHorizonSeconds,
            profile.maximumSelectableHorizonSeconds);
        return value >= minimum - 1e-4f && value <= maximum + 1e-4f;
    }

    static bool ContainsMatchingCompletionEvent(string csv, string participantId)
    {
        if (string.IsNullOrEmpty(csv))
        {
            return false;
        }

        string[] lines = csv.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i < lines.Length; i++)
        {
            if (!TryReadCsvPrefix(lines[i], 4, out string[] fields))
            {
                continue;
            }
            if (string.Equals(fields[0], ProtocolVersion, StringComparison.Ordinal)
                && string.Equals(fields[2], "sequence_complete", StringComparison.Ordinal)
                && string.Equals(
                    fields[3]?.Trim(),
                    participantId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    static bool TryReadCsvPrefix(string line, int fieldCount, out string[] fields)
    {
        fields = new string[fieldCount];
        if (line == null || fieldCount <= 0)
        {
            return false;
        }

        StringBuilder value = new StringBuilder();
        bool quoted = false;
        int fieldIndex = 0;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        value.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    value.Append(c);
                }
                continue;
            }

            if (c == '"' && value.Length == 0)
            {
                quoted = true;
            }
            else if (c == ',')
            {
                fields[fieldIndex++] = value.ToString();
                value.Clear();
                if (fieldIndex == fieldCount)
                {
                    return true;
                }
            }
            else
            {
                value.Append(c);
            }
        }

        if (fieldIndex < fieldCount)
        {
            fields[fieldIndex++] = value.ToString();
        }
        return fieldIndex >= fieldCount && !quoted;
    }

    static void TryDeleteFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
        }
    }
}
