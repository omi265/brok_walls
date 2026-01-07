using System.Windows.Media.Imaging;

namespace BroKWalls
{
    public class ImmichPhoto
    {
        public string? Id { get; set; }
        public BitmapSource? Thumbnail { get; set; }
        public byte[]? ThumbnailBytes { get; set; }
    }
}
