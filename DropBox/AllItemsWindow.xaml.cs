using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        private Action<DropItem> onItemDeleted;

        public AllItemsWindow(ObservableCollection<DropItem> items, Action<DropItem> onItemDeleted)
        {
            this.InitializeComponent();
            this.Items = items;
            this.onItemDeleted = onItemDeleted;
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
            }
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
                onItemDeleted?.Invoke(item);
            }
        }
    }
}
