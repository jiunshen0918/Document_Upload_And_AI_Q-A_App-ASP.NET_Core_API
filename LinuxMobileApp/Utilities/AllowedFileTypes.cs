using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using ClosedXML.Excel;
using CsvHelper;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace LinuxMobileApp.Utilities;

public static class AllowedFileTypes
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".pdf", ".docx", ".csv", ".xlsx", ".json", ".xml", ".jpg", ".png"
    };

    public static bool IsAllowed(string fileName) => Extensions.Contains(Path.GetExtension(fileName));

    public static Task<string> ExtractTextAsync(string extension, byte[] bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(bytes, writable: false);
        var ext = extension.ToLowerInvariant();
        return Task.FromResult(ext switch
        {
            ".txt" or ".md" => DecodeUtf8(bytes),
            ".json" => ValidateJson(bytes),
            ".xml" => ValidateXml(stream),
            ".csv" => ValidateCsv(bytes),
            ".pdf" => ExtractPdf(stream),
            ".docx" => ExtractDocx(stream),
            ".xlsx" => ExtractXlsx(stream),
            ".jpg" => ValidateJpeg(bytes),
            ".png" => ValidatePng(bytes),
            _ => throw new InvalidDataException("This file type is not supported.")
        });
    }

    private static string DecodeUtf8(byte[] bytes) => new UTF8Encoding(false, true).GetString(bytes);

    private static string ValidateJson(byte[] bytes)
    {
        using var json = JsonDocument.Parse(bytes);
        return JsonSerializer.Serialize(json.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string ValidateXml(Stream stream)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(stream, settings);
        var output = new StringBuilder();
        while (reader.Read())
        {
            if (reader.NodeType is XmlNodeType.Element or XmlNodeType.EndElement)
                output.Append('<').Append(reader.NodeType == XmlNodeType.EndElement ? "/" : "")
                    .Append(reader.Name).AppendLine(">");
            else if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
                output.AppendLine(reader.Value);
        }
        return output.ToString();
    }

    private static string ValidateCsv(byte[] bytes)
    {
        var text = DecodeUtf8(bytes);
        using var reader = new StringReader(text);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        while (csv.Read()) _ = csv.Parser.Record;
        return text;
    }

    private static string ExtractPdf(Stream stream)
    {
        using var document = PdfDocument.Open(stream);
        return string.Join("\n", document.GetPages().Select(page => page.Text));
    }

    private static string ExtractDocx(Stream stream)
    {
        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart?.Document?.Body
            ?? throw new InvalidDataException("The Word document has no readable document body.");
        return string.Join("\n", body.Descendants<Paragraph>()
            .Select(paragraph => string.Concat(paragraph.Descendants<Text>().Select(text => text.Text)))
            .Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static string ExtractXlsx(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var output = new StringBuilder();
        foreach (var sheet in workbook.Worksheets)
        {
            output.AppendLine($"Worksheet: {sheet.Name}");
            foreach (var row in sheet.RowsUsed())
                output.AppendLine(string.Join(" | ", row.CellsUsed().Select(cell => cell.GetFormattedString())));
        }
        return output.ToString();
    }

    private static string ValidateJpeg(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
            throw new InvalidDataException("The JPEG file is damaged or is not a valid JPEG image.");

        var position = 2;
        var hasFrame = false;
        var hasScan = false;
        while (position < bytes.Length)
        {
            if (bytes[position++] != 0xFF)
                throw new InvalidDataException("The JPEG file is damaged or is not a valid JPEG image.");
            while (position < bytes.Length && bytes[position] == 0xFF) position++;
            if (position >= bytes.Length) break;
            var marker = bytes[position++];
            if (marker == 0xD9)
            {
                if (hasFrame && hasScan) return string.Empty;
                break;
            }
            if (marker is 0x01 or >= 0xD0 and <= 0xD7) continue;
            if (position + 2 > bytes.Length) break;
            var segmentLength = (bytes[position] << 8) | bytes[position + 1];
            if (segmentLength < 2 || position + segmentLength > bytes.Length) break;
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC)) hasFrame = true;
            position += segmentLength;
            if (marker == 0xDA)
            {
                hasScan = true;
                while (position + 1 < bytes.Length)
                {
                    if (bytes[position++] != 0xFF) continue;
                    var next = bytes[position++];
                    if (next == 0x00 || next is >= 0xD0 and <= 0xD7) continue;
                    position -= 2;
                    break;
                }
            }
        }
        throw new InvalidDataException("The JPEG file is damaged or is not a valid JPEG image.");
    }

    private static string ValidatePng(byte[] bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(signature))
            throw new InvalidDataException("The PNG file is damaged or is not a valid PNG image.");

        var position = 8;
        var chunkIndex = 0;
        var hasImageData = false;
        while (position + 12 <= bytes.Length)
        {
            var dataLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position, 4));
            if (dataLength > int.MaxValue || position + 12L + dataLength > bytes.Length)
                break;
            var length = (int)dataLength;
            var type = bytes.AsSpan(position + 4, 4);
            if (chunkIndex == 0 && (!type.SequenceEqual("IHDR"u8) || length != 13)) break;
            var expectedCrc = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position + 8 + length, 4));
            if (PngCrc32(bytes.AsSpan(position + 4, length + 4)) != expectedCrc) break;
            if (type.SequenceEqual("IDAT"u8)) hasImageData = true;
            position += length + 12;
            chunkIndex++;
            if (!type.SequenceEqual("IEND"u8)) continue;
            if (length == 0 && hasImageData && position == bytes.Length) return string.Empty;
            break;
        }
        throw new InvalidDataException("The PNG file is damaged or is not a valid PNG image.");
    }

    private static uint PngCrc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
