using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp.Formats.Png;
using ISImage = SixLabors.ImageSharp.Image;

namespace MinecraftControlHub.UI.Helpers;

/// <summary>
/// Attached property that loads a remote image URL into an <see cref="Image"/> asynchronously.
/// Modrinth serves icons/gallery images as WebP, which WPF cannot decode natively, so the bytes
/// are decoded with ImageSharp and re-encoded to PNG before being handed to WPF.
/// </summary>
public static class AsyncImageLoader
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly ConcurrentDictionary<string, BitmapImage> Cache = new();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "MinecraftControlHub/1.0");
        return client;
    }

    public static readonly DependencyProperty SourceUrlProperty =
        DependencyProperty.RegisterAttached(
            "SourceUrl",
            typeof(string),
            typeof(AsyncImageLoader),
            new PropertyMetadata(null, OnSourceUrlChanged));

    public static void SetSourceUrl(DependencyObject element, string? value) =>
        element.SetValue(SourceUrlProperty, value);

    public static string? GetSourceUrl(DependencyObject element) =>
        (string?)element.GetValue(SourceUrlProperty);

    private static async void OnSourceUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image image)
            return;

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

    private static async Task<BitmapImage?> LoadAsync(string url)
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

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = new MemoryStream(pngBytes);
        bitmap.EndInit();
        bitmap.Freeze();

        Cache[url] = bitmap;
        return bitmap;
    }
}
