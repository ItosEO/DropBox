using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;

namespace DropBox
{
    public sealed partial class AllItemsWindow : Window
    {
        public ObservableCollection<DropItem> Items { get; set; }

        public AllItemsWindow()
        {
            this.InitializeComponent();
            Items = new ObservableCollection<DropItem>();
            ItemsListView.ItemsSource = Items;

            // 设置窗口大小
            SetWindowSize(500, 600);
            ConfigureWindow();
        }

        private void SetWindowSize(int width, int height)
        {
            var hWnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            appWindow.Resize(new Windows.Graphics.SizeInt32 { Width = width, Height = height });
        }

        private void ConfigureWindow()
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(DragRegion);

            var hWnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
            }

            if (appWindow.TitleBar != null)
            {
                appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
                appWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                appWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                
                // 隐藏系统标题栏按钮
                appWindow.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async void Item_DragStarting(UIElement sender, DragStartingEventArgs args)
        {
            if (sender is Grid grid && grid.DataContext is DropItem item)
            {
                var deferral = args.GetDeferral();

                try
                {
                    switch (item.Type)
                    {
                        case DropItemType.File:
                            if (item.StorageFile != null)
                            {
                                args.Data.SetStorageItems(new System.Collections.Generic.List<IStorageItem> { item.StorageFile });
                            }
                            break;

                        case DropItemType.Folder:
                            if (item.StorageFolder != null)
                            {
                                args.Data.SetStorageItems(new System.Collections.Generic.List<IStorageItem> { item.StorageFolder });
                            }
                            break;

                        case DropItemType.Text:
                            args.Data.SetText(item.TextContent);
                            break;

                        case DropItemType.Bitmap:
                            if (item.BitmapStream != null)
                            {
                                var tempFile = await CreateTempFileFromBitmap(item.BitmapStream);
                                args.Data.SetStorageItems(new System.Collections.Generic.List<IStorageItem> { tempFile });
                                args.Data.SetBitmap(RandomAccessStreamReference.CreateFromStream(item.BitmapStream));
                            }
                            break;
                    }

                    args.Data.RequestedOperation = DataPackageOperation.Copy;
                }
                finally
                {
                    deferral.Complete();
                }
            }
        }

        private async Task<StorageFile> CreateTempFileFromBitmap(IRandomAccessStream bitmapStream)
        {
            var tempFolder = ApplicationData.Current.TemporaryFolder;
            var fileName = $"temp_image_{Guid.NewGuid()}.png";
            var tempFile = await tempFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);

            using (var fileStream = await tempFile.OpenAsync(FileAccessMode.ReadWrite))
            {
                await RandomAccessStream.CopyAsync(bitmapStream, fileStream);
            }

            return tempFile;
        }

        private void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is DropItem item)
            {
                Items.Remove(item);
            }
        }

        // 拖入功能
        private void Grid_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "添加到 DropBox";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsContentVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;
        }

        private async void Grid_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (e.DataView.Contains(StandardDataFormats.StorageItems))
                {
                    var items = await e.DataView.GetStorageItemsAsync();
                    foreach (var item in items)
                    {
                        await AddStorageItem(item);
                    }
                }
                else if (e.DataView.Contains(StandardDataFormats.Text))
                {
                    var text = await e.DataView.GetTextAsync();
                    AddTextItem(text);
                }
                else if (e.DataView.Contains(StandardDataFormats.Bitmap))
                {
                    var bitmapRef = await e.DataView.GetBitmapAsync();
                    await AddBitmapItem(bitmapRef);
                }
            }
            catch
            {
                // 错误处理
            }
        }

        private async Task AddStorageItem(IStorageItem item)
        {
            if (item is StorageFile file)
            {
                var properties = await file.GetBasicPropertiesAsync();
                var size = FormatFileSize(properties.Size);

                var dropItem = new DropItem
                {
                    Name = file.Name,
                    Description = $"{file.FileType.ToUpper()} · {size}",
                    IconGlyph = GetFileIconGlyph(file.FileType),
                    Type = DropItemType.File,
                    FilePath = file.Path,
                    StorageFile = file
                };

                Items.Add(dropItem);
            }
            else if (item is StorageFolder folder)
            {
                var dropItem = new DropItem
                {
                    Name = folder.Name,
                    Description = "文件夹",
                    IconGlyph = "\uE8B7",
                    Type = DropItemType.Folder,
                    FilePath = folder.Path,
                    StorageFolder = folder
                };

                Items.Add(dropItem);
            }
        }

        private void AddTextItem(string text)
        {
            var preview = text.Length > 50 ? text.Substring(0, 50) + "..." : text;
            var dropItem = new DropItem
            {
                Name = "文本内容",
                Description = preview,
                IconGlyph = "\uE8A5",
                Type = DropItemType.Text,
                TextContent = text
            };

            Items.Add(dropItem);
        }

        private async Task AddBitmapItem(RandomAccessStreamReference bitmapRef)
        {
            using var stream = await bitmapRef.OpenReadAsync();
            var dropItem = new DropItem
            {
                Name = "图片",
                Description = $"位图 · {FormatFileSize(stream.Size)}",
                IconGlyph = "\uEB9F",
                Type = DropItemType.Bitmap,
                BitmapStream = stream.CloneStream()
            };

            Items.Add(dropItem);
        }

        private string GetFileIconGlyph(string fileType)
        {
            return fileType.ToLower() switch
            {
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".svg" or ".webp" => "\uEB9F", // 图片
                ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" => "\uE8B2", // 视频
                ".mp3" or ".wav" or ".flac" or ".aac" or ".wma" => "\uEC4F", // 音频
                ".pdf" => "\uE8A5", // PDF
                ".doc" or ".docx" => "\uE8A5", // Word
                ".xls" or ".xlsx" => "\uE8A5", // Excel
                ".ppt" or ".pptx" => "\uE8A5", // PowerPoint
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "\uE8B7", // 压缩包
                ".exe" or ".msi" => "\uE8AB", // 可执行文件
                ".txt" or ".log" => "\uE8A5", // 文本
                ".cs" or ".cpp" or ".h" or ".py" or ".js" or ".html" or ".css" or ".json" or ".xml" => "\uE943", // 代码
                _ => "\uE8A5" // 默认文档图标
            };
        }

        private string FormatFileSize(ulong bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }
    }
}
