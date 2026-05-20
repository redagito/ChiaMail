using System.IO;
using System.Text;
using ChiaMail.Models;

namespace ChiaMail.Services;

public static class CsvService
{
    public const long MaxFileSizeBytes = 50 * 1024 * 1024;
    public const int MaxRecipients = 10000;

    public static (List<Recipient> Recipients, string[] Headers, List<string[]> RawRows, List<string> Errors) Parse(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath), "File path cannot be empty.");

        if (!File.Exists(filePath))
            throw new FileNotFoundException("CSV file not found.", filePath);

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length == 0)
            throw new InvalidDataException("CSV file is empty.");

        if (fileInfo.Length > MaxFileSizeBytes)
            throw new InvalidDataException(
                $"CSV file is too large ({fileInfo.Length / (1024.0 * 1024.0):F1} MB). Maximum allowed is {MaxFileSizeBytes / 1024 / 1024} MB.");

        var lines = ReadAllLines(filePath);

        if (lines.Count < 2)
            throw new InvalidDataException(
                $"CSV must contain a header row and at least one data row. Found only {lines.Count} line(s).");

        var headerLine = lines[0].TrimStart('\uFEFF');
        if (string.IsNullOrWhiteSpace(headerLine))
            throw new InvalidDataException("CSV header row is empty.");

        var headers = ParseCsvLine(headerLine);
        if (headers.Length == 0)
            throw new InvalidDataException("CSV header row could not be parsed.");

        var duplicateHeaders = headers
            .GroupBy(h => h, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateHeaders.Count > 0)
            throw new InvalidDataException($"Duplicate column headers: {string.Join(", ", duplicateHeaders)}.");

        var emailIdx = FindColumnIndex(headers, "email");
        var firstNameIdx = FindColumnIndex(headers, "firstname", "first_name", "first name");
        var lastNameIdx = FindColumnIndex(headers, "lastname", "last_name", "last name");

        if (emailIdx < 0)
            throw new InvalidDataException($"CSV must contain an 'Email' column. Found: [{string.Join(", ", headers)}].");

        var recipients = new List<Recipient>(lines.Count - 1);
        var rawRows = new List<string[]>(lines.Count - 1);
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        for (int i = 1; i < lines.Count; i++)
        {
            var currentLine = lines[i];
            if (string.IsNullOrWhiteSpace(currentLine))
                continue;

            var fields = ParseCsvLine(currentLine);
            if (fields.Length == 0 || fields.All(string.IsNullOrWhiteSpace))
                continue;

            rawRows.Add(fields);

            var email = emailIdx < fields.Length ? fields[emailIdx].Trim() : string.Empty;

            if (string.IsNullOrWhiteSpace(email))
            {
                errors.Add($"Row {i + 1}: Email is empty. Row skipped.");
                continue;
            }

            if (!IsValidEmail(email))
            {
                errors.Add($"Row {i + 1}: '{Truncate(email, 80)}' is not a valid email. Row skipped.");
                continue;
            }

            if (!seenEmails.Add(email))
            {
                errors.Add($"Row {i + 1}: Duplicate email '{email}'. Row skipped.");
                continue;
            }

            recipients.Add(new Recipient
            {
                Email = email,
                FirstName = firstNameIdx >= 0 && firstNameIdx < fields.Length ? fields[firstNameIdx].Trim() : string.Empty,
                LastName = lastNameIdx >= 0 && lastNameIdx < fields.Length ? fields[lastNameIdx].Trim() : string.Empty
            });
        }

        if (recipients.Count == 0)
        {
            var summary = string.Join(Environment.NewLine, errors.Take(10));
            if (errors.Count > 10)
                summary += $"{Environment.NewLine}... and {errors.Count - 10} more error(s).";
            throw new InvalidDataException($"No valid recipients found.{Environment.NewLine}{summary}");
        }

        if (recipients.Count > MaxRecipients)
            throw new InvalidDataException(
                $"CSV contains {recipients.Count} recipients. Maximum allowed is {MaxRecipients}.");

        return (recipients, headers, rawRows, errors);
    }

    private static List<string> ReadAllLines(string filePath)
    {
        var lines = new List<string>();
        using var reader = new StreamReader(filePath, Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) != null)
            lines.Add(line);
        return lines;
    }

    private static int FindColumnIndex(string[] headers, params string[] candidates)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            var trimmed = headers[i].Trim();
            foreach (var candidate in candidates)
            {
                if (string.Equals(trimmed, candidate, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }
        return -1;
    }

    public static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        if (email.Length > 254)
            return false;

        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    internal static string[] ParseCsvLine(string line)
    {
        if (line is null)
            throw new ArgumentNullException(nameof(line));

        if (line.Length == 0)
            return [];

        var fields = new List<string>();
        var current = new StringBuilder(capacity: 256);
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString().Trim());
        return fields.ToArray();
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";
}
