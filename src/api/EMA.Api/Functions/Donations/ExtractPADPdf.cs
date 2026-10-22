using System.IO;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Docnet.Core;
using Docnet.Core.Models;
using Tesseract;

namespace EMA.Api.Functions.Donations;

public class ExtractPADPdf
{
    private readonly string _tessdataPath;

    public ExtractPADPdf()
    {
        _tessdataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
    }

    [Function("ExtractPADPdf")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "donations/pad-extract-pdf")] HttpRequestData req)
    {
        using var memoryStream = new MemoryStream();
        await req.Body.CopyToAsync(memoryStream);
        byte[] pdfBytes = memoryStream.ToArray();

        var extractedLines = ExtractOcrText(pdfBytes);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(extractedLines);
        return response;
    }

    private List<string> ExtractOcrText(byte[] pdfBytes)
    {
        var result = new List<string>();
        if (!Directory.Exists(_tessdataPath) || pdfBytes.Length == 0) return result;

        try
        {
            using var docReader = DocLib.Instance.GetDocReader(pdfBytes, new PageDimensions(3.0));
            using var engine = new TesseractEngine(_tessdataPath, "eng", EngineMode.Default);

            int pageCount = docReader.GetPageCount();

            for (int i = 0; i < pageCount; i++)
            {
                try
                {
                    using var pageReader = docReader.GetPageReader(i);
                    byte[] bgraBytes = pageReader.GetImage();
                    int width = pageReader.GetPageWidth();
                    int height = pageReader.GetPageHeight();

                    if (width > 0 && height > 0 && bgraBytes != null && bgraBytes.Length > 0)
                    {
                        byte[] bmpBytes = BgraToGrayscaleBmpBytes(bgraBytes, width, height);

                        using var pix = Pix.LoadFromMemory(bmpBytes);
                        using var ocrPage = engine.Process(pix);

                        string fullPageText = ocrPage.GetText();
                        var pageLines = ExtractHorizontalRowsFromPage(ocrPage, fullPageText);
                        result.AddRange(pageLines);
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return result;
    }

    private static List<string> ExtractHorizontalRowsFromPage(Page page, string fullPageText)
    {
        var words = new List<WordBoxInfo>();

        using (var iter = page.GetIterator())
        {
            iter.Begin();
            do
            {
                if (iter.TryGetBoundingBox(PageIteratorLevel.Word, out var rect))
                {
                    string text = iter.GetText(PageIteratorLevel.Word);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        words.Add(new WordBoxInfo { Text = text.Trim(), Y = rect.Y1, X = rect.X1 });
                    }
                }
            } while (iter.Next(PageIteratorLevel.Word));
        }

        if (!words.Any())
        {
            return fullPageText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                               .Where(l => !string.IsNullOrWhiteSpace(l))
                               .ToList();
        }

        var lineGroups = new List<List<WordBoxInfo>>();
        foreach (var word in words.OrderBy(w => w.Y).ThenBy(w => w.X))
        {
            var targetGroup = lineGroups.FirstOrDefault(g => Math.Abs(g.Average(w => w.Y) - word.Y) <= 35);
            if (targetGroup != null)
            {
                targetGroup.Add(word);
            }
            else
            {
                lineGroups.Add(new List<WordBoxInfo> { word });
            }
        }

        return lineGroups
            .OrderBy(g => g.Average(w => w.Y))
            .Select(g => string.Join(" ", g.OrderBy(w => w.X).Select(w => w.Text)))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
    }

    private static byte[] BgraToGrayscaleBmpBytes(byte[] bgra, int width, int height)
    {
        int rowPadding = (4 - ((width * 3) % 4)) % 4;
        int rowSize = (width * 3) + rowPadding;
        int imageSize = rowSize * height;
        int fileSize = 54 + imageSize;

        byte[] bmp = new byte[fileSize];

        bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
        BitConverter.GetBytes(fileSize).CopyTo(bmp, 2);
        BitConverter.GetBytes(54).CopyTo(bmp, 10);

        BitConverter.GetBytes(40).CopyTo(bmp, 14);
        BitConverter.GetBytes(width).CopyTo(bmp, 18);
        BitConverter.GetBytes(-height).CopyTo(bmp, 22);
        BitConverter.GetBytes((short)1).CopyTo(bmp, 26);
        BitConverter.GetBytes((short)24).CopyTo(bmp, 28);

        int pixelIdx = 0;
        int bmpOffset = 54;

        for (int y = 0; y < height; y++)
        {
            int rowStart = bmpOffset + (y * rowSize);
            for (int x = 0; x < width; x++)
            {
                byte b = bgra[pixelIdx];
                byte g = bgra[pixelIdx + 1];
                byte r = bgra[pixelIdx + 2];
                byte a = bgra[pixelIdx + 3];

                float alpha = a / 255.0f;
                byte rComp = (byte)((r * alpha) + (255 * (1.0f - alpha)));
                byte gComp = (byte)((g * alpha) + (255 * (1.0f - alpha)));
                byte bComp = (byte)((b * alpha) + (255 * (1.0f - alpha)));

                byte gray = (byte)((rComp * 0.299) + (gComp * 0.587) + (bComp * 0.114));

                bmp[rowStart + (x * 3)] = gray;
                bmp[rowStart + (x * 3) + 1] = gray;
                bmp[rowStart + (x * 3) + 2] = gray;

                pixelIdx += 4;
            }
        }

        return bmp;
    }

    private class WordBoxInfo
    {
        public string Text { get; set; } = "";
        public int Y { get; set; }
        public int X { get; set; }
    }
}
