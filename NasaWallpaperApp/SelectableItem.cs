using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace NasaWallpaperApp
{
    public class SelectableItem : INotifyPropertyChanged
    {
        public string Id { get; set; } = "";
        
        private string _name = "";
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }
        
        public string? AssetId { get; set; }
        public string? ThumbnailUrl { get; set; } 

        private ImageSource? _thumbnail;
        public ImageSource? Thumbnail
        {
            get => _thumbnail;
            set
            {
                _thumbnail = value;
                OnPropertyChanged();
            }
        }

        public override string ToString() => Name;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}