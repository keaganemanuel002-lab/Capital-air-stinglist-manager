using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StingListManager.ViewModels;

public partial class PagedViewModelBase : ViewModelBase
{
    [ObservableProperty] private int pageNumber = 1;
    [ObservableProperty] private int pageSize = 200;

    public int Skip => (PageNumber - 1) * PageSize;

    [RelayCommand]
    protected virtual void NextPage()
    {
        PageNumber++;
        LoadPage();
    }

    [RelayCommand]
    protected virtual void PrevPage()
    {
        if (PageNumber <= 1) return;
        PageNumber--;
        LoadPage();
    }

    [RelayCommand]
    protected virtual void FirstPage()
    {
        PageNumber = 1;
        LoadPage();
    }

    protected virtual void LoadPage() { }
}
