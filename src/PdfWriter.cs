// PdfWriter: minimal hand-rolled PDF emitter. No PDF library exists that
// fits this project's csc.exe-only, no-NuGet constraint, so this writes
// the PDF file format directly - just enough to render the SINVAUX-branded
// incident report approved in the design doc (header lockup, Grenat
// hairline, stat row, findings table, footer). Not a general-purpose PDF
// library - do not extend beyond what a report needs.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Aftermath
{
    public class PdfReportStat
    {
        public string Value = "";
        public string Label = "";
    }

    public class PdfReportRow
    {
        public string Item = "";
        public string Location = "";
        public string Verdict = "";
        public string Action = "";
    }

    public class PdfReport
    {
        public string MachineName = "";
        public DateTime GeneratedAtUtc;
        public int ScanNumber;
        public List<PdfReportStat> Stats = new List<PdfReportStat>();
        public List<PdfReportRow> Rows = new List<PdfReportRow>();
    }

    public static class PdfWriter
    {
        public static void Write(PdfReport report, string outputPath)
        {
            var content = new StringBuilder();

            // Grenat hairline under the header.
            content.Append("0.486 0.180 0.227 rg\n72 740 468 3 re f\n");

            // Header lockup.
            content.Append("BT /F1 9 Tf 72 750 Td (SINVAUX) Tj ET\n");
            content.Append("BT /F1 16 Tf 72 718 Td (Aftermath - Incident Report) Tj ET\n");
            content.Append("0 0 0 rg\n");
            string meta = Escape(report.MachineName) + "  |  Generated " +
                report.GeneratedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
                "  |  Scan #" + report.ScanNumber.ToString(CultureInfo.InvariantCulture);
            content.Append("BT /F1 9 Tf 72 700 Td (" + meta + ") Tj ET\n");

            // Stat row.
            double statX = 72;
            double statY = 660;
            double statW = 100;
            foreach (var stat in report.Stats)
            {
                content.Append("BT /F1 16 Tf " + statX.ToString(CultureInfo.InvariantCulture) + " " +
                    statY.ToString(CultureInfo.InvariantCulture) + " Td (" + Escape(stat.Value) + ") Tj ET\n");
                content.Append("BT /F1 8 Tf " + statX.ToString(CultureInfo.InvariantCulture) + " " +
                    (statY - 14).ToString(CultureInfo.InvariantCulture) + " Td (" + Escape(stat.Label) + ") Tj ET\n");
                statX += statW;
            }

            // Findings table.
            double tableTop = 610;
            content.Append("BT /F1 8 Tf 72 " + tableTop.ToString(CultureInfo.InvariantCulture) + " Td (ITEM) Tj ET\n");
            content.Append("BT /F1 8 Tf 220 " + tableTop.ToString(CultureInfo.InvariantCulture) + " Td (LOCATION) Tj ET\n");
            content.Append("BT /F1 8 Tf 380 " + tableTop.ToString(CultureInfo.InvariantCulture) + " Td (VERDICT) Tj ET\n");
            content.Append("BT /F1 8 Tf 460 " + tableTop.ToString(CultureInfo.InvariantCulture) + " Td (ACTION) Tj ET\n");

            double rowY = tableTop - 16;
            foreach (var row in report.Rows)
            {
                content.Append("BT /F1 9 Tf 72 " + rowY.ToString(CultureInfo.InvariantCulture) + " Td (" + Escape(row.Item) + ") Tj ET\n");
                content.Append("BT /F1 9 Tf 220 " + rowY.ToString(CultureInfo.InvariantCulture) + " Td (" + Escape(row.Location) + ") Tj ET\n");
                content.Append("BT /F1 9 Tf 380 " + rowY.ToString(CultureInfo.InvariantCulture) + " Td (" + Escape(row.Verdict) + ") Tj ET\n");
                content.Append("BT /F1 9 Tf 460 " + rowY.ToString(CultureInfo.InvariantCulture) + " Td (" + Escape(row.Action) + ") Tj ET\n");
                rowY -= 16;
            }

            // Footer.
            content.Append("BT /F1 7 Tf 72 40 Td (SINVAUX Aftermath - no detection engine, reports on Defender's findings) Tj ET\n");

            WritePdfFile(content.ToString(), outputPath);
        }

        private static void WritePdfFile(string pageContent, string outputPath)
        {
            var objects = new List<byte[]>();
            objects.Add(Ascii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n"));
            objects.Add(Ascii("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n"));
            objects.Add(Ascii("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
                "/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>\nendobj\n"));

            byte[] contentBytes = Ascii(pageContent);
            objects.Add(Ascii("4 0 obj\n<< /Length " + contentBytes.Length + " >>\nstream\n" +
                pageContent + "endstream\nendobj\n"));

            objects.Add(Ascii("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n"));

            using (var ms = new MemoryStream())
            {
                byte[] header = Ascii("%PDF-1.4\n");
                ms.Write(header, 0, header.Length);

                long[] offsets = new long[objects.Count + 1];
                for (int i = 0; i < objects.Count; i++)
                {
                    offsets[i + 1] = ms.Position;
                    ms.Write(objects[i], 0, objects[i].Length);
                }

                long xrefStart = ms.Position;
                var xref = new StringBuilder();
                xref.Append("xref\n0 " + (objects.Count + 1) + "\n");
                xref.Append("0000000000 65535 f \n");
                for (int i = 1; i <= objects.Count; i++)
                    xref.Append(offsets[i].ToString("D10") + " 00000 n \n");
                byte[] xrefBytes = Ascii(xref.ToString());
                ms.Write(xrefBytes, 0, xrefBytes.Length);

                string trailer = "trailer\n<< /Size " + (objects.Count + 1) + " /Root 1 0 R >>\n" +
                    "startxref\n" + xrefStart + "\n%%EOF";
                byte[] trailerBytes = Ascii(trailer);
                ms.Write(trailerBytes, 0, trailerBytes.Length);

                File.WriteAllBytes(outputPath, ms.ToArray());
            }
        }

        private static byte[] Ascii(string s)
        {
            return Encoding.GetEncoding("ISO-8859-1").GetBytes(s);
        }

        // Escapes PDF string-literal special characters. Non-Latin1 characters
        // are stripped since the base Helvetica font here only covers Latin1 -
        // acceptable for this report's content (file paths, verdicts, machine
        // names), matching what the mockup showed.
        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '\\') sb.Append("\\\\");
                else if (c == '(') sb.Append("\\(");
                else if (c == ')') sb.Append("\\)");
                else if (c < 32 || c > 255) continue;
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
