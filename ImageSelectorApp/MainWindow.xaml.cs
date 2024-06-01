using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.IO;
using Windows.Storage.Pickers;
using System.Linq;
using Windows.Storage;
using System.Text.Json;
using System.Threading.Tasks;
using System;
using System.Diagnostics;
using Windows.Foundation;

namespace ImageSelectorApp
{
    public sealed partial class MainWindow : Window
    {
        public ObservableCollection<ImageItem> Images { get; set; } = new ObservableCollection<ImageItem>();
        private const string ConfigFileName = "config.json";
        private string configFilePath;
        private string lastUsedFolder;
        private string destinationFolder;

        public MainWindow()
        {
            this.InitializeComponent();
            configFilePath = Path.Combine(ApplicationData.Current.LocalFolder.Path, ConfigFileName);
            LoadLastUsedFolderAsync();
            ImagesGridView.ItemsSource = Images;
            UpdateMoveImagesButtonState();
        }

        private async Task LoadLastUsedFolderAsync()
        {
            if (File.Exists(configFilePath))
            {
                var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(configFilePath));
                if (!string.IsNullOrEmpty(config.LastUsedFolder))
                {
                    lastUsedFolder = config.LastUsedFolder;
                    await LoadImagesFromFolderAsync(lastUsedFolder);
                }
                if (!string.IsNullOrEmpty(config.DestinationFolder))
                {
                    destinationFolder = config.DestinationFolder;
                }
            }
        }

        private async void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                lastUsedFolder = folder.Path;
                await SaveConfigAsync();
                await LoadImagesFromFolderAsync(folder.Path);
            }
        }

        private async void ChooseDestinationFolder_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                if (folder.Path == lastUsedFolder)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Erreur",
                        Content = "Le répertoire de destination ne peut pas être le même que le répertoire source. Veuillez choisir un autre répertoire.",
                        CloseButtonText = "OK",
                        XamlRoot = this.Content.XamlRoot
                    };
                    await dialog.ShowAsync();
                    return;
                }
                destinationFolder = folder.Path;
                await SaveConfigAsync();
                UpdateMoveImagesButtonState();
            }
        }

        private async void MoveImages_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(destinationFolder))
            {
                var dialog = new ContentDialog
                {
                    Title = "Erreur",
                    Content = "Veuillez d'abord choisir un répertoire de destination.",
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };
                await dialog.ShowAsync();
                return;
            }

            var selectedImages = ImagesGridView.SelectedItems.Cast<ImageItem>().ToList();
            foreach (var image in selectedImages)
            {
                // Déplacer l'image sélectionnée
                var sourceFile = await StorageFile.GetFileFromPathAsync(image.ImagePath);
                await sourceFile.MoveAsync(await StorageFolder.GetFolderFromPathAsync(destinationFolder), sourceFile.Name, NameCollisionOption.ReplaceExisting);

                // Déplacer l'image correspondante se terminant par .png ou _.png
                var matchingFileName = image.FileName.EndsWith("_.png") ? image.FileName.Replace("_.png", ".png") : image.FileName.Replace(".png", "_.png");
                var matchingItem = await (await StorageFolder.GetFolderFromPathAsync(lastUsedFolder)).TryGetItemAsync(matchingFileName).AsTask();
                if (matchingItem is StorageFile matchingFile)
                {
                    await matchingFile.MoveAsync(await StorageFolder.GetFolderFromPathAsync(destinationFolder), matchingFile.Name, NameCollisionOption.ReplaceExisting);
                }
            }

            // Rafraîchir les images après le déplacement
            await LoadImagesFromFolderAsync(lastUsedFolder);

            // Optionnel: Afficher un message de confirmation
            var dialogConfirmation = new ContentDialog
            {
                Title = "Images déplacées",
                Content = "Les images sélectionnées ont été déplacées avec succès.",
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };

            await dialogConfirmation.ShowAsync();
        }

        private async Task SaveConfigAsync()
        {
            var config = new Config { LastUsedFolder = lastUsedFolder, DestinationFolder = destinationFolder };
            var json = JsonSerializer.Serialize(config);
            await File.WriteAllTextAsync(configFilePath, json);
        }

        private async Task LoadImagesFromFolderAsync(string folderPath)
        {
            Images.Clear();
            var folder = await StorageFolder.GetFolderFromPathAsync(folderPath);
            var files = await folder.GetFilesAsync();
            foreach (var file in files.Where(f => (f.FileType == ".png" || f.FileType == ".jpg" || f.FileType == ".jpeg") && f.Name.EndsWith("_.png")))
            {
                Images.Add(new ImageItem { FileName = file.Name, ImagePath = file.Path, DateCreated = file.DateCreated, DateModified = (await file.GetBasicPropertiesAsync()).DateModified });
            }
        }

        private void ImagesGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Sélectionner automatiquement les images correspondantes
            foreach (var addedItem in e.AddedItems.Cast<ImageItem>())
            {
                SelectMatchingImage(addedItem, true);
            }

            // Désélectionner automatiquement les images correspondantes
            foreach (var removedItem in e.RemovedItems.Cast<ImageItem>())
            {
                SelectMatchingImage(removedItem, false);
            }

            UpdateMoveImagesButtonState();
        }

        private void SelectMatchingImage(ImageItem imageItem, bool select)
        {
            var matchingImageName = imageItem.FileName;
            if (imageItem.FileName.EndsWith("_.png"))
            {
                matchingImageName = imageItem.FileName.Replace("_.png", ".png");
            }
            else if (imageItem.FileName.EndsWith(".png"))
            {
                matchingImageName = imageItem.FileName.Replace(".png", "_.png");
            }

            var matchingImage = Images.FirstOrDefault(img => img.FileName == matchingImageName);
            if (matchingImage != null)
            {
                if (select && !ImagesGridView.SelectedItems.Contains(matchingImage))
                {
                    ImagesGridView.SelectedItems.Add(matchingImage);
                }
                else if (!select && ImagesGridView.SelectedItems.Contains(matchingImage))
                {
                    ImagesGridView.SelectedItems.Remove(matchingImage);
                }
            }
        }

        private void UpdateMoveImagesButtonState()
        {
            MoveImagesButton.IsEnabled = ImagesGridView.SelectedItems.Count > 0 && !string.IsNullOrEmpty(destinationFolder);
        }

        private void SelectAllImages_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;

            if (button.Content.ToString() == "Tout sélectionner")
            {
                ImagesGridView.SelectAll();
                button.Content = "Aucune sélection";
            }
            else
            {
                ImagesGridView.SelectedItems.Clear();
                button.Content = "Tout sélectionner";
            }
        }

        private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SortComboBox.SelectedItem != null)
            {
                var selectedOption = (SortComboBox.SelectedItem as ComboBoxItem).Content.ToString();
                SortImages(selectedOption);
            }
        }

        private void SortImages(string sortOption)
        {
            switch (sortOption)
            {
                case "Nom - Ascendant":
                    Images = new ObservableCollection<ImageItem>(Images.OrderBy(img => img.FileName));
                    break;
                case "Nom - Descendant":
                    Images = new ObservableCollection<ImageItem>(Images.OrderByDescending(img => img.FileName));
                    break;
                case "Date - Ascendant":
                    Images = new ObservableCollection<ImageItem>(Images.OrderBy(img => img.DateCreated));
                    break;
                case "Date - Descendant":
                    Images = new ObservableCollection<ImageItem>(Images.OrderByDescending(img => img.DateCreated));
                    break;
                case "Date de modification - Ascendant":
                    Images = new ObservableCollection<ImageItem>(Images.OrderBy(img => img.DateModified));
                    break;
                case "Date de modification - Descendant":
                    Images = new ObservableCollection<ImageItem>(Images.OrderByDescending(img => img.DateModified));
                    break;
            }
            ImagesGridView.ItemsSource = Images;
        }
    }

    public class ImageItem
    {
        public string FileName { get; set; }
        public string ImagePath { get; set; }
        public DateTimeOffset DateCreated { get; set; }
        public DateTimeOffset DateModified { get; set; }
    }

    public class Config
    {
        public string LastUsedFolder { get; set; }
        public string DestinationFolder { get; set; }
    }
}
