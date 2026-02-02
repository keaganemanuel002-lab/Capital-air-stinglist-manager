using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Models;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class ProductCatalogViewModel : ViewModelBase
{
    private readonly ProductCatalogService _catalogService;

    public ObservableCollection<ProductCatalogItem> Products { get; } = new();

    [ObservableProperty]
    private ProductCatalogItem? selectedProduct;

    public ProductCatalogViewModel(AppState appState)
    {
        _catalogService = new ProductCatalogService(appState.Settings);
        foreach (var product in _catalogService.LoadCatalog())
        {
            Products.Add(product);
        }
    }

    [RelayCommand]
    private void Add()
    {
        var item = new ProductCatalogItem
        {
            Code = "NEW-PRODUCT",
            Name = "New Product",
            BasePriceExVat = 0m,
            Description = "",
            IsVatExempt = false
        };
        Products.Add(item);
        SelectedProduct = item;
    }

    [RelayCommand]
    private void Remove()
    {
        if (SelectedProduct is null) return;
        Products.Remove(SelectedProduct);
        SelectedProduct = Products.FirstOrDefault();
    }

    [RelayCommand]
    private void Save()
    {
        _catalogService.SaveCatalog(Products.ToList());
    }
}
