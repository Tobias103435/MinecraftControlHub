using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SixLabors.ImageSharp.Formats.Png;
using ISImage = SixLabors.ImageSharp.Image;

namespace MinecraftControlHub.UI.Helpers;

/// <summary>
/// Attached property that loads a remote image URL into an <see cref="Image"/> asynchronously.
/// Modrinth serves icons/gallery images as WebP, which Avalonia's Bitmap cannot decode on all
/// platforms, so the bytes are decoded with ImageSharp and re-encoded to PNG before being handed
/// to Avalonia.
/// </summary>
public static class AsyncImageLoader
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly ConcurrentDictionary<string, Bitmap> Cache = new();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "MinecraftControlHub/1.0");
        return client;
    }

    public static readonly AttachedProperty<string?> SourceUrlProperty =
        AvaloniaProperty.RegisterAttached<Image, Image, string?>("SourceUrl");

    static AsyncImageLoader()
    {
        SourceUrlProperty.Changed.AddClassHandler<Image>(OnSourceUrlChanged);
    }

    public static void SetSourceUrl(Image element, string? value) =>
        element.SetValue(SourceUrlProperty, value);

    public static string? GetSourceUrl(Image element) =>
        element.GetValue(SourceUrlProperty);

    private static async void OnSourceUrlChanged(Image image, AvaloniaPropertyChangedEventArgs e)
    {
        image.Source = null;
        var url = e.NewValue as string;
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            var bitmap = await LoadAsync(url);
            // Ensure the URL is still the one we were asked to load (avoids flicker on recycled containers).
            if (bitmap != null && GetSourceUrl(image) == url)
                image.Source = bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Image load error for {url}: {ex.Message}");
        }
    }

    private static async Task<Bitmap?> LoadAsync(string url)
    {
        if (Cache.TryGetValue(url, out var cached))
            return cached;

        var bytes = await Http.GetByteArrayAsync(url);

        byte[] pngBytes;
        using (var input = new MemoryStream(bytes))
        using (var img = await ISImage.LoadAsync(input))
        using (var output = new MemoryStream())
        {
            await img.SaveAsync(output, new PngEncoder());
            pngBytes = output.ToArray();
        }

        using var pngStream = new MemoryStream(pngBytes);
        var bitmap = new Bitmap(pngStream);

        Cache[url] = bitmap;
        return bitmap;
    }
}
