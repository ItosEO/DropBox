using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
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

            // 监听集合变化以更新标题
            Items.CollectionChanged += Items_CollectionChanged;

            // 设置窗口大小
            SetWindowSize(700, 550);
            
            // 扩展内容到标题栏
            ExtendsContentIntoTitleBar = true;
            // 设置标题栏拖动区域
            SetTitleBar(DragRegion);
            
            ConfigureWindow();

            // 监听窗口激活状态，实现永不失焦
            this.Activated += AllItemsWindow_Activated;

            // 初始化标题
            UpdateTitle();
        }

        private void Items_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            UpdateTitle();
        }

        private void UpdateTitle()
        {
            var count = Items.Count;
            TitleTextBlock.Text = $"{count} {(count == 1 ? "item" : "items")}";
        }

        private void AllItemsWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            // 当窗口失去焦点时（Deactivated），立即重新激活
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                this.Activate();
            }
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
            var hWnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            // 使用 Presenter 来设置窗口样式
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                // 禁用最小化和最大化按钮
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                // 设置窗口始终置顶
                presenter.IsAlwaysOnTop = true;
            }

            // 配置标题栏外观
            if (appWindow.TitleBar != null)
            {
                appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
                appWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                appWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                appWindow.TitleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 232, 17, 35);
                appWindow.TitleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 197, 15, 31);
            }
        }

        private void ListView_DragItemsStarting(object sender, DragItemsStartingEventArgs args)
        {
            if (args.Items.Count > 0 && args.Items[0] is DropItem item)
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
                            args.Data.SetStorageItems(new List<IStorageItem>());
                            args.Data.SetBitmap(RandomAccessStreamReference.CreateFromStream(item.BitmapStream));
                        }
                        break;
                }

                args.Data.RequestedOperation = DataPackageOperation.Copy;
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

        private void ListView_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // 获取点击的原始元素
            var clickedElement = e.OriginalSource as FrameworkElement;
            
            // 向上遍历可视化树，检查是否点击在 ListViewItem 上
            bool isListViewItem = false;
            var element = clickedElement;
            while (element != null && element != ItemsListView)
            {
                if (element is ListViewItem)
                {
                    isListViewItem = true;
                    break;
                }
                element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element) as FrameworkElement;
            }
            
            // 如果点击的是 ListView 的空白区域，取消选择
            if (!isListViewItem)
            {
                ItemsListView.SelectedItem = null;
            }
        }

        private void ContextMenu_Opening(object sender, object e)
        {
            if (sender is MenuFlyout menuFlyout && menuFlyout.Target is FrameworkElement element)
            {
                // 获取右键点击的项目
                if (element.DataContext is DropItem item)
                {
                    // 选中该项目
                    ItemsListView.SelectedItem = item;
                }
            }
        }

        private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.Tag is DropItem item)
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
                    StorageFile = file,
                    SizeText = size
                };

                // 尝试加载缩略图
                await LoadThumbnailAsync(dropItem, file);

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
                    StorageFolder = folder,
                    SizeText = string.Empty
                };

                // 尝试加载文件夹缩略图
                await LoadFolderThumbnailAsync(dropItem, folder);

                Items.Add(dropItem);
            }
        }

        private async Task LoadThumbnailAsync(DropItem dropItem, StorageFile file)
        {
            try
            {
                // 尝试获取文件缩略图（对于图片/视频等）
                var thumbnail = await file.GetThumbnailAsync(
                    Windows.Storage.FileProperties.ThumbnailMode.SingleItem,
                    48,
                    Windows.Storage.FileProperties.ThumbnailOptions.UseCurrentScale);

                if (thumbnail != null && thumbnail.Size > 0)
                {
                    var bitmapImage = new BitmapImage();
                    await bitmapImage.SetSourceAsync(thumbnail);
                    dropItem.Thumbnail = bitmapImage;
                    return;
                }
            }
            catch
            {
                // 继续尝试获取图标
            }

            // 如果缩略图获取失败，尝试获取文件类型图标
            try
            {
                var thumbnail = await file.GetThumbnailAsync(
                    Windows.Storage.FileProperties.ThumbnailMode.DocumentsView,
                    48,
                    Windows.Storage.FileProperties.ThumbnailOptions.UseCurrentScale);

                if (thumbnail != null && thumbnail.Size > 0)
                {
                    var bitmapImage = new BitmapImage();
                    await bitmapImage.SetSourceAsync(thumbnail);
                    dropItem.Thumbnail = bitmapImage;
                }
            }
            catch
            {
                // 如果都失败，保持使用字体图标
            }
        }

        private async Task LoadFolderThumbnailAsync(DropItem dropItem, StorageFolder folder)
        {
            try
            {
                var thumbnail = await folder.GetThumbnailAsync(
                    Windows.Storage.FileProperties.ThumbnailMode.SingleItem,
                    48,
                    Windows.Storage.FileProperties.ThumbnailOptions.UseCurrentScale);

                if (thumbnail != null && thumbnail.Size > 0)
                {
                    var bitmapImage = new BitmapImage();
                    await bitmapImage.SetSourceAsync(thumbnail);
                    dropItem.Thumbnail = bitmapImage;
                }
            }
            catch
            {
                // 如果加载失败，保持使用字体图标
            }
        }

        private void AddTextItem(string text)
        {
            var preview = text.Length > 50 ? text.Substring(0, 50) + "..." : text;
            var size = System.Text.Encoding.UTF8.GetByteCount(text);
            
            var dropItem = new DropItem
            {
                Name = preview,
                Description = preview,
                IconGlyph = "\uE8A5",
                Type = DropItemType.Text,
                TextContent = text,
                SizeText = FormatFileSize((ulong)size)
            };

            Items.Add(dropItem);
        }

        private async Task AddBitmapItem(RandomAccessStreamReference bitmapRef)
        {
            using var stream = await bitmapRef.OpenReadAsync();
            var clonedStream = stream.CloneStream();
            var size = FormatFileSize(stream.Size);
            
            var dropItem = new DropItem
            {
                Name = "图片",
                Description = $"位图 · {size}",
                IconGlyph = "\uEB9F",
                Type = DropItemType.Bitmap,
                BitmapStream = clonedStream,
                SizeText = size
            };

            // 为位图创建缩略图
            try
            {
                var bitmapImage = new BitmapImage();
                await bitmapImage.SetSourceAsync(clonedStream);
                dropItem.Thumbnail = bitmapImage;
            }
            catch
            {
                // 如果加载失败，保持使用图标
            }

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

    public class NullToVisibilityConverter : IValueConverter
    {
        public bool Inverted { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isNull = value == null;
            bool isVisible = Inverted ? isNull : !isNull;
            return isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
